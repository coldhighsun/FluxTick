using FluxTick.Abstractions;
using FluxTick.App.Controls;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for <see cref="RangeChangeRules"/> and <see cref="SparklineHover"/>.
/// </summary>
public class RangeChangeRulesTests
{
    /// <summary>
    /// The end of the windows used by the tests.
    /// </summary>
    private static readonly DateTimeOffset AsOf = new(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Creates the timeline of a day whose prices were traded at the given hours before the end of it.
    /// </summary>
    /// <param name="coversRange">
    /// Whether the prices are the history of the range.
    /// </param>
    /// <param name="hoursAgo">
    /// How many hours before the end of the window each price was traded, oldest first.
    /// </param>
    /// <returns>
    /// The timeline.
    /// </returns>
    private static SparklineTimeline DayTimeline(bool coversRange, params int[] hoursAgo) =>
        new([.. hoursAgo.Select(h => AsOf.AddHours(-h))], AsOf, TimeSpan.FromHours(24), TimeSpan.FromMinutes(5), coversRange ? HistoryRange.Day : null);

    /// <summary>
    /// Verifies the change is worked out from the first price of the window when the history of the range is there.
    /// </summary>
    [Fact]
    public void Compute_RangeCovered_ChangesFromTheFirstPrice()
    {
        var timeline = DayTimeline(true, 24, 12, 0);

        var change = RangeChangeRules.Compute([100m, 110m, 120m], timeline, 125m);

        change.Should().NotBeNull();
        change!.Value.Baseline.Should().Be(100m);
        change.Value.BaselineTime.Should().Be(AsOf.AddHours(-24));
        change.Value.Change.Should().Be(25m);
        change.Value.Percent.Should().Be(25m);
    }

    /// <summary>
    /// Verifies the last hour keeps the change against the reference price of the source.
    /// </summary>
    [Fact]
    public void Compute_HourWindow_IsNull() =>
        RangeChangeRules.Compute([100m, 110m], new SparklineTimeline([AsOf.AddMinutes(-60), AsOf], AsOf, TimeSpan.FromHours(1), TimeSpan.FromMinutes(1), range: HistoryRange.Hour), 110m).Should().BeNull();

    /// <summary>
    /// Verifies a chart that only holds the last hour of a source without the range is not a change over the range.
    /// </summary>
    [Fact]
    public void Compute_RangeNotCovered_IsNull() =>
        RangeChangeRules.Compute([100m, 110m], DayTimeline(false, 1, 0), 110m).Should().BeNull();

    /// <summary>
    /// Verifies the change is of the range the window of the timeline tells, whatever range was chosen since, so that the
    /// card stays with the chart it shows while the history of a newly chosen range loads.
    /// </summary>
    [Fact]
    public void Compute_TimelineOfAWindow_ChangesOverThatRange()
    {
        var timeline = new SparklineTimeline([AsOf.AddDays(-365), AsOf], AsOf, TimeSpan.FromDays(365), TimeSpan.FromDays(1), range: HistoryRange.Year);

        var change = RangeChangeRules.Compute([100m, 110m], timeline, 110m);

        change.Should().NotBeNull();
        change!.Value.Range.Should().Be(HistoryRange.Year);
    }

    /// <summary>
    /// Verifies there is no change to work out from nothing, from a price that is not positive or from prices that have
    /// no times.
    /// </summary>
    [Fact]
    public void Compute_NothingToMeasureFrom_IsNull()
    {
        RangeChangeRules.Compute([], DayTimeline(true), 110m).Should().BeNull();
        RangeChangeRules.Compute([0m, 5m], DayTimeline(true, 24, 0), 5m).Should().BeNull();
        RangeChangeRules.Compute([100m, 110m], DayTimeline(true, 24), 110m).Should().BeNull();
        RangeChangeRules.Compute([100m], null, 110m).Should().BeNull();
    }

    /// <summary>
    /// Verifies a first price a night or a weekend after the start of the window still stands for the start.
    /// </summary>
    [Fact]
    public void Compute_FirstPriceShortlyAfterWindowStart_Changes() =>
        RangeChangeRules.Compute([100m, 110m], DayTimeline(true, 22, 0), 110m).Should().NotBeNull();

    /// <summary>
    /// Verifies a range whose history starts well after the window, such as a year of which only part is held, is measured
    /// from its first price and tells when that was.
    /// </summary>
    [Fact]
    public void Compute_FirstPriceLongAfterWindowStart_ChangesFromIt()
    {
        var change = RangeChangeRules.Compute([100m, 110m], DayTimeline(true, 12, 0), 110m);

        change.Should().NotBeNull();
        change!.Value.Baseline.Should().Be(100m);
        change.Value.BaselineTime.Should().Be(AsOf.AddHours(-12));
        change.Value.Percent.Should().Be(10m);
    }

    /// <summary>
    /// Verifies the texts about the chart name the range its timeline tells, and the last hour for a chart
    /// that holds no history of a range.
    /// </summary>
    [Fact]
    public void ShownRange_HistoryOrFallback_NamesTheDataShown()
    {
        RangeChangeRules.ShownRange(DayTimeline(true, 24, 0)).Should().Be(HistoryRange.Day);
        RangeChangeRules.ShownRange(DayTimeline(false, 1, 0)).Should().Be(HistoryRange.Hour);
        RangeChangeRules.ShownRange(null).Should().Be(HistoryRange.Hour);
        RangeChangeRules.ShownRange(new SparklineTimeline([], AsOf, TimeSpan.FromDays(365), TimeSpan.FromDays(1), range: HistoryRange.Year)).Should().Be(HistoryRange.Year);
    }

    /// <summary>
    /// Verifies the pointer finds the price nearest to it along the time axis.
    /// </summary>
    [Fact]
    public void IndexAt_Pointer_FindsTheNearestPrice()
    {
        var window = TimeSpan.FromHours(24);
        DateTimeOffset[] times = [AsOf.AddHours(-24), AsOf.AddHours(-12), AsOf];

        // The chart is 100 wide with 3 around it: the prices are at 3, 53 and 103.
        SparklineHover.IndexAt(0, 100, 3, times, AsOf, window).Should().Be(0);
        SparklineHover.IndexAt(40, 100, 3, times, AsOf, window).Should().Be(1);
        SparklineHover.IndexAt(90, 100, 3, times, AsOf, window).Should().Be(2);
        SparklineHover.IndexAt(500, 100, 3, times, AsOf, window).Should().Be(2);
    }

    /// <summary>
    /// Verifies a chart without prices has nothing under the pointer.
    /// </summary>
    [Fact]
    public void IndexAt_NoPrices_IsMinusOne() =>
        SparklineHover.IndexAt(10, 100, 3, [], AsOf, TimeSpan.FromHours(1)).Should().Be(-1);

    /// <summary>
    /// Verifies the time is written as the clock for today, with the date for another day, and as a date for a year.
    /// </summary>
    [Fact]
    public void TimeLabel_Range_PicksTheFormat()
    {
        var zone = TimeZoneInfo.Utc;

        SparklineHover.TimeLabel(AsOf.AddHours(-2), HistoryRange.Day, AsOf, zone).Should().Be("10:00");
        SparklineHover.TimeLabel(AsOf.AddHours(-20), HistoryRange.Day, AsOf, zone).Should().Be("01-09 16:00");
        SparklineHover.TimeLabel(AsOf.AddHours(-2), HistoryRange.FiveDays, AsOf, zone).Should().Be("01-10 10:00");
        SparklineHover.TimeLabel(AsOf.AddHours(-2), HistoryRange.Month, AsOf, zone).Should().Be("01-10");
        SparklineHover.TimeLabel(AsOf.AddHours(-2), HistoryRange.HalfYear, AsOf, zone).Should().Be("2026-01-10");
        SparklineHover.TimeLabel(AsOf.AddHours(-2), HistoryRange.Year, AsOf, zone).Should().Be("2026-01-10");
    }

    /// <summary>
    /// Verifies a point of a year, whose bucket starts at midnight UTC, keeps its date in a zone west of UTC.
    /// </summary>
    [Fact]
    public void TimeLabel_YearWestOfUtc_KeepsTheDateOfTheBucket()
    {
        var newYork = TimeZoneInfo.CreateCustomTimeZone("Test/West", TimeSpan.FromHours(-5), "West", "West");
        var bucket = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);

        SparklineHover.TimeLabel(bucket, HistoryRange.Year, AsOf, newYork).Should().Be("2026-11-03");
    }
}
