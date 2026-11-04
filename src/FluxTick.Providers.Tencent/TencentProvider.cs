using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Tencent;

/// <summary>
/// Polls the Tencent quote service for every subscribed instrument, one request per batch of instruments.
/// </summary>
public sealed class TencentProvider : BatchQuotePollingProviderBase, IRangeHistoryProvider, ISymbolSearchProvider, IMarketHoursProvider
{
    /// <summary>
    /// The number of search results returned.
    /// </summary>
    private const int SearchLimit = 20;

    /// <summary>
    /// The number of daily candles that cover a month, with a few days to spare for the days a market is shut.
    /// </summary>
    private const int MonthCandles = 32;

    /// <summary>
    /// The number of daily candles that cover half a year of trading days, with a few to spare.
    /// </summary>
    private const int HalfYearCandles = 130;

    /// <summary>
    /// The number of daily candles that cover a year of trading days, with a few to spare.
    /// </summary>
    private const int YearCandles = 260;

    /// <summary>
    /// The host of the symbol search service.
    /// </summary>
    private static readonly Uri SearchBase = new("https://smartbox.gtimg.cn");

    /// <summary>
    /// The host of the history service.
    /// </summary>
    private static readonly Uri HistoryBase = new("https://web.ifzq.gtimg.cn");

    /// <summary>
    /// The quote endpoint.
    /// </summary>
    private readonly Uri _quoteBase;

    /// <summary>
    /// The HTTP client; owned by the provider.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// Initializes a new instance of the <see cref="TencentProvider"/> class.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    /// <param name="quoteBase">
    /// The quote endpoint.
    /// </param>
    /// <param name="interval">
    /// The poll interval.
    /// </param>
    /// <param name="http">
    /// The HTTP client; the provider takes ownership.
    /// </param>
    /// <param name="timeProvider">
    /// The clock.
    /// </param>
    /// <param name="logger">
    /// The logger.
    /// </param>
    internal TencentProvider(
        ProviderDescriptor descriptor,
        Uri quoteBase,
        TimeSpan interval,
        HttpClient http,
        TimeProvider timeProvider,
        ILogger logger)
        : base(descriptor, interval, timeProvider, logger)
    {
        _quoteBase = quoteBase;
        _http = http;
    }

    /// <inheritdoc />
    public MarketState GetMarketState(Asset asset, DateTimeOffset now, DateTimeOffset? lastTradeTime = null, TimeSpan? holidayGrace = null) =>
        ExchangeHours.StateOf(ExchangeOf(asset.Symbol), now, lastTradeTime, holidayGrace);

    /// <summary>
    /// Finds the exchange of a symbol from its market prefix (<c>sh600000</c>, <c>hk00700</c>, <c>usAAPL</c>).
    /// </summary>
    /// <param name="symbol">
    /// The symbol.
    /// </param>
    /// <returns>
    /// The exchange, or <see langword="null"/> when it is not known.
    /// </returns>
    internal static ExchangeHours? ExchangeOf(string symbol)
    {
        if (QuoteText.HasPrefix(symbol, "sh") || QuoteText.HasPrefix(symbol, "sz") || QuoteText.HasPrefix(symbol, "bj"))
        {
            return ExchangeHours.ChinaA;
        }

        return QuoteText.HasPrefix(symbol, "hk") ? ExchangeHours.HongKong : QuoteText.HasPrefix(symbol, "us") ? ExchangeHours.Us : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PricePoint>> GetHistoryAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        var code = TencentParser.ToRequestCode(asset.Symbol);
        if (!TencentParser.HasMinuteHistory(code))
        {
            return [];
        }

        var uri = EndpointOptions.Combine(HistoryBase, $"/appstock/app/minute/query?code={Uri.EscapeDataString(code)}");
        var json = await _http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        var points = TencentParser.ParseMinuteHistory(json, code);

        // For some markets the service sends only the latest point, which does not make a sparkline.
        return points.Count > 1 ? points : [];
    }

    /// <inheritdoc />
    public bool SupportsRange(HistoryRange range) => true;

    /// <inheritdoc />
    public bool SupportsRange(Asset asset, HistoryRange range) =>
        TencentParser.HasMinuteHistory(TencentParser.ToRequestCode(asset.Symbol));

    /// <inheritdoc />
    public async Task<IReadOnlyList<PricePoint>> GetHistoryAsync(Asset asset, HistoryRange range, CancellationToken cancellationToken = default)
    {
        if (range == HistoryRange.Hour)
        {
            return await GetHistoryAsync(asset, cancellationToken).ConfigureAwait(false);
        }

        var code = TencentParser.ToRequestCode(asset.Symbol);

        // The services of the longer ranges need the exchange suffix that the US codes of this app do not have.
        if (!TencentParser.HasMinuteHistory(code))
        {
            return [];
        }

        // A day and five days are told by the minutes of the last five trading days, which one answer holds; the service has
        // no way to ask for fewer. A month and longer are told by daily candles, counted back from today.
        if (range is HistoryRange.Day or HistoryRange.FiveDays)
        {
            var minutes = await _http.GetStringAsync(EndpointOptions.Combine(HistoryBase, $"/appstock/app/day/query?code={Uri.EscapeDataString(code)}"), cancellationToken).ConfigureAwait(false);

            // All of it, also for a day: the chart keeps to its window, which reaches into the session before while the
            // market is open, and a market shut for longer than the window ends its chart with the last session.
            return TencentParser.ParseFiveDayMinutes(minutes, code);
        }

        var path = code.StartsWith("hk", StringComparison.OrdinalIgnoreCase) ? "hkfqkline" : "fqkline";
        var count = range switch
        {
            HistoryRange.Month => MonthCandles,
            HistoryRange.HalfYear => HalfYearCandles,
            _ => YearCandles,
        };
        var daily = await _http.GetStringAsync(EndpointOptions.Combine(HistoryBase, $"/appstock/app/{path}/get?param={Uri.EscapeDataString(code)},day,,,{count},qfq"), cancellationToken).ConfigureAwait(false);

        // Today's candle is dated at the close, ahead of the clock during the session; a point ahead of the clock would stop
        // the series from taking the live prices, which bring the price of today anyway.
        var now = TimeProvider.GetUtcNow();
        return [.. TencentParser.ParseDailyCandles(daily, code).Where(p => p.Timestamp <= now)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Asset>> SearchAsync(string query, AssetClass? assetClass, CancellationToken cancellationToken = default)
    {
        if (assetClass is not (null or AssetClass.Equity or AssetClass.Index) || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var uri = EndpointOptions.Combine(SearchBase, $"/s3/?v=2&t=all&q={Uri.EscapeDataString(query.Trim())}");
        var text = await _http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        return TencentParser.ParseSearch(text, Descriptor.Id)
            .Where(a => assetClass is null || a.AssetClass == assetClass)
            .Take(SearchLimit)
            .ToArray();
    }

    /// <inheritdoc />
    protected override ValueTask DisposeResourcesAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    protected override string ToRequestCode(string symbol) => TencentParser.ToRequestCode(symbol);

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<ParsedQuote>> RequestQuotesAsync(string[] codes, CancellationToken cancellationToken)
    {
        var uri = EndpointOptions.Combine(_quoteBase, $"/q={string.Join(',', codes.Select(Uri.EscapeDataString))}");
        using var response = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return TencentParser.ParseQuotes(GbkText.Decode(bytes));
    }
}
