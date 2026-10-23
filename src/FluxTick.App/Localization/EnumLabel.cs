using System.Globalization;
using Avalonia.Data.Converters;

namespace FluxTick.App.Localization;

/// <summary>
/// Names enum values in the active language, for lists that offer them. The text key is the enum type name and the
/// value joined by an underscore, for example <c>ThemeMode_Dark</c>.
/// </summary>
public static class EnumLabel
{
    /// <summary>
    /// Gets a converter for a binding of the value followed by <see cref="Loc.Language"/>. The second value only makes
    /// the binding evaluate again when the language changes.
    /// </summary>
    public static IMultiValueConverter Converter { get; } = new LabelConverter();

    /// <summary>
    /// Gets the text that names an enum value.
    /// </summary>
    /// <param name="value">
    /// The enum value.
    /// </param>
    /// <returns>
    /// The label in the active language, or the name of the value when no label is defined.
    /// </returns>
    public static string Of(Enum value) => Loc.Instance.Find($"{value.GetType().Name}_{value}") ?? value.ToString();

    /// <summary>
    /// Converts the bound enum value to its label.
    /// </summary>
    private sealed class LabelConverter : IMultiValueConverter
    {
        /// <inheritdoc />
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
            values.Count > 0 && values[0] is Enum value ? Of(value) : null;
    }
}
