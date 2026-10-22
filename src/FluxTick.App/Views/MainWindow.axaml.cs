using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        PositionChanged += (_, _) => RaiseGeometryChanged();
        Resized += (_, _) => RaiseGeometryChanged();
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
        Width = Math.Max(saved.Width, MinWidth);
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
        }

        _observed = DataContext as MainViewModel;
        if (_observed is not null)
        {
            _observed.SettingsRequested += OnSettingsRequested;
            _observed.AddAssetRequested += OnAddAssetRequested;
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
    /// Raises <see cref="GeometryChanged"/> with the current geometry.
    /// </summary>
    private void RaiseGeometryChanged() =>
        GeometryChanged?.Invoke(this, new WindowGeometry(Position.X, Position.Y, Width, Height));

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
