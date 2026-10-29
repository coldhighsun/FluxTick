using FluxTick.Abstractions;

namespace FluxTick.Providers.Common;

/// <summary>
/// The regular trading hours of one exchange, for the sources that implement <see cref="IMarketHoursProvider"/>. A
/// source maps its symbols to the exchanges it lists and asks them for the state.
/// </summary>
public sealed class ExchangeHours
{
    /// <summary>
    /// How long a regular session has to be under way before a last trade from an earlier day means that the market is
    /// shut, so that the first trades of the day have time to come in.
    /// </summary>
    private static readonly TimeSpan DefaultHolidayGrace = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The stretches of a trading day, in order; the time between them is closed.
    /// </summary>
    private readonly TradingSession[] _sessions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExchangeHours"/> class.
    /// </summary>
    /// <param name="zoneIds">
    /// The identifiers of the time zone, tried in turn as systems name zones differently.
    /// </param>
    /// <param name="sessions">
    /// The stretches of a trading day, in order; the time between them is closed.
    /// </param>
    public ExchangeHours(string[] zoneIds, TradingSession[] sessions)
    {
        _sessions = sessions;
        Zone = zoneIds.Select(id => TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : null).FirstOrDefault(z => z is not null);
    }

    /// <summary>
    /// Gets the Shanghai, Shenzhen and Beijing exchanges.
    /// </summary>
    public static ExchangeHours ChinaA { get; } = new(
        ["Asia/Shanghai", "China Standard Time"],
        [
            new(Hm(9, 15), Hm(9, 30), MarketState.PreMarket),
            new(Hm(9, 30), Hm(11, 30), MarketState.Open),
            new(Hm(11, 30), Hm(13, 0), MarketState.Break),
            new(Hm(13, 0), Hm(15, 0), MarketState.Open),
        ]);

    /// <summary>
    /// Gets the Hong Kong exchange.
    /// </summary>
    public static ExchangeHours HongKong { get; } = new(
        ["Asia/Hong_Kong", "China Standard Time"],
        [
            new(Hm(9, 0), Hm(9, 30), MarketState.PreMarket),
            new(Hm(9, 30), Hm(12, 0), MarketState.Open),
            new(Hm(12, 0), Hm(13, 0), MarketState.Break),
            new(Hm(13, 0), Hm(16, 10), MarketState.Open), // 16:00 to 16:10 is the closing auction
        ]);

    /// <summary>
    /// Gets the US exchanges.
    /// </summary>
    public static ExchangeHours Us { get; } = new(
        ["America/New_York", "Eastern Standard Time"],
        [
            new(Hm(4, 0), Hm(9, 30), MarketState.PreMarket),
            new(Hm(9, 30), Hm(16, 0), MarketState.Open),
            new(Hm(16, 0), Hm(20, 0), MarketState.AfterHours),
        ]);

    /// <summary>
    /// Gets the time zone of the exchange, or <see langword="null"/> when the system knows none of its identifiers, so
    /// that a system without time zone data does not fail the whole class.
    /// </summary>
    public TimeZoneInfo? Zone { get; }

    /// <summary>
    /// Makes a time of day.
    /// </summary>
    /// <param name="hours">
    /// The hour.
    /// </param>
    /// <param name="minutes">
    /// The minute.
    /// </param>
    /// <returns>
    /// The time of day.
    /// </returns>
    public static TimeSpan Hm(int hours, int minutes) => new(hours, minutes, 0);

    /// <summary>
    /// Gets the trading state of the exchange.
    /// </summary>
    /// <param name="now">
    /// The current time.
    /// </param>
    /// <param name="lastTradeTime">
    /// When the instrument last traded, or <see langword="null"/> when unknown. A regular session that is well under way
    /// with the last trade from an earlier day is taken for a holiday.
    /// </param>
    /// <param name="holidayGrace">
    /// How long a session has to be under way for that, or <see langword="null"/> for 15 minutes.
    /// </param>
    /// <returns>
    /// The state; <see cref="MarketState.Unknown"/> when the system does not know the time zone.
    /// </returns>
    public MarketState GetState(DateTimeOffset now, DateTimeOffset? lastTradeTime = null, TimeSpan? holidayGrace = null)
    {
        if (Zone is not { } zone)
        {
            return MarketState.Unknown;
        }

        var local = TimeZoneInfo.ConvertTime(now, zone);
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return MarketState.Closed;
        }

        var time = local.TimeOfDay;
        var session = _sessions.FirstOrDefault(s => time >= s.Start && time < s.End);
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
            && time - session.Start >= (holidayGrace ?? DefaultHolidayGrace))
        {
            return MarketState.Closed;
        }

        return session.State;
    }

    /// <summary>
    /// Pulls a time that lies after a regular session ended back to the end of that session. For sources that stamp a quote
    /// with their own clock instead of the time of the trade, which would keep a closing price looking current for as
    /// long as the source goes on sending it. A time inside a session, before the first one of the day or on a weekend is
    /// returned as it is.
    /// </summary>
    /// <param name="time">
    /// The time the source gave.
    /// </param>
    /// <returns>
    /// The end of the last regular session that ended before the time on the same day, or the time itself.
    /// </returns>
    public DateTimeOffset ClampToSessionEnd(DateTimeOffset time)
    {
        if (Zone is not { } zone)
        {
            return time;
        }

        var local = TimeZoneInfo.ConvertTime(time, zone);
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return time;
        }

        var clock = local.TimeOfDay;
        if (_sessions.Any(s => s.State == MarketState.Open && clock >= s.Start && clock < s.End))
        {
            return time;
        }

        var ended = _sessions.Where(s => s.State == MarketState.Open && s.End <= clock).Select(s => s.End).DefaultIfEmpty().Max();
        return ended == TimeSpan.Zero ? time : time - (clock - ended);
    }

    /// <summary>
    /// Gets the trading state for the exchange a source found for a symbol, for the body of
    /// <see cref="IMarketHoursProvider.GetMarketState"/>.
    /// </summary>
    /// <param name="exchange">
    /// The exchange, or <see langword="null"/> when the source does not know the market of the symbol.
    /// </param>
    /// <param name="now">
    /// The current time.
    /// </param>
    /// <param name="lastTradeTime">
    /// When the instrument last traded, or <see langword="null"/> when unknown.
    /// </param>
    /// <param name="holidayGrace">
    /// How long a session has to be under way for a last trade from an earlier day to mean a holiday, or
    /// <see langword="null"/> for the default.
    /// </param>
    /// <returns>
    /// The state; <see cref="MarketState.Unknown"/> without an exchange.
    /// </returns>
    public static MarketState StateOf(ExchangeHours? exchange, DateTimeOffset now, DateTimeOffset? lastTradeTime, TimeSpan? holidayGrace) =>
        exchange?.GetState(now, lastTradeTime, holidayGrace) ?? MarketState.Unknown;
}

/// <summary>
/// A stretch of the trading day with one state.
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
public sealed record TradingSession(TimeSpan Start, TimeSpan End, MarketState State);
