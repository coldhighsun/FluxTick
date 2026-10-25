using Avalonia;
using Avalonia.Controls;
using FluxTick.App.Views;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for coming back from full screen.
/// </summary>
public class FullScreenRestoreTests
{
    /// <summary>
    /// The usable screen area.
    /// </summary>
    private static readonly PixelRect WorkArea = new(0, 0, 1920, 1040);

    /// <summary>
    /// The window size in screen pixels.
    /// </summary>
    private static readonly PixelSize Size = new(500, 600);

    /// <summary>
    /// Verifies the remembered height is used.
    /// </summary>
    [Fact]
    public void Height_WithRecord_UsesIt()
    {
        FullScreenRestore.Height(new RestoreBounds(new PixelPoint(10, 10), 700), 400).Should().Be(700);
    }

    /// <summary>
    /// Verifies a height taller than the screen holds is limited, and that the screen wins over the minimum.
    /// </summary>
    [Fact]
    public void Height_TallerThanTheScreen_IsLimited()
    {
        var record = new RestoreBounds(new PixelPoint(0, 0), 1080);

        FullScreenRestore.Height(record, 400, 900).Should().Be(900);
        FullScreenRestore.Height(record, 400, 300).Should().Be(300);
    }

    /// <summary>
    /// Verifies the header is reachable when it lies on a screen, also when that is a second screen.
    /// </summary>
    [Fact]
    public void IsReachable_HeaderOnAScreen_IsTrue()
    {
        var areas = new[] { new PixelRect(0, 0, 1920, 1040), new PixelRect(1920, 0, 1920, 1040) };

        FullScreenRestore.IsReachable(new PixelRect(2500, 100, 340, 30), areas).Should().BeTrue();
        FullScreenRestore.IsReachable(new PixelRect(1800, 100, 340, 30), areas).Should().BeTrue("most of it is on the first screen");
    }

    /// <summary>
    /// Verifies the header is not reachable when it lies beyond every screen, or only a sliver of it is on one.
    /// </summary>
    [Fact]
    public void IsReachable_HeaderBeyondTheScreens_IsFalse()
    {
        var areas = new[] { new PixelRect(0, 0, 1920, 1040) };

        FullScreenRestore.IsReachable(new PixelRect(2500, 100, 340, 30), areas).Should().BeFalse("a screen was unplugged");
        FullScreenRestore.IsReachable(new PixelRect(1900, 100, 340, 30), areas).Should().BeFalse("only 20 px are visible");
        FullScreenRestore.IsReachable(new PixelRect(100, 1035, 340, 30), areas).Should().BeFalse("only 5 px of the strip are on the work area");
    }

    /// <summary>
    /// Verifies a header a few pixels above or below the work area, as one flush with the edge of a screen can be, is
    /// still reachable.
    /// </summary>
    [Fact]
    public void IsReachable_HeaderAFewPixelsOutside_IsTrue()
    {
        var areas = new[] { new PixelRect(0, 0, 1920, 1040) };

        FullScreenRestore.IsReachable(new PixelRect(100, -2, 340, 30), areas).Should().BeTrue();
        FullScreenRestore.IsReachable(new PixelRect(100, 1025, 340, 30), areas).Should().BeTrue();
    }

    /// <summary>
    /// Verifies the default height is used without a record.
    /// </summary>
    [Fact]
    public void Height_WithoutRecord_IsTheDefault()
    {
        FullScreenRestore.Height(null, 100).Should().Be(FullScreenRestore.DefaultHeight);
        FullScreenRestore.DefaultHeight.Should().Be(420);
    }

    /// <summary>
    /// Verifies a record that cannot be used is replaced by the default.
    /// </summary>
    [Fact]
    public void Height_WithUnusableRecord_IsTheDefault()
    {
        FullScreenRestore.Height(new RestoreBounds(new PixelPoint(0, 0), 0), 100).Should().Be(FullScreenRestore.DefaultHeight);
    }

    /// <summary>
    /// Verifies the height never goes below the minimum.
    /// </summary>
    [Fact]
    public void Height_BelowMinimum_IsRaised()
    {
        FullScreenRestore.Height(new RestoreBounds(new PixelPoint(0, 0), 300), 400).Should().Be(400);
        FullScreenRestore.Height(null, 500).Should().Be(500);
    }

    /// <summary>
    /// Verifies what each way out of full screen puts back: only a real exit to the normal window does, not a restore
    /// from the minimized state or a change to maximized; a drag leaves the place to the pointer; the list layout keeps
    /// its own size.
    /// </summary>
    /// <param name="wasFullScreen">
    /// Whether the window was full screen.
    /// </param>
    /// <param name="newState">
    /// The state the window has now.
    /// </param>
    /// <param name="exit">
    /// How the window leaves.
    /// </param>
    /// <param name="size">
    /// Whether the height is put back.
    /// </param>
    /// <param name="position">
    /// Whether the place is put back.
    /// </param>
    [Theory]
    [InlineData(true, WindowState.Normal, FullScreenExit.Restore, true, true)]
    [InlineData(true, WindowState.Normal, FullScreenExit.Dragged, true, false)]
    [InlineData(true, WindowState.Normal, FullScreenExit.KeepLayoutSize, false, false)]
    [InlineData(true, WindowState.Maximized, FullScreenExit.Restore, false, false)]
    [InlineData(true, WindowState.Minimized, FullScreenExit.Restore, false, false)]
    [InlineData(false, WindowState.Normal, FullScreenExit.Restore, false, false)]
    [InlineData(false, WindowState.FullScreen, FullScreenExit.Restore, false, false)]
    public void Restores_OnlyForARealExit(bool wasFullScreen, WindowState newState, FullScreenExit exit, bool size, bool position)
    {
        FullScreenRestore.Restores(wasFullScreen, newState, exit).Should().Be((size, position));
    }

    /// <summary>
    /// Verifies the window is centered on the pointer sideways, with the middle of the header at the pointer's height.
    /// </summary>
    [Fact]
    public void Place_CentersTheWindowOnThePointer()
    {
        var position = FullScreenRestore.Place(new PixelPoint(1000, 300), 20, Size, WorkArea);

        position.Should().Be(new PixelPoint(1000 - 250, 300 - 20));
    }

    /// <summary>
    /// Verifies a window dragged out near the right edge stays on the screen.
    /// </summary>
    [Fact]
    public void Place_NearTheRightEdge_IsClampedIntoTheWorkArea()
    {
        var position = FullScreenRestore.Place(new PixelPoint(1915, 300), 20, Size, WorkArea);

        position.X.Should().Be(1920 - 500);
    }

    /// <summary>
    /// Verifies a window dragged out near the left edge stays on the screen.
    /// </summary>
    [Fact]
    public void Place_NearTheLeftEdge_IsClampedIntoTheWorkArea()
    {
        FullScreenRestore.Place(new PixelPoint(5, 300), 20, Size, WorkArea).X.Should().Be(0);
    }

    /// <summary>
    /// Verifies the header stays reachable when the pointer is at the top of the screen.
    /// </summary>
    [Fact]
    public void Place_AtTheTop_DoesNotLeaveTheWorkArea()
    {
        FullScreenRestore.Place(new PixelPoint(1000, 3), 20, Size, WorkArea).Y.Should().Be(0);
    }

    /// <summary>
    /// Verifies a window taller than the work area is aligned to the top.
    /// </summary>
    [Fact]
    public void Place_TallerThanTheWorkArea_AlignsToTheTop()
    {
        var area = new PixelRect(0, 0, 1920, 500);

        FullScreenRestore.Place(new PixelPoint(1000, 300), 20, Size, area).Y.Should().Be(0);
    }

    /// <summary>
    /// Verifies the pointer ends up in the middle of the header, whatever the window's width, and does not depend on
    /// where it was in the full screen window.
    /// </summary>
    /// <param name="width">
    /// The window width in screen pixels.
    /// </param>
    [Theory]
    [InlineData(340)]
    [InlineData(511)]
    [InlineData(900)]
    public void Place_PutsThePointerInTheMiddleOfTheHeader(int width)
    {
        var size = new PixelSize(width, 600);

        var position = FullScreenRestore.Place(new PixelPoint(1000, 300), 20, size, WorkArea);

        (position.X + (width / 2.0)).Should().BeApproximately(1000, 0.5);
        (position.Y + 20).Should().Be(300);
    }
}
