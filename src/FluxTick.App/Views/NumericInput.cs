using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluxTick.App.Services;

namespace FluxTick.App.Views;

/// <summary>
/// Limits a text box to the typing of a real number: set <c>views:NumericInput.Enabled="True"</c> on it.
/// </summary>
public static class NumericInput
{
    /// <summary>
    /// Identifies the attached property that limits a text box to real numbers.
    /// </summary>
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("Enabled", typeof(NumericInput));

    /// <summary>
    /// Initializes static members of the <see cref="NumericInput"/> class.
    /// </summary>
    static NumericInput() => EnabledProperty.Changed.AddClassHandler<TextBox>(OnEnabledChanged);

    /// <summary>
    /// Gets whether a text box is limited to real numbers.
    /// </summary>
    /// <param name="box">
    /// The text box.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the box is limited.
    /// </returns>
    public static bool GetEnabled(TextBox box) => box.GetValue(EnabledProperty);

    /// <summary>
    /// Sets whether a text box is limited to real numbers.
    /// </summary>
    /// <param name="box">
    /// The text box.
    /// </param>
    /// <param name="value">
    /// Whether the box is limited.
    /// </param>
    public static void SetEnabled(TextBox box, bool value) => box.SetValue(EnabledProperty, value);

    /// <summary>
    /// Starts or stops watching the typing of a text box.
    /// </summary>
    /// <param name="box">
    /// The text box.
    /// </param>
    /// <param name="e">
    /// The change.
    /// </param>
    private static void OnEnabledChanged(TextBox box, AvaloniaPropertyChangedEventArgs e)
    {
        box.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
        box.RemoveHandler(TextBox.PastingFromClipboardEvent, OnPasting);
        if (e.NewValue is true)
        {
            // The tunnel is used so that the text is refused before the box has put it in.
            box.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
            box.AddHandler(TextBox.PastingFromClipboardEvent, OnPasting, RoutingStrategies.Tunnel);
        }
    }

    /// <summary>
    /// Refuses typed text that would make the box show something that is not a number.
    /// </summary>
    /// <param name="sender">
    /// The text box.
    /// </param>
    /// <param name="e">
    /// The typing.
    /// </param>
    private static void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (sender is TextBox box && !string.IsNullOrEmpty(e.Text) && !NumberInput.IsPartialNumber(After(box, e.Text)))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Refuses a paste that would make the box show something that is not a number. The clipboard is read here, which
    /// the box does again for the paste itself.
    /// </summary>
    /// <param name="sender">
    /// The text box.
    /// </param>
    /// <param name="e">
    /// The paste.
    /// </param>
    private static async void OnPasting(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || TopLevel.GetTopLevel(box)?.Clipboard is not { } clipboard)
        {
            return;
        }

        // The box starts the paste once this handler returns, so it is stopped and the text is put in by hand when it fits.
        e.Handled = true;
        var before = box.Text;
        var start = Math.Min(box.SelectionStart, box.SelectionEnd);
        try
        {
            var text = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard);

            // The box may have changed while the clipboard was read, and the paste would then land in the wrong place.
            if (!string.IsNullOrEmpty(text) && box.Text == before && NumberInput.IsPartialNumber(After(box, text)))
            {
                box.Text = After(box, text);
                box.CaretIndex = start + text.Length;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The clipboard may be locked by another program. This is an async void handler, where an exception would end
            // the application, and a paste that does not happen is the harmless outcome.
        }
    }

    /// <summary>
    /// Gets the text a box shows after some text replaces its selection.
    /// </summary>
    /// <param name="box">
    /// The text box.
    /// </param>
    /// <param name="added">
    /// The text that is put in.
    /// </param>
    /// <returns>
    /// The text of the box afterwards.
    /// </returns>
    private static string After(TextBox box, string added)
    {
        var text = box.Text ?? string.Empty;
        var start = Math.Clamp(Math.Min(box.SelectionStart, box.SelectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(box.SelectionStart, box.SelectionEnd), 0, text.Length);
        return string.Concat(text.AsSpan(0, start), added, text.AsSpan(end));
    }
}
