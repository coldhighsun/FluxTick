using FluxTick.Abstractions;

namespace FluxTick.Providers.Common;

/// <summary>
/// How far back a source needs to look to seed a sparkline. The sparkline of the app shows the last 60 minutes only (60
/// points of a minute each), so earlier prices are never shown and asking for them is wasted traffic.
/// </summary>
public static class HistoryWindow
{
    /// <summary>
    /// Gets the span to request: the 60 minutes of the sparkline plus five more, so that the edge of the window and the
    /// delay of a source do not leave a gap.
    /// </summary>
    public static TimeSpan Span { get; } = TimeSpan.FromMinutes(65);

    /// <summary>
    /// Gets how far back a source that asks by time looks for the last session when the last day holds no prices, as for a
    /// market shut over a weekend. It reaches over a weekend with a holiday next to it.
    /// </summary>
    public static TimeSpan LastSessionSpan { get; } = TimeSpan.FromDays(4);

    /// <summary>
    /// Gets how long after its last candle a market counts as shut, for <see cref="MissesLastSession"/>.
    /// </summary>
    private static TimeSpan SessionOverAfter { get; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Gets how close to the start of the window the first candle has to be for the window to have cut off what came before
    /// it, for <see cref="MissesLastSession"/>.
    /// </summary>
    private static TimeSpan WindowEdge { get; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Tells whether the candles of the last day miss part of the last session, so that the source has to ask again over
    /// <see cref="LastSessionSpan"/>: there are none, or the market has shut since and the start of the window cut the
    /// session. The chart ends with the last session once it is older than the window, so that must be whole.
    /// </summary>
    /// <param name="day">
    /// The candles of the last day, oldest first.
    /// </param>
    /// <param name="now">
    /// The current time, which is the end of the window.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the source has to ask further back.
    /// </returns>
    public static bool MissesLastSession(IReadOnlyList<PricePoint> day, DateTimeOffset now)
    {
        if (day.Count < 2)
        {
            return true;
        }

        var windowStart = now - HistoryRangeSpec.For(HistoryRange.Day).Window;
        return day[0].Timestamp - windowStart <= WindowEdge && now - day[^1].Timestamp >= SessionOverAfter;
    }
}
