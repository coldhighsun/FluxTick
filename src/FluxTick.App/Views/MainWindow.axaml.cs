using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
    /// Steps the width animation; created on first use.
    /// </summary>
    private DispatcherTimer? _widthTimer;

    /// <summary>
    /// The handler of the current width animation, kept so it can be detached before the next one starts.
    /// </summary>
    private EventHandler _widthStep = (_, _) => { };

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
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        Width = WindowWidthRules.ListWidthWithChart;
        MinWidth = WindowWidthRules.CardsMinWidth;
        PositionChanged += (_, _) => RaiseGeometryChanged();
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

        var area = Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
        var scale = Screens.Primary?.Scaling ?? 1;
        Position = new PixelPoint(
            area.Right - (int)(Width * scale) - ScreenMargin,
            area.Y + ScreenMargin);
    }

    /// <summary>
    /// Shows the window when hidden, hides it when visible.
    /// </summary>
    public void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        Show();
        Activate();
    }

    /// <summary>
    /// Hides the window to the tray instead of closing it, unless the application is exiting.
    /// </summary>
    /// <param name="e">
    /// The closing event data.
    /// </param>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (Application.Current is App { IsExiting: false })
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
            _observed.PropertyChanged -= OnLayoutPropertyChanged;
            _observed.Settings.PropertyChanged -= OnLayoutPropertyChanged;
        }

        _observed = DataContext as MainViewModel;
        if (_observed is not null)
        {
            _observed.SettingsRequested += OnSettingsRequested;
            _observed.AddAssetRequested += OnAddAssetRequested;
            _observed.PropertyChanged += OnLayoutPropertyChanged;
            _observed.Settings.PropertyChanged += OnLayoutPropertyChanged;
        }

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
    }

    /// <summary>
    /// Gives the window the width its layout asks for: a fixed one in the list layout, the remembered one in the card
    /// layout. The right edge stays where it is, so the window does not leave the screen it is docked to.
    /// </summary>
    private void ApplyWidthRule()
    {
        var (layout, compact) = CurrentLayout();
        var rule = WindowWidthRules.For(layout, compact);

        // The card layout does not depend on compact mode, so toggling it there must not touch the window.
        if (rule == _appliedRule)
        {
            return;
        }

        _appliedRule = rule;
        var target = WindowWidthRules.TargetWidth(layout, compact, _cardsWidth);
        _widthTimer?.Stop();
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
        var clock = Stopwatch.StartNew();
        _widthTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        _widthTimer.Tick -= _widthStep;
        _widthStep = (_, _) =>
        {
            var progress = clock.Elapsed / WindowWidthRules.AnimationDuration;
            Width = WindowWidthRules.Animate(from, to, progress);
            Position = new PixelPoint((int)Math.Round(WindowWidthRules.Animate(startLeft, endLeft, progress)), top);
            if (progress >= 1)
            {
                _widthTimer.Stop();
                FinishWidthChange(rule);
            }
        };
        _widthTimer.Tick += _widthStep;
        _widthTimer.Start();
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
        if (e.Reason != WindowResizeReason.Application && CurrentLayout().Layout == WatchlistLayout.Cards)
        {
            _cardsWidth = Math.Max(e.ClientSize.Width, WindowWidthRules.CardsMinWidth);
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
            BeginMoveDrag(e);
        }
    }

    /// <summary>
    /// Toggles the compact setting when the header is double-clicked.
    /// </summary>
    /// <param name="sender">
    /// The header.
    /// </param>
    /// <param name="e">
    /// The tap event data.
    /// </param>
    private void OnHeaderDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Settings.Compact = !viewModel.Settings.Compact;
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
            BeginResizeDrag(WindowEdge.SouthEast, e);
        }
    }

    /// <summary>
    /// Hides the window to the tray.
    /// </summary>
    /// <param name="sender">
    /// The button.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnHideClicked(object? sender, RoutedEventArgs e) => Hide();

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
            GroupNamePrompt.Show(GroupButton, "New group", PlacementMode.BottomEdgeAlignedRight, name => viewModel.CreateGroupAsync(name));
        }
    }

    /// <summary>
    /// Raises <see cref="GeometryChanged"/> with the current geometry.
    /// </summary>
    private void RaiseGeometryChanged() =>
        GeometryChanged?.Invoke(this, new WindowGeometry(Position.X, Position.Y, _cardsWidth, Height));

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
