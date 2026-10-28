using System.Net.WebSockets;
using System.Text;
using FluxTick.Abstractions;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Common;

/// <summary>
/// Base class for sources that stream quotes over a WebSocket. It owns connecting, resubscribing, an optional
/// keep-alive message and reconnecting with exponential backoff; derived classes only describe the wire protocol.
/// </summary>
public abstract partial class WebSocketProviderBase : LoggingProviderBase
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
    /// The connection loop, which runs while instruments are subscribed.
    /// </summary>
    private readonly BackgroundLoop _loop;

    /// <summary>
    /// The currently open socket, or <see langword="null"/>.
    /// </summary>
    private ClientWebSocket? _socket;

    /// <summary>
    /// The state of the stream itself, before <see cref="AdjustStatus"/> turns it into the status of the provider.
    /// </summary>
    private ProviderStatus _streamStatus = new(ProviderConnectionState.Idle);

    /// <summary>
    /// Makes working out and publishing the status one step, as the connection loop and a derived class can both ask.
    /// </summary>
    private readonly object _statusLock = new();

    /// <summary>
    /// The timestamp of the last sign of life of the connection: when it was established or a message with a price arrived.
    /// </summary>
    private long _lastActivity;

    /// <summary>
    /// The number of messages that published a quote, over all connections.
    /// </summary>
    private long _messages;

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
        : base(descriptor, timeProvider, logger)
    {
        _webSocketUri = webSocketUri;
        _loop = new BackgroundLoop(ShutdownToken);
        _lastActivity = timeProvider.GetTimestamp();
    }

    /// <summary>
    /// Gets how long the connection has been quiet: the time since it was established or the last message with a price
    /// arrived, so acknowledgements and heartbeats do not count. A
    /// stream that stays quiet is a market that does not trade, or a server that stopped talking; the two cannot be told
    /// apart from here.
    /// </summary>
    protected TimeSpan IdleFor => TimeProvider.GetElapsedTime(Volatile.Read(ref _lastActivity));

    /// <summary>
    /// Gets how many messages that published a quote the stream has delivered so far, over all connections. Unlike
    /// <see cref="IdleFor"/>, which also starts over when a connection is made and moves for acknowledgements and
    /// heartbeats, it only grows when the stream delivers prices.
    /// </summary>
    protected long MessageCount => Interlocked.Read(ref _messages);

    /// <summary>
    /// Called when the state of the stream changed, before the status of the provider is worked out from it, with the
    /// status lock held. A derived class that keeps state for <see cref="AdjustStatus"/> updates it here. The default does
    /// nothing.
    /// </summary>
    /// <param name="stream">
    /// The new state of the stream.
    /// </param>
    protected virtual void OnStreamStatusChanged(ProviderStatus stream)
    {
    }

    /// <summary>
    /// Gets a value indicating whether the stream itself is connected, whatever status the provider reports.
    /// </summary>
    protected bool StreamConnected => Volatile.Read(ref _streamStatus).State == ProviderConnectionState.Connected;

    /// <summary>
    /// Turns the state of the stream into the status of the provider. The default reports the stream as it is; a source
    /// that has another way to get prices while the stream is down overrides it, so that the prices that keep arriving do
    /// not count as those of a source that is reconnecting. Call <see cref="RefreshStatus"/> when what it depends on
    /// changes.
    /// </summary>
    /// <param name="stream">
    /// The state of the stream.
    /// </param>
    /// <returns>
    /// The status to report.
    /// </returns>
    protected virtual ProviderStatus AdjustStatus(ProviderStatus stream) => stream;

    /// <summary>
    /// Works out the status again and reports it if it changed.
    /// </summary>
    protected void RefreshStatus()
    {
        lock (_statusLock)
        {
            var status = AdjustStatus(Volatile.Read(ref _streamStatus));
            SetStatus(status.State, status.Message);
        }
    }

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
    /// Gets how long to wait for the connection and handshake before the attempt counts as failed.
    /// </summary>
    protected virtual TimeSpan ConnectTimeout => TimeSpan.FromSeconds(15);

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
    /// Sets the options of a new socket before it connects, for example request headers the server insists on. The
    /// default does nothing.
    /// </summary>
    /// <param name="options">
    /// The options of the socket that is about to connect.
    /// </param>
    protected virtual void ConfigureSocket(ClientWebSocketOptions options)
    {
    }

    /// <summary>
    /// Handles one received text message. Call <c>Publish</c> for every quote it carries and ignore everything else.
    /// Publish on the calling thread before returning: a message counts as the stream delivering prices only if it
    /// published a quote that way. An exception thrown here is logged and the message is dropped; the connection stays up.
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
    protected override async ValueTask DisposeCoreAsync()
    {
        await _loop.StopAsync().ConfigureAwait(false);
        await DisposeResourcesAsync().ConfigureAwait(false);
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
        SetStreamStatus(ProviderConnectionState.Reconnecting, "Connecting");
        while (!cancellationToken.IsCancellationRequested)
        {
            string? failure = null;
            long? connectedAt = null;

            // A request to connect again names the socket it was made for, so one that arrives after that connection ended
            // cannot turn the failure of the next one into a silent retry without status or backoff.
            ClientWebSocket? attempt = null;
            try
            {
                using var socket = new ClientWebSocket();
                attempt = socket;
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(30);
                ConfigureSocket(socket.Options);
                var connectAssets = CurrentSubscriptions;
                var connectUri = BuildConnectionUri(connectAssets);

                // A middlebox that accepts the connection but never completes the handshake must not hang the loop.
                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    connectCts.CancelAfter(ConnectTimeout);
                    try
                    {
                        await socket.ConnectAsync(connectUri, connectCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // Only the timeout cancelled it; the status shows that instead of "The operation was canceled".
                        throw new ConnectTimeoutException($"Connecting timed out after {ConnectTimeout.TotalSeconds:0} seconds");
                    }
                }

                connectedAt = TimeProvider.GetTimestamp();
                await SubscribeAsync(socket, connectAssets, cancellationToken).ConfigureAwait(false);
                await ReceiveWithHeartbeatAsync(socket, cancellationToken).ConfigureAwait(false);
                failure = "Connection closed by server";
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception) when (ReconnectRequestedFor(attempt))
            {
                // The socket was dropped on purpose to apply a subscription change; the check below connects again.
            }
            catch (Exception ex)
            {
                failure = ex.Message;
                if (ex is ConnectTimeoutException)
                {
                    // The stack of an exception made for the status text says nothing, and the loop retries often.
                    Logger.LogWarning("{Provider} connection failed: {Reason}", Descriptor.DisplayName, ex.Message);
                }
                else
                {
                    Logger.LogWarning(ex, "{Provider} connection failed", Descriptor.DisplayName);
                }
            }
            finally
            {
                _socket = null;
                _sent = null;
            }

            if (await ConsumeReconnectRequestAsync(attempt, cancellationToken).ConfigureAwait(false))
            {
                // A subscription change, not a failure: the status stays as it was and the backoff is left alone.
                continue;
            }

            // A server that accepts the connection and then hangs up, even after answering the subscription, must not
            // reset the backoff, or it would be contacted once a second forever; only a connection that stayed up for
            // a while counts as healthy.
            if (connectedAt is { } started && TimeProvider.GetElapsedTime(started) >= HealthyUptime)
            {
                backoff = InitialBackoff;
            }

            SetStreamStatus(ProviderConnectionState.Reconnecting, failure);
            try
            {
                await Task.Delay(backoff + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)), TimeProvider, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            backoff = Backoff.Double(backoff, MaxBackoff);
        }
    }

    /// <summary>
    /// The timeout of connecting, made for the status text. It is logged by its message alone, since its stack says
    /// nothing; any other timeout keeps its stack in the log.
    /// </summary>
    /// <param name="message">
    /// The message shown as the reason in the status.
    /// </param>
    private sealed class ConnectTimeoutException(string message) : TimeoutException(message);

    /// <summary>
    /// Drops the open connection on purpose so that the loop connects again at once, without a failure status or a
    /// backoff, as it does for a change of the subscriptions. For a source that finds out by itself that its stream no
    /// longer delivers. Does nothing while there is no open connection.
    /// </summary>
    protected void ReconnectStream()
    {
        if (Volatile.Read(ref _socket) is { } socket)
        {
            RequestReconnect(socket);
        }
    }

    /// <summary>
    /// Records the state of the stream and reports the status that follows from it.
    /// </summary>
    /// <param name="state">
    /// The state of the stream.
    /// </param>
    /// <param name="message">
    /// Optional detail, such as why the stream is reconnecting.
    /// </param>
    private void SetStreamStatus(ProviderConnectionState state, string? message = null)
    {
        lock (_statusLock)
        {
            var stream = new ProviderStatus(state, message);
            Volatile.Write(ref _streamStatus, stream);
            OnStreamStatusChanged(stream);
            RefreshStatus();
        }
    }
}
