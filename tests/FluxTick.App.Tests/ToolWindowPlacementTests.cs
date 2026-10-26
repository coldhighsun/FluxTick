using Avalonia;
using FluxTick.App.Views;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for placing a tool window beside the main window.
/// </summary>
public class ToolWindowPlacementTests
{
    /// <summary>
    /// The usable screen area.
    /// </summary>
    private static readonly PixelRect WorkArea = new(0, 0, 1920, 1040);

    /// <summary>
    /// The tool window size.
    /// </summary>
    private static readonly PixelSize Size = new(420, 700);

    /// <summary>
    /// Verifies the tool window of a full screen main window opens in the middle of the screen.
    /// </summary>
    [Fact]
    public void Center_PutsTheWindowInTheMiddleOfTheArea()
    {
        var screen = new PixelRect(1920, 0, 2560, 1440);

        var position = ToolWindowPlacement.Center(Size, screen);

        position.Should().Be(new PixelPoint(1920 + (2560 - 420) / 2, (1440 - 700) / 2));
    }

    /// <summary>
    /// Verifies a window larger than the area keeps its top-left corner on the area.
    /// </summary>
    [Fact]
    public void Center_WindowLargerThanArea_StaysAtTheCorner()
    {
        var small = new PixelRect(0, 0, 300, 500);

        ToolWindowPlacement.Center(Size, small).Should().Be(new PixelPoint(0, 0));
    }

    /// <summary>
    /// Verifies the tool window opens to the left of the main window when there is room.
    /// </summary>
    [Fact]
    public void Place_PrefersLeftSide()
    {
        var owner = new PixelRect(1500, 100, 340, 440);

        var position = ToolWindowPlacement.Place(owner, Size, WorkArea);

        position.Should().Be(new PixelPoint(1500 - 8 - 420, 100));
    }

    /// <summary>
    /// Verifies the tool window moves to the right side when the left side is too narrow.
    /// </summary>
    [Fact]
    public void Place_FallsBackToRightSide()
    {
        var owner = new PixelRect(100, 100, 340, 440);

        var position = ToolWindowPlacement.Place(owner, Size, WorkArea);

        position.Should().Be(new PixelPoint(100 + 340 + 8, 100));
    }

    /// <summary>
    /// Verifies the tool window is clamped into the work area when neither side fits.
    /// </summary>
    [Fact]
    public void Place_ClampsIntoWorkAreaWhenNeitherSideFits()
    {
        var narrow = new PixelRect(0, 0, 600, 800);
        var owner = new PixelRect(100, 600, 400, 440);

        var position = ToolWindowPlacement.Place(owner, Size, narrow);

        position.X.Should().BeGreaterThanOrEqualTo(narrow.X);
        (position.X + Size.Width).Should().BeLessThanOrEqualTo(narrow.Right);
        (position.Y + Size.Height).Should().BeLessThanOrEqualTo(narrow.Bottom);
    }

    /// <summary>
    /// Verifies a window that is as tall as its content is placed with the height of the content, limited to the maximum,
    /// and one with a height of its own with that height.
    /// </summary>
    [Fact]
    public void PlacementHeight_ByWindowKind_PicksTheHeight()
    {
        ToolWindowPlacement.PlacementHeight(double.NaN, 380, 900).Should().Be(380);
        ToolWindowPlacement.PlacementHeight(double.NaN, 1200, 900).Should().Be(900);
        ToolWindowPlacement.PlacementHeight(520, 380, 900).Should().Be(520);
        ToolWindowPlacement.PlacementHeight(520, 380, 400).Should().Be(400);
    }
}
