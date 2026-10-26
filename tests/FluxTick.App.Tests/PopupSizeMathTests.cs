using Avalonia;
using FluxTick.App.Views;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for how a menu window grows when its content is taller than the window.
/// </summary>
public class PopupSizeMathTests
{
    /// <summary>
    /// The work area used by the tests.
    /// </summary>
    private static readonly PixelRect Area = new(0, 0, 1920, 1080);

    /// <summary>
    /// Verifies a menu below the pointer grows downward and keeps its top.
    /// </summary>
    [Fact]
    public void Grow_MenuBelowTheAnchor_GrowsDownward()
    {
        var grown = PopupSizeMath.Grow(new PixelRect(500, 300, 165, 168), 28, anchorY: 300, Area);

        grown.Should().Be(new PixelRect(500, 300, 165, 196));
    }

    /// <summary>
    /// Verifies a menu that hangs from the pointer, because it did not fit below, grows upward and keeps its bottom there.
    /// </summary>
    [Fact]
    public void Grow_MenuAboveTheAnchor_GrowsUpward()
    {
        var grown = PopupSizeMath.Grow(new PixelRect(500, 800, 165, 168), 28, anchorY: 968, Area);

        grown.Should().Be(new PixelRect(500, 772, 165, 196));
        grown.Bottom.Should().Be(968);
    }

    /// <summary>
    /// Verifies a menu that would reach below the work area is moved up to fit.
    /// </summary>
    [Fact]
    public void Grow_ReachingBelowTheWorkArea_IsMovedUp()
    {
        var grown = PopupSizeMath.Grow(new PixelRect(500, 900, 165, 168), 28, anchorY: null, Area);

        grown.Bottom.Should().Be(Area.Bottom);
        grown.Height.Should().Be(196);
    }

    /// <summary>
    /// Verifies a menu growing upward does not leave the top of the work area.
    /// </summary>
    [Fact]
    public void Grow_UpwardPastTheTop_StaysInsideTheWorkArea()
    {
        var grown = PopupSizeMath.Grow(new PixelRect(500, 10, 165, 168), 28, anchorY: 178, Area);

        grown.Y.Should().Be(0);
    }

    /// <summary>
    /// Verifies a menu is never made taller than the work area, so a very long one keeps its scroll arrow on the screen.
    /// </summary>
    [Fact]
    public void Grow_WantingMoreThanTheWorkArea_IsCappedAtItsHeight()
    {
        var grown = PopupSizeMath.Grow(new PixelRect(500, 20, 165, 1000), 400, anchorY: null, Area);

        grown.Height.Should().Be(Area.Height);
        grown.Y.Should().Be(Area.Y);
    }

    /// <summary>
    /// Verifies only a screen with another scaling than the primary one can leave a menu sized wrong.
    /// </summary>
    [Fact]
    public void MaySizedWrong_ByScaling_OnlyForADifferentScreen()
    {
        PopupSizeMath.MaySizedWrong(1.25, 1.0).Should().BeTrue();
        PopupSizeMath.MaySizedWrong(1.0, 1.0).Should().BeFalse();
        PopupSizeMath.MaySizedWrong(1.5, 1.5).Should().BeFalse();
    }
}
