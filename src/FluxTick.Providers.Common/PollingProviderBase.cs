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
/// <param name="Problems">
/// The problem of each instrument that has one, or <see langword="null"/> when the source does not tell them apart. A
/// source that does lets a problem go away with the instrument it was about.
/// </param>
public readonly record struct PollOutcome(
    int Succeeded,
    int Failed,
    string? LastError,
    bool BackOff,
    IReadOnlyDictionary<AssetId, string>? Problems = null);

/// <summary>
/// Base class for sources that poll a service. It owns the polling loop, an immediate poll for newly added
/// instruments, the delay that doubles while the service pushes back, and the translation of cycle results into
/// the provider status; derived classes only fetch and publish quotes.
/// </summary>
public abstract class PollingProviderBase : LoggingProviderBase
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
    /// The polling loop, which runs while instruments are subscribed.
    /// </summary>
    private readonly BackgroundLoop _loop;

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
        : base(descriptor, timeProvider, logger)
    {
        _interval = interval;
        _loop = new BackgroundLoop(ShutdownToken);
    }

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
            await _loop.StopAsync().ConfigureAwait(false);
            SetStatus(ProviderConnectionState.Idle);
            return;
        }

        if (!_loop.IsRunning)
        {
            _loop.Start(RunAsync);
        }
        else if (added.Count > 0)
        {
            // Show new instruments right away instead of at the next tick. The poll belongs to the loop, so stopping the
            // loop also stops it from reporting a status afterwards.
            var token = _loop.Token;
            _ = Task.Run(() => PollAndReportAsync(added, token), CancellationToken.None);
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// Tells whether the market of an instrument is known not to be trading, so that polling it is pointless. The first
    /// price of a new instrument is fetched regardless. Without a stream nothing else delivers the pre-market and after
    /// hours, so only a shut market or a break is skipped; see <see cref="TradingHoursPolicy"/>.
    /// </summary>
    /// <param name="asset">
    /// The instrument.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the market is known not to be trading.
    /// </returns>
    protected virtual bool IsOutsideTrading(Asset asset) =>
        TradingHoursPolicy.IsOutsideTrading(this as IMarketHoursProvider, asset, TimeProvider.GetUtcNow(), hasStream: false);

    /// <inheritdoc />
    protected override async ValueTask DisposeCoreAsync()
    {
        await _loop.StopAsync().ConfigureAwait(false);
        await DisposeResourcesAsync().ConfigureAwait(false);
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
        var first = true;
        while (!cancellationToken.IsCancellationRequested)
        {
            // A market that is shut has nothing new; the status stays as the last poll left it. The first poll gives every
            // instrument its first price, shut or not; later ones are given theirs when they are added.
            var assets = first ? CurrentSubscriptions : [.. CurrentSubscriptions.Where(a => !IsOutsideTrading(a))];
            first = false;
            var backOff = assets.Count > 0 && await PollAndReportAsync(assets, cancellationToken).ConfigureAwait(false);
            delay = backOff ? Backoff.Double(delay, MaxBackoff) : _interval;
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
