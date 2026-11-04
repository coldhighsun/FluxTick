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
    protected override string HistoryRequest(Asset asset)
    {
        // Without a start and an end the service answers with its last 300 candles, five times what the sparkline shows.
        var end = TimeProvider.GetUtcNow();
        return $"/products/{Uri.EscapeDataString(asset.Symbol)}/candles?granularity=60&start={Iso(end - HistoryWindow.Span)}&end={Iso(end)}";
    }

    /// <inheritdoc />
    public override bool SupportsRange(HistoryRange range) => true;

    /// <inheritdoc />
    protected override string HistoryRequest(Asset asset, HistoryRange range)
    {
        // The endpoint takes only a few candle widths and answers with 300 candles at most, so the widths are the nearest
        // allowed ones and a year of days is cut to the last 300.
        var (granularity, count) = range switch
        {
            HistoryRange.Day => (TimeSpan.FromMinutes(5), 288),
            HistoryRange.FiveDays => (TimeSpan.FromHours(1), 120),
            HistoryRange.Month => (TimeSpan.FromHours(6), 120),
            HistoryRange.HalfYear => (TimeSpan.FromDays(1), 183),
            _ => (TimeSpan.FromDays(1), 300),
        };

        var end = TimeProvider.GetUtcNow();
        var start = end - (granularity * count);
        return $"/products/{Uri.EscapeDataString(asset.Symbol)}/candles?granularity={(int)granularity.TotalSeconds}&start={Iso(start)}&end={Iso(end)}";
    }

    /// <inheritdoc />
    protected override IReadOnlyList<PricePoint> ParseHistory(string json, HistoryRange range) =>
        CoinbaseMessages.ParseCandles(json, HistoryRangeSpec.For(range).Capacity);

    /// <summary>
    /// Writes a time the way the candles endpoint takes it.
    /// </summary>
    /// <param name="time">
    /// The time.
    /// </param>
    /// <returns>
    /// The time in ISO 8601, in UTC, to the second.
    /// </returns>
    private static string Iso(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

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
