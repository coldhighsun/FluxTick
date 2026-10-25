using FluxTick.Abstractions;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Common;

/// <summary>
/// The result of one polling cycle, from which the provider status and the next delay follow.
/// </summary>
/// <param name="Succeeded">
/// The number of requests that succeeded.
/// </param>
/// <param name="Failed">
/// The number of requests that failed.
/// </param>
/// <param name="LastError">
/// The most recent problem worth showing in the provider status, or <see langword="null"/>.
/// </param>
/// <param name="BackOff">
/// Whether the next cycle should wait longer, for example because the service is rate limiting.
/// </param>
public readonly record struct PollOutcome(int Succeeded, int Failed, string? LastError, bool BackOff);

/// <summary>
/// Base class for sources that poll a service. It owns the polling loop, an immediate poll for newly added
/// instruments, the delay that doubles while the service pushes back, and the translation of cycle results into
/// the provider status; derived classes only fetch and publish quotes.
/// </summary>
public abstract class PollingProviderBase : MarketDataProviderBase
{
    /// <summary>
    /// The number of consecutive failed cycles before the provider reports <see cref="ProviderConnectionState.Failed"/>.
    /// </summary>
    private const int FailureThreshold = 3;

    /// <summary>
    /// The upper bound for the delay after repeated pushback.
    /// </summary>
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The configured poll interval.
    /// </summary>
    private readonly TimeSpan _interval;

    /// <summary>
    /// The polling loop, or <see langword="null"/> while idle.
    /// </summary>
    private Task? _loop;

    /// <summary>
    /// Stops the polling loop without disposing the provider.
    /// </summary>
    private CancellationTokenSource? _loopCts;

    /// <summary>
    /// The token of the current loop, kept apart from <see cref="_loopCts"/> because a token stays usable after its
    /// source was cancelled and disposed.
    /// </summary>
    private CancellationToken _loopToken;

    /// <summary>
    /// The number of consecutive cycles without a single success.
    /// </summary>
    private int _failedCycles;

    /// <summary>
    /// Initializes a new instance of the <see cref="PollingProviderBase"/> class.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    /// <param name="interval">
    /// The poll interval.
    /// </param>
    /// <param name="timeProvider">
    /// The clock.
    /// </param>
    /// <param name="logger">
    /// The logger.
    /// </param>
    protected PollingProviderBase(ProviderDescriptor descriptor, TimeSpan interval, TimeProvider timeProvider, ILogger logger)
        : base(descriptor, timeProvider)
    {
        _interval = interval;
        Logger = logger;
    }

    /// <summary>
    /// Gets the logger.
    /// </summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// Fetches the given instruments once and publishes their quotes. An exception that escapes is logged and counts
    /// as one failed request.
    /// </summary>
    /// <param name="assets">
    /// The instruments to fetch; never empty.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the cycle.
    /// </param>
    /// <returns>
    /// How the cycle went.
    /// </returns>
    protected abstract Task<PollOutcome> PollOnceAsync(IReadOnlyCollection<Asset> assets, CancellationToken cancellationToken);

    /// <summary>
    /// Called once on disposal after the polling loop has ended. Release resources owned by the derived class here.
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
            _loopToken = _loopCts.Token;
            var token = _loopToken;
            _loop = Task.Run(() => RunAsync(token), CancellationToken.None);
        }
        else if (added.Count > 0)
        {
            // Show new instruments right away instead of at the next tick. The poll belongs to the loop, so stopping the
            // loop also stops it from reporting a status afterwards. The token is a copy taken when the loop started:
            // reading it from the source would throw if a concurrent disposal had already released the source.
            var token = _loopToken;
            _ = Task.Run(() => PollAndReportAsync(added, token), CancellationToken.None);
        }
    }

    /// <inheritdoc />
    protected override void OnStatusListenerFailed(Exception exception) =>
        Logger.LogWarning(exception, "A listener of the {Provider} status failed", Descriptor.DisplayName);

    /// <inheritdoc />
    protected override async ValueTask DisposeCoreAsync()
    {
        await StopLoopAsync().ConfigureAwait(false);
        await DisposeResourcesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the polling loop and waits for it to finish.
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
    /// Polls all subscriptions repeatedly, slowing down while the service pushes back.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token that stops the loop.
    /// </param>
    /// <returns>
    /// A task that completes when the loop is cancelled.
    /// </returns>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var delay = _interval;
        while (!cancellationToken.IsCancellationRequested)
        {
            var backOff = await PollAndReportAsync(CurrentSubscriptions, cancellationToken).ConfigureAwait(false);
            delay = backOff ? TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxBackoff.Ticks)) : _interval;
            try
            {
                await Task.Delay(delay, TimeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Runs one cycle and updates the provider status from its outcome. An unexpected exception is a failed cycle,
    /// not the end of the loop.
    /// </summary>
    /// <param name="assets">
    /// The instruments to fetch.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the cycle.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the next cycle should wait longer.
    /// </returns>
    private async Task<bool> PollAndReportAsync(IReadOnlyCollection<Asset> assets, CancellationToken cancellationToken)
    {
        PollOutcome outcome;
        try
        {
            outcome = await PollOnceAsync(assets, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{Provider} poll failed", Descriptor.DisplayName);
            outcome = new PollOutcome(0, 1, ex.Message, BackOff: true);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            // The loop was stopped while the poll ran; the new status (for example Idle) must stand.
            return false;
        }

        if (outcome.Succeeded > 0)
        {
            Volatile.Write(ref _failedCycles, 0);
            SetStatus(ProviderConnectionState.Connected, outcome.LastError);
        }
        else if (outcome.Failed > 0)
        {
            var failedCycles = Interlocked.Increment(ref _failedCycles);
            SetStatus(
                failedCycles >= FailureThreshold ? ProviderConnectionState.Failed : ProviderConnectionState.Reconnecting,
                outcome.LastError);
        }

        return outcome.BackOff;
    }
}
