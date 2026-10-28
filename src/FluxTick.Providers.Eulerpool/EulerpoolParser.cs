using System.Globalization;
using System.Text.Json;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;

namespace FluxTick.Providers.Eulerpool;

/// <summary>
/// Parses the messages of the Eulerpool stream and the answers of its REST API.
/// </summary>
internal static class EulerpoolParser
{
    /// <summary>
    /// The smallest Unix time in milliseconds; a smaller number is taken to be in seconds.
    /// </summary>
    private const long MinMilliseconds = 100_000_000_000;

    /// <summary>
    /// Tries to read a price from a stream message. Trades (<c>T</c>) carry the price of the trade; the one-second bars
    /// (<c>A</c>), the only channel a free key receives, carry the close of the bar and are dated by the end of the bar.
    /// Status messages, quotes and anything else are ignored.
    /// </summary>
    /// <param name="text">
    /// The message text.
    /// </param>
    /// <param name="tick">
    /// The price, or the default when the message carries none.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the message carries a price.
    /// </returns>
    public static bool TryParseTick(string text, out EulerpoolTick tick)
    {
        tick = default;
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || GetString(root, "ev") is not { } ev
            || GetString(root, "ticker") is not { } symbol)
        {
            return false;
        }

        decimal price;
        DateTimeOffset? time;
        switch (ev)
        {
            case "T":
                if (!JsonValues.TryGetPositiveDecimal(root, "price", out price))
                {
                    return false;
                }

                time = ReadTime(root, "ts") ?? ReadTime(root, "timestamp");
                break;
            case "A":
                if (!JsonValues.TryGetPositiveDecimal(root, "close", out price))
                {
                    return false;
                }

                time = ReadTime(root, "end");
                break;
            default:
                return false;
        }

        tick = new EulerpoolTick(symbol, price, time);
        return true;
    }

    /// <summary>
    /// Parses the answer of the latest quotes endpoint, an object with one entry per ticker that has a price.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The prices, keyed by ticker.
    /// </returns>
    public static IReadOnlyList<ParsedQuote> ParseQuotes(string json)
    {
        using var document = JsonDocument.Parse(json);
        var quotes = new List<ParsedQuote>();
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return quotes;
        }

        foreach (var entry in document.RootElement.EnumerateObject())
        {
            if (JsonValues.TryGetPositiveDecimal(entry.Value, "price", out var price))
            {
                quotes.Add(new ParsedQuote(entry.Name, price, null, ReadTime(entry.Value, "timestamp")));
            }
        }

        return quotes;
    }

    /// <summary>
    /// Parses the answer of the daily candles endpoint (<c>{"t":[..],"o":[..],"h":[..],"l":[..],"c":[..],"v":[..]}</c>)
    /// into the previous close: the close of the latest candle from a day before today at the exchange. The candle of
    /// today, which is partial while the market trades, is not it.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <param name="now">
    /// The current time.
    /// </param>
    /// <param name="zone">
    /// The time zone of the exchange, or <see langword="null"/> to use UTC.
    /// </param>
    /// <returns>
    /// The previous close, or <see langword="null"/> when there is no earlier candle with a price.
    /// </returns>
    public static decimal? ParsePreviousClose(string json, DateTimeOffset now, TimeZoneInfo? zone)
    {
        using var document = JsonDocument.Parse(json);
        zone ??= TimeZoneInfo.Utc;
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        foreach (var point in ReadCandles(document.RootElement).Reverse())
        {
            if (TimeZoneInfo.ConvertTime(point.Timestamp, zone).Date < today)
            {
                return point.Price;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses the answer of the candles endpoint into prices, the close of each candle at its time. Candles without a
    /// usable time or close are skipped.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The prices, oldest first as the service sends them; empty for an answer without candles.
    /// </returns>
    public static IReadOnlyList<PricePoint> ParseHistory(string json)
    {
        using var document = JsonDocument.Parse(json);
        return [.. ReadCandles(document.RootElement)];
    }

    /// <summary>
    /// Reads the parallel arrays of times (<c>t</c>) and closes (<c>c</c>) of a candles answer.
    /// </summary>
    /// <param name="root">
    /// The body of the answer.
    /// </param>
    /// <returns>
    /// The close of each candle with a usable time and a positive close, in the order of the answer.
    /// </returns>
    private static IEnumerable<PricePoint> ReadCandles(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("t", out var times) || times.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("c", out var closes) || closes.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        var count = Math.Min(times.GetArrayLength(), closes.GetArrayLength());
        for (var i = 0; i < count; i++)
        {
            if (times[i].ValueKind == JsonValueKind.Number
                && times[i].TryGetInt64(out var seconds)
                && JsonValues.TryGetDecimal(closes[i], out var close)
                && close > 0m)
            {
                DateTimeOffset time;
                try
                {
                    time = DateTimeOffset.FromUnixTimeSeconds(seconds);
                }
                catch (ArgumentOutOfRangeException)
                {
                    continue;
                }

                yield return new PricePoint(time, close);
            }
        }
    }

    /// <summary>
    /// Parses the answer of the search endpoint. Bonds, funds and other types that have no price stream are left out.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <param name="providerId">
    /// The id of the provider the instruments belong to.
    /// </param>
    /// <returns>
    /// The instruments found.
    /// </returns>
    public static IReadOnlyList<Asset> ParseSearch(string json, string providerId)
    {
        using var document = JsonDocument.Parse(json);
        var assets = new List<Asset>();
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array)
        {
            return assets;
        }

        foreach (var item in results.EnumerateArray())
        {
            var symbol = GetString(item, "ticker");
            if (symbol is null || MapType(GetString(item, "type")) is not { } assetClass)
            {
                continue;
            }

            assets.Add(new Asset(providerId, symbol, GetString(item, "name") ?? symbol, assetClass));
        }

        return assets;
    }

    /// <summary>
    /// Maps the type of the search service to an asset class.
    /// </summary>
    /// <param name="type">
    /// The type the service reports.
    /// </param>
    /// <returns>
    /// The asset class, or <see langword="null"/> for a type the source cannot price.
    /// </returns>
    private static AssetClass? MapType(string? type) => type?.ToLowerInvariant() switch
    {
        "stock" or "etf" => AssetClass.Equity,
        _ => null,
    };

    /// <summary>
    /// Reads a Unix time that is a number or a string, in seconds or in milliseconds.
    /// </summary>
    /// <param name="parent">
    /// The object that holds the property.
    /// </param>
    /// <param name="name">
    /// The name of the property.
    /// </param>
    /// <returns>
    /// The time, or <see langword="null"/> when the property is missing or not a time.
    /// </returns>
    private static DateTimeOffset? ReadTime(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var element))
        {
            return null;
        }

        var raw = element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(value >= MinMilliseconds ? value : value * 1000);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a non-empty string property.
    /// </summary>
    /// <param name="parent">
    /// The object that holds the property.
    /// </param>
    /// <param name="name">
    /// The name of the property.
    /// </param>
    /// <returns>
    /// The value, or <see langword="null"/> when it is missing, not a string or blank.
    /// </returns>
    private static string? GetString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()
            : null;
}

/// <summary>
/// A price read from a stream message.
/// </summary>
/// <param name="Symbol">
/// The ticker the message names.
/// </param>
/// <param name="Price">
/// The price.
/// </param>
/// <param name="Time">
/// When it was traded, or <see langword="null"/> when the message names no time.
/// </param>
internal readonly record struct EulerpoolTick(string Symbol, decimal Price, DateTimeOffset? Time);
