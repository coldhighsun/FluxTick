using FluxTick.Abstractions;
using FluxTick.App.Services;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.App.Tests;

/// <summary>
/// Runs UI actions inline so tests need no UI thread. Actions run one at a time, like on a real UI thread, because the
/// service raises its events from its own thread.
/// </summary>
public sealed class InlineDispatcher : IUiDispatcher
{
    /// <summary>
    /// Serializes the actions.
    /// </summary>
    private readonly object _gate = new();

    /// <inheritdoc />
    public void Post(Action action)
    {
        lock (_gate)
        {
            action();
        }
    }

    /// <inheritdoc />
    public void PostBackground(Action action) => Post(action);
}

/// <summary>
/// A settings store that keeps the settings in memory.
/// </summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    /// <summary>
    /// Gets or sets the stored settings.
    /// </summary>
    public AppSettings? Stored { get; set; }

    /// <inheritdoc />
    public Task<AppSettings?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Stored?.Clone());

    /// <inheritdoc />
    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Stored = settings.Clone();
        if (SaveGate is { } gate)
        {
            await gate.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Gets or sets a task every save waits for, which simulates a save that takes a while.
    /// </summary>
    public Task? SaveGate { get; set; }
}

/// <summary>
/// A controllable provider.
/// </summary>
/// <param name="descriptor">
/// The provider descriptor.
/// </param>
public sealed class FakeProvider(ProviderDescriptor descriptor)
    : MarketDataProviderBase(descriptor, TimeProvider.System), ISymbolSearchProvider
{
    /// <summary>
    /// Gets or sets the assets returned by <see cref="SearchAsync"/>.
    /// </summary>
    public IReadOnlyList<Asset> SearchResults { get; set; } = [];

    /// <summary>
    /// Gets the subscription set after the latest change.
    /// </summary>
    public IReadOnlyCollection<Asset> Subscribed => CurrentSubscriptions;

    /// <summary>
    /// Publishes a quote as if it arrived from the data source.
    /// </summary>
    /// <param name="quote">
    /// The quote to publish.
    /// </param>
    public void Emit(Quote quote) => Publish(quote);

    /// <summary>
    /// Changes the connection status as if the data source connected, failed or reconnected.
    /// </summary>
    /// <param name="state">
    /// The new connection state.
    /// </param>
    /// <param name="message">
    /// An optional detail message.
    /// </param>
    public void ChangeStatus(ProviderConnectionState state, string? message = null) => SetStatus(state, message);

    /// <inheritdoc />
    public Task<IReadOnlyList<Asset>> SearchAsync(string query, AssetClass? assetClass, CancellationToken cancellationToken = default) =>
        Task.FromResult(SearchResults);

    /// <inheritdoc />
    protected override Task OnSubscriptionsChangedAsync(
        IReadOnlyCollection<Asset> current,
        IReadOnlyCollection<Asset> added,
        IReadOnlyCollection<Asset> removed,
        CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// A factory for <see cref="FakeProvider"/>.
/// </summary>
public sealed class FakeFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FakeFactory"/> class.
    /// </summary>
    /// <param name="id">
    /// The provider id.
    /// </param>
    /// <param name="classes">
    /// The supported asset classes.
    /// </param>
    /// <param name="searchable">
    /// Whether the provider supports search.
    /// </param>
    public FakeFactory(string id, AssetClass[] classes, bool searchable = true)
    {
        Searchable = searchable;
        Descriptor = new ProviderDescriptor(
            id,
            id.ToUpperInvariant(),
            "Test",
            classes,
            ProviderUpdateMode.Streaming,
            Options:
            [
                new ProviderOptionDefinition("Interval", "Interval", ProviderOptionType.Duration, "00:00:05", Min: 2, Max: 60),
                new ProviderOptionDefinition("Url", "Url", ProviderOptionType.String, "https://a"),
            ]);
    }

    /// <summary>
    /// Gets the most recently created provider.
    /// </summary>
    public IMarketDataProvider? Last { get; private set; }

    /// <summary>
    /// Gets the most recently created provider as a <see cref="FakeProvider"/>.
    /// </summary>
    public FakeProvider Fake => (FakeProvider)Last!;

    /// <summary>
    /// Gets a value indicating whether created providers support search.
    /// </summary>
    private bool Searchable { get; }

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; }

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context)
    {
        IMarketDataProvider provider = Searchable ? new FakeProvider(Descriptor) : new NoSearchProvider(Descriptor);
        Last = provider;
        return provider;
    }

    /// <summary>
    /// A provider that does not implement search, by hiding the interface behind a wrapper type.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    private sealed class NoSearchProvider(ProviderDescriptor descriptor) : MarketDataProviderBase(descriptor, TimeProvider.System)
    {
        /// <inheritdoc />
        protected override Task OnSubscriptionsChangedAsync(
            IReadOnlyCollection<Asset> current,
            IReadOnlyCollection<Asset> added,
            IReadOnlyCollection<Asset> removed,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

/// <summary>
/// Builds the full view model graph over fake providers and an in-memory store.
/// </summary>
public sealed class Harness : IAsyncDisposable
{
    /// <summary>
    /// The crypto provider id.
    /// </summary>
    public const string Crypto = "crypto";

    /// <summary>
    /// The stocks provider id.
    /// </summary>
    public const string Stocks = "stocks";

    /// <summary>
    /// The no-search provider id.
    /// </summary>
    public const string Plain = "plain";

    /// <summary>
    /// Initializes a new instance of the <see cref="Harness"/> class.
    /// </summary>
    /// <param name="settings">
    /// The initial settings; defaults to an empty watchlist.
    /// </param>
    /// <param name="autoStart">
    /// The start at sign-in registration; defaults to none.
    /// </param>
    /// <param name="trayProbe">
    /// Tells whether the desktop shows tray icons; defaults to yes, so no test asks the real desktop.
    /// </param>
    public Harness(AppSettings? settings = null, IAutoStartService? autoStart = null, Func<bool>? trayProbe = null)
    {
        CryptoFactory = new FakeFactory(Crypto, [AssetClass.Crypto]);
        StocksFactory = new FakeFactory(Stocks, [AssetClass.Equity, AssetClass.Index]);
        PlainFactory = new FakeFactory(Plain, [AssetClass.Equity], searchable: false);
        var registry = new ProviderRegistry([CryptoFactory, StocksFactory, PlainFactory]);
        Router = new ProviderRouter(registry);
        Store = new InMemorySettingsStore { Stored = settings ?? new AppSettings() };
        Settings = new SettingsManager(Store, NullLogger<SettingsManager>.Instance);
        Settings.LoadAsync(new AppSettings()).GetAwaiter().GetResult();
        Market = new MarketDataService(registry, NullLoggerFactory.Instance, Time);

        var ui = new InlineDispatcher();
        AddAsset = new AddAssetViewModel(Settings, Market, Router, ui, Time, NullLogger<AddAssetViewModel>.Instance);
        SettingsPage = new SettingsViewModel(Settings, registry, Router, Market, ui, Time, autoStart: autoStart, trayProbe: trayProbe ?? (() => true));
        Alerts = new AlertsViewModel(Settings, registry, ui);
        Holding = new HoldingViewModel(Settings, ui);
        Rates = new ExchangeRateService(new FixedRates(), NullLogger<ExchangeRateService>.Instance, Time, new ExchangeRateOptions());
        Main = new MainViewModel(Settings, Market, registry, ui, AddAsset, SettingsPage, Alerts, Holding, Rates, NullLogger<MainViewModel>.Instance);
    }

    /// <summary>
    /// Gets the price alerts view model of the main window.
    /// </summary>
    public AlertsViewModel Alerts { get; }

    /// <summary>
    /// Gets the holding view model of the main window.
    /// </summary>
    public HoldingViewModel Holding { get; }

    /// <summary>
    /// Gets the exchange rate service, which fetches fixed rates when told to.
    /// </summary>
    public ExchangeRateService Rates { get; }

    /// <summary>
    /// Gets the clock.
    /// </summary>
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Gets the crypto factory.
    /// </summary>
    public FakeFactory CryptoFactory { get; }

    /// <summary>
    /// Gets the stocks factory.
    /// </summary>
    public FakeFactory StocksFactory { get; }

    /// <summary>
    /// Gets the factory of the provider without search.
    /// </summary>
    public FakeFactory PlainFactory { get; }

    /// <summary>
    /// Gets the router.
    /// </summary>
    public ProviderRouter Router { get; }

    /// <summary>
    /// Gets the store.
    /// </summary>
    public InMemorySettingsStore Store { get; }

    /// <summary>
    /// Gets the settings manager.
    /// </summary>
    public SettingsManager Settings { get; }

    /// <summary>
    /// Gets the market data service.
    /// </summary>
    public MarketDataService Market { get; }

    /// <summary>
    /// Gets the add instrument page.
    /// </summary>
    public AddAssetViewModel AddAsset { get; }

    /// <summary>
    /// Gets the settings page.
    /// </summary>
    public SettingsViewModel SettingsPage { get; }

    /// <summary>
    /// Gets the root view model.
    /// </summary>
    public MainViewModel Main { get; }

    /// <summary>
    /// Creates settings with the given instruments on the watchlist.
    /// </summary>
    /// <param name="assets">
    /// The instruments to track.
    /// </param>
    /// <returns>
    /// The settings.
    /// </returns>
    public static AppSettings WatchlistOf(params Asset[] assets)
    {
        var settings = new AppSettings();
        settings.Watchlist.AddRange(assets.Select(WatchlistEntry.From));
        return settings;
    }

    /// <summary>
    /// Waits until a condition holds, failing on timeout.
    /// </summary>
    /// <param name="condition">
    /// The condition to wait for.
    /// </param>
    /// <returns>
    /// A task that completes when the condition holds.
    /// </returns>
    public static async Task EventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(10);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Main.Dispose();
        SettingsPage.Dispose();
        AddAsset.Dispose();
        return Market.DisposeAsync();
    }
}

/// <summary>
/// A rate source with a fixed table: one US dollar buys 7 yuan.
/// </summary>
public sealed class FixedRates : IExchangeRateProvider
{
    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, decimal>> GetRatesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, decimal>>(new Dictionary<string, decimal> { ["CNY"] = 7m });
}
