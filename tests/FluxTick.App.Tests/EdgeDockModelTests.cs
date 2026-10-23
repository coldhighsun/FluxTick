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

    /// <summary>
    /// A move the window made by itself is not the user's, even when the system reports it after the slide ended.
    /// </summary>
    [Fact]
    public void IsUserMove_AfterTheOwnSlide_IgnoresTheTarget()
    {
        var model = new EdgeDockModel();
        model.SetEnabled(true);
        model.BeginSlide(new PixelPoint(0, 300));

        model.IsUserMove(new PixelPoint(120, 300)).Should().BeFalse();
        model.EndSlide();
        model.IsUserMove(new PixelPoint(0, 300)).Should().BeFalse();
        model.IsUserMove(new PixelPoint(5, 300)).Should().BeTrue();
    }

    /// <summary>
    /// Nothing is decided while the window slides or the user drags it, or when edge hiding is off.
    /// </summary>
    [Fact]
    public void CanSettle_WhileSlidingMovingOrDisabled_IsFalse()
    {
        var model = new EdgeDockModel();
        model.CanSettle.Should().BeFalse();

        model.SetEnabled(true);
        model.CanSettle.Should().BeTrue();

        model.SetMoving(true);
        model.CanSettle.Should().BeFalse();
        model.SetMoving(false);

        model.BeginSlide(new PixelPoint(0, 0));
        model.CanSettle.Should().BeFalse();
    }

    /// <summary>
    /// The geometry is not saved while the window slides or rests hidden.
    /// </summary>
    [Fact]
    public void SuppressesGeometry_WhileSlidingOrHidden_IsTrue()
    {
        var model = new EdgeDockModel();
        model.Settle(new PixelRect(0, 300, 340, 420), Area, [], 8, 4);
        model.SuppressesGeometry.Should().BeFalse();

        model.SetHidden(true);
        model.SuppressesGeometry.Should().BeTrue();

        model.SetHidden(false);
        model.BeginSlide(new PixelPoint(0, 300));
        model.SuppressesGeometry.Should().BeTrue();
    }

    /// <summary>
    /// A window the platform did not keep beyond the screen ends the dock, and the second miss in a row turns edge
    /// hiding off even when it is switched on again.
    /// </summary>
    [Fact]
    public void RecordPlacement_MissedTwice_StaysOff()
    {
        var model = new EdgeDockModel();
        model.SetEnabled(true);
        model.Settle(new PixelRect(0, 300, 340, 420), Area, [], 8, 4);
        model.SetHidden(true);

        model.RecordPlacement(honored: false);

        model.IsDocked.Should().BeFalse();
        model.Hidden.Should().BeFalse();
        model.Supported.Should().BeTrue();
        model.SetEnabled(true);
        model.Enabled.Should().BeTrue();

        model.RecordPlacement(honored: false);

        model.Supported.Should().BeFalse();
        model.SetEnabled(true);
        model.Enabled.Should().BeFalse();
    }

    /// <summary>
    /// One miss is forgiven when the window was kept beyond the screen in between.
    /// </summary>
    [Fact]
    public void RecordPlacement_MissThenHonored_KeepsEdgeHidingOn()
    {
        var model = new EdgeDockModel();
        model.SetEnabled(true);

        model.RecordPlacement(honored: false);
        model.RecordPlacement(honored: true);
        model.RecordPlacement(honored: false);

        model.Supported.Should().BeTrue();
    }

    /// <summary>
    /// The tray icon shows and hides a window that is not docked, and slides a docked one out and in.
    /// </summary>
    [Fact]
    public void ToggleFromTray_ByDockState_PicksTheAction()
    {
        var model = new EdgeDockModel();
        model.ToggleFromTray(visible: false).Should().Be(TrayToggle.Show);
        model.ToggleFromTray(visible: true).Should().Be(TrayToggle.Hide);

        model.Settle(new PixelRect(0, 300, 340, 420), Area, [], 8, 4);
        model.ToggleFromTray(visible: true).Should().Be(TrayToggle.SlideIn);

        model.SetHidden(true);
        model.ToggleFromTray(visible: true).Should().Be(TrayToggle.SlideOut);
        model.ToggleFromTray(visible: false).Should().Be(TrayToggle.Show);
    }

    /// <summary>
    /// The wait to hide starts again only for a shown, docked window that the pointer is not on.
    /// </summary>
    [Fact]
    public void ShouldResumeLeave_ByWindowState_DecidesToWait()
    {
        var model = new EdgeDockModel();
        model.ShouldResumeLeave(visible: true, pointerOver: false).Should().BeFalse();

        model.Settle(new PixelRect(0, 300, 340, 420), Area, [], 8, 4);
        model.ShouldResumeLeave(visible: true, pointerOver: false).Should().BeTrue();
        model.ShouldResumeLeave(visible: true, pointerOver: true).Should().BeFalse();
        model.ShouldResumeLeave(visible: false, pointerOver: false).Should().BeFalse();

        model.SetHidden(true);
        model.ShouldResumeLeave(visible: true, pointerOver: false).Should().BeFalse();
    }

    /// <summary>
    /// A work area that is still there keeps the dock, and one that changed is replaced by the screen that took its place.
    /// </summary>
    [Fact]
    public void Rebase_AreaStillThereOrChanged_KeepsTheDock()
    {
        var model = new EdgeDockModel();
        model.Settle(new PixelRect(0, 300, 340, 420), Area, [], 8, 4);

        model.Rebase([Area]).Should().BeTrue();
        model.Area.Should().Be(Area);

        var shrunk = new PixelRect(0, 40, 1920, 960);
        model.Rebase([shrunk]).Should().BeTrue();
        model.Area.Should().Be(shrunk);
    }

    /// <summary>
    /// When the screen the window was docked to is gone, the dock cannot be kept.
    /// </summary>
    [Fact]
    public void Rebase_ScreenGone_ReturnsFalse()
    {
        var model = new EdgeDockModel();
        var second = new PixelRect(1920, 0, 1280, 1000);
        model.Settle(new PixelRect(3200 - 340 - 2, 200, 340, 420), second, [], 8, 4);

        model.Rebase([new PixelRect(0, 0, 1920, 1000)]).Should().BeFalse();
    }
}
