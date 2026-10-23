using FluxTick.App.Views;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for <see cref="WindowWidthRules"/>.
/// </summary>
public class WindowWidthRulesTests
{
    /// <summary>
    /// Verifies the list layout has a fixed width that depends only on whether the sparklines are shown.
    /// </summary>
    [Fact]
    public void For_ListLayout_IsFixedAndDependsOnCompact()
    {
        WindowWidthRules.For(WatchlistLayout.List, compact: false).Should().Be(new WidthRule(340, 340));
        WindowWidthRules.For(WatchlistLayout.List, compact: true).Should().Be(new WidthRule(260, 260));
    }

    /// <summary>
    /// Verifies the card layout is free above its minimum, whether compact mode is on or not.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void For_CardLayout_IsFreeAboveMinimum(bool compact)
    {
        WindowWidthRules.For(WatchlistLayout.Cards, compact).Should().Be(new WidthRule(300, double.PositiveInfinity));
    }

    /// <summary>
    /// Verifies the list layout ignores the remembered card width.
    /// </summary>
    [Theory]
    [InlineData(false, 340)]
    [InlineData(true, 260)]
    public void TargetWidth_ListLayout_IgnoresCardWidth(bool compact, double expected)
    {
        WindowWidthRules.TargetWidth(WatchlistLayout.List, compact, 555).Should().Be(expected);
    }

    /// <summary>
    /// Verifies the card layout uses the remembered width, but never less than its minimum.
    /// </summary>
    /// <param name="remembered">
    /// The remembered card width.
    /// </param>
    /// <param name="expected">
    /// The expected width.
    /// </param>
    [Theory]
    [InlineData(555, 555)]
    [InlineData(300, 300)]
    [InlineData(100, 300)]
    public void TargetWidth_CardLayout_UsesRememberedWidthAboveMinimum(double remembered, double expected)
    {
        WindowWidthRules.TargetWidth(WatchlistLayout.Cards, compact: false, remembered).Should().Be(expected);
    }

    /// <summary>
    /// Verifies a narrower window moves its left edge right and a wider one moves it left, so the right edge stays.
    /// </summary>
    /// <param name="left">
    /// The current left edge.
    /// </param>
    /// <param name="oldWidth">
    /// The current width.
    /// </param>
    /// <param name="newWidth">
    /// The new width.
    /// </param>
    /// <param name="expected">
    /// The expected left edge.
    /// </param>
    [Theory]
    [InlineData(1000, 340, 260, 1080)]
    [InlineData(1080, 260, 340, 1000)]
    [InlineData(1000, 510, 340, 1170)]
    [InlineData(500, 340, 340, 500)]
    public void LeftForRightEdge_WidthChanges_KeepsRightEdge(int left, double oldWidth, double newWidth, int expected)
    {
        var result = WindowWidthRules.LeftForRightEdge(left, oldWidth, newWidth);

        result.Should().Be(expected);
        (result + newWidth).Should().Be(left + oldWidth);
    }

    /// <summary>
    /// Verifies a window is moved only as far as needed to lie inside the work area.
    /// </summary>
    /// <param name="left">
    /// The left edge of the window.
    /// </param>
    /// <param name="width">
    /// The width of the window.
    /// </param>
    /// <param name="expected">
    /// The expected left edge.
    /// </param>
    [Theory]
    [InlineData(100, 340, 100)]
    [InlineData(-50, 340, 0)]
    [InlineData(1700, 340, 1580)]
    [InlineData(0, 2500, 0)]
    public void ClampLeft_WindowOutsideWorkArea_IsMovedInside(int left, double width, int expected)
    {
        WindowWidthRules.ClampLeft(left, width, areaLeft: 0, areaRight: 1920).Should().Be(expected);
    }

    /// <summary>
    /// Verifies the animation starts at the first value, ends at the second and never overshoots.
    /// </summary>
    [Fact]
    public void Animate_Progress_RunsFromStartToEndWithoutOvershoot()
    {
        WindowWidthRules.Animate(340, 260, 0).Should().Be(340);
        WindowWidthRules.Animate(340, 260, 1).Should().Be(260);
        WindowWidthRules.Animate(340, 260, -1).Should().Be(340);
        WindowWidthRules.Animate(340, 260, 2).Should().Be(260);

        for (var step = 0; step <= 20; step++)
        {
            WindowWidthRules.Animate(340, 260, step / 20.0).Should().BeInRange(260, 340);
        }
    }

    /// <summary>
    /// Verifies the animation is eased out: it covers more than half of the way in the first half of the time, and
    /// each step is smaller than the one before.
    /// </summary>
    [Fact]
    public void Animate_Progress_IsEasedOut()
    {
        WindowWidthRules.Animate(0, 100, 0.5).Should().BeGreaterThan(50);

        var previous = 0.0;
        var previousStep = double.MaxValue;
        for (var step = 1; step <= 10; step++)
        {
            var value = WindowWidthRules.Animate(0, 100, step / 10.0);
            (value - previous).Should().BeLessThan(previousStep);
            previousStep = value - previous;
            previous = value;
        }
    }
}
