using Avalonia;
using FluxTick.App.Views;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the rules that hide the main window at a screen edge.
/// </summary>
public sealed class EdgeDockRulesTests
{
    /// <summary>
    /// The work area of the screen used by the tests.
    /// </summary>
    private static readonly PixelRect Area = new(0, 40, 1920, 1000);

    /// <summary>
    /// A window within the snap distance of an edge is docked to it, and one in the middle is not.
    /// </summary>
    /// <param name="x">
    /// The left edge of the window.
    /// </param>
    /// <param name="y">
    /// The top edge of the window.
    /// </param>
    /// <param name="expected">
    /// The name of the expected edge.
    /// </param>
    [Theory]
    [InlineData(5, 300, "Left")]
    [InlineData(-20, 300, "Left")]
    [InlineData(1920 - 340 - 3, 300, "Right")]
    [InlineData(800, 45, "Top")]
    [InlineData(800, 300, "None")]
    [InlineData(20, 300, "None")]
    public void DetectEdge_NearOrAwayFromEdges_ReturnsTheEdge(int x, int y, string expected)
    {
        var window = new PixelRect(x, y, 340, 420);

        EdgeDockRules.DetectEdge(window, Area, 8).Should().Be(Enum.Parse<DockEdge>(expected));
    }

    /// <summary>
    /// A window in a corner docks to the top, so that it hides upward rather than sideways.
    /// </summary>
    [Fact]
    public void DetectEdge_InTopLeftCorner_PrefersTop()
    {
        EdgeDockRules.DetectEdge(new PixelRect(2, 42, 340, 420), Area, 8).Should().Be(DockEdge.Top);
    }

    /// <summary>
    /// A window that is not on the screen is not docked to it.
    /// </summary>
    [Fact]
    public void DetectEdge_WindowOutsideTheArea_ReturnsNone()
    {
        EdgeDockRules.DetectEdge(new PixelRect(-2000, 300, 340, 420), Area, 8).Should().Be(DockEdge.None);
    }

    /// <summary>
    /// The shown position is flush with the edge and keeps the other axis inside the work area.
    /// </summary>
    [Fact]
    public void ShownPosition_PerEdge_IsFlushAndInsideTheArea()
    {
        EdgeDockRules.ShownPosition(new PixelRect(5, 300, 340, 420), Area, DockEdge.Left).Should().Be(new PixelPoint(0, 300));
        EdgeDockRules.ShownPosition(new PixelRect(1570, 900, 340, 420), Area, DockEdge.Right).Should().Be(new PixelPoint(1580, 620));
        EdgeDockRules.ShownPosition(new PixelRect(1800, 45, 340, 420), Area, DockEdge.Top).Should().Be(new PixelPoint(1580, 40));
    }

    /// <summary>
    /// The hidden position leaves only the sliver on the screen.
    /// </summary>
    [Fact]
    public void HiddenPosition_PerEdge_LeavesOnlyTheSliver()
    {
        EdgeDockRules.HiddenPosition(new PixelRect(0, 300, 340, 420), Area, DockEdge.Left, 4).Should().Be(new PixelPoint(-336, 300));
        EdgeDockRules.HiddenPosition(new PixelRect(1580, 300, 340, 420), Area, DockEdge.Right, 4).Should().Be(new PixelPoint(1916, 300));
        EdgeDockRules.HiddenPosition(new PixelRect(800, 40, 340, 420), Area, DockEdge.Top, 4).Should().Be(new PixelPoint(800, -376));
    }

    /// <summary>
    /// The work area does not have to start at the origin, as on a second screen or beside a taskbar.
    /// </summary>
    [Fact]
    public void HiddenPosition_WorkAreaOnASecondScreen_IsRelativeToTheArea()
    {
        var second = new PixelRect(1920, 0, 1280, 1000);

        EdgeDockRules.HiddenPosition(new PixelRect(1925, 200, 340, 420), second, DockEdge.Left, 4).Should().Be(new PixelPoint(1584, 200));
    }

    /// <summary>
    /// A hidden window that would show on a neighboring screen is not allowed.
    /// </summary>
    [Fact]
    public void StaysOffOtherScreens_Neighbor_DependsOnOverlap()
    {
        var neighbor = new PixelRect(-1280, 0, 1280, 1000);

        EdgeDockRules.StaysOffOtherScreens(new PixelRect(-336, 300, 340, 420), [neighbor]).Should().BeFalse();
        EdgeDockRules.StaysOffOtherScreens(new PixelRect(1916, 300, 340, 420), [neighbor]).Should().BeTrue();
        EdgeDockRules.StaysOffOtherScreens(new PixelRect(-336, 300, 340, 420), []).Should().BeTrue();
    }
}
