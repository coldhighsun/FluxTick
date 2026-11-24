using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using FluxTick.App.Services;

namespace FluxTick.App.Views;

/// <summary>
/// Shows one instrument on a window that fills the screen. The window shares the row of the list, so it is live without
/// a feed of its own; Esc or a double-click ends it. Dragging it from any free spot makes it half transparent while it
/// follows the pointer, and on release it fills the monitor it was dropped on.
/// </summary>
public sealed partial class AssetFocusWindow : Window
{
    /// <summary>
    /// Gets how long after a drop a double-click does not close the window.
    /// </summary>
    private static readonly TimeSpan DropGrace = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// Whether the left button is down and may turn into a drag.
    /// </summary>
    private bool _pressed;

    /// <summary>
    /// Where the pointer was pressed, relative to the window.
    /// </summary>
    private Point _pressPoint;

    /// <summary>
    /// Whether the window is being carried to another monitor.
    /// </summary>
    private bool _dragging;

    /// <summary>
    /// Where the pointer is held on the carried window, in screen pixels from its top-left corner.
    /// </summary>
    private PixelPoint _grab;

    /// <summary>
    /// When the window was last dropped.
    /// </summary>
    private DateTimeOffset _lastDrop = DateTimeOffset.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetFocusWindow"/> class.
    /// </summary>
    public AssetFocusWindow()
    {
        InitializeComponent();
        KeyDown += OnWindowKeyDown;
        DoubleTapped += OnWindowDoubleTapped;

        // Handled events count too, so that a control that takes the pointer does not stop a drag.
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(PointerMovedEvent, OnWindowPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(PointerReleasedEvent, OnWindowPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        PointerCaptureLost += OnWindowPointerCaptureLost;
        ApplySizes(Height);
    }

    /// <summary>
    /// Sizes the text again when the window changes height.
    /// </summary>
    /// <param name="e">The event data.</param>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ApplySizes(e.NewSize.Height);
    }

    /// <summary>
    /// Gives the keyboard focus to the window, so that Esc works at once.
    /// </summary>
    /// <param name="e">The event data.</param>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Focus();
    }

    /// <summary>
    /// Sets the font sizes from the height of the window.
    /// </summary>
    /// <param name="height">The height of the window.</param>
    private void ApplySizes(double height)
    {
        var detail = AssetFocusRules.DetailFontSize(height);
        var name = AssetFocusRules.NameFontSize(height);
        var change = AssetFocusRules.ChangeFontSize(height);
        Set("PriceText", AssetFocusRules.PriceFontSize(height));
        Set("ChangeText", change);
        Set("StatusText", change);
        Set("AmountText", detail * 1.4);
        Set("BasisText", detail);
        Set("NameText", name);
        Set("SymbolText", name * 0.7);
        Set("ProviderText", detail);

        foreach (var block in this.GetVisualDescendants().OfType<TextBlock>().Where(b => b.Classes.Contains("detail")))
        {
            block.FontSize = detail;
        }
    }

    /// <summary>
    /// Sets the font size of a named text.
    /// </summary>
    /// <param name="name">The name of the text.</param>
    /// <param name="size">The font size.</param>
    private void Set(string name, double size)
    {
        if (this.FindControl<TextBlock>(name) is { } block)
        {
            block.FontSize = size;
        }
    }

    /// <summary>
    /// Closes the window on Esc.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The event data.</param>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    /// <summary>
    /// Closes the window on a double-click.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The event data.</param>
    private void OnWindowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_dragging || DateTimeOffset.UtcNow - _lastDrop < DropGrace)
        {
            return;
        }

        Close();
    }

    /// <summary>
    /// Remembers a left press, which becomes a drag once the pointer moves far enough.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The event data.</param>
    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_dragging || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pressed = true;
        _pressPoint = e.GetPosition(this);
    }

    /// <summary>
    /// Starts the drag when the pointer has moved far enough from the press, and carries the window while it goes on.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The event data.</param>
    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging)
        {
            var pointer = ScreenPointer(e);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                Position = pointer - _grab;
            }
            else
            {
                // The release was missed; drop the window where it is.
                FinishDrag(pointer);
            }

            return;
        }

        if (!_pressed || WindowState != WindowState.FullScreen)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pressed = false;
            return;
        }

        if (AssetFocusDragRules.ExceedsThreshold(_pressPoint, e.GetPosition(this)))
        {
            StartDrag(e);
        }
    }

    /// <summary>
    /// Drops the window where the button was released.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The event data.</param>
    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressed = false;
        if (_dragging)
        {
            FinishDrag(ScreenPointer(e));
        }
    }

    /// <summary>
    /// Takes the pointer back when the change of state of the window took it, so that the drag goes on.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The event data.</param>
    private void OnWindowPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_dragging)
        {
            e.Pointer.Capture(this);
        }
    }

    /// <summary>
    /// Makes the window half transparent and keeps it under the pointer while the pointer moves.
    /// </summary>
    /// <param name="e">The event of the pointer move.</param>
    private void StartDrag(PointerEventArgs e)
    {
        _pressed = false;
        if (Screens.ScreenFromWindow(this) is not { } screen)
        {
            return;
        }

        var pointer = ScreenPointer(e);
        var size = AssetFocusDragRules.CarriedSize(screen.Bounds, screen.Scaling);
        var origin = AssetFocusDragRules.CarriedOrigin(pointer, screen.Bounds, size, screen.Scaling);

        _dragging = true;
        _grab = pointer - origin;
        Opacity = AssetFocusDragRules.CarriedOpacity;
        WindowState = WindowState.Normal;
        Width = size.Width;
        Height = size.Height;
        Position = origin;

        // The change of state may have taken the pointer from the window; keep receiving its moves.
        e.Pointer.Capture(this);
    }

    /// <summary>
    /// Gets where the pointer is on the desktop. While the window follows the pointer, a position worked out from the event
    /// lags behind the window and makes it flicker, so the position of the cursor itself is read where the platform gives it.
    /// </summary>
    /// <param name="e">The pointer event.</param>
    /// <returns>The position, in screen pixels.</returns>
    private PixelPoint ScreenPointer(PointerEventArgs e)
    {
        return CursorPosition.TryGet(out var position) ? position : this.PointToScreen(e.GetPosition(this));
    }

    /// <summary>
    /// Fills the monitor the window was dropped on.
    /// </summary>
    /// <param name="pointer">Where the window was dropped, in screen pixels.</param>
    private void FinishDrag(PixelPoint pointer)
    {
        _dragging = false;
        Opacity = 1;
        _lastDrop = DateTimeOffset.UtcNow;
        var screen = Screens.ScreenFromPoint(pointer) ?? Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is not null)
        {
            Position = screen.Bounds.Position;
        }

        WindowState = WindowState.FullScreen;
    }
}
