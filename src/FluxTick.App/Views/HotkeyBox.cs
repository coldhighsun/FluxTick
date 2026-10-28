using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace FluxTick.App.Views;

/// <summary>
/// A box that records a hotkey. While it has the focus, the modifiers that are held show at once, for example
/// <c>Ctrl+Alt+</c>; the key that completes the combination sets <see cref="Value"/>. Letting go of the keys without
/// completing one drops what was shown, and the box shows <see cref="Value"/> again. Backspace or Delete alone clear the
/// value. <see cref="IsCapturing"/> tells the app to let go of its own hotkey while keys are recorded, as Windows would
/// otherwise take the combination before the box can see it.
/// </summary>
public sealed class HotkeyBox : Border
{
    /// <summary>
    /// Identifies <see cref="Value"/>.
    /// </summary>
    public static readonly StyledProperty<string> ValueProperty =
        AvaloniaProperty.Register<HotkeyBox, string>(nameof(Value), string.Empty, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Identifies <see cref="EmptyText"/>.
    /// </summary>
    public static readonly StyledProperty<string> EmptyTextProperty =
        AvaloniaProperty.Register<HotkeyBox, string>(nameof(EmptyText), string.Empty);

    /// <summary>
    /// Identifies <see cref="RecordingText"/>.
    /// </summary>
    public static readonly StyledProperty<string> RecordingTextProperty =
        AvaloniaProperty.Register<HotkeyBox, string>(nameof(RecordingText), string.Empty);

    /// <summary>
    /// Identifies <see cref="IsCapturing"/>.
    /// </summary>
    public static readonly StyledProperty<bool> IsCapturingProperty =
        AvaloniaProperty.Register<HotkeyBox, bool>(nameof(IsCapturing), false, defaultBindingMode: BindingMode.OneWayToSource);

    /// <summary>
    /// The text of the box.
    /// </summary>
    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center };

    /// <summary>
    /// What is shown while keys are held, or <see langword="null"/> when the box shows <see cref="Value"/>.
    /// </summary>
    private string? _preview;

    /// <summary>
    /// Whether a combination was just completed and its modifiers are still held; they are not the start of a new one.
    /// </summary>
    private bool _completed;

    /// <summary>
    /// Whether the box has the keyboard focus.
    /// </summary>
    private bool _hasFocus;

    /// <summary>
    /// The window the box is in, or <see langword="null"/> while it is not in one.
    /// </summary>
    private WindowBase? _window;

    /// <summary>
    /// Initializes static members of the <see cref="HotkeyBox"/> class.
    /// </summary>
    static HotkeyBox()
    {
        ValueProperty.Changed.AddClassHandler<HotkeyBox>((box, _) => box.ShowText());
        EmptyTextProperty.Changed.AddClassHandler<HotkeyBox>((box, _) => box.ShowText());
        RecordingTextProperty.Changed.AddClassHandler<HotkeyBox>((box, _) => box.ShowText());
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HotkeyBox"/> class.
    /// </summary>
    public HotkeyBox()
    {
        Focusable = true;
        Padding = new Thickness(10, 6);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        MinHeight = 32;
        Child = _text;
        GotFocus += (_, _) => FocusChanged(hasFocus: true);
        LostFocus += (_, _) => FocusChanged(hasFocus: false);
        ApplyLook();
        ShowText();
    }

    /// <summary>
    /// Gets or sets the hotkey, written like <c>Ctrl+Alt+H</c>, or an empty text for none.
    /// </summary>
    public string Value
    {
        get => GetValue(ValueProperty) ?? string.Empty;
        set => SetValue(ValueProperty, value ?? string.Empty);
    }

    /// <summary>
    /// Gets or sets the text shown while there is no hotkey and no keys are held, for example "None".
    /// </summary>
    public string EmptyText
    {
        get => GetValue(EmptyTextProperty) ?? string.Empty;
        set => SetValue(EmptyTextProperty, value ?? string.Empty);
    }

    /// <summary>
    /// Gets or sets the text shown while the box records keys and there is no hotkey and no keys are held, for example
    /// "Press keys". When empty, <see cref="EmptyText"/> is shown instead.
    /// </summary>
    public string RecordingText
    {
        get => GetValue(RecordingTextProperty) ?? string.Empty;
        set => SetValue(RecordingTextProperty, value ?? string.Empty);
    }

    /// <summary>
    /// Gets a value indicating whether the box has the focus and records keys.
    /// </summary>
    public bool IsCapturing
    {
        get => GetValue(IsCapturingProperty);
        private set => SetValue(IsCapturingProperty, value);
    }

    /// <summary>
    /// Gets the text the box shows now.
    /// </summary>
    internal string DisplayText => _text.Text ?? string.Empty;

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as WindowBase;
        if (_window is not null)
        {
            _window.Activated += OnWindowActivated;
            _window.Deactivated += OnWindowDeactivated;
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_window is not null)
        {
            _window.Activated -= OnWindowActivated;
            _window.Deactivated -= OnWindowDeactivated;
            _window = null;
        }

        // A window that closes with the focus in the box may not report that the focus left.
        StopCapturing();
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (HotkeyInput.Read(e.Key, e.KeyModifiers, out var gesture))
        {
            case HotkeyPress.Set:
                _preview = null;
                _completed = true;
                Value = gesture.ToString();
                ShowText();
                e.Handled = true;
                break;
            case HotkeyPress.Clear:
                _preview = null;
                _completed = false;
                Value = string.Empty;
                ShowText();
                e.Handled = true;
                break;
            case HotkeyPress.Wait:
                _completed = false;
                _preview = HotkeyInput.Describe(e.KeyModifiers);
                ShowText();
                e.Handled = true;
                break;
            default:
                base.OnKeyDown(e);
                break;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        // What is held after the release: if no combination was completed, what was shown goes away with the keys. Once
        // one was completed, the modifiers that are still held are not the start of another.
        var held = HotkeyInput.AfterRelease(e.Key, e.KeyModifiers);
        _completed &= held != KeyModifiers.None;
        _preview = _completed ? null : HotkeyInput.Describe(held);
        ShowText();
        e.Handled = true;
    }

    /// <summary>
    /// Ends the recording when the window is left for another program, which keeps the focus in the box but sends it the
    /// keys no more; the app takes its hotkey back meanwhile.
    /// </summary>
    /// <param name="sender">
    /// The window.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnWindowDeactivated(object? sender, EventArgs e) => WindowActivationChanged(active: false);

    /// <summary>
    /// Records again when the window comes back with the focus still in the box.
    /// </summary>
    /// <param name="sender">
    /// The window.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnWindowActivated(object? sender, EventArgs e) => WindowActivationChanged(active: true);

    /// <summary>
    /// Starts or ends the recording when the box gains or loses the keyboard focus.
    /// </summary>
    /// <param name="hasFocus">
    /// Whether the box has the focus now.
    /// </param>
    internal void FocusChanged(bool hasFocus)
    {
        _hasFocus = hasFocus;
        if (hasFocus)
        {
            IsCapturing = true;
            ApplyLook();
            ShowText();
        }
        else
        {
            StopCapturing();
        }
    }

    /// <summary>
    /// Ends the recording when the window is left, and starts it again when the window comes back with the focus still in
    /// the box.
    /// </summary>
    /// <param name="active">
    /// Whether the window is the active one now.
    /// </param>
    internal void WindowActivationChanged(bool active)
    {
        if (!active)
        {
            StopCapturing();
        }
        else if (_hasFocus)
        {
            IsCapturing = true;
            ApplyLook();
            ShowText();
        }
    }

    /// <summary>
    /// Ends the recording and shows the hotkey again.
    /// </summary>
    private void StopCapturing()
    {
        _preview = null;
        _completed = false;
        IsCapturing = false;
        ApplyLook();
        ShowText();
    }

    /// <summary>
    /// Shows the keys that are held, or else the hotkey, or else a hint in the muted color of a text box placeholder.
    /// </summary>
    private void ShowText()
    {
        var hint = _hasFocus && RecordingText.Length > 0 ? RecordingText : EmptyText;
        var isHint = _preview is null && Value.Length == 0;
        _text.Text = _preview ?? (isHint ? hint : Value);
        _text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(isHint ? "TextControlPlaceholderForeground" : "TextControlForeground"));
    }

    /// <summary>
    /// Takes the look of a text box from the theme, with the border of a box that has the focus.
    /// </summary>
    private void ApplyLook()
    {
        this.Bind(BackgroundProperty, this.GetResourceObservable(_hasFocus ? "TextControlBackgroundFocused" : "TextControlBackground"));
        this.Bind(BorderBrushProperty, this.GetResourceObservable(_hasFocus ? "TextControlBorderBrushFocused" : "TextControlBorderBrush"));
    }
}
