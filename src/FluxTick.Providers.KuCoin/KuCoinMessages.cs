using System.Text.Json;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;

namespace FluxTick.Providers.KuCoin;

/// <summary>
/// Pure functions that build and parse KuCoin WebSocket and REST payloads.
/// </summary>
internal static class KuCoinMessages
{
    /// <summary>
    /// The application-level keep-alive KuCoin answers with a pong; the server asks for one every 18 seconds.
    /// </summary>
    public const string Ping = """{"id":"ping","type":"ping"}""";

    /// <summary>
    /// The most symbols KuCoin accepts in one subscribe request.
    /// </summary>
    public const int MaxSymbolsPerMessage = 100;

    /// <summary>
    /// The prefix of the snapshot topic, which carries the last price and the 24 hour open in one message.
    /// </summary>
    private const string TopicPrefix = "/market/snapshot:";

    /// <summary>
    /// Builds subscribe or unsubscribe requests for the snapshot topic, one topic with many symbols per request.
    /// </summary>
    /// <param name="operation">
    /// <c>subscribe</c> or <c>unsubscribe</c>.
    /// </param>
    /// <param name="symbols">
    /// The symbols, for example <c>BTC-USDT</c>.
    /// </param>
    /// <returns>
    /// The JSON texts to send.
    /// </returns>
    public static IEnumerable<string> ControlMessages(string operation, IEnumerable<string> symbols) =>
        symbols
            .Chunk(MaxSymbolsPerMessage)
            .Select(chunk => JsonSerializer.Serialize(new
            {
                id = Guid.NewGuid().ToString("N"),
                type = operation,
                topic = TopicPrefix + string.Join(',', chunk),
                response = true,
            }));

    /// <summary>
    /// Reads the token response of the public bullet endpoint and builds the address to connect to.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <param name="fallback">
    /// The configured address, used when the response names none that is a WebSocket address.
    /// </param>
    /// <param name="connectId">
    /// A unique id of the connection, which KuCoin asks for.
    /// </param>
    /// <returns>
    /// The address with the token and the connection id in its query.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// The response is an error or carries no token.
    /// </exception>
    public static Uri BuildConnectionUri(string json, Uri fallback, string connectId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("code", out var code) || code.GetString() != "200000"
            || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("token", out var tokenElement) || tokenElement.GetString() is not { Length: > 0 } token)
        {
            var message = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("msg", out var m) ? m.GetString() : null;
            throw new HttpRequestException($"KuCoin did not hand out a WebSocket token: {message}");
        }

        var endpoint = fallback;
        if (data.TryGetProperty("instanceServers", out var servers) && servers.ValueKind == JsonValueKind.Array)
        {
            foreach (var server in servers.EnumerateArray())
            {
                if (server.TryGetProperty("endpoint", out var e)
                    && Uri.TryCreate(e.GetString(), UriKind.Absolute, out var parsed)
                    && parsed.Scheme is "wss" or "ws")
                {
                    endpoint = parsed;
                    break;
                }
            }
        }

        return new UriBuilder(endpoint) { Query = $"token={Uri.EscapeDataString(token)}&connectId={Uri.EscapeDataString(connectId)}" }.Uri;
    }

    /// <summary>
    /// Parses a market snapshot push. The reference is the price 24 hours ago, which the snapshot calls the open.
    /// </summary>
    /// <param name="json">
    /// The received text.
    /// </param>
    /// <param name="providerId">
    /// The provider id to stamp on the resulting quote.
    /// </param>
    /// <param name="fallbackTime">
    /// The timestamp used when the message carries none.
    /// </param>
    /// <param name="quote">
    /// The parsed quote.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the message was a snapshot with a price.
    /// </returns>
    public static bool TryParseSnapshot(string json, string providerId, DateTimeOffset fallbackTime, out Quote quote)
    {
        quote = null!;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("type", out var type) || type.GetString() != "message"
            || !root.TryGetProperty("topic", out var topicElement) || topicElement.GetString() is not { } topic
            || !topic.StartsWith(TopicPrefix, StringComparison.Ordinal)
            || !root.TryGetProperty("data", out var outer) || outer.ValueKind != JsonValueKind.Object
            || !outer.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !JsonValues.TryGetDecimal(data, "lastTradedPrice", out var price))
        {
            return false;
        }

        var symbol = data.TryGetProperty("symbol", out var s) && s.GetString() is { Length: > 0 } named ? named : topic[TopicPrefix.Length..];
        decimal? reference = data.TryGetProperty("marketChange24h", out var change)
            && change.ValueKind == JsonValueKind.Object
            && JsonValues.TryGetPositiveDecimal(change, "open", out var open)
                ? open
                : null;
        DateTimeOffset? traded = data.TryGetProperty("datetime", out var ts) && JsonValues.TryGetUnixMilliseconds(ts, out var parsed)
            ? parsed
            : null;
        quote = CryptoQuote.Create(new AssetId(providerId, symbol), price, reference, traded ?? fallbackTime, traded is null);
        return true;
    }

    /// <summary>
    /// Reads the error of a failed control reply.
    /// </summary>
    /// <param name="json">
    /// The received text.
    /// </param>
    /// <returns>
    /// The error text, or <see langword="null"/> when the message is not an error.
    /// </returns>
    public static string? TryParseError(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("type", out var type) || type.GetString() != "error")
        {
            return null;
        }

        return root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String
            ? data.GetString()
            : "Request failed";
    }

    /// <summary>
    /// Parses a candles response into close-price samples. Each candle is
    /// <c>[time (s), open, close, high, low, volume, turnover]</c>, newest first.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The samples, oldest first.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// The response reports an error code.
    /// </exception>
    public static IReadOnlyList<PricePoint> ParseCandles(string json)
    {
        using var document = JsonDocument.Parse(json);
        var data = EnsureOk(document.RootElement);
        var points = new List<PricePoint>();
        foreach (var candle in data.EnumerateArray())
        {
            if (candle.ValueKind == JsonValueKind.Array
                && candle.GetArrayLength() > 2
                && JsonValues.TryGetDecimal(candle[0], out var seconds)
                && JsonValues.TryGetDecimal(candle[2], out var close))
            {
                points.Add(new PricePoint(DateTimeOffset.FromUnixTimeSeconds((long)seconds), close));
            }
        }

        points.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return points;
    }

    /// <summary>
    /// Parses a symbol listing into the tradable pairs.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The pairs with trading enabled.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// The response reports an error code.
    /// </exception>
    public static IReadOnlyList<ListedSymbol> ParseSymbols(string json)
    {
        using var document = JsonDocument.Parse(json);
        var data = EnsureOk(document.RootElement);
        var symbols = new List<ListedSymbol>();
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("symbol", out var symbolElement) || symbolElement.GetString() is not { Length: > 0 } symbol)
            {
                continue;
            }

            if (item.TryGetProperty("enableTrading", out var enabled) && enabled.ValueKind == JsonValueKind.False)
            {
                continue;
            }

            var baseCoin = item.TryGetProperty("baseCurrency", out var b) ? b.GetString() : null;
            var quoteCoin = item.TryGetProperty("quoteCurrency", out var q) ? q.GetString() : null;
            var display = !string.IsNullOrEmpty(baseCoin) && !string.IsNullOrEmpty(quoteCoin) ? $"{baseCoin}/{quoteCoin}" : symbol;
            symbols.Add(new ListedSymbol(symbol, display));
        }

        return symbols;
    }

    /// <summary>
    /// Checks the response envelope and returns its <c>data</c> array.
    /// </summary>
    /// <param name="root">
    /// The response root.
    /// </param>
    /// <returns>
    /// The data array.
    /// </returns>
    private static JsonElement EnsureOk(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Unexpected KuCoin response.");
        }

        var code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        if (code != "200000")
        {
            var message = root.TryGetProperty("msg", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            throw new HttpRequestException($"KuCoin error {code ?? "?"}: {message}");
        }

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("KuCoin response has no data array.");
        }

        return data;
    }
}
