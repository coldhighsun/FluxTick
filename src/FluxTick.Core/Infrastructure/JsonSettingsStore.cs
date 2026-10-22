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
    /// <inheritdoc />
    public async Task<AppSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, AppSettings.JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            var backup = $"{path}.bad";
            logger.LogWarning(ex, "Settings file is corrupt; moving it to {Backup} and using defaults", backup);
            File.Move(path, backup, overwrite: true);
            return null;
        }
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
