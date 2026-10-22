using System.Globalization;

namespace FluxTick.Abstractions;

/// <summary>
/// The kind of value a provider option holds. Drives validation and the editor shown in the settings UI.
/// </summary>
public enum ProviderOptionType
{
    /// <summary>
    /// Free text.
    /// </summary>
    String,

    /// <summary>
    /// Free text that must not be displayed or logged (API keys, tokens).
    /// </summary>
    Secret,

    /// <summary>
    /// A whole number.
    /// </summary>
    Int,

    /// <summary>
    /// A true/false switch.
    /// </summary>
    Bool,

    /// <summary>
    /// A <see cref="TimeSpan"/>, stored in the invariant constant format (<c>c</c>) and edited in seconds.
    /// </summary>
    Duration,

    /// <summary>
    /// One value out of <see cref="ProviderOptionDefinition.Choices"/>.
    /// </summary>
    Choice,
}

/// <summary>
/// Declares one user-configurable option of a provider. The settings UI renders its editor from this declaration,
/// so a new provider never needs UI changes.
/// </summary>
/// <param name="Key">
/// The stable key the value is stored under.
/// </param>
/// <param name="Label">
/// The label shown to the user.
/// </param>
/// <param name="Type">
/// The kind of value.
/// </param>
/// <param name="DefaultValue">
/// The default value in its stored text form.
/// </param>
/// <param name="Description">
/// Optional help text.
/// </param>
/// <param name="Choices">
/// The allowed values when <paramref name="Type"/> is <see cref="ProviderOptionType.Choice"/>.
/// </param>
/// <param name="Min">
/// Optional inclusive lower bound for <see cref="ProviderOptionType.Int"/> (value) and
/// <see cref="ProviderOptionType.Duration"/> (seconds).
/// </param>
/// <param name="Max">
/// Optional inclusive upper bound, interpreted like <paramref name="Min"/>.
/// </param>
public sealed record ProviderOptionDefinition(
    string Key,
    string Label,
    ProviderOptionType Type,
    string DefaultValue,
    string? Description = null,
    IReadOnlyList<string>? Choices = null,
    double? Min = null,
    double? Max = null)
{
    /// <summary>
    /// Validates a stored text value against this definition.
    /// </summary>
    /// <param name="value">
    /// The value to validate.
    /// </param>
    /// <returns>
    /// An error message, or <see langword="null"/> when the value is valid.
    /// </returns>
    public string? Validate(string? value)
    {
        value ??= string.Empty;
        switch (Type)
        {
            case ProviderOptionType.Int:
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                {
                    return "Must be a whole number.";
                }

                return CheckRange(number);
            case ProviderOptionType.Bool:
                return bool.TryParse(value, out _) ? null : "Must be true or false.";
            case ProviderOptionType.Duration:
                if (!TryParseDuration(value, out var span))
                {
                    return "Must be a duration.";
                }

                return CheckRange(span.TotalSeconds);
            case ProviderOptionType.Choice:
                return Choices is not null && Choices.Contains(value) ? null : "Must be one of the listed choices.";
            default:
                return null;
        }
    }

    /// <summary>
    /// Writes a valid stored value in one canonical text form, so values that mean the same compare as equal
    /// whichever way they were written (for example <c>05</c> and <c>5</c>, or <c>00:00:05</c> and <c>00:00:05.0000000</c>).
    /// A value that cannot be read for its type is returned unchanged.
    /// </summary>
    /// <param name="value">
    /// The stored value.
    /// </param>
    /// <returns>
    /// The canonical text form.
    /// </returns>
    internal string Canonicalize(string value) => Type switch
    {
        ProviderOptionType.Int when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            => number.ToString(CultureInfo.InvariantCulture),
        ProviderOptionType.Bool when bool.TryParse(value, out var flag)
            => flag ? "true" : "false",
        ProviderOptionType.Duration when TryParseDuration(value, out var span)
            => FormatDuration(span),
        _ => value,
    };

    /// <summary>
    /// Reads a duration in its stored text form (the invariant constant format, such as <c>00:00:05</c>).
    /// A bare number is not accepted, although the format itself would read it as a number of days.
    /// </summary>
    /// <param name="value">
    /// The stored text.
    /// </param>
    /// <param name="duration">
    /// The duration when the text is valid.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the text is a valid stored duration.
    /// </returns>
    internal static bool TryParseDuration(string? value, out TimeSpan duration)
    {
        duration = default;
        return value is not null
            && value.Contains(':')
            && TimeSpan.TryParseExact(value, "c", CultureInfo.InvariantCulture, out duration);
    }

    /// <summary>
    /// Writes a duration in its stored text form.
    /// </summary>
    /// <param name="duration">
    /// The duration.
    /// </param>
    /// <returns>
    /// The stored text.
    /// </returns>
    internal static string FormatDuration(TimeSpan duration) => duration.ToString("c", CultureInfo.InvariantCulture);

    /// <summary>
    /// Checks a numeric value against <see cref="Min"/> and <see cref="Max"/>.
    /// </summary>
    /// <param name="number">
    /// The value to check.
    /// </param>
    /// <returns>
    /// An error message, or <see langword="null"/> when the value is within range.
    /// </returns>
    private string? CheckRange(double number)
    {
        if (Min is { } min && number < min)
        {
            return $"Must be at least {min.ToString(CultureInfo.InvariantCulture)}.";
        }

        if (Max is { } max && number > max)
        {
            return $"Must be at most {max.ToString(CultureInfo.InvariantCulture)}.";
        }

        return null;
    }
}
