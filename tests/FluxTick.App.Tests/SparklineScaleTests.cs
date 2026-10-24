using FluxTick.App.Controls;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for <see cref="SparklineScale"/>.
/// </summary>
public class SparklineScaleTests
{
    /// <summary>
    /// Verifies a chart without alert levels shows exactly the range of the prices.
    /// </summary>
    [Fact]
    public void Range_WithoutLevels_IsTheRangeOfThePrices() =>
        SparklineScale.Range(100, 110, []).Should().Be((100, 110));

    /// <summary>
    /// Verifies a level close to the prices makes the chart grow to include it, on either side.
    /// </summary>
    [Fact]
    public void Range_LevelsNearThePrices_GrowTheChart() =>
        SparklineScale.Range(100, 110, [115, 95]).Should().Be((95, 115));

    /// <summary>
    /// Verifies a level inside the range changes nothing.
    /// </summary>
    [Fact]
    public void Range_LevelInsideThePrices_ChangesNothing() =>
        SparklineScale.Range(100, 110, [105]).Should().Be((100, 110));

    /// <summary>
    /// Verifies a level further away than the span of the prices is left out, so the line is not flattened.
    /// </summary>
    [Fact]
    public void Range_LevelTooFarAway_IsLeftOut() =>
        SparklineScale.Range(100, 110, [125, 70]).Should().Be((100, 110));

    /// <summary>
    /// Verifies a flat series reaches a share of its price.
    /// </summary>
    [Fact]
    public void Range_FlatSeries_ReachesTwoPercentOfThePrice()
    {
        SparklineScale.Range(100, 100, [101.5]).Should().Be((100, 101.5));
        SparklineScale.Range(100, 100, [103]).Should().Be((100, 100));
    }

    /// <summary>
    /// Verifies a price is placed between the bottom and the top, and one outside is moved to the edge and marked.
    /// </summary>
    [Fact]
    public void Place_InsideAndOutsideTheChart()
    {
        SparklineScale.Place(105, 100, 110).Should().Be((0.5, false));
        SparklineScale.Place(120, 100, 110).Should().Be((1, true));
        SparklineScale.Place(90, 100, 110).Should().Be((0, true));
    }

    /// <summary>
    /// Verifies a chart without a span puts everything in the middle.
    /// </summary>
    [Fact]
    public void Place_ChartWithoutSpan_IsTheMiddle() =>
        SparklineScale.Place(100, 100, 100).Should().Be((0.5, false));

    /// <summary>
    /// The end of the window used by the time tests.
    /// </summary>
    private static readonly DateTimeOffset AsOf = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies the start of the window is at the left, its end at the right, and times outside it sit at the edges.
    /// </summary>
    [Fact]
    public void TimeX_PlacesTimesAlongTheWindow()
    {
        var window = TimeSpan.FromMinutes(60);

        SparklineScale.TimeX(AsOf, AsOf, window).Should().Be(1);
        SparklineScale.TimeX(AsOf.AddMinutes(-60), AsOf, window).Should().Be(0);
        SparklineScale.TimeX(AsOf.AddMinutes(-30), AsOf, window).Should().Be(0.5);
        SparklineScale.TimeX(AsOf.AddMinutes(-90), AsOf, window).Should().Be(0);
        SparklineScale.TimeX(AsOf.AddMinutes(5), AsOf, window).Should().Be(1);
    }
}
