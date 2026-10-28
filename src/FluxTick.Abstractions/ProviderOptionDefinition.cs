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
/// Why a value is not valid for a provider option, so that the UI can word the message in its own language.
/// </summary>
public enum OptionValidationError
{
    /// <summary>
    /// The value is not a whole number.
    /// </summary>
    NotWholeNumber,

    /// <summary>
    /// The value is not true or false.
    /// </summary>
    NotBool,

    /// <summary>
    /// The value is not a duration.
    /// </summary>
    NotDuration,

    /// <summary>
    /// The value is not one of the listed choices.
    /// </summary>
    NotInChoices,

    /// <summary>
    /// The value is below the lower bound given in <see cref="OptionValidation.Limit"/>.
    /// </summary>
    BelowMinimum,

    /// <summary>
    /// The value is above the upper bound given in <see cref="OptionValidation.Limit"/>.
    /// </summary>
    AboveMaximum,

    /// <summary>
    /// A required value is missing.
    /// </summary>
    Required,
}

/// <summary>
/// The reason a value is not valid for a provider option.
/// </summary>
/// <param name="Error">
/// What is wrong with the value.
/// </param>
/// <param name="Message">
/// An English message that describes it.
/// </param>
/// <param name="Limit">
/// The bound that was crossed, for <see cref="OptionValidationError.BelowMinimum"/> and
/// <see cref="OptionValidationError.AboveMaximum"/>.
/// </param>
public readonly record struct OptionValidation(OptionValidationError Error, string Message, double? Limit = null);

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
/// <param name="Required">
/// Whether a <see cref="ProviderOptionType.String"/> or <see cref="ProviderOptionType.Secret"/> option must not be
/// blank, such as an API key. The settings page opens the options of a source that is switched on while one is missing,
/// and the source is not enabled in the settings file until every required value is given.
/// </param>
public sealed record ProviderOptionDefinition(
    string Key,
    string Label,
    ProviderOptionType Type,
    string DefaultValue,
    string? Description = null,
    IReadOnlyList<string>? Choices = null,
    double? Min = null,
    double? Max = null,
    bool Required = false)
{
    /// <summary>
    /// The key of the option in which the polled sources keep how often they fetch prices.
    /// </summary>
    public const string PollIntervalKey = "PollInterval";

    /// <summary>
    /// The key of the option that sets how long a live stream may stay quiet during trading hours before the source polls
    /// its REST API as well, for the sources that stream and use REST for what the stream misses.
    /// </summary>
    public const string QuietPollIntervalKey = "QuietPollInterval";

    /// <summary>
    /// Validates a stored text value against this definition.
    /// </summary>
    /// <param name="value">
    /// The value to validate.
    /// </param>
    /// <returns>
    /// An error message, or <see langword="null"/> when the value is valid.
    /// </returns>
    public string? Validate(string? value) => Check(value)?.Message;

    /// <summary>
    /// Determines whether a value is missing for an option that requires one: the option is required, holds text, and the
    /// value is blank. This is the one place that says what a missing required value is.
    /// </summary>
    /// <param name="value">
    /// The value to check.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a required value is missing.
    /// </returns>
    public bool IsMissing(string? value) =>
        Required && Type is ProviderOptionType.String or ProviderOptionType.Secret && string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Validates a stored text value against this definition and says what is wrong with it.
    /// </summary>
    /// <param name="value">
    /// The value to validate.
    /// </param>
    /// <returns>
    /// The reason the value is not valid, or <see langword="null"/> when it is valid.
    /// </returns>
    public OptionValidation? Check(string? value)
    {
        value ??= string.Empty;
        if (IsMissing(value))
        {
            return new(OptionValidationError.Required, "A value is required.");
        }

        switch (Type)
        {
            case ProviderOptionType.Int:
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                {
                    return new(OptionValidationError.NotWholeNumber, "Must be a whole number.");
                }

                return CheckRange(number);
            case ProviderOptionType.Bool:
                return bool.TryParse(value, out _) ? null : new(OptionValidationError.NotBool, "Must be true or false.");
            case ProviderOptionType.Duration:
                if (!TryParseDuration(value, out var span))
                {
                    return new(OptionValidationError.NotDuration, "Must be a duration.");
                }

                // Zero or negative would make a polling loop spin or throw, whatever bounds the option declares. A
                // positive Min already rejects it below, with its own limit.
                if (span <= TimeSpan.Zero && Min is not > 0)
                {
                    return new(OptionValidationError.BelowMinimum, "Must be greater than zero.", 0);
                }

                return CheckRange(span.TotalSeconds);
            case ProviderOptionType.Choice:
                return Choices is not null && Choices.Contains(value)
                    ? null
                    : new(OptionValidationError.NotInChoices, "Must be one of the listed choices.");
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
    /// The reason the value is out of range, or <see langword="null"/> when it is within range.
    /// </returns>
    private OptionValidation? CheckRange(double number)
    {
        if (Min is { } min && number < min)
        {
            return new(OptionValidationError.BelowMinimum, $"Must be at least {min.ToString(CultureInfo.InvariantCulture)}.", min);
        }

        if (Max is { } max && number > max)
        {
            return new(OptionValidationError.AboveMaximum, $"Must be at most {max.ToString(CultureInfo.InvariantCulture)}.", max);
        }

        return null;
    }
}
