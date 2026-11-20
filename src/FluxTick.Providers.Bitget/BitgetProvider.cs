using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Bitget;

/// <summary>
/// Streams spot ticker updates from Bitget and reconnects with exponential backoff when the connection drops.
/// </summary>
public sealed class BitgetProvider : CryptoExchangeProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BitgetProvider"/> class.
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
    internal BitgetProvider(
        ProviderDescriptor descriptor,
        ExchangeEndpoints endpoints,
        HttpClient http,
        TimeProvider timeProvider,
        ILogger logger)
        : base(descriptor, endpoints, http, timeProvider, logger)
    {
    }

    /// <inheritdoc />
    protected override TimeSpan? HeartbeatInterval => TimeSpan.FromSeconds(25);

    /// <inheritdoc />
    protected override string? HeartbeatMessage => BitgetMessages.Ping;

    /// <inheritdoc />
    protected override string SymbolsRequest => "/api/v2/spot/public/symbols";

    /// <inheritdoc />
    protected override string HistoryRequest(Asset asset) =>
        $"/api/v2/spot/market/candles?symbol={Uri.EscapeDataString(asset.Symbol)}&granularity=1min&limit=60";

    /// <inheritdoc />
    public override bool SupportsRange(HistoryRange range) => true;

    /// <inheritdoc />
    protected override string HistoryRequest(Asset asset, HistoryRange range) =>
        $"/api/v2/spot/market/candles?symbol={Uri.EscapeDataString(asset.Symbol)}&granularity={GranularityOf(range)}&limit={HistoryRangeSpec.For(range).Capacity}";

    /// <summary>
    /// Names the candle width of a range the way the candles endpoint takes it.
    /// </summary>
    /// <param name="range">
    /// The range.
    /// </param>
    /// <returns>
    /// The granularity, whose width is the bucket of the range.
    /// </returns>
    private static string GranularityOf(HistoryRange range) => range switch
    {
        HistoryRange.Day => "5min",
        HistoryRange.FiveDays => "30min",
        HistoryRange.Month => "4h",
        HistoryRange.HalfYear or HistoryRange.Year => "1day",
        _ => "1min",
    };

    /// <inheritdoc />
    protected override IReadOnlyList<PricePoint> ParseHistory(string json) => BitgetMessages.ParseCandles(json);

    /// <inheritdoc />
    protected override IReadOnlyList<ListedSymbol> ParseSymbols(string json) => BitgetMessages.ParseSymbols(json);

    /// <inheritdoc />
    protected override IEnumerable<string> BuildSubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        BitgetMessages.ControlMessages("subscribe", assets.Select(a => a.Symbol));

    /// <inheritdoc />
    protected override IEnumerable<string> BuildUnsubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        BitgetMessages.ControlMessages("unsubscribe", assets.Select(a => a.Symbol));

    /// <inheritdoc />
    protected override void HandleMessage(string text)
    {
        if (text == BitgetMessages.Pong)
        {
            return;
        }

        if (BitgetMessages.TryParseTicker(text, Descriptor.Id, TimeProvider.GetUtcNow(), out var quote))
        {
            Publish(quote);
        }
        else if (BitgetMessages.TryParseError(text) is { } error)
        {
            Logger.LogWarning("Bitget rejected a request: {Error}", error);
        }
    }
}
