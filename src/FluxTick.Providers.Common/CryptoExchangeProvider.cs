using FluxTick.Abstractions;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Common;

/// <summary>
/// Base class for crypto exchange sources that stream tickers over a WebSocket and use REST for one-minute history
/// and for the list of tradable pairs behind symbol search. Derived classes describe the exchange's wire formats.
/// </summary>
public abstract class CryptoExchangeProvider : WebSocketProviderBase, IHistoryProvider, ISymbolSearchProvider
{
    /// <summary>
    /// How long a downloaded pair list stays valid.
    /// </summary>
    private static readonly TimeSpan SymbolCacheLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The REST endpoint.
    /// </summary>
    private readonly Uri _restBase;

    /// <summary>
    /// The HTTP client used for REST calls; owned by the provider.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// The cached pair list.
    /// </summary>
    private readonly ExpiringCache<SymbolIndex> _symbols;

    /// <summary>
    /// Initializes a new instance of the <see cref="CryptoExchangeProvider"/> class.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    /// <param name="endpoints">
    /// The exchange addresses.
    /// </param>
    /// <param name="http">
    /// The HTTP client for REST calls; the provider takes ownership.
    /// </param>
    /// <param name="timeProvider">
    /// The clock.
    /// </param>
    /// <param name="logger">
    /// The logger.
    /// </param>
    protected CryptoExchangeProvider(
        ProviderDescriptor descriptor,
        ExchangeEndpoints endpoints,
        HttpClient http,
        TimeProvider timeProvider,
        ILogger logger)
        : base(descriptor, (endpoints ?? throw new ArgumentNullException(nameof(endpoints))).WebSocket, timeProvider, logger)
    {
        _restBase = endpoints.Rest;
        _http = http;
        _symbols = new ExpiringCache<SymbolIndex>(SymbolCacheLifetime, timeProvider);
    }

    /// <summary>
    /// Gets the path and query, relative to the REST endpoint, of the request for an instrument's recent
    /// one-minute candles.
    /// </summary>
    /// <param name="asset">
    /// The instrument.
    /// </param>
    /// <returns>
    /// The request, starting with <c>/</c>.
    /// </returns>
    protected abstract string HistoryRequest(Asset asset);

    /// <summary>
    /// Gets the path and query, relative to the REST endpoint, of the request that lists the tradable pairs.
    /// </summary>
    protected abstract string SymbolsRequest { get; }

    /// <summary>
    /// Parses the response to <see cref="HistoryRequest"/>.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The close prices, oldest first.
    /// </returns>
    protected abstract IReadOnlyList<PricePoint> ParseHistory(string json);

    /// <summary>
    /// Parses the response to <see cref="SymbolsRequest"/>.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The tradable pairs.
    /// </returns>
    protected abstract IReadOnlyList<ListedSymbol> ParseSymbols(string json);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PricePoint>> GetHistoryAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var json = await _http.GetStringAsync(EndpointOptions.Combine(_restBase, HistoryRequest(asset)), cancellationToken).ConfigureAwait(false);
        return ParseHistory(json);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Asset>> SearchAsync(string query, AssetClass? assetClass, CancellationToken cancellationToken = default)
    {
        if (assetClass is not (null or AssetClass.Crypto) || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var index = await _symbols.GetAsync(LoadSymbolsAsync, cancellationToken).ConfigureAwait(false);
        return SymbolSearch.Search(index, query, Descriptor.Id);
    }

    /// <inheritdoc />
    protected override ValueTask DisposeResourcesAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Downloads the list of tradable pairs and prepares it for searching.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token to cancel the request.
    /// </param>
    /// <returns>
    /// The prepared listing.
    /// </returns>
    private async Task<SymbolIndex> LoadSymbolsAsync(CancellationToken cancellationToken)
    {
        var json = await _http.GetStringAsync(EndpointOptions.Combine(_restBase, SymbolsRequest), cancellationToken).ConfigureAwait(false);
        return new SymbolIndex(ParseSymbols(json));
    }
}
