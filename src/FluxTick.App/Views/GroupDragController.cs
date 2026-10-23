using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;
using FluxTick.App.ViewModels;

namespace FluxTick.App.Views;

/// <summary>
/// Lets the sections of the card layout (user-defined groups, or asset class sections) be reordered by pressing a
/// section's header and dragging it up or down. The section follows the pointer with its cards, and the sections it
/// passes slide out of the way.
/// </summary>
internal sealed class GroupDragController
{
    /// <summary>
    /// The vertical distance the pointer must travel before a press turns into a drag.
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
    /// The z-index given to the dragged group so that it is drawn above the others.
    /// </summary>
    private const int DraggedZIndex = 100;

    /// <summary>
    /// How long the other groups take to move out of the way.
    /// </summary>
    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(160);

    /// <summary>
    /// The items control that lists the sections.
    /// </summary>
    private readonly ItemsControl _host;

    /// <summary>
    /// The scroll viewer around the sections.
    /// </summary>
    private readonly ScrollViewer _scroller;

    /// <summary>
    /// Supplies the view model the drag acts on.
    /// </summary>
    private readonly Func<MainViewModel?> _viewModel;

    /// <summary>
    /// The control that owns the host, used to find the top level.
    /// </summary>
    private readonly Control _owner;

    /// <summary>
    /// The pointer that started the current press or drag.
    /// </summary>
    private IPointer? _pointer;

    /// <summary>
    /// The top level the Escape handler was attached to, kept so the same instance is used to detach it.
    /// </summary>
    private TopLevel? _keyHost;

    /// <summary>
    /// The section whose header was pressed.
    /// </summary>
    private WatchSectionViewModel? _section;

    /// <summary>
    /// The containers of all user-defined groups in display order, captured when the drag starts.
    /// </summary>
    private List<Control> _containers = [];

    /// <summary>
    /// The original top and height of every group container, in host coordinates.
    /// </summary>
    private List<(double Top, double Height)> _slots = [];

    /// <summary>
    /// The index of the pressed group among the groups, or -1 when no press is in progress.
    /// </summary>
    private int _pressedIndex = -1;

    /// <summary>
    /// The index the dragged group would currently drop into.
    /// </summary>
    private int _targetIndex;

    /// <summary>
    /// The pointer's vertical position, in host coordinates, the drag distance is measured from.
    /// </summary>
    private double _startY;

    /// <summary>
    /// Whether the press has travelled far enough to count as a drag.
    /// </summary>
    private bool _dragging;

    /// <summary>
    /// The z-index values to restore once the drag ends.
    /// </summary>
    private int _savedZIndex;

    /// <summary>
    /// Initializes a new instance of the <see cref="GroupDragController"/> class and starts listening to the host.
    /// </summary>
    /// <param name="host">
    /// The items control that lists the sections.
    /// </param>
    /// <param name="scroller">
    /// The scroll viewer around the sections.
    /// </param>
    /// <param name="owner">
    /// The control that contains everything above.
    /// </param>
    /// <param name="viewModel">
    /// Supplies the main view model.
    /// </param>
    public GroupDragController(ItemsControl host, ScrollViewer scroller, Control owner, Func<MainViewModel?> viewModel)
    {
        _host = host;
        _scroller = scroller;
        _owner = owner;
        _viewModel = viewModel;

        // The header is a toggle button, which handles the press itself, so the press is observed even when handled.
        host.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        host.PointerMoved += OnPointerMoved;
        host.PointerReleased += OnPointerReleased;
        host.PointerCaptureLost += (_, _) => Reset();
        owner.DetachedFromVisualTree += (_, _) => Reset();
    }

    /// <summary>
    /// Remembers which group header was pressed so a later pointer move can turn it into a drag.
    /// </summary>
    /// <param name="sender">
    /// The host.
    /// </param>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel() is not { IsCardLayout: true } || !e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var header = (e.Source as Visual)?.FindAncestorOfType<ToggleButton>(includeSelf: true);
        if (header is not { Name: "SectionHeader", DataContext: WatchSectionViewModel { Kind: WatchSectionKind.Type or WatchSectionKind.Group } section })
        {
            return;
        }

        _section = section;
        _pointer = e.Pointer;
        _startY = e.GetPosition(_host).Y;
        _dragging = false;
    }

    /// <summary>
    /// Starts the drag once the pointer has moved far enough and then moves the group with it.
    /// </summary>
    /// <param name="sender">
    /// The host.
    /// </param>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_section is null)
        {
            return;
        }

        if (!e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed)
        {
            Reset();
            return;
        }

        var y = e.GetPosition(_host).Y;
        if (!_dragging)
        {
            if (Math.Abs(y - _startY) < DragThreshold)
            {
                return;
            }

            // Measure the drag from where the threshold was crossed so the group does not jump under the cursor.
            _startY = y;
            if (!BeginDrag(e.Pointer))
            {
                Reset();
                return;
            }
        }

        ScrollNearEdge(e);
        UpdateDrag(e.GetPosition(_host).Y);
        e.Handled = true;
    }

    /// <summary>
    /// Finishes the drag and persists the new order.
    /// </summary>
    /// <param name="sender">
    /// The host.
    /// </param>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    private async void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_section is null)
        {
            return;
        }

        if (!_dragging)
        {
            Reset();
            return;
        }

        var section = _section;
        var pressed = _pressedIndex;
        var target = _targetIndex;
        e.Handled = true;

        // Clear the offsets before the groups are reordered so none of them shows a stale one.
        Reset();

        if (target != pressed && _viewModel() is { } vm && vm.Sections.Contains(section))
        {
            await vm.MoveSectionAsync(section, target);
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
    /// Captures the pointer and snapshots where the groups are.
    /// </summary>
    /// <param name="pointer">
    /// The pointer that is dragging.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the drag started.
    /// </returns>
    private bool BeginDrag(IPointer pointer)
    {
        if (_section is null || _host.ItemsPanelRoot is not Panel panel)
        {
            return false;
        }

        // Groups and asset class sections are ordered among their own kind.
        _containers = [.. panel.Children.Where(c => c.DataContext is WatchSectionViewModel s && s.Kind == _section.Kind)];
        _pressedIndex = _containers.FindIndex(c => ReferenceEquals(c.DataContext, _section));
        if (_pressedIndex < 0 || _containers.Count < 2)
        {
            return false;
        }

        _targetIndex = _pressedIndex;
        _slots = [.. _containers.Select(c => (BoundsInHost(c).Top, c.Bounds.Height))];

        foreach (var container in _containers.Where((_, i) => i != _pressedIndex))
        {
            container.Transitions =
            [
                new TransformOperationsTransition
                {
                    Property = Visual.RenderTransformProperty,
                    Duration = SlideDuration,
                    Easing = new CubicEaseOut(),
                },
            ];
        }

        var dragged = _containers[_pressedIndex];
        _savedZIndex = dragged.ZIndex;
        dragged.ZIndex = DraggedZIndex;

        // The group is drawn over the others, so it gets the window's background to stay readable on top of them.
        if (dragged is ContentPresenter presenter
            && Application.Current?.TryGetResource("WindowBgBrush", presenter.ActualThemeVariant, out var brush) == true
            && brush is IBrush background)
        {
            presenter.Background = background;
            presenter.CornerRadius = new CornerRadius(8);
        }

        _dragging = true;
        pointer.Capture(_host);
        _keyHost = TopLevel.GetTopLevel(_owner);
        _keyHost?.AddHandler(InputElement.KeyDownEvent, OnTopLevelKeyDown, RoutingStrategies.Tunnel);
        return true;
    }

    /// <summary>
    /// Moves the dragged group to the pointer and slides the groups it passes out of the way.
    /// </summary>
    /// <param name="y">
    /// The pointer's vertical position in host coordinates.
    /// </param>
    private void UpdateDrag(double y)
    {
        var slot = _slots[_pressedIndex];
        var first = _slots[0];
        var last = _slots[^1];
        var offset = Math.Clamp(y - _startY, first.Top - slot.Top, last.Top + last.Height - (slot.Top + slot.Height));

        _targetIndex = RowReorderMath.ComputeTargetIndex(_slots, _pressedIndex, slot.Top + offset + (slot.Height / 2));

        for (var i = 0; i < _containers.Count; i++)
        {
            if (i == _pressedIndex)
            {
                _containers[i].RenderTransform = new TranslateTransform(0, offset);
                continue;
            }

            // Dragging down pulls the groups it passes up; dragging up pushes the groups it passes down.
            var shift = 0d;
            if (_targetIndex > _pressedIndex && i > _pressedIndex && i <= _targetIndex)
            {
                shift = -slot.Height;
            }
            else if (_targetIndex < _pressedIndex && i >= _targetIndex && i < _pressedIndex)
            {
                shift = slot.Height;
            }

            var builder = TransformOperations.CreateBuilder(1);
            builder.AppendTranslate(0, shift);
            _containers[i].RenderTransform = builder.Build();
        }
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
        var pointer = _pointer;
        var wasDragging = _dragging;
        var containers = _containers;
        var pressed = _pressedIndex;

        _section = null;
        _pointer = null;
        _dragging = false;
        _containers = [];
        _slots = [];
        _pressedIndex = -1;

        if (!wasDragging)
        {
            return;
        }

        _keyHost?.RemoveHandler(InputElement.KeyDownEvent, OnTopLevelKeyDown);
        _keyHost = null;

        // Drop the transitions first so the groups do not slide back once the offsets are cleared.
        for (var i = 0; i < containers.Count; i++)
        {
            containers[i].Transitions = null;
            containers[i].RenderTransform = null;
            if (i == pressed)
            {
                containers[i].ZIndex = _savedZIndex;
                if (containers[i] is ContentPresenter presenter)
                {
                    presenter.Background = null;
                    presenter.CornerRadius = default;
                }
            }
        }

        if (pointer?.Captured == _host)
        {
            pointer.Capture(null);
        }
    }

    /// <summary>
    /// Gets the area of a visual in host coordinates.
    /// </summary>
    /// <param name="visual">
    /// The visual to locate.
    /// </param>
    /// <returns>
    /// The area, or an empty rectangle when the visual is not laid out under the host.
    /// </returns>
    private Rect BoundsInHost(Visual visual) =>
        visual.TranslatePoint(default, _host) is { } origin ? new Rect(origin, visual.Bounds.Size) : default;
}
