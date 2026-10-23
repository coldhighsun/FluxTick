using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace FluxTick.App.Localization;

/// <summary>
/// A XAML markup extension that shows the text for a key and follows the language as it changes:
/// <c>Text="{loc:T Settings_Theme}"</c>.
/// </summary>
/// <param name="key">
/// The text key.
/// </param>
public sealed class TExtension(string key) : MarkupExtension
{
    /// <summary>
    /// Looks the key up again whenever the language changes.
    /// </summary>
    private static readonly IMultiValueConverter TextConverter = new KeyConverter();

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new MultiBinding
        {
            Bindings = { new Binding(nameof(Loc.Language)) { Source = Loc.Instance, Mode = BindingMode.OneWay } },
            Converter = TextConverter,
            ConverterParameter = key,
        };

    /// <summary>
    /// Converts the key to its text. The bound language only makes the binding evaluate again when it changes.
    /// </summary>
    private sealed class KeyConverter : IMultiValueConverter
    {
        /// <inheritdoc />
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
            parameter is string text ? Loc.Instance[text] : null;
    }
}
