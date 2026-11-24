using Avalonia;
using FluxTick.App.Views;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for <see cref="AssetFocusDragRules"/>.
/// </summary>
public class AssetFocusDragRulesTests
{
    /// <summary>
    /// Verifies a small movement is a click and a large one is a drag.
    /// </summary>
    [Fact]
    public void ExceedsThreshold_SmallAndLargeMoves_OnlyLargeStartsDrag()
    {
        var start = new Point(100, 100);

        AssetFocusDragRules.ExceedsThreshold(start, new Point(102, 101)).Should().BeFalse();
        AssetFocusDragRules.ExceedsThreshold(start, new Point(100, 100 + AssetFocusDragRules.DragThreshold)).Should().BeTrue();
        AssetFocusDragRules.ExceedsThreshold(start, new Point(130, 140)).Should().BeTrue();
    }

    /// <summary>
    /// Verifies the carried window is a share of a large screen.
    /// </summary>
    [Fact]
    public void CarriedSize_LargeScreen_TakesAShare()
    {
        var size = AssetFocusDragRules.CarriedSize(new PixelRect(0, 0, 2560, 1440), 1);

        size.Width.Should().BeApproximately(2560 * AssetFocusDragRules.CarriedShare, 0.01);
        size.Height.Should().BeApproximately(1440 * AssetFocusDragRules.CarriedShare, 0.01);
    }

    /// <summary>
    /// Verifies the carried window keeps a floor on a small screen but never exceeds the screen.
    /// </summary>
    [Fact]
    public void CarriedSize_SmallScreen_KeepsFloorWithinScreen()
    {
        var floor = AssetFocusDragRules.CarriedSize(new PixelRect(0, 0, 1024, 768), 1);
        var tiny = AssetFocusDragRules.CarriedSize(new PixelRect(0, 0, 500, 400), 1);

        floor.Width.Should().Be(AssetFocusDragRules.MinCarriedWidth);
        floor.Height.Should().Be(AssetFocusDragRules.MinCarriedHeight);
        tiny.Should().Be(new Size(500, 400));
    }

    /// <summary>
    /// Verifies the size is given in device independent pixels, so a scaled screen is divided by its scaling.
    /// </summary>
    [Fact]
    public void CarriedSize_ScaledScreen_IsInDeviceIndependentPixels()
    {
        if (OperatingSystem.IsMacOS())
        {
            return;
        }

        var size = AssetFocusDragRules.CarriedSize(new PixelRect(0, 0, 3840, 2160), 2);

        size.Width.Should().BeApproximately(1920 * AssetFocusDragRules.CarriedShare, 0.01);
        size.Height.Should().BeApproximately(1080 * AssetFocusDragRules.CarriedShare, 0.01);
    }

    /// <summary>
    /// Verifies the pointer keeps its relative place in the carried window.
    /// </summary>
    [Fact]
    public void CarriedOrigin_PointerInTheMiddle_CentersWindowOnPointer()
    {
        var bounds = new PixelRect(1920, 0, 1920, 1080);
        var size = new Size(1000, 600);

        var origin = AssetFocusDragRules.CarriedOrigin(new PixelPoint(2880, 540), bounds, size, 1);

        origin.Should().Be(new PixelPoint(2380, 240));
    }

    /// <summary>
    /// Verifies a pointer in a corner keeps the window's corner under it.
    /// </summary>
    [Fact]
    public void CarriedOrigin_PointerAtCorner_KeepsCornerUnderPointer()
    {
        var bounds = new PixelRect(0, 0, 1920, 1080);
        var size = new Size(1000, 600);

        AssetFocusDragRules.CarriedOrigin(new PixelPoint(0, 0), bounds, size, 1).Should().Be(new PixelPoint(0, 0));
        AssetFocusDragRules.CarriedOrigin(new PixelPoint(1920, 1080), bounds, size, 1).Should().Be(new PixelPoint(920, 480));
    }

    /// <summary>
    /// Verifies an empty screen does not divide by zero.
    /// </summary>
    [Fact]
    public void CarriedOrigin_EmptyScreen_UsesPointer()
    {
        var origin = AssetFocusDragRules.CarriedOrigin(new PixelPoint(10, 20), new PixelRect(0, 0, 0, 0), new Size(100, 100), 1);

        origin.Should().Be(new PixelPoint(10, 20));
    }
}
