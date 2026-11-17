using FluxTick.Abstractions;
using FluxTick.Core.Application;
using FluxTick.Core.Domain;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for <see cref="SparklineSeries"/>.
/// </summary>
public class SparklineSeriesTests
{
    /// <summary>
    /// A fixed point in time aligned to a minute boundary.
    /// </summary>
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies prices within one bucket replace each other.
    /// </summary>
    [Fact]
    public void Add_WithinSameBucket_ReplacesLastPoint()
    {
        var series = new SparklineSeries(10, TimeSpan.FromMinutes(1));

        series.Add(1m, T0);
        series.Add(2m, T0.AddSeconds(30));

        series.Snapshot(T0).Should().Equal(2m);
    }

    /// <summary>
    /// Verifies a later bucket appends a point.
    /// </summary>
    [Fact]
    public void Add_InNextBucket_AppendsPoint()
    {
        var series = new SparklineSeries(10, TimeSpan.FromMinutes(1));

        series.Add(1m, T0);
        series.Add(2m, T0.AddMinutes(1));

        series.Snapshot(T0.AddMinutes(1)).Should().Equal(1m, 2m);
    }

    /// <summary>
    /// Verifies the oldest points are dropped once the capacity is exceeded.
    /// </summary>
    [Fact]
    public void Add_BeyondCapacity_DropsOldest()
    {
        var series = new SparklineSeries(3, TimeSpan.FromMinutes(1));

        for (var i = 0; i < 5; i++)
        {
            series.Add(i, T0.AddMinutes(i));
        }

        series.Snapshot(T0.AddMinutes(4)).Should().Equal(2m, 3m, 4m);
    }

    /// <summary>
    /// Verifies out-of-order prices from an older bucket are ignored.
    /// </summary>
    [Fact]
    public void Add_OlderThanNewestBucket_IsIgnored()
    {
        var series = new SparklineSeries(10, TimeSpan.FromMinutes(1));
        series.Add(2m, T0.AddMinutes(1));

        series.Add(1m, T0);

        series.Snapshot(T0.AddMinutes(1)).Should().Equal(2m);
    }

    /// <summary>
    /// Verifies seeding sorts the history, keeps the last price per bucket and continues from the newest sample.
    /// </summary>
    [Fact]
    public void Seed_WithUnorderedHistory_SortsAndBuckets()
    {
        var series = new SparklineSeries(10, TimeSpan.FromMinutes(1));

        series.Seed(
        [
            new PricePoint(T0.AddMinutes(1), 3m),
            new PricePoint(T0, 1m),
            new PricePoint(T0.AddSeconds(20), 2m),
        ]);
        series.Add(4m, T0.AddMinutes(1).AddSeconds(10));

        series.Snapshot(T0.AddMinutes(1)).Should().Equal(2m, 4m);
    }

    /// <summary>
    /// Verifies points older than the window are left out, and the rest is kept.
    /// </summary>
    [Fact]
    public void Snapshot_WithPartOfThePointsOutsideTheWindow_ReturnsOnlyTheRecentOnes()
    {
        var series = new SparklineSeries(10, TimeSpan.FromMinutes(1));
        for (var i = 0; i < 10; i++)
        {
            series.Add(i, T0.AddMinutes(i));
        }

        var now = T0.AddMinutes(12);

        series.Snapshot(now).Should().Equal(3m, 4m, 5m, 6m, 7m, 8m, 9m);
        series.Snapshot(now).Should().HaveCount(7);
    }

    /// <summary>
    /// Verifies the series is empty once every point is older than the window.
    /// </summary>
    [Fact]
    public void Snapshot_WithEveryPointOutsideTheWindow_IsEmpty()
    {
        var series = new SparklineSeries(10, TimeSpan.FromMinutes(1));
        series.Add(1m, T0);
        series.Add(2m, T0.AddMinutes(1));

        var now = T0.AddMinutes(30);

        series.Snapshot(now).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a point after the window has slid past it does not come back and new points start the series again.
    /// </summary>
    [Fact]
    public void Add_AfterAllPointsExpired_StartsAFreshWindow()
    {
        var series = new SparklineSeries(10, TimeSpan.FromMinutes(1));
        series.Add(1m, T0);

        series.Add(5m, T0.AddMinutes(60));

        series.Snapshot(T0.AddMinutes(60)).Should().Equal(5m);
    }

    /// <summary>
    /// Verifies the times are those of the prices of the window, and the window is the capacity times the bucket.
    /// </summary>
    [Fact]
    public void Times_MatchThePricesOfTheWindow()
    {
        var series = new SparklineSeries(10, TimeSpan.FromMinutes(1));
        for (var i = 0; i < 10; i++)
        {
            series.Add(i, T0.AddMinutes(i).AddSeconds(20));
        }

        var now = T0.AddMinutes(12);

        series.Times(now).Should().Equal(T0.AddMinutes(3), T0.AddMinutes(4), T0.AddMinutes(5), T0.AddMinutes(6), T0.AddMinutes(7), T0.AddMinutes(8), T0.AddMinutes(9));
        series.Times(now).Should().HaveSameCount(series.Snapshot(now));
        series.Window.Should().Be(TimeSpan.FromMinutes(10));
    }

    /// <summary>
    /// Verifies the times are made once while the buckets and the window start stay as they are, and again when a bucket
    /// is added or the window moves on.
    /// </summary>
    [Fact]
    public void Times_AreReusedUntilTheBucketsOrTheWindowChange()
    {
        var series = new SparklineSeries(10, TimeSpan.FromMinutes(1));
        series.Add(1m, T0);
        series.Add(2m, T0.AddMinutes(1));

        var first = series.Times(T0.AddMinutes(1));
        first.Should().NotBeAssignableTo<DateTimeOffset[]>("the list is shared, so a cast must not give access to it");
        series.Add(3m, T0.AddMinutes(1).AddSeconds(30));

        series.Times(T0.AddMinutes(1).AddSeconds(40)).Should().BeSameAs(first, "a price in the newest bucket changes no start");

        series.Add(4m, T0.AddMinutes(2));
        var afterNewBucket = series.Times(T0.AddMinutes(2));

        afterNewBucket.Should().NotBeSameAs(first).And.HaveCount(3);

        var afterWindowMoved = series.Times(T0.AddMinutes(11));

        afterWindowMoved.Should().NotBeSameAs(afterNewBucket).And.HaveCount(1);
    }

    /// <summary>
    /// Verifies the distance that makes a gap follows the latest distance between the prices, and nothing is a gap
    /// while there are not two prices to take one from.
    /// </summary>
    [Fact]
    public void SparklineTimeline_GapAfter_FollowsTheLatestDistanceBetweenThePrices()
    {
        static SparklineTimeline Of(params DateTimeOffset[] times) => new(times, T0, TimeSpan.FromMinutes(60));
        var everyMinute = Enumerable.Range(0, 10).Select(i => T0.AddMinutes(i)).ToArray();
        var everyFiveMinutes = Enumerable.Range(0, 10).Select(i => T0.AddMinutes(i * 5)).ToArray();
        DateTimeOffset[] oneLongStretch = [T0, T0.AddMinutes(1), T0.AddMinutes(2), T0.AddMinutes(8), T0.AddMinutes(9), T0.AddMinutes(10)];
        DateTimeOffset[] historyThenSlowPolls = [.. everyMinute, T0.AddMinutes(14), T0.AddMinutes(19)];
        DateTimeOffset[] historyThenOnePoll = [.. everyMinute, T0.AddMinutes(14)];

        Of(everyMinute).GapAfter.Should().Be(TimeSpan.FromMinutes(2.5));
        Of(everyFiveMinutes).GapAfter.Should().Be(TimeSpan.FromMinutes(12.5));
        Of(oneLongStretch).GapAfter.Should().Be(TimeSpan.FromMinutes(2.5), "the one long stretch does not raise the bar for itself");
        Of(historyThenSlowPolls).GapAfter.Should().Be(TimeSpan.FromMinutes(12.5), "the pace changed: the polls are the pace now");
        Of(historyThenOnePoll).GapAfter.Should().Be(TimeSpan.FromMinutes(2.5), "one poll does not tell the new pace yet");
        Of(T0, T0.AddMinutes(10)).GapAfter.Should().Be(TimeSpan.FromMinutes(25));
        Of(T0).GapAfter.Should().Be(TimeSpan.MaxValue);
        Of().GapAfter.Should().Be(TimeSpan.MaxValue);
    }

    /// <summary>
    /// Verifies a stretch is a gap only beyond the distance of the timeline.
    /// </summary>
    [Fact]
    public void SparklineTimeline_IsGap_OnlyBeyondGapAfter()
    {
        var everyMinute = new SparklineTimeline([T0, T0.AddMinutes(1), T0.AddMinutes(2)], T0, TimeSpan.FromMinutes(60));
        var unknown = new SparklineTimeline([T0], T0, TimeSpan.FromMinutes(60));

        everyMinute.IsGap(T0, T0.AddMinutes(2)).Should().BeFalse();
        everyMinute.IsGap(T0, T0.AddMinutes(3)).Should().BeTrue();
        unknown.IsGap(T0, T0.AddDays(30)).Should().BeFalse();
    }

    /// <summary>
    /// Verifies invalid construction arguments are rejected.
    /// </summary>
    [Fact]
    public void Constructor_WithInvalidArguments_Throws()
    {
        var tooSmall = () => new SparklineSeries(1, TimeSpan.FromMinutes(1));
        var noBucket = () => new SparklineSeries(10, TimeSpan.Zero);

        tooSmall.Should().Throw<ArgumentOutOfRangeException>();
        noBucket.Should().Throw<ArgumentOutOfRangeException>();
    }
}
