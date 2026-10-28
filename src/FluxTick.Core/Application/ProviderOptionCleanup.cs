using FluxTick.Abstractions;

namespace FluxTick.Core.Application;

/// <summary>
/// Removes stored provider option values that equal the declared default.
/// Only options that differ from the default are kept in the settings file, so a default declared by a newer version
/// reaches every user who never customized the option. Earlier versions wrote every option explicitly; this brings
/// such files in line with the current rule.
/// </summary>
public static class ProviderOptionCleanup
{
    /// <summary>
    /// Removes the stored option values that are valid and equal to the default, however they are written.
    /// Values that are invalid are kept: the provider ignores them anyway, and a value that is out of range today
    /// may be valid again when the rules change, so deleting it would be irreversible for no gain.
    /// Values of options a descriptor does not declare, and of providers that have no descriptor, are left alone.
    /// </summary>
    /// <param name="settings">
    /// The settings to clean.
    /// </param>
    /// <param name="descriptors">
    /// The descriptors of the available providers.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when something was removed.
    /// </returns>
    public static bool DropOptionsAtDefault(AppSettings settings, IEnumerable<ProviderDescriptor> descriptors)
    {
        var changed = false;
        foreach (var descriptor in descriptors)
        {
            if (!settings.Providers.TryGetValue(descriptor.Id, out var stored))
            {
                continue;
            }

            foreach (var definition in descriptor.OptionDefinitions)
            {
                if (stored.Options.TryGetValue(definition.Key, out var value) && IsDefault(definition, value))
                {
                    stored.Options.Remove(definition.Key);
                    changed = true;
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Determines whether a stored value means the declared default.
    /// </summary>
    /// <param name="definition">
    /// The option definition.
    /// </param>
    /// <param name="stored">
    /// The stored value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value is valid and equals the default.
    /// </returns>
    private static bool IsDefault(ProviderOptionDefinition definition, string stored) =>
        definition.Validate(stored) is null
        && definition.Canonicalize(stored) == definition.Canonicalize(definition.DefaultValue);
}
