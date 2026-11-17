using Avalonia;
using FluxTick.App.Services;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for converting the pointer position of macOS to screen pixels.
/// </summary>
public class MacTrayCursorTests
{
    /// <summary>
    /// Verifies a position near the top of a non-scaled screen lands near y = 0, as the y axis of Cocoa points up.
    /// </summary>
    [Fact]
    public void ToScreenPixels_MenuBarPosition_FlipsYAxis()
    {
        var position = MacTrayCursor.ToScreenPixels(1200, 899, 900, 1);

        position.Should().Be(new PixelPoint(1200, 1));
    }

    /// <summary>
    /// Verifies points are scaled to pixels on a Retina screen.
    /// </summary>
    [Fact]
    public void ToScreenPixels_RetinaScreen_ScalesToPixels()
    {
        var position = MacTrayCursor.ToScreenPixels(1200.5, 890, 900, 2);

        position.Should().Be(new PixelPoint(2401, 20));
    }

    /// <summary>
    /// Verifies a Retina screen reported in points, as macOS does for the screens of the app, needs no scale: the position
    /// stays in points.
    /// </summary>
    [Fact]
    public void ToScreenPixels_ScreenInPoints_KeepsPoints()
    {
        var position = MacTrayCursor.ToScreenPixels(1200.4, 890, 900, 1);

        position.Should().Be(new PixelPoint(1200, 10));
    }

    /// <summary>
    /// Verifies the popup opens below an icon in the menu bar at the top of the screen.
    /// </summary>
    [Fact]
    public void Place_MenuBarIcon_OpensBelow()
    {
        var workArea = new PixelRect(0, 48, 2880, 1752);
        var icon = new PixelRect(2400, 20, 1, 1);

        var position = TrayPopupPlacement.Place(icon, new PixelSize(280, 300), workArea);

        position.Y.Should().Be(workArea.Y);
    }

    /// <summary>
    /// Verifies the tray list is offered on macOS as well.
    /// </summary>
    [Fact]
    public void IsSupported_OnWindowsOrMac_IsTrue()
    {
        TrayPopupAvailability.IsSupported.Should().Be(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());
    }
}
