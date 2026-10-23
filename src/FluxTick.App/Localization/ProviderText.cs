using System.Globalization;
using Avalonia.Data.Converters;
using FluxTick.Abstractions;

namespace FluxTick.App.Localization;

/// <summary>
/// Shows the texts of data sources in the active language. The providers describe themselves in English and know
/// nothing of the interface, so the translations live here, keyed by provider id and option key. Anything without a
/// translation is shown as the provider wrote it.
/// </summary>
public static class ProviderText
{
    /// <summary>
    /// Gets a converter for a binding of a <see cref="ProviderDescriptor"/> followed by <see cref="Loc.Language"/>
    /// that yields the name of the data source.
    /// </summary>
    public static IMultiValueConverter NameConverter { get; } = new NameLabelConverter();

    /// <summary>
    /// Gets the display name of a data source.
    /// </summary>
    /// <param name="descriptor">
    /// The data source.
    /// </param>
    /// <returns>
    /// The translated name, or the name the provider gave.
    /// </returns>
    public static string Name(ProviderDescriptor descriptor) =>
        Loc.Instance.Find($"Provider_{descriptor.Id}_Name") ?? descriptor.DisplayName;

    /// <summary>
    /// Gets the description of a data source.
    /// </summary>
    /// <param name="descriptor">
    /// The data source.
    /// </param>
    /// <returns>
    /// The translated description, or the one the provider gave.
    /// </returns>
    public static string Description(ProviderDescriptor descriptor) =>
        Loc.Instance.Find($"Provider_{descriptor.Id}_Desc") ?? descriptor.Description;

    /// <summary>
    /// Gets the label of an option.
    /// </summary>
    /// <param name="definition">
    /// The option.
    /// </param>
    /// <returns>
    /// The translated label, or the one the provider gave.
    /// </returns>
    public static string OptionLabel(ProviderOptionDefinition definition) =>
        Loc.Instance.Find($"Option_{definition.Key}_Label") ?? definition.Label;

    /// <summary>
    /// Gets the hint of an option.
    /// </summary>
    /// <param name="descriptor">
    /// The data source that owns the option.
    /// </param>
    /// <param name="definition">
    /// The option.
    /// </param>
    /// <returns>
    /// The translated hint, or the one the provider gave.
    /// </returns>
    public static string? OptionDescription(ProviderDescriptor descriptor, ProviderOptionDefinition definition) =>
        Loc.Instance.Find($"Option_{descriptor.Id}_{definition.Key}_Desc") ?? definition.Description;

    /// <summary>
    /// Words the reason a value is not valid in the active language.
    /// </summary>
    /// <param name="validation">
    /// The reason reported by the option definition.
    /// </param>
    /// <returns>
    /// The message in the active language.
    /// </returns>
    public static string Validation(OptionValidation validation) => validation.Error switch
    {
        OptionValidationError.NotWholeNumber => Loc.Get("Validation_WholeNumber"),
        OptionValidationError.NotBool => Loc.Get("Validation_Bool"),
        OptionValidationError.NotDuration => Loc.Get("Validation_Duration"),
        OptionValidationError.NotInChoices => Loc.Get("Validation_Choice"),
        OptionValidationError.BelowMinimum => Loc.Get("Validation_AtLeast", FormatLimit(validation.Limit)),
        OptionValidationError.AboveMaximum => Loc.Get("Validation_AtMost", FormatLimit(validation.Limit)),
        _ => validation.Message,
    };

    /// <summary>
    /// Writes a bound the way the provider's own message does.
    /// </summary>
    /// <param name="limit">
    /// The bound.
    /// </param>
    /// <returns>
    /// The bound in invariant notation.
    /// </returns>
    private static string FormatLimit(double? limit) => (limit ?? 0).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Converts a bound data source to its name.
    /// </summary>
    private sealed class NameLabelConverter : IMultiValueConverter
    {
        /// <inheritdoc />
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
            values.Count > 0 && values[0] is ProviderDescriptor descriptor ? Name(descriptor) : null;
    }
}
