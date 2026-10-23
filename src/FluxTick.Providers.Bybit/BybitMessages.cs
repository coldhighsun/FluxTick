using System.Text.Json;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;

namespace FluxTick.Providers.Bybit;

/// <summary>
/// Pure functions that build and parse Bybit v5 WebSocket and REST payloads.
/// </summary>
internal static class BybitMessages
{
    /// <summary>
    /// The application-level keep-alive Bybit expects roughly every 20 seconds.
    /// </summary>
    public const string Ping = """{"op":"ping"}""";

    /// <summary>
    /// The most topics Bybit accepts in one spot subscribe request.
    /// </summary>
    public const int MaxArgsPerMessage = 10;

    /// <summary>
    /// The topic prefix of the spot ticker stream.
    /// </summary>
    private const string TopicPrefix = "tickers.";

    /// <summary>
    /// Builds subscribe or unsubscribe requests for the ticker topics, split to the size Bybit allows.
    /// </summary>
    /// <param name="operation">
    /// <c>subscribe</c> or <c>unsubscribe</c>.
    /// </param>
    /// <param name="symbols">
    /// The symbols, for example <c>BTCUSDT</c>.
    /// </param>
    /// <returns>
    /// The JSON texts to send.
    /// </returns>
    public static IEnumerable<string> ControlMessages(string operation, IEnumerable<string> symbols) =>
        symbols
            .Chunk(MaxArgsPerMessage)
            .Select(chunk => JsonSerializer.Serialize(new { op = operation, args = chunk.Select(s => TopicPrefix + s).ToArray() }));

    /// <summary>
    /// Parses a spot ticker push.
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
            || !root.TryGetProperty("topic", out var topic) || topic.GetString() is not { } topicName
            || !topicName.StartsWith(TopicPrefix, StringComparison.Ordinal)
            || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("symbol", out var symbolElement) || symbolElement.GetString() is not { Length: > 0 } symbol
            || !JsonValues.TryGetDecimal(data, "lastPrice", out var price))
        {
            return false;
        }

        decimal? reference = JsonValues.TryGetPositiveDecimal(data, "prevPrice24h", out var previous) ? previous : null;
        var timestamp = root.TryGetProperty("ts", out var ts) && JsonValues.TryGetUnixMilliseconds(ts, out var parsed)
            ? parsed
            : fallbackTime;
        quote = new Quote(new AssetId(providerId, symbol), price, reference, timestamp);
        return true;
    }

    /// <summary>
    /// Reads the error of a failed control reply.
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
            || !root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.False)
        {
            return null;
        }

        return root.TryGetProperty("ret_msg", out var message) && message.ValueKind == JsonValueKind.String
            ? message.GetString()
            : "Request failed";
    }

    /// <summary>
    /// Parses a kline response into close-price samples.
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
    public static IReadOnlyList<PricePoint> ParseKlines(string json)
    {
        using var document = JsonDocument.Parse(json);
        var list = EnsureOk(document.RootElement);
        var points = new List<PricePoint>();
        foreach (var kline in list.EnumerateArray())
        {
            if (kline.ValueKind == JsonValueKind.Array
                && kline.GetArrayLength() > 4
                && JsonValues.TryGetUnixMilliseconds(kline[0], out var time)
                && JsonValues.TryGetDecimal(kline[4], out var close))
            {
                points.Add(new PricePoint(time, close));
            }
        }

        // Bybit lists the newest candle first.
        points.Reverse();
        return points;
    }

    /// <summary>
    /// Parses an instruments response into the tradable pairs.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The instruments in <c>Trading</c> status.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// The response reports an error code.
    /// </exception>
    public static IReadOnlyList<ListedSymbol> ParseInstruments(string json)
    {
        using var document = JsonDocument.Parse(json);
        var list = EnsureOk(document.RootElement);
        var symbols = new List<ListedSymbol>();
        foreach (var item in list.EnumerateArray())
        {
            if (!item.TryGetProperty("symbol", out var symbolElement) || symbolElement.GetString() is not { Length: > 0 } symbol)
            {
                continue;
            }

            if (item.TryGetProperty("status", out var status) && status.GetString() is { } text && text != "Trading")
            {
                continue;
            }

            var baseCoin = item.TryGetProperty("baseCoin", out var b) ? b.GetString() : null;
            var quoteCoin = item.TryGetProperty("quoteCoin", out var q) ? q.GetString() : null;
            var display = !string.IsNullOrEmpty(baseCoin) && !string.IsNullOrEmpty(quoteCoin) ? $"{baseCoin}/{quoteCoin}" : symbol;
            symbols.Add(new ListedSymbol(symbol, display));
        }

        return symbols;
    }

    /// <summary>
    /// Checks the response envelope and returns its <c>result.list</c> array.
    /// </summary>
    /// <param name="root">
    /// The response root.
    /// </param>
    /// <returns>
    /// The list array.
    /// </returns>
    private static JsonElement EnsureOk(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Unexpected Bybit response.");
        }

        var returnCode = root.TryGetProperty("retCode", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var parsedCode)
            ? parsedCode
            : (int?)null;
        if (returnCode != 0)
        {
            var message = root.TryGetProperty("retMsg", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            throw new HttpRequestException($"Bybit error {returnCode ?? -1}: {message}");
        }

        if (!root.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("list", out var list)
            || list.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Bybit response has no result list.");
        }

        return list;
    }
}
