using FluxTick.Abstractions;
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

        series.Snapshot().Should().Equal(2m);
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

        series.Snapshot().Should().Equal(1m, 2m);
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

        series.Snapshot().Should().Equal(2m, 3m, 4m);
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

        series.Snapshot().Should().Equal(2m);
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

        series.Snapshot().Should().Equal(2m, 4m);
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
