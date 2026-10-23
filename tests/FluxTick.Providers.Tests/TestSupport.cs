using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Waiting helpers shared by the provider tests.
/// </summary>
internal static class TestWait
{
    /// <summary>
    /// How long to wait for something that should happen promptly.
    /// </summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Polls until a condition holds, failing on timeout.
    /// </summary>
    /// <param name="condition">
    /// The condition to wait for.
    /// </param>
    /// <returns>
    /// A task that completes when the condition holds.
    /// </returns>
    public static async Task EventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition did not become true in time.");
            }

            await Task.Delay(20);
        }
    }

    /// <summary>
    /// Finds a port nothing listens on.
    /// </summary>
    /// <returns>
    /// The port.
    /// </returns>
    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

/// <summary>
/// An HTTP handler that answers from a delegate and records every request.
/// </summary>
/// <param name="respond">
/// Produces the response for a request.
/// </param>
internal sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    /// <summary>
    /// Gets the requests received so far.
    /// </summary>
    public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

    /// <summary>
    /// Creates a successful JSON response.
    /// </summary>
    /// <param name="json">
    /// The body.
    /// </param>
    /// <returns>
    /// The response.
    /// </returns>
    public static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        return Task.FromResult(respond(request));
    }
}

/// <summary>
/// A WebSocket server on the loopback interface that records what clients send.
/// </summary>
internal sealed class LoopbackServer : IAsyncDisposable
{
    /// <summary>
    /// The listener.
    /// </summary>
    private readonly HttpListener _listener = new();

    /// <summary>
    /// The accept loop.
    /// </summary>
    private readonly Task _accepting;

    /// <summary>
    /// Everything clients sent, in arrival order.
    /// </summary>
    private readonly ConcurrentQueue<string> _received = new();

    /// <summary>
    /// The most recently accepted connection.
    /// </summary>
    private WebSocket? _current;

    /// <summary>
    /// The connections that are accepted but never read.
    /// </summary>
    private readonly ConcurrentBag<WebSocket> _stalled = new();

    /// <summary>
    /// The number of accepted connections.
    /// </summary>
    private int _connections;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoopbackServer"/> class and starts listening.
    /// </summary>
    /// <param name="port">
    /// The port to listen on.
    /// </param>
    private LoopbackServer(int port)
    {
        Uri = new Uri($"ws://127.0.0.1:{port}/");
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _accepting = Task.Run(AcceptLoopAsync);
    }

    /// <summary>
    /// Gets the address clients connect to.
    /// </summary>
    public Uri Uri { get; }

    /// <summary>
    /// Gets the number of connections accepted so far.
    /// </summary>
    public int ConnectionCount => Volatile.Read(ref _connections);

    /// <summary>
    /// Gets or sets a value indicating whether the server closes every connection right after accepting it, like a
    /// server that takes the handshake and then hangs up.
    /// </summary>
    public bool CloseOnConnect { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the server accepts connections but never reads from them, like a peer
    /// whose receive buffers are full, so that a large enough send by the client blocks.
    /// </summary>
    public bool StallOnConnect { get; set; }

    /// <summary>
    /// Starts a server on a free port.
    /// </summary>
    /// <returns>
    /// The running server.
    /// </returns>
    public static LoopbackServer Start() => new(TestWait.FreePort());

    /// <summary>
    /// Counts how often a message was received.
    /// </summary>
    /// <param name="message">
    /// The message text.
    /// </param>
    /// <returns>
    /// The number of times it arrived.
    /// </returns>
    public int Count(string message) => _received.Count(m => m == message);

    /// <summary>
    /// Sends a text frame to the current connection.
    /// </summary>
    /// <param name="text">
    /// The text.
    /// </param>
    /// <returns>
    /// A task that completes when the frame was sent.
    /// </returns>
    public async Task SendAsync(string text)
    {
        await TestWait.EventuallyAsync(() => _current is { State: WebSocketState.Open });
        await _current!.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    /// <summary>
    /// Ends the current connection from the server side. The server first sends a close frame, because aborting the
    /// socket alone does not reliably close the underlying connection of the managed <see cref="HttpListener"/> on
    /// Linux and macOS, which left the client waiting for a keep-alive timeout.
    /// </summary>
    public void DropCurrentConnection()
    {
        var socket = _current;
        if (socket is null)
        {
            return;
        }

        try
        {
            socket.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, "drop", CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            // The connection was already gone.
        }

        socket.Abort();
    }

    /// <summary>
    /// Waits until a message was received at least once.
    /// </summary>
    /// <param name="message">
    /// The expected text.
    /// </param>
    /// <returns>
    /// A task that completes when it arrived.
    /// </returns>
    public Task WaitForMessageAsync(string message) => WaitForMessageCountAsync(message, 1);

    /// <summary>
    /// Waits until a message was received a number of times.
    /// </summary>
    /// <param name="message">
    /// The expected text.
    /// </param>
    /// <param name="count">
    /// The number of times.
    /// </param>
    /// <returns>
    /// A task that completes when it arrived often enough.
    /// </returns>
    public Task WaitForMessageCountAsync(string message, int count) =>
        TestWait.EventuallyAsync(() => Count(message) >= count);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _listener.Abort();
        try
        {
            await _accepting;
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
        {
            // The listener was stopped.
        }

        foreach (var socket in _stalled)
        {
            socket.Dispose();
        }
    }

    /// <summary>
    /// Accepts connections until the listener stops.
    /// </summary>
    /// <returns>
    /// A task that completes when the listener stops.
    /// </returns>
    private async Task AcceptLoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                continue;
            }

            var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
            _current = socket;
            Interlocked.Increment(ref _connections);
            if (StallOnConnect)
            {
                _stalled.Add(socket);
                continue;
            }

            if (CloseOnConnect)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                _ = Task.Run(() => DisposeWhenClientLeavesAsync(socket));
                continue;
            }

            _ = Task.Run(() => ReadLoopAsync(socket));
        }
    }

    /// <summary>
    /// Waits for the client of a connection that the server already closed to go away, then releases the socket.
    /// </summary>
    /// <param name="socket">
    /// The connection.
    /// </param>
    /// <returns>
    /// A task that completes when the socket was released.
    /// </returns>
    private static async Task DisposeWhenClientLeavesAsync(WebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[256];
        try
        {
            while (socket.State is WebSocketState.CloseSent or WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, timeout.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or OperationCanceledException)
        {
            // The client went away or did not answer in time.
        }
        finally
        {
            socket.Dispose();
        }
    }

    /// <summary>
    /// Records the text frames of one connection until it ends.
    /// </summary>
    /// <param name="socket">
    /// The connection.
    /// </param>
    /// <returns>
    /// A task that completes when the connection ends.
    /// </returns>
    private async Task ReadLoopAsync(WebSocket socket)
    {
        var buffer = new byte[4096];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                _received.Enqueue(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or HttpListenerException)
        {
            // The client went away.
        }
    }
}
