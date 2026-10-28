using System.Security.Cryptography;
using FluxTick.Abstractions;
using FluxTick.Core.Application;
using Microsoft.Extensions.Logging;

namespace FluxTick.Core.Infrastructure;

/// <summary>
/// Keeps the secret options of the data sources (<see cref="ProviderOptionType.Secret"/>, such as an API key) protected in
/// the settings file. Around another store it protects them as they are written and opens them as they are read, so that
/// everything above it, from the settings manager to the providers, works with the plain values. A value from an older
/// version, which is stored as it is, is protected the first time the settings are loaded.
/// </summary>
/// <param name="inner">
/// The store that reads and writes the file.
/// </param>
/// <param name="protector">
/// Protects and opens the values.
/// </param>
/// <param name="descriptors">
/// Gives the data sources, which tell which of their options are secret.
/// </param>
/// <param name="logger">
/// The logger; it never gets a value.
/// </param>
public sealed class ProtectedSettingsStore(
    ISettingsStore inner,
    ISecretProtector protector,
    Func<IEnumerable<ProviderDescriptor>> descriptors,
    ILogger<ProtectedSettingsStore> logger) : ISettingsStore
{
    /// <summary>
    /// The mark in front of a protected value. A stored value without it comes from a version that stored them as they are.
    /// </summary>
    public const string Prefix = "protected:v1:";

    /// <summary>
    /// The protected values that could not be opened, as they are in the file, by data source and option. They are written
    /// back while the data source is switched on and no new value is entered, so that a value that cannot be opened now
    /// (another account, a profile that is not loaded) is not lost for good by an unrelated save. Switching the data source
    /// off is how the user lets go of one: a source that is off has no use for a key that cannot be opened.
    /// </summary>
    private readonly Dictionary<(string ProviderId, string Key), string> _unreadable = [];

    /// <summary>
    /// Guards <see cref="_unreadable"/>.
    /// </summary>
    private readonly object _unreadableLock = new();

    /// <inheritdoc />
    public async Task<AppSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await inner.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (settings is null)
        {
            return null;
        }

        var foundPlain = false;
        foreach (var secret in SecretOptions())
        {
            if (!settings.Providers.TryGetValue(secret.ProviderId, out var provider)
                || !provider.Options.TryGetValue(secret.Key, out var stored)
                || stored.Length == 0)
            {
                continue;
            }

            if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
            {
                foundPlain = true;
            }
            else if (protector.TryUnprotect(stored[Prefix.Length..], secret.Purpose, out var plain))
            {
                provider.Options[secret.Key] = plain;
            }
            else
            {
                // Another user or machine, or a damaged file. The settings do not get the value, so the source asks for it
                // again; the text stays in the file until it is replaced.
                logger.LogWarning("The {Key} of {Provider} could not be read; enter it again", secret.Key, secret.ProviderId);
                lock (_unreadableLock)
                {
                    _unreadable[(secret.ProviderId, secret.Key)] = stored;
                }

                provider.Options.Remove(secret.Key);
            }
        }

        if (foundPlain && protector.IsEffective)
        {
            try
            {
                await SaveAsync(settings, cancellationToken).ConfigureAwait(false);
                logger.LogInformation(
                    "Secret options were stored as they were; they are protected now. Older copies of the settings file, such as settings.json.bad, may still hold them as they were");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Secret options could not be protected in the settings file yet");
            }
        }

        return settings;
    }

    /// <inheritdoc />
    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return inner.SaveAsync(PrepareForFile(settings, SecretOptions()), cancellationToken);
    }

    /// <summary>
    /// Gets what is written to the file for the settings in use. When there is nothing to protect and nothing to write back,
    /// that is the settings themselves; settings are saved after each small change, so no copy is made then.
    /// </summary>
    /// <param name="settings">
    /// The settings in use, which keep the plain values.
    /// </param>
    /// <param name="secrets">
    /// The secret options of the data sources.
    /// </param>
    /// <returns>
    /// The settings to write.
    /// </returns>
    private AppSettings PrepareForFile(AppSettings settings, List<SecretOption> secrets)
    {
        lock (_unreadableLock)
        {
            var protectsSomething = protector.IsEffective && secrets.Any(s => ValueOf(settings, s) is not null);
            if (!protectsSomething && _unreadable.Count == 0)
            {
                return settings;
            }

            var copy = settings.Clone();
            foreach (var secret in secrets)
            {
                if (copy.Providers.TryGetValue(secret.ProviderId, out var provider))
                {
                    if (ValueOf(copy, secret) is { } entered)
                    {
                        EnteredValue(provider, secret, entered);
                    }
                    else
                    {
                        WriteBackUnreadable(copy, provider, secret);
                    }
                }
            }

            return copy;
        }
    }

    /// <summary>
    /// Handles a value the user entered: it replaces one that could not be opened, and is protected for the file.
    /// </summary>
    /// <param name="provider">
    /// The options of the data source in the copy that is written.
    /// </param>
    /// <param name="secret">
    /// The option.
    /// </param>
    /// <param name="entered">
    /// The value.
    /// </param>
    private void EnteredValue(ProviderSettings provider, SecretOption secret, string entered)
    {
        _unreadable.Remove((secret.ProviderId, secret.Key));
        if (protector.IsEffective)
        {
            provider.Options[secret.Key] = Prefix + ProtectOrThrow(entered, secret);
        }
    }

    /// <summary>
    /// Writes back the text of a value that could not be opened, if the data source is still switched on; if the user
    /// switched it off, the text is let go.
    /// </summary>
    /// <param name="copy">
    /// The settings that are written.
    /// </param>
    /// <param name="provider">
    /// The options of the data source in the copy.
    /// </param>
    /// <param name="secret">
    /// The option.
    /// </param>
    private void WriteBackUnreadable(AppSettings copy, ProviderSettings provider, SecretOption secret)
    {
        if (!_unreadable.TryGetValue((secret.ProviderId, secret.Key), out var text))
        {
            return;
        }

        if (copy.IsProviderSwitchedOn(secret.Descriptor))
        {
            provider.Options[secret.Key] = text;
        }
        else
        {
            _unreadable.Remove((secret.ProviderId, secret.Key));
        }
    }

    /// <summary>
    /// Gets the value of a secret option in settings.
    /// </summary>
    /// <param name="settings">
    /// The settings.
    /// </param>
    /// <param name="secret">
    /// The option.
    /// </param>
    /// <returns>
    /// The value, or <see langword="null"/> when there is none or it is empty.
    /// </returns>
    private static string? ValueOf(AppSettings settings, SecretOption secret) =>
        settings.Providers.TryGetValue(secret.ProviderId, out var provider)
        && provider.Options.TryGetValue(secret.Key, out var value)
        && value.Length > 0
            ? value
            : null;

    /// <summary>
    /// Protects a value, reporting a failure the way a file that cannot be written is reported.
    /// </summary>
    /// <param name="plain">
    /// The value.
    /// </param>
    /// <param name="secret">
    /// The option.
    /// </param>
    /// <returns>
    /// The protected text.
    /// </returns>
    private string ProtectOrThrow(string plain, SecretOption secret)
    {
        try
        {
            return protector.Protect(plain, secret.Purpose);
        }
        catch (CryptographicException ex)
        {
            // The settings manager knows how to deal with a file that cannot be written; it does not know this.
            throw new IOException($"The {secret.Key} of {secret.ProviderId} could not be protected.", ex);
        }
    }

    /// <summary>
    /// Lists the secret options of the data sources.
    /// </summary>
    /// <returns>
    /// The options.
    /// </returns>
    private List<SecretOption> SecretOptions() =>
    [
        .. descriptors().SelectMany(d => d.OptionDefinitions
            .Where(o => o.Type == ProviderOptionType.Secret)
            .Select(o => new SecretOption(d, o.Key))),
    ];

    /// <summary>
    /// A secret option of a data source.
    /// </summary>
    /// <param name="Descriptor">
    /// The data source.
    /// </param>
    /// <param name="Key">
    /// The option.
    /// </param>
    private sealed record SecretOption(ProviderDescriptor Descriptor, string Key)
    {
        /// <summary>
        /// Gets the id of the data source.
        /// </summary>
        public string ProviderId => Descriptor.Id;

        /// <summary>
        /// Gets what a protected value is bound to: the option it belongs to, so that it does not open for another.
        /// </summary>
        public string Purpose => $"{Descriptor.Id}:{Key}";
    }
}
