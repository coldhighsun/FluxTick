using FluxTick.Abstractions;
using FluxTick.Core.Domain;
using Microsoft.Extensions.Logging;

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
    }

    /// <summary>
    /// Raised when the snapshot of an instrument changed. May be raised on any thread.
    /// </summary>
    public event EventHandler<AssetSnapshot>? SnapshotChanged;

    /// <summary>
    /// Raised when a provider's connection status changed. May be raised on any thread.
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

        return search is null
            ? []
            : await search.SearchAsync(query, assetClass, cancellationToken).ConfigureAwait(false);
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
        await _applyLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var desired = new Dictionary<string, IReadOnlyDictionary<string, string>>();
            foreach (var descriptor in _registry.Descriptors.Where(settings.IsProviderEnabled))
            {
                desired[descriptor.Id] = EffectiveOptions(descriptor, settings.Providers.GetValueOrDefault(descriptor.Id)?.Options);
            }

            await RetireProvidersAsync(desired).ConfigureAwait(false);
            StartMissingProviders(desired);

            var watchlist = settings.Watchlist.Select(e => e.ToAsset()).DistinctBy(a => a.Id).ToList();
            UpdateAssets(watchlist);

            List<ProviderSlot> slots;
            lock (_gate)
            {
                slots = [.. _slots.Values];
            }

            foreach (var slot in slots)
            {
                var mine = watchlist.Where(a => a.ProviderId == slot.Id).ToArray();
                await slot.Provider.SetSubscriptionsAsync(mine, cancellationToken).ConfigureAwait(false);
            }

            SeedEmptySeries();
            RefreshHealth(forceRaise: true);
        }
        finally
        {
            _applyLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _healthTimer.DisposeAsync().ConfigureAwait(false);
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
    private void StartMissingProviders(Dictionary<string, IReadOnlyDictionary<string, string>> desired)
    {
        foreach (var (id, options) in desired)
        {
            lock (_gate)
            {
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
                provider.StatusChanged += (_, status) => OnProviderStatusChanged(id, status);
                slot.Pump = Task.Run(() => PumpAsync(slot));
                lock (_gate)
                {
                    _slots[id] = slot;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogError(ex, "Failed to create provider {ProviderId}", id);
            }
        }
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
    /// Starts a background history load for every instrument whose sparkline is still empty.
    /// </summary>
    private void SeedEmptySeries()
    {
        List<(AssetState State, IHistoryProvider History)> work = [];
        lock (_gate)
        {
            foreach (var state in _assets.Values)
            {
                if (state.Series.Count == 0
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
            SnapshotChanged?.Invoke(this, snapshot);
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

        SnapshotChanged?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Re-evaluates the health of all instruments and raises events for those that changed.
    /// </summary>
    /// <param name="forceRaise">
    /// Raise an event for every instrument, even when its health did not change.
    /// </param>
    private void RefreshHealth(bool forceRaise = false)
    {
        List<AssetSnapshot> changed = [];
        lock (_gate)
        {
            foreach (var state in _assets.Values)
            {
                var snapshot = BuildSnapshot(state);
                if (forceRaise || snapshot.Health != state.LastHealth)
                {
                    state.LastHealth = snapshot.Health;
                    changed.Add(snapshot);
                }
            }
        }

        foreach (var snapshot in changed)
        {
            SnapshotChanged?.Invoke(this, snapshot);
        }
    }

    /// <summary>
    /// Handles a provider status change by updating health and forwarding the event.
    /// </summary>
    /// <param name="providerId">
    /// The provider id.
    /// </param>
    /// <param name="status">
    /// The new status.
    /// </param>
    private void OnProviderStatusChanged(string providerId, ProviderStatus status)
    {
        ProviderStatusChanged?.Invoke(this, new ProviderStatusChangedEventArgs(providerId, status));
        RefreshHealth();
    }

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
