using Avalonia;
using Avalonia.Controls;
using FluxTick.App.Views;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for what the window remembers about full screen, as the sequences of state changes it goes through.
/// </summary>
public class FullScreenMemoryTests
{
    /// <summary>
    /// The place the window had before full screen.
    /// </summary>
    private static readonly PixelPoint Home = new(800, 120);

    /// <summary>
    /// Verifies a normal exit puts back both the height and the place that were recorded.
    /// </summary>
    [Fact]
    public void Leave_AfterEnter_RestoresSizeAndPosition()
    {
        var memory = new FullScreenMemory();
        memory.Enter(Home, 640, positionIsHidden: false);

        var restore = memory.Leave(WindowState.Normal, FullScreenExit.Restore);

        restore.Should().Be(new FullScreenLeave(true, true, true));
        memory.Bounds.Should().Be(new RestoreBounds(Home, 640));
    }

    /// <summary>
    /// Verifies a drag puts the height back but leaves the place to the pointer.
    /// </summary>
    [Fact]
    public void Leave_Dragged_RestoresOnlyTheSize()
    {
        var memory = new FullScreenMemory();
        memory.Enter(Home, 640, positionIsHidden: false);

        memory.Leave(WindowState.Normal, FullScreenExit.Dragged).Should().Be(new FullScreenLeave(true, true, false));
    }

    /// <summary>
    /// Verifies choosing the list layout keeps the size of the layout.
    /// </summary>
    [Fact]
    public void Leave_KeepLayoutSize_RestoresNothing()
    {
        var memory = new FullScreenMemory();
        memory.Enter(Home, 640, positionIsHidden: false);

        memory.Leave(WindowState.Normal, FullScreenExit.KeepLayoutSize).Should().Be(new FullScreenLeave(true, false, false));
    }

    /// <summary>
    /// Verifies a window that was never full screen gets nothing put back when it returns to normal.
    /// </summary>
    [Fact]
    public void Leave_WithoutEnter_RestoresNothing()
    {
        new FullScreenMemory().Leave(WindowState.Normal, FullScreenExit.Restore).Should().Be(new FullScreenLeave(false, false, false));
    }

    /// <summary>
    /// Verifies entering and leaving say whether the full screen state really changed, which is what the change event
    /// is raised from: not for a second entry (a minimize round trip) and not for a leave that was no full screen.
    /// </summary>
    [Fact]
    public void EnterAndLeave_ReportRealChangesOnly()
    {
        var memory = new FullScreenMemory();

        memory.Enter(Home, 640, positionIsHidden: false).Should().BeTrue();
        memory.Enter(Home, 640, positionIsHidden: false).Should().BeFalse();
        memory.Leave(WindowState.Normal, FullScreenExit.Restore).Left.Should().BeTrue();
        memory.Leave(WindowState.Normal, FullScreenExit.Restore).Left.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a window hidden beyond a screen edge is recorded at the place it was last shown at.
    /// </summary>
    [Fact]
    public void Enter_WithHiddenPosition_KeepsTheEarlierPlace()
    {
        var memory = new FullScreenMemory();
        memory.Enter(Home, 640, positionIsHidden: false);
        memory.Leave(WindowState.Normal, FullScreenExit.Restore);

        memory.Enter(new PixelPoint(-5000, 120), 650, positionIsHidden: true);

        memory.Bounds.Should().Be(new RestoreBounds(Home, 650));
    }

    /// <summary>
    /// Verifies a hidden window without an earlier record is recorded where it is, since nothing better is known.
    /// </summary>
    [Fact]
    public void Enter_WithHiddenPositionAndNoRecord_UsesThePosition()
    {
        var memory = new FullScreenMemory();

        memory.Enter(new PixelPoint(-5000, 120), 650, positionIsHidden: true);

        memory.Bounds!.Position.Should().Be(new PixelPoint(-5000, 120));
    }

    /// <summary>
    /// Verifies going full screen again while it still counts as full screen, as after a minimize, keeps the record:
    /// what is on the screen then is the full screen window.
    /// </summary>
    [Fact]
    public void Enter_WhileStillFullScreen_KeepsTheRecord()
    {
        var memory = new FullScreenMemory();
        memory.Enter(Home, 640, positionIsHidden: false);

        memory.Enter(new PixelPoint(0, 0), 1080, positionIsHidden: false);

        memory.Bounds.Should().Be(new RestoreBounds(Home, 640));
    }

    /// <summary>
    /// Verifies a window that is minimized and restored to full screen still gets its size back later. Minimizing does
    /// not touch the memory; only the leave calls do.
    /// </summary>
    [Fact]
    public void Leave_AfterAMinimizeRoundTrip_RestoresTheOriginalBounds()
    {
        var memory = new FullScreenMemory();
        memory.Enter(Home, 640, positionIsHidden: false);
        memory.Enter(new PixelPoint(0, 0), 1080, positionIsHidden: false);

        memory.Leave(WindowState.Normal, FullScreenExit.Restore).Should().Be(new FullScreenLeave(true, true, true));
        memory.Bounds.Should().Be(new RestoreBounds(Home, 640));
    }

    /// <summary>
    /// Verifies a maximized window is no longer full screen, and a later restore to normal puts nothing back.
    /// </summary>
    [Fact]
    public void Leave_ToMaximized_EndsFullScreenAndRestoresNothing()
    {
        var memory = new FullScreenMemory();
        memory.Enter(Home, 640, positionIsHidden: false);

        memory.Leave(WindowState.Maximized, FullScreenExit.Restore).Should().Be(new FullScreenLeave(true, false, false));
        memory.Leave(WindowState.Normal, FullScreenExit.Restore).Should().Be(new FullScreenLeave(false, false, false));
    }

    /// <summary>
    /// Verifies a window that goes full screen from the maximized state is recorded afresh.
    /// </summary>
    [Fact]
    public void Enter_AfterMaximized_RecordsTheNewBounds()
    {
        var memory = new FullScreenMemory();
        memory.Enter(Home, 640, positionIsHidden: false);
        memory.Leave(WindowState.Maximized, FullScreenExit.Restore);

        memory.Enter(new PixelPoint(10, 10), 900, positionIsHidden: false);

        memory.Bounds.Should().Be(new RestoreBounds(new PixelPoint(10, 10), 900));
    }
}
