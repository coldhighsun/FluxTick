using FluxTick.Abstractions;
using FluxTick.Core.Domain;

namespace FluxTick.Core.Tests.MarketData;

/// <summary>
/// Tests for <see cref="QuoteHistoryCompactor"/>.
/// </summary>
public sealed class QuoteHistoryCompactorTests
{
    /// <summary>
    /// The time the tests measure age against.
    /// </summary>
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A retention long enough not to matter.
    /// </summary>
    private static readonly TimeSpan Year = TimeSpan.FromDays(365);

    /// <summary>
    /// Checks the bucket that each age is kept at.
    /// </summary>
    /// <param name="minutes">
    /// The age in minutes.
    /// </param>
    /// <param name="bucketSeconds">
    /// The expected bucket in seconds.
    /// </param>
    [Theory]
    [InlineData(-5, 10)]
    [InlineData(0, 10)]
    [InlineData(90, 10)]
    [InlineData(91, 300)]
    [InlineData(30 * 60, 300)]
    [InlineData((30 * 60) + 1, 1800)]
    [InlineData(6 * 24 * 60, 1800)]
    [InlineData((6 * 24 * 60) + 1, 14400)]
    [InlineData(32 * 24 * 60, 14400)]
    [InlineData((32 * 24 * 60) + 1, 86400)]
    public void BucketFor_Age_ReturnsTierBucket(int minutes, int bucketSeconds)
    {
        var bucket = QuoteHistoryCompactor.BucketFor(TimeSpan.FromMinutes(minutes));

        bucket.Should().Be(TimeSpan.FromSeconds(bucketSeconds));
    }

    /// <summary>
    /// Checks that prices in one fine bucket collapse to the last one.
    /// </summary>
    [Fact]
    public void Compact_PricesInSameFineBucket_KeepsLast()
    {
        var start = Now.AddMinutes(-10);
        var points = new[]
        {
            new PricePoint(start.AddSeconds(1), 1m),
            new PricePoint(start.AddSeconds(9), 2m),
            new PricePoint(start.AddSeconds(11), 3m),
        };

        var result = QuoteHistoryCompactor.Compact(points, Now, Year);

        result.Select(p => p.Price).Should().Equal(2m, 3m);
    }

    /// <summary>
    /// Checks that unordered input comes back oldest first.
    /// </summary>
    [Fact]
    public void Compact_UnorderedInput_ReturnsOldestFirst()
    {
        var points = new[]
        {
            new PricePoint(Now.AddMinutes(-1), 3m),
            new PricePoint(Now.AddMinutes(-3), 1m),
            new PricePoint(Now.AddMinutes(-2), 2m),
        };

        var result = QuoteHistoryCompactor.Compact(points, Now, Year);

        result.Select(p => p.Price).Should().Equal(1m, 2m, 3m);
    }

    /// <summary>
    /// Checks that a price older than the retention is dropped.
    /// </summary>
    [Fact]
    public void Compact_PriceOlderThanRetention_IsDropped()
    {
        var points = new[]
        {
            new PricePoint(Now.AddDays(-11), 1m),
            new PricePoint(Now.AddDays(-9), 2m),
        };

        var result = QuoteHistoryCompactor.Compact(points, Now, TimeSpan.FromDays(10));

        result.Select(p => p.Price).Should().Equal(2m);
    }

    /// <summary>
    /// Checks that a day-old price shares its 5-minute bucket with its neighbours, where the live tier would not.
    /// </summary>
    [Fact]
    public void Compact_PricesInSameCoarseBucket_KeepsLast()
    {
        var bucketStart = new DateTimeOffset(2026, 6, 14, 6, 0, 0, TimeSpan.Zero);
        var points = new[]
        {
            new PricePoint(bucketStart.AddSeconds(5), 1m),
            new PricePoint(bucketStart.AddSeconds(100), 2m),
            new PricePoint(bucketStart.AddSeconds(299), 3m),
            new PricePoint(bucketStart.AddSeconds(301), 4m),
        };

        var result = QuoteHistoryCompactor.Compact(points, Now, Year);

        result.Select(p => p.Price).Should().Equal(3m, 4m);
    }

    /// <summary>
    /// Checks that compacting twice gives what compacting once did.
    /// </summary>
    [Fact]
    public void Compact_AppliedTwice_IsIdempotent()
    {
        var points = Enumerable.Range(0, 5000)
            .Select(i => new PricePoint(Now.AddMinutes(-i * 7), i))
            .ToArray();

        var once = QuoteHistoryCompactor.Compact(points, Now, Year);
        var twice = QuoteHistoryCompactor.Compact(once, Now, Year);

        twice.Should().Equal(once);
    }

    /// <summary>
    /// Checks that compacting later, when the prices have aged into a coarser tier, still keeps one price per coarse bucket.
    /// </summary>
    [Fact]
    public void Compact_AfterAging_KeepsOnePricePerCoarseBucket()
    {
        var points = Enumerable.Range(0, 60)
            .Select(i => new PricePoint(Now.AddMinutes(-i), i))
            .ToArray();
        var fine = QuoteHistoryCompactor.Compact(points, Now, Year);

        var aged = QuoteHistoryCompactor.Compact(fine, Now.AddHours(3), Year);

        aged.Count.Should().BeLessThan(fine.Count);
        aged.GroupBy(p => p.Timestamp.UtcTicks / TimeSpan.FromMinutes(5).Ticks).Should().OnlyContain(g => g.Count() == 1);
    }
}
