using Avalonia;
using FluxTick.App.Services;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for placing the tray popup next to the tray icon.
/// </summary>
public class TrayPopupPlacementTests
{
    /// <summary>
    /// The usable screen area with a taskbar of 40 pixels at the bottom.
    /// </summary>
    private static readonly PixelRect WorkArea = new(0, 0, 1920, 1040);

    /// <summary>
    /// The popup size.
    /// </summary>
    private static readonly PixelSize Size = new(280, 300);

    /// <summary>
    /// Verifies the popup opens above an icon on a bottom taskbar, centered on the icon.
    /// </summary>
    [Fact]
    public void Place_BottomTaskbar_OpensAboveCentered()
    {
        var icon = new PixelRect(1600, 1050, 24, 24);

        var position = TrayPopupPlacement.Place(icon, Size, WorkArea);

        position.X.Should().Be(1612 - 140);

        // The gap would push it into the taskbar, so it sits flush against the work area instead.
        position.Y.Should().Be(WorkArea.Bottom - Size.Height);
    }

    /// <summary>
    /// Verifies the popup is kept inside the work area near the right edge.
    /// </summary>
    [Fact]
    public void Place_NearRightEdge_IsClampedIntoWorkArea()
    {
        var icon = new PixelRect(1890, 1050, 24, 24);

        var position = TrayPopupPlacement.Place(icon, Size, WorkArea);

        position.X.Should().Be(WorkArea.Right - Size.Width);
    }

    /// <summary>
    /// Verifies the popup opens below an icon on a top taskbar.
    /// </summary>
    [Fact]
    public void Place_TopTaskbar_OpensBelow()
    {
        var work = new PixelRect(0, 40, 1920, 1040);
        var icon = new PixelRect(1600, 8, 24, 24);

        var position = TrayPopupPlacement.Place(icon, Size, work);

        position.Y.Should().Be(work.Y);
    }

    /// <summary>
    /// Verifies the popup opens beside an icon on a right-hand taskbar.
    /// </summary>
    [Fact]
    public void Place_RightTaskbar_OpensToTheLeft()
    {
        var work = new PixelRect(0, 0, 1880, 1080);
        var icon = new PixelRect(1890, 1000, 24, 24);

        var position = TrayPopupPlacement.Place(icon, Size, work);

        position.X.Should().Be(work.Right - Size.Width);
    }

    /// <summary>
    /// Verifies the popup opens beside an icon on a left-hand taskbar.
    /// </summary>
    [Fact]
    public void Place_LeftTaskbar_OpensToTheRight()
    {
        var work = new PixelRect(40, 0, 1880, 1080);
        var icon = new PixelRect(8, 1000, 24, 24);

        var position = TrayPopupPlacement.Place(icon, Size, work);

        position.X.Should().Be(work.X);
    }

    /// <summary>
    /// Verifies the popup opens above an icon inside the overflow flyout when the icon is in the lower half of the work area.
    /// </summary>
    [Fact]
    public void Place_IconInsideWorkAreaLowerHalf_OpensAbove()
    {
        var icon = new PixelRect(1700, 800, 24, 24);

        var position = TrayPopupPlacement.Place(icon, Size, WorkArea);

        position.Y.Should().Be(800 - 8 - 300);
        position.X.Should().Be(1712 - 140);
    }

    /// <summary>
    /// Verifies the popup opens below an icon inside the work area when the icon is in the upper half.
    /// </summary>
    [Fact]
    public void Place_IconInsideWorkAreaUpperHalf_OpensBelow()
    {
        var icon = new PixelRect(1700, 100, 24, 24);

        var position = TrayPopupPlacement.Place(icon, Size, WorkArea);

        position.Y.Should().Be(124 + 8);
    }
}
