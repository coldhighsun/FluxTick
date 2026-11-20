using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.GateIo;

/// <summary>
/// Streams spot ticker updates from Gate.io and reconnects with exponential backoff when the connection drops.
/// </summary>
public sealed class GateIoProvider : CryptoExchangeProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GateIoProvider"/> class.
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
    internal GateIoProvider(
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
    protected override string? HeartbeatMessage => GateIoMessages.Ping;

    /// <inheritdoc />
    protected override string SymbolsRequest => "/api/v4/spot/currency_pairs";

    /// <inheritdoc />
    protected override string HistoryRequest(Asset asset) =>
        $"/api/v4/spot/candlesticks?currency_pair={Uri.EscapeDataString(asset.Symbol)}&interval=1m&limit=60";

    /// <inheritdoc />
    public override bool SupportsRange(HistoryRange range) => true;

    /// <inheritdoc />
    protected override string HistoryRequest(Asset asset, HistoryRange range) =>
        $"/api/v4/spot/candlesticks?currency_pair={Uri.EscapeDataString(asset.Symbol)}&interval={IntervalOf(range)}&limit={HistoryRangeSpec.For(range).Capacity}";

    /// <summary>
    /// Names the candle width of a range the way the candlesticks endpoint takes it.
    /// </summary>
    /// <param name="range">
    /// The range.
    /// </param>
    /// <returns>
    /// The interval, whose width is the bucket of the range.
    /// </returns>
    private static string IntervalOf(HistoryRange range) => range switch
    {
        HistoryRange.Day => "5m",
        HistoryRange.FiveDays => "30m",
        HistoryRange.Month => "4h",
        HistoryRange.HalfYear or HistoryRange.Year => "1d",
        _ => "1m",
    };

    /// <inheritdoc />
    protected override IReadOnlyList<PricePoint> ParseHistory(string json) => GateIoMessages.ParseCandles(json);

    /// <inheritdoc />
    protected override IReadOnlyList<ListedSymbol> ParseSymbols(string json) => GateIoMessages.ParsePairs(json);

    /// <inheritdoc />
    protected override IEnumerable<string> BuildSubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        GateIoMessages.ControlMessages("subscribe", assets.Select(a => a.Symbol), TimeProvider.GetUtcNow());

    /// <inheritdoc />
    protected override IEnumerable<string> BuildUnsubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        GateIoMessages.ControlMessages("unsubscribe", assets.Select(a => a.Symbol), TimeProvider.GetUtcNow());

    /// <inheritdoc />
    protected override void HandleMessage(string text)
    {
        if (GateIoMessages.TryParseTicker(text, Descriptor.Id, TimeProvider.GetUtcNow(), out var quote))
        {
            Publish(quote);
        }
        else if (GateIoMessages.TryParseError(text) is { } error)
        {
            Logger.LogWarning("Gate.io rejected a request: {Error}", error);
        }
    }
}
