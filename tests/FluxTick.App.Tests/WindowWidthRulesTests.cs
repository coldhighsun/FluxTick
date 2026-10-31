using FluxTick.App.Views;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for <see cref="WindowWidthRules"/>.
/// </summary>
public class WindowWidthRulesTests
{
    /// <summary>
    /// Verifies the list layout has a fixed width.
    /// </summary>
    [Fact]
    public void For_ListLayout_IsFixed()
    {
        WindowWidthRules.For(WatchlistLayout.List).Should().Be(new WidthRule(360, 360));
    }

    /// <summary>
    /// Verifies the card layout is free above its minimum.
    /// </summary>
    [Fact]
    public void For_CardLayout_IsFreeAboveMinimum()
    {
        WindowWidthRules.For(WatchlistLayout.Cards).Should().Be(new WidthRule(328, double.PositiveInfinity));
    }

    /// <summary>
    /// Verifies the list layout ignores the remembered card width.
    /// </summary>
    [Fact]
    public void TargetWidth_ListLayout_IgnoresCardWidth()
    {
        WindowWidthRules.TargetWidth(WatchlistLayout.List, 555).Should().Be(360);
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
    [InlineData(328, 328)]
    [InlineData(100, 328)]
    public void TargetWidth_CardLayout_UsesRememberedWidthAboveMinimum(double remembered, double expected)
    {
        WindowWidthRules.TargetWidth(WatchlistLayout.Cards, remembered).Should().Be(expected);
    }

    /// <summary>
    /// Verifies the anchor follows the docked edge: left keeps the left edge, right the right edge, the others the center.
    /// </summary>
    /// <param name="edge">
    /// The name of the docked edge; the type is internal to the app.
    /// </param>
    /// <param name="expected">
    /// The expected left edge.
    /// </param>
    [Theory]
    [InlineData("Left", 1000)]
    [InlineData("Right", 1170)]
    [InlineData("Top", 1085)]
    [InlineData("None", 1085)]
    public void LeftForDock_Edge_KeepsItsAnchor(string edge, int expected)
    {
        WindowWidthRules.LeftForDock(1000, 510, 340, Enum.Parse<DockEdge>(edge)).Should().Be(expected);
    }

    /// <summary>
    /// Verifies a narrower window moves its left edge right and a wider one moves it left, so the center stays.
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
    [InlineData(1000, 340, 280, 1030)]
    [InlineData(1030, 280, 340, 1000)]
    [InlineData(1000, 510, 340, 1085)]
    [InlineData(500, 340, 340, 500)]
    public void LeftForCenter_WidthChanges_KeepsCenter(int left, double oldWidth, double newWidth, int expected)
    {
        var result = WindowWidthRules.LeftForCenter(left, oldWidth, newWidth);

        result.Should().Be(expected);
        (result + (newWidth / 2)).Should().Be(left + (oldWidth / 2));
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
        WindowWidthRules.Animate(340, 280, 0).Should().Be(340);
        WindowWidthRules.Animate(340, 280, 1).Should().Be(280);
        WindowWidthRules.Animate(340, 280, -1).Should().Be(340);
        WindowWidthRules.Animate(340, 280, 2).Should().Be(280);

        for (var step = 0; step <= 20; step++)
        {
            WindowWidthRules.Animate(340, 280, step / 20.0).Should().BeInRange(280, 340);
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

    /// <summary>
    /// Verifies that the minimum width of the card layout fits one large card exactly, with the same space left and right.
    /// </summary>
    [Fact]
    public void CardsMinWidth_OneLargeCard_FitsExactlyWithEqualSides()
    {
        var space = CardLayout.ListPadding.Left + CardLayout.ListPadding.Right + (2 * CardLayout.WindowFrame);

        CardLayout.ListPadding.Left.Should().Be(CardLayout.ListPadding.Right);
        WindowWidthRules.CardsMinWidth.Should().Be(CardLayout.LargeCardWidth + (2 * CardLayout.CardMargin) + space);
        WindowWidthRules.CardsMinWidth.Should().Be(328);
    }
}
