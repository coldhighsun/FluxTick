using System.Text.Json;
using FluxTick.Core.Application;
using Microsoft.Extensions.Logging;

namespace FluxTick.Core.Infrastructure;

/// <summary>
/// Stores settings as an indented JSON file. Writes are atomic and reach the disk before the old file is replaced, the
/// file before each save is kept as a copy, and a corrupt file is set aside and replaced by that copy instead of crashing.
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
    /// The suffix of the copy that is kept of the settings file before each save.
    /// </summary>
    private const string CopySuffix = ".bak";

    /// <summary>
    /// The suffix of the temporary file that a save writes before it takes the place of the settings file.
    /// </summary>
    private const string TempSuffix = ".tmp";

    /// <summary>
    /// The pause between two attempts to read the file.
    /// </summary>
    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Lets only one write of the settings file, its copy and their temporary files run at a time.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public async Task<AppSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        // A power loss in the middle of replacing the file can leave only the copy, so that alone is not a first run.
        if (!File.Exists(path) && !File.Exists($"{path}{CopySuffix}"))
        {
            return null;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // A save in progress replaces the file and its copy, so a read that overlaps it could take a half-written
                // state for a corrupt file and restore an older copy over the new settings.
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return await ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _gate.Release();
                }
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
    /// Reads the settings file once. A corrupt file is moved aside and the last good copy is used instead; a file that
    /// cannot be read throws, so that the caller does not take it for a first run and write defaults over it.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token to cancel the operation.
    /// </param>
    /// <returns>
    /// The settings, or <see langword="null"/> when the file is corrupt and there is no usable copy of it.
    /// </returns>
    private async Task<AppSettings?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            logger.LogWarning("Settings file is missing but an earlier copy exists");
            return await RestoreLastGoodCopyAsync(fileMissing: true, cancellationToken).ConfigureAwait(false);
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = Parse(bytes);
            if (settings is not null)
            {
                return settings;
            }

            logger.LogWarning("Settings file holds no settings");
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Settings file is corrupt");
        }

        return await RestoreLastGoodCopyAsync(fileMissing: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Parses the content of a settings file.
    /// </summary>
    /// <param name="bytes">
    /// The UTF-8 content of the file.
    /// </param>
    /// <returns>
    /// The settings, or <see langword="null"/> when the content is a JSON null.
    /// </returns>
    /// <exception cref="JsonException">
    /// The content is not valid JSON of a settings file, for example because it is empty or filled with zeros.
    /// </exception>
    private static AppSettings? Parse(byte[] bytes) =>
        JsonSerializer.Deserialize<SettingsFile>(bytes, AppSettings.JsonOptions)?.ToSettings();

    /// <summary>
    /// Sets the corrupt settings file aside so that it can be looked at later.
    /// </summary>
    /// <param name="move">
    /// <see langword="true"/> to move the file, <see langword="false"/> to copy it and leave the original in place.
    /// </param>
    private void SetCorruptFileAside(bool move)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var backup = $"{path}.bad";
        try
        {
            if (move)
            {
                File.Move(path, backup, overwrite: true);
            }
            else
            {
                File.Copy(path, backup, overwrite: true);
            }

            logger.LogWarning("Kept the corrupt settings file as {Backup}", backup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not set the corrupt settings file aside");
        }
    }

    /// <summary>
    /// Reads the copy of the settings that was kept before the last save, and puts it back as the settings file.
    /// A power loss can leave the file empty or filled with zeros; the copy is the state just before. A copy that cannot be
    /// read at the moment throws before the corrupt file is touched, so that the caller does not take the settings for
    /// missing and save defaults over them, and a retry finds everything where it was. The corrupt file is only
    /// copied aside and is replaced in one step by the restored one, so if that fails the corrupt file stays at its place
    /// and the next start tries again. When the settings file is missing, a complete temporary file of a save that could
    /// not take its place is newer than the copy and is used first. The caller holds the write gate.
    /// </summary>
    /// <param name="fileMissing">
    /// <see langword="true"/> when the settings file is missing, so that an unfinished save may hold the newest settings.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the operation.
    /// </param>
    /// <returns>
    /// The settings of the copy, or <see langword="null"/> when there is none or it is corrupt too.
    /// </returns>
    private async Task<AppSettings?> RestoreLastGoodCopyAsync(bool fileMissing, CancellationToken cancellationToken)
    {
        var source = $"{path}{TempSuffix}";
        var (bytes, settings) = fileMissing
            ? await TryReadUnfinishedSaveAsync(source, cancellationToken).ConfigureAwait(false)
            : (null, null);
        if (settings is null)
        {
            source = $"{path}{CopySuffix}";
            if (File.Exists(source))
            {
                bytes = await File.ReadAllBytesAsync(source, cancellationToken).ConfigureAwait(false);
                try
                {
                    settings = Parse(bytes);
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "The earlier copy of the settings is corrupt");
                }
            }
        }

        if (settings is null || bytes is null)
        {
            SetCorruptFileAside(move: true);
            logger.LogWarning("There is no usable earlier copy of the settings; using defaults");
            return null;
        }

        SetCorruptFileAside(move: false);
        try
        {
            // This writes through the temporary file too, so a restored unfinished save is consumed by it.
            await WriteDurablyAsync(path, bytes, backup: null, cancellationToken).ConfigureAwait(false);
            logger.LogWarning("Restored the settings from {Source}", source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The corrupt file is still in place and so is the copy, so the next start can try again; the settings of
            // the copy are still usable now.
            logger.LogWarning(ex, "Could not put the earlier copy of the settings back");
        }

        return settings;
    }

    /// <summary>
    /// Reads the temporary file that a save left behind when it could not take the place of the settings file. It is
    /// only a better source than the copy, so a file that is missing, cannot be read or is incomplete is skipped.
    /// </summary>
    /// <param name="temp">
    /// The full path of the temporary file.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the operation.
    /// </param>
    /// <returns>
    /// The content and settings of the file, or <see langword="null"/> values when it cannot be used.
    /// </returns>
    private async Task<(byte[]? Bytes, AppSettings? Settings)> TryReadUnfinishedSaveAsync(
        string temp,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(temp))
        {
            return (null, null);
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(temp, cancellationToken).ConfigureAwait(false);
            return (bytes, Parse(bytes));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The unfinished save of the settings is not usable");
            return (null, null);
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

        // Saves share one temporary file name, so two of them must not overlap. The settings are serialized under the
        // same gate, so that the save that runs last also writes the newest state.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, AppSettings.JsonOptions);
            await WriteDurablyAsync(path, bytes, $"{path}{CopySuffix}", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Writes a file so that it is complete on the disk before it takes the place of the old one: the data goes to a
    /// temporary file that is flushed to the disk, and that file is then moved over the target. When a backup path is
    /// given, the file that is replaced becomes that backup in the same operation.
    /// </summary>
    /// <param name="target">
    /// The full path of the file to write.
    /// </param>
    /// <param name="bytes">
    /// The content of the file.
    /// </param>
    /// <param name="backup">
    /// The full path that receives the file being replaced, or <see langword="null"/> to keep no backup.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the operation.
    /// </param>
    /// <returns>
    /// A task that completes when the file is in place.
    /// </returns>
    private async Task WriteDurablyAsync(
        string target,
        byte[] bytes,
        string? backup,
        CancellationToken cancellationToken)
    {
        var temp = $"{target}{TempSuffix}";
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            // The file may hold secrets such as API keys, and the default would let other users on the machine read it. It is
            // created with this mode, not changed afterwards, so it is never readable. On Windows the folder of the user is
            // private already.
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        // Opened outside the try block: when this fails the temporary file may belong to someone else and is left alone.
        var stream = new FileStream(temp, options);
        var complete = false;
        try
        {
            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);

                // Without this the data can still be in the cache of the system when the power fails, and the file ends up as
                // long as it should be but full of zeros.
                // It can take a while on a busy disk, and the caller may be the UI thread, so it runs on the thread pool.
                await Task.Run(() => stream.Flush(flushToDisk: true), CancellationToken.None).ConfigureAwait(false);
            }

            complete = true;
            if (backup is not null && File.Exists(target) && IsUsableSettingsFile(target) && TryReplaceKeepingBackup(temp, target, backup))
            {
                return;
            }

            File.Move(temp, target, overwrite: true);
        }
        catch
        {
            // A replace that fails halfway can move the old file to the backup without putting the new one in its place.
            // The complete temporary file is then the only copy of this save, so it is kept for the next load.
            if (complete && !Path.Exists(target))
            {
                logger.LogWarning("The settings file is gone; keeping {Temp} for the next start", temp);
            }
            else
            {
                // A partly written file would stay next to the settings, and it may hold secrets such as API keys.
                TryDeleteTemporaryFile(temp);
            }

            throw;
        }
    }

    /// <summary>
    /// Deletes a temporary file that a failed write left behind. A file that cannot be deleted is left for the next save,
    /// which overwrites it.
    /// </summary>
    /// <param name="temp">
    /// The full path of the temporary file.
    /// </param>
    private void TryDeleteTemporaryFile(string temp)
    {
        try
        {
            File.Delete(temp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not delete the temporary settings file {Temp}", temp);
        }
    }

    /// <summary>
    /// Checks that a settings file can be read and parsed, so that only a good file is ever kept as the backup.
    /// </summary>
    /// <param name="file">
    /// The full path of the file to check.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the file holds settings; <see langword="false"/> when it is corrupt or cannot be read.
    /// </returns>
    private bool IsUsableSettingsFile(string file)
    {
        try
        {
            return Parse(File.ReadAllBytes(file)) is not null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The current settings file is not usable; it is not kept as the earlier copy");
            return false;
        }
    }

    /// <summary>
    /// Moves the temporary file over the target and keeps the replaced file as the backup. The backup is only a safety
    /// net, so a backup that cannot be written must not stop the save.
    /// </summary>
    /// <param name="temp">
    /// The full path of the complete new file.
    /// </param>
    /// <param name="target">
    /// The full path of the file to replace.
    /// </param>
    /// <param name="backup">
    /// The full path that receives the replaced file.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the target was replaced; <see langword="false"/> when the caller has to move the file itself.
    /// </returns>
    private bool TryReplaceKeepingBackup(string temp, string target, string backup)
    {
        try
        {
            File.Replace(temp, target, backup);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not keep the earlier copy of the settings; saving without it");
            return false;
        }
    }
}
