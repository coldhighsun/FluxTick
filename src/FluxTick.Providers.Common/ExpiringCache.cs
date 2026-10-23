namespace FluxTick.Providers.Common;

/// <summary>
/// Caches one asynchronously loaded value for a fixed time, for example a downloaded symbol list. Concurrent
/// callers that arrive while a load is running share that load.
/// </summary>
/// <typeparam name="T">
/// The cached value type.
/// </typeparam>
/// <param name="lifetime">
/// How long a loaded value stays valid.
/// </param>
/// <param name="timeProvider">
/// The clock.
/// </param>
public sealed class ExpiringCache<T>(TimeSpan lifetime, TimeProvider timeProvider)
    where T : class
{
    /// <summary>
    /// Guards the fields below.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// The cached value, or <see langword="null"/> before the first load.
    /// </summary>
    private T? _value;

    /// <summary>
    /// When the value was loaded.
    /// </summary>
    private DateTimeOffset _loadedAt;

    /// <summary>
    /// The load in flight, or <see langword="null"/>.
    /// </summary>
    private Task<T>? _loading;

    /// <summary>
    /// Gets the cached value, loading it first when there is none or it has expired. A caller that cancels stops
    /// waiting but does not cancel the load, because other callers may share it.
    /// </summary>
    /// <param name="load">
    /// Loads a fresh value. It runs at most once at a time and is never given a cancelled token on behalf of a caller.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that ends this caller's wait.
    /// </param>
    /// <returns>
    /// The value.
    /// </returns>
    public Task<T> GetAsync(Func<CancellationToken, Task<T>> load, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(load);

        Task<T> pending;
        TaskCompletionSource<T>? started = null;
        lock (_gate)
        {
            if (_value is { } cached && timeProvider.GetUtcNow() - _loadedAt < lifetime)
            {
                return Task.FromResult(cached);
            }

            if (_loading is null)
            {
                // The shared task is registered before the load starts, so the load can never finish and clear it
                // ahead of this assignment.
                started = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                _loading = started.Task;
            }

            pending = _loading;
        }

        if (started is not null)
        {
            // Run off the caller's context so the lock above is never held during the load.
            _ = Task.Run(() => LoadAsync(load, started), CancellationToken.None);
        }

        return pending.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Runs a load, stores its result and completes the shared task.
    /// </summary>
    /// <param name="load">
    /// Loads a fresh value.
    /// </param>
    /// <param name="source">
    /// The shared task that the waiting callers observe.
    /// </param>
    /// <returns>
    /// A task that completes when the load ended; it never throws.
    /// </returns>
    private async Task LoadAsync(Func<CancellationToken, Task<T>> load, TaskCompletionSource<T> source)
    {
        T fresh;
        try
        {
            fresh = await load(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _loading = null;
            }

            source.SetException(ex);
            return;
        }

        lock (_gate)
        {
            _value = fresh;
            _loadedAt = timeProvider.GetUtcNow();
            _loading = null;
        }

        source.SetResult(fresh);
    }
}
