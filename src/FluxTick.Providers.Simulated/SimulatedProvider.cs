using FluxTick.Abstractions;

namespace FluxTick.Providers.Simulated;

/// <summary>
/// A provider that produces a deterministic random walk per symbol. The same symbol always starts at the same price
/// and follows the same sequence, which keeps demos and tests reproducible.
/// </summary>
/// <param name="descriptor">
/// The provider descriptor.
/// </param>
/// <param name="interval">
/// How often prices are generated.
/// </param>
/// <param name="timeProvider">
/// The clock and timer source.
/// </param>
public sealed class SimulatedProvider(ProviderDescriptor descriptor, TimeSpan interval, TimeProvider timeProvider)
    : MarketDataProviderBase(descriptor, timeProvider), IHistoryProvider, ISymbolSearchProvider
{
    /// <summary>
    /// The instruments offered by <see cref="SearchAsync"/>.
    /// </summary>
    private static readonly Asset[] Catalog =
    [
        new(SimulatedProviderFactory.ProviderId, "SIM-BTC", "Simulated Bitcoin", AssetClass.Crypto),
        new(SimulatedProviderFactory.ProviderId, "SIM-ETH", "Simulated Ether", AssetClass.Crypto),
        new(SimulatedProviderFactory.ProviderId, "SIM-AAPL", "Simulated Apple", AssetClass.Equity),
        new(SimulatedProviderFactory.ProviderId, "SIM-NVDA", "Simulated Nvidia", AssetClass.Equity),
        new(SimulatedProviderFactory.ProviderId, "SIM-SPX", "Simulated S&P 500", AssetClass.Index),
        new(SimulatedProviderFactory.ProviderId, "SIM-EURUSD", "Simulated EUR/USD", AssetClass.Forex),
        new(SimulatedProviderFactory.ProviderId, "SIM-GOLD", "Simulated Gold", AssetClass.Commodity),
    ];

    /// <summary>
    /// Guards <see cref="_walks"/> and <see cref="_timer"/>.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// The random walk state per subscribed instrument.
    /// </summary>
    private readonly Dictionary<AssetId, Walk> _walks = [];

    /// <summary>
    /// The tick timer, running while at least one instrument is subscribed.
    /// </summary>
    private ITimer? _timer;

    /// <inheritdoc />
    public Task<IReadOnlyList<PricePoint>> GetHistoryAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        var walk = new Walk(asset.Symbol);
        var now = TimeProvider.GetUtcNow();
        var prices = new decimal[60];
        for (var i = prices.Length - 1; i >= 0; i--)
        {
            prices[i] = walk.Price;
            walk.Step();
        }

        IReadOnlyList<PricePoint> points = prices
            .Select((price, index) => new PricePoint(now.AddMinutes(index - prices.Length), price))
            .ToArray();
        return Task.FromResult(points);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Asset>> SearchAsync(string query, AssetClass? assetClass, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Asset> result = Catalog
            .Where(a => (assetClass is null || a.AssetClass == assetClass)
                && (a.Symbol.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || a.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    protected override Task OnSubscriptionsChangedAsync(
        IReadOnlyCollection<Asset> current,
        IReadOnlyCollection<Asset> added,
        IReadOnlyCollection<Asset> removed,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            foreach (var asset in removed)
            {
                _walks.Remove(asset.Id);
            }

            foreach (var asset in added)
            {
                _walks[asset.Id] = new Walk(asset.Symbol);
            }

            if (_walks.Count == 0)
            {
                _timer?.Dispose();
                _timer = null;
                SetStatus(ProviderConnectionState.Idle);
            }
            else if (_timer is null)
            {
                _timer = TimeProvider.CreateTimer(_ => Tick(), null, TimeSpan.Zero, interval);
                SetStatus(ProviderConnectionState.Connected);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override ValueTask DisposeCoreAsync()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Advances every walk and publishes the new prices.
    /// </summary>
    private void Tick()
    {
        var now = TimeProvider.GetUtcNow();
        lock (_gate)
        {
            foreach (var (id, walk) in _walks)
            {
                walk.Step();
                Publish(new Quote(id, walk.Price, walk.Reference, now));
            }
        }
    }

    /// <summary>
    /// A deterministic random walk seeded from a symbol.
    /// </summary>
    private sealed class Walk
    {
        /// <summary>
        /// The deterministic random source.
        /// </summary>
        private readonly Random _random;

        /// <summary>
        /// Initializes a new instance of the <see cref="Walk"/> class.
        /// </summary>
        /// <param name="symbol">
        /// The symbol that seeds the walk.
        /// </param>
        public Walk(string symbol)
        {
            var seed = StableHash(symbol);
            _random = new Random(seed);
            Reference = Round(10m + (seed & 0xFFFF) / 10m);
            Price = Reference;
        }

        /// <summary>
        /// Gets the starting price, used as the change reference.
        /// </summary>
        public decimal Reference { get; }

        /// <summary>
        /// Gets the current price.
        /// </summary>
        public decimal Price { get; private set; }

        /// <summary>
        /// Moves the price by a small random relative step.
        /// </summary>
        public void Step()
        {
            var factor = 1m + (decimal)(_random.NextDouble() - 0.5) * 0.004m;
            Price = Round(Price * factor);
        }

        /// <summary>
        /// Rounds a price to a sensible number of decimals.
        /// </summary>
        /// <param name="value">
        /// The value to round.
        /// </param>
        /// <returns>
        /// The rounded value.
        /// </returns>
        private static decimal Round(decimal value) => decimal.Round(value, value < 100m ? 4 : 2);

        /// <summary>
        /// Computes a hash that is stable across processes, unlike <see cref="string.GetHashCode()"/>.
        /// </summary>
        /// <param name="text">
        /// The text to hash.
        /// </param>
        /// <returns>
        /// A non-negative hash.
        /// </returns>
        private static int StableHash(string text)
        {
            var hash = 2166136261u;
            foreach (var c in text)
            {
                hash = (hash ^ c) * 16777619u;
            }

            return (int)(hash & 0x7FFFFFFF);
        }
    }
}
