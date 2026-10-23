using System.Text.Json;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;

namespace FluxTick.Providers.Binance;

/// <summary>
/// Pure functions that build and parse Binance WebSocket and REST payloads.
/// </summary>
internal static class BinanceMessages
{
    /// <summary>
    /// Builds the stream name for an instrument's 24h mini ticker.
    /// </summary>
    /// <param name="symbol">
    /// The Binance symbol, for example <c>BTCUSDT</c>.
    /// </param>
    /// <returns>
    /// The lower-case stream name, for example <c>btcusdt@miniTicker</c>.
    /// </returns>
    public static string StreamName(string symbol) => $"{symbol.ToLowerInvariant()}@miniTicker";

    /// <summary>
    /// Builds a SUBSCRIBE or UNSUBSCRIBE control message.
    /// </summary>
    /// <param name="method">
    /// <c>SUBSCRIBE</c> or <c>UNSUBSCRIBE</c>.
    /// </param>
    /// <param name="symbols">
    /// The symbols to (un)subscribe.
    /// </param>
    /// <param name="id">
    /// The request id echoed back by the server.
    /// </param>
    /// <returns>
    /// The JSON text to send.
    /// </returns>
    public static string ControlMessage(string method, IEnumerable<string> symbols, int id) =>
        JsonSerializer.Serialize(new { method, @params = symbols.Select(StreamName).ToArray(), id });

    /// <summary>
    /// Parses a 24h mini ticker event, either bare or wrapped in a combined-stream envelope.
    /// </summary>
    /// <param name="json">
    /// The received JSON text.
    /// </param>
    /// <param name="providerId">
    /// The provider id to stamp on the resulting quote.
    /// </param>
    /// <param name="quote">
    /// The parsed quote when the message was a mini ticker.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the message was a mini ticker event; control replies and other events return
    /// <see langword="false"/>.
    /// </returns>
    public static bool TryParseMiniTicker(string json, string providerId, out Quote quote)
    {
        quote = null!;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            root = data;
        }

        if (!root.TryGetProperty("e", out var eventType) || eventType.GetString() != "24hrMiniTicker"
            || !root.TryGetProperty("s", out var symbol)
            || !root.TryGetProperty("c", out var close)
            || !root.TryGetProperty("E", out var eventTime))
        {
            return false;
        }

        if (!JsonValues.TryGetDecimal(close, out var price))
        {
            return false;
        }

        decimal? reference = JsonValues.TryGetPositiveDecimal(root, "o", out var openPrice) ? openPrice : null;
        quote = new Quote(
            new AssetId(providerId, symbol.GetString()!),
            price,
            reference,
            DateTimeOffset.FromUnixTimeMilliseconds(eventTime.GetInt64()));
        return true;
    }

    /// <summary>
    /// Parses a klines response into close-price samples.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The samples, oldest first.
    /// </returns>
    public static IReadOnlyList<PricePoint> ParseKlines(string json)
    {
        using var document = JsonDocument.Parse(json);
        var points = new List<PricePoint>();
        foreach (var kline in document.RootElement.EnumerateArray())
        {
            if (kline.GetArrayLength() > 4 && JsonValues.TryGetDecimal(kline[4], out var close))
            {
                points.Add(new PricePoint(DateTimeOffset.FromUnixTimeMilliseconds(kline[0].GetInt64()), close));
            }
        }

        return points;
    }

    /// <summary>
    /// Parses a ticker price listing into symbols.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// All listed symbols.
    /// </returns>
    public static IReadOnlyList<string> ParseSymbols(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement
            .EnumerateArray()
            .Select(e => e.GetProperty("symbol").GetString()!)
            .ToArray();
    }
}
