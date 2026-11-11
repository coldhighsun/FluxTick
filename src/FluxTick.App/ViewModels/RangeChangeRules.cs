using FluxTick.Abstractions;
using FluxTick.Core.Application;

namespace FluxTick.App.ViewModels;

/// <summary>
/// The change over the range of the chart: the price now against the first price the chart holds for the window.
/// </summary>
/// <param name="Range">
/// The range the change was worked out for.
/// </param>
/// <param name="Baseline">
/// The first price of the window.
/// </param>
/// <param name="BaselineTime">
/// When the first price of the window was traded, which is after the start of the window for a source that holds less.
/// </param>
/// <param name="Change">
/// The price now minus <paramref name="Baseline"/>.
/// </param>
/// <param name="Percent">
/// The change in percent of <paramref name="Baseline"/>.
/// </param>
public readonly record struct RangeChange(HistoryRange Range, decimal Baseline, DateTimeOffset BaselineTime, decimal Change, decimal Percent);

/// <summary>
/// Decides when the change of a card follows the range of the chart and works it out.
/// </summary>
public static class RangeChangeRules
{
    /// <summary>
    /// Works out the change over the range the chart shows, which the window of the timeline tells. The last hour has no
    /// change of its own, and neither has a range whose history is not there (a source without it). While the history of a
    /// newly chosen range loads, the chart and this change stay with the range that was shown. A source that holds only part
    /// of the range (a year of 300 days, an instrument that is new) is measured from its first price, whose time
    /// <see cref="RangeChange.BaselineTime"/> tells; the chart shows nothing is known before it.
    /// </summary>
    /// <param name="sparkline">
    /// The prices of the chart, oldest first.
    /// </param>
    /// <param name="timeline">
    /// When the prices were traded.
    /// </param>
    /// <param name="price">
    /// The latest price.
    /// </param>
    /// <returns>
    /// The change, or <see langword="null"/> when the change against the reference price is the only one to show.
    /// </returns>
    public static RangeChange? Compute(IReadOnlyList<decimal> sparkline, SparklineTimeline? timeline, decimal price)
    {
        var range = ShownRange(timeline);
        if (range == HistoryRange.Hour
            || timeline is null
            || sparkline.Count == 0
            || timeline.Times.Count != sparkline.Count
            || sparkline[0] <= 0m)
        {
            return null;
        }

        var baseline = sparkline[0];
        var change = price - baseline;
        return new RangeChange(range, baseline, timeline.Times[0], change, change / baseline * 100m);
    }

    /// <summary>
    /// Tells which range the data of a chart is for, from the window of its timeline: the range whose history the timeline
    /// holds, else the last hour that the chart falls back to, so that the texts around the chart do not name a range the
    /// prices are not of.
    /// </summary>
    /// <param name="timeline">
    /// When the prices were traded, or <see langword="null"/> before the first snapshot.
    /// </param>
    /// <returns>
    /// The range of the history the timeline holds, otherwise <see cref="HistoryRange.Hour"/>.
    /// </returns>
    public static HistoryRange ShownRange(SparklineTimeline? timeline) =>
        timeline?.Range ?? HistoryRange.Hour;
}
