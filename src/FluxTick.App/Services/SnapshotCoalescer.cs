using System.Collections.Concurrent;
using FluxTick.Abstractions;
using FluxTick.Core.Application;

namespace FluxTick.App.Services;

/// <summary>
/// Hands market snapshots to the UI thread without letting a fast feed flood it. Only the latest snapshot of each
/// instrument is kept while the UI thread is busy, and the hand-over runs after pending input, so a click is never
/// stuck behind a queue of prices that have already been replaced.
/// </summary>
/// <param name="ui">
/// The UI thread dispatcher.
/// </param>
/// <param name="apply">
/// Applies one snapshot; it runs on the UI thread.
/// </param>
/// <param name="onError">
/// Called when a hand-over could not be queued. Such a failure never reaches the caller of
/// <see cref="Submit"/>, which is a data source's quote loop that must not end because the UI is not available.
/// </param>
public sealed class SnapshotCoalescer(IUiDispatcher ui, Action<AssetSnapshot> apply, Action<Exception>? onError = null)
{
    /// <summary>
    /// The latest snapshot of each instrument that has not been applied yet, keyed by the instrument id.
    /// </summary>
    private readonly ConcurrentDictionary<AssetId, AssetSnapshot> _pending = new();

    /// <summary>
    /// 1 while a hand-over is queued and has not started yet, otherwise 0.
    /// </summary>
    private int _scheduled;

    /// <summary>
    /// Offers a snapshot. It replaces an older one of the same instrument that was not applied yet. May be called
    /// from any thread.
    /// </summary>
    /// <param name="snapshot">
    /// The snapshot.
    /// </param>
    public void Submit(AssetSnapshot snapshot)
    {
        _pending[snapshot.Asset.Id] = snapshot;
        Schedule();
    }

    /// <summary>
    /// Queues a hand-over unless one is queued already.
    /// </summary>
    private void Schedule()
    {
        if (Interlocked.Exchange(ref _scheduled, 1) == 0)
        {
            try
            {
                ui.PostBackground(Drain);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Nothing is queued, so nothing will clear the flag: the next snapshot has to be able to try again.
                // The snapshot stays pending. The error is reported, not thrown, because the caller is the quote
                // loop of a data source, which would end for good on an exception.
                Interlocked.Exchange(ref _scheduled, 0);
                onError?.Invoke(ex);
            }
        }
    }

    /// <summary>
    /// Applies every pending snapshot on the UI thread.
    /// </summary>
    private void Drain()
    {
        // Cleared before reading, so a snapshot that arrives meanwhile queues the next hand-over and is not lost.
        Interlocked.Exchange(ref _scheduled, 0);
        try
        {
            foreach (var id in _pending.Keys)
            {
                if (_pending.TryRemove(id, out var snapshot))
                {
                    apply(snapshot);
                }
            }
        }
        finally
        {
            // Reached with snapshots left only when applying one failed; they must not wait for the next price.
            if (!_pending.IsEmpty)
            {
                Schedule();
            }
        }
    }
}
