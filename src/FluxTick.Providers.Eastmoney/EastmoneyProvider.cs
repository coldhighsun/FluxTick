using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Eastmoney;

/// <summary>
/// Polls the Eastmoney quote service for every subscribed instrument, one request per batch of instruments.
/// </summary>
public sealed class EastmoneyProvider : BatchQuotePollingProviderBase, IRangeHistoryProvider, ISymbolSearchProvider, IMarketHoursProvider
{
    /// <summary>
    /// The number of search results returned.
    /// </summary>
    private const int SearchLimit = 20;

    /// <summary>
    /// The host of the symbol search service.
    /// </summary>
    private static readonly Uri SearchBase = new("https://searchadapter.eastmoney.com");

    /// <summary>
    /// The host of the history service.
    /// </summary>
    private static readonly Uri HistoryBase = new("https://push2his.eastmoney.com");

    /// <summary>
    /// The quote endpoint.
    /// </summary>
    private readonly Uri _quoteBase;

    /// <summary>
    /// The HTTP client; owned by the provider.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// Initializes a new instance of the <see cref="EastmoneyProvider"/> class.
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
    internal EastmoneyProvider(
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
        ExchangeHours.StateOf(EastmoneyParser.ExchangeOf(asset.Symbol), now, lastTradeTime, holidayGrace);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PricePoint>> GetHistoryAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        if (EastmoneyParser.ExchangeOf(asset.Symbol) is null)
        {
            return [];
        }

        // The service counts candles instead of taking a time window; one-minute candles of the last hour.
        var count = (int)HistoryWindow.Span.TotalMinutes;
        var json = await _http.GetStringAsync(KlineUri(asset.Symbol, 1, count), cancellationToken).ConfigureAwait(false);
        var points = EastmoneyParser.ParseKlines(json, asset.Symbol);
        return points.Count > 1 ? points : [];
    }

    /// <inheritdoc />
    public bool SupportsRange(HistoryRange range) => true;

    /// <inheritdoc />
    public bool SupportsRange(Asset asset, HistoryRange range) => EastmoneyParser.ExchangeOf(asset.Symbol) is not null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<PricePoint>> GetHistoryAsync(Asset asset, HistoryRange range, CancellationToken cancellationToken = default)
    {
        if (range == HistoryRange.Hour)
        {
            return await GetHistoryAsync(asset, cancellationToken).ConfigureAwait(false);
        }

        if (EastmoneyParser.ExchangeOf(asset.Symbol) is null)
        {
            return [];
        }

        // The widest candle the service has that is not wider than the bucket of the range, and no more candles than the
        // range shows. Counting candles reaches back over the trading days only, so a shut market still has its last session.
        var spec = HistoryRangeSpec.For(range);
        var json = await _http.GetStringAsync(KlineUri(asset.Symbol, KlineTypeOf(range), spec.Capacity), cancellationToken).ConfigureAwait(false);

        // The daily candle of today is dated at the close, ahead of the clock during the session; a point ahead of the
        // clock would stop the series from taking the live prices, which bring the price of today anyway.
        var now = TimeProvider.GetUtcNow();
        return [.. EastmoneyParser.ParseKlines(json, asset.Symbol).Where(p => p.Timestamp <= now)];
    }

    /// <summary>
    /// Names the candle type (minutes, or 101 for a day) the service takes for a range.
    /// </summary>
    /// <param name="range">
    /// The range.
    /// </param>
    /// <returns>
    /// The candle type.
    /// </returns>
    private static int KlineTypeOf(HistoryRange range) => range switch
    {
        HistoryRange.Day => 5,
        HistoryRange.FiveDays => 30,
        HistoryRange.Month => 60,
        HistoryRange.HalfYear or HistoryRange.Year => 101,
        _ => 1,
    };

    /// <summary>
    /// Builds the address of a candle request.
    /// </summary>
    /// <param name="symbol">
    /// The symbol, which is the id of the instrument at the service.
    /// </param>
    /// <param name="type">
    /// The candle type.
    /// </param>
    /// <param name="count">
    /// The number of candles, counted back from now.
    /// </param>
    /// <returns>
    /// The address.
    /// </returns>
    private static Uri KlineUri(string symbol, int type, int count) =>
        EndpointOptions.Combine(
            HistoryBase,
            $"/api/qt/stock/kline/get?secid={Uri.EscapeDataString(symbol)}&ut={EastmoneyParser.PublicToken}&fields1=f1,f2,f3&fields2=f51,f53&klt={type}&fqt=1&lmt={count}&end=20500101");

    /// <inheritdoc />
    public async Task<IReadOnlyList<Asset>> SearchAsync(string query, AssetClass? assetClass, CancellationToken cancellationToken = default)
    {
        if (assetClass is not (null or AssetClass.Equity or AssetClass.Index) || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var uri = EndpointOptions.Combine(
            SearchBase,
            $"/api/suggest/get?input={Uri.EscapeDataString(query.Trim())}&type=14&token={EastmoneyParser.SearchToken}&count={SearchLimit}");
        var json = await _http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        return EastmoneyParser.ParseSearch(json, Descriptor.Id)
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
    protected override string ToRequestCode(string symbol) => EastmoneyParser.ToRequestCode(symbol);

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<ParsedQuote>> RequestQuotesAsync(string[] codes, CancellationToken cancellationToken)
    {
        var uri = EndpointOptions.Combine(
            _quoteBase,
            $"/api/qt/ulist.np/get?fltt=2&secids={string.Join(',', codes.Select(Uri.EscapeDataString))}&fields=f2,f12,f13,f18,f124&ut={EastmoneyParser.PublicToken}");
        var json = await _http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        return EastmoneyParser.ParseQuotes(json);
    }
}
