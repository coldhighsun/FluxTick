using FluxTick.Abstractions;
using FluxTick.Core.Domain;

namespace FluxTick.Core.Tests.MarketData;

/// <summary>
/// Tests for <see cref="HistoryCoverage"/>.
/// </summary>
public sealed class HistoryCoverageTests
{
    /// <summary>
    /// The end of the time range the tests check.
    /// </summary>
    private static readonly DateTimeOffset To = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The start of the time range the tests check.
    /// </summary>
    private static readonly DateTimeOffset From = To.AddHours(-1);

    /// <summary>
    /// The longest time without a price that is no hole.
    /// </summary>
    private static readonly TimeSpan Gap = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Verifies the gap of each bucket: twice the bucket, not less than ten minutes.
    /// </summary>
    /// <param name="bucketMinutes">
    /// The bucket in minutes.
    /// </param>
    /// <param name="expectedMinutes">
    /// The expected gap in minutes.
    /// </param>
    [Theory]
    [InlineData(1, 10)]
    [InlineData(5, 10)]
    [InlineData(30, 60)]
    [InlineData(240, 480)]
    public void GapThreshold_Bucket_IsTwiceTheBucketButNotUnderTenMinutes(int bucketMinutes, int expectedMinutes)
    {
        var gap = HistoryCoverage.GapThreshold(TimeSpan.FromMinutes(bucketMinutes));

        gap.Should().Be(TimeSpan.FromMinutes(expectedMinutes));
    }

    /// <summary>
    /// Verifies that prices that reach from the start to the end without a hole are complete.
    /// </summary>
    [Fact]
    public void IsComplete_PricesAcrossTheWholeRange_ReturnsTrue()
    {
        var points = Enumerable.Range(0, 12).Select(i => new PricePoint(From.AddMinutes(5 + (i * 5)), i)).ToArray();

        HistoryCoverage.IsComplete(points, From, To, Gap).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that one price, or none, is never enough for a chart.
    /// </summary>
    /// <param name="count">
    /// The number of prices.
    /// </param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void IsComplete_FewerThanTwoPrices_ReturnsFalse(int count)
    {
        var points = Enumerable.Range(0, count).Select(i => new PricePoint(To.AddMinutes(-1), i)).ToArray();

        HistoryCoverage.IsComplete(points, From, To, Gap).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a range that starts before the first price is not complete.
    /// </summary>
    [Fact]
    public void IsComplete_FirstPriceTooLate_ReturnsFalse()
    {
        PricePoint[] points = [new PricePoint(From.AddMinutes(11), 1m), new PricePoint(To, 2m)];

        HistoryCoverage.IsComplete(points, From, To, Gap).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a range that ends long after the last price is not complete.
    /// </summary>
    [Fact]
    public void IsComplete_LastPriceTooEarly_ReturnsFalse()
    {
        PricePoint[] points = [new PricePoint(From, 1m), new PricePoint(To.AddMinutes(-11), 2m)];

        HistoryCoverage.IsComplete(points, From, To, Gap).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a hole between two prices makes the range incomplete, and that a gap of exactly the threshold does not.
    /// </summary>
    /// <param name="holeMinutes">
    /// The time between the two middle prices in minutes.
    /// </param>
    /// <param name="expected">
    /// Whether the range is complete.
    /// </param>
    [Theory]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void IsComplete_HoleInTheMiddle_IsJudgedByTheThreshold(int holeMinutes, bool expected)
    {
        var points = new List<PricePoint> { new(From, 1m), new(From.AddMinutes(5), 2m) };
        var next = From.AddMinutes(5 + holeMinutes);
        while (next <= To)
        {
            points.Add(new PricePoint(next, 3m));
            next = next.AddMinutes(5);
        }

        HistoryCoverage.IsComplete(points, From, To, Gap).Should().Be(expected);
    }
}
