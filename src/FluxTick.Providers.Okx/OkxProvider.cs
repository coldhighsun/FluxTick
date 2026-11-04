using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Okx;

/// <summary>
/// Streams spot ticker updates from OKX and reconnects with exponential backoff when the connection drops.
/// </summary>
public sealed class OkxProvider : CryptoExchangeProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OkxProvider"/> class.
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
    internal OkxProvider(
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
    protected override string? HeartbeatMessage => OkxMessages.Ping;

    /// <inheritdoc />
    protected override string SymbolsRequest => "/api/v5/public/instruments?instType=SPOT";

    /// <inheritdoc />
    protected override string HistoryRequest(Asset asset) =>
        $"/api/v5/market/candles?instId={Uri.EscapeDataString(asset.Symbol)}&bar=1m&limit=60";

    /// <inheritdoc />
    public override bool SupportsRange(HistoryRange range) => true;

    /// <inheritdoc />
    protected override string HistoryRequest(Asset asset, HistoryRange range) =>
        // The candles endpoint answers with 300 candles at most, which leaves a year of days short by two months.
        $"/api/v5/market/candles?instId={Uri.EscapeDataString(asset.Symbol)}&bar={BarOf(range)}&limit={Math.Min(HistoryRangeSpec.For(range).Capacity, MaxCandles)}";

    /// <summary>
    /// The most candles the candles endpoint answers with.
    /// </summary>
    private const int MaxCandles = 300;

    /// <summary>
    /// Names the candle width of a range the way the candles endpoint takes it.
    /// </summary>
    /// <param name="range">
    /// The range.
    /// </param>
    /// <returns>
    /// The bar, whose width is the bucket of the range.
    /// </returns>
    private static string BarOf(HistoryRange range) => range switch
    {
        HistoryRange.Day => "5m",
        HistoryRange.FiveDays => "30m",
        HistoryRange.Month => "4H",

        // The plain 1D candle starts at midnight in UTC+8, a day before the UTC day the chart dates its points by.
        HistoryRange.HalfYear or HistoryRange.Year => "1Dutc",
        _ => "1m",
    };

    /// <inheritdoc />
    protected override IReadOnlyList<PricePoint> ParseHistory(string json) => OkxMessages.ParseCandles(json);

    /// <inheritdoc />
    protected override IReadOnlyList<ListedSymbol> ParseSymbols(string json) => OkxMessages.ParseInstruments(json);

    /// <inheritdoc />
    protected override IEnumerable<string> BuildSubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        OkxMessages.ControlMessages("subscribe", assets.Select(a => a.Symbol));

    /// <inheritdoc />
    protected override IEnumerable<string> BuildUnsubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        OkxMessages.ControlMessages("unsubscribe", assets.Select(a => a.Symbol));

    /// <inheritdoc />
    protected override void HandleMessage(string text)
    {
        var message = OkxMessages.Parse(text, Descriptor.Id, TimeProvider.GetUtcNow());
        foreach (var quote in message.Quotes)
        {
            Publish(quote);
        }

        if (message.Error is { } error)
        {
            Logger.LogWarning("OKX rejected a request: {Error}", error);
        }
    }
}
