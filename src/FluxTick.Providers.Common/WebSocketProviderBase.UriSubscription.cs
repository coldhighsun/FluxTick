using System.Net.WebSockets;
using System.Text;
using FluxTick.Abstractions;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Common;

public abstract partial class WebSocketProviderBase
{
    /// <summary>
    /// Set to the socket that is dropped on purpose, to apply a subscription change (see <see cref="SubscribesByUri"/>) or
    /// because the stream no longer delivers, so the loop reconnects at once instead of treating the drop as a failure.
    /// </summary>
    private ClientWebSocket? _reconnectFor;

    /// <summary>
    /// Tells whether a socket was dropped on purpose.
    /// </summary>
    /// <param name="socket">
    /// The socket of the connection that just ended, or <see langword="null"/> when none was made.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the drop was requested for exactly that socket.
    /// </returns>
    private bool ReconnectRequestedFor(ClientWebSocket? socket) => socket is not null && ReferenceEquals(Volatile.Read(ref _reconnectFor), socket);

    /// <summary>
    /// Gets a value indicating whether the instruments are part of the connection address instead of being sent as
    /// messages. When <see langword="true"/>, <see cref="BuildConnectionUri"/> must put them in the address, no
    /// subscribe or unsubscribe message is sent, and a change of the subscriptions drops the connection on purpose so
    /// that it is opened again with the new address.
    /// </summary>
    protected virtual bool SubscribesByUri => false;

    /// <summary>
    /// Gets how long the loop waits before it opens the connection again for a subscription change (see
    /// <see cref="SubscribesByUri"/>), so that several changes in a row cost one reconnect.
    /// </summary>
    protected virtual TimeSpan ReconnectDebounce => TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Builds the address to connect to. The default is the endpoint given to the constructor; a source whose address
    /// names the instruments (see <see cref="SubscribesByUri"/>) builds it from them.
    /// </summary>
    /// <param name="assets">
    /// The instruments to subscribe, which may be empty when they were removed while the loop was waiting.
    /// </param>
    /// <returns>
    /// The address.
    /// </returns>
    protected virtual Uri BuildConnectionUri(IReadOnlyCollection<Asset> assets) => _webSocketUri;

    /// <summary>
    /// Drops the socket on purpose and asks the loop to connect again. For a change of the subscriptions the address
    /// carries the instruments, so the only way to change them is to connect again; the loop reads the current
    /// subscriptions when it does. It needs no lock: it only sets a flag and aborts the socket, both safe to repeat and to
    /// race with the end of the connection.
    /// </summary>
    /// <param name="socket">
    /// The open socket.
    /// </param>
    private void RequestReconnect(ClientWebSocket socket)
    {
        Logger.LogInformation("{Provider} reconnecting to apply a subscription change", Descriptor.DisplayName);
        Volatile.Write(ref _reconnectFor, socket);
        try
        {
            socket.Abort();
        }
        catch (ObjectDisposedException)
        {
            // The connection ended on its own at the same moment; the loop is already on its way to a new one.
        }
    }

    /// <summary>
    /// Takes the connected socket as subscribed to the instruments its address was built from. A change since then needs a
    /// new connection, which the reconcile asks for. The caller holds the state lock.
    /// </summary>
    /// <param name="socket">
    /// The connected socket.
    /// </param>
    /// <param name="connectAssets">
    /// The instruments the connection address was built from.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the reconcile.
    /// </param>
    /// <returns>
    /// A task that completes when the socket was taken over.
    /// </returns>
    private async Task SubscribeByUriAsync(ClientWebSocket socket, IReadOnlyCollection<Asset> connectAssets, CancellationToken cancellationToken)
    {
        MarkConnected(socket, connectAssets);
        await ReconcileAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Tells whether the connection that just ended was dropped on purpose to apply a subscription change, and if so
    /// waits a moment so that further changes in a row join the reconnect.
    /// </summary>
    /// <param name="socket">
    /// The socket of the connection that just ended, or <see langword="null"/> when none was made.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that ends the wait.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the drop was requested and the loop should connect again at once.
    /// </returns>
    private async Task<bool> ConsumeReconnectRequestAsync(ClientWebSocket? socket, CancellationToken cancellationToken)
    {
        if (!ReconnectRequestedFor(socket))
        {
            return false;
        }

        Volatile.Write(ref _reconnectFor, null);
        try
        {
            await Task.Delay(ReconnectDebounce, TimeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The loop ends with the cancellation.
        }

        return true;
    }
}
