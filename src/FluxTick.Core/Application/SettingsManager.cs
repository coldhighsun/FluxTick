using Microsoft.Extensions.Logging;

namespace FluxTick.Core.Application;

/// <summary>
/// Abstraction over where application settings are persisted.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Loads the persisted settings.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token to cancel the operation.
    /// </param>
    /// <returns>
    /// The settings, or <see langword="null"/> when nothing usable is stored.
    /// </returns>
    Task<AppSettings?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists settings.
    /// </summary>
    /// <param name="settings">
    /// The settings to store.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the operation.
    /// </param>
    /// <returns>
    /// A task that completes once the settings are stored.
    /// </returns>
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>
/// Holds the current settings, notifies listeners about changes and persists them.
/// </summary>
/// <param name="store">
/// The backing store.
/// </param>
/// <param name="logger">
/// The logger.
/// </param>
/// <param name="timeProvider">
/// The clock; the system clock when none is given.
/// </param>
public sealed class SettingsManager(ISettingsStore store, ILogger<SettingsManager> logger, TimeProvider? timeProvider = null)
{
    /// <summary>
    /// Serializes updates and saves.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// The current immutable-by-convention settings instance.
    /// </summary>
    private AppSettings _current = new();

    /// <summary>
    /// Whether the stored settings could not be read, so that nothing may be saved over them yet.
    /// </summary>
    private volatile bool _loadFailed;

    /// <summary>
    /// How long to wait after a failed attempt before the stored settings are read again.
    /// </summary>
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The time from which the stored settings may be read again.
    /// </summary>
    private DateTimeOffset _nextRecovery;

    /// <summary>
    /// The clock that times the recovery attempts.
    /// </summary>
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Gets a value indicating whether the stored settings could not be read, so that the defaults are in use and changes
    /// are not saved until the file can be read.
    /// </summary>
    public bool StoredSettingsUnreadable => _loadFailed;

    /// <summary>
    /// Gets the current settings. Treat the instance as read-only; use <see cref="UpdateAsync"/> to change it.
    /// </summary>
    public AppSettings Current => Volatile.Read(ref _current);

    /// <summary>
    /// Raised after the settings changed. May be raised on any thread.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Loads the persisted settings, or adopts a fallback when nothing is stored.
    /// </summary>
    /// <param name="fallback">
    /// The settings for a first run.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the operation.
    /// </param>
    /// <returns>
    /// A task that completes once the settings are loaded.
    /// </returns>
    public async Task LoadAsync(AppSettings fallback, CancellationToken cancellationToken = default)
    {
        AppSettings? loaded;
        try
        {
            loaded = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file exists but cannot be read. Saving the fallback now would replace the user's settings with
            // defaults, so the fallback is only used in memory until the file can be read again; see TryUpdateAsync.
            logger.LogError(ex, "Settings could not be read; using defaults without saving them");
            fallback.Normalize();
            Volatile.Write(ref _current, fallback);
            _loadFailed = true;
            return;
        }

        var adopted = loaded ?? fallback;
        var normalized = adopted.Normalize();
        Volatile.Write(ref _current, adopted);
        if (loaded is null || normalized)
        {
            try
            {
                await store.SaveAsync(Current, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Failed to save settings");
            }
        }
    }

    /// <summary>
    /// Applies a change to a copy of the settings, publishes it and persists it.
    /// </summary>
    /// <param name="mutate">
    /// The change to apply to the copy.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the operation.
    /// </param>
    /// <returns>
    /// A task that completes once the change is published and saved.
    /// </returns>
    public Task UpdateAsync(Action<AppSettings> mutate, CancellationToken cancellationToken = default) =>
        TryUpdateAsync(
            next =>
            {
                mutate(next);
                return true;
            },
            cancellationToken);

    /// <summary>
    /// Applies a change to a copy of the settings and, when it changed something, publishes and persists it. A change
    /// that finds nothing to do costs neither a save nor a notification.
    /// </summary>
    /// <param name="mutate">
    /// The change to apply to the copy; it returns <see langword="false"/> when it left the copy as it was.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the operation.
    /// </param>
    /// <returns>
    /// A task that completes once the change is published and saved, with <see langword="true"/> when there was a change.
    /// </returns>
    public async Task<bool> TryUpdateAsync(Func<AppSettings, bool> mutate, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loadFailed && _time.GetUtcNow() >= _nextRecovery)
            {
                await TryRecoverAsync(cancellationToken).ConfigureAwait(false);
            }

            var next = Current.Clone();
            if (!mutate(next))
            {
                return false;
            }

            next.Normalize();
            Volatile.Write(ref _current, next);
            try
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                // A failing listener must not keep the change from being saved.
                logger.LogError(ex, "A listener of the settings change failed");
            }

            if (_loadFailed)
            {
                // The stored settings were never read; writing now would replace them with the defaults.
                return true;
            }

            try
            {
                await store.SaveAsync(next, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Failed to save settings");
            }

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reads the stored settings again after the first read failed, and adopts them when that works. Must be called
    /// while holding the gate.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token to cancel the operation.
    /// </param>
    /// <returns>
    /// A task that completes when the attempt ended.
    /// </returns>
    private async Task TryRecoverAsync(CancellationToken cancellationToken)
    {
        try
        {
            var stored = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (stored is not null)
            {
                stored.Normalize();
                Volatile.Write(ref _current, stored);
            }

            // Without a stored file, the defaults are all there is, and they may be saved.
            _loadFailed = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Settings still cannot be read; the change is kept in memory only");

            // Reading retries for a moment, with the gate held; do not do that with every change.
            _nextRecovery = _time.GetUtcNow() + RecoveryInterval;
        }
    }
}
