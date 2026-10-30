using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluxTick.App.Localization;
using FluxTick.App.Services;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;
using Microsoft.Extensions.Logging;

namespace FluxTick.App.Views;

/// <summary>
/// The window position and size in screen pixels.
/// </summary>
/// <param name="X">
/// The left edge.
/// </param>
/// <param name="Y">
/// The top edge.
/// </param>
/// <param name="Width">
/// The width in device independent pixels.
/// </param>
/// <param name="Height">
/// The height in device independent pixels.
/// </param>
public sealed record WindowGeometry(int X, int Y, double Width, double Height);

/// <summary>
/// The borderless, translucent, always-on-top window that hosts the watchlist.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// The distance to the screen edge for the default placement.
    /// </summary>
    private const int ScreenMargin = 24;

    /// <summary>
    /// How far the pointer must move from where the header was pressed, in device independent pixels, before it counts
    /// as a drag out of full screen rather than a click, when the platform gives no distance of its own.
    /// </summary>
    private const double DefaultDragDistance = 4;

    /// <summary>
    /// The open tool window (settings or add instrument), if any.
    /// </summary>
    private ToolWindow? _toolWindow;

    /// <summary>
    /// The view model whose window requests are being observed.
    /// </summary>
    private MainViewModel? _observed;

    /// <summary>
    /// Steps the width animation.
    /// </summary>
    private readonly StepAnimation _widthAnimation = new();

    /// <summary>
    /// The width limits the window has now, so that a change that leaves them as they are does not move the window.
    /// </summary>
    private WidthRule _appliedRule;

    /// <summary>
    /// The width the user chose for the card layout. The list layout has a fixed width of its own, so this is the
    /// value that is remembered and saved.
    /// </summary>
    private double _cardsWidth = WindowWidthRules.ListWidthWithChart;

    /// <summary>
    /// The resize by the grip that is under way on macOS, or <see langword="null"/> when there is none.
    /// </summary>
    private GripDrag? _gripDrag;

    /// <summary>
    /// The width the window had before it went full screen on macOS, where the window has no size of its own meanwhile, or
    /// <see langword="null"/> when none was set aside.
    /// </summary>
    private double? _widthBeforeFullScreen;

    /// <summary>
    /// Where the window is on its way to or from full screen on macOS.
    /// </summary>
    private MacFullScreenPhase _macPhase;

    /// <summary>
    /// Grows the window to the screen before it enters the full screen space, and shrinks it back after it left, so that
    /// the system's switch does not make it jump.
    /// </summary>
    private readonly StepAnimation _fullScreenAnimation = new();

    /// <summary>
    /// The timer of the wait for the system to enter or leave the full screen space, or <see langword="null"/> when none runs.
    /// </summary>
    private DispatcherTimer? _waitTimer;

    /// <summary>
    /// How the window is to leave full screen once the system is there, when that was asked for while it was on its way in.
    /// </summary>
    private FullScreenExit? _pendingLeave;

    /// <summary>
    /// The state the window was in when it was minimized, which is the one a restore returns to.
    /// </summary>
    private WindowState _stateBeforeMinimize = WindowState.Normal;

    /// <summary>
    /// What the window looked like before it went full screen, which it gets back when it leaves full screen.
    /// </summary>
    private readonly FullScreenMemory _fullScreenMemory = new();

    /// <summary>
    /// How the exit from full screen that is under way happens. Set only by <see cref="LeaveFullScreen"/>, around the
    /// change of the state, which the platform reports back at once; any other exit (the system's own) is a restore.
    /// </summary>
    private FullScreenExit _fullScreenExit = FullScreenExit.Restore;

    /// <summary>
    /// The width and position a running width animation ends at, or <see langword="null"/> when none runs.
    /// </summary>
    private (double Width, PixelPoint Position)? _widthAnimationEnd;

    /// <summary>
    /// The press on the header while the window is full screen, which turns into a drag once the pointer moved, or
    /// <see langword="null"/> when there is none.
    /// </summary>
    private PointerPressedEventArgs? _fullScreenPress;

    /// <summary>
    /// Where the header was pressed, in device independent pixels of the window.
    /// </summary>
    private Point _fullScreenPressPoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        Width = WindowWidthRules.ListWidthWithChart;
        MinWidth = WindowWidthRules.CardsMinWidth;
        PositionChanged += (_, _) =>
        {
            RaiseGeometryChanged();
            if (_dock.IsUserMove(Position))
            {
                ScheduleDockSettle();
            }
        };
        Resized += OnResized;
        Watchlist.SizeChanged += (_, _) => ApplyHeightRule();
        Watchlist.ListContentHeightChanged += (_, _) => ApplyHeightRule();
        if (GroupButton.Flyout is MenuFlyout groupFlyout)
        {
            groupFlyout.Opening += (_, _) =>
            {
                if (DataContext is MainViewModel viewModel)
                {
                    GroupingMenu.Fill(groupFlyout.Items, viewModel);
                }
            };
        }
    }

    /// <summary>
    /// Raised when the user moved or resized the window.
    /// </summary>
    public event EventHandler<WindowGeometry>? GeometryChanged;

    /// <summary>
    /// Raised when the window entered or left full screen; the argument tells which.
    /// </summary>
    public event EventHandler<bool>? FullScreenChanged;

    /// <summary>
    /// Gets the logger used to record failures of the tool windows.
    /// </summary>
    public ILogger<MainWindow>? Logger { get; init; }

    /// <summary>
    /// Restores a saved geometry, or places the window in the top-right corner when none is usable.
    /// </summary>
    /// <param name="saved">
    /// The persisted geometry.
    /// </param>
    public void RestoreGeometry(WindowSettings saved)
    {
        _cardsWidth = Math.Max(saved.Width, WindowWidthRules.CardsMinWidth);
        var (layout, compact) = CurrentLayout();
        var rule = WindowWidthRules.For(layout, compact);
        MinWidth = 0;
        Width = WindowWidthRules.TargetWidth(layout, compact, _cardsWidth);
        MinWidth = rule.Min;
        MaxWidth = rule.Max;
        _appliedRule = rule;
        Height = Math.Max(saved.Height, MinHeight);
        WindowStartupLocation = WindowStartupLocation.Manual;

        if (saved is { X: { } x, Y: { } y } && IsOnAnyScreen(new PixelPoint(x, y)))
        {
            Position = new PixelPoint(x, y);
        }
        else
        {
            Position = DefaultPosition();
        }

        // After the normal size and position are set: they are what the window returns to when it leaves full screen.
        if (saved.FullScreen && layout == WatchlistLayout.Cards && !OperatingSystem.IsMacOS())
        {
            WindowState = WindowState.FullScreen;
        }
    }

    /// <summary>
    /// Gets the place the window has when it has no usable saved position: the top-right corner of the primary screen.
    /// </summary>
    /// <returns>
    /// The top-left position of the window.
    /// </returns>
    private PixelPoint DefaultPosition()
    {
        var area = Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
        var scale = ScreenUnits.PerPixel(Screens.Primary?.Scaling ?? 1);
        return new PixelPoint(
            area.Right - (int)(Width * scale) - ScreenMargin,
            area.Y + ScreenMargin);
    }

    /// <summary>
    /// Shows the window when hidden, hides it when visible. A window that is docked to a screen edge is not hidden to
    /// the tray but slides to its edge and back out, the same way it does for the pointer.
    /// </summary>
    /// <param name="showOnly">
    /// Whether a window that is in sight stays there and is only brought forward, instead of being hidden.
    /// </param>
    public void ToggleVisibility(bool showOnly = false)
    {
        // A minimized window is visible to the shell but out of sight, and may have no taskbar button to restore it
        // from: the tray click brings it back instead of hiding it.
        if (RestoreIfMinimized())
        {
            RevealDocked();
            return;
        }

        switch (showOnly ? _dock.ShowFromTray(IsVisible) : _dock.ToggleFromTray(IsVisible))
        {
            case TrayToggle.Keep:
                Activate();
                break;
            case TrayToggle.Show:
                Show();
                Activate();
                RevealDocked();
                break;
            case TrayToggle.Hide:
                Hide();
                break;
            case TrayToggle.SlideOut:
                SlideDock(hide: false, explicitRequest: true);
                Activate();
                break;
            case TrayToggle.SlideIn:
                // Hiding the window to the tray closes the tool window; sliding it away does the same, so none is left
                // open beside a window that is gone, and neither is a menu or pop-up.
                _toolWindow?.Close();
                CloseDockPopups();
                SlideDock(hide: true, explicitRequest: true);
                break;
        }
    }

    /// <summary>
    /// Brings a minimized window back in the state it had before it was minimized, and activates it.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the window was minimized and is restored; <see langword="false"/> when it was not.
    /// </returns>
    public bool RestoreIfMinimized()
    {
        if (!IsVisible || WindowState != WindowState.Minimized)
        {
            return false;
        }

        WindowState = _stateBeforeMinimize;
        Activate();
        return true;
    }

    /// <summary>
    /// Closes the tool window that is open, if any.
    /// </summary>
    public void CloseToolWindow() => _toolWindow?.Close();

    /// <summary>
    /// Brings the tool window that is open, if any, to the front.
    /// </summary>
    public void ActivateToolWindow() => _toolWindow?.Activate();

    /// <summary>
    /// Hides the window to the tray instead of closing it, unless the application is exiting or the user chose that
    /// closing the window exits.
    /// </summary>
    /// <param name="e">
    /// The closing event data.
    /// </param>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Only the user's own close hides the window; a shutdown or logoff must not be vetoed.
        if (e.CloseReason == WindowCloseReason.WindowClosing
            && Application.Current is App { IsExiting: false, CloseToTray: true })
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// Follows the window requests of the current view model.
    /// </summary>
    /// <param name="e">
    /// The event data.
    /// </param>
    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_observed is not null)
        {
            _observed.SettingsRequested -= OnSettingsRequested;
            _observed.AddAssetRequested -= OnAddAssetRequested;
            _observed.AlertsRequested -= OnAlertsRequested;
            _observed.HoldingRequested -= OnHoldingRequested;
            _observed.FullScreenExitRequested -= OnFullScreenExitRequested;
            _observed.PropertyChanged -= OnLayoutPropertyChanged;
            _observed.Settings.PropertyChanged -= OnLayoutPropertyChanged;
        }

        _observed = DataContext as MainViewModel;
        if (_observed is not null)
        {
            _observed.SettingsRequested += OnSettingsRequested;
            _observed.AddAssetRequested += OnAddAssetRequested;
            _observed.AlertsRequested += OnAlertsRequested;
            _observed.HoldingRequested += OnHoldingRequested;
            _observed.FullScreenExitRequested += OnFullScreenExitRequested;
            _observed.PropertyChanged += OnLayoutPropertyChanged;
            _observed.Settings.PropertyChanged += OnLayoutPropertyChanged;
        }

        FollowAvailability();
        ApplyAutoHide();
        ApplyHeightRule();
        base.OnDataContextChanged(e);
    }

    /// <summary>
    /// Closes the open tool window together with the main window when it is hidden.
    /// </summary>
    /// <param name="change">
    /// The property change data.
    /// </param>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        // Before the base class hands the new state to the platform, which resizes the window: this is the last moment the
        // size of the normal window can be read. Whatever state it was in, a window that goes full screen is remembered; one
        // that only returns from the minimized state keeps the record it has.
        var entered = false;
        if (change.Property == WindowStateProperty && change.NewValue is WindowState.FullScreen && change.OldValue is not WindowState.FullScreen)
        {
            entered = RememberNormalBounds();
        }

        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && _macPhase == MacFullScreenPhase.Own)
        {
            // A window hidden to the tray must not keep the menu bar and the Dock of other apps away.
            MacPresentation.SetBarsHidden(IsVisible);
        }

        if (change.Property == IsVisibleProperty && !IsVisible)
        {
            // A drag cannot go on in a window that is gone, and would leave the dock believing the window is dragged.
            EndGripDrag();
        }

        if (change.Property == IsVisibleProperty && !IsVisible)
        {
            _toolWindow?.Close();
        }
        else if (change.Property == WindowStateProperty)
        {
            if (change.NewValue is WindowState.Minimized && change.OldValue is WindowState before && before != WindowState.Minimized)
            {
                _stateBeforeMinimize = before;
            }

            OnWindowStateChanged();
        }

        // After the platform and the layout followed, so a listener sees the window as it is.
        if (entered)
        {
            FullScreenChanged?.Invoke(this, true);
        }
    }

    /// <summary>
    /// Leaves full screen with the Escape key, unless something inside the window used the key.
    /// </summary>
    /// <param name="e">
    /// The key event data.
    /// </param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && (WindowState == WindowState.FullScreen || FillsScreen))
        {
            e.Handled = true;
            LeaveFullScreen(FullScreenExit.Restore);
        }
    }

    /// <summary>
    /// Takes the window out of full screen. What it gets back depends on how it leaves: see <see cref="FullScreenExit"/>.
    /// </summary>
    /// <param name="exit">
    /// How the window leaves full screen.
    /// </param>
    private void LeaveFullScreen(FullScreenExit exit)
    {
        _fullScreenExit = exit;
        try
        {
            if (InNativeSpace)
            {
                // The system puts the window back by itself, with an animation. The window stays full screen for the
                // application until that is over, or what it gets back would be undone by the system.
                if (_macPhase == MacFullScreenPhase.Leaving)
                {
                    return;
                }

                if (_macPhase == MacFullScreenPhase.Entering)
                {
                    // The system is still on its way in; it would enter after the window was given back. It leaves as soon
                    // as it is there.
                    _pendingLeave = exit;
                    return;
                }

                BeginNativeLeave(exit);
            }
            else if (FillsScreen)
            {
                _macPhase = MacFullScreenPhase.None;
                MacPresentation.SetBarsHidden(false);
                OnWindowStateChanged();
            }
            else
            {
                WindowState = WindowState.Normal;
            }
        }
        finally
        {
            _fullScreenExit = FullScreenExit.Restore;
        }
    }

    /// <summary>
    /// Makes room for the window to fill the screen and shows the cards there, and takes the width rules, the layout and
    /// the edge hiding up again when it is back to its normal size. The size and place of the normal window are taken
    /// down when full screen starts (<see cref="RememberNormalBounds"/>) and put back when it ends, whichever way the
    /// window leaves it, except for an exit that keeps the size of the layout; the size and the position that are saved
    /// in the settings stay those of the normal window all along.
    /// </summary>
    private void OnWindowStateChanged()
    {
        // Minimizing or maximizing is not full screen: the layout stays, and whatever is running keeps running.
        if (WindowState is WindowState.Minimized or WindowState.Maximized)
        {
            // A maximized window is no longer full screen, which the saved state must not claim.
            if (WindowState == WindowState.Maximized && _fullScreenMemory.Leave(WindowState.Maximized, FullScreenExit.Restore).Left)
            {
                FullScreenChanged?.Invoke(this, false);
            }

            return;
        }

        _widthAnimation.Stop();
        CancelDockSettle();
        CancelDockLeave();
        if (WindowState == WindowState.FullScreen || FillsScreen)
        {
            // The list layout has a fixed width, which would keep the window from filling the screen.
            MinWidth = 0;
            MaxWidth = double.PositiveInfinity;
            (DataContext as MainViewModel)?.SetFullScreen(true);
            return;
        }

        (DataContext as MainViewModel)?.SetFullScreen(false);
        if (_widthBeforeFullScreen is { } width)
        {
            _widthBeforeFullScreen = null;
            Width = width;
        }

        var leave = _fullScreenMemory.Leave(WindowState, _fullScreenExit);
        try
        {
            // Whatever way the user left full screen, the window comes back as it was, not as large as the screen.
            if (leave.RestoreSize)
            {
                var screen = (_fullScreenMemory.Bounds is { } known ? Screens.ScreenFromPoint(known.Position) : null)
                    ?? Screens.ScreenFromWindow(this)
                    ?? Screens.Primary;
                var maxHeight = screen is null ? double.PositiveInfinity : screen.WorkingArea.Height / ScreenUnits.PerPixel(screen.Scaling);
                Height = FullScreenRestore.Height(_fullScreenMemory.Bounds, MinHeight, maxHeight);
            }

            if (leave.RestorePosition && _fullScreenMemory.Bounds is { } record && IsHeaderReachable(record.Position))
            {
                Position = record.Position;
            }

            var (layout, compact) = CurrentLayout();
            if (_appliedRule != default && WindowWidthRules.For(layout, compact) == _appliedRule)
            {
                FinishWidthChange(_appliedRule);
            }
            else
            {
                // The layout was changed while the window filled the screen. A window that is being dragged out has no time
                // for an animation, which would move it against the pointer: it gets its width at once.
                _appliedRule = default;
                ApplyWidthRule(immediate: _fullScreenExit == FullScreenExit.Dragged);
            }

            ResumeDockLeave();
        }
        finally
        {
            // Last, so a listener sees the window as it is now, not half way out of full screen; and whatever went wrong
            // above, the saved state must not go on saying that the window is full screen.
            if (leave.Left)
            {
                FullScreenChanged?.Invoke(this, false);
            }
        }
    }

    /// <summary>
    /// Determines whether a window put at a place can be grabbed by its header, which a remembered place may no longer
    /// allow after the screens were rearranged.
    /// </summary>
    /// <param name="position">
    /// The top-left position of the window in screen pixels.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when enough of the header is on one screen.
    /// </returns>
    private bool IsHeaderReachable(PixelPoint position)
    {
        var scale = ScreenUnitScale;
        var width = double.IsNaN(Width) ? Bounds.Width : Width;
        var header = new PixelRect(
            position,
            new PixelSize((int)Math.Round(width * scale), Math.Max(1, (int)Math.Round(Header.Bounds.Height * scale))));
        return FullScreenRestore.IsReachable(header, Screens.All.Select(screen => screen.WorkingArea));
    }

    /// <summary>
    /// Remembers the position and the height of the normal window, for the way back from full screen.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the window entered full screen, <see langword="false"/> when it was in it already.
    /// </returns>
    private bool RememberNormalBounds()
    {
        // A width animation that is still running is stopped by going full screen. The window would come back with a width
        // and a position from the middle of it, so it is taken to where the animation was going.
        if (_widthAnimationEnd is { } end)
        {
            _widthAnimationEnd = null;
            _widthAnimation.Stop();
            Width = end.Width;
            Position = end.Position;
        }

        // A window that rests beyond its screen edge is at a hidden position; the one it is shown at is the one to return to.
        var height = double.IsNaN(Height) ? ClientSize.Height : Height;
        return _fullScreenMemory.Enter(Position, height, _dock.SuppressesGeometry);
    }

    /// <summary>
    /// Gets the layout and the compact flag that decide the width of the window.
    /// </summary>
    /// <returns>
    /// The layout and whether compact mode is on; the list layout without compact mode when there is no view model.
    /// </returns>
    private (WatchlistLayout Layout, bool Compact) CurrentLayout() =>
        _observed is { } viewModel
            ? (viewModel.IsCardLayout ? WatchlistLayout.Cards : WatchlistLayout.List, viewModel.Settings.Compact)
            : (WatchlistLayout.List, false);

    /// <summary>
    /// Fits the width of the window to a change of the layout or of compact mode.
    /// </summary>
    /// <param name="sender">
    /// The view model that changed.
    /// </param>
    /// <param name="e">
    /// The property change data.
    /// </param>
    private void OnLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsCardLayout) or nameof(SettingsViewModel.Compact))
        {
            ApplyWidthRule();
        }

        // What the window needs around the list does not change with these, so the last measure is good enough.
        if (e.PropertyName is nameof(MainViewModel.IsCardLayout) or nameof(MainViewModel.CardSize) or nameof(MainViewModel.Grouping))
        {
            ApplyHeightRule();
        }
        else if (e.PropertyName is nameof(SettingsViewModel.AutoHideAtEdge) or nameof(SettingsViewModel.AlwaysOnTop))
        {
            ApplyAutoHide();
        }
    }

    /// <summary>
    /// Gives the window the width its layout asks for: a fixed one in the list layout, the remembered one in the card
    /// layout. A window docked to the left or right edge keeps that edge; otherwise the center stays where it is. The top does not move.
    /// </summary>
    /// <param name="immediate">
    /// Whether the width is set at once instead of being animated.
    /// </param>
    private void ApplyWidthRule(bool immediate = false)
    {
        // A window that fills the screen has no width of its own; the rule is applied when it is back to normal.
        if (WindowState != WindowState.Normal || FillsScreen)
        {
            return;
        }

        var (layout, compact) = CurrentLayout();
        var rule = WindowWidthRules.For(layout, compact);

        // The card layout does not depend on compact mode, so toggling it there must not touch the window.
        if (rule == _appliedRule)
        {
            return;
        }

        _appliedRule = rule;
        var target = WindowWidthRules.TargetWidth(layout, compact, _cardsWidth);
        _widthAnimation.Stop();

        // The width rules keep the window on the screen, which would pull a hidden window half way out, and a slide that
        // is still running would fight the width animation for the position.
        EndDockMotion();
        var current = IsVisible ? ClientSize.Width : Width;

        // Lift the limits first, so that neither the old nor the new ones get in the way of the change.
        MinWidth = 0;
        MaxWidth = double.PositiveInfinity;

        if (Math.Abs(current - target) <= 0.5)
        {
            FinishWidthChange(rule);
        }
        else if (immediate || !IsVisible)
        {
            Width = target;
            FinishWidthChange(rule);
        }
        else
        {
            AnimateWidth(current, target, rule);
        }
    }

    /// <summary>
    /// Keeps the window high enough for one whole card of the shown size in the card layout. What the window needs around
    /// the list is measured, since the portfolio line comes and goes.
    /// </summary>
    private void ApplyHeightRule()
    {
        // Nothing is measured before the XAML is loaded and the first layout pass is done. The window needs no extra height
        // while it fills the screen, but the limit is kept up to date so that the window is high enough when it goes back
        // to normal.
        if (_observed is not { } viewModel || Watchlist is null || Watchlist.Bounds.Height <= 0)
        {
            return;
        }

        var chrome = ClientSize.Height - Watchlist.Bounds.Height;
        var layout = viewModel.IsCardLayout ? WatchlistLayout.Cards : WatchlistLayout.List;
        var minHeight = WindowHeightRules.MinHeight(layout, viewModel.CardSize, viewModel.Grouping != CardGrouping.None, chrome);
        if (layout == WatchlistLayout.List)
        {
            // Rows that are not laid out yet say nothing; the list reports its height when it has one.
            if (Watchlist.ListContentHeight is not { } content)
            {
                return;
            }

            var limit = Screens.ScreenFromWindow(this)?.WorkingArea.Height / ScreenUnitScale ?? double.PositiveInfinity;
            minHeight = WindowHeightRules.ListContentMinHeight(content, chrome, limit);
        }

        if (Math.Abs(MinHeight - minHeight) > 0.5)
        {
            MinHeight = minHeight;
        }
    }

    /// <summary>
    /// Changes the width in small steps, eased out, while the anchor of the docked edge (the center when not docked) stays in place, and applies the width limits
    /// once the target is reached.
    /// </summary>
    /// <param name="from">
    /// The current width in device independent pixels.
    /// </param>
    /// <param name="to">
    /// The target width in device independent pixels.
    /// </param>
    /// <param name="rule">
    /// The limits to apply at the end.
    /// </param>
    private void AnimateWidth(double from, double to, WidthRule rule)
    {
        var scale = ScreenUnitScale;
        var startLeft = Position.X;
        var endLeft = WindowWidthRules.LeftForDock(startLeft, from * scale, to * scale, _dock.Edge);
        if (Screens.ScreenFromWindow(this)?.WorkingArea is { } area)
        {
            endLeft = WindowWidthRules.ClampLeft(endLeft, to * scale, area.X, area.Right);
        }

        var top = Position.Y;
        _widthAnimationEnd = (to, new PixelPoint(endLeft, top));
        _widthAnimation.Run(
            WindowWidthRules.AnimationDuration,
            progress =>
            {
                Width = WindowWidthRules.Animate(from, to, progress);
                Position = new PixelPoint((int)Math.Round(WindowWidthRules.Animate(startLeft, endLeft, progress)), top);
            },
            () => FinishWidthChange(rule));
    }

    /// <summary>
    /// Applies the width limits of a layout once the window has its width.
    /// </summary>
    /// <param name="rule">
    /// The limits to apply.
    /// </param>
    private void FinishWidthChange(WidthRule rule)
    {
        _widthAnimationEnd = null;
        MinWidth = rule.Min;
        MaxWidth = rule.Max;

        // A docked window kept its edge while the width changed; it is put flush with the edge again and hides once the
        // pointer is away.
        ScheduleDockSettle(realign: true);
    }

    /// <summary>
    /// Remembers the card layout width the user chose and reports the new geometry.
    /// </summary>
    /// <param name="sender">
    /// The window.
    /// </param>
    /// <param name="e">
    /// The resize data.
    /// </param>
    private void OnResized(object? sender, WindowResizedEventArgs e)
    {
        // Every resize the application did not ask for counts: the user dragging the grip, but also the system
        // snapping the window or a change of screen scaling. The ones this class asks for are its own width rule.
        if (e.Reason != WindowResizeReason.Application && WindowState == WindowState.Normal && CurrentLayout().Layout == WatchlistLayout.Cards)
        {
            _cardsWidth = Math.Max(e.ClientSize.Width, WindowWidthRules.CardsMinWidth);
        }

        // A shown, docked window that the user resized is put flush with its edge once the size stops changing.
        if (e.Reason == WindowResizeReason.User && _dock.ShouldWaitToHide && !_dock.Sliding)
        {
            ScheduleDockSettle(realign: true);
        }

        RaiseGeometryChanged();
    }

    /// <summary>
    /// Opens the settings window modally.
    /// </summary>
    /// <param name="sender">
    /// The view model.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private async void OnSettingsRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        await ShowToolWindowAsync(viewModel, ToolWindowKind.Settings, new SettingsWindow { DataContext = viewModel.Settings });
    }

    /// <summary>
    /// Opens the price alerts window of an instrument modally.
    /// </summary>
    /// <param name="sender">
    /// The view model.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private async void OnAlertsRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        await ShowToolWindowAsync(viewModel, ToolWindowKind.Alerts, new AlertEditorWindow { DataContext = viewModel.AlertEditor });
    }

    /// <summary>
    /// Opens the holding window of an instrument modally; it closes itself once the holding is saved or cleared.
    /// </summary>
    /// <param name="sender">
    /// The view model.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private async void OnHoldingRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var window = new HoldingEditorWindow { DataContext = viewModel.HoldingEditor };
        void Finish(object? s, EventArgs a) => window.Close();
        viewModel.HoldingEditor.Completed += Finish;
        await ShowToolWindowAsync(viewModel, ToolWindowKind.Holding, window, () => viewModel.HoldingEditor.Completed -= Finish);
    }

    /// <summary>
    /// Opens the window for adding an instrument or changing its source modally.
    /// </summary>
    /// <param name="sender">
    /// The view model.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private async void OnAddAssetRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var window = new AddAssetWindow { DataContext = viewModel.AddAsset };
        void Finish(object? s, EventArgs a) => window.Close();
        viewModel.AddAsset.Completed += Finish;
        await ShowToolWindowAsync(viewModel, ToolWindowKind.AddAsset, window, () => viewModel.AddAsset.Completed -= Finish);
    }

    /// <summary>
    /// Shows a tool window modally and keeps the bookkeeping consistent until it is gone, even when showing it fails.
    /// </summary>
    /// <param name="viewModel">
    /// The main view model.
    /// </param>
    /// <param name="kind">
    /// The kind of window, published to the view model while it is open.
    /// </param>
    /// <param name="window">
    /// The window to show.
    /// </param>
    /// <param name="onClosed">
    /// An optional cleanup to run once the window is gone.
    /// </param>
    /// <returns>
    /// A task that completes when the window is closed.
    /// </returns>
    private async Task ShowToolWindowAsync(MainViewModel viewModel, ToolWindowKind kind, ToolWindow window, Action? onClosed = null)
    {
        var cleanedUp = false;

        void CleanUp()
        {
            if (cleanedUp)
            {
                return;
            }

            cleanedUp = true;
            _toolWindow = null;
            viewModel.EndToolWindow();
            onClosed?.Invoke();
            ResumeDockLeave();
        }

        _toolWindow = window;
        viewModel.BeginToolWindow(kind);
        window.Closed += (_, _) => CleanUp();

        try
        {
            await window.ShowCenteredAsync(this);
        }
        catch (Exception ex)
        {
            // Called from async void event handlers, where an escaping exception would end the application.
            Logger?.LogError(ex, "A tool window could not be shown");
        }
        finally
        {
            CleanUp();
        }
    }

    /// <summary>
    /// Starts dragging the window by its header unless the position is locked.
    /// </summary>
    /// <param name="sender">
    /// The header.
    /// </param>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var locked = (DataContext as MainViewModel)?.Settings.Locked == true;
        if (locked || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (WindowState == WindowState.FullScreen || FillsScreen)
        {
            // A full screen window has nowhere to be dragged to. Leaving full screen right now would turn a double click
            // into "leave and enter again", so it waits until the pointer moved: that is the drag.
            _fullScreenPress = e;
            _fullScreenPressPoint = e.GetPosition(this);
            e.Pointer.Capture(Header);
            return;
        }

        RunDockDrag(() => BeginMoveDrag(e), resize: false);
    }

    /// <summary>
    /// Takes the window out of full screen and carries on dragging it once the pointer moved far enough from where the
    /// header was pressed.
    /// </summary>
    /// <param name="sender">
    /// The header.
    /// </param>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    private void OnHeaderPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_fullScreenPress is not { } press)
        {
            return;
        }

        if (!(WindowState == WindowState.FullScreen || FillsScreen) || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ClearFullScreenPress(e.Pointer);
            return;
        }

        // The system's own distance, which a user can raise, so that a shaky double click is not taken for a drag.
        var tap = Application.Current?.PlatformSettings?.GetTapSize(e.Pointer.Type) ?? new Size(DefaultDragDistance, DefaultDragDistance);
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _fullScreenPressPoint.X) < tap.Width
            && Math.Abs(point.Y - _fullScreenPressPoint.Y) < tap.Height)
        {
            return;
        }

        var pointer = this.PointToScreen(point);
        var scale = ScreenUnitScale;
        ClearFullScreenPress(e.Pointer);
        RunDockDrag(
            () =>
            {
                // The way out of full screen puts back the height and the width of the layout, but not the old place: the
                // window is put with the middle of its header under the pointer, so it follows the pointer from the first
                // moment, in one move.
                LeaveFullScreen(FullScreenExit.Dragged);
                var width = double.IsNaN(Width) ? Bounds.Width : Width;
                var height = double.IsNaN(Height) ? Bounds.Height : Height;
                var size = PixelSize.FromSize(new Size(width, height), scale);
                var workArea = (Screens.ScreenFromPoint(pointer) ?? Screens.Primary)?.WorkingArea ?? new PixelRect(pointer, size);
                var headerMiddleY = (int)Math.Round(Header.Bounds.Height * scale / 2);
                Position = FullScreenRestore.Place(pointer, headerMiddleY, size, workArea);
                BeginMoveDrag(press);
            },
            resize: false);
    }

    /// <summary>
    /// Forgets the press on the header when the button was released or the pointer was taken away.
    /// </summary>
    /// <param name="sender">
    /// The header.
    /// </param>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    private void OnHeaderPointerReleased(object? sender, PointerReleasedEventArgs e) => ClearFullScreenPress(e.Pointer);

    /// <summary>
    /// Forgets the press on the header when the pointer was taken away.
    /// </summary>
    /// <param name="sender">
    /// The header.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnHeaderPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => _fullScreenPress = null;

    /// <summary>
    /// Ends the wait for a drag of the header in full screen.
    /// </summary>
    /// <param name="pointer">
    /// The pointer that pressed the header.
    /// </param>
    private void ClearFullScreenPress(IPointer pointer)
    {
        _fullScreenPress = null;
        if (pointer.Captured == Header)
        {
            pointer.Capture(null);
        }
    }

    /// <summary>
    /// Switches between full screen and the normal window when the header is double-clicked.
    /// </summary>
    /// <param name="sender">
    /// The header.
    /// </param>
    /// <param name="e">
    /// The tap event data.
    /// </param>
    private void OnHeaderDoubleTapped(object? sender, TappedEventArgs e) => ToggleFullScreen();

    /// <summary>
    /// Leaves full screen when the view model asks for it, because the list layout was chosen.
    /// </summary>
    /// <param name="sender">
    /// The view model.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnFullScreenExitRequested(object? sender, EventArgs e) => LeaveFullScreen(FullScreenExit.KeepLayoutSize);

    /// <summary>
    /// Switches between full screen and the normal window when the full screen button is clicked.
    /// </summary>
    /// <param name="sender">
    /// The button.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnFullScreenClicked(object? sender, RoutedEventArgs e) => ToggleFullScreen();

    /// <summary>
    /// Asks the system to leave its full screen space and gives the window back once the system is done with it.
    /// </summary>
    /// <param name="exit">
    /// How the window leaves full screen.
    /// </param>
    private void BeginNativeLeave(FullScreenExit exit)
    {
        _macPhase = MacFullScreenPhase.Leaving;
        _fullScreenAnimation.Stop();
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (MacPresentation.IsFullScreen(handle))
        {
            MacPresentation.ToggleFullScreen(handle);
        }

        var lastSize = ClientSize;
        var steady = 0;
        WaitUntil(
            () =>
            {
                // Out of the space, and the size has stopped changing: the system is done with the window.
                if (MacPresentation.IsFullScreen(handle))
                {
                    steady = 0;
                    return false;
                }

                steady = ClientSize == lastSize ? steady + 1 : 0;
                lastSize = ClientSize;
                return steady >= 4;
            },
            () => FinishNativeLeave(exit, handle));
    }

    /// <summary>
    /// Gives the window back to the application after the system left its full screen space, and shrinks it to its normal
    /// frame.
    /// </summary>
    /// <param name="exit">
    /// How the window leaves full screen.
    /// </param>
    /// <param name="handle">
    /// The <c>NSWindow</c> of the window.
    /// </param>
    private void FinishNativeLeave(FullScreenExit exit, IntPtr handle)
    {
        _macPhase = MacFullScreenPhase.Native;
        if (MacPresentation.IsFullScreen(handle))
        {
            // The system did not get out of its full screen space in time. The window stays full screen for the
            // application, so that both agree, and the user can ask again.
            return;
        }

        _macPhase = MacFullScreenPhase.None;
        _fullScreenExit = exit;
        var fromPosition = Position;
        var fromWidth = ClientSize.Width;
        var fromHeight = ClientSize.Height;
        var restoredAt = exit == FullScreenExit.Restore && _fullScreenMemory.Bounds is { } record && IsHeaderReachable(record.Position)
            ? record.Position
            : (PixelPoint?)null;
        try
        {
            if (WindowState == WindowState.Normal)
            {
                OnWindowStateChanged();
            }
            else
            {
                WindowState = WindowState.Normal;
            }
        }
        finally
        {
            _fullScreenExit = FullScreenExit.Restore;
        }

        // The window is back where it was; it is taken back to the size it has now and shrinks to it. The position is read
        // from the record: the window only reports a position it was given once the system has moved it.
        var toPosition = restoredAt ?? Position;
        var toWidth = double.IsNaN(Width) ? fromWidth : Width;
        var toHeight = double.IsNaN(Height) ? fromHeight : Height;
        var limits = (MinWidth, MaxWidth, MinHeight, MaxHeight);
        MinWidth = 0;
        MaxWidth = double.PositiveInfinity;
        MinHeight = 0;
        MaxHeight = double.PositiveInfinity;
        Position = fromPosition;
        Width = fromWidth;
        Height = fromHeight;
        AnimateFrame(
            toPosition,
            toWidth,
            toHeight,
            () =>
            {
                (MinWidth, MaxWidth, MinHeight, MaxHeight) = limits;
                RaiseGeometryChanged();
            });
    }

    /// <summary>
    /// Enters the full screen space of macOS. A borderless window may be left at its size by the system, which is noticed a
    /// moment later; the window then fills the screen itself instead.
    /// </summary>
    private void EnterMacFullScreen()
    {
        // Avalonia asks for the full screen space only when it kept the window resizable, which a borderless one is not, so
        // the window is made ready and the system is asked directly.
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        MacPresentation.PrepareFullScreen(handle);

        // Taken down before the system moves the window, so that the way back leads to where it was. From here on the
        // application treats the window as full screen, so the moves of the system are not taken for the user's.
        if (RememberNormalBounds())
        {
            FullScreenChanged?.Invoke(this, true);
        }

        _widthBeforeFullScreen = double.IsNaN(Width) ? Bounds.Width : Width;
        _macPhase = MacFullScreenPhase.Native;
        OnWindowStateChanged();
        MinHeight = 0;
        var target = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (target is null)
        {
            BeginMacFullScreen(handle);
            return;
        }

        // The window grows to what the full screen space gives it before the system switches, so that nothing jumps.
        var area = target.WorkingArea;
        AnimateFrame(
            new PixelPoint(target.Bounds.X, area.Y),
            target.Bounds.Width,
            target.Bounds.Bottom - area.Y,
            () => BeginMacFullScreen(handle));
    }

    /// <summary>
    /// Asks the system for the full screen space and sizes the window once it is there.
    /// </summary>
    /// <param name="handle">
    /// The <c>NSWindow</c> of the window.
    /// </param>
    private void BeginMacFullScreen(IntPtr handle)
    {
        if (_macPhase != MacFullScreenPhase.Native)
        {
            return;
        }

        if (!IsVisible)
        {
            // Hidden to the tray while the window grew: the system does not enter its space for a hidden window, so the
            // window is not left in a full screen state that nothing completes.
            _macPhase = MacFullScreenPhase.None;
            OnWindowStateChanged();
            return;
        }

        MacPresentation.ToggleFullScreen(handle);
        _macPhase = MacFullScreenPhase.Entering;
        WaitUntil(
            () => _macPhase != MacFullScreenPhase.Entering || MacPresentation.IsFullScreen(handle),
            () =>
            {
                if (_macPhase == MacFullScreenPhase.Entering)
                {
                    _macPhase = MacFullScreenPhase.Native;
                }

                if (_pendingLeave is { } pending)
                {
                    _pendingLeave = null;
                    if (_macPhase == MacFullScreenPhase.Native)
                    {
                        LeaveFullScreen(pending);
                    }

                    return;
                }

                if (_macPhase != MacFullScreenPhase.Native)
                {
                    return;
                }

                var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
                if (!MacPresentation.IsFullScreen(handle) || screen is null)
                {
                    // The system did not make the window full screen; it fills the screen itself.
                    _macPhase = MacFullScreenPhase.None;
                    FillScreen();
                    return;
                }

                // The system leaves the window at its size inside the full screen space, so it is given the size of the
                // screen. The system keeps it below the camera housing of a screen that has one, as it does for any window.
                Width = screen.Bounds.Width;
                Height = screen.Bounds.Height;
            });
    }

    /// <summary>
    /// Moves and resizes the window to a frame in small steps, eased out.
    /// </summary>
    /// <param name="position">
    /// The position to reach.
    /// </param>
    /// <param name="width">
    /// The width to reach.
    /// </param>
    /// <param name="height">
    /// The height to reach.
    /// </param>
    /// <param name="done">
    /// Called once the window is there.
    /// </param>
    private void AnimateFrame(PixelPoint position, double width, double height, Action done)
    {
        var startPosition = Position;
        var startWidth = ClientSize.Width;
        var startHeight = ClientSize.Height;
        _fullScreenAnimation.Run(
            TimeSpan.FromMilliseconds(220),
            progress =>
            {
                Position = new PixelPoint(
                    (int)Math.Round(WindowWidthRules.Animate(startPosition.X, position.X, progress)),
                    (int)Math.Round(WindowWidthRules.Animate(startPosition.Y, position.Y, progress)));
                Width = WindowWidthRules.Animate(startWidth, width, progress);
                Height = WindowWidthRules.Animate(startHeight, height, progress);
            },
            done);
    }

    /// <summary>
    /// Runs an action as soon as a condition holds, looking at it often, or when the time is up. The system does not tell
    /// when it has entered or left the full screen space, but sets a style bit of the window at the start.
    /// </summary>
    /// <param name="condition">
    /// What to wait for.
    /// </param>
    /// <param name="action">
    /// What to do then; it also runs when the time is up, and looks at the state itself.
    /// </param>
    private void WaitUntil(Func<bool> condition, Action action)
    {
        // One wait at a time: a new one replaces the one that is still running instead of racing it.
        CancelWait();
        var started = DateTime.UtcNow;
        DispatcherTimer? timer = null;
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Normal, (_, _) =>
        {
            if (condition() || DateTime.UtcNow - started > TimeSpan.FromSeconds(2))
            {
                timer?.Stop();
                if (ReferenceEquals(_waitTimer, timer))
                {
                    _waitTimer = null;
                }

                action();
            }
        });
        _waitTimer = timer;
        timer.Start();
    }

    /// <summary>
    /// Stops the wait that is running, if any, without running its action.
    /// </summary>
    private void CancelWait()
    {
        _waitTimer?.Stop();
        _waitTimer = null;
    }

    /// <summary>
    /// Stops the wait for the system, which has no one left to act on.
    /// </summary>
    /// <param name="e">
    /// The event data.
    /// </param>
    protected override void OnClosed(EventArgs e)
    {
        CancelWait();
        _fullScreenAnimation.Stop();
        if (FillsScreen)
        {
            // The menu bar and the Dock are the system's, and do not come back by themselves.
            MacPresentation.SetBarsHidden(false);
        }

        base.OnClosed(e);
    }

    /// <summary>
    /// Makes the window as large as the screen it is on, which is what full screen is on macOS. The system's own full
    /// screen leaves a borderless window at its size.
    /// </summary>
    private void FillScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null || WindowState != WindowState.Normal)
        {
            return;
        }

        var entered = RememberNormalBounds();
        _macPhase = MacFullScreenPhase.Own;
        MacPresentation.SetBarsHidden(IsVisible);
        OnWindowStateChanged();
        _widthBeforeFullScreen ??= double.IsNaN(Width) ? Bounds.Width : Width;

        // The screen is measured in points here, which is what the size of a window is measured in.
        MinHeight = 0;
        Position = screen.Bounds.Position;
        Width = screen.Bounds.Width;
        Height = screen.Bounds.Height;
        if (entered)
        {
            FullScreenChanged?.Invoke(this, true);
        }
    }

    /// <summary>
    /// Fills the screen with the window, or brings it back to its normal size.
    /// </summary>
    private void ToggleFullScreen()
    {
        if (WindowState == WindowState.Normal && !FillsScreen)
        {
            // The list is a narrow strip and has no full screen form; only the cards fill the screen.
            if (CurrentLayout().Layout != WatchlistLayout.Cards)
            {
                return;
            }

            if (OperatingSystem.IsMacOS())
            {
                EnterMacFullScreen();
            }
            else
            {
                WindowState = WindowState.FullScreen;
            }
        }
        else
        {
            LeaveFullScreen(FullScreenExit.Restore);
        }
    }

    /// <summary>
    /// Starts resizing from the bottom-right grip.
    /// </summary>
    /// <param name="sender">
    /// The grip.
    /// </param>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    private void OnResizeGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // macOS does not carry out a resize the window asks the system for, so the grip resizes the window itself there.
        if (OperatingSystem.IsMacOS() && sender is IInputElement grip)
        {
            if (FillsScreen)
            {
                // A window that fills the screen has nothing to resize.
                return;
            }

            var width = double.IsNaN(Width) ? Bounds.Width : Width;
            var height = double.IsNaN(Height) ? Bounds.Height : Height;
            _gripDrag = new GripDrag(ScreenPointer(e), width, height);
            _dock.SetDragging(true);
            e.Pointer.Capture(grip);
            return;
        }

        RunDockDrag(() => BeginResizeDrag(WindowEdge.SouthEast, e), resize: true);
    }

    /// <summary>
    /// Gets the pointer position in points, the unit the size of a window is measured in. The system is asked for it,
    /// because the position the window converts from its own coordinates is off while the window changes size: macOS counts
    /// from the bottom of the screen, and the window only learns its new size after the fact.
    /// </summary>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    /// <returns>
    /// The position in points.
    /// </returns>
    private Point ScreenPointer(PointerEventArgs e) =>
        MacTrayCursor.TryGetPoints(Screens, out var x, out var y)
            ? new Point(x, y)
            : this.PointToScreen(e.GetPosition(this)).ToPoint(1) / ScreenUnitScale;

    /// <summary>
    /// Follows the pointer with the bottom-right corner while the grip is dragged on macOS.
    /// </summary>
    /// <param name="sender">
    /// The grip.
    /// </param>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    private void OnResizeGripMoved(object? sender, PointerEventArgs e)
    {
        if (_gripDrag is not { } drag)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // The release was missed, e.g. because the window was deactivated during the drag.
            EndGripDrag();
            return;
        }

        var pointer = ScreenPointer(e);
        var height = Math.Max(MinHeight, Math.Min(MaxHeight, drag.Height + (pointer.Y - drag.Pointer.Y)));
        Height = height;

        // The list layout has a fixed width, so there the grip only resizes vertically.
        if (CurrentLayout().Layout == WatchlistLayout.Cards)
        {
            var width = Math.Max(Math.Max(MinWidth, WindowWidthRules.CardsMinWidth), Math.Min(MaxWidth, drag.Width + (pointer.X - drag.Pointer.X)));
            Width = width;
            _cardsWidth = width;
        }

        RaiseGeometryChanged();
    }

    /// <summary>
    /// Ends the resize by the grip on macOS.
    /// </summary>
    /// <param name="sender">
    /// The grip.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnResizeGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer.Captured == sender)
        {
            e.Pointer.Capture(null);
        }

        EndGripDrag();
    }

    /// <summary>
    /// Ends the resize by the grip on macOS when the pointer was taken away.
    /// </summary>
    /// <param name="sender">
    /// The grip.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnResizeGripCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndGripDrag();

    /// <summary>
    /// Lets the window settle at its edge again after the resize by the grip.
    /// </summary>
    private void EndGripDrag()
    {
        if (_gripDrag is null)
        {
            return;
        }

        _gripDrag = null;
        _dock.SetDragging(false);
        if (_dock.ShouldWaitToHide)
        {
            ScheduleDockSettle(realign: true);
        }
    }

    /// <summary>
    /// Closes the window, which hides it to the tray or exits the app according to the setting; see <see cref="OnClosing"/>.
    /// </summary>
    /// <param name="sender">
    /// The button.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Asks for the name of a new group next to the grouping button.
    /// </summary>
    /// <param name="sender">
    /// The menu item.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnNewGroupClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            GroupNamePrompt.Show(GroupButton, Loc.Get("Group_NewTitle"), PlacementMode.BottomEdgeAlignedRight, name => viewModel.CreateGroupAsync(name));
        }
    }

    /// <summary>
    /// Raises <see cref="GeometryChanged"/> with the current geometry.
    /// </summary>
    private void RaiseGeometryChanged()
    {
        // A window that slides or rests beyond its edge would be saved off the screen; the position it is shown at
        // is the one that is remembered.
        if (_dock.SuppressesGeometry || WindowState != WindowState.Normal || FillsScreen)
        {
            return;
        }

        GeometryChanged?.Invoke(this, new WindowGeometry(Position.X, Position.Y, _cardsWidth, Height));
    }

    /// <summary>
    /// Determines whether a point lies on the usable area of any connected screen, so a window whose corner is under
    /// the taskbar does not count as visible.
    /// </summary>
    /// <param name="point">
    /// The point in screen pixels.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the point is on a screen.
    /// </returns>
    private bool IsOnAnyScreen(PixelPoint point) => Screens.All.Any(s => s.WorkingArea.Contains(point));

    /// <summary>
    /// Where a resize by the grip began.
    /// </summary>
    /// <param name="Pointer">
    /// The pointer position in points.
    /// </param>
    /// <param name="Width">
    /// The width of the window.
    /// </param>
    /// <param name="Height">
    /// The height of the window.
    /// </param>
    private sealed record GripDrag(Point Pointer, double Width, double Height);

    /// <summary>
    /// Gets a value indicating whether the window fills the screen on macOS, by the system's full screen space or by its own
    /// size.
    /// </summary>
    private bool FillsScreen => _macPhase != MacFullScreenPhase.None;

    /// <summary>
    /// Gets a value indicating whether the system's full screen space is what the window is in on macOS, or is asked to enter.
    /// </summary>
    private bool InNativeSpace => _macPhase is MacFullScreenPhase.Native or MacFullScreenPhase.Entering or MacFullScreenPhase.Leaving;

    /// <summary>
    /// The stages of full screen on macOS, which are exclusive: the flags they replace could be combined in ways that mean
    /// nothing.
    /// </summary>
    private enum MacFullScreenPhase
    {
        /// <summary>
        /// The window is not full screen.
        /// </summary>
        None,

        /// <summary>
        /// The window fills the screen by its own size, without the full screen space of the system, which leaves a
        /// borderless window at its size.
        /// </summary>
        Own,

        /// <summary>
        /// The window is in the full screen space of the system, or about to ask for it.
        /// </summary>
        Native,

        /// <summary>
        /// The system has been asked for the full screen space and has not got there yet.
        /// </summary>
        Entering,

        /// <summary>
        /// The window is on its way out of the full screen space of the system.
        /// </summary>
        Leaving,
    }
}
