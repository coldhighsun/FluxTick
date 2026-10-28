using System.Text.Json;
using FluxTick.Abstractions;

namespace FluxTick.Providers.Yahoo;

/// <summary>
/// The parsed content of a Yahoo chart response.
/// </summary>
/// <param name="Quote">
/// The latest quote.
/// </param>
/// <param name="History">
/// Intraday samples, oldest first.
/// </param>
/// <param name="Sessions">
/// Whether the extended sessions are under way, as the chart describes them or, for the US exchanges, as their fixed
/// lengths give them; <see langword="null"/> when neither tells.
/// </param>
internal sealed record YahooChartData(Quote Quote, IReadOnlyList<PricePoint> History, YahooSessions? Sessions = null);

/// <summary>
/// Whether the extended sessions of the day are under way, for the sessions the chart describes.
/// </summary>
/// <param name="InPreMarket">
/// Whether the pre-market session is under way, or <see langword="null"/> when the chart gives none.
/// </param>
/// <param name="InPostMarket">
/// Whether the post-market session is under way, or <see langword="null"/> when the chart gives none.
/// </param>
internal sealed record YahooSessions(bool? InPreMarket, bool? InPostMarket);

/// <summary>
/// Raised when Yahoo answers with an error object instead of data (unknown symbol, delisted, ...).
/// </summary>
/// <param name="message">
/// The message reported by Yahoo.
/// </param>
internal sealed class YahooApiException(string message) : Exception(message);

/// <summary>
/// Pure functions that parse Yahoo Finance JSON payloads.
/// </summary>
internal static class YahooParser
{
    /// <summary>
    /// The smallest number of seconds since the Unix epoch that a date can hold.
    /// </summary>
    private static readonly long MinUnixSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds();

    /// <summary>
    /// The largest number of seconds since the Unix epoch that a date can hold.
    /// </summary>
    private static readonly long MaxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    /// <summary>
    /// How long before the regular session the opening auction of a market without a pre-market period starts, such as
    /// the Shanghai and Shenzhen markets, whose call auction is from 09:15.
    /// </summary>
    private static readonly TimeSpan OpeningAuction = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The time zone Yahoo names for the US exchanges.
    /// </summary>
    private const string UsTimeZone = "America/New_York";

    /// <summary>
    /// How long the pre-market session of the US exchanges lasts, up to the open.
    /// </summary>
    private static readonly TimeSpan UsPreMarketLength = TimeSpan.FromMinutes(330);

    /// <summary>
    /// How long the post-market session of the US exchanges lasts, from the close.
    /// </summary>
    private static readonly TimeSpan UsPostMarketLength = TimeSpan.FromHours(4);

    /// <summary>
    /// Parses a <c>v8/finance/chart</c> response.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <param name="providerId">
    /// The provider id to stamp on the quote.
    /// </param>
    /// <param name="symbol">
    /// The symbol that was requested.
    /// </param>
    /// <param name="now">
    /// The time the response was received, or <see langword="null"/> for the current time.
    /// </param>
    /// <returns>
    /// The quote and intraday history.
    /// </returns>
    /// <exception cref="YahooApiException">
    /// Yahoo reported an error or the response had no price.
    /// </exception>
    public static YahooChartData ParseChart(string json, string providerId, string symbol, DateTimeOffset? now = null)
    {
        var received = now ?? DateTimeOffset.UtcNow;
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("chart", out var chart))
        {
            throw new YahooApiException("Yahoo returned an unexpected response.");
        }

        if (chart.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            var description = error.TryGetProperty("description", out var d) ? d.GetString() : null;
            throw new YahooApiException(description ?? "Yahoo returned an error.");
        }

        if (!chart.TryGetProperty("result", out var results)
            || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0)
        {
            throw new YahooApiException("Yahoo returned no result.");
        }

        var result = results[0];
        if (!result.TryGetProperty("meta", out var meta) || meta.ValueKind != JsonValueKind.Object)
        {
            throw new YahooApiException("Yahoo returned no price.");
        }

        if (!meta.TryGetProperty("regularMarketPrice", out var priceElement)
            || priceElement.ValueKind != JsonValueKind.Number
            || !priceElement.TryGetDecimal(out var price))
        {
            throw new YahooApiException("Yahoo returned no price.");
        }

        decimal? reference = null;
        foreach (var name in new[] { "previousClose", "chartPreviousClose" })
        {
            if (meta.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var value))
            {
                reference = value;
                break;
            }
        }

        // A time that is missing, or that no date can hold, counts as not given: the quote is as old as the answer.
        var tradeTime = meta.TryGetProperty("regularMarketTime", out var time)
            && time.TryGetInt64(out var seconds)
            && TryUnixTime(seconds, out var traded)
            ? (DateTimeOffset?)traded
            : null;

        var quote = new Quote(new AssetId(providerId, symbol), price, reference, tradeTime ?? received)
        {
            Market = ParseMarketState(meta, received),
            TimeIsEstimated = tradeTime is null,
        };
        return new YahooChartData(quote, ParseHistory(result), ParseSessions(meta, received));
    }

    /// <summary>
    /// Tells whether the extended sessions are under way, for those the trading periods of the chart describe.
    /// </summary>
    /// <param name="meta">
    /// The <c>meta</c> object of the chart result.
    /// </param>
    /// <param name="now">
    /// The time to tell the sessions of.
    /// </param>
    /// <returns>
    /// The sessions, or <see langword="null"/> when the chart has no trading periods.
    /// </returns>
    private static YahooSessions? ParseSessions(JsonElement meta, DateTimeOffset now)
    {
        if (!meta.TryGetProperty("currentTradingPeriod", out var periods) || periods.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // The US exchanges do not always give the extended periods, but their length is fixed: the pre-market session runs
        // for five and a half hours up to the open and the post-market session for four hours from the close. Taken from
        // the regular session of the day, this also holds on a day that closes early. Other exchanges have other hours.
        var us = meta.TryGetProperty("exchangeTimezoneName", out var zone)
            && zone.ValueKind == JsonValueKind.String
            && zone.GetString() == UsTimeZone
            && TryGetPeriod(periods, "regular", out _);
        TryGetPeriod(periods, "regular", out var regular);
        var seconds = now.ToUnixTimeSeconds();
        return new YahooSessions(
            TryGetPeriod(periods, "pre", out var pre)
                ? pre.Contains(seconds)
                : us ? new TradingPeriod(regular.Start - (long)UsPreMarketLength.TotalSeconds, regular.Start).Contains(seconds) : null,
            TryGetPeriod(periods, "post", out var post)
                ? post.Contains(seconds)
                : us ? new TradingPeriod(regular.End, regular.End + (long)UsPostMarketLength.TotalSeconds).Contains(seconds) : null);
    }

    /// <summary>
    /// Works out the trading state of the market from the trading periods Yahoo gives for the current day. Yahoo knows
    /// the holidays and shortened days of every exchange, which regular hours do not. Only shares, funds and indices are
    /// told; the periods of currencies, futures and crypto cover the whole day.
    /// </summary>
    /// <param name="meta">
    /// The <c>meta</c> object of the chart result.
    /// </param>
    /// <param name="now">
    /// The time to tell the state of.
    /// </param>
    /// <returns>
    /// The state, or <see langword="null"/> when Yahoo does not say.
    /// </returns>
    private static MarketState? ParseMarketState(JsonElement meta, DateTimeOffset now)
    {
        if (!meta.TryGetProperty("instrumentType", out var type)
            || type.ValueKind != JsonValueKind.String
            || type.GetString() is not ("EQUITY" or "ETF" or "INDEX")
            || !meta.TryGetProperty("currentTradingPeriod", out var periods)
            || periods.ValueKind != JsonValueKind.Object
            || !TryGetPeriod(periods, "regular", out var regular))
        {
            return null;
        }

        var seconds = now.ToUnixTimeSeconds();
        if (regular.Contains(seconds))
        {
            return MarketState.Open;
        }

        if ((TryGetPeriod(periods, "pre", out var pre) && pre.Contains(seconds))
            || (seconds < regular.Start && seconds >= regular.Start - (long)OpeningAuction.TotalSeconds))
        {
            return MarketState.PreMarket;
        }

        return TryGetPeriod(periods, "post", out var post) && post.Contains(seconds) ? MarketState.AfterHours : MarketState.Closed;
    }

    /// <summary>
    /// Reads one trading period.
    /// </summary>
    /// <param name="periods">
    /// The <c>currentTradingPeriod</c> object.
    /// </param>
    /// <param name="name">
    /// The name of the period: <c>pre</c>, <c>regular</c> or <c>post</c>.
    /// </param>
    /// <param name="period">
    /// The period, when it was read.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the period is there with a start and an end.
    /// </returns>
    private static bool TryGetPeriod(JsonElement periods, string name, out TradingPeriod period)
    {
        period = default;
        if (periods.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("start", out var start)
            && start.TryGetInt64(out var startSeconds)
            && element.TryGetProperty("end", out var end)
            && end.TryGetInt64(out var endSeconds))
        {
            period = new TradingPeriod(startSeconds, endSeconds);
            return true;
        }

        return false;
    }

    /// <summary>
    /// A stretch of the trading day, in seconds since the Unix epoch.
    /// </summary>
    /// <param name="Start">
    /// When it starts.
    /// </param>
    /// <param name="End">
    /// When it ends.
    /// </param>
    private readonly record struct TradingPeriod(long Start, long End)
    {
        /// <summary>
        /// Determines whether a time falls within the period; a period that is empty contains no time.
        /// </summary>
        /// <param name="seconds">
        /// The time in seconds since the Unix epoch.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when it does.
        /// </returns>
        public bool Contains(long seconds) => seconds >= Start && seconds < End;
    }

    /// <summary>
    /// Parses a <c>v1/finance/search</c> response into assets.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <param name="providerId">
    /// The provider id to stamp on the assets.
    /// </param>
    /// <returns>
    /// The instruments Yahoo can quote; unsupported quote types (such as options) are skipped.
    /// </returns>
    public static IReadOnlyList<Asset> ParseSearch(string json, string providerId)
    {
        using var document = JsonDocument.Parse(json);
        var assets = new List<Asset>();
        if (!document.RootElement.TryGetProperty("quotes", out var quotes))
        {
            return assets;
        }

        foreach (var item in quotes.EnumerateArray())
        {
            if (!item.TryGetProperty("symbol", out var symbol) || !item.TryGetProperty("quoteType", out var type))
            {
                continue;
            }

            if (MapQuoteType(type.GetString()) is not { } assetClass)
            {
                continue;
            }

            var name = GetString(item, "longname") ?? GetString(item, "shortname") ?? symbol.GetString()!;
            assets.Add(new Asset(providerId, symbol.GetString()!, name, assetClass));
        }

        return assets;
    }

    /// <summary>
    /// Maps a Yahoo quote type to an asset class.
    /// </summary>
    /// <param name="quoteType">
    /// The Yahoo quote type.
    /// </param>
    /// <returns>
    /// The asset class, or <see langword="null"/> when the type is not supported.
    /// </returns>
    internal static AssetClass? MapQuoteType(string? quoteType) => quoteType switch
    {
        "EQUITY" or "ETF" or "MUTUALFUND" => AssetClass.Equity,
        "INDEX" => AssetClass.Index,
        "CURRENCY" => AssetClass.Forex,
        "FUTURE" => AssetClass.Commodity,
        "CRYPTOCURRENCY" => AssetClass.Crypto,
        _ => null,
    };

    /// <summary>
    /// Extracts the intraday close series from a chart result.
    /// </summary>
    /// <param name="result">
    /// The first element of <c>chart.result</c>.
    /// </param>
    /// <returns>
    /// The samples, oldest first; empty when the response carries none.
    /// </returns>
    private static IReadOnlyList<PricePoint> ParseHistory(JsonElement result)
    {
        if (!result.TryGetProperty("timestamp", out var timestamps)
            || timestamps.ValueKind != JsonValueKind.Array
            || !result.TryGetProperty("indicators", out var indicators)
            || !indicators.TryGetProperty("quote", out var quoteArray)
            || quoteArray.ValueKind != JsonValueKind.Array
            || quoteArray.GetArrayLength() == 0
            || !quoteArray[0].TryGetProperty("close", out var closes)
            || closes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var points = new List<PricePoint>();
        var count = Math.Min(timestamps.GetArrayLength(), closes.GetArrayLength());
        for (var i = 0; i < count; i++)
        {
            if (closes[i].ValueKind == JsonValueKind.Number
                && closes[i].TryGetDecimal(out var close)
                && timestamps[i].ValueKind == JsonValueKind.Number
                && timestamps[i].TryGetInt64(out var seconds)
                && TryUnixTime(seconds, out var traded))
            {
                points.Add(new PricePoint(traded, close));
            }
        }

        return points;
    }

    /// <summary>
    /// Converts seconds since the Unix epoch to a time, unless no date can hold them, which
    /// <see cref="DateTimeOffset.FromUnixTimeSeconds"/> would answer with an exception.
    /// </summary>
    /// <param name="seconds">
    /// The seconds since the Unix epoch.
    /// </param>
    /// <param name="time">
    /// The time, when there is one.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the seconds are within the range of dates.
    /// </returns>
    private static bool TryUnixTime(long seconds, out DateTimeOffset time)
    {
        if (seconds < MinUnixSeconds || seconds > MaxUnixSeconds)
        {
            time = default;
            return false;
        }

        time = DateTimeOffset.FromUnixTimeSeconds(seconds);
        return true;
    }

    /// <summary>
    /// Reads an optional string property.
    /// </summary>
    /// <param name="element">
    /// The object to read from.
    /// </param>
    /// <param name="name">
    /// The property name.
    /// </param>
    /// <returns>
    /// The value, or <see langword="null"/> when missing.
    /// </returns>
    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
