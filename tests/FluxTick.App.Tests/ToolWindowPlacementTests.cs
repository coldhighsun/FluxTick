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
}
