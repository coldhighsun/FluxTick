using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.CryptoCom;

/// <summary>
/// Streams spot ticker updates from the Crypto.com Exchange and reconnects with exponential backoff when the
/// connection drops.
/// </summary>
public sealed class CryptoComProvider : CryptoExchangeProvider
{
    /// <summary>
    /// The most candles one request returns.
    /// </summary>
    private const int MaxCandles = 300;

    /// <summary>
    /// Initializes a new instance of the <see cref="CryptoComProvider"/> class.
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
    internal CryptoComProvider(
        ProviderDescriptor descriptor,
        ExchangeEndpoints endpoints,
        HttpClient http,
        TimeProvider timeProvider,
        ILogger logger)
        : base(descriptor, endpoints, http, timeProvider, logger)
    {
    }

    /// <inheritdoc />
    protected override TimeSpan SubscribeDelay => TimeSpan.FromSeconds(1);

    /// <inheritdoc />
    protected override string SymbolsRequest => "/exchange/v1/public/get-instruments";

    /// <inheritdoc />
    protected override string HistoryRequest(Asset asset) =>
        $"/exchange/v1/public/get-candlestick?instrument_name={Uri.EscapeDataString(asset.Symbol)}&timeframe=1m&count=60";

    /// <inheritdoc />
    public override bool SupportsRange(HistoryRange range) => true;

    /// <inheritdoc />
    protected override string HistoryRequest(Asset asset, HistoryRange range) =>
        $"/exchange/v1/public/get-candlestick?instrument_name={Uri.EscapeDataString(asset.Symbol)}&timeframe={TimeframeOf(range)}&count={Math.Min(HistoryRangeSpec.For(range).Capacity, MaxCandles)}";

    /// <summary>
    /// Names the candle width of a range the way the candlestick endpoint takes it.
    /// </summary>
    /// <param name="range">
    /// The range.
    /// </param>
    /// <returns>
    /// The timeframe, whose width is the bucket of the range.
    /// </returns>
    private static string TimeframeOf(HistoryRange range) => range switch
    {
        HistoryRange.Day => "5m",
        HistoryRange.FiveDays => "30m",
        HistoryRange.Month => "4h",
        HistoryRange.HalfYear or HistoryRange.Year => "1D",
        _ => "1m",
    };

    /// <inheritdoc />
    protected override IReadOnlyList<PricePoint> ParseHistory(string json) => CryptoComMessages.ParseCandles(json);

    /// <inheritdoc />
    protected override IReadOnlyList<ListedSymbol> ParseSymbols(string json) => CryptoComMessages.ParseInstruments(json);

    /// <inheritdoc />
    protected override IEnumerable<string> BuildSubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        CryptoComMessages.ControlMessages("subscribe", assets.Select(a => a.Symbol));

    /// <inheritdoc />
    protected override IEnumerable<string> BuildUnsubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        CryptoComMessages.ControlMessages("unsubscribe", assets.Select(a => a.Symbol));

    /// <inheritdoc />
    protected override string? ReplyTo(string text) => CryptoComMessages.HeartbeatReply(text);

    /// <inheritdoc />
    protected override void HandleMessage(string text)
    {
        if (CryptoComMessages.TryParseTicker(text, Descriptor.Id, TimeProvider.GetUtcNow(), out var quote))
        {
            Publish(quote);
        }
        else if (CryptoComMessages.TryParseError(text) is { } error)
        {
            Logger.LogWarning("Crypto.com rejected a request: {Error}", error);
        }
    }
}
