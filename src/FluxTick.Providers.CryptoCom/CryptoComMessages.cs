using System.Text.Json;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;

namespace FluxTick.Providers.CryptoCom;

/// <summary>
/// Pure functions that build and parse Crypto.com Exchange v1 WebSocket and REST payloads.
/// </summary>
internal static class CryptoComMessages
{
    /// <summary>
    /// The prefix of a ticker channel name.
    /// </summary>
    private const string ChannelPrefix = "ticker.";

    /// <summary>
    /// The most channels sent in one subscribe request.
    /// </summary>
    public const int MaxChannelsPerMessage = 50;

    /// <summary>
    /// Builds subscribe or unsubscribe requests for the ticker channels, split to a safe size.
    /// </summary>
    /// <param name="operation">
    /// <c>subscribe</c> or <c>unsubscribe</c>.
    /// </param>
    /// <param name="symbols">
    /// The instruments, for example <c>BTC_USDT</c>.
    /// </param>
    /// <returns>
    /// The JSON texts to send.
    /// </returns>
    public static IEnumerable<string> ControlMessages(string operation, IEnumerable<string> symbols) =>
        symbols
            .Chunk(MaxChannelsPerMessage)
            .Select(chunk => JsonSerializer.Serialize(new
            {
                id = 1,
                method = operation,
                @params = new { channels = chunk.Select(s => ChannelPrefix + s).ToArray() },
            }));

    /// <summary>
    /// Answers a server heartbeat. The server closes a connection that does not echo the heartbeat id within seconds.
    /// </summary>
    /// <param name="json">
    /// The received text.
    /// </param>
    /// <returns>
    /// The reply to send, or <see langword="null"/> when the message is not a heartbeat.
    /// </returns>
    public static string? HeartbeatReply(string json)
    {
        if (!json.Contains("public/heartbeat", StringComparison.Ordinal))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("method", out var method) || method.GetString() != "public/heartbeat"
            || !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return $$"""{"id":{{id.GetRawText()}},"method":"public/respond-heartbeat"}""";
    }

    /// <summary>
    /// Parses a ticker push. The price 24 hours ago is derived from the last price and the change as a fraction.
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
    /// <see langword="true"/> when the message was a ticker with a price.
    /// </returns>
    public static bool TryParseTicker(string json, string providerId, DateTimeOffset fallbackTime, out Quote quote)
    {
        quote = null!;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("channel", out var channel) || channel.GetString() != "ticker"
            || !result.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
        {
            return false;
        }

        var item = data[0];
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("i", out var nameElement) || nameElement.GetString() is not { Length: > 0 } symbol
            || !JsonValues.TryGetDecimal(item, "a", out var price))
        {
            return false;
        }

        decimal? reference = null;
        if (JsonValues.TryGetDecimal(item, "c", out var change) && change > -1m)
        {
            reference = price / (1m + change);
        }

        DateTimeOffset? traded = item.TryGetProperty("t", out var t) && JsonValues.TryGetUnixMilliseconds(t, out var parsed)
            ? parsed
            : null;
        quote = CryptoQuote.Create(new AssetId(providerId, symbol), price, reference, traded ?? fallbackTime, traded is null);
        return true;
    }

    /// <summary>
    /// Reads the error of a failed reply.
    /// </summary>
    /// <param name="json">
    /// The received text.
    /// </param>
    /// <returns>
    /// The error text, or <see langword="null"/> when the message is not a failed reply.
    /// </returns>
    public static string? TryParseError(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.Number
            || !code.TryGetInt32(out var value) || value == 0)
        {
            return null;
        }

        var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
        return $"{message ?? "Request failed"} (code {value})";
    }

    /// <summary>
    /// Parses a candlestick response into close-price samples.
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
            if (candle.ValueKind == JsonValueKind.Object
                && candle.TryGetProperty("t", out var t) && JsonValues.TryGetUnixMilliseconds(t, out var time)
                && JsonValues.TryGetDecimal(candle, "c", out var close))
            {
                points.Add(new PricePoint(time, close));
            }
        }

        points.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return points;
    }

    /// <summary>
    /// Parses an instrument listing into the tradable spot pairs; perpetuals and futures are left out.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The tradable <c>CCY_PAIR</c> instruments.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// The response reports an error code.
    /// </exception>
    public static IReadOnlyList<ListedSymbol> ParseInstruments(string json)
    {
        using var document = JsonDocument.Parse(json);
        var data = EnsureOk(document.RootElement);
        var symbols = new List<ListedSymbol>();
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("symbol", out var symbolElement) || symbolElement.GetString() is not { Length: > 0 } symbol
                || !item.TryGetProperty("inst_type", out var type) || type.GetString() != "CCY_PAIR")
            {
                continue;
            }

            if (item.TryGetProperty("tradable", out var tradable) && tradable.ValueKind == JsonValueKind.False)
            {
                continue;
            }

            var baseCoin = item.TryGetProperty("base_ccy", out var b) ? b.GetString() : null;
            var quoteCoin = item.TryGetProperty("quote_ccy", out var q) ? q.GetString() : null;
            var display = !string.IsNullOrEmpty(baseCoin) && !string.IsNullOrEmpty(quoteCoin) ? $"{baseCoin}/{quoteCoin}" : symbol;
            symbols.Add(new ListedSymbol(symbol, display));
        }

        return symbols;
    }

    /// <summary>
    /// Checks the response envelope and returns its <c>result.data</c> array.
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
            throw new JsonException("Unexpected Crypto.com response.");
        }

        var code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var parsed)
            ? parsed
            : (int?)null;
        if (code != 0)
        {
            var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            throw new HttpRequestException($"Crypto.com error {code ?? -1}: {message}");
        }

        if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Crypto.com response has no result data.");
        }

        return data;
    }
}
