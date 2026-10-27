using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace FluxTick.App.Views;

/// <summary>
/// Lets the items of a vertical list be reordered by pressing and dragging them. The other items slide out of the
/// way while one is dragged; the new position is reported when it is dropped. Text boxes, buttons, switches and drop downs inside an item
/// keep working as usual, so only the rest of the item starts a drag.
/// </summary>
internal sealed class ListDragController
{
    /// <summary>
    /// The distance the pointer must travel before a press turns into a drag.
    /// </summary>
    private const double DragThreshold = 4;

    /// <summary>
    /// The height of the zone at the top and bottom of the scroll viewer that scrolls it while dragging.
    /// </summary>
    private const double EdgeScrollZone = 28;

    /// <summary>
    /// The number of device-independent pixels scrolled per pointer move inside the edge zone.
    /// </summary>
    private const double EdgeScrollStep = 14;

    /// <summary>
    /// The items control that lists the items.
    /// </summary>
    private readonly ItemsControl _host;

    /// <summary>
    /// The scroll viewer around the list.
    /// </summary>
    private readonly ScrollViewer _scroller;

    /// <summary>
    /// Called with the original and the new index when an item is dropped on another position.
    /// </summary>
    private readonly Func<int, int, Task> _move;

    /// <summary>
    /// Called with an item container and <see langword="true"/> when it is lifted for a drag, and with
    /// <see langword="false"/> for every container when the drag is over. When it is not given, the dragged item is
    /// drawn slightly transparent.
    /// </summary>
    private readonly Action<Control, bool>? _lift;

    /// <summary>
    /// The panel that hosts the item containers while a press or drag is in progress.
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
    /// The index of the pressed item, or -1 when nothing is pressed.
    /// </summary>
    private int _pressedIndex = -1;

    /// <summary>
    /// The index the dragged item would take if dropped now.
    /// </summary>
    private int _targetIndex;

    /// <summary>
    /// The pointer's vertical position within the panel the drag distance is measured from.
    /// </summary>
    private double _startY;

    /// <summary>
    /// Whether the press has travelled far enough to count as a drag.
    /// </summary>
    private bool _dragging;

    /// <summary>
    /// The data of the dragged item, used to notice that the list changed during the drag.
    /// </summary>
    private object? _draggedItem;

    /// <summary>
    /// The top and height of every item when the drag started.
    /// </summary>
    private List<(double Top, double Height)> _slots = [];

    /// <summary>
    /// The transforms that displace the items while one is dragged.
    /// </summary>
    private List<TranslateTransform> _transforms = [];

    /// <summary>
    /// The gap between two neighbouring items, which adds to the distance displaced items move.
    /// </summary>
    private double _spacing;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListDragController"/> class and starts listening to the host.
    /// </summary>
    /// <param name="host">
    /// The items control that lists the items.
    /// </param>
    /// <param name="scroller">
    /// The scroll viewer around the list.
    /// </param>
    /// <param name="move">
    /// Called with the original and the new index when an item is dropped on another position.
    /// </param>
    /// <param name="lift">
    /// Optional hook that styles the dragged item; see the field of the same name.
    /// </param>
    public ListDragController(ItemsControl host, ScrollViewer scroller, Func<int, int, Task> move, Action<Control, bool>? lift = null)
    {
        _host = host;
        _scroller = scroller;
        _move = move;
        _lift = lift;

        host.PointerPressed += OnPointerPressed;
        host.PointerMoved += OnPointerMoved;
        host.PointerReleased += OnPointerReleased;
        host.PointerCaptureLost += (_, _) => Reset();
        host.DetachedFromVisualTree += (_, _) => Reset();
    }

    /// <summary>
    /// Remembers which item was pressed so a later pointer move can turn it into a drag.
    /// </summary>
    /// <param name="sender">
    /// The host.
    /// </param>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed || _host.ItemsPanelRoot is not Panel { Children.Count: > 1 } panel)
        {
            return;
        }

        var index = IndexOfItem(panel, e.Source as Visual);
        if (index < 0 || IsInteractive(panel, e.Source as Visual))
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
    /// Starts the drag once the pointer has moved far enough and then moves the dragged item with it.
    /// </summary>
    /// <param name="sender">
    /// The host.
    /// </param>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedIndex < 0 || _panel is null)
        {
            return;
        }

        if (!e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed)
        {
            Reset();
            return;
        }

        var y = e.GetPosition(_panel).Y;
        if (!_dragging)
        {
            if (Math.Abs(y - _startY) < DragThreshold)
            {
                return;
            }

            // Measure the drag from where the threshold was crossed so the item does not jump under the cursor.
            _startY = y;
            if (!BeginDrag(e.Pointer))
            {
                Reset();
                return;
            }
        }

        ScrollNearEdge(e);
        UpdateDrag(y);
        e.Handled = true;
    }

    /// <summary>
    /// Finishes the drag and reports the new position.
    /// </summary>
    /// <param name="sender">
    /// The host.
    /// </param>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    private async void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressedIndex < 0 || _panel is null)
        {
            return;
        }

        if (!_dragging)
        {
            Reset();
            return;
        }

        var pressed = _pressedIndex;
        var target = _targetIndex;
        var intact = IsLayoutIntact();
        e.Handled = true;

        // Clear the transforms before the list is reordered so it never shows a stale offset.
        Reset();

        // If the list changed during the drag the captured indices no longer mean anything, so drop nothing.
        if (intact && target != pressed)
        {
            await _move(pressed, target);
        }
    }

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
            Reset();
        }
    }

    /// <summary>
    /// Captures the pointer and snapshots the layout so the drop position is computed against the original slots.
    /// </summary>
    /// <param name="pointer">
    /// The pointer that is dragging.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the drag started; <see langword="false"/> when the list no longer has two items to
    /// reorder, for example because one was removed after the press.
    /// </returns>
    private bool BeginDrag(IPointer pointer)
    {
        if (_panel is null || _panel.Children.Count < 2 || _pressedIndex >= _panel.Children.Count)
        {
            return false;
        }

        _dragging = true;
        _slots = [.. _panel.Children.Select(c => (c.Bounds.Top, c.Bounds.Height))];
        _spacing = Math.Max(0, _slots[1].Top - (_slots[0].Top + _slots[0].Height));
        _transforms = [.. _panel.Children.Select(_ => new TranslateTransform())];
        for (var i = 0; i < _panel.Children.Count; i++)
        {
            _panel.Children[i].RenderTransform = _transforms[i];
        }

        pointer.Capture(_host);

        var container = _panel.Children[_pressedIndex];
        _draggedItem = container.DataContext;
        container.ZIndex = 1;
        if (_lift is null)
        {
            container.Opacity = 0.9;
        }
        else
        {
            _lift(container, true);
        }

        _keyHost = TopLevel.GetTopLevel(_host);
        _keyHost?.AddHandler(InputElement.KeyDownEvent, OnTopLevelKeyDown, RoutingStrategies.Tunnel);
        return true;
    }

    /// <summary>
    /// Moves the dragged item to the pointer and shifts the items in between to make room for it.
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
            Reset();
            return;
        }

        var slot = _slots[_pressedIndex];
        var first = _slots[0];
        var last = _slots[^1];
        var offset = Math.Clamp(y - _startY, first.Top - slot.Top, last.Top + last.Height - (slot.Top + slot.Height));

        _targetIndex = RowReorderMath.ComputeTargetIndex(_slots, _pressedIndex, slot.Top + offset + (slot.Height / 2));

        var step = slot.Height + _spacing;
        for (var i = 0; i < _transforms.Count; i++)
        {
            _transforms[i].Y = ShiftFor(i, offset, step);
        }
    }

    /// <summary>
    /// Gets how far an item is displaced from its original slot while another item is being dragged.
    /// </summary>
    /// <param name="index">
    /// The original index of the item.
    /// </param>
    /// <param name="draggedOffset">
    /// The vertical offset of the dragged item.
    /// </param>
    /// <param name="step">
    /// The height of the dragged item plus the gap between items, which is how far displaced neighbours move.
    /// </param>
    /// <returns>
    /// The vertical displacement of the item.
    /// </returns>
    private double ShiftFor(int index, double draggedOffset, double step)
    {
        if (index == _pressedIndex)
        {
            return draggedOffset;
        }

        // Dragging down pulls the items it passes up; dragging up pushes the items it passes down.
        if (_targetIndex > _pressedIndex && index > _pressedIndex && index <= _targetIndex)
        {
            return -step;
        }

        if (_targetIndex < _pressedIndex && index >= _targetIndex && index < _pressedIndex)
        {
            return step;
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
        var y = e.GetPosition(_scroller).Y;
        var offset = _scroller.Offset;
        if (y < EdgeScrollZone)
        {
            _scroller.Offset = new Vector(offset.X, Math.Max(0, offset.Y - EdgeScrollStep));
        }
        else if (y > _scroller.Bounds.Height - EdgeScrollZone)
        {
            _scroller.Offset = new Vector(offset.X, offset.Y + EdgeScrollStep);
        }
    }

    /// <summary>
    /// Clears all drag visuals, releases the pointer capture and forgets the current press.
    /// </summary>
    private void Reset()
    {
        var panel = _panel;
        var pointer = _pointer;
        var wasDragging = _dragging;

        _panel = null;
        _pointer = null;
        _pressedIndex = -1;
        _dragging = false;
        _draggedItem = null;
        _slots = [];
        _transforms = [];

        if (!wasDragging)
        {
            return;
        }

        _keyHost?.RemoveHandler(InputElement.KeyDownEvent, OnTopLevelKeyDown);
        _keyHost = null;

        if (panel is not null)
        {
            foreach (var child in panel.Children)
            {
                child.RenderTransform = null;
                child.ZIndex = 0;
                if (_lift is null)
                {
                    child.Opacity = 1;
                }
                else
                {
                    _lift(child, false);
                }
            }
        }

        if (pointer?.Captured == _host)
        {
            pointer.Capture(null);
        }
    }

    /// <summary>
    /// Checks that the items still match the layout snapshot taken when the drag started.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the item count is unchanged and the dragged item is still at its original index.
    /// </returns>
    private bool IsLayoutIntact() =>
        _panel is not null
        && _draggedItem is not null
        && _slots.Count == _panel.Children.Count
        && _pressedIndex >= 0
        && _pressedIndex < _panel.Children.Count
        && ReferenceEquals(_panel.Children[_pressedIndex].DataContext, _draggedItem);

    /// <summary>
    /// Tells whether a press landed on a control that has its own use for the pointer, such as a text box.
    /// </summary>
    /// <param name="panel">
    /// The panel hosting the item containers, where the search stops.
    /// </param>
    /// <param name="visual">
    /// The visual that was pressed.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the press must not start a drag.
    /// </returns>
    private static bool IsInteractive(Panel panel, Visual? visual)
    {
        for (; visual is not null && !ReferenceEquals(visual, panel); visual = visual.GetVisualParent())
        {
            if (visual is TextBox or Button or ToggleSwitch or ComboBox)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the index of the item container that contains a visual.
    /// </summary>
    /// <param name="panel">
    /// The panel hosting the item containers.
    /// </param>
    /// <param name="visual">
    /// The visual to locate, usually the pointer event source.
    /// </param>
    /// <returns>
    /// The item index, or -1 when the visual is not inside an item.
    /// </returns>
    private static int IndexOfItem(Panel panel, Visual? visual)
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
