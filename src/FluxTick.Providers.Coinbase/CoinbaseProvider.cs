using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Coinbase;

/// <summary>
/// Streams spot ticker updates from Coinbase Exchange and reconnects with exponential backoff when the connection drops.
/// </summary>
public sealed class CoinbaseProvider : CryptoExchangeProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CoinbaseProvider"/> class.
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
    internal CoinbaseProvider(
        ProviderDescriptor descriptor,
        ExchangeEndpoints endpoints,
        HttpClient http,
        TimeProvider timeProvider,
        ILogger logger)
        : base(descriptor, endpoints, http, timeProvider, logger)
    {
    }

    /// <inheritdoc />
    protected override string SymbolsRequest => "/products";

    /// <inheritdoc />
    protected override string HistoryRequest(Asset asset) =>
        $"/products/{Uri.EscapeDataString(asset.Symbol)}/candles?granularity=60";

    /// <inheritdoc />
    protected override IReadOnlyList<PricePoint> ParseHistory(string json) => CoinbaseMessages.ParseCandles(json);

    /// <inheritdoc />
    protected override IReadOnlyList<ListedSymbol> ParseSymbols(string json) => CoinbaseMessages.ParseProducts(json);

    /// <inheritdoc />
    protected override IEnumerable<string> BuildSubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        CoinbaseMessages.ControlMessages("subscribe", assets.Select(a => a.Symbol));

    /// <inheritdoc />
    protected override IEnumerable<string> BuildUnsubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        CoinbaseMessages.ControlMessages("unsubscribe", assets.Select(a => a.Symbol));

    /// <inheritdoc />
    protected override void HandleMessage(string text)
    {
        if (CoinbaseMessages.TryParseTicker(text, Descriptor.Id, TimeProvider.GetUtcNow(), out var quote))
        {
            Publish(quote);
        }
        else if (CoinbaseMessages.TryParseError(text) is { } error)
        {
            Logger.LogWarning("Coinbase rejected a request: {Error}", error);
        }
    }
}
