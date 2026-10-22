using System.Globalization;

namespace FluxTick.Abstractions;

/// <summary>
/// The effective option values handed to a provider. Missing or invalid values fall back to the declared defaults,
/// so a provider never has to deal with hand-edited settings.
/// </summary>
public sealed class ProviderOptions
{
    /// <summary>
    /// The descriptor that declares the options.
    /// </summary>
    private readonly ProviderDescriptor _descriptor;

    /// <summary>
    /// The raw stored values.
    /// </summary>
    private readonly IReadOnlyDictionary<string, string> _values;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderOptions"/> class.
    /// </summary>
    /// <param name="descriptor">
    /// The descriptor that declares the options.
    /// </param>
    /// <param name="values">
    /// The stored values, or <see langword="null"/> to use only defaults.
    /// </param>
    public ProviderOptions(ProviderDescriptor descriptor, IReadOnlyDictionary<string, string>? values = null)
    {
        _descriptor = descriptor;
        _values = values ?? new Dictionary<string, string>();
    }

    /// <summary>
    /// Gets the effective text value of an option.
    /// </summary>
    /// <param name="key">
    /// The option key.
    /// </param>
    /// <returns>
    /// The stored value when valid, otherwise the declared default.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The key is not declared by the descriptor.
    /// </exception>
    public string GetString(string key)
    {
        var definition = _descriptor.OptionDefinitions.FirstOrDefault(o => o.Key == key)
            ?? throw new ArgumentException($"Unknown option '{key}'.", nameof(key));

        if (_values.TryGetValue(key, out var stored) && definition.Validate(stored) is null)
        {
            return stored;
        }

        return definition.DefaultValue;
    }

    /// <summary>
    /// Gets the effective value of an <see cref="ProviderOptionType.Int"/> option.
    /// </summary>
    /// <param name="key">
    /// The option key.
    /// </param>
    /// <returns>
    /// The parsed value.
    /// </returns>
    public int GetInt(string key) => int.Parse(GetString(key), CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets the effective value of a <see cref="ProviderOptionType.Bool"/> option.
    /// </summary>
    /// <param name="key">
    /// The option key.
    /// </param>
    /// <returns>
    /// The parsed value.
    /// </returns>
    public bool GetBool(string key) => bool.Parse(GetString(key));

    /// <summary>
    /// Gets the effective value of a <see cref="ProviderOptionType.Duration"/> option.
    /// </summary>
    /// <param name="key">
    /// The option key.
    /// </param>
    /// <returns>
    /// The parsed value.
    /// </returns>
    public TimeSpan GetDuration(string key) =>
        ProviderOptionDefinition.TryParseDuration(GetString(key), out var duration)
            ? duration
            : throw new FormatException($"Option '{key}' is not a valid duration.");
}
