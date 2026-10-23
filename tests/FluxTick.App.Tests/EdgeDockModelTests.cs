using Avalonia;
using FluxTick.App.Views;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the dock state of the main window.
/// </summary>
public sealed class EdgeDockModelTests
{
    /// <summary>
    /// The work area of the screen used by the tests.
    /// </summary>
    private static readonly PixelRect Area = new(0, 40, 1920, 1000);

    /// <summary>
    /// A window dropped at an edge is docked there and is moved flush with it.
    /// </summary>
    [Fact]
    public void Settle_NearAnEdge_DocksAndReturnsTheShownPosition()
    {
        var model = new EdgeDockModel();

        var shown = model.Settle(new PixelRect(5, 300, 340, 420), Area, [], 8, 4);

        shown.Should().Be(new PixelPoint(0, 300));
        model.Edge.Should().Be(DockEdge.Left);
        model.IsDocked.Should().BeTrue();
        model.ShouldWaitToHide.Should().BeTrue();
    }

    /// <summary>
    /// A window dropped away from the edges is not docked.
    /// </summary>
    [Fact]
    public void Settle_InTheMiddle_DoesNotDock()
    {
        var model = new EdgeDockModel();

        model.Settle(new PixelRect(800, 300, 340, 420), Area, [], 8, 4).Should().BeNull();

        model.IsDocked.Should().BeFalse();
        model.ShouldWaitToHide.Should().BeFalse();
    }

    /// <summary>
    /// Dragging a docked window away from its edge ends the dock.
    /// </summary>
    [Fact]
    public void Settle_DraggedAwayFromTheEdge_Undocks()
    {
        var model = new EdgeDockModel();
        model.Settle(new PixelRect(0, 300, 340, 420), Area, [], 8, 4);

        model.Settle(new PixelRect(600, 300, 340, 420), Area, [], 8, 4);

        model.IsDocked.Should().BeFalse();
    }

    /// <summary>
    /// An edge that has another screen behind it is not used, because the hidden window would show there.
    /// </summary>
    [Fact]
    public void Settle_NeighborScreenBehindTheEdge_DoesNotDock()
    {
        var model = new EdgeDockModel();
        PixelRect[] others = [new(-1280, 0, 1280, 1000)];

        model.Settle(new PixelRect(5, 300, 340, 420), Area, others, 8, 4).Should().BeNull();

        model.IsDocked.Should().BeFalse();
    }

    /// <summary>
    /// A hidden window is left alone, so the strip at the edge is not mistaken for a window in the way.
    /// </summary>
    [Fact]
    public void Settle_WhileHidden_ChangesNothing()
    {
        var model = new EdgeDockModel();
        model.Settle(new PixelRect(0, 300, 340, 420), Area, [], 8, 4);
        model.SetHidden(true);

        model.Settle(new PixelRect(-336, 300, 340, 420), Area, [], 8, 4).Should().BeNull();

        model.Hidden.Should().BeTrue();
        model.Edge.Should().Be(DockEdge.Left);
    }

    /// <summary>
    /// The positions use the work area the window was docked in, so they do not depend on the screen the system
    /// reports for a window that is almost entirely off the screen.
    /// </summary>
    [Fact]
    public void Positions_AfterDocking_UseTheRememberedArea()
    {
        var model = new EdgeDockModel();
        var second = new PixelRect(1920, 0, 1280, 1000);
        model.Settle(new PixelRect(3200 - 340 - 2, 200, 340, 420), second, [], 8, 4);

        model.HiddenPosition(new PixelRect(2850, 200, 340, 420), 4).Should().Be(new PixelPoint(3196, 200));
        model.ShownPosition(new PixelRect(3196, 200, 340, 420)).Should().Be(new PixelPoint(2860, 200));
    }

    /// <summary>
    /// Only a docked window can be hidden.
    /// </summary>
    [Fact]
    public void SetHidden_WithoutDock_StaysShown()
    {
        var model = new EdgeDockModel();

        model.SetHidden(true);

        model.Hidden.Should().BeFalse();
    }

    /// <summary>
    /// A hidden window does not wait to hide again when the pointer leaves it.
    /// </summary>
    [Fact]
    public void ShouldWaitToHide_Hidden_IsFalse()
    {
        var model = new EdgeDockModel();
        model.Settle(new PixelRect(0, 300, 340, 420), Area, [], 8, 4);
        model.SetHidden(true);

        model.ShouldWaitToHide.Should().BeFalse();
    }

    /// <summary>
    /// After the pointer stayed away the window hides, waits while something holds it open, and stops when the pointer
    /// is back.
    /// </summary>
    [Fact]
    public void Leave_ByPointerAndBlockers_DecidesWhatToDo()
    {
        var model = new EdgeDockModel();
        model.Settle(new PixelRect(0, 300, 340, 420), Area, [], 8, 4);

        model.Leave(pointerOver: false, canHide: true).Should().Be(DockLeave.Hide);
        model.Leave(pointerOver: false, canHide: false).Should().Be(DockLeave.Wait);
        model.Leave(pointerOver: true, canHide: true).Should().Be(DockLeave.Stop);
    }

    /// <summary>
    /// A window that is not docked, or is hidden already, has nothing to leave.
    /// </summary>
    [Fact]
    public void Leave_NotDockedOrHidden_Stops()
    {
        var model = new EdgeDockModel();
        model.Leave(pointerOver: false, canHide: true).Should().Be(DockLeave.Stop);

        model.Settle(new PixelRect(0, 300, 340, 420), Area, [], 8, 4);
        model.SetHidden(true);
        model.Leave(pointerOver: false, canHide: true).Should().Be(DockLeave.Stop);
    }

    /// <summary>
    /// Resetting makes the window a plain window again.
    /// </summary>
    [Fact]
    public void Reset_AfterDocking_ClearsTheDock()
    {
        var model = new EdgeDockModel();
        model.Settle(new PixelRect(0, 300, 340, 420), Area, [], 8, 4);
        model.SetHidden(true);

        model.Reset();

        model.IsDocked.Should().BeFalse();
        model.Hidden.Should().BeFalse();
    }
}
