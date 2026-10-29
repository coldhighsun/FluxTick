using FluxTick.Abstractions;
using FluxTick.Core.Domain;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using ThrottledLogging;

namespace FluxTick.Core.Application;

/// <summary>
/// Tuning knobs of <see cref="MarketDataService"/>.
/// </summary>
public sealed record MarketDataServiceOptions
{
    /// <summary>
    /// Gets how long without a quote before an instrument is considered stale. A polled source whose poll interval plus
    /// half of this time is longer is given that instead, so that the time between two polls is not taken for silence.
    /// </summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets the number of points kept per sparkline.
    /// </summary>
    public int SparklineCapacity { get; init; } = 60;

    /// <summary>
    /// Gets the time covered by one sparkline point.
    /// </summary>
    public TimeSpan SparklineBucket { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets how often health is re-evaluated.
    /// </summary>
    public TimeSpan HealthInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets how long a handler of <see cref="MarketDataService.ProviderStatusChanged"/> or
    /// <see cref="MarketDataService.SnapshotChanged"/> may take before a warning is logged, because the events behind it
    /// wait.
    /// </summary>
    public TimeSpan SlowHandlerWarning { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets the shortest time between two log messages about the same failing or slow event handler. Such a problem
    /// repeats with every quote, and the messages in between are only counted.
    /// </summary>
    public TimeSpan HandlerLogInterval { get; init; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// Event data for a provider status change.
/// </summary>
/// <param name="ProviderId">
/// The provider whose status changed.
/// </param>
/// <param name="Status">
/// The new status.
/// </param>
public sealed record ProviderStatusChangedEventArgs(string ProviderId, ProviderStatus Status);

/// <summary>
/// Runs the enabled providers, routes the watchlist to them and merges their quotes into per-instrument state
/// (latest quote, sparkline, health). This is the single object the UI observes.
/// </summary>
public sealed class MarketDataService : IAsyncDisposable
{
    /// <summary>
    /// The number of history answers without a point after which an instrument is not asked again.
    /// </summary>
    private const int MaxEmptyHistoryAnswers = 3;

    /// <summary>
    /// The number of failed history loads after which the health timer stops asking again. With the wait doubling from
    /// 30 seconds, that is about 15 minutes of trying; a source that is still failing then is asked again only when the
    /// settings are applied, not for as long as the application runs.
    /// </summary>
    private const int MaxTimerSeedRetries = 5;

    /// <summary>
    /// The number of quotes in a row, each traded before the one shown, that are ignored as late answers; the next one is
    /// taken as a time that really went back.
    /// </summary>
    private const int MaxOlderQuotesInARow = 5;

    /// <summary>
    /// How long a regular session has to be under way before no trade today means a holiday, for a source that reports the
    /// state of the market itself. It is longer than the default, as such a source may lag by up to half an hour, and its
    /// last trade is from the day before until the first delayed trade comes in.
    /// </summary>
    private static readonly TimeSpan ReportingSourceHolidayGrace = TimeSpan.FromMinutes(45);

    /// <summary>
    /// The wait before a failed history load is tried again; it doubles with each failure.
    /// </summary>
    private static readonly TimeSpan SeedRetryBase = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The longest wait before a failed history load is tried again.
    /// </summary>
    private static readonly TimeSpan SeedRetryMax = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The available providers.
    /// </summary>
    private readonly ProviderRegistry _registry;

    /// <summary>
    /// The logger factory handed to providers.
    /// </summary>
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// The clock.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The tuning options.
    /// </summary>
    private readonly MarketDataServiceOptions _options;

    /// <summary>
    /// The service logger.
    /// </summary>
    private readonly ILogger<MarketDataService> _logger;

    /// <summary>
    /// Guards the mutable state below.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// Serializes <see cref="ApplyAsync"/> calls.
    /// </summary>
    private readonly SemaphoreSlim _applyLock = new(1, 1);

    /// <summary>
    /// Per-instrument state keyed by id.
    /// </summary>
    private readonly Dictionary<AssetId, AssetState> _assets = [];

    /// <summary>
    /// The running providers keyed by provider id.
    /// </summary>
    private readonly Dictionary<string, ProviderSlot> _slots = [];

    /// <summary>
    /// The watchlist order.
    /// </summary>
    private List<AssetId> _order = [];

    /// <summary>
    /// Periodically re-evaluates health so staleness is noticed without new quotes.
    /// </summary>
    private readonly ITimer _healthTimer;

    /// <summary>
    /// Whether the service was disposed; set once, read under <see cref="_applyLock"/>.
    /// </summary>
    private volatile bool _disposed;

    /// <summary>
    /// The providers whose status is to be announced, in order. Events are never raised on the thread that reports a
    /// change, because that may be the one holding the apply lock; <see cref="_statusPump"/> raises them.
    /// </summary>
    private readonly Channel<string> _statusQueue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>
    /// Raises the queued status events one after the other.
    /// </summary>
    private readonly Task _statusPump;

    /// <summary>
    /// How long disposing waits for an apply that is still running.
    /// </summary>
    private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Initializes a new instance of the <see cref="MarketDataService"/> class.
    /// </summary>
    /// <param name="registry">
    /// The available providers.
    /// </param>
    /// <param name="loggerFactory">
    /// The logger factory.
    /// </param>
    /// <param name="timeProvider">
    /// The clock.
    /// </param>
    /// <param name="options">
    /// Optional tuning options.
    /// </param>
    public MarketDataService(
        ProviderRegistry registry,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider,
        MarketDataServiceOptions? options = null)
    {
        _registry = registry;
        _loggerFactory = loggerFactory;
        _timeProvider = timeProvider;
        _options = options ?? new MarketDataServiceOptions();
        _logger = loggerFactory.CreateLogger<MarketDataService>();
        _healthTimer = timeProvider.CreateTimer(
            _ =>
            {
                RefreshHealth();

                // A tick that was already running when the service was disposed must not start loads on providers that
                // are being closed.
                if (!_disposed)
                {
                    SeedEmptySeries(failedOnly: true);
                }
            },
            null,
            _options.HealthInterval,
            _options.HealthInterval);
        _statusPump = Task.Run(PumpStatusAsync);
    }

    /// <summary>
    /// Raised when the snapshot of an instrument changed. May be raised on any thread: on the one that delivers the
    /// quote of a provider, and on the ones that apply settings, refresh health or report a status change. Handlers must
    /// return quickly (post to the UI thread instead of waiting), because they hold up the quotes of the provider, or the
    /// status events of all providers, while they run. A handler that throws is logged and does not keep the other
    /// handlers from getting the snapshot or the provider from delivering more quotes, and a slow one is reported.
    /// Both are logged at most once per <see cref="MarketDataServiceOptions.HandlerLogInterval"/>.
    /// </summary>
    public event EventHandler<AssetSnapshot>? SnapshotChanged;

    /// <summary>
    /// Raised when a provider's connection status changed, or what is shown for it (for example paused instead of idle).
    /// Events are queued and raised one after the other by a thread of the service, in the order they were queued, so
    /// they can arrive a moment after the change and after <c>ApplyAsync</c> returned. The status in the event is the
    /// one the provider has when the event is raised, so a change can be delivered with an earlier queue entry of the
    /// same provider, and quick changes may be seen as one. A status equal to the one raised last is not raised again. Handlers must return quickly (post to the UI thread instead of waiting): a handler that blocks
    /// holds back the events of every provider, and a warning is logged when one is slow. A handler that throws is logged
    /// and does not keep the other handlers from getting the event.
    /// </summary>
    public event EventHandler<ProviderStatusChangedEventArgs>? ProviderStatusChanged;

    /// <summary>
    /// Gets snapshots of all tracked instruments in watchlist order.
    /// </summary>
    /// <returns>
    /// The snapshots.
    /// </returns>
    public IReadOnlyList<AssetSnapshot> GetSnapshots()
    {
        lock (_gate)
        {
            return _order.Where(_assets.ContainsKey).Select(id => BuildSnapshot(_assets[id])).ToArray();
        }
    }

    /// <summary>
    /// Gets the status of a running provider.
    /// </summary>
    /// <param name="providerId">
    /// The provider id.
    /// </param>
    /// <returns>
    /// The status, or <see langword="null"/> when the provider is not running.
    /// </returns>
    public ProviderStatus? GetProviderStatus(string providerId)
    {
        lock (_gate)
        {
            return _slots.TryGetValue(providerId, out var slot) ? slot.Provider.Status : null;
        }
    }

    /// <summary>
    /// Determines whether a running provider can search its instrument catalog.
    /// </summary>
    /// <param name="providerId">
    /// The provider id.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the provider is running and supports search.
    /// </returns>
    public bool SupportsSearch(string providerId)
    {
        lock (_gate)
        {
            return _slots.TryGetValue(providerId, out var slot) && slot.Provider is ISymbolSearchProvider;
        }
    }

    /// <summary>
    /// Searches a running provider's instrument catalog.
    /// </summary>
    /// <param name="providerId">
    /// The provider id.
    /// </param>
    /// <param name="query">
    /// The text typed by the user.
    /// </param>
    /// <param name="assetClass">
    /// Restricts results to one asset class, or <see langword="null"/> for any.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the search.
    /// </param>
    /// <returns>
    /// Matching instruments; empty when the provider is not running or cannot search.
    /// </returns>
    public async Task<IReadOnlyList<Asset>> SearchAsync(
        string providerId,
        string query,
        AssetClass? assetClass,
        CancellationToken cancellationToken = default)
    {
        ISymbolSearchProvider? search;
        lock (_gate)
        {
            search = _slots.TryGetValue(providerId, out var slot) ? slot.Provider as ISymbolSearchProvider : null;
        }

        if (search is null)
        {
            _logger.LogInformation("Search on {ProviderId} skipped: the provider is not running or cannot search", providerId);
            return [];
        }

        var found = await search.SearchAsync(query, assetClass, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Search on {ProviderId} for {Query} found {Count} instruments", providerId, query, found.Count);
        return found;
    }

    /// <summary>
    /// Reconciles the running providers and subscriptions with the settings: starts newly enabled providers,
    /// restarts those whose options changed, stops disabled ones and routes the watchlist.
    /// </summary>
    /// <param name="settings">
    /// The settings to apply.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the operation.
    /// </param>
    /// <returns>
    /// A task that completes once the new configuration is active.
    /// </returns>
    public async Task ApplyAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        List<string> announced = [];
        List<AssetId> snapshots;
        await _applyLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            var desired = new Dictionary<string, IReadOnlyDictionary<string, string>>();
            foreach (var descriptor in _registry.Descriptors.Where(settings.IsProviderEnabled))
            {
                desired[descriptor.Id] = EffectiveOptions(descriptor, settings.Providers.GetValueOrDefault(descriptor.Id)?.Options);
            }

            await RetireProvidersAsync(desired).ConfigureAwait(false);
            announced.AddRange(await StartMissingProvidersAsync(desired).ConfigureAwait(false));

            var watchlist = WatchlistOf(settings);
            UpdateAssets(watchlist);
            await RouteSubscriptionsAsync(watchlist, cancellationToken).ConfigureAwait(false);
            snapshots = CollectHealthChanges(forceRaise: true);
        }
        finally
        {
            _applyLock.Release();
        }

        // Events leave the service only after the lock is released, so a handler that waits for another apply cannot
        // deadlock.
        RaiseSnapshots(snapshots);
        EnqueueStatus(announced);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Later applies to do nothing from here on. The providers are disposed first, which also frees an apply that waits
        // on one of them, so a stuck provider cannot block shutdown.
        _disposed = true;
        await _healthTimer.DisposeAsync().ConfigureAwait(false);
        await DisposeSlotsAsync().ConfigureAwait(false);

        // An apply that was already running may have started providers meanwhile; give it a moment, then dispose those.
        if (await _applyLock.WaitAsync(DisposeWait).ConfigureAwait(false))
        {
            _applyLock.Release();
        }

        await DisposeSlotsAsync().ConfigureAwait(false);

        // Let the queued events out, but do not wait for a handler that never returns.
        _statusQueue.Writer.TryComplete();
        try
        {
            await _statusPump.WaitAsync(DisposeWait).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // A handler that never returns; the process is going away anyway.
        }
    }

    /// <summary>
    /// Disposes every running provider.
    /// </summary>
    /// <returns>
    /// A task that completes once they are disposed.
    /// </returns>
    private async Task DisposeSlotsAsync()
    {
        List<ProviderSlot> slots;
        lock (_gate)
        {
            slots = [.. _slots.Values];
            _slots.Clear();
        }

        foreach (var slot in slots)
        {
            await DisposeSlotQuietlyAsync(slot).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Disposes one provider slot; a failure is logged so the remaining slots are still disposed.
    /// </summary>
    /// <param name="slot">
    /// The slot to dispose.
    /// </param>
    /// <returns>
    /// A task that completes once the slot is disposed or its failure is logged.
    /// </returns>
    private async Task DisposeSlotQuietlyAsync(ProviderSlot slot)
    {
        try
        {
            await slot.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Disposing provider {Provider} failed", slot.Id);
        }
    }

    /// <summary>
    /// Builds the instruments of the watchlist, once per id, in display order.
    /// </summary>
    /// <param name="settings">
    /// The settings that hold the watchlist.
    /// </param>
    /// <returns>
    /// The instruments.
    /// </returns>
    private static List<Asset> WatchlistOf(AppSettings settings) =>
        settings.Watchlist.Select(e => e.ToAsset()).DistinctBy(a => a.Id).ToList();

    /// <summary>
    /// Sends every provider its instruments: a provider with none is left without a connection until it gets some. Must
    /// be called while holding the apply lock.
    /// </summary>
    /// <param name="watchlist">
    /// The instruments to track.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the operation.
    /// </param>
    /// <returns>
    /// A task that completes once every provider has its instruments.
    /// </returns>
    private async Task RouteSubscriptionsAsync(List<Asset> watchlist, CancellationToken cancellationToken)
    {
        List<ProviderSlot> slots;
        lock (_gate)
        {
            slots = [.. _slots.Values];
        }

        // A provider that fails must not keep the others from getting their subscriptions; the failure is raised once
        // all of them were tried.
        List<Exception> failures = [];
        foreach (var slot in slots)
        {
            var mine = watchlist.Where(a => a.ProviderId == slot.Id).ToArray();
            _logger.LogDebug("Routing {Count} instruments to provider {ProviderId}", mine.Length, slot.Id);
            try
            {
                await slot.Provider.SetSubscriptionsAsync(mine, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Also a timeout that surfaces as a cancellation: only the caller's own token ends the whole routing.
                failures.Add(ex);
            }
        }

        if (failures.Count > 0)
        {
            throw failures.Count == 1 ? failures[0] : new AggregateException(failures);
        }

        SeedEmptySeries();
    }

    /// <summary>
    /// Queues a status event for each provider. The event carries the status the provider has when it is raised, so
    /// events that wait for each other still end on the current state.
    /// </summary>
    /// <param name="providerIds">
    /// The providers whose shown status changed.
    /// </param>
    private void EnqueueStatus(IEnumerable<string> providerIds)
    {
        foreach (var id in providerIds)
        {
            _statusQueue.Writer.TryWrite(id);
        }
    }

    /// <summary>
    /// Raises the snapshot event.
    /// </summary>
    /// <param name="snapshot">
    /// The snapshot.
    /// </param>
    private void RaiseSnapshot(AssetSnapshot snapshot) => Raise(SnapshotChanged, snapshot, nameof(SnapshotChanged));

    /// <summary>
    /// Raises an event one subscriber at a time, so a subscriber that throws or is slow does not keep the others from
    /// getting it. A failure is logged, and a subscriber that takes longer than the limit is reported, because the
    /// events behind it wait. Both are logged at most once per <see cref="MarketDataServiceOptions.HandlerLogInterval"/>
    /// per event, as they would otherwise repeat with every quote.
    /// </summary>
    /// <typeparam name="TArgs">
    /// The event data type.
    /// </typeparam>
    /// <param name="handler">
    /// The event.
    /// </param>
    /// <param name="args">
    /// The event data.
    /// </param>
    /// <param name="eventName">
    /// The name of the event, for the log.
    /// </param>
    private void Raise<TArgs>(EventHandler<TArgs>? handler, TArgs args, string eventName)
    {
        if (handler is null)
        {
            return;
        }

        // Enumerating the invocation list does not allocate, which matters because every quote goes through here.
        foreach (var subscriber in Delegate.EnumerateInvocationList(handler))
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                subscriber(this, args);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogErrorThrottled($"{eventName}:failed", _options.HandlerLogInterval, ex, "A handler of {Event} failed", eventName);
            }

            var took = System.Diagnostics.Stopwatch.GetElapsedTime(started);
            if (took > _options.SlowHandlerWarning)
            {
                _logger.LogWarningThrottled($"{eventName}:slow", _options.HandlerLogInterval, "A handler of {Event} took {Elapsed} ms; the events behind it wait", eventName, (int)took.TotalMilliseconds);
            }
        }
    }

    /// <summary>
    /// Raises the queued status events in order, each followed by a health refresh.
    /// </summary>
    /// <returns>
    /// A task that completes when the queue is completed and drained.
    /// </returns>
    private async Task PumpStatusAsync()
    {
        // What was raised last for each provider, so a change that leaves the shown status as it is raises nothing.
        Dictionary<string, ProviderStatus> raised = [];
        await foreach (var id in _statusQueue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            // After disposal the providers are gone; refreshing now would report every instrument as having no source.
            if (_disposed)
            {
                continue;
            }

            try
            {
                ProviderStatus? status;
                lock (_gate)
                {
                    status = _slots.TryGetValue(id, out var slot) ? slot.Provider.Status : null;
                }

                if (status is null)
                {
                    raised.Remove(id);
                }
                else if (!raised.TryGetValue(id, out var last) || last != status)
                {
                    raised[id] = status;
                    Raise(ProviderStatusChanged, new ProviderStatusChangedEventArgs(id, status), nameof(ProviderStatusChanged));
                }

                // The handler may have run while the service was being disposed.
                if (!_disposed)
                {
                    RefreshHealth();
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogError(ex, "Could not handle the status of provider {ProviderId}", id);
            }
        }
    }

    /// <summary>
    /// Disposes providers that are no longer wanted or whose options changed.
    /// </summary>
    /// <param name="desired">
    /// The wanted providers and their options.
    /// </param>
    /// <returns>
    /// A task that completes once the providers are disposed.
    /// </returns>
    private async Task RetireProvidersAsync(Dictionary<string, IReadOnlyDictionary<string, string>> desired)
    {
        List<ProviderSlot> retired = [];
        lock (_gate)
        {
            foreach (var (id, slot) in _slots.ToArray())
            {
                if (!desired.TryGetValue(id, out var options) || !OptionsEqual(slot.Options, options))
                {
                    _logger.LogInformation(
                        "Retiring provider {ProviderId}: {Reason}", id, desired.ContainsKey(id) ? "its options changed" : "it is disabled");
                    _slots.Remove(id);
                    retired.Add(slot);

                    // The wait after a failed history load was for the provider that is gone; the new one starts afresh.
                    foreach (var state in _assets.Values.Where(s => s.Asset.ProviderId == id))
                    {
                        state.SeedFailures = 0;
                        state.NextSeedAttempt = default;
                        state.EmptyHistoryAnswers = 0;
                    }
                }
            }
        }

        foreach (var slot in retired)
        {
            await DisposeSlotQuietlyAsync(slot).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Creates and starts providers that are wanted but not running.
    /// </summary>
    /// <param name="desired">
    /// The wanted providers and their options.
    /// </param>
    /// <returns>
    /// The ids of the providers that were started, to announce once the apply lock is released.
    /// </returns>
    private async Task<List<string>> StartMissingProvidersAsync(Dictionary<string, IReadOnlyDictionary<string, string>> desired)
    {
        List<string> started = [];
        foreach (var (id, options) in desired)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return started;
                }

                if (_slots.ContainsKey(id))
                {
                    continue;
                }
            }

            if (!_registry.TryGetFactory(id, out var factory))
            {
                continue;
            }

            try
            {
                var provider = factory.Create(new ProviderCreationContext(
                    new ProviderOptions(factory.Descriptor, options),
                    _loggerFactory,
                    _timeProvider));
                var slot = new ProviderSlot(id, provider, options);
                provider.StatusChanged += (_, _) => OnProviderStatusChanged(id);
                slot.Pump = Task.Run(() => PumpAsync(slot));
                bool rejected;
                lock (_gate)
                {
                    // Disposal may have cleared the slots while the provider was being created; nobody would dispose it.
                    rejected = _disposed;
                    if (!rejected)
                    {
                        _slots[id] = slot;
                    }
                }

                if (rejected)
                {
                    await slot.DisposeAsync().ConfigureAwait(false);
                    return started;
                }

                _logger.LogInformation("Provider {ProviderId} created with {OptionCount} options", id, options.Count);
                started.Add(id);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogError(ex, "Failed to create provider {ProviderId}", id);
            }
        }

        return started;
    }

    /// <summary>
    /// Aligns the tracked instruments with the watchlist, keeping state of instruments that stay.
    /// </summary>
    /// <param name="watchlist">
    /// The new watchlist in display order.
    /// </param>
    private void UpdateAssets(List<Asset> watchlist)
    {
        lock (_gate)
        {
            var keep = watchlist.Select(a => a.Id).ToHashSet();
            foreach (var id in _assets.Keys.Where(id => !keep.Contains(id)).ToArray())
            {
                _assets.Remove(id);
            }

            foreach (var asset in watchlist)
            {
                if (_assets.TryGetValue(asset.Id, out var state))
                {
                    state.Asset = asset;
                }
                else
                {
                    _assets[asset.Id] = new AssetState(asset, new SparklineSeries(_options.SparklineCapacity, _options.SparklineBucket))
                    {
                        // Nothing was raised yet, so the bucket of now is not a change.
                        LastSparklineBucket = _timeProvider.GetUtcNow().UtcTicks / _options.SparklineBucket.Ticks,
                    };
                }
            }

            _order = [.. watchlist.Select(a => a.Id)];
        }
    }

    /// <summary>
    /// Starts a background history load for every instrument whose sparkline was not seeded from history yet. A sparkline
    /// that is not empty is no reason to skip it: a source that sends its first price right away has put a live point in
    /// before this runs.
    /// </summary>
    /// <param name="failedOnly">
    /// Whether to start only the loads that failed before, fewer than <see cref="MaxTimerSeedRetries"/> times, and whose
    /// wait is over. The health timer does this, so that a failed load is retried although nothing changes the settings;
    /// the empty answers of a market without data stay counted per route.
    /// </param>
    private void SeedEmptySeries(bool failedOnly = false)
    {
        List<(AssetState State, IHistoryProvider History)> work = [];
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            foreach (var state in _assets.Values)
            {
                if (state is { Seeded: false, Seeding: false }
                    && (!failedOnly || state.SeedFailures is > 0 and < MaxTimerSeedRetries)
                    && state.NextSeedAttempt <= now
                    && _slots.TryGetValue(state.Asset.ProviderId, out var slot)
                    && slot.Provider is IHistoryProvider history)
                {
                    state.Seeding = true;
                    work.Add((state, history));
                }
            }
        }

        foreach (var (state, history) in work)
        {
            _ = Task.Run(() => SeedAsync(state, history));
        }
    }

    /// <summary>
    /// Loads history for one instrument and merges it with any live ticks that arrived meanwhile.
    /// </summary>
    /// <param name="state">
    /// The instrument state.
    /// </param>
    /// <param name="history">
    /// The provider to load from.
    /// </param>
    /// <returns>
    /// A task that completes when seeding is done.
    /// </returns>
    private async Task SeedAsync(AssetState state, IHistoryProvider history)
    {
        AssetSnapshot? snapshot = null;
        try
        {
            var points = await history.GetHistoryAsync(state.Asset).ConfigureAwait(false);
            lock (_gate)
            {
                state.SeedFailures = 0;
                state.NextSeedAttempt = default;
                if (points.Count == 0 && ++state.EmptyHistoryAnswers >= MaxEmptyHistoryAnswers)
                {
                    // A source without history, or a market without data today, would otherwise be asked again with every
                    // settings change; a few empty answers in a row are taken as the answer.
                    state.Seeded = true;
                }

                if (points.Count > 0 && _assets.ContainsKey(state.Asset.Id))
                {
                    var latest = state.Latest;
                    state.Seeded = true;
                    state.Series.Seed(points);
                    if (latest is not null)
                    {
                        state.Series.Add(latest.Price, latest.Timestamp);
                    }

                    snapshot = BuildSnapshot(state);
                    MarkPublished(state, snapshot);
                    _logger.LogInformation("Seeded {AssetId} with {Count} history points", state.Asset.Id, points.Count);
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "Could not load history for {AssetId}", state.Asset.Id);

            // Asking again with every route and every tick of the health timer would hammer a source that keeps failing:
            // wait longer after each failure, up to a cap.
            lock (_gate)
            {
                state.SeedFailures++;
                var wait = TimeSpan.FromTicks(Math.Min(
                    SeedRetryBase.Ticks << Math.Min(state.SeedFailures - 1, 10),
                    SeedRetryMax.Ticks));
                state.NextSeedAttempt = _timeProvider.GetUtcNow() + wait;
            }
        }
        finally
        {
            lock (_gate)
            {
                state.Seeding = false;
            }
        }

        if (snapshot is not null)
        {
            RaiseSnapshot(snapshot);
        }
    }

    /// <summary>
    /// Reads a provider's quotes until it is disposed.
    /// </summary>
    /// <param name="slot">
    /// The provider to read.
    /// </param>
    /// <returns>
    /// A task that completes when the stream ends.
    /// </returns>
    private async Task PumpAsync(ProviderSlot slot)
    {
        try
        {
            await foreach (var quote in slot.Provider.ReadQuotesAsync(slot.Cts.Token).ConfigureAwait(false))
            {
                HandleQuote(quote);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the provider is retired.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Quote stream of {ProviderId} ended unexpectedly", slot.Id);
        }
    }

    /// <summary>
    /// Applies a quote to the matching instrument and notifies listeners. A quote whose trade time is older than the
    /// one shown is not applied, unless one of the two times is only an estimate.
    /// </summary>
    /// <param name="quote">
    /// The quote to apply.
    /// </param>
    private void HandleQuote(Quote quote)
    {
        AssetSnapshot snapshot;
        lock (_gate)
        {
            if (!_assets.TryGetValue(quote.AssetId, out var state))
            {
                return;
            }

            var received = _timeProvider.GetUtcNow();
            if (state.Latest is { TimeIsEstimated: false } previous && !quote.TimeIsEstimated)
            {
                if (quote.Timestamp < previous.Timestamp && state.OlderQuotesInARow < MaxOlderQuotesInARow)
                {
                    // A quote traded before the one shown, such as a snapshot or a poll that finishes after a pushed tick,
                    // must not move the price back or replace the market state of the newer one. The source did answer,
                    // which is what the health check reads from the time of the last quote received. A source whose times
                    // keep running backwards (a clock or time zone that jumped) is not a late answer but a bad time; after
                    // a few such quotes in a row the price follows it again rather than freezing for good.
                    state.OlderQuotesInARow++;
                    state.LastReceivedAt = received;
                    return;
                }

                if (quote.Timestamp > previous.Timestamp)
                {
                    state.AdvancedAt = received;
                }
            }

            state.OlderQuotesInARow = 0;

            state.Latest = quote;
            state.LastReceivedAt = received;
            state.Series.Add(quote.Price, quote.Timestamp);
            snapshot = BuildSnapshot(state);
            MarkPublished(state, snapshot);
        }

        RaiseSnapshot(snapshot);
    }

    /// <summary>
    /// Re-evaluates the health of all instruments and raises events for those that changed.
    /// </summary>
    /// <param name="forceRaise">
    /// Raise an event for every instrument, even when its health did not change.
    /// </param>
    private void RefreshHealth(bool forceRaise = false) => RaiseSnapshots(CollectHealthChanges(forceRaise));

    /// <summary>
    /// Re-evaluates the health of all instruments and returns the ones that changed, without raising events.
    /// </summary>
    /// <param name="forceRaise">
    /// Return every instrument, even when its health did not change.
    /// </param>
    /// <returns>
    /// The instruments whose snapshot is to be raised.
    /// </returns>
    private List<AssetId> CollectHealthChanges(bool forceRaise)
    {
        List<AssetId> changed = [];
        lock (_gate)
        {
            foreach (var state in _assets.Values)
            {
                // Only the health and the sparkline bucket are needed here; the snapshot is built when the event is
                // raised. The sparkline slides along with the time, so a new bucket alone also changes the snapshot.
                var (health, _) = ComputeHealth(state);
                var now = _timeProvider.GetUtcNow();
                var bucket = BucketOf(state, now);

                // A new bucket moves the window on, which changes the chart only when it has points to move or just lost
                // its last ones; an empty one stays as it is.
                var slid = bucket != state.LastSparklineBucket && (state.LastSparklineCount > 0 || state.Series.AnyWithin(now));
                state.LastSparklineBucket = bucket;
                var market = MarketStateOf(state, now);
                if (health != state.LastHealth || market != state.LastMarketState)
                {
                    _logger.LogInformation(
                        "{AssetId} health {PreviousHealth} -> {Health}, market {PreviousMarket} -> {Market}",
                        state.Asset.Id, state.LastHealth, health, state.LastMarketState, market);
                }

                if (forceRaise || health != state.LastHealth || slid || market != state.LastMarketState)
                {
                    state.LastHealth = health;
                    state.LastMarketState = market;
                    changed.Add(state.Asset.Id);
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Raises the snapshot event for each instrument with what it looks like now. Building the snapshot only when the
    /// event is raised keeps one that waited for a lock from overtaking a newer quote with older data.
    /// </summary>
    /// <param name="ids">
    /// The instruments whose snapshot changed.
    /// </param>
    private void RaiseSnapshots(List<AssetId> ids)
    {
        foreach (var id in ids)
        {
            AssetSnapshot? snapshot = null;
            lock (_gate)
            {
                if (_assets.TryGetValue(id, out var state))
                {
                    snapshot = BuildSnapshot(state);
                    MarkPublished(state, snapshot);
                }
            }

            if (snapshot is not null)
            {
                RaiseSnapshot(snapshot);
            }
        }
    }

    /// <summary>
    /// Handles a provider status change by queueing its event, which also refreshes health.
    /// </summary>
    /// <param name="providerId">
    /// The provider id.
    /// </param>
    private void OnProviderStatusChanged(string providerId) => EnqueueStatus([providerId]);

    /// <summary>
    /// Builds an immutable snapshot. Must be called while holding the lock.
    /// </summary>
    /// <param name="state">
    /// The instrument state.
    /// </param>
    /// <returns>
    /// The snapshot.
    /// </returns>
    private AssetSnapshot BuildSnapshot(AssetState state)
    {
        var (health, detail) = ComputeHealth(state);
        var now = _timeProvider.GetUtcNow();
        var sparkline = state.Series.Snapshot(now);
        var timeline = new SparklineTimeline(state.Series.Times(now), now, state.Series.Window);
        return new AssetSnapshot(state.Asset, state.Latest, sparkline, health, detail, timeline, MarketStateOf(state, now));
    }

    /// <summary>
    /// Works out the trading state of an instrument: what the source reports with its quote, else the regular hours of the
    /// market. Prices that stopped while the source keeps answering mean the market does not trade, but a source that went
    /// quiet says nothing about it, so the state is then unknown and the row keeps its stale warning.
    /// </summary>
    /// <param name="state">
    /// The instrument state.
    /// </param>
    /// <param name="now">
    /// The current time.
    /// </param>
    /// <returns>
    /// The trading state of its market.
    /// </returns>
    private MarketState MarketStateOf(AssetState state, DateTimeOffset now)
    {
        if (state.LastReceivedAt is not { } received
            || !_slots.TryGetValue(state.Asset.ProviderId, out var slot)
            || now - received > StaleAfterOf(slot))
        {
            return MarketState.Unknown;
        }

        var latest = state.Latest;
        var hours = MarketCalendar.GetState(state.Asset, now);
        if (hours == MarketState.Unknown && slot.Provider is IMarketHoursProvider source)
        {
            hours = source.GetMarketState(state.Asset, now, latest?.Timestamp, latest?.Market is null ? null : ReportingSourceHolidayGrace);
        }

        if (latest?.Market is not { } reported)
        {
            return hours;
        }

        // The source knows shortened days and, it is assumed, holidays, which the hours do not.
        return reported switch
        {
            // It does not know the midday break of the Chinese and Hong Kong markets, which it reports as open.
            MarketState.Open when hours == MarketState.Break => MarketState.Break,

            // A session that is well under way with no trade yet today is a holiday that the source did not catch.
            MarketState.Open when hours == MarketState.Closed => MarketState.Closed,

            // A price that keeps advancing beats a report of a shut market, which may be about the day before when the
            // source has not turned the page yet.
            MarketState.Closed when state.AdvancedAt is { } advanced && now - advanced <= StaleAfterOf(slot) => hours,
            _ => reported,
        };
    }

    /// <summary>
    /// Records what a snapshot that is about to be raised showed, so that only a change from it is raised next. Building
    /// a snapshot for a caller that asks for one does not count, as nobody listening to the event gets it. Must be
    /// called while holding the lock.
    /// </summary>
    /// <param name="state">
    /// The instrument state.
    /// </param>
    /// <param name="snapshot">
    /// The snapshot that is raised.
    /// </param>
    private static void MarkPublished(AssetState state, AssetSnapshot snapshot)
    {
        state.LastHealth = snapshot.Health;
        state.LastMarketState = snapshot.MarketState;
        state.LastSparklineCount = snapshot.Sparkline.Count;
        if (snapshot.Timeline is { } timeline)
        {
            state.LastSparklineBucket = BucketOf(state, timeline.AsOf);
        }
    }

    /// <summary>
    /// Finds the bucket of the sparkline a time falls into; the sparkline slides along with the time, so a snapshot is
    /// due when the time moves into a new bucket.
    /// </summary>
    /// <param name="state">
    /// The instrument state.
    /// </param>
    /// <param name="time">
    /// The time.
    /// </param>
    /// <returns>
    /// The number of the bucket.
    /// </returns>
    private static long BucketOf(AssetState state, DateTimeOffset time) => time.UtcTicks / state.Series.Bucket.Ticks;

    /// <summary>
    /// Derives the health of an instrument. Must be called while holding the lock.
    /// </summary>
    /// <param name="state">
    /// The instrument state.
    /// </param>
    /// <returns>
    /// The health and an optional detail.
    /// </returns>
    private (AssetHealth Health, string? Detail) ComputeHealth(AssetState state)
    {
        if (!_slots.TryGetValue(state.Asset.ProviderId, out var slot))
        {
            return _registry.GetDescriptor(state.Asset.ProviderId) is null
                ? (AssetHealth.ProviderMissing, null)
                : (AssetHealth.ProviderDisabled, null);
        }

        var status = slot.Provider.Status;
        if (status.State == ProviderConnectionState.Failed)
        {
            return (AssetHealth.Failed, status.Message);
        }

        // A source that has been reconnecting for longer than the threshold has old data, and "reconnecting" alone would hide
        // that for as long as the reconnecting goes on. The reason stays as the detail.
        if (status.State == ProviderConnectionState.Reconnecting)
        {
            return state.LastReceivedAt is { } received && _timeProvider.GetUtcNow() - received > StaleAfterOf(slot)
                ? (AssetHealth.StaleReconnecting, status.Message)
                : (AssetHealth.Reconnecting, status.Message);
        }

        if (state.LastReceivedAt is null)
        {
            return (AssetHealth.Waiting, null);
        }

        // Stale when nothing was received lately, or when what keeps arriving is an old price (a polled source repeats
        // the last price of a closed market with the time it was traded, which the receive time alone would not show).
        var now = _timeProvider.GetUtcNow();
        var quoteTime = state.Latest?.Timestamp ?? state.LastReceivedAt.Value;
        var staleAfter = StaleAfterOf(slot);
        return now - state.LastReceivedAt.Value > staleAfter || now - quoteTime > staleAfter
            ? (AssetHealth.Stale, null)
            : (AssetHealth.Live, null);
    }

    /// <summary>
    /// Works out how long an instrument may go without a quote before it is stale: the configured time, or the poll
    /// interval of its source plus half of that time when this is longer, as a polled source delivers once per interval
    /// and may be late by a margin. The result grows evenly with the interval.
    /// </summary>
    /// <param name="slot">
    /// The source of the instrument.
    /// </param>
    /// <returns>
    /// The time.
    /// </returns>
    private TimeSpan StaleAfterOf(ProviderSlot slot) =>
        slot.PollInterval is { } interval && interval + _options.StaleAfter / 2 > _options.StaleAfter
            ? interval + _options.StaleAfter / 2
            : _options.StaleAfter;

    /// <summary>
    /// Resolves the option values a provider really runs with: the stored value when valid, otherwise the declared
    /// default, written in canonical form. Comparing these instead of the raw stored values means a value written out
    /// explicitly, written differently or left to the default are the same configuration, so the provider is not
    /// restarted for it.
    /// </summary>
    /// <param name="descriptor">
    /// The descriptor that declares the options.
    /// </param>
    /// <param name="stored">
    /// The stored values, or <see langword="null"/> when none are stored.
    /// </param>
    /// <returns>
    /// The effective value of every declared option.
    /// </returns>
    private static Dictionary<string, string> EffectiveOptions(ProviderDescriptor descriptor, IReadOnlyDictionary<string, string>? stored)
    {
        var options = new ProviderOptions(descriptor, stored);
        var effective = new Dictionary<string, string>();
        foreach (var definition in descriptor.OptionDefinitions)
        {
            // An indexer, not Add, so a descriptor that declares a key twice cannot break applying the settings.
            effective[definition.Key] = definition.Canonicalize(options.GetString(definition.Key));
        }

        return effective;
    }

    /// <summary>
    /// Compares two option dictionaries for equality.
    /// </summary>
    /// <param name="left">
    /// The first dictionary.
    /// </param>
    /// <param name="right">
    /// The second dictionary.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both hold the same pairs.
    /// </returns>
    private static bool OptionsEqual(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count && left.All(p => right.TryGetValue(p.Key, out var v) && v == p.Value);

    /// <summary>
    /// Mutable per-instrument state guarded by the service lock.
    /// </summary>
    /// <param name="asset">
    /// The instrument.
    /// </param>
    /// <param name="series">
    /// The sparkline series.
    /// </param>
    private sealed class AssetState(Asset asset, SparklineSeries series)
    {
        /// <summary>
        /// Gets or sets the instrument.
        /// </summary>
        public Asset Asset { get; set; } = asset;

        /// <summary>
        /// Gets the sparkline series.
        /// </summary>
        public SparklineSeries Series { get; } = series;

        /// <summary>
        /// Gets or sets the latest quote.
        /// </summary>
        public Quote? Latest { get; set; }

        /// <summary>
        /// Gets or sets when the latest quote was received.
        /// </summary>
        public DateTimeOffset? LastReceivedAt { get; set; }

        /// <summary>
        /// Gets or sets when a quote last arrived that was traded later than the one before it, or <see langword="null"/>
        /// while the price has not moved since the first quote. A source that repeats an old price does not move it.
        /// </summary>
        public DateTimeOffset? AdvancedAt { get; set; }

        /// <summary>
        /// Gets or sets how many quotes in a row were ignored because they were traded before the one shown.
        /// </summary>
        public int OlderQuotesInARow { get; set; }

        /// <summary>
        /// Gets or sets the last health that was published.
        /// </summary>
        public AssetHealth? LastHealth { get; set; }

        /// <summary>
        /// Gets or sets the last trading state that was published.
        /// </summary>
        public MarketState? LastMarketState { get; set; }

        /// <summary>
        /// Gets or sets the number of sparkline points in the last snapshot that was raised.
        /// </summary>
        public int LastSparklineCount { get; set; }

        /// <summary>
        /// Gets or sets the sparkline bucket of the time of the last snapshot that was raised.
        /// </summary>
        public long LastSparklineBucket { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a history load is in flight.
        /// </summary>
        public bool Seeding { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the sparkline was seeded from history.
        /// </summary>
        public bool Seeded { get; set; }

        /// <summary>
        /// Gets or sets how many history loads ended without a single point.
        /// </summary>
        public int EmptyHistoryAnswers { get; set; }

        /// <summary>
        /// Gets or sets how many history loads failed in a row.
        /// </summary>
        public int SeedFailures { get; set; }

        /// <summary>
        /// Gets or sets the time before which the history is not asked for again after a failure.
        /// </summary>
        public DateTimeOffset NextSeedAttempt { get; set; }
    }

    /// <summary>
    /// A running provider with its quote pump.
    /// </summary>
    /// <param name="id">
    /// The provider id.
    /// </param>
    /// <param name="provider">
    /// The provider instance.
    /// </param>
    /// <param name="options">
    /// The options the instance was created with.
    /// </param>
    private sealed class ProviderSlot(string id, IMarketDataProvider provider, IReadOnlyDictionary<string, string> options)
    {
        /// <summary>
        /// Gets how often a polled source fetches prices, or <see langword="null"/> for a source that does not poll.
        /// </summary>
        public TimeSpan? PollInterval { get; } =
            options.TryGetValue(ProviderOptionDefinition.PollIntervalKey, out var text) && ProviderOptionDefinition.TryParseDuration(text, out var interval)
                ? interval
                : null;

        /// <summary>
        /// Gets the provider id.
        /// </summary>
        public string Id { get; } = id;

        /// <summary>
        /// Gets the provider instance.
        /// </summary>
        public IMarketDataProvider Provider { get; } = provider;

        /// <summary>
        /// Gets the options the instance was created with.
        /// </summary>
        public IReadOnlyDictionary<string, string> Options { get; } = options;

        /// <summary>
        /// Gets the cancellation source that stops the pump.
        /// </summary>
        public CancellationTokenSource Cts { get; } = new();

        /// <summary>
        /// Gets or sets the pump task.
        /// </summary>
        public Task Pump { get; set; } = Task.CompletedTask;

        /// <summary>
        /// Stops the pump and disposes the provider.
        /// </summary>
        /// <returns>
        /// A task that completes when everything is stopped.
        /// </returns>
        public async Task DisposeAsync()
        {
            await Cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await Provider.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                // The pump must end and the source be released even when the provider fails to dispose.
                try
                {
                    await Pump.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected.
                }

                Cts.Dispose();
            }
        }
    }
}
