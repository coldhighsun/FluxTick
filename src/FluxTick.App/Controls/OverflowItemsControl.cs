using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace FluxTick.App.Controls;

/// <summary>
/// An items control that lays its items out in an <see cref="OverflowPanel"/> and reports how many did not fit, so a
/// view can bind an overflow indicator to it by element name.
/// </summary>
public sealed class OverflowItemsControl : ItemsControl
{
    /// <summary>
    /// Identifies the <see cref="Spacing"/> property.
    /// </summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<OverflowItemsControl, double>(nameof(Spacing));

    /// <summary>
    /// Identifies the <see cref="ReservedWidth"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ReservedWidthProperty =
        AvaloniaProperty.Register<OverflowItemsControl, double>(nameof(ReservedWidth));

    /// <summary>
    /// Identifies the <see cref="HiddenCount"/> property.
    /// </summary>
    public static readonly DirectProperty<OverflowItemsControl, int> HiddenCountProperty =
        AvaloniaProperty.RegisterDirect<OverflowItemsControl, int>(nameof(HiddenCount), o => o.HiddenCount);

    /// <summary>
    /// Identifies the <see cref="HasHiddenItems"/> property.
    /// </summary>
    public static readonly DirectProperty<OverflowItemsControl, bool> HasHiddenItemsProperty =
        AvaloniaProperty.RegisterDirect<OverflowItemsControl, bool>(nameof(HasHiddenItems), o => o.HasHiddenItems);

    /// <summary>
    /// Identifies the <see cref="HiddenHasProblem"/> property.
    /// </summary>
    public static readonly DirectProperty<OverflowItemsControl, bool> HiddenHasProblemProperty =
        AvaloniaProperty.RegisterDirect<OverflowItemsControl, bool>(nameof(HiddenHasProblem), o => o.HiddenHasProblem);

    /// <summary>
    /// The panel currently in use, if one has been created.
    /// </summary>
    private OverflowPanel? _panel;

    /// <summary>
    /// The backing field of <see cref="HiddenCount"/>.
    /// </summary>
    private int _hiddenCount;

    /// <summary>
    /// The backing field of <see cref="HasHiddenItems"/>.
    /// </summary>
    private bool _hasHiddenItems;

    /// <summary>
    /// The backing field of <see cref="HiddenHasProblem"/>.
    /// </summary>
    private bool _hiddenHasProblem;

    /// <summary>
    /// Initializes static members of the <see cref="OverflowItemsControl"/> class.
    /// </summary>
    static OverflowItemsControl()
    {
        SpacingProperty.Changed.AddClassHandler<OverflowItemsControl>(
            (control, e) => control._panel?.Spacing = e.GetNewValue<double>());
        ReservedWidthProperty.Changed.AddClassHandler<OverflowItemsControl>(
            (control, e) => control._panel?.ReservedWidth = e.GetNewValue<double>());
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OverflowItemsControl"/> class.
    /// </summary>
    public OverflowItemsControl()
    {
        ItemsPanel = new FuncTemplate<Panel?>(CreatePanel);
    }

    /// <summary>
    /// Gets or sets the gap between two items.
    /// </summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    /// Gets or sets the width of the overflow indicator, so the panel can keep room for it. The indicator is expected to
    /// be exactly this wide, which keeps the layout free of any feedback from it.
    /// </summary>
    public double ReservedWidth
    {
        get => GetValue(ReservedWidthProperty);
        set => SetValue(ReservedWidthProperty, value);
    }

    /// <summary>
    /// Gets the number of items that did not fit.
    /// </summary>
    public int HiddenCount
    {
        get => _hiddenCount;
        private set
        {
            if (SetAndRaise(HiddenCountProperty, ref _hiddenCount, value))
            {
                SetAndRaise(HasHiddenItemsProperty, ref _hasHiddenItems, value > 0);
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether some items did not fit.
    /// </summary>
    public bool HasHiddenItems => _hasHiddenItems;

    /// <summary>
    /// Gets a value indicating whether an item that did not fit is flagged with <see cref="OverflowPanel.HasProblemProperty"/>.
    /// </summary>
    public bool HiddenHasProblem
    {
        get => _hiddenHasProblem;
        private set => SetAndRaise(HiddenHasProblemProperty, ref _hiddenHasProblem, value);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(ItemsControl);

    /// <summary>
    /// Creates the panel with the current settings and mirrors its overflow state, letting go of the previous panel first.
    /// </summary>
    /// <returns>
    /// The new panel.
    /// </returns>
    private OverflowPanel CreatePanel()
    {
        DetachPanel();
        var panel = new OverflowPanel { Spacing = Spacing, ReservedWidth = ReservedWidth };
        panel.PropertyChanged += OnPanelPropertyChanged;
        _panel = panel;
        return panel;
    }

    /// <summary>
    /// Stops following the current panel and clears the overflow state it reported.
    /// </summary>
    private void DetachPanel()
    {
        if (_panel is null)
        {
            return;
        }

        _panel.PropertyChanged -= OnPanelPropertyChanged;
        _panel = null;
        HiddenCount = 0;
        HiddenHasProblem = false;
    }

    /// <summary>
    /// Copies the overflow state of the panel.
    /// </summary>
    /// <param name="sender">
    /// The panel.
    /// </param>
    /// <param name="e">
    /// The property change data.
    /// </param>
    private void OnPanelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == OverflowPanel.HiddenCountProperty)
        {
            HiddenCount = e.GetNewValue<int>();
        }
        else if (e.Property == OverflowPanel.HiddenHasProblemProperty)
        {
            HiddenHasProblem = e.GetNewValue<bool>();
        }
    }
}
