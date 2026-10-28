using System.Net.WebSockets;
using System.Text;
using FluxTick.Abstractions;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Common;

public abstract partial class WebSocketProviderBase
{
    /// <summary>
    /// Serializes writes to the socket.
    /// </summary>
    private readonly SemaphoreSlim _sendLock = new(1, 1);

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
            var publishedBefore = PublishedByThisThread;
            try
            {
                HandleMessage(text);
            }
            catch (Exception ex)
            {
                // One message the parser cannot digest must not cost the connection.
                Logger.LogDebug(ex, "Ignoring malformed {Provider} message", Descriptor.DisplayName);
            }

            if (PublishedByThisThread != publishedBefore)
            {
                Volatile.Write(ref _lastActivity, TimeProvider.GetTimestamp());
                Interlocked.Increment(ref _messages);
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
