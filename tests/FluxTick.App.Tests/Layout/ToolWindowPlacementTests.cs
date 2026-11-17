using Avalonia;
using FluxTick.App.Views;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for placing a tool window.
/// </summary>
public class ToolWindowPlacementTests
{
    /// <summary>
    /// The tool window size.
    /// </summary>
    private static readonly PixelSize Size = new(420, 700);

    /// <summary>
    /// Verifies the tool window opens in the middle of the area, also when the area does not start at the origin.
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

    /// <summary>
    /// Verifies the size in device independent pixels is scaled to screen pixels before the window is centered in the work
    /// area, so that the window does not sit off center on a scaled display.
    /// </summary>
    [Fact]
    public void CenterOnScreen_ScaledScreen_CentersTheScaledSizeInTheWorkArea()
    {
        var workArea = new PixelRect(0, 0, 2000, 1000);

        var position = ToolWindowPlacement.CenterOnScreen(new Avalonia.Size(400, 600), 1.5, workArea);

        position.Should().Be(new PixelPoint((2000 - 600) / 2, (1000 - 900) / 2));
    }
}
