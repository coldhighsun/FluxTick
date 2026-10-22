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
public sealed class SettingsManager(ISettingsStore store, ILogger<SettingsManager> logger)
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
        var loaded = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _current, loaded ?? fallback);
        if (loaded is null)
        {
            await store.SaveAsync(Current, cancellationToken).ConfigureAwait(false);
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
    public async Task UpdateAsync(Action<AppSettings> mutate, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var next = Current.Clone();
            mutate(next);
            Volatile.Write(ref _current, next);
            Changed?.Invoke(this, EventArgs.Empty);
            try
            {
                await store.SaveAsync(next, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                logger.LogError(ex, "Failed to save settings");
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
