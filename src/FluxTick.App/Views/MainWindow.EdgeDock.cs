using System.Diagnostics;
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
    /// drag to the system, which reports no end, so the end is taken to be the moment the position stops changing.
    /// </summary>
    private static readonly TimeSpan DockSettleDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// How long the pointer has to be away before a shown window slides back to its edge.
    /// </summary>
    private static readonly TimeSpan DockLeaveDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// The context menus that are open, so the window does not hide under them.
    /// </summary>
    private static readonly HashSet<ContextMenu> OpenMenus = [];

    /// <summary>
    /// Whether the setting is on.
    /// </summary>
    private bool _autoHideAtEdge;

    /// <summary>
    /// The edge the window is docked to.
    /// </summary>
    private DockEdge _dockEdge;

    /// <summary>
    /// Whether the window rests, or is sliding to rest, in its hidden position.
    /// </summary>
    private bool _dockHidden;

    /// <summary>
    /// Whether the window moves by itself, so that the changes of position it causes are not taken for the user's.
    /// </summary>
    private bool _dockSliding;

    /// <summary>
    /// Waits for the position to stop changing; created on first use.
    /// </summary>
    private DispatcherTimer? _dockSettleTimer;

    /// <summary>
    /// Waits for the pointer to stay away from a shown window; created on first use.
    /// </summary>
    private DispatcherTimer? _dockLeaveTimer;

    /// <summary>
    /// Steps the slide; created on first use.
    /// </summary>
    private DispatcherTimer? _dockSlideTimer;

    /// <summary>
    /// The handler of the current slide, kept so it can be detached before the next one starts.
    /// </summary>
    private EventHandler _dockSlideStep = (_, _) => { };

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
        _dockLeaveTimer?.Stop();
        if (_dockEdge != DockEdge.None && _dockHidden)
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
        if (_dockEdge != DockEdge.None && !_dockHidden)
        {
            StartDockTimer(ref _dockLeaveTimer, DockLeaveDelay, OnDockLeaveTick);
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
    /// Follows the setting: starts looking at where the window rests, or lets a hidden window come out and stops.
    /// </summary>
    private void ApplyAutoHide()
    {
        _autoHideAtEdge = (DataContext as MainViewModel)?.Settings.AutoHideAtEdge == true;
        if (_autoHideAtEdge)
        {
            ScheduleDockSettle();
            return;
        }

        _dockSettleTimer?.Stop();
        _dockLeaveTimer?.Stop();
        _dockEdge = DockEdge.None;
        if (_dockHidden)
        {
            SlideDockTo(ShownPositionFor(_lastDockEdge), onDone: null);
        }

        _dockHidden = false;
    }

    /// <summary>
    /// The edge the window was last docked to, to bring it back from when the setting is switched off while it is hidden.
    /// </summary>
    private DockEdge _lastDockEdge;

    /// <summary>
    /// Restarts the wait for the position to stop changing.
    /// </summary>
    private void ScheduleDockSettle()
    {
        if (_autoHideAtEdge && IsVisible)
        {
            StartDockTimer(ref _dockSettleTimer, DockSettleDelay, OnDockSettled);
        }
    }

    /// <summary>
    /// Decides, once the window stopped moving, whether it is docked, and hides it if the pointer is not on it.
    /// </summary>
    /// <param name="sender">
    /// The timer.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnDockSettled(object? sender, EventArgs e)
    {
        _dockSettleTimer?.Stop();
        if (!_autoHideAtEdge || _dockSliding || _dockHidden || Screens.ScreenFromWindow(this) is not { } screen)
        {
            return;
        }

        var window = CurrentPixelBounds();
        var edge = EdgeDockRules.DetectEdge(window, screen.WorkingArea, ScaledPixels(EdgeDockRules.SnapDistance));
        if (edge != DockEdge.None)
        {
            var hidden = new PixelRect(
                EdgeDockRules.HiddenPosition(window, screen.WorkingArea, edge, ScaledPixels(EdgeDockRules.Sliver)),
                window.Size);
            var others = Screens.All.Where(s => s.Bounds != screen.Bounds).Select(s => s.Bounds);
            if (!EdgeDockRules.StaysOffOtherScreens(hidden, others))
            {
                edge = DockEdge.None;
            }
        }

        _dockEdge = edge;
        if (edge == DockEdge.None)
        {
            return;
        }

        _lastDockEdge = edge;
        MoveDock(EdgeDockRules.ShownPosition(window, screen.WorkingArea, edge));
        RaiseGeometryChanged();
        if (!IsPointerOver)
        {
            SlideDock(hide: true);
        }
    }

    /// <summary>
    /// Hides a shown, docked window once the pointer stayed away and nothing keeps it open.
    /// </summary>
    /// <param name="sender">
    /// The timer.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnDockLeaveTick(object? sender, EventArgs e)
    {
        if (!_autoHideAtEdge || _dockEdge == DockEdge.None || _dockHidden || IsPointerOver)
        {
            _dockLeaveTimer?.Stop();
            return;
        }

        // The timer keeps ticking while something holds the window open, so it hides after that is gone.
        if (CanAutoHide())
        {
            _dockLeaveTimer?.Stop();
            SlideDock(hide: true);
        }
    }

    /// <summary>
    /// Determines whether nothing needs the window to stay out: no tool window, no menu or pop-up, no drag going on.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the window may hide.
    /// </returns>
    private bool CanAutoHide() =>
        IsVisible
        && _toolWindow is null
        && _dockSettleTimer is not { IsEnabled: true }
        && OpenMenus.Count == 0
        && GroupNamePrompt.OpenCount == 0
        && GroupButton.Flyout is not { IsOpen: true }
        && StatusButton.Flyout is not { IsOpen: true };

    /// <summary>
    /// Slides the window to its hidden or its shown position.
    /// </summary>
    /// <param name="hide">
    /// <see langword="true"/> to hide the window at its edge, <see langword="false"/> to bring it out.
    /// </param>
    private void SlideDock(bool hide)
    {
        _dockHidden = hide;
        var target = hide
            ? HiddenPositionFor(_dockEdge)
            : ShownPositionFor(_dockEdge);
        SlideDockTo(target, onDone: hide ? null : RaiseGeometryChanged);
    }

    /// <summary>
    /// Gets the hidden position for an edge on the screen the window is on.
    /// </summary>
    /// <param name="edge">
    /// The edge.
    /// </param>
    /// <returns>
    /// The top-left position of the window.
    /// </returns>
    private PixelPoint HiddenPositionFor(DockEdge edge) =>
        Screens.ScreenFromWindow(this) is { } screen
            ? EdgeDockRules.HiddenPosition(CurrentPixelBounds(), screen.WorkingArea, edge, ScaledPixels(EdgeDockRules.Sliver))
            : Position;

    /// <summary>
    /// Gets the shown position for an edge on the screen the window is on.
    /// </summary>
    /// <param name="edge">
    /// The edge.
    /// </param>
    /// <returns>
    /// The top-left position of the window.
    /// </returns>
    private PixelPoint ShownPositionFor(DockEdge edge) =>
        Screens.ScreenFromWindow(this) is { } screen
            ? EdgeDockRules.ShownPosition(CurrentPixelBounds(), screen.WorkingArea, edge)
            : Position;

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
        var clock = Stopwatch.StartNew();
        _dockSliding = true;
        _dockSlideTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        _dockSlideTimer.Tick -= _dockSlideStep;
        _dockSlideStep = (_, _) =>
        {
            var progress = clock.Elapsed / EdgeDockRules.SlideDuration;
            Position = new PixelPoint(
                (int)Math.Round(WindowWidthRules.Animate(start.X, target.X, progress)),
                (int)Math.Round(WindowWidthRules.Animate(start.Y, target.Y, progress)));
            if (progress >= 1)
            {
                _dockSlideTimer.Stop();
                _dockSliding = false;
                onDone?.Invoke();
            }
        };
        _dockSlideTimer.Tick += _dockSlideStep;
        _dockSlideTimer.Start();
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
    /// Brings a hidden window out, for example when it is shown from the tray.
    /// </summary>
    private void RevealDocked()
    {
        if (_dockEdge != DockEdge.None && _dockHidden)
        {
            SlideDock(hide: false);
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

    /// <summary>
    /// Restarts a timer with a new interval and handler, creating it on first use.
    /// </summary>
    /// <param name="timer">
    /// The field that holds the timer.
    /// </param>
    /// <param name="interval">
    /// The interval.
    /// </param>
    /// <param name="handler">
    /// The handler to call on every tick.
    /// </param>
    private static void StartDockTimer(ref DispatcherTimer? timer, TimeSpan interval, EventHandler handler)
    {
        if (timer is null)
        {
            timer = new DispatcherTimer { Interval = interval };
            timer.Tick += handler;
        }

        timer.Stop();
        timer.Start();
    }
}
