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
    /// Raised when the window moved to, or came back from, its hidden position at a screen edge.
    /// </summary>
    public event EventHandler? DockedAwayChanged;

    /// <summary>
    /// Gets a value indicating whether the window rests in its hidden position at a screen edge, where only a sliver of
    /// it can be seen and no prices are read.
    /// </summary>
    public bool IsDockedAway => _dock.Hidden;

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
        _dock.HiddenChanged += (_, _) => DockedAwayChanged?.Invoke(this, EventArgs.Empty);
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
            return;
        }

        Position = DefaultPosition();
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
    public void ToggleVisibility()
    {
        // A minimized window is visible to the shell but out of sight, and may have no taskbar button to restore it
        // from: the tray click brings it back instead of hiding it.
        if (RestoreIfMinimized())
        {
            RevealDocked();
            return;
        }

        switch (_dock.ToggleFromTray(IsVisible))
        {
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
        if (Application.Current is App { IsExiting: false, CloseToTray: true })
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
            _observed.AboutRequested -= OnAboutRequested;
            _observed.AlertsRequested -= OnAlertsRequested;
            _observed.FullScreenExitRequested -= OnFullScreenExitRequested;
            _observed.PropertyChanged -= OnLayoutPropertyChanged;
            _observed.Settings.PropertyChanged -= OnLayoutPropertyChanged;
        }

        _observed = DataContext as MainViewModel;
        if (_observed is not null)
        {
            _observed.SettingsRequested += OnSettingsRequested;
            _observed.AddAssetRequested += OnAddAssetRequested;
            _observed.AboutRequested += OnAboutRequested;
            _observed.AlertsRequested += OnAlertsRequested;
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
            WindowState = WindowState.Normal;
        }
    }

    /// <summary>
    /// Makes room for the window to fill the screen and shows the cards there, and takes the width rules, the layout and
    /// the edge hiding up again when it is back to its normal size. Nothing of the full screen window is remembered: the size and the position that are saved
    /// stay those of the normal window.
    /// </summary>
    private void OnWindowStateChanged()
    {
        // Minimizing or maximizing is not full screen: the layout stays, and whatever is running keeps running.
        if (WindowState is WindowState.Minimized or WindowState.Maximized)
        {
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
        var (layout, compact) = CurrentLayout();
        if (_appliedRule != default && WindowWidthRules.For(layout, compact) == _appliedRule)
        {
            FinishWidthChange(_appliedRule);
        }
        else
        {
            // The layout was changed while the window filled the screen.
            _appliedRule = default;
            ApplyWidthRule();
        }

        ResumeDockLeave();
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
    private void ApplyWidthRule()
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
        else if (!IsVisible)
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
    /// Opens the about window modally.
    /// </summary>
    /// <param name="sender">
    /// The view model.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private async void OnAboutRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        await ShowToolWindowAsync(viewModel, ToolWindowKind.About, new AboutWindow { DataContext = new AboutViewModel(viewModel.Settings.Updates) });
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
        if (!locked && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            RunDockDrag(() => BeginMoveDrag(e), resize: false);
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
    private void OnFullScreenExitRequested(object? sender, EventArgs e) => WindowState = WindowState.Normal;

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
    private void ToggleFullScreen() =>
        WindowState = WindowState == WindowState.Normal ? WindowState.FullScreen : WindowState.Normal;

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
    /// Determines whether a point lies on any connected screen.
    /// </summary>
    /// <param name="point">
    /// The point in screen pixels.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the point is on a screen.
    /// </returns>
    private bool IsOnAnyScreen(PixelPoint point) => Screens.All.Any(s => s.Bounds.Contains(point));
}
