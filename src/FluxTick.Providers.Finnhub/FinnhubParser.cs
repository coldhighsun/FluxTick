using System.Text.Json;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;

namespace FluxTick.Providers.Finnhub;

/// <summary>
/// One trade of the stream.
/// </summary>
/// <param name="Symbol">
/// The symbol the trade is of.
/// </param>
/// <param name="Price">
/// The price of the trade.
/// </param>
/// <param name="Time">
/// When the trade happened.
/// </param>
internal sealed record FinnhubTrade(string Symbol, decimal Price, DateTimeOffset Time);

/// <summary>
/// Pure functions that build and parse payloads of the Finnhub stream and REST API.
/// </summary>
internal static class FinnhubParser
{
    /// <summary>
    /// Builds the message that subscribes to or unsubscribes from the trades of one symbol; the stream takes one symbol
    /// per message.
    /// </summary>
    /// <param name="type">
    /// <c>subscribe</c> or <c>unsubscribe</c>.
    /// </param>
    /// <param name="symbol">
    /// The symbol.
    /// </param>
    /// <returns>
    /// The JSON text to send.
    /// </returns>
    public static string ControlMessage(string type, string symbol) => JsonSerializer.Serialize(new { type, symbol });

    /// <summary>
    /// Parses a message of the stream.
    /// </summary>
    /// <param name="json">
    /// The received text.
    /// </param>
    /// <returns>
    /// The trades it carries; none for pings, errors and other messages.
    /// </returns>
    public static IReadOnlyList<FinnhubTrade> ParseTrades(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("type", out var type) || type.GetString() != "trade"
            || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var trades = new List<FinnhubTrade>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("s", out var symbolElement) && symbolElement.GetString() is { Length: > 0 } symbol
                && JsonValues.TryGetDecimal(item, "p", out var price) && price > 0
                && item.TryGetProperty("t", out var t) && JsonValues.TryGetUnixMilliseconds(t, out var time))
            {
                trades.Add(new FinnhubTrade(symbol, price, time));
            }
        }

        return trades;
    }

    /// <summary>
    /// Reads the error a stream message reports.
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

        return root.TryGetProperty("msg", out var message) && message.ValueKind == JsonValueKind.String ? message.GetString() : "Request failed";
    }

    /// <summary>
    /// Parses the answer of the quote service for one symbol.
    /// </summary>
    /// <param name="symbol">
    /// The symbol that was asked for, which the answer does not repeat.
    /// </param>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The quote, or <see langword="null"/> when the symbol has no price: the service answers an unknown symbol with zeros.
    /// </returns>
    public static ParsedQuote? ParseQuote(string symbol, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !JsonValues.TryGetDecimal(root, "c", out var price) || price <= 0)
        {
            return null;
        }

        decimal? previous = JsonValues.TryGetPositiveDecimal(root, "pc", out var close) ? close : null;
        DateTimeOffset? time = JsonValues.TryGetDecimal(root, "t", out var seconds) && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds((long)seconds)
            : null;
        return new ParsedQuote(symbol, price, previous, time);
    }

    /// <summary>
    /// Parses a symbol search response into instruments.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <param name="providerId">
    /// The provider id to stamp on the instruments.
    /// </param>
    /// <returns>
    /// The instruments whose symbol is a plain US ticker; a symbol with an exchange suffix or prefix belongs to a market
    /// the free plan does not serve.
    /// </returns>
    public static IReadOnlyList<Asset> ParseSearch(string json, string providerId)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var assets = new List<Asset>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in result.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("symbol", out var symbolElement) || symbolElement.GetString() is not { Length: > 0 } symbol
                || symbol.AsSpan().ContainsAny(".:") || !seen.Add(symbol))
            {
                continue;
            }

            var name = item.TryGetProperty("description", out var description) ? description.GetString() : null;
            assets.Add(new Asset(providerId, symbol, string.IsNullOrWhiteSpace(name) ? symbol : name, AssetClass.Equity));
        }

        return assets;
    }
}
