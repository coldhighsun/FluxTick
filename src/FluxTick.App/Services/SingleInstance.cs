using System.Diagnostics;
using System.IO.Pipes;

namespace FluxTick.App.Services;

/// <summary>
/// Lets only one copy of the app run per user. A second copy asks the first to show its window and ends.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>
    /// How long a second copy waits for the first one to take its request.
    /// </summary>
    private static readonly TimeSpan NotifyTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The name of the pipe the first copy listens on.
    /// </summary>
    private readonly string _name;

    /// <summary>
    /// The mutex that is owned for as long as this copy runs.
    /// </summary>
    private readonly Mutex _mutex;

    /// <summary>
    /// Ends the listener.
    /// </summary>
    private readonly CancellationTokenSource _stop = new();

    /// <summary>
    /// The listener task, or <see langword="null"/> before listening started.
    /// </summary>
    private Task? _listener;

    /// <summary>
    /// Whether the guard was disposed already.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleInstance"/> class.
    /// </summary>
    /// <param name="name">
    /// The name that is shared by the copies that exclude each other.
    /// </param>
    /// <param name="mutex">
    /// The owned mutex.
    /// </param>
    private SingleInstance(string name, Mutex mutex)
    {
        _name = name;
        _mutex = mutex;
    }

    /// <summary>
    /// Raised, on a background thread, when another copy asked this one to show its window.
    /// </summary>
    public event EventHandler? Activated;

    /// <summary>
    /// Gets the name that excludes other copies of the app of the same user. The demo mode runs on settings of its own,
    /// so it excludes only other demo copies.
    /// </summary>
    /// <param name="demo">
    /// Whether the app runs in offline demo mode.
    /// </param>
    /// <returns>
    /// The name.
    /// </returns>
    public static string NameFor(bool demo) => $"FluxTick-{HashOf(Environment.UserName)}{(demo ? "-demo" : string.Empty)}";

    /// <summary>
    /// Shortens a user name to characters that every platform accepts in a mutex or pipe name; a raw name may hold spaces,
    /// path separators or be too long.
    /// </summary>
    /// <param name="userName">
    /// The user name.
    /// </param>
    /// <returns>
    /// A short lowercase hexadecimal string that is the same for the same user name.
    /// </returns>
    private static string HashOf(string userName) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(userName)))[..12];

    /// <summary>
    /// Tries to become the only running copy.
    /// </summary>
    /// <param name="name">
    /// The name that is shared by the copies that exclude each other.
    /// </param>
    /// <returns>
    /// The guard to dispose when the app ends, or <see langword="null"/> when another copy already runs.
    /// </returns>
    public static SingleInstance? TryAcquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: true, $"Local\\{name}", out var created);
        if (created)
        {
            return new SingleInstance(name, mutex);
        }

        mutex.Dispose();
        return null;
    }

    /// <summary>
    /// Asks the copy that runs to show its window.
    /// </summary>
    /// <param name="name">
    /// The name that is shared by the copies that exclude each other.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the running copy took the request.
    /// </returns>
    public static bool NotifyRunning(string name)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < NotifyTimeout)
        {
            var remaining = NotifyTimeout - clock.Elapsed;
            try
            {
                using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut);
                client.Connect((int)remaining.TotalMilliseconds);

                // Wait for the running copy to say it took the request: a client that leaves right after connecting can
                // make the server's accept fail, and the request would be lost.
                using var timeout = new CancellationTokenSource(NotifyTimeout - clock.Elapsed);
                if (client.ReadAsync(new byte[1], timeout.Token).AsTask().GetAwaiter().GetResult() == 1)
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or UnauthorizedAccessException or ArgumentException)
            {
                // Nobody answers in time, or the pipe cannot be used at all: asking again would not help.
                return false;
            }
            catch (IOException)
            {
                // The connection broke; see below.
            }

            // The connection ended without an answer, as happens when it was made to a copy of the listener that was
            // just finishing the previous request. The next listener is ready in a moment, so ask again.
            Thread.Sleep(50);
        }

        return false;
    }

    /// <summary>
    /// Starts to accept requests of other copies.
    /// </summary>
    public void StartListening() => _listener ??= Task.Run(ListenAsync);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stop.Cancel();
        try
        {
            _listener?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // The listener ended with an error that was handled there.
        }

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Released by a thread other than the one that took it; the process end releases it too.
        }

        _mutex.Dispose();
        _stop.Dispose();
    }

    /// <summary>
    /// Tells the client its request was taken, and waits for the client to leave so that the answer is not cut off.
    /// </summary>
    /// <param name="server">
    /// The connected pipe.
    /// </param>
    /// <returns>
    /// A task that completes when the client left or the wait timed out.
    /// </returns>
    private async Task AcknowledgeAsync(NamedPipeServerStream server)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            timeout.CancelAfter(NotifyTimeout);
            await server.WriteAsync(new byte[] { 1 }, timeout.Token);
            await server.FlushAsync(timeout.Token);
            await server.ReadAtLeastAsync(new byte[1], 1, throwOnEndOfStream: false, timeout.Token);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            // The client left early or never read the answer; the request was raised already.
        }
    }

    /// <summary>
    /// Accepts one connection after the other and raises <see cref="Activated"/> for each.
    /// </summary>
    /// <returns>
    /// A task that ends when the guard is disposed.
    /// </returns>
    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token);
                Activated?.Invoke(this, EventArgs.Empty);
                await AcknowledgeAsync(server);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ArgumentException)
            {
                // The name cannot be used for a pipe on this platform (for instance, it makes the socket path too long);
                // a second copy then cannot reach this one, but this one keeps running.
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The other copy left before it was served, or the pipe could not be created; try again shortly.
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), _stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
