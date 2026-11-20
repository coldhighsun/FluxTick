using System.Collections.Concurrent;
using System.Net.Http.Headers;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.TwelveData;

/// <summary>
/// Streams prices from the Twelve Data WebSocket. The REST quote service gives every new instrument its first price and
/// the previous close the change is measured against, and takes over while the stream is down.
/// </summary>
public sealed class TwelveDataProvider : RestBackedWebSocketProviderBase, IRangeHistoryProvider, ISymbolSearchProvider, IMarketHoursProvider
{
    /// <summary>
    /// The number of search results returned.
    /// </summary>
    private const int SearchLimit = 20;

    /// <summary>
    /// The API endpoint.
    /// </summary>
    private readonly Uri _apiBase;

    /// <summary>
    /// The stream endpoint without the key.
    /// </summary>
    private readonly Uri _streamBase;

    /// <summary>
    /// The API key.
    /// </summary>
    private readonly string _apiKey;

    /// <summary>
    /// The market identifier code of each symbol, learned from its first quote, which tells its trading hours.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _exchanges = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The previous close of each symbol, which gives the change; the stream carries prices only.
    /// </summary>
    private readonly ConcurrentDictionary<string, decimal> _previousCloses = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The HTTP client; owned by the provider.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// Initializes a new instance of the <see cref="TwelveDataProvider"/> class.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    /// <param name="apiBase">
    /// The API endpoint.
    /// </param>
    /// <param name="streamUri">
    /// The stream endpoint; the key is added when connecting.
    /// </param>
    /// <param name="apiKey">
    /// The API key; blank until the user has entered one.
    /// </param>
    /// <param name="interval">
    /// How often the REST API is polled while the stream is down.
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
    /// <param name="quietInterval">
    /// How long a connected stream may stay quiet before REST is polled as well, or <see langword="null"/> for the default.
    /// </param>
    internal TwelveDataProvider(
        ProviderDescriptor descriptor,
        Uri apiBase,
        Uri streamUri,
        string apiKey,
        TimeSpan interval,
        HttpClient http,
        TimeProvider timeProvider,
        ILogger logger,
        TimeSpan? quietInterval = null)
        : base(descriptor, streamUri, interval, timeProvider, logger, quietInterval)
    {
        _apiBase = apiBase;
        _streamBase = streamUri;
        _apiKey = apiKey;
        _http = http;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PricePoint>> GetHistoryAsync(Asset asset, CancellationToken cancellationToken = default) =>
        FetchSeriesAsync(asset, "1min", (int)HistoryWindow.Span.TotalMinutes, cancellationToken);

    /// <inheritdoc />
    public bool SupportsRange(HistoryRange range) => true;

    /// <inheritdoc />
    public async Task<IReadOnlyList<PricePoint>> GetHistoryAsync(Asset asset, HistoryRange range, CancellationToken cancellationToken = default)
    {
        if (range == HistoryRange.Hour)
        {
            return await GetHistoryAsync(asset, cancellationToken).ConfigureAwait(false);
        }

        // The width of the bucket of the range, and no more candles than the range shows. The service counts candles back
        // from the last one, so a shut market still has its last session.
        var interval = range switch
        {
            HistoryRange.Day => "5min",
            HistoryRange.FiveDays => "30min",
            HistoryRange.Month => "4h",
            _ => "1day",
        };
        var points = await FetchSeriesAsync(asset, interval, HistoryRangeSpec.For(range).Capacity, cancellationToken).ConfigureAwait(false);

        // A daily candle is dated at the close, ahead of the clock during the session.
        var now = TimeProvider.GetUtcNow();
        return [.. points.Where(p => p.Timestamp <= now)];
    }

    /// <summary>
    /// Fetches the candles of an instrument.
    /// </summary>
    /// <param name="asset">
    /// The instrument.
    /// </param>
    /// <param name="interval">
    /// The candle width, the way the service names it.
    /// </param>
    /// <param name="count">
    /// The number of candles.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the request.
    /// </param>
    /// <returns>
    /// The close of each candle, oldest first.
    /// </returns>
    private async Task<IReadOnlyList<PricePoint>> FetchSeriesAsync(Asset asset, string interval, int count, CancellationToken cancellationToken)
    {
        var uri = EndpointOptions.Combine(
            _apiBase,
            $"/time_series?symbol={Uri.EscapeDataString(asset.Symbol)}&interval={interval}&outputsize={count}");
        return TwelveDataParser.ParseTimeSeries(await GetStringAsync(uri, cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Asset>> SearchAsync(string query, AssetClass? assetClass, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || assetClass is not (null or AssetClass.Equity or AssetClass.Index))
        {
            return [];
        }

        var uri = EndpointOptions.Combine(_apiBase, $"/symbol_search?symbol={Uri.EscapeDataString(query.Trim())}&outputsize={SearchLimit * 2}");
        var json = await GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        return [.. TwelveDataParser.ParseSearch(json, Descriptor.Id).Where(a => assetClass is null || a.AssetClass == assetClass).Take(SearchLimit)];
    }

    /// <inheritdoc />
    public MarketState GetMarketState(Asset asset, DateTimeOffset now, DateTimeOffset? lastTradeTime = null, TimeSpan? holidayGrace = null)
    {
        // A pair of currencies or of a cryptocurrency (BTC/USD) trades around the clock; a stock follows its exchange once the
        // first quote has told which it is, and is of unknown state until then.
        if (asset.Symbol.Contains('/', StringComparison.Ordinal))
        {
            return MarketState.Open;
        }

        return _exchanges.TryGetValue(asset.Symbol, out var mic)
            ? ExchangeHours.StateOf(TwelveDataParser.ExchangeOf(mic), now, lastTradeTime, holidayGrace)
            : MarketState.Unknown;
    }

    /// <inheritdoc />
    protected override Uri BuildConnectionUri(IReadOnlyCollection<Asset> assets) =>
        new UriBuilder(_streamBase) { Query = $"apikey={Uri.EscapeDataString(_apiKey)}" }.Uri;

    /// <inheritdoc />
    protected override TimeSpan? HeartbeatInterval => TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    protected override string? HeartbeatMessage => TwelveDataParser.Heartbeat;

    /// <inheritdoc />
    protected override IEnumerable<string> BuildSubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        TwelveDataParser.ControlMessages("subscribe", assets.Select(a => a.Symbol));

    /// <inheritdoc />
    protected override IEnumerable<string> BuildUnsubscribeMessages(IReadOnlyCollection<Asset> assets)
    {
        // Symbols are told apart without regard to case by the stream; one that goes must not take the stream of another
        // subscription that stays under the same symbol.
        var kept = CurrentSubscriptions.Select(a => a.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return TwelveDataParser.ControlMessages("unsubscribe", assets.Select(a => a.Symbol).Where(s => !kept.Contains(s)));
    }

    /// <inheritdoc />
    protected override void HandleMessage(string text)
    {
        if (TwelveDataParser.TryParsePrice(text, out var price))
        {
            PublishToSubscribers(
                price.Symbol,
                price.Price,
                _previousCloses.TryGetValue(price.Symbol, out var previous) ? previous : null,
                price.Time);
        }
        else if (TwelveDataParser.TryParseStreamError(text) is { } error)
        {
            Logger.LogWarning("Twelve Data reported a problem: {Error}", error);
        }
    }

    /// <inheritdoc />
    protected override Task<PollOutcome> FetchQuotesAsync(IReadOnlyCollection<Asset> assets, CancellationToken cancellationToken) =>
        BatchQuoteFetcher.FetchAsync(
            assets,
            symbol => symbol.Trim(),
            RequestQuotesAsync,
            Publish,
            Logger,
            Descriptor.DisplayName,
            cancellationToken);

    /// <summary>
    /// Fetches the latest prices of a batch of symbols, and keeps the previous close of each for the stream.
    /// </summary>
    /// <param name="codes">
    /// The symbols.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that cancels the request.
    /// </param>
    /// <returns>
    /// The prices the service has.
    /// </returns>
    private async Task<IReadOnlyList<ParsedQuote>> RequestQuotesAsync(string[] codes, CancellationToken cancellationToken)
    {
        var uri = EndpointOptions.Combine(_apiBase, $"/quote?symbol={Uri.EscapeDataString(string.Join(',', codes))}");
        var json = await GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        var quotes = TwelveDataParser.ParseQuotes(json);
        foreach (var (symbol, mic) in TwelveDataParser.ParseExchanges(json))
        {
            _exchanges[symbol] = mic;
        }

        foreach (var quote in quotes)
        {
            if (quote.PreviousClose is { } previous)
            {
                _previousCloses[quote.Key] = previous;
            }
        }

        return quotes;
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeResourcesAsync()
    {
        await base.DisposeResourcesAsync().ConfigureAwait(false);
        _http.Dispose();
    }

    /// <summary>
    /// Gets the body of an authenticated request. The key goes in a header, not in the address, so that it cannot end up
    /// in a log of requests.
    /// </summary>
    /// <param name="uri">
    /// The address.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that cancels the request.
    /// </param>
    /// <returns>
    /// The response body.
    /// </returns>
    private async Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("apikey", _apiKey);

        // The service answers errors, such as a wrong key or the rate limit, with an error body, in some cases with status 200.
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode && !body.StartsWith('{'))
        {
            response.EnsureSuccessStatusCode();
        }

        return body;
    }
}
