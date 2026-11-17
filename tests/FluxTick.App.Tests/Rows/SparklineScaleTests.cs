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

    /// <summary>
    /// Verifies the ticks are the multiples of 5% inside the chart, placed by their price.
    /// </summary>
    [Fact]
    public void PercentTicks_SmallRange_MarksMultiplesOfFive()
    {
        var ticks = SparklineScale.PercentTicks(96, 112, 100);

        ticks.Select(t => t.Percent).Should().Equal(0d, 5d, 10d);
        ticks[0].Position.Should().BeApproximately(0.25, 1e-9);
        ticks[1].Position.Should().BeApproximately(0.5625, 1e-9);
        ticks[2].Position.Should().BeApproximately(0.875, 1e-9);
    }

    /// <summary>
    /// Verifies a range narrower than a step has only the 0% tick, as the step is never below 5%.
    /// </summary>
    [Fact]
    public void PercentTicks_RangeInsideOneStep_HasOnlyZero() =>
        SparklineScale.PercentTicks(99, 101, 100).Select(t => t.Percent).Should().Equal(0d);

    /// <summary>
    /// Verifies the step grows with the range so that no more than four ticks remain.
    /// </summary>
    [Fact]
    public void PercentTicks_WideRange_GrowsTheStep()
    {
        var ticks = SparklineScale.PercentTicks(50, 300, 100);

        ticks.Select(t => t.Percent).Should().Equal(0d, 100d, 200d);
        SparklineScale.PercentTicks(90, 130, 100).Select(t => t.Percent).Should().Equal(0d, 10d, 20d, 30d);
    }

    /// <summary>
    /// Verifies the step between two ticks is at least the minimum.
    /// </summary>
    [Fact]
    public void PercentTicks_AnyRange_StepsAtLeastFive()
    {
        var percents = SparklineScale.PercentTicks(80, 125, 100).Select(t => t.Percent).ToArray();

        percents.Zip(percents.Skip(1), (a, b) => b - a).Should().OnlyContain(step => step >= 5);
    }

    /// <summary>
    /// Verifies there are no ticks when there is nothing to measure from or no span to put them in.
    /// </summary>
    /// <param name="bottom">
    /// The price at the bottom of the chart.
    /// </param>
    /// <param name="top">
    /// The price at the top of the chart.
    /// </param>
    /// <param name="baseline">
    /// The price of 0%.
    /// </param>
    [Theory]
    [InlineData(90, 110, 0)]
    [InlineData(90, 110, -5)]
    [InlineData(100, 100, 100)]
    public void PercentTicks_NothingToMeasure_IsEmpty(double bottom, double top, double baseline) =>
        SparklineScale.PercentTicks(bottom, top, baseline).Should().BeEmpty();

    /// <summary>
    /// Verifies the labels carry a sign for a change and none for zero.
    /// </summary>
    /// <param name="percent">
    /// The change in percent.
    /// </param>
    /// <param name="expected">
    /// The label.
    /// </param>
    [Theory]
    [InlineData(5, "+5%")]
    [InlineData(0, "0%")]
    [InlineData(-12.5, "-12.5%")]
    [InlineData(100, "+100%")]
    public void PercentLabel_Value_IsSigned(double percent, string expected) =>
        SparklineScale.PercentLabel(percent).Should().Be(expected);
}
