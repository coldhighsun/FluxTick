using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluxTick.App.Localization;
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
        if (saved.FullScreen)
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
        var scale = Screens.Primary?.Scaling ?? 1;
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
        if (!e.Handled && e.Key == Key.Escape && WindowState == WindowState.FullScreen)
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
            WindowState = WindowState.Normal;
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
        if (WindowState == WindowState.FullScreen)
        {
            // The list layout has a fixed width, which would keep the window from filling the screen.
            MinWidth = 0;
            MaxWidth = double.PositiveInfinity;
            (DataContext as MainViewModel)?.SetFullScreen(true);
            return;
        }

        (DataContext as MainViewModel)?.SetFullScreen(false);
        var leave = _fullScreenMemory.Leave(WindowState, _fullScreenExit);
        try
        {
            // Whatever way the user left full screen, the window comes back as it was, not as large as the screen.
            if (leave.RestoreSize)
            {
                var screen = (_fullScreenMemory.Bounds is { } known ? Screens.ScreenFromPoint(known.Position) : null)
                    ?? Screens.ScreenFromWindow(this)
                    ?? Screens.Primary;
                var maxHeight = screen is null ? double.PositiveInfinity : screen.WorkingArea.Height / screen.Scaling;
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
        var scale = RenderScaling;
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
        else if (e.PropertyName is nameof(SettingsViewModel.AutoHideAtEdge) or nameof(SettingsViewModel.AlwaysOnTop))
        {
            ApplyAutoHide();
        }
    }

    /// <summary>
    /// Gives the window the width its layout asks for: a fixed one in the list layout, the remembered one in the card
    /// layout. The right edge stays where it is, so the window does not leave the screen it is docked to.
    /// </summary>
    /// <param name="immediate">
    /// Whether the width is set at once instead of being animated.
    /// </param>
    private void ApplyWidthRule(bool immediate = false)
    {
        // A window that fills the screen has no width of its own; the rule is applied when it is back to normal.
        if (WindowState != WindowState.Normal)
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
    /// Changes the width in small steps, eased out, while the right edge stays in place, and applies the width limits
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
        var scale = RenderScaling;
        var startLeft = Position.X;
        var endLeft = WindowWidthRules.LeftForRightEdge(startLeft, from * scale, to * scale);
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
            await window.ShowBesideAsync(this);
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

        if (WindowState == WindowState.FullScreen)
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

        if (WindowState != WindowState.FullScreen || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
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
        var scale = RenderScaling;
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
    /// Fills the screen with the window, or brings it back to its normal size.
    /// </summary>
    private void ToggleFullScreen()
    {
        if (WindowState == WindowState.Normal)
        {
            WindowState = WindowState.FullScreen;
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
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            RunDockDrag(() => BeginResizeDrag(WindowEdge.SouthEast, e), resize: true);
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
        if (_dock.SuppressesGeometry || WindowState != WindowState.Normal)
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
}
