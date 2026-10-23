using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using FluxTick.App.ViewModels;

namespace FluxTick.App.Views;

/// <summary>
/// Hides the window at the screen edge it was dragged to and slides it out when the pointer comes near.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// How long the window has to stay where it is before it counts as dropped. Moving it by its header hands the
    /// drag to the system, which may report no end, so the end is also taken to be the moment the position stops
    /// changing.
    /// </summary>
    private static readonly TimeSpan DockSettleDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// How long the pointer has to be away before a shown window slides back to its edge.
    /// </summary>
    private static readonly TimeSpan DockLeaveDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// The context menus that were opened and may still be, so the window does not hide under them.
    /// </summary>
    private static readonly HashSet<ContextMenu> OpenMenus = [];

    /// <summary>
    /// The dock state.
    /// </summary>
    private readonly EdgeDockModel _dock = new();

    /// <summary>
    /// Slides the window to and from its edge.
    /// </summary>
    private readonly StepAnimation _dockSlide = new();

    /// <summary>
    /// Whether edge hiding is on: the setting is on and the window stays above others, because a hidden window whose
    /// strip is covered by another window cannot be reached.
    /// </summary>
    private bool _autoHideAtEdge;

    /// <summary>
    /// Whether the window moves by itself, so that the changes of position it causes are not taken for the user's.
    /// </summary>
    private bool _dockSliding;

    /// <summary>
    /// Whether the user is dragging the window by its header.
    /// </summary>
    private bool _moving;

    /// <summary>
    /// The wait for the position to stop changing, or <see langword="null"/> when none is pending.
    /// </summary>
    private IDisposable? _dockSettle;

    /// <summary>
    /// The wait for the pointer to stay away, or <see langword="null"/> when none is pending.
    /// </summary>
    private IDisposable? _dockLeave;

    /// <summary>
    /// Tracks the context menus of the application.
    /// </summary>
    static MainWindow()
    {
        MenuBase.OpenedEvent.AddClassHandler<ContextMenu>((menu, _) => OpenMenus.Add(menu));
        MenuBase.ClosedEvent.AddClassHandler<ContextMenu>((menu, _) => OpenMenus.Remove(menu));
    }

    /// <summary>
    /// Slides a hidden window out when the pointer reaches its visible strip.
    /// </summary>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        CancelDockLeave();
        if (_dock.Hidden)
        {
            SlideDock(hide: false);
        }
    }

    /// <summary>
    /// Starts waiting to hide a docked window when the pointer leaves it.
    /// </summary>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_dock.ShouldWaitToHide)
        {
            ScheduleDockLeave();
        }
    }

    /// <summary>
    /// Starts the window after it opened: docks it when it was left at an edge.
    /// </summary>
    /// <param name="e">
    /// The event data.
    /// </param>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ApplyAutoHide();
    }

    /// <summary>
    /// Follows the settings: starts looking at where the window rests, or lets a hidden window come out and stops.
    /// </summary>
    private void ApplyAutoHide()
    {
        _autoHideAtEdge = (DataContext as MainViewModel)?.Settings is { AutoHideAtEdge: true, AlwaysOnTop: true };
        if (_autoHideAtEdge)
        {
            ScheduleDockSettle();
            return;
        }

        CancelDockSettle();
        CancelDockLeave();
        if (_dock.Hidden)
        {
            SlideDockTo(_dock.ShownPosition(CurrentPixelBounds()), RaiseGeometryChanged);
        }

        _dock.Reset();
    }

    /// <summary>
    /// Brings a hidden window out at once, before something moves or resizes it that would otherwise pull it half way
    /// back onto the screen.
    /// </summary>
    private void EndDockHiding()
    {
        if (!_dock.Hidden)
        {
            return;
        }

        _dockSlide.Stop();
        _dock.SetHidden(false);
        MoveDock(_dock.ShownPosition(CurrentPixelBounds()));
    }

    /// <summary>
    /// Shows a docked window that is hidden at its edge.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the window was hidden at its edge and is now coming out.
    /// </returns>
    private bool RevealDocked()
    {
        if (!_dock.Hidden)
        {
            return false;
        }

        SlideDock(hide: false);
        return true;
    }

    /// <summary>
    /// Restarts the wait for the position to stop changing.
    /// </summary>
    private void ScheduleDockSettle()
    {
        CancelDockSettle();
        if (_autoHideAtEdge && IsVisible)
        {
            _dockSettle = DispatcherTimer.RunOnce(OnDockSettled, DockSettleDelay);
        }
    }

    /// <summary>
    /// Restarts the wait for the pointer to stay away.
    /// </summary>
    private void ScheduleDockLeave()
    {
        CancelDockLeave();
        _dockLeave = DispatcherTimer.RunOnce(OnDockLeave, DockLeaveDelay);
    }

    /// <summary>
    /// Cancels the wait for the position to stop changing.
    /// </summary>
    private void CancelDockSettle()
    {
        _dockSettle?.Dispose();
        _dockSettle = null;
    }

    /// <summary>
    /// Cancels the wait for the pointer to stay away.
    /// </summary>
    private void CancelDockLeave()
    {
        _dockLeave?.Dispose();
        _dockLeave = null;
    }

    /// <summary>
    /// Decides, once the window stopped moving, whether it is docked, and starts hiding it if the pointer is not on it.
    /// </summary>
    private void OnDockSettled()
    {
        _dockSettle = null;
        if (!_autoHideAtEdge || _dockSliding || _moving || Screens.ScreenFromWindow(this) is not { } screen)
        {
            return;
        }

        var others = Screens.All.Where(s => s.Bounds != screen.Bounds).Select(s => s.Bounds).ToList();
        var shown = _dock.Settle(
            CurrentPixelBounds(), screen.WorkingArea, others, ScaledPixels(EdgeDockRules.SnapDistance), ScaledPixels(EdgeDockRules.Sliver));
        if (shown is null)
        {
            return;
        }

        MoveDock(shown.Value);
        RaiseGeometryChanged();
        if (!IsPointerOver)
        {
            ScheduleDockLeave();
        }
    }

    /// <summary>
    /// Hides a shown, docked window once the pointer stayed away and nothing keeps it open.
    /// </summary>
    private void OnDockLeave()
    {
        _dockLeave = null;
        switch (_dock.Leave(IsPointerOver, CanAutoHide()))
        {
            case DockLeave.Hide:
                SlideDock(hide: true);
                break;
            case DockLeave.Wait:
                // Something holds the window open, so look again once it may be gone.
                ScheduleDockLeave();
                break;
        }
    }

    /// <summary>
    /// Determines whether nothing needs the window to stay out: no tool window, no menu or pop-up, no drag going on.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the window may hide.
    /// </returns>
    private bool CanAutoHide()
    {
        // A menu that was torn down without reporting that it closed must not keep the window open forever.
        OpenMenus.RemoveWhere(menu => !menu.IsOpen);
        return IsVisible
            && !_moving
            && _dockSettle is null
            && _toolWindow is null
            && OpenMenus.Count == 0
            && GroupNamePrompt.OpenCount == 0
            && GroupButton.Flyout is not { IsOpen: true }
            && StatusButton.Flyout is not { IsOpen: true };
    }

    /// <summary>
    /// Slides the window to its hidden or its shown position.
    /// </summary>
    /// <param name="hide">
    /// <see langword="true"/> to hide the window at its edge, <see langword="false"/> to bring it out.
    /// </param>
    private void SlideDock(bool hide)
    {
        _dock.SetHidden(hide);
        var window = CurrentPixelBounds();
        var target = hide ? _dock.HiddenPosition(window, ScaledPixels(EdgeDockRules.Sliver)) : _dock.ShownPosition(window);
        SlideDockTo(target, hide ? null : RaiseGeometryChanged);
    }

    /// <summary>
    /// Moves the window to a position in small steps, eased out.
    /// </summary>
    /// <param name="target">
    /// The position to reach.
    /// </param>
    /// <param name="onDone">
    /// Called once the window is there.
    /// </param>
    private void SlideDockTo(PixelPoint target, Action? onDone)
    {
        var start = Position;
        _dockSliding = true;
        _dockSlide.Run(
            EdgeDockRules.SlideDuration,
            progress => Position = new PixelPoint(
                (int)Math.Round(WindowWidthRules.Animate(start.X, target.X, progress)),
                (int)Math.Round(WindowWidthRules.Animate(start.Y, target.Y, progress))),
            () =>
            {
                _dockSliding = false;
                onDone?.Invoke();
            });
    }

    /// <summary>
    /// Moves the window at once without the user's position handling taking it for a drag.
    /// </summary>
    /// <param name="target">
    /// The position to move to.
    /// </param>
    private void MoveDock(PixelPoint target)
    {
        _dockSliding = true;
        try
        {
            Position = target;
        }
        finally
        {
            _dockSliding = false;
        }
    }

    /// <summary>
    /// Gets the bounds of the window in screen pixels.
    /// </summary>
    /// <returns>
    /// The position and the size of the window.
    /// </returns>
    private PixelRect CurrentPixelBounds() => new(Position, PixelSize.FromSize(ClientSize, RenderScaling));

    /// <summary>
    /// Converts a length in device independent pixels to screen pixels.
    /// </summary>
    /// <param name="length">
    /// The length in device independent pixels.
    /// </param>
    /// <returns>
    /// The length in screen pixels, at least one.
    /// </returns>
    private int ScaledPixels(double length) => Math.Max(1, (int)Math.Round(length * RenderScaling));
}
