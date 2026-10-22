namespace FluxTick.Abstractions;

/// <summary>
/// A source of live quotes. Implementations start their network activity lazily, on the first subscription.
/// Derive from <see cref="MarketDataProviderBase"/> instead of implementing this interface directly.
/// </summary>
public interface IMarketDataProvider : IAsyncDisposable
{
    /// <summary>
    /// Gets the self-description of the provider.
    /// </summary>
    ProviderDescriptor Descriptor { get; }

    /// <summary>
    /// Gets the current connection status.
    /// </summary>
    ProviderStatus Status { get; }

    /// <summary>
    /// Raised when <see cref="Status"/> changes. May be raised on any thread.
    /// </summary>
    event EventHandler<ProviderStatus>? StatusChanged;

    /// <summary>
    /// Replaces the full set of instruments the provider should deliver quotes for.
    /// </summary>
    /// <param name="assets">
    /// The instruments to subscribe to; instruments of other providers are ignored.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the operation.
    /// </param>
    /// <returns>
    /// A task that completes once the subscription set is applied.
    /// </returns>
    Task SetSubscriptionsAsync(IReadOnlyCollection<Asset> assets, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the quotes produced for the current subscriptions until the provider is disposed or the token is cancelled.
    /// Intended to be consumed by a single reader.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token to stop reading.
    /// </param>
    /// <returns>
    /// The stream of quotes.
    /// </returns>
    IAsyncEnumerable<Quote> ReadQuotesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional capability: provides recent price history to seed sparklines before live data accumulates.
/// </summary>
public interface IHistoryProvider
{
    /// <summary>
    /// Gets recent price samples, oldest first.
    /// </summary>
    /// <param name="asset">
    /// The instrument to load history for.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the operation.
    /// </param>
    /// <returns>
    /// The samples, possibly empty.
    /// </returns>
    Task<IReadOnlyList<PricePoint>> GetHistoryAsync(Asset asset, CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional capability: searches the provider's instrument catalog so users can pick a valid symbol.
/// </summary>
public interface ISymbolSearchProvider
{
    /// <summary>
    /// Searches for instruments.
    /// </summary>
    /// <param name="query">
    /// The text typed by the user.
    /// </param>
    /// <param name="assetClass">
    /// Restricts results to one asset class, or <see langword="null"/> for any.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the operation.
    /// </param>
    /// <returns>
    /// Matching instruments, best match first.
    /// </returns>
    Task<IReadOnlyList<Asset>> SearchAsync(string query, AssetClass? assetClass, CancellationToken cancellationToken = default);
}

/// <summary>
/// Everything a factory needs to build a provider.
/// </summary>
/// <param name="Options">
/// The effective option values.
/// </param>
/// <param name="LoggerFactory">
/// The logger factory.
/// </param>
/// <param name="TimeProvider">
/// The clock to use for timestamps and timers.
/// </param>
public sealed record ProviderCreationContext(
    ProviderOptions Options,
    Microsoft.Extensions.Logging.ILoggerFactory LoggerFactory,
    TimeProvider TimeProvider);

/// <summary>
/// Creates provider instances. Register one implementation per data source; this is the only extension point
/// the application needs.
/// </summary>
public interface IMarketDataProviderFactory
{
    /// <summary>
    /// Gets the self-description shared by all instances.
    /// </summary>
    ProviderDescriptor Descriptor { get; }

    /// <summary>
    /// Creates a provider instance. Must not perform network I/O.
    /// </summary>
    /// <param name="context">
    /// The options, logging and clock for the new instance.
    /// </param>
    /// <returns>
    /// The new provider.
    /// </returns>
    IMarketDataProvider Create(ProviderCreationContext context);
}
