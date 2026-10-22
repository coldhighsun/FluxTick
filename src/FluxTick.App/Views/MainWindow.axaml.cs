using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;

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
