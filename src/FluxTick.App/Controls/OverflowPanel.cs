using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;

namespace FluxTick.App.Controls;

/// <summary>
/// The pure arithmetic behind <see cref="OverflowPanel"/>, kept free of UI types so it can be tested.
/// </summary>
internal static class OverflowLayout
{
    /// <summary>
    /// Determines how many leading items fit into the available width.
    /// </summary>
    /// <param name="widths">
    /// The desired widths of the items in order.
    /// </param>
    /// <param name="spacing">
    /// The gap between two neighbouring items.
    /// </param>
    /// <param name="available">
    /// The width the items may occupy.
    /// </param>
    /// <param name="reservedWidth">
    /// The width kept free, gap included, for the overflow indicator when not every item fits.
    /// </param>
    /// <returns>
    /// The number of leading items to show; all of them when they fit without the reserved width.
    /// </returns>
    public static int ComputeVisibleCount(IReadOnlyList<double> widths, double spacing, double available, double reservedWidth)
    {
        if (widths.Count == 0)
        {
            return 0;
        }

        if (double.IsPositiveInfinity(available) || Total(widths, widths.Count, spacing) <= available)
        {
            return widths.Count;
        }

        var budget = available - reservedWidth;
        var count = 0;
        var used = 0.0;
        foreach (var width in widths)
        {
            var next = used + (count > 0 ? spacing : 0) + width;
            if (next > budget)
            {
                break;
            }

            used = next;
            count++;
        }

        return count;
    }

    /// <summary>
    /// Measures the width taken by the leading items including the gaps between them.
    /// </summary>
    /// <param name="widths">
    /// The desired widths of the items in order.
    /// </param>
    /// <param name="count">
    /// The number of leading items to measure.
    /// </param>
    /// <param name="spacing">
    /// The gap between two neighbouring items.
    /// </param>
    /// <returns>
    /// The total width, zero for no items.
    /// </returns>
    public static double Total(IReadOnlyList<double> widths, int count, double spacing)
    {
        var total = 0.0;
        for (var i = 0; i < count; i++)
        {
            total += widths[i] + (i > 0 ? spacing : 0);
        }

        return total;
    }
}

/// <summary>
/// A horizontal panel that shows as many leading children as fit and reports how many were left out, so a status bar
/// never clips an item halfway. Children flagged with <see cref="HasProblemProperty"/> are placed first, so they are the
/// last to be left out.
/// </summary>
public sealed class OverflowPanel : Panel
{
    /// <summary>
    /// Identifies the <see cref="Spacing"/> property.
    /// </summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<OverflowPanel, double>(nameof(Spacing));

    /// <summary>
    /// Identifies the <see cref="ReservedWidth"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ReservedWidthProperty =
        AvaloniaProperty.Register<OverflowPanel, double>(nameof(ReservedWidth));

    /// <summary>
    /// Identifies the HasProblem attached property, set on an item to show it first and to report it when it is hidden.
    /// </summary>
    public static readonly AttachedProperty<bool> HasProblemProperty =
        AvaloniaProperty.RegisterAttached<OverflowPanel, Control, bool>("HasProblem");

    /// <summary>
    /// Identifies the <see cref="HiddenCount"/> property.
    /// </summary>
    public static readonly DirectProperty<OverflowPanel, int> HiddenCountProperty =
        AvaloniaProperty.RegisterDirect<OverflowPanel, int>(nameof(HiddenCount), o => o.HiddenCount);

    /// <summary>
    /// Identifies the <see cref="HiddenHasProblem"/> property.
    /// </summary>
    public static readonly DirectProperty<OverflowPanel, bool> HiddenHasProblemProperty =
        AvaloniaProperty.RegisterDirect<OverflowPanel, bool>(nameof(HiddenHasProblem), o => o.HiddenHasProblem);

    /// <summary>
    /// The children in display order from the last measure pass: the flagged ones first, then the others.
    /// </summary>
    private readonly List<Control> _ordered = [];

    /// <summary>
    /// The desired widths of <see cref="_ordered"/> from the last measure pass.
    /// </summary>
    private readonly List<double> _orderedWidths = [];

    /// <summary>
    /// The number of flagged children, which are the first ones in <see cref="_ordered"/>.
    /// </summary>
    private int _flaggedCount;

    /// <summary>
    /// The number of children that fitted in the last measure pass.
    /// </summary>
    private int _visibleCount;

    /// <summary>
    /// The backing field of <see cref="HiddenCount"/>.
    /// </summary>
    private int _hiddenCount;

    /// <summary>
    /// The backing field of <see cref="HiddenHasProblem"/>.
    /// </summary>
    private bool _hiddenHasProblem;

    /// <summary>
    /// Initializes static members of the <see cref="OverflowPanel"/> class.
    /// </summary>
    static OverflowPanel()
    {
        AffectsMeasure<OverflowPanel>(SpacingProperty, ReservedWidthProperty);
        ClipToBoundsProperty.OverrideDefaultValue<OverflowPanel>(true);

        // A flag flipping changes the order and the hidden state but not the size of the flagged item, so Avalonia would
        // not re-measure the panel on its own.
        HasProblemProperty.Changed.AddClassHandler<Control>((control, _) => control.FindAncestorOfType<OverflowPanel>()?.InvalidateMeasure());
    }

    /// <summary>
    /// Gets or sets the gap between two children.
    /// </summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    /// Gets or sets the width of an overflow indicator drawn next to the panel; a gap of <see cref="Spacing"/> is kept
    /// before it whenever children are left out.
    /// </summary>
    public double ReservedWidth
    {
        get => GetValue(ReservedWidthProperty);
        set => SetValue(ReservedWidthProperty, value);
    }

    /// <summary>
    /// Gets the number of children that did not fit.
    /// </summary>
    public int HiddenCount
    {
        get => _hiddenCount;
        private set => SetAndRaise(HiddenCountProperty, ref _hiddenCount, value);
    }

    /// <summary>
    /// Gets a value indicating whether a hidden child is flagged with the HasProblem attached property.
    /// </summary>
    public bool HiddenHasProblem
    {
        get => _hiddenHasProblem;
        private set => SetAndRaise(HiddenHasProblemProperty, ref _hiddenHasProblem, value);
    }

    /// <summary>
    /// Gets the HasProblem flag of an item.
    /// </summary>
    /// <param name="control">
    /// The item.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the item is flagged.
    /// </returns>
    public static bool GetHasProblem(Control control) => control.GetValue(HasProblemProperty);

    /// <summary>
    /// Sets the HasProblem flag of an item.
    /// </summary>
    /// <param name="control">
    /// The item.
    /// </param>
    /// <param name="value">
    /// Whether the item is flagged.
    /// </param>
    public static void SetHasProblem(Control control, bool value) => control.SetValue(HasProblemProperty, value);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        _ordered.Clear();
        _orderedWidths.Clear();

        // Flagged children go first, so a problem is only pushed out of sight once every child ahead of it has one too.
        var childAvailable = new Size(double.PositiveInfinity, availableSize.Height);
        var height = 0.0;
        _flaggedCount = AppendChildren(flagged: true, childAvailable, ref height);
        AppendChildren(flagged: false, childAvailable, ref height);

        _visibleCount = OverflowLayout.ComputeVisibleCount(_orderedWidths, Spacing, availableSize.Width, ReservedWidth + Spacing);
        HiddenCount = _ordered.Count - _visibleCount;
        HiddenHasProblem = _visibleCount < _flaggedCount;
        return new Size(OverflowLayout.Total(_orderedWidths, _visibleCount, Spacing), height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_ordered.Count != Children.Count)
        {
            // The children changed since the last measure pass; a new pass is already pending.
            return finalSize;
        }

        var x = 0.0;
        for (var position = 0; position < _ordered.Count; position++)
        {
            var child = _ordered[position];
            var width = child.DesiredSize.Width;
            if (position < _visibleCount)
            {
                AutomationProperties.SetIsOffscreenBehavior(child, IsOffscreenBehavior.Default);
                child.Arrange(new Rect(x, 0, width, finalSize.Height));
                x += width + Spacing;
            }
            else
            {
                // Parked beyond the right edge: the clip hides the child and keeps it out of hit testing, and the
                // automation tree is told it is not on screen.
                AutomationProperties.SetIsOffscreenBehavior(child, IsOffscreenBehavior.Offscreen);
                child.Arrange(new Rect(finalSize.Width + 1, 0, width, finalSize.Height));
            }
        }

        return finalSize;
    }

    /// <summary>
    /// Measures the children whose flag matches and appends them to the display order.
    /// </summary>
    /// <param name="flagged">
    /// Whether to take the flagged or the unflagged children.
    /// </param>
    /// <param name="childAvailable">
    /// The size offered to each child.
    /// </param>
    /// <param name="height">
    /// The tallest desired height so far, raised by the children measured here.
    /// </param>
    /// <returns>
    /// The number of children appended.
    /// </returns>
    private int AppendChildren(bool flagged, Size childAvailable, ref double height)
    {
        var appended = 0;
        foreach (var child in Children)
        {
            if (IsFlagged(child) != flagged)
            {
                continue;
            }

            child.Measure(childAvailable);
            _ordered.Add(child);
            _orderedWidths.Add(child.DesiredSize.Width);
            height = Math.Max(height, child.DesiredSize.Height);
            appended++;
        }

        return appended;
    }

    /// <summary>
    /// Reads the HasProblem flag of a child, looking through the presenter an items control wraps it in.
    /// </summary>
    /// <param name="child">
    /// The child of the panel.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the child or its content is flagged.
    /// </returns>
    private static bool IsFlagged(Control child) =>
        GetHasProblem(child) || (child is ContentPresenter { Child: { } content } && GetHasProblem(content));
}
