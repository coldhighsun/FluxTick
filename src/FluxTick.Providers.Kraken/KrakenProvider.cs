using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Kraken;

/// <summary>
/// Streams spot ticker updates from Kraken and reconnects with exponential backoff when the connection drops.
/// </summary>
public sealed class KrakenProvider : CryptoExchangeProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KrakenProvider"/> class.
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
    internal KrakenProvider(
        ProviderDescriptor descriptor,
        ExchangeEndpoints endpoints,
        HttpClient http,
        TimeProvider timeProvider,
        ILogger logger)
        : base(descriptor, endpoints, http, timeProvider, logger)
    {
    }

    /// <inheritdoc />
    protected override string SymbolsRequest => "/0/public/AssetPairs";

    /// <inheritdoc />
    protected override string HistoryRequest(Asset asset) =>
        // Without a start the service answers with its last 720 candles, twelve times what the sparkline shows.
        $"/0/public/OHLC?pair={Uri.EscapeDataString(asset.Symbol)}&interval=1&since={(TimeProvider.GetUtcNow() - HistoryWindow.Span).ToUnixTimeSeconds()}";

    /// <inheritdoc />
    protected override IReadOnlyList<PricePoint> ParseHistory(string json) => KrakenMessages.ParseOhlc(json);

    /// <inheritdoc />
    protected override IReadOnlyList<ListedSymbol> ParseSymbols(string json) => KrakenMessages.ParsePairs(json);

    /// <inheritdoc />
    protected override IEnumerable<string> BuildSubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        KrakenMessages.ControlMessages("subscribe", assets.Select(a => a.Symbol));

    /// <inheritdoc />
    protected override IEnumerable<string> BuildUnsubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        KrakenMessages.ControlMessages("unsubscribe", assets.Select(a => a.Symbol));

    /// <inheritdoc />
    protected override void HandleMessage(string text)
    {
        var message = KrakenMessages.Parse(text, Descriptor.Id, TimeProvider.GetUtcNow());
        foreach (var quote in message.Quotes)
        {
            Publish(quote);
        }

        if (message.Error is { } error)
        {
            Logger.LogWarning("Kraken rejected a request: {Error}", error);
        }
    }
}
