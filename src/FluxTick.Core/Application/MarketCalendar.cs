using FluxTick.Abstractions;

namespace FluxTick.Core.Application;

/// <summary>
/// Tells whether the market of an instrument is trading, from the regular hours of the market the instrument belongs to.
/// The market is told from the provider and the symbol. Public holidays are not listed; they show up as a regular
/// session in which nothing has traded yet that day, which needs the quote to carry the time of the trade rather than
/// the time it was fetched. A source that reports the state of its market itself is preferred to these hours; see
/// <see cref="Quote.Market"/>.
/// </summary>
public static class MarketCalendar
{
    /// <summary>
    /// How long a regular session has to be under way before a last trade from an earlier day means that the market is
    /// shut, so that the first trades of the day have time to come in.
    /// </summary>
    private static readonly TimeSpan HolidayGrace = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The time of day at which the forex week starts on Sunday and ends on Friday, in New York.
    /// </summary>
    private static readonly TimeSpan ForexRollover = TimeSpan.FromHours(17);

    /// <summary>
    /// The Shanghai, Shenzhen and Beijing markets.
    /// </summary>
    private static readonly Market ChinaA = new(
        ["Asia/Shanghai", "China Standard Time"],
        [
            new(Hm(9, 15), Hm(9, 30), MarketState.PreMarket),
            new(Hm(9, 30), Hm(11, 30), MarketState.Open),
            new(Hm(11, 30), Hm(13, 0), MarketState.Break),
            new(Hm(13, 0), Hm(15, 0), MarketState.Open),
        ]);

    /// <summary>
    /// The Hong Kong market.
    /// </summary>
    private static readonly Market HongKong = new(
        ["Asia/Hong_Kong", "China Standard Time"],
        [
            new(Hm(9, 0), Hm(9, 30), MarketState.PreMarket),
            new(Hm(9, 30), Hm(12, 0), MarketState.Open),
            new(Hm(12, 0), Hm(13, 0), MarketState.Break),
            new(Hm(13, 0), Hm(16, 10), MarketState.Open), // 16:00 to 16:10 is the closing auction
        ]);

    /// <summary>
    /// The US markets.
    /// </summary>
    private static readonly Market Us = new(
        ["America/New_York", "Eastern Standard Time"],
        [
            new(Hm(4, 0), Hm(9, 30), MarketState.PreMarket),
            new(Hm(9, 30), Hm(16, 0), MarketState.Open),
            new(Hm(16, 0), Hm(20, 0), MarketState.AfterHours),
        ]);

    /// <summary>
    /// The indices of Yahoo that belong to the US markets.
    /// </summary>
    private static readonly HashSet<string> UsIndices = new(StringComparer.OrdinalIgnoreCase)
    {
        "^GSPC", "^DJI", "^IXIC", "^RUT", "^VIX", "^NYA", "^NDX",
    };

    /// <summary>
    /// The indices of Yahoo that belong to the Hong Kong market.
    /// </summary>
    private static readonly HashSet<string> HongKongIndices = new(StringComparer.OrdinalIgnoreCase) { "^HSI", "^HSCE" };

    /// <summary>
    /// Works out the trading state of an instrument.
    /// </summary>
    /// <param name="asset">
    /// The instrument.
    /// </param>
    /// <param name="now">
    /// The time to tell the state of.
    /// </param>
    /// <param name="lastTradeTime">
    /// When the latest price was traded, or <see langword="null"/> when that is not known.
    /// </param>
    /// <param name="holidayGrace">
    /// How long a regular session has to be under way before a last trade from an earlier day means a holiday, or
    /// <see langword="null"/> for the default. A source that lags behind the market needs a longer one.
    /// </param>
    /// <returns>
    /// The state, or <see cref="MarketState.Unknown"/> when the market of the instrument is not known.
    /// </returns>
    public static MarketState GetState(Asset asset, DateTimeOffset now, DateTimeOffset? lastTradeTime = null, TimeSpan? holidayGrace = null)
    {
        switch (asset.AssetClass)
        {
            case AssetClass.Crypto:
                return MarketState.Open;
            case AssetClass.Forex:
                return ForexState(now);
            case AssetClass.Commodity:
                return MarketState.Unknown;
        }

        var market = MarketOf(asset);
        if (market?.Zone is not { } zone)
        {
            return MarketState.Unknown;
        }

        var local = TimeZoneInfo.ConvertTime(now, zone);
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return MarketState.Closed;
        }

        var time = local.TimeOfDay;
        Session? session = null;
        foreach (var candidate in market.Sessions)
        {
            if (time >= candidate.Start && time < candidate.End)
            {
                session = candidate;
                break;
            }
        }

        if (session is null)
        {
            return MarketState.Closed;
        }

        // A weekday that the hours cannot know to be a holiday: the session is well under way and the last trade is from an
        // earlier day. Outside the regular session that is normal, as most sources report the last regular trade only. A
        // source that went quiet is no sign of a holiday, so the caller does not ask while it is.
        if (session.State == MarketState.Open
            && lastTradeTime is { } traded
            && TimeZoneInfo.ConvertTime(traded, zone).Date < local.Date
            && time - session.Start >= (holidayGrace ?? HolidayGrace))
        {
            return MarketState.Closed;
        }

        return session.State;
    }

    /// <summary>
    /// Creates a time of day.
    /// </summary>
    /// <param name="hours">
    /// The hours.
    /// </param>
    /// <param name="minutes">
    /// The minutes.
    /// </param>
    /// <returns>
    /// The time of day as a time span.
    /// </returns>
    private static TimeSpan Hm(int hours, int minutes) => new(hours, minutes, 0);

    /// <summary>
    /// Works out whether the forex market is open: from Sunday 17:00 to Friday 17:00 in New York.
    /// </summary>
    /// <param name="now">
    /// The time.
    /// </param>
    /// <returns>
    /// <see cref="MarketState.Open"/> or <see cref="MarketState.Closed"/>, or <see cref="MarketState.Unknown"/> when the
    /// system does not know the New York time zone.
    /// </returns>
    private static MarketState ForexState(DateTimeOffset now)
    {
        if (Us.Zone is not { } zone)
        {
            return MarketState.Unknown;
        }

        var local = TimeZoneInfo.ConvertTime(now, zone);
        var open = local.DayOfWeek switch
        {
            DayOfWeek.Saturday => false,
            DayOfWeek.Sunday => local.TimeOfDay >= ForexRollover,
            DayOfWeek.Friday => local.TimeOfDay < ForexRollover,
            _ => true,
        };
        return open ? MarketState.Open : MarketState.Closed;
    }

    /// <summary>
    /// Finds the market an instrument trades on.
    /// </summary>
    /// <param name="asset">
    /// The instrument.
    /// </param>
    /// <returns>
    /// The market, or <see langword="null"/> when it is not known.
    /// </returns>
    private static Market? MarketOf(Asset asset)
    {
        var symbol = asset.Symbol;
        switch (asset.ProviderId)
        {
            case "tencent":
                if (StartsWith(symbol, "sh") || StartsWith(symbol, "sz") || StartsWith(symbol, "bj"))
                {
                    return ChinaA;
                }

                return StartsWith(symbol, "hk") ? HongKong : StartsWith(symbol, "us") ? Us : null;
            case "sina":
                return StartsWith(symbol, "sh") || StartsWith(symbol, "sz") || StartsWith(symbol, "bj") ? ChinaA : null;
            case "yahoo":
                return YahooMarketOf(symbol);
            default:
                return null;
        }
    }

    /// <summary>
    /// Finds the market of a Yahoo symbol.
    /// </summary>
    /// <param name="symbol">
    /// The symbol, such as <c>AAPL</c>, <c>0700.HK</c>, <c>600000.SS</c> or <c>^GSPC</c>.
    /// </param>
    /// <returns>
    /// The market, or <see langword="null"/> when it is not known.
    /// </returns>
    private static Market? YahooMarketOf(string symbol)
    {
        if (symbol.StartsWith('^'))
        {
            return UsIndices.Contains(symbol) ? Us : HongKongIndices.Contains(symbol) ? HongKong : null;
        }

        var dot = symbol.LastIndexOf('.');
        if (dot < 0)
        {
            // A symbol with an equals sign is a forex pair or a future; a five letter one that ends in X is a mutual fund,
            // which is priced once a day.
            return symbol.Contains('=') || (symbol.Length == 5 && symbol.EndsWith('X')) ? null : Us;
        }

        var suffix = symbol[(dot + 1)..];

        // The letter A, B or C after the dot is the share class of a US company, such as BRK.B. Other single letters name
        // exchanges, such as L for London.
        return suffix.Length == 1 && suffix[0] is 'A' or 'B' or 'C' or 'a' or 'b' or 'c'
            ? Us
            : suffix.ToUpperInvariant() switch
            {
                "SS" or "SZ" or "BJ" => ChinaA,
                "HK" => HongKong,
                _ => null,
            };
    }

    /// <summary>
    /// Determines whether a symbol starts with a prefix, ignoring case, and has more after it.
    /// </summary>
    /// <param name="symbol">
    /// The symbol.
    /// </param>
    /// <param name="prefix">
    /// The prefix.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when it does.
    /// </returns>
    private static bool StartsWith(string symbol, string prefix) =>
        symbol.Length > prefix.Length && symbol.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A stretch of the day with one state.
    /// </summary>
    /// <param name="Start">
    /// The time of day at which it starts.
    /// </param>
    /// <param name="End">
    /// The time of day at which it ends.
    /// </param>
    /// <param name="State">
    /// The state during it.
    /// </param>
    private sealed record Session(TimeSpan Start, TimeSpan End, MarketState State);

    /// <summary>
    /// The hours of one market.
    /// </summary>
    private sealed class Market
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Market"/> class.
        /// </summary>
        /// <param name="zoneIds">
        /// The identifiers of the time zone, tried in turn as systems name zones differently.
        /// </param>
        /// <param name="sessions">
        /// The stretches of a trading day, in order; the time between them is closed.
        /// </param>
        public Market(string[] zoneIds, Session[] sessions)
        {
            Sessions = sessions;
            Zone = Find(zoneIds);
        }

        /// <summary>
        /// Gets the time zone of the market, or <see langword="null"/> when the system knows none of its identifiers, so
        /// that a system without time zone data does not fail the whole class.
        /// </summary>
        public TimeZoneInfo? Zone { get; }

        /// <summary>
        /// Gets the stretches of a trading day.
        /// </summary>
        public Session[] Sessions { get; }

        /// <summary>
        /// Finds a time zone by the first identifier the system knows.
        /// </summary>
        /// <param name="ids">
        /// The identifiers.
        /// </param>
        /// <returns>
        /// The time zone, or <see langword="null"/> when none is known.
        /// </returns>
        private static TimeZoneInfo? Find(string[] ids)
        {
            foreach (var id in ids)
            {
                if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
                {
                    return zone;
                }
            }

            return null;
        }
    }
}
