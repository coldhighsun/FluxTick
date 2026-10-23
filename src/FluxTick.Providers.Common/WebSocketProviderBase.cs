using System.Net.WebSockets;
using System.Text;
using FluxTick.Abstractions;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Common;

/// <summary>
/// Base class for sources that stream quotes over a WebSocket. It owns connecting, resubscribing, an optional
/// keep-alive message and reconnecting with exponential backoff; derived classes only describe the wire protocol.
/// </summary>
public abstract class WebSocketProviderBase : MarketDataProviderBase
{
    /// <summary>
    /// The longest wait between reconnect attempts.
    /// </summary>
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The WebSocket endpoint.
    /// </summary>
    private readonly Uri _webSocketUri;

    /// <summary>
    /// Serializes writes to the socket.
    /// </summary>
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    /// <summary>
    /// Makes "connection is up and subscribed" and "subscriptions changed" atomic with respect to each other, so
    /// an instrument is subscribed on the socket exactly once.
    /// </summary>
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    /// <summary>
    /// The connection loop, or <see langword="null"/> while idle.
    /// </summary>
    private Task? _loop;

    /// <summary>
    /// Stops the connection loop without disposing the provider.
    /// </summary>
    private CancellationTokenSource? _loopCts;

    /// <summary>
    /// The currently open socket, or <see langword="null"/>.
    /// </summary>
    private ClientWebSocket? _socket;

    /// <summary>
    /// The instruments subscribed on the current socket; guarded by <see cref="_stateLock"/>.
    /// </summary>
    private Dictionary<AssetId, Asset>? _sent;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebSocketProviderBase"/> class.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    /// <param name="webSocketUri">
    /// The WebSocket endpoint.
    /// </param>
    /// <param name="timeProvider">
    /// The clock.
    /// </param>
    /// <param name="logger">
    /// The logger.
    /// </param>
    protected WebSocketProviderBase(ProviderDescriptor descriptor, Uri webSocketUri, TimeProvider timeProvider, ILogger logger)
        : base(descriptor, timeProvider)
    {
        _webSocketUri = webSocketUri;
        Logger = logger;
    }

    /// <summary>
    /// Gets the logger.
    /// </summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// Gets the wait before the first reconnect attempt; it doubles after every failure up to 30 seconds.
    /// </summary>
    protected virtual TimeSpan InitialBackoff => TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets how long a connection must stay up before a later disconnect restarts the backoff at
    /// <see cref="InitialBackoff"/>. A connection that ends sooner, whatever it received, keeps doubling the wait.
    /// </summary>
    protected virtual TimeSpan HealthyUptime => TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets how long one outgoing message may take, measured on the system clock. A send that exceeds it counts as a
    /// failed send: the connection is dropped and the reconnect subscribes again, so a stalled socket cannot block
    /// subscription changes for long.
    /// </summary>
    protected virtual TimeSpan SendTimeout => TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets the size in bytes above which a single incoming message is treated as a protocol failure.
    /// </summary>
    protected virtual int MaxMessageBytes => 4 * 1024 * 1024;

    /// <summary>
    /// Gets how often <see cref="HeartbeatMessage"/> is sent, or <see langword="null"/> when the server needs no
    /// application-level keep-alive.
    /// </summary>
    protected virtual TimeSpan? HeartbeatInterval => null;

    /// <summary>
    /// Gets the text frame sent every <see cref="HeartbeatInterval"/>.
    /// </summary>
    protected virtual string? HeartbeatMessage => null;

    /// <summary>
    /// Builds the messages that subscribe to the given instruments. Return several messages when the server limits
    /// how many instruments one request may carry.
    /// </summary>
    /// <param name="assets">
    /// The instruments to subscribe; never empty.
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

    /// <summary>
    /// Handles one received text message. Call <c>Publish</c> for every quote it carries and ignore everything else.
    /// An exception thrown here is logged and the message is dropped; the connection stays up.
    /// </summary>
    /// <param name="text">
    /// The message text.
    /// </param>
    protected abstract void HandleMessage(string text);

    /// <summary>
    /// Called once on disposal after the connection loop has ended. Release resources owned by the derived class here.
    /// </summary>
    /// <returns>
    /// A task that completes when the resources are released.
    /// </returns>
    protected virtual ValueTask DisposeResourcesAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    protected override async Task OnSubscriptionsChangedAsync(
        IReadOnlyCollection<Asset> current,
        IReadOnlyCollection<Asset> added,
        IReadOnlyCollection<Asset> removed,
        CancellationToken cancellationToken)
    {
        if (current.Count == 0)
        {
            await StopLoopAsync().ConfigureAwait(false);
            SetStatus(ProviderConnectionState.Idle);
            return;
        }

        if (_loop is null)
        {
            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ShutdownToken);
            var token = _loopCts.Token;
            _loop = Task.Run(() => RunAsync(token), CancellationToken.None);
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

    /// <inheritdoc />
    protected override async ValueTask DisposeCoreAsync()
    {
        await StopLoopAsync().ConfigureAwait(false);
        await DisposeResourcesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the connection loop and waits for it to finish.
    /// </summary>
    /// <returns>
    /// A task that completes when the loop has ended.
    /// </returns>
    private async Task StopLoopAsync()
    {
        var loop = _loop;
        var cts = _loopCts;
        _loop = null;
        _loopCts = null;
        if (loop is null || cts is null)
        {
            return;
        }

        await cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        cts.Dispose();
    }

    /// <summary>
    /// Connects, subscribes and reads until the connection drops, then retries with exponential backoff. Any failure
    /// other than shutdown ends only the current connection, never the loop.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token that stops the loop.
    /// </param>
    /// <returns>
    /// A task that completes when the loop is cancelled.
    /// </returns>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var backoff = InitialBackoff;
        SetStatus(ProviderConnectionState.Reconnecting, "Connecting");
        while (!cancellationToken.IsCancellationRequested)
        {
            string? failure = null;
            long? connectedAt = null;
            try
            {
                using var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(30);
                await socket.ConnectAsync(_webSocketUri, cancellationToken).ConfigureAwait(false);
                connectedAt = TimeProvider.GetTimestamp();
                await SubscribeAsync(socket, cancellationToken).ConfigureAwait(false);
                await ReceiveWithHeartbeatAsync(socket, cancellationToken).ConfigureAwait(false);
                failure = "Connection closed by server";
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                failure = ex.Message;
                Logger.LogWarning(ex, "{Provider} connection failed", Descriptor.DisplayName);
            }
            finally
            {
                _socket = null;
                _sent = null;
            }

            // A server that accepts the connection and then hangs up, even after answering the subscription, must not
            // reset the backoff, or it would be contacted once a second forever; only a connection that stayed up for
            // a while counts as healthy.
            if (connectedAt is { } started && TimeProvider.GetElapsedTime(started) >= HealthyUptime)
            {
                backoff = InitialBackoff;
            }

            SetStatus(ProviderConnectionState.Reconnecting, failure);
            try
            {
                await Task.Delay(backoff + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)), TimeProvider, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, MaxBackoff.TotalSeconds));
        }
    }

    /// <summary>
    /// Publishes the connected socket and subscribes to the instruments at that moment, atomically with respect to
    /// subscription changes.
    /// </summary>
    /// <param name="socket">
    /// The connected socket.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the subscription.
    /// </param>
    /// <returns>
    /// A task that completes when the subscribe messages were sent.
    /// </returns>
    private async Task SubscribeAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var assets = CurrentSubscriptions;
            _sent = assets.ToDictionary(a => a.Id);
            _socket = socket;
            SetStatus(ProviderConnectionState.Connected);
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

    /// <summary>
    /// Reads messages while sending the keep-alive message on its schedule, until the connection ends.
    /// </summary>
    /// <param name="socket">
    /// The open socket.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that stops reading.
    /// </param>
    /// <returns>
    /// A task that completes when the server closes the connection.
    /// </returns>
    private async Task ReceiveWithHeartbeatAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = HeartbeatInterval is { } interval && HeartbeatMessage is { } message
            ? Task.Run(() => HeartbeatAsync(socket, message, interval, connectionCts.Token), CancellationToken.None)
            : Task.CompletedTask;
        try
        {
            await ReceiveAsync(socket, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await connectionCts.CancelAsync().ConfigureAwait(false);
            await heartbeat.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sends the keep-alive message at a fixed interval until cancelled or the socket fails.
    /// </summary>
    /// <param name="socket">
    /// The open socket.
    /// </param>
    /// <param name="message">
    /// The text to send.
    /// </param>
    /// <param name="interval">
    /// The time between messages.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that ends the heartbeat.
    /// </param>
    /// <returns>
    /// A task that completes when the heartbeat ends; it never throws.
    /// </returns>
    private async Task HeartbeatAsync(ClientWebSocket socket, string message, TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(interval, TimeProvider, cancellationToken).ConfigureAwait(false);
                await SendAsync(socket, message, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The connection ended.
        }
        catch (Exception ex)
        {
            // A heartbeat that cannot be sent means the socket is dead or stalled; dropping it makes the receive loop
            // end so the connection loop reconnects.
            Logger.LogDebug(ex, "{Provider} heartbeat failed", Descriptor.DisplayName);
            socket.Abort();
        }
    }

    /// <summary>
    /// Reads messages until the socket closes and hands each one to <see cref="HandleMessage"/>.
    /// </summary>
    /// <param name="socket">
    /// The open socket.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that stops reading.
    /// </param>
    /// <returns>
    /// A task that completes when the server closes the connection, or fails when a message exceeds
    /// <see cref="MaxMessageBytes"/>.
    /// </returns>
    private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            if (message.Length + result.Count > MaxMessageBytes)
            {
                // A message that never ends would otherwise grow the buffer until memory runs out; the loop reconnects.
                throw new InvalidDataException($"A message exceeded {MaxMessageBytes} bytes.");
            }

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);
            try
            {
                HandleMessage(text);
            }
            catch (Exception ex)
            {
                // One message the parser cannot digest must not cost the connection.
                Logger.LogDebug(ex, "Ignoring malformed {Provider} message", Descriptor.DisplayName);
            }
        }
    }

    /// <summary>
    /// Sends control messages on the socket; a failure is not fatal because the caller drops the socket and the
    /// reconnect resubscribes everything.
    /// </summary>
    /// <param name="socket">
    /// The socket.
    /// </param>
    /// <param name="messages">
    /// The messages to send.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that ends the send because the provider shuts down; the exception it causes is not swallowed.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when all messages were sent; <see langword="false"/> when the socket failed or a send
    /// timed out.
    /// </returns>
    private async Task<bool> TrySendAsync(ClientWebSocket socket, IEnumerable<string> messages, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var message in messages)
            {
                await SendAsync(socket, message, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Logger.LogDebug(ex, "Could not send control message; the reconnect will resubscribe");
            return false;
        }
    }

    /// <summary>
    /// Sends a text frame, serialized with other senders.
    /// </summary>
    /// <param name="socket">
    /// The socket to write to.
    /// </param>
    /// <param name="text">
    /// The text to send.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the send.
    /// </param>
    /// <returns>
    /// A task that completes when the frame was sent.
    /// </returns>
    private async Task SendAsync(ClientWebSocket socket, string text, CancellationToken cancellationToken)
    {
        // A send on a stalled connection must not hold the subscription locks until the operating system gives up.
        // The timeout runs on the system clock on purpose: one linked source with CancelAfter costs a single allocation
        // per send, and tests shorten the timeout through SendTimeout instead of advancing a fake clock.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sendTimeout = SendTimeout;
        if (sendTimeout == TimeSpan.Zero)
        {
            // CancelAfter does not cancel before the call returns when the delay is zero; a send that may take no time
            // must fail deterministically. Timeout.InfiniteTimeSpan, by contrast, means no timeout and goes to CancelAfter.
            linked.Cancel();
        }
        else
        {
            linked.CancelAfter(sendTimeout);
        }

        await _sendLock.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, linked.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
