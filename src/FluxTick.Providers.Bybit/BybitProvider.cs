using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Bybit;

/// <summary>
/// Streams spot ticker updates from Bybit and reconnects with exponential backoff when the connection drops.
/// </summary>
public sealed class BybitProvider : CryptoExchangeProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BybitProvider"/> class.
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
    internal BybitProvider(
        ProviderDescriptor descriptor,
        ExchangeEndpoints endpoints,
        HttpClient http,
        TimeProvider timeProvider,
        ILogger logger)
        : base(descriptor, endpoints, http, timeProvider, logger)
    {
    }

    /// <inheritdoc />
    protected override TimeSpan? HeartbeatInterval => TimeSpan.FromSeconds(20);

    /// <inheritdoc />
    protected override string? HeartbeatMessage => BybitMessages.Ping;

    /// <inheritdoc />
    protected override string SymbolsRequest => "/v5/market/instruments-info?category=spot&limit=1000";

    /// <inheritdoc />
    protected override string HistoryRequest(Asset asset) =>
        $"/v5/market/kline?category=spot&symbol={Uri.EscapeDataString(asset.Symbol)}&interval=1&limit=60";

    /// <inheritdoc />
    protected override IReadOnlyList<PricePoint> ParseHistory(string json) => BybitMessages.ParseKlines(json);

    /// <inheritdoc />
    protected override IReadOnlyList<ListedSymbol> ParseSymbols(string json) => BybitMessages.ParseInstruments(json);

    /// <inheritdoc />
    protected override IEnumerable<string> BuildSubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        BybitMessages.ControlMessages("subscribe", assets.Select(a => a.Symbol));

    /// <inheritdoc />
    protected override IEnumerable<string> BuildUnsubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        BybitMessages.ControlMessages("unsubscribe", assets.Select(a => a.Symbol));

    /// <inheritdoc />
    protected override void HandleMessage(string text)
    {
        if (BybitMessages.TryParseTicker(text, Descriptor.Id, TimeProvider.GetUtcNow(), out var quote))
        {
            Publish(quote);
        }
        else if (BybitMessages.TryParseError(text) is { } error)
        {
            Logger.LogWarning("Bybit rejected a request: {Error}", error);
        }
    }
}
