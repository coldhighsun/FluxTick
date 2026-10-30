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
    /// Dates daily closes by the day at the exchange, once, so that picking a previous close needs no time zone conversion
    /// per candle. The result is ordered by day whatever the order of the answer. Most candles are stamped with the open of
    /// the session, which falls on the trading day at the exchange; one stamped at exactly midnight UTC names a calendar day
    /// rather than an instant, so its UTC date is the day (converting it would move it to the evening before for an
    /// exchange west of UTC).
    /// </summary>
    /// <param name="closes">
    /// The closes of the daily candles, each at the time of its candle.
    /// </param>
    /// <param name="zone">
    /// The time zone of the exchange, or <see langword="null"/> to use UTC.
    /// </param>
    /// <returns>
    /// The closes, oldest day first.
    /// </returns>
    public static DailyClose[] ToDailyCloses(IEnumerable<PricePoint> closes, TimeZoneInfo? zone)
    {
        zone ??= TimeZoneInfo.Utc;
        return [.. closes
            .OrderBy(c => c.Timestamp)
            .Select(c => new DailyClose(DayAtExchange(c.Timestamp, zone), c.Price))];
    }

    /// <summary>
    /// Gets the day at the exchange of a candle time. A time at exactly midnight UTC names a calendar day rather than an instant,
    /// so its UTC date is the day; converting it would move it to the evening before for an exchange west of UTC.
    /// </summary>
    /// <param name="time">
    /// The time.
    /// </param>
    /// <param name="zone">
    /// The time zone of the exchange.
    /// </param>
    /// <returns>
    /// The day at the exchange.
    /// </returns>
    private static DateOnly DayAtExchange(DateTimeOffset time, TimeZoneInfo zone) =>
        time.UtcDateTime.TimeOfDay == TimeSpan.Zero
            ? DateOnly.FromDateTime(time.UtcDateTime)
            : DayOfInstant(time, zone);

    /// <summary>
    /// Gets the day at the exchange of an instant, such as the time a price was traded.
    /// </summary>
    /// <param name="time">
    /// The instant.
    /// </param>
    /// <param name="zone">
    /// The time zone of the exchange.
    /// </param>
    /// <returns>
    /// The date there.
    /// </returns>
    private static DateOnly DayOfInstant(DateTimeOffset time, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, zone).DateTime);

    /// <summary>
    /// Picks the previous close out of daily closes: the close of the latest candle from a day before the day the price was
    /// traded, at the exchange. The candle of that day is partial while the market trades, so it is not it. Dating by the
    /// trade rather than by the clock keeps a price that is still the last close (over a weekend or a holiday, or before
    /// the open) measured against the close before it, instead of against itself. A price traded after the regular session
    /// of its day, and different from the close of that day, is an after-hours price and is measured against that close.
    /// </summary>
    /// <param name="closes">
    /// The daily closes, oldest day first, as <see cref="ToDailyCloses"/> gives them.
    /// </param>
    /// <param name="price">
    /// The price to measure.
    /// </param>
    /// <param name="tradeTime">
    /// When the price was traded.
    /// </param>
    /// <param name="exchange">
    /// The exchange of the ticker, or <see langword="null"/> when it is not known, which dates by UTC.
    /// </param>
    /// <param name="unsettledDay">
    /// The day whose candle may still be partial because the closes were fetched before the session ended, or
    /// <see langword="null"/> when every candle is final. An after-hours price of that day has no previous close yet.
    /// </param>
    /// <returns>
    /// The previous close, or <see langword="null"/> when there is no earlier candle or the candle needed is not final.
    /// </returns>
    public static decimal? PreviousCloseBefore(IReadOnlyList<DailyClose> closes, decimal price, DateTimeOffset tradeTime, ExchangeHours? exchange, DateOnly? unsettledDay = null)
    {
        // The time of a trade is an instant, so unlike a candle stamped at midnight UTC it is always converted.
        var tradeDay = DayOfInstant(tradeTime, exchange?.Zone ?? TimeZoneInfo.Utc);
        bool? afterHours = null;
        for (var i = closes.Count - 1; i >= 0; i--)
        {
            var close = closes[i];
            if (close.Day < tradeDay)
            {
                return close.Price;
            }

            // The close of the trading day itself is the reference only for a price traded after the regular session that
            // differs from it; a price equal to it is that close, and is measured against the day before. The check is made
            // only when such a candle is met, as it is the costly part. It relies on the candle being complete, which the
            // cache ensures by fetching again once the session has ended.
            if (close.Day == tradeDay)
            {
                afterHours ??= exchange?.HasRegularSessionEnded(tradeTime) ?? false;
                if (afterHours == true)
                {
                    // A candle still open for updates holds an intraday price, so it is no reference: wait for the fetch
                    // the cache makes once the day has settled.
                    if (close.Day == unsettledDay)
                    {
                        return null;
                    }

                    if (close.Price != price)
                    {
                        return close.Price;
                    }
                }
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

/// <summary>
/// The close of a daily candle, dated by the day at the exchange.
/// </summary>
/// <param name="Day">
/// The trading day at the exchange.
/// </param>
/// <param name="Price">
/// The close of that day.
/// </param>
internal readonly record struct DailyClose(DateOnly Day, decimal Price);
