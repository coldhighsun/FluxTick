namespace FluxTick.Providers.Common;

/// <summary>
/// A background loop that a source starts when the first instrument is subscribed and stops when the last one goes. It
/// owns the cancellation source and the task, so the order of cancelling, waiting and releasing is written once. Calls
/// must be serialized by the caller, which the subscription changes of a provider are.
/// </summary>
/// <param name="shutdown">
/// A token that ends the loop when the provider is disposed.
/// </param>
internal sealed class BackgroundLoop(CancellationToken shutdown)
{
    /// <summary>
    /// Ends the loop without disposing the provider.
    /// </summary>
    private CancellationTokenSource? _cts;

    /// <summary>
    /// The running loop, or <see langword="null"/>.
    /// </summary>
    private Task? _task;

    /// <summary>
    /// Gets the token of the loop that was started last, a copy taken when it started: reading it from the source would
    /// throw if a concurrent disposal had already released the source. Once the loop was stopped this is its cancelled
    /// token, so work that began just before the stop and reads the token late is cancelled too. It is
    /// <see cref="CancellationToken.None"/> only before the first start.
    /// </summary>
    public CancellationToken Token { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a loop was started and not stopped.
    /// </summary>
    public bool IsRunning => _task is not null;

    /// <summary>
    /// Starts the loop on the thread pool. Does nothing when it already runs.
    /// </summary>
    /// <param name="run">
    /// The loop body; it must return when its token is cancelled.
    /// </param>
    public void Start(Func<CancellationToken, Task> run)
    {
        if (_task is not null)
        {
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        var token = _cts.Token;
        Token = token;
        _task = Task.Run(() => run(token), CancellationToken.None);
    }

    /// <summary>
    /// Cancels the loop and waits for it to end. Does nothing when no loop runs.
    /// </summary>
    /// <returns>
    /// A task that completes when the loop has ended.
    /// </returns>
    public async Task StopAsync()
    {
        var task = _task;
        var cts = _cts;
        _task = null;
        _cts = null;
        if (task is null || cts is null)
        {
            return;
        }

        await cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        cts.Dispose();
    }
}
