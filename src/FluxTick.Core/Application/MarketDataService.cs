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
    /// Gets how long without a quote before an instrument is considered stale.
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
    /// Gets how long the sources stay connected after the last user of the data is gone, when connecting on demand.
    /// A window that is shown and hidden in quick succession then does not reconnect every time.
    /// </summary>
    public TimeSpan IdleDisconnectDelay { get; init; } = TimeSpan.FromSeconds(30);

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
    private readonly object _gate = new();

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
    /// The settings applied last, used to re-route the subscriptions when the use of the data changes.
    /// </summary>
    private AppSettings? _lastSettings;

    /// <summary>
    /// How many users currently look at the data. Guarded by <see cref="_gate"/>.
    /// </summary>
    private int _usageCount;

    /// <summary>
    /// Whether sources connect only while the data is used. Guarded by <see cref="_gate"/>.
    /// </summary>
    private bool _onDemand;

    /// <summary>
    /// Whether the last user left recently and the sources are still kept connected. Guarded by <see cref="_gate"/>.
    /// </summary>
    private bool _inGrace;

    /// <summary>
    /// Ends the grace period. Guarded by <see cref="_gate"/>.
    /// </summary>
    private ITimer? _graceTimer;

    /// <summary>
    /// Identifies the running grace period, so that a callback of one that was replaced is ignored. Guarded by
    /// <see cref="_gate"/>.
    /// </summary>
    private int _graceVersion;

    /// <summary>
    /// Whether the subscriptions were last routed to the providers, or <see langword="null"/> before the first time.
    /// Guarded by <see cref="_applyLock"/>.
    /// </summary>
    private bool? _connected;

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
        _healthTimer = timeProvider.CreateTimer(_ => RefreshHealth(), null, _options.HealthInterval, _options.HealthInterval);
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
            return _slots.TryGetValue(providerId, out var slot) ? EffectiveStatus(slot.Provider.Status) : null;
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

        return search is null
            ? []
            : await search.SearchAsync(query, assetClass, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Registers that someone looks at the data. While connecting on demand, the sources only connect while at least one
    /// usage is held (and for a short grace period after the last one ends). Dispose the result to end the usage.
    /// </summary>
    /// <returns>
    /// The usage to dispose when the data is no longer looked at.
    /// </returns>
    public IDisposable AcquireUsage()
    {
        bool changed;
        lock (_gate)
        {
            var before = WantsConnection;
            _usageCount++;
            CancelGrace();
            changed = WantsConnection != before;
        }

        if (changed)
        {
            _ = ReapplyAsync();
        }

        return new Usage(this);
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
        List<AssetId> snapshots = [];
        await _applyLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            bool connect;
            lock (_gate)
            {
                // The first settings are the ones the app starts with: nobody has looked yet, so nothing connects until
                // somebody does. Only a later switch-on gets the grace period of a user who just left.
                var first = _lastSettings is null;
                _lastSettings = settings;
                var onDemand = settings.General.ConnectOnDemand;
                if (onDemand && !_onDemand && _usageCount == 0 && !first)
                {
                    StartGrace();
                }

                _onDemand = onDemand;
                connect = WantsConnection;
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
            announced.AddRange(await RouteSubscriptionsAsync(watchlist, connect, cancellationToken).ConfigureAwait(false));
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
        // Later applies do nothing from here on. The providers are disposed first, which also frees an apply that waits
        // on one of them, so a stuck provider cannot block shutdown.
        _disposed = true;
        await _healthTimer.DisposeAsync().ConfigureAwait(false);
        lock (_gate)
        {
            CancelGrace();
        }

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
            await slot.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the sources should be connected. Must be called while holding the lock.
    /// </summary>
    private bool WantsConnection => !_onDemand || _usageCount > 0 || _inGrace;

    /// <summary>
    /// Ends one usage and, when it was the last one, keeps the sources for the grace period.
    /// </summary>
    private void ReleaseUsage()
    {
        lock (_gate)
        {
            if (_usageCount == 0)
            {
                return;
            }

            _usageCount--;
            if (_usageCount == 0 && _onDemand)
            {
                StartGrace();
            }
        }
    }

    /// <summary>
    /// Starts the grace period after which the sources disconnect. Must be called while holding the lock.
    /// </summary>
    private void StartGrace()
    {
        CancelGrace();
        _inGrace = true;
        var version = _graceVersion;
        _graceTimer = _timeProvider.CreateTimer(_ => OnGraceElapsed(version), null, _options.IdleDisconnectDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Stops a running grace period. Must be called while holding the lock.
    /// </summary>
    private void CancelGrace()
    {
        _inGrace = false;
        _graceVersion++;
        _graceTimer?.Dispose();
        _graceTimer = null;
    }

    /// <summary>
    /// Disconnects the sources when the grace period ended without a new usage.
    /// </summary>
    /// <param name="version">
    /// The grace period the callback belongs to.
    /// </param>
    private void OnGraceElapsed(int version)
    {
        lock (_gate)
        {
            if (!_inGrace || version != _graceVersion)
            {
                return;
            }

            CancelGrace();
            if (WantsConnection)
            {
                return;
            }
        }

        _ = ReapplyAsync();
    }

    /// <summary>
    /// Routes the subscriptions again with the settings applied last, after the use of the data changed.
    /// </summary>
    /// <returns>
    /// A task that completes once the subscriptions are updated.
    /// </returns>
    private async Task ReapplyAsync()
    {
        List<string> announced = [];
        List<AssetId> snapshots = [];
        await _applyLock.WaitAsync().ConfigureAwait(false);
        try
        {
            AppSettings? settings;
            bool connect;
            lock (_gate)
            {
                settings = _lastSettings;
                connect = WantsConnection;
            }

            // Nothing to do before the first apply, after dispose, or when the use changed but the state did not
            // (for example the window was shown and hidden again within the grace period).
            if (settings is null || _disposed || connect == _connected)
            {
                return;
            }

            announced.AddRange(await RouteSubscriptionsAsync(WatchlistOf(settings), connect, CancellationToken.None).ConfigureAwait(false));
            snapshots = CollectHealthChanges(forceRaise: false);
        }
        catch (Exception ex) when (_disposed && ex is OperationCanceledException or ObjectDisposedException)
        {
            // The providers were disposed under the apply; nothing is left to update.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Could not update the subscriptions after the use of the data changed");
        }
        finally
        {
            _applyLock.Release();
        }

        RaiseSnapshots(snapshots);
        EnqueueStatus(announced);
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
    /// Sends every provider its instruments, or none while disconnected. Must be called while holding the apply lock.
    /// </summary>
    /// <param name="watchlist">
    /// The instruments to track.
    /// </param>
    /// <param name="connect">
    /// Whether the sources should be connected.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the operation.
    /// </param>
    /// <returns>
    /// The ids of the providers whose shown status changed, to announce once the apply lock is released.
    /// </returns>
    private async Task<List<string>> RouteSubscriptionsAsync(List<Asset> watchlist, bool connect, CancellationToken cancellationToken)
    {
        // The age of the last quote is kept across a pause, so prices from before it show as stale until new ones arrive.
        List<ProviderSlot> slots;
        lock (_gate)
        {
            slots = [.. _slots.Values];
        }

        foreach (var slot in slots)
        {
            var mine = connect ? watchlist.Where(a => a.ProviderId == slot.Id).ToArray() : [];
            await slot.Provider.SetSubscriptionsAsync(mine, cancellationToken).ConfigureAwait(false);
        }

        // Only now: if routing failed above, the next apply still sees the old state and does the work again.
        var previous = _connected;
        _connected = connect;
        List<string> announced = [];
        if (previous != connect)
        {
            LogConnectionChange(connect, first: previous is null);

            // A provider that stays idle sends no event of its own, but what it is shown as changed.
            announced.AddRange(slots.Where(slot => slot.Provider.Status.State == ProviderConnectionState.Idle).Select(slot => slot.Id));
        }

        if (connect)
        {
            SeedEmptySeries();
        }

        return announced;
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
                    status = _slots.TryGetValue(id, out var slot) ? EffectiveStatus(slot.Provider.Status) : null;
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
    /// Logs that the sources were connected or disconnected on purpose, with the reason.
    /// </summary>
    /// <param name="connected">
    /// Whether the sources are connected now.
    /// </param>
    /// <param name="first">
    /// Whether this is the first state since the start, so nothing was connected or disconnected before it.
    /// </param>
    private void LogConnectionChange(bool connected, bool first)
    {
        bool onDemand;
        int usages;
        lock (_gate)
        {
            onDemand = _onDemand;
            usages = _usageCount;
        }

        if (connected)
        {
            _logger.LogInformation(
                onDemand ? "Data sources connected: {Usages} window(s) show prices" : "Data sources connected: connecting on demand is off",
                usages);
        }
        else if (first)
        {
            _logger.LogInformation("Data sources stay disconnected: no window shows prices yet");
        }
        else
        {
            _logger.LogInformation("Data sources disconnected: no window shows prices and the grace period is over");
        }
    }

    /// <summary>
    /// Reports a provider that was disconnected on purpose as paused instead of idle.
    /// </summary>
    /// <param name="status">
    /// The status the provider reports.
    /// </param>
    /// <returns>
    /// The status to show.
    /// </returns>
    private ProviderStatus EffectiveStatus(ProviderStatus status)
    {
        lock (_gate)
        {
            return status.State == ProviderConnectionState.Idle && !WantsConnection
                ? new ProviderStatus(ProviderConnectionState.Paused)
                : status;
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
                    _slots.Remove(id);
                    retired.Add(slot);
                }
            }
        }

        foreach (var slot in retired)
        {
            await slot.DisposeAsync().ConfigureAwait(false);
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
                    _assets[asset.Id] = new AssetState(asset, new SparklineSeries(_options.SparklineCapacity, _options.SparklineBucket));
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
    private void SeedEmptySeries()
    {
        List<(AssetState State, IHistoryProvider History)> work = [];
        lock (_gate)
        {
            foreach (var state in _assets.Values)
            {
                if (!state.Seeded
                    && !state.Seeding
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
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "Could not load history for {AssetId}", state.Asset.Id);
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
    /// Applies a quote to the matching instrument and notifies listeners.
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

            state.Latest = quote;
            state.LastReceivedAt = _timeProvider.GetUtcNow();
            state.Series.Add(quote.Price, quote.Timestamp);
            snapshot = BuildSnapshot(state);
            state.LastHealth = snapshot.Health;
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
                // Only the health is needed here; the snapshot is built when the event is raised.
                var (health, _) = ComputeHealth(state);
                if (forceRaise || health != state.LastHealth)
                {
                    state.LastHealth = health;
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
                    state.LastHealth = snapshot.Health;
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
        return new AssetSnapshot(state.Asset, state.Latest, state.Series.Snapshot(), health, detail);
    }

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

        if (status.State == ProviderConnectionState.Reconnecting)
        {
            return (AssetHealth.Reconnecting, status.Message);
        }

        if (state.LastReceivedAt is null)
        {
            return (AssetHealth.Waiting, null);
        }

        return _timeProvider.GetUtcNow() - state.LastReceivedAt.Value > _options.StaleAfter
            ? (AssetHealth.Stale, null)
            : (AssetHealth.Live, null);
    }

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
    /// One held usage of the data; disposing it more than once ends it only once.
    /// </summary>
    /// <param name="owner">
    /// The service that counts the usage.
    /// </param>
    private sealed class Usage(MarketDataService owner) : IDisposable
    {
        /// <summary>
        /// Whether the usage was already ended.
        /// </summary>
        private int _disposed;

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.ReleaseUsage();
            }
        }
    }

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
        /// Gets or sets the last health that was published.
        /// </summary>
        public AssetHealth? LastHealth { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a history load is in flight.
        /// </summary>
        public bool Seeding { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the sparkline was seeded from history.
        /// </summary>
        public bool Seeded { get; set; }
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
            await Provider.DisposeAsync().ConfigureAwait(false);
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
