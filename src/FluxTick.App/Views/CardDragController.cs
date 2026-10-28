using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;

namespace FluxTick.App.Views;

/// <summary>
/// A section and its cards as they were laid out when a drag started.
/// </summary>
/// <param name="Section">
/// The section view model.
/// </param>
/// <param name="Container">
/// The control that holds the whole section.
/// </param>
/// <param name="Others">
/// The cards of the section other than the dragged one, in order.
/// </param>
/// <param name="OtherRects">
/// The laid-out areas of <paramref name="Others"/>.
/// </param>
internal sealed record SectionState(WatchSectionViewModel Section, Control Container, List<AssetCardView> Others, List<Rect> OtherRects);

/// <summary>
/// Lets the cards of the card layout be reordered, and moved between custom groups, by pressing and dragging them.
/// The other cards stay where they are while a card is dragged; an insertion bar shows where it would land.
/// </summary>
internal sealed class CardDragController
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
    /// The margin around the visible part of a card, which a card's laid-out area includes.
    /// </summary>
    private const double CardMargin = 3;

    /// <summary>
    /// How long the other cards take to move out of the way.
    /// </summary>
    private static readonly TimeSpan FlowDuration = TimeSpan.FromMilliseconds(160);

    /// <summary>
    /// The z-index given to the dragged card and its ancestors so that it is drawn above the other sections.
    /// </summary>
    private const int DraggedZIndex = 100;

    /// <summary>
    /// The items control that lists the sections.
    /// </summary>
    private readonly ItemsControl _host;

    /// <summary>
    /// The scroll viewer around the sections.
    /// </summary>
    private readonly ScrollViewer _scroller;

    /// <summary>
    /// The bar or highlight that shows where the card would land.
    /// </summary>
    private readonly Border _indicator;

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
    /// The card that was pressed.
    /// </summary>
    private AssetCardView? _card;

    /// <summary>
    /// The row of the pressed card.
    /// </summary>
    private AssetRowViewModel? _row;

    /// <summary>
    /// The section the pressed card came from.
    /// </summary>
    private WatchSectionViewModel? _source;

    /// <summary>
    /// The position of the pressed card among the cards of its section.
    /// </summary>
    private int _originalIndex;

    /// <summary>
    /// The pointer position, in host coordinates, the drag distance is measured from.
    /// </summary>
    private Point _start;

    /// <summary>
    /// Whether the press has travelled far enough to count as a drag.
    /// </summary>
    private bool _dragging;

    /// <summary>
    /// The area of the dragged card when the drag started, in host coordinates.
    /// </summary>
    private Rect _origin;

    /// <summary>
    /// The cards that were laid out when the drag started, without the dragged one.
    /// </summary>
    private List<CardSlot> _slots = [];

    /// <summary>
    /// The sections that were laid out when the drag started.
    /// </summary>
    private List<SectionArea> _areas = [];

    /// <summary>
    /// Where the card would land right now.
    /// </summary>
    private DropTarget? _target;

    /// <summary>
    /// The sections as they were laid out when the drag started, with their cards other than the dragged one.
    /// </summary>
    private List<SectionState> _states = [];

    /// <summary>
    /// The same sections described for predicting how the cards flow.
    /// </summary>
    private List<FlowSection> _flow = [];

    /// <summary>
    /// The controls that were given a transition and an offset during the drag and must be put back afterwards.
    /// </summary>
    private List<Control> _animated = [];

    /// <summary>
    /// The empty groups that currently show a drop slot.
    /// </summary>
    private List<WatchSectionViewModel> _slotSections = [];

    /// <summary>
    /// The controls whose z-index was raised for the drag, with the values to restore.
    /// </summary>
    private List<(Control Control, int ZIndex)> _raised = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="CardDragController"/> class and starts listening to the host.
    /// </summary>
    /// <param name="host">
    /// The items control that lists the sections.
    /// </param>
    /// <param name="scroller">
    /// The scroll viewer around the sections.
    /// </param>
    /// <param name="indicator">
    /// The bar that shows the drop position. It must sit in a canvas laid over the host.
    /// </param>
    /// <param name="owner">
    /// The control that contains everything above.
    /// </param>
    /// <param name="viewModel">
    /// Supplies the main view model.
    /// </param>
    public CardDragController(ItemsControl host, ScrollViewer scroller, Border indicator, Control owner, Func<MainViewModel?> viewModel)
    {
        _host = host;
        _scroller = scroller;
        _indicator = indicator;
        _owner = owner;
        _viewModel = viewModel;

        host.PointerPressed += OnPointerPressed;
        host.PointerMoved += OnPointerMoved;
        host.PointerReleased += OnPointerReleased;
        host.PointerCaptureLost += (_, _) => Reset();
        owner.DetachedFromVisualTree += (_, _) => Reset();
    }

    /// <summary>
    /// Remembers which card was pressed so a later pointer move can turn it into a drag.
    /// </summary>
    /// <param name="sender">
    /// The host.
    /// </param>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel() is not { IsCardLayout: true } vm || !e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var card = (e.Source as Visual)?.FindAncestorOfType<AssetCardView>(includeSelf: true);
        if (card?.DataContext is not AssetRowViewModel row)
        {
            return;
        }

        var source = vm.Sections.FirstOrDefault(s => s.Items.Contains(row));
        if (source is null)
        {
            return;
        }

        _card = card;
        _row = row;
        _source = source;
        _originalIndex = source.Items.IndexOf(row);
        _pointer = e.Pointer;
        _start = e.GetPosition(_host);
        _dragging = false;
    }

    /// <summary>
    /// Starts the drag once the pointer has moved far enough and then moves the card with it.
    /// </summary>
    /// <param name="sender">
    /// The host.
    /// </param>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_card is null)
        {
            return;
        }

        if (!e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed)
        {
            Reset();
            return;
        }

        var position = e.GetPosition(_host);
        if (!_dragging)
        {
            if (Math.Abs(position.X - _start.X) < DragThreshold && Math.Abs(position.Y - _start.Y) < DragThreshold)
            {
                return;
            }

            // Measure the drag from where the threshold was crossed so the card does not jump under the cursor.
            _start = position;
            if (!BeginDrag(e.Pointer))
            {
                Reset();
                return;
            }
        }

        ScrollNearEdge(e);
        UpdateDrag(e.GetPosition(_host));
        e.Handled = true;
    }

    /// <summary>
    /// Finishes the drag and persists the new position.
    /// </summary>
    /// <param name="sender">
    /// The host.
    /// </param>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    private async void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_card is null)
        {
            return;
        }

        if (!_dragging)
        {
            Reset();
            return;
        }

        var target = _target;
        var row = _row;
        var source = _source;
        var originalIndex = _originalIndex;
        e.Handled = true;

        // Clear the drag visuals before the cards are rearranged so none of them shows a stale offset.
        Reset();

        if (target is not { } drop || row is null || source is null || _viewModel() is not { } vm)
        {
            return;
        }

        // If the cards changed during the drag, the layout that was measured no longer means anything.
        var targetSection = vm.Sections.FirstOrDefault(s => s.Key == drop.SectionKey);
        if (targetSection is null || !vm.Sections.Contains(source) || !source.Items.Contains(row))
        {
            return;
        }

        if (drop.SectionKey == source.Key && drop.Index == originalIndex)
        {
            return;
        }

        await vm.MoveCardAsync(row, targetSection, drop.Index);
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
    /// Captures the pointer, snapshots the layout and lifts the dragged card above the others.
    /// </summary>
    /// <param name="pointer">
    /// The pointer that is dragging.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the drag started.
    /// </returns>
    private bool BeginDrag(IPointer pointer)
    {
        if (_card is null)
        {
            return false;
        }

        // Empty groups open a card-sized slot first, so the layout that is measured already has room in them.
        // That moves everything below them down; the drag start follows the card so it stays under the pointer.
        var before = BoundsInHost(_card).TopLeft;
        OpenDropSlots();
        _host.UpdateLayout();
        _origin = BoundsInHost(_card);
        _start += _origin.TopLeft - before;

        if (!Snapshot())
        {
            CloseDropSlots();
            return false;
        }

        _dragging = true;
        pointer.Capture(_host);

        _card.Classes.Set("dragging", true);
        ToolTip.SetIsOpen(_card, false);
        _raised = [];
        for (Visual? visual = _card; visual is not null && !ReferenceEquals(visual, _host); visual = visual.GetVisualParent())
        {
            if (visual is Control control)
            {
                _raised.Add((control, control.ZIndex));
                control.ZIndex = DraggedZIndex;
            }
        }

        _keyHost = TopLevel.GetTopLevel(_owner);
        _keyHost?.AddHandler(InputElement.KeyDownEvent, OnTopLevelKeyDown, RoutingStrategies.Tunnel);
        return true;
    }

    /// <summary>
    /// Gives every empty, expanded group a slot as large as the dragged card.
    /// </summary>
    private void OpenDropSlots()
    {
        if (_card is null || _viewModel() is not { } vm)
        {
            return;
        }

        // The card's area includes its margin, which the slot draws around itself the same way.
        const double margin = 6;
        var size = _card.Bounds.Size;
        foreach (var section in vm.Sections.Where(s => s.Kind == WatchSectionKind.Group && s.Items.Count == 0 && !s.IsCollapsed))
        {
            section.DropSlotWidth = Math.Max(0, size.Width - margin);
            section.DropSlotHeight = Math.Max(0, size.Height - margin);
            section.IsDropSlotVisible = true;
            _slotSections.Add(section);
        }
    }

    /// <summary>
    /// Takes the drop slots away again.
    /// </summary>
    private void CloseDropSlots()
    {
        foreach (var section in _slotSections)
        {
            section.IsDropSlotVisible = false;
        }

        _slotSections = [];
    }

    /// <summary>
    /// Records where every section and card currently is, and predicts the layout without the dragged card, which is
    /// the layout the drop position is measured against.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when at least one section was found.
    /// </returns>
    private bool Snapshot()
    {
        _slots = [];
        _areas = [];
        _states = [];
        _flow = [];
        if (_host.ItemsPanelRoot is not Panel panel)
        {
            return false;
        }

        foreach (var container in panel.Children)
        {
            if (container.DataContext is not WatchSectionViewModel section)
            {
                continue;
            }

            var header = container.GetVisualDescendants().OfType<ToggleButton>().FirstOrDefault(b => b.Name == "SectionHeader");
            var headerBounds = header is { IsEffectivelyVisible: true } ? BoundsInHost(header) : default;
            var all = container.GetVisualDescendants().OfType<AssetCardView>().Where(c => c.IsEffectivelyVisible).ToList();
            var others = all.Where(c => !ReferenceEquals(c.DataContext, _row)).ToList();
            var rows = all.Select(c => Math.Round(BoundsInHost(c).Top)).Distinct().Count();
            var bodyTop = all.Count > 0 ? all.Min(c => BoundsInHost(c).Top) : headerBounds.Bottom + 2;

            _states.Add(new SectionState(section, container, others, [.. others.Select(BoundsInHost)]));
            _flow.Add(new FlowSection(section.Key, BoundsInHost(container), headerBounds, bodyTop, Math.Max(1, rows), section.IsCollapsed, others.Count));
        }

        // Without the dragged card the other cards close up; that layout does not depend on where the card is
        // dropped, so measuring against it keeps the drop position from jumping as the cards make room.
        var reference = CardFlowMath.Layout(_flow, _origin.Size, null);
        for (var i = 0; i < _states.Count; i++)
        {
            var state = _states[i];
            for (var k = 0; k < state.Others.Count && !state.Section.IsCollapsed; k++)
            {
                _slots.Add(new CardSlot(state.Section.Key, k, reference[i].Slot(k)));
            }

            var itemCount = state.Section.Items.Count - (state.Section.Items.Contains(_row!) ? 1 : 0);
            _areas.Add(new SectionArea(state.Section.Key, reference[i].Bounds, reference[i].Header, itemCount, state.Section.IsCollapsed));
        }

        return _areas.Count > 0;
    }

    /// <summary>
    /// Moves the dragged card to the pointer, lets the other cards flow around the gap it would leave or fill, and
    /// shows where it would land.
    /// </summary>
    /// <param name="position">
    /// The pointer position in host coordinates.
    /// </param>
    private void UpdateDrag(Point position)
    {
        if (_card is null || _source is null || _viewModel() is not { } vm)
        {
            return;
        }

        // A card keeps its type and its data source, so when grouping by either it can only move within its own
        // section.
        var locked = vm.Grouping is CardGrouping.ByType or CardGrouping.ByProvider ? _source.Key : null;
        _target = CardDropMath.ComputeTarget(_slots, _areas, position, locked);

        var gapKey = _target is { IsHeaderHighlight: false } drop ? drop.SectionKey : null;
        int? gapIndex = _target is { IsHeaderHighlight: false } gap ? gap.Index : null;
        var layout = CardFlowMath.Layout(_flow, _origin.Size, gapKey);

        for (var i = 0; i < _states.Count; i++)
        {
            var state = _states[i];
            SetOffset(state.Container, new Vector(0, layout[i].ShiftY));
            var cardGap = state.Section.Key == gapKey ? gapIndex : null;
            for (var k = 0; k < state.Others.Count; k++)
            {
                var slot = layout[i].LocalSlot(CardFlowMath.PositionOf(k, cardGap));
                SetOffset(state.Others[k], slot.TopLeft - state.OtherRects[k].TopLeft);
            }
        }

        // The dragged card sits in a section that may itself have moved; the pointer decides where it is drawn.
        var sourceShift = layout[_states.FindIndex(s => ReferenceEquals(s.Section, _source))].ShiftY;
        var delta = position - _start;
        _card.RenderTransform = new TranslateTransform(delta.X, delta.Y - sourceShift);

        ShowIndicator(_target, layout);
    }

    /// <summary>
    /// Shows a card-shaped outline where the dragged card would land, or outlines the header of a collapsed
    /// section it would be dropped onto. Hides the indicator when there is no target.
    /// </summary>
    /// <param name="target">
    /// The drop target, or <see langword="null"/> to hide the indicator.
    /// </param>
    /// <param name="layout">
    /// The predicted layout with a gap for the dragged card.
    /// </param>
    private void ShowIndicator(DropTarget? target, IReadOnlyList<SectionFlow> layout)
    {
        if (target is not { } drop)
        {
            _indicator.IsVisible = false;
            return;
        }

        var section = layout.First(l => l.Key == drop.SectionKey);

        // A card's area includes its margin, so the outline is drawn inside it.
        var area = drop.IsHeaderHighlight ? section.Header : section.Slot(drop.Index).Deflate(new Thickness(CardMargin));
        Canvas.SetLeft(_indicator, area.Left);
        Canvas.SetTop(_indicator, area.Top);
        _indicator.Width = area.Width;
        _indicator.Height = area.Height;
        _indicator.IsVisible = true;
    }

    /// <summary>
    /// Moves a control away from where it was laid out, animating the change.
    /// </summary>
    /// <param name="control">
    /// The control to move.
    /// </param>
    /// <param name="offset">
    /// How far to move it from its laid-out position.
    /// </param>
    private void SetOffset(Control control, Vector offset)
    {
        if (control.Transitions is null)
        {
            control.Transitions =
            [
                new TransformOperationsTransition
                {
                    Property = Visual.RenderTransformProperty,
                    Duration = FlowDuration,
                    Easing = new CubicEaseOut(),
                },
            ];
            _animated.Add(control);
        }

        var builder = TransformOperations.CreateBuilder(1);
        builder.AppendTranslate(offset.X, offset.Y);
        control.RenderTransform = builder.Build();
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
        var card = _card;
        var pointer = _pointer;
        var wasDragging = _dragging;
        var raised = _raised;

        _card = null;
        _row = null;
        _source = null;
        _pointer = null;
        _dragging = false;
        _target = null;
        var animated = _animated;
        _slots = [];
        _areas = [];
        _states = [];
        _flow = [];
        _animated = [];
        _raised = [];

        if (!wasDragging)
        {
            return;
        }

        // Drop the transitions first so the cards do not slide back once the offsets are cleared.
        foreach (var control in animated)
        {
            control.Transitions = null;
            control.RenderTransform = null;
        }

        _keyHost?.RemoveHandler(InputElement.KeyDownEvent, OnTopLevelKeyDown);
        _keyHost = null;

        CloseDropSlots();
        _indicator.IsVisible = false;
        if (card is not null)
        {
            card.RenderTransform = null;
            card.Classes.Set("dragging", false);
        }

        foreach (var (control, zIndex) in raised)
        {
            control.ZIndex = zIndex;
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
