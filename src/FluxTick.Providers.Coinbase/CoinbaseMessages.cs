using System.Text.Json;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;

namespace FluxTick.Providers.Coinbase;

/// <summary>
/// Pure functions that build and parse Coinbase Exchange WebSocket and REST payloads.
/// </summary>
internal static class CoinbaseMessages
{
    /// <summary>
    /// The most products sent in one subscribe request.
    /// </summary>
    public const int MaxProductsPerMessage = 100;

    /// <summary>
    /// The number of one-minute candles requested for a sparkline.
    /// </summary>
    public const int HistoryLength = 60;

    /// <summary>
    /// Builds subscribe or unsubscribe requests for the <c>ticker</c> channel, split to a safe size.
    /// </summary>
    /// <param name="type">
    /// <c>subscribe</c> or <c>unsubscribe</c>.
    /// </param>
    /// <param name="symbols">
    /// The product ids, for example <c>BTC-USD</c>.
    /// </param>
    /// <returns>
    /// The JSON texts to send.
    /// </returns>
    public static IEnumerable<string> ControlMessages(string type, IEnumerable<string> symbols) =>
        symbols
            .Chunk(MaxProductsPerMessage)
            .Select(chunk => JsonSerializer.Serialize(new { type, product_ids = chunk, channels = new[] { "ticker" } }));

    /// <summary>
    /// Parses a <c>ticker</c> message.
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
            || !root.TryGetProperty("type", out var type) || type.GetString() != "ticker"
            || !root.TryGetProperty("product_id", out var product) || product.GetString() is not { Length: > 0 } symbol
            || !JsonValues.TryGetDecimal(root, "price", out var price))
        {
            return false;
        }

        decimal? reference = JsonValues.TryGetPositiveDecimal(root, "open_24h", out var open) ? open : null;
        var timestamp = JsonValues.TryGetIsoTime(root, "time", out var parsed) ? parsed : fallbackTime;
        quote = new Quote(new AssetId(providerId, symbol), price, reference, timestamp);
        return true;
    }

    /// <summary>
    /// Reads the message of an <c>error</c> event.
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

        var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
        var reason = root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
        return string.IsNullOrEmpty(reason) ? message : $"{message} ({reason})";
    }

    /// <summary>
    /// Parses a candles response into close-price samples.
    /// </summary>
    /// <param name="json">
    /// The response body: arrays of <c>[time, low, high, open, close, volume]</c>, newest first.
    /// </param>
    /// <returns>
    /// The most recent samples, oldest first.
    /// </returns>
    public static IReadOnlyList<PricePoint> ParseCandles(string json)
    {
        using var document = JsonDocument.Parse(json);
        var points = new List<PricePoint>();
        foreach (var candle in document.RootElement.EnumerateArray())
        {
            if (candle.ValueKind == JsonValueKind.Array
                && candle.GetArrayLength() > 4
                && candle[0].TryGetInt64(out var seconds)
                && JsonValues.TryGetDecimal(candle[4], out var close))
            {
                points.Add(new PricePoint(DateTimeOffset.FromUnixTimeSeconds(seconds), close));
            }
        }

        points.Reverse();
        return points.Count > HistoryLength ? points.GetRange(points.Count - HistoryLength, HistoryLength) : points;
    }

    /// <summary>
    /// Parses a products response into the tradable pairs.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The online products.
    /// </returns>
    public static IReadOnlyList<ListedSymbol> ParseProducts(string json)
    {
        using var document = JsonDocument.Parse(json);
        var symbols = new List<ListedSymbol>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var id) || id.GetString() is not { Length: > 0 } symbol)
            {
                continue;
            }

            if (item.TryGetProperty("status", out var status) && status.GetString() is { } text && text != "online")
            {
                continue;
            }

            if (item.TryGetProperty("trading_disabled", out var disabled) && disabled.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            symbols.Add(new ListedSymbol(symbol, SymbolSearch.PairNameOfDashedId(symbol)));
        }

        return symbols;
    }
}
