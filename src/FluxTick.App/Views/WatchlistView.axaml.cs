using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluxTick.App.ViewModels;

namespace FluxTick.App.Views;

/// <summary>
/// The scrolling list of watched instruments. Rows can be reordered by pressing and dragging them vertically.
/// </summary>
public sealed partial class WatchlistView : UserControl
{
    /// <summary>
    /// The vertical distance the pointer must travel before a press turns into a drag.
    /// </summary>
    private const double DragThreshold = 4;

    /// <summary>
    /// The height of the zone at the top and bottom of the list that scrolls it while dragging.
    /// </summary>
    private const double EdgeScrollZone = 28;

    /// <summary>
    /// The number of device-independent pixels scrolled per pointer move inside the edge zone.
    /// </summary>
    private const double EdgeScrollStep = 14;

    /// <summary>
    /// The panel that hosts the row containers while a press or drag is in progress.
    /// </summary>
    private Panel? _panel;

    /// <summary>
    /// The pointer that started the current press or drag.
    /// </summary>
    private IPointer? _pointer;

    /// <summary>
    /// The top level the Escape handler was attached to, kept so the same instance is used to detach it.
    /// </summary>
    private TopLevel? _keyHost;

    /// <summary>
    /// One reusable translate transform per row, applied for the duration of a drag.
    /// </summary>
    private List<TranslateTransform> _transforms = [];

    /// <summary>
    /// The original top and height of every row, captured when the drag starts.
    /// </summary>
    private List<(double Top, double Height)> _slots = [];

    /// <summary>
    /// The row being dragged, captured when the drag starts so a changed collection cannot redirect the drop.
    /// </summary>
    private AssetRowViewModel? _draggedRow;

    /// <summary>
    /// The index of the pressed row, or -1 when no press is in progress.
    /// </summary>
    private int _pressedIndex = -1;

    /// <summary>
    /// The index the dragged row would currently drop into.
    /// </summary>
    private int _targetIndex;

    /// <summary>
    /// The pointer's vertical position within the panel when it was pressed.
    /// </summary>
    private double _startY;

    /// <summary>
    /// Whether the press has travelled far enough to count as a drag.
    /// </summary>
    private bool _dragging;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistView"/> class.
    /// </summary>
    public WatchlistView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => ResetDrag();
    }

    /// <summary>
    /// Remembers which row was pressed so a later pointer move can turn it into a drag.
    /// </summary>
    /// <param name="sender">
    /// The rows host.
    /// </param>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    private void OnRowsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (RowsHost.ItemsPanelRoot is not Panel panel || panel.Children.Count < 2)
        {
            return;
        }

        if (!e.GetCurrentPoint(RowsHost).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var index = IndexOfRow(panel, e.Source as Visual);
        if (index < 0)
        {
            return;
        }

        _panel = panel;
        _pointer = e.Pointer;
        _pressedIndex = index;
        _targetIndex = index;
        _startY = e.GetPosition(panel).Y;
        _dragging = false;
    }

    /// <summary>
    /// Starts the drag once the pointer has moved far enough and then moves the dragged row with it.
    /// </summary>
    /// <param name="sender">
    /// The rows host.
    /// </param>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    private void OnRowsPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedIndex < 0 || _panel is null)
        {
            return;
        }

        if (!e.GetCurrentPoint(RowsHost).Properties.IsLeftButtonPressed)
        {
            ResetDrag();
            return;
        }

        var y = e.GetPosition(_panel).Y;
        if (!_dragging)
        {
            if (Math.Abs(y - _startY) < DragThreshold)
            {
                return;
            }

            // Measure the drag from where the threshold was crossed so the row does not jump under the cursor.
            _startY = y;
            BeginDrag(e.Pointer);
        }

        ScrollNearEdge(e);
        UpdateDrag(y);
        e.Handled = true;
    }

    /// <summary>
    /// Finishes the drag and persists the new order.
    /// </summary>
    /// <param name="sender">
    /// The rows host.
    /// </param>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    private async void OnRowsPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressedIndex < 0 || _panel is null)
        {
            return;
        }

        if (!_dragging)
        {
            ResetDrag();
            return;
        }

        var pressed = _pressedIndex;
        var target = _targetIndex;
        var row = _draggedRow;
        var layoutIntact = IsLayoutIntact();
        e.Handled = true;

        // Clear the transforms before the rows are reordered so the list never shows a stale offset.
        ResetDrag();

        // If the rows changed during the drag the captured indices no longer mean anything, so drop nothing.
        if (layoutIntact && target != pressed && DataContext is MainViewModel vm)
        {
            await vm.MoveToAsync(row, target);
        }
    }

    /// <summary>
    /// Abandons the drag when the pointer capture is lost.
    /// </summary>
    /// <param name="sender">
    /// The rows host.
    /// </param>
    /// <param name="e">
    /// The capture event data.
    /// </param>
    private void OnRowsPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ResetDrag();

    /// <summary>
    /// Cancels the drag when Escape is pressed.
    /// </summary>
    /// <param name="sender">
    /// The top level the handler is attached to.
    /// </param>
    /// <param name="e">
    /// The key event data.
    /// </param>
    private void OnTopLevelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _dragging)
        {
            e.Handled = true;
            ResetDrag();
        }
    }

    /// <summary>
    /// Captures the pointer and snapshots the row layout so the drop target can be computed against the original slots.
    /// </summary>
    /// <param name="pointer">
    /// The pointer that is dragging.
    /// </param>
    private void BeginDrag(IPointer pointer)
    {
        if (_panel is null)
        {
            return;
        }

        _dragging = true;
        _slots = [.. _panel.Children.Select(c => (c.Bounds.Top, c.Bounds.Height))];
        _transforms = [.. _panel.Children.Select(_ => new TranslateTransform())];
        for (var i = 0; i < _panel.Children.Count; i++)
        {
            _panel.Children[i].RenderTransform = _transforms[i];
        }

        pointer.Capture(RowsHost);

        var container = _panel.Children[_pressedIndex];
        _draggedRow = container.DataContext as AssetRowViewModel;
        container.ZIndex = 1;

        if ((container as ContentPresenter)?.Child is UserControl { Content: Control rowBorder } rowView)
        {
            rowView.Classes.Set("dragging", true);
            ToolTip.SetIsOpen(rowBorder, false);
        }

        _keyHost = TopLevel.GetTopLevel(this);
        _keyHost?.AddHandler(KeyDownEvent, OnTopLevelKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Moves the dragged row to the pointer and shifts the rows in between to make room for it.
    /// </summary>
    /// <param name="y">
    /// The pointer's vertical position within the panel.
    /// </param>
    private void UpdateDrag(double y)
    {
        if (_panel is null)
        {
            return;
        }

        if (!IsLayoutIntact())
        {
            ResetDrag();
            return;
        }

        var slot = _slots[_pressedIndex];
        var first = _slots[0];
        var last = _slots[^1];
        var offset = Math.Clamp(y - _startY, first.Top - slot.Top, last.Top + last.Height - (slot.Top + slot.Height));

        _targetIndex = RowReorderMath.ComputeTargetIndex(_slots, _pressedIndex, slot.Top + offset + (slot.Height / 2));

        for (var i = 0; i < _transforms.Count; i++)
        {
            _transforms[i].Y = ShiftFor(i, offset, slot.Height);
        }
    }

    /// <summary>
    /// Gets how far a row is displaced from its original slot while another row is being dragged.
    /// </summary>
    /// <param name="index">
    /// The original index of the row.
    /// </param>
    /// <param name="draggedOffset">
    /// The vertical offset of the dragged row.
    /// </param>
    /// <param name="draggedHeight">
    /// The height of the dragged row, which is how far displaced neighbours move.
    /// </param>
    /// <returns>
    /// The vertical displacement of the row.
    /// </returns>
    private double ShiftFor(int index, double draggedOffset, double draggedHeight)
    {
        if (index == _pressedIndex)
        {
            return draggedOffset;
        }

        // Dragging down pulls the rows it passes up; dragging up pushes the rows it passes down.
        if (_targetIndex > _pressedIndex && index > _pressedIndex && index <= _targetIndex)
        {
            return -draggedHeight;
        }

        if (_targetIndex < _pressedIndex && index >= _targetIndex && index < _pressedIndex)
        {
            return draggedHeight;
        }

        return 0;
    }

    /// <summary>
    /// Scrolls the list while the pointer is near its top or bottom edge.
    /// </summary>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    private void ScrollNearEdge(PointerEventArgs e)
    {
        var y = e.GetPosition(Scroller).Y;
        var offset = Scroller.Offset;
        if (y < EdgeScrollZone)
        {
            Scroller.Offset = new Vector(offset.X, Math.Max(0, offset.Y - EdgeScrollStep));
        }
        else if (y > Scroller.Bounds.Height - EdgeScrollZone)
        {
            Scroller.Offset = new Vector(offset.X, offset.Y + EdgeScrollStep);
        }
    }

    /// <summary>
    /// Clears all drag visuals, releases the pointer capture and forgets the current press.
    /// </summary>
    private void ResetDrag()
    {
        var panel = _panel;
        var pointer = _pointer;
        var wasDragging = _dragging;

        _panel = null;
        _pointer = null;
        _pressedIndex = -1;
        _dragging = false;
        _draggedRow = null;
        _slots = [];
        _transforms = [];

        if (!wasDragging)
        {
            return;
        }

        _keyHost?.RemoveHandler(KeyDownEvent, OnTopLevelKeyDown);
        _keyHost = null;

        if (panel is not null)
        {
            foreach (var child in panel.Children)
            {
                child.RenderTransform = null;
                child.ZIndex = 0;
                (child as ContentPresenter)?.Child?.Classes.Set("dragging", false);
            }
        }

        if (pointer?.Captured == RowsHost)
        {
            pointer.Capture(null);
        }
    }

    /// <summary>
    /// Checks that the rows still match the layout snapshot taken when the drag started.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the row count is unchanged and the dragged row is still at its original index.
    /// </returns>
    private bool IsLayoutIntact() =>
        _panel is not null
        && _draggedRow is not null
        && _slots.Count == _panel.Children.Count
        && _pressedIndex >= 0
        && _pressedIndex < _panel.Children.Count
        && ReferenceEquals(_panel.Children[_pressedIndex].DataContext, _draggedRow);

    /// <summary>
    /// Finds the index of the row container that contains a visual.
    /// </summary>
    /// <param name="panel">
    /// The panel hosting the row containers.
    /// </param>
    /// <param name="visual">
    /// The visual to locate, usually the pointer event source.
    /// </param>
    /// <returns>
    /// The row index, or -1 when the visual is not inside a row.
    /// </returns>
    private static int IndexOfRow(Panel panel, Visual? visual)
    {
        while (visual is not null)
        {
            if (ReferenceEquals(visual.GetVisualParent(), panel))
            {
                return panel.Children.IndexOf((Control)visual);
            }

            visual = visual.GetVisualParent();
        }

        return -1;
    }
}
