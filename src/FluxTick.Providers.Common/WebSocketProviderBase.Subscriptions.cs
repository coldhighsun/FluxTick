using System.Net.WebSockets;
using System.Text;
using FluxTick.Abstractions;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Common;

public abstract partial class WebSocketProviderBase
{
    /// <summary>
    /// Makes "connection is up and subscribed" and "subscriptions changed" atomic with respect to each other, so
    /// an instrument is subscribed on the socket exactly once.
    /// </summary>
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    /// <summary>
    /// The instruments subscribed on the current socket; guarded by <see cref="_stateLock"/>.
    /// </summary>
    private Dictionary<AssetId, Asset>? _sent;

    /// <summary>
    /// Builds the messages that subscribe to the given instruments. Return several messages when the server limits
    /// how many instruments one request may carry.
    /// </summary>
    /// <param name="assets">
    /// The instruments to subscribe; never empty. Not called when <see cref="SubscribesByUri"/> is set.
    /// </param>
    /// <returns>
    /// The text frames to send, in order.
    /// </returns>
    protected abstract IEnumerable<string> BuildSubscribeMessages(IReadOnlyCollection<Asset> assets);

    /// <summary>
    /// Builds the messages that unsubscribe from the given instruments.
    /// </summary>
    /// <param name="assets">
    /// The instruments to unsubscribe; never empty.
    /// </param>
    /// <returns>
    /// The text frames to send, in order.
    /// </returns>
    protected abstract IEnumerable<string> BuildUnsubscribeMessages(IReadOnlyCollection<Asset> assets);

    /// <inheritdoc />
    protected override async Task OnSubscriptionsChangedAsync(
        IReadOnlyCollection<Asset> current,
        IReadOnlyCollection<Asset> added,
        IReadOnlyCollection<Asset> removed,
        CancellationToken cancellationToken)
    {
        if (current.Count == 0)
        {
            await _loop.StopAsync().ConfigureAwait(false);
            SetStreamStatus(ProviderConnectionState.Idle);
            return;
        }

        if (!_loop.IsRunning)
        {
            _loop.Start(RunAsync);
            return;
        }

        // The caller may stop waiting for the lock, which a connect that is sending its subscriptions can hold for up to
        // SendTimeout. Once the lock is taken the token is no longer used: the work is short because every send is
        // bounded by SendTimeout, and abandoning it halfway would leave the socket out of step with the subscriptions.
        var shutdown = ShutdownToken;
        try
        {
            await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The base class has already recorded the new subscriptions, which this class never got to send. The
            // connection stays up; a detached task takes the lock when it frees up and sends the difference, so the
            // call fails but the subscriptions are not lost and the connection is not disturbed.
            _ = Task.Run(() => ReconcileLaterAsync(shutdown), CancellationToken.None);
            throw;
        }

        try
        {
            await ReconcileAsync(shutdown).ConfigureAwait(false);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Takes the state lock once it is free and brings the socket in line with the current subscriptions. It runs
    /// detached for a caller that stopped waiting, so it must never throw.
    /// </summary>
    /// <param name="shutdown">
    /// A token that ends the wait when the provider shuts down.
    /// </param>
    /// <returns>
    /// A task that completes when the socket was reconciled or the provider shut down.
    /// </returns>
    private async Task ReconcileLaterAsync(CancellationToken shutdown)
    {
        try
        {
            await _stateLock.WaitAsync(shutdown).ConfigureAwait(false);
            try
            {
                await ReconcileAsync(shutdown).ConfigureAwait(false);
            }
            finally
            {
                _stateLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // The provider shut down.
        }
        catch (Exception ex)
        {
            // Nobody awaits this task, so a failure must be handled here: drop the socket so that the reconnect
            // subscribes to the full current set instead of the change being lost.
            Logger.LogWarning(ex, "{Provider} could not apply a subscription change", Descriptor.DisplayName);
            _socket?.Abort();
        }
    }

    /// <summary>
    /// Sends the difference between the current subscriptions and what the open socket was told. The caller holds
    /// <see cref="_stateLock"/>. Because it compares against the sent set instead of trusting a list of changes, it is
    /// safe to run more than once and it picks up changes whose own call gave up waiting.
    /// </summary>
    /// <param name="shutdown">
    /// A token that ends the sends when the provider shuts down.
    /// </param>
    /// <returns>
    /// A task that completes when the difference was sent or the socket was dropped for a reconnect.
    /// </returns>
    private async Task ReconcileAsync(CancellationToken shutdown)
    {
        // Without an open socket the next connect subscribes to the full current set.
        var sent = _sent;
        if (_socket is not { State: WebSocketState.Open } socket || sent is null)
        {
            return;
        }

        var current = CurrentSubscriptions;
        var currentIds = current.Select(a => a.Id).ToHashSet();
        var toAdd = current.Where(a => !sent.ContainsKey(a.Id)).ToArray();
        var toRemove = sent.Values.Where(a => !currentIds.Contains(a.Id)).ToArray();
        if (toAdd.Length == 0 && toRemove.Length == 0)
        {
            return;
        }

        if (SubscribesByUri)
        {
            RequestReconnect(socket);
            return;
        }

        // The sent set is updated before the messages go out, so a change that fails to arrive is repaired by the
        // reconnect below instead of being sent twice.
        foreach (var asset in toAdd)
        {
            sent[asset.Id] = asset;
        }

        foreach (var asset in toRemove)
        {
            sent.Remove(asset.Id);
        }

        var delivered = true;
        try
        {
            if (toAdd.Length > 0)
            {
                delivered = await TrySendAsync(socket, BuildSubscribeMessages(toAdd), shutdown).ConfigureAwait(false);
            }

            if (delivered && toRemove.Length > 0)
            {
                delivered = await TrySendAsync(socket, BuildUnsubscribeMessages(toRemove), shutdown).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (!shutdown.IsCancellationRequested)
        {
            // The derived class failed to build the messages. The sent set already counts them, so keeping the socket
            // would leave the instrument without quotes for good; the reconnect subscribes again from the full set.
            Logger.LogWarning(ex, "{Provider} could not build its subscription messages", Descriptor.DisplayName);
            delivered = false;
        }

        if (!delivered)
        {
            // A failed send must not leave the socket open with a subscription that never reached the server. Dropping
            // the socket makes the loop reconnect and subscribe to the full current set.
            socket.Abort();
        }
    }

    /// <summary>
    /// Takes a socket that just connected as the one the instruments are subscribed on, reports the connection and starts
    /// the clock of its quiet time. The caller holds the state lock.
    /// </summary>
    /// <param name="socket">
    /// The connected socket.
    /// </param>
    /// <param name="subscribed">
    /// The instruments the socket is subscribed to from the start.
    /// </param>
    private void MarkConnected(ClientWebSocket socket, IReadOnlyCollection<Asset> subscribed)
    {
        _sent = subscribed.ToDictionary(a => a.Id);
        _socket = socket;
        SetStreamStatus(ProviderConnectionState.Connected);
        Volatile.Write(ref _lastActivity, TimeProvider.GetTimestamp());
    }

    /// <summary>
    /// Publishes the connected socket and subscribes to the instruments at that moment, atomically with respect to
    /// subscription changes.
    /// </summary>
    /// <param name="socket">
    /// The connected socket.
    /// </param>
    /// <param name="connectAssets">
    /// The instruments the connection address was built from; only used when <see cref="SubscribesByUri"/> is set.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the subscription.
    /// </param>
    /// <returns>
    /// A task that completes when the subscribe messages were sent.
    /// </returns>
    private async Task SubscribeAsync(ClientWebSocket socket, IReadOnlyCollection<Asset> connectAssets, CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (SubscribesByUri)
            {
                await SubscribeByUriAsync(socket, connectAssets, cancellationToken).ConfigureAwait(false);
                return;
            }

            var assets = CurrentSubscriptions;
            MarkConnected(socket, assets);
            if (assets.Count > 0)
            {
                foreach (var message in BuildSubscribeMessages(assets))
                {
                    await SendAsync(socket, message, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }
}
