using System.Text.Json;
using FluxTick.Core.Application;
using Microsoft.Extensions.Logging;

namespace FluxTick.Core.Infrastructure;

/// <summary>
/// Stores settings as an indented JSON file. Writes are atomic and a corrupt file is set aside instead of crashing.
/// </summary>
/// <param name="path">
/// The full path of the settings file.
/// </param>
/// <param name="logger">
/// The logger.
/// </param>
public sealed class JsonSettingsStore(string path, ILogger<JsonSettingsStore> logger) : ISettingsStore
{
    /// <summary>
    /// How often a file that cannot be opened is tried before the failure is reported.
    /// </summary>
    private const int ReadAttempts = 3;

    /// <summary>
    /// The pause between two attempts to read the file.
    /// </summary>
    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(150);

    /// <inheritdoc />
    public async Task<AppSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < ReadAttempts)
            {
                // A scanner or sync tool often holds the file for a moment; the settings must not be mistaken for missing.
                logger.LogDebug(ex, "Settings file is busy; trying again");
                await Task.Delay(ReadRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Reads the settings file once. A corrupt file is moved aside; a file that cannot be read throws, so that the
    /// caller does not take it for a first run and write defaults over it.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token to cancel the operation.
    /// </param>
    /// <returns>
    /// The settings, or <see langword="null"/> when the file is corrupt.
    /// </returns>
    private async Task<AppSettings?> ReadAsync(CancellationToken cancellationToken)
    {
        SettingsFile? file;
        try
        {
            await using var stream = File.OpenRead(path);
            file = await JsonSerializer.DeserializeAsync<SettingsFile>(stream, AppSettings.JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            var backup = $"{path}.bad";
            logger.LogWarning(ex, "Settings file is corrupt; moving it to {Backup} and using defaults", backup);
            try
            {
                File.Move(path, backup, overwrite: true);
            }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(moveEx, "Could not move the corrupt settings file aside");
            }

            return null;
        }

        if (file is null)
        {
            return null;
        }

        return file.ToSettings();
    }

    /// <inheritdoc />
    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = $"{path}.tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, settings, AppSettings.JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temp, path, overwrite: true);
    }
}
