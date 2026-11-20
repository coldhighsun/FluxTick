using System.Text.Json;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;

namespace FluxTick.Providers.Bitget;

/// <summary>
/// Pure functions that build and parse Bitget v2 WebSocket and REST payloads.
/// </summary>
internal static class BitgetMessages
{
    /// <summary>
    /// The plain-text keep-alive Bitget expects at least every 30 seconds.
    /// </summary>
    public const string Ping = "ping";

    /// <summary>
    /// The plain-text answer to a ping.
    /// </summary>
    public const string Pong = "pong";

    /// <summary>
    /// The most channels sent in one subscribe request.
    /// </summary>
    public const int MaxArgsPerMessage = 20;

    /// <summary>
    /// Builds subscribe or unsubscribe requests for the spot ticker channel, split to a safe size.
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
            .Select(chunk => JsonSerializer.Serialize(new
            {
                op = operation,
                args = chunk.Select(s => new { instType = "SPOT", channel = "ticker", instId = s }).ToArray(),
            }));

    /// <summary>
    /// Parses a spot ticker push; the reference is the price 24 hours ago.
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
        if (json.Length == 0 || json[0] != '{')
        {
            return false;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
        {
            return false;
        }

        var item = data[0];
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("instId", out var idElement) || idElement.GetString() is not { Length: > 0 } symbol
            || !JsonValues.TryGetDecimal(item, "lastPr", out var price))
        {
            return false;
        }

        decimal? reference = JsonValues.TryGetPositiveDecimal(item, "open24h", out var open) ? open : null;
        DateTimeOffset? traded = item.TryGetProperty("ts", out var ts) && JsonValues.TryGetUnixMilliseconds(ts, out var parsed)
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
    /// The error text, or <see langword="null"/> when the message is not an error event.
    /// </returns>
    public static string? TryParseError(string json)
    {
        if (json.Length == 0 || json[0] != '{')
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("event", out var kind) || kind.GetString() != "error")
        {
            return null;
        }

        return root.TryGetProperty("msg", out var message) && message.ValueKind == JsonValueKind.String
            ? message.GetString()
            : "Request failed";
    }

    /// <summary>
    /// Parses a candles response into close-price samples. Each candle is
    /// <c>[time (ms), open, high, low, close, ...]</c>, oldest first.
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
                && candle.GetArrayLength() > 4
                && JsonValues.TryGetUnixMilliseconds(candle[0], out var time)
                && JsonValues.TryGetDecimal(candle[4], out var close))
            {
                points.Add(new PricePoint(time, close));
            }
        }

        points.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return points;
    }

    /// <summary>
    /// Parses a symbols response into the tradable pairs.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The symbols whose status is <c>online</c>.
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

            if (item.TryGetProperty("status", out var status) && status.GetString() is { } text && text != "online")
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
            throw new JsonException("Unexpected Bitget response.");
        }

        var code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        if (code != "00000")
        {
            var message = root.TryGetProperty("msg", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            throw new HttpRequestException($"Bitget error {code ?? "?"}: {message}");
        }

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Bitget response has no data array.");
        }

        return data;
    }
}
