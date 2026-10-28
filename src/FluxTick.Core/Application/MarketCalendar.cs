using FluxTick.Abstractions;

namespace FluxTick.Core.Application;

/// <summary>
/// Tells whether the market of an instrument is trading, for the asset classes whose hours do not depend on the source:
/// crypto trades around the clock and forex trades from Sunday evening to Friday evening in New York. The hours of
/// stock exchanges depend on the symbols of the source, so they belong to the provider; see
/// <see cref="IMarketHoursProvider"/>. A source that reports the state of its market itself is preferred to these hours;
/// see <see cref="Quote.Market"/>.
/// </summary>
public static class MarketCalendar
{
    /// <summary>
    /// The time of day at which the forex week starts on Sunday and ends on Friday, in New York.
    /// </summary>
    private static readonly TimeSpan ForexRollover = TimeSpan.FromHours(17);

    /// <summary>
    /// The time zone of the forex week, or <see langword="null"/> when the system knows none of its identifiers, so that a
    /// system without time zone data does not fail the whole class.
    /// </summary>
    private static readonly TimeZoneInfo? NewYork =
        TimeZoneInfo.TryFindSystemTimeZoneById("America/New_York", out var zone) ? zone
        : TimeZoneInfo.TryFindSystemTimeZoneById("Eastern Standard Time", out zone) ? zone
        : null;

    /// <summary>
    /// Gets the trading state of an instrument whose hours do not depend on the source.
    /// </summary>
    /// <param name="asset">
    /// The instrument.
    /// </param>
    /// <param name="now">
    /// The current time.
    /// </param>
    /// <returns>
    /// The state, or <see cref="MarketState.Unknown"/> for an asset class whose hours the source has to tell.
    /// </returns>
    public static MarketState GetState(Asset asset, DateTimeOffset now) => asset.AssetClass switch
    {
        AssetClass.Crypto => MarketState.Open,
        AssetClass.Forex => ForexState(now),
        _ => MarketState.Unknown,
    };

    /// <summary>
    /// Gets the state of the forex market, which is shut from Friday evening to Sunday evening in New York.
    /// </summary>
    /// <param name="now">
    /// The current time.
    /// </param>
    /// <returns>
    /// The state.
    /// </returns>
    private static MarketState ForexState(DateTimeOffset now)
    {
        if (NewYork is not { } zone)
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
}
