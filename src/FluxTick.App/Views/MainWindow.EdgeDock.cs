using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using FluxTick.App.Services;
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
    /// How long after a window was sent beyond its edge the system gets to put it there.
    /// </summary>
    private static readonly TimeSpan DockVerifyDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// How long the list of screens has to stay as it is before the dock is checked against it, because the system can
    /// report several incomplete lists while a monitor is plugged in or out.
    /// </summary>
    private static readonly TimeSpan DockScreensDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The context menus that were opened and may still be, so the window does not hide under them.
    /// </summary>
    private static readonly HashSet<ContextMenu> OpenMenus = [];

    /// <summary>
    /// The dock state.
    /// </summary>
    private readonly EdgeDockModel _dock = new(PlatformCapabilities.CanPlaceWindowsOffScreen);

    /// <summary>
    /// Slides the window to and from its edge.
    /// </summary>
    private readonly StepAnimation _dockSlide = new();

    /// <summary>
    /// Whether the window has been opened before, so that what is done once at the start is not repeated when it is
    /// shown again after it was hidden to the tray.
    /// </summary>
    private bool _dockStarted;

    /// <summary>
    /// The wait for the position to stop changing, or <see langword="null"/> when none is pending.
    /// </summary>
    private IDisposable? _dockSettle;

    /// <summary>
    /// The wait for the list of screens to stay as it is, or <see langword="null"/> when none is pending.
    /// </summary>
    private IDisposable? _dockScreens;

    /// <summary>
    /// A slide that was asked for while the screens were still changing and waits for them: <see langword="true"/>
    /// to hide, <see langword="false"/> to bring out.
    /// </summary>
    private bool? _dockSlideAfterScreens;

    /// <summary>
    /// The settings that follow <see cref="EdgeDockModel.Available"/>.
    /// </summary>
    private SettingsViewModel? _dockSettings;

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
    /// Starts watching the window the first time it opens: docks it when it was left at an edge.
    /// </summary>
    /// <param name="e">
    /// The event data.
    /// </param>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_dockStarted)
        {
            return;
        }

        _dockStarted = true;
        Screens.Changed += OnScreensChanged;
        ApplyAutoHide();
    }

    /// <summary>
    /// Makes the settings follow whether edge hiding can work, now and when that changes.
    /// </summary>
    private void FollowAvailability()
    {
        _dock.AvailabilityChanged -= OnDockAvailabilityChanged;
        _dockSettings = (DataContext as MainViewModel)?.Settings;
        if (_dockSettings is null)
        {
            return;
        }

        _dockSettings.AutoHideAvailable = _dock.Available;
        _dock.AvailabilityChanged += OnDockAvailabilityChanged;
    }

    /// <summary>
    /// Tells the settings that edge hiding became unavailable.
    /// </summary>
    /// <param name="sender">
    /// The dock model.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnDockAvailabilityChanged(object? sender, EventArgs e)
    {
        if (_dockSettings is not null)
        {
            _dockSettings.AutoHideAvailable = _dock.Available;
        }
    }

    /// <summary>
    /// Follows the settings: starts looking at where the window rests, or lets a hidden window come out and stops.
    /// </summary>
    private void ApplyAutoHide()
    {
        _dock.SetEnabled((DataContext as MainViewModel)?.Settings is { AutoHideAtEdge: true, AlwaysOnTop: true });
        if (_dock.Enabled)
        {
            ScheduleDockSettle();
            return;
        }

        CancelDockSettle();
        CancelDockLeave();
        if (_dock.Hidden && EnsureDockArea())
        {
            SlideDockTo(_dock.ShownPosition(CurrentPixelBounds()), RaiseGeometryChanged);
        }

        _dock.Reset();
    }

    /// <summary>
    /// Stops a slide that is running and brings a hidden window out at once, before something moves or resizes the
    /// window that would otherwise fight the slide or pull it half way back onto the screen.
    /// </summary>
    private void EndDockMotion()
    {
        _dockSlide.Stop();
        _dock.EndSlide();
        if (!_dock.Hidden || !EnsureDockArea())
        {
            return;
        }

        _dock.SetHidden(false);
        MoveDock(_dock.ShownPosition(CurrentPixelBounds()));
    }

    /// <summary>
    /// Shows a docked window that is hidden at its edge.
    /// </summary>
    private void RevealDocked()
    {
        if (_dock.Hidden)
        {
            SlideDock(hide: false);
        }
    }

    /// <summary>
    /// Makes sure the screen the window is docked to still exists. When it is gone, for example because a monitor was
    /// unplugged while the window was hidden at its edge, the dock ends and the window goes to the default place, so
    /// it cannot stay out of reach.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the dock is still valid.
    /// </returns>
    private bool EnsureDockArea()
    {
        // While the list of screens is still changing it cannot be trusted, so the dock is kept until it settles.
        if (!_dock.IsDocked || _dockScreens is not null || _dock.Rebase(Screens.All.Select(s => s.WorkingArea).ToList()))
        {
            return true;
        }

        _dockSlide.Stop();
        _dock.Reset();
        CancelDockLeave();
        MoveDock(DefaultPosition());
        RaiseGeometryChanged();
        return false;
    }

    /// <summary>
    /// Keeps a window that is docked to a screen on a screen that exists, once the list of screens stopped changing.
    /// </summary>
    /// <param name="sender">
    /// The screens.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnScreensChanged(object? sender, EventArgs e)
    {
        _dockScreens?.Dispose();
        _dockScreens = DispatcherTimer.RunOnce(
            () =>
            {
                _dockScreens = null;
                var slide = _dockSlideAfterScreens;
                _dockSlideAfterScreens = null;
                if (EnsureDockArea() && slide is { } hide)
                {
                    SlideDock(hide);
                }
            },
            DockScreensDelay);
    }

    /// <summary>
    /// Closes the menus and pop-ups that are open, so none is left behind where the window was.
    /// </summary>
    private void CloseDockPopups()
    {
        foreach (var menu in OpenMenus.ToList())
        {
            menu.Close();
        }

        GroupButton.Flyout?.Hide();
        StatusButton.Flyout?.Hide();
        GroupNamePrompt.CloseAll();
    }

    /// <summary>
    /// Starts waiting to hide the window again after something that kept it open went away.
    /// </summary>
    private void ResumeDockLeave()
    {
        if (_dock.ShouldResumeLeave(IsVisible, IsPointerOver))
        {
            ScheduleDockLeave();
        }
    }

    /// <summary>
    /// Restarts the wait for the position to stop changing.
    /// </summary>
    private void ScheduleDockSettle()
    {
        CancelDockSettle();
        if (_dock.Enabled && IsVisible)
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
        if (!_dock.CanSettle || Screens.ScreenFromWindow(this) is not { } screen)
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
                // A menu or pop-up holds the window open, so look again once it may be gone. A tool window or a window
                // sent to the tray does not need watching: closing the tool window starts the wait again.
                if (IsVisible && _toolWindow is null)
                {
                    ScheduleDockLeave();
                }

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
            && !_dock.Moving
            && _dockSettle is null
            && _toolWindow is null
            && OpenMenus.Count == 0
            && !GroupNamePrompt.IsOpen
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
        // A target computed from a list of screens that is still changing may lie on a screen that is gone.
        if (_dockScreens is not null)
        {
            _dockSlideAfterScreens = hide;
            return;
        }

        if (!EnsureDockArea())
        {
            return;
        }

        _dock.SetHidden(hide);
        var window = CurrentPixelBounds();
        var target = hide ? _dock.HiddenPosition(window, ScaledPixels(EdgeDockRules.Sliver)) : _dock.ShownPosition(window);
        SlideDockTo(target, hide ? () => VerifyHidden(target) : RaiseGeometryChanged);
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
        _dock.BeginSlide(target);
        _dockSlide.Run(
            EdgeDockRules.SlideDuration,
            progress => Position = new PixelPoint(
                (int)Math.Round(WindowWidthRules.Animate(start.X, target.X, progress)),
                (int)Math.Round(WindowWidthRules.Animate(start.Y, target.Y, progress))),
            () =>
            {
                _dock.EndSlide();
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
        _dock.BeginSlide(target);
        try
        {
            Position = target;
        }
        finally
        {
            _dock.EndSlide();
        }
    }

    /// <summary>
    /// Checks a little after the window was sent beyond its edge that the system left it there. A window the system
    /// kept in sight is not taken for hidden, and edge hiding stays off from then on. Platforms that are known not to
    /// place windows (see <see cref="PlatformCapabilities.CanPlaceWindowsOffScreen"/>) never get this far.
    /// </summary>
    /// <param name="target">
    /// The hidden position the window was sent to.
    /// </param>
    private void VerifyHidden(PixelPoint target) =>
        DispatcherTimer.RunOnce(
            () =>
            {
                if (!_dock.Hidden || _dock.Sliding)
                {
                    return;
                }

                var tolerance = ScaledPixels(EdgeDockRules.Sliver);
                if (Math.Abs(Position.X - target.X) > tolerance || Math.Abs(Position.Y - target.Y) > tolerance)
                {
                    // The window is in plain sight or stranded beyond the edge, so it goes back to where it is shown
                    // before the edge is given up.
                    var shown = _dock.ShownPosition(CurrentPixelBounds());
                    _dock.Refuse(_dock.Edge);
                    MoveDock(shown);
                    RaiseGeometryChanged();
                }
            },
            DockVerifyDelay);

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
