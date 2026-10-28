using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text.Json;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Eulerpool;

/// <summary>
/// Streams prices from the Eulerpool stream. The REST API gives every new instrument its first price and takes over
/// while the stream is down. A free key receives one-second bars with a delay instead of live trades.
/// </summary>
public sealed class EulerpoolProvider : RestBackedWebSocketProviderBase, ISymbolSearchProvider, IMarketHoursProvider, IHistoryProvider
{
    /// <summary>
    /// The number of search results returned.
    /// </summary>
    private const int SearchLimit = 20;

    /// <summary>
    /// The channels asked for: trades, and the one-second bars a free key gets instead of them.
    /// </summary>
    private static readonly string[] Channels = ["T", "A"];

    /// <summary>
    /// The REST API endpoint.
    /// </summary>
    private readonly Uri _apiBase;

    /// <summary>
    /// The API key.
    /// </summary>
    private readonly string _apiKey;

    /// <summary>
    /// The HTTP client; owned by the provider.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// The previous close of each ticker, which gives the change; neither the stream nor the latest quotes carry it.
    /// </summary>
    private readonly EulerpoolReferenceCache _references;

    /// <summary>
    /// Initializes a new instance of the <see cref="EulerpoolProvider"/> class.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    /// <param name="apiBase">
    /// The REST API endpoint.
    /// </param>
    /// <param name="streamUri">
    /// The stream endpoint.
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
    /// How long a connected stream may stay quiet during trading hours before REST is polled as well, or
    /// <see langword="null"/> for the default.
    /// </param>
    internal EulerpoolProvider(
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
        _apiKey = apiKey;
        _http = http;
        _references = new EulerpoolReferenceCache(FetchPreviousCloseAsync, timeProvider, logger);
    }

    /// <inheritdoc />
    public MarketState GetMarketState(Asset asset, DateTimeOffset now, DateTimeOffset? lastTradeTime = null, TimeSpan? holidayGrace = null) =>
        ExchangeHours.StateOf(EulerpoolExchanges.Of(asset.Symbol), now, lastTradeTime, holidayGrace);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PricePoint>> GetHistoryAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        // The one-minute candles of the last hour or so, which is all the sparkline shows.
        var now = TimeProvider.GetUtcNow();
        var uri = EndpointOptions.Combine(
            _apiBase,
            $"/api/1/charting/ohlcv/{Uri.EscapeDataString(asset.Symbol)}?resolution=1&from={(now - HistoryWindow.Span).ToUnixTimeSeconds()}&to={now.ToUnixTimeSeconds()}");
        var json = await GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        return EulerpoolParser.ParseHistory(json);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Asset>> SearchAsync(string query, AssetClass? assetClass, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || assetClass is not (null or AssetClass.Equity))
        {
            return [];
        }

        var uri = EndpointOptions.Combine(_apiBase, $"/api/1/equity/search?q={Uri.EscapeDataString(query.Trim())}");
        var json = await GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        return [.. EulerpoolParser.ParseSearch(json, Descriptor.Id).Where(a => assetClass is null || a.AssetClass == assetClass).Take(SearchLimit)];
    }

    /// <inheritdoc />
    protected override void ConfigureSocket(ClientWebSocketOptions options) =>
        options.SetRequestHeader("Authorization", $"Bearer {_apiKey}");

    /// <inheritdoc />
    protected override IEnumerable<string> BuildSubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        BuildMessages("subscribe", assets, includeChannels: true);

    /// <inheritdoc />
    protected override IEnumerable<string> BuildUnsubscribeMessages(IReadOnlyCollection<Asset> assets)
    {
        // The stream tells tickers apart without regard to case, but the subscriptions are told apart by case; an
        // instrument that goes must not take the stream of one that stays under the same ticker.
        var kept = CurrentSubscriptions.Select(a => a.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return BuildMessages("unsubscribe", [.. assets.Where(a => !kept.Contains(a.Symbol))], includeChannels: false);
    }

    /// <summary>
    /// Builds the messages that subscribe to or unsubscribe from tickers, <c>{"action":"unsubscribe","params":{"tickers":[..]}}</c>.
    /// The documentation shows only subscribing; unsubscribing was found by trying it, and the reply to either lists the
    /// tickers that remain subscribed.
    /// </summary>
    /// <param name="action">
    /// The action, <c>subscribe</c> or <c>unsubscribe</c>.
    /// </param>
    /// <param name="assets">
    /// The instruments.
    /// </param>
    /// <param name="includeChannels">
    /// Whether to name the channels, as subscribing does.
    /// </param>
    /// <returns>
    /// The messages, in batches of 100 tickers.
    /// </returns>
    private static IEnumerable<string> BuildMessages(string action, IReadOnlyCollection<Asset> assets, bool includeChannels) =>
        assets
            .Select(a => a.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Chunk(100)
            .Select(batch => JsonSerializer.Serialize(new
            {
                action,
                @params = includeChannels ? (object)new { tickers = batch, channels = Channels } : new { tickers = batch },
            }));

    /// <inheritdoc />
    protected override void HandleMessage(string text)
    {
        if (EulerpoolParser.TryParseTick(text, out var tick))
        {
            PublishToSubscribers(tick.Symbol, tick.Price, _references.Get(tick.Symbol), tick.Time);
        }
    }

    /// <inheritdoc />
    protected override Task<PollOutcome> FetchQuotesAsync(IReadOnlyCollection<Asset> assets, CancellationToken cancellationToken) =>
        BatchQuoteFetcher.FetchAsync(
            assets,
            symbol => symbol,
            RequestQuotesAsync,
            Publish,
            Logger,
            Descriptor.DisplayName,
            cancellationToken);

    /// <inheritdoc />
    protected override async ValueTask DisposeResourcesAsync()
    {
        await base.DisposeResourcesAsync().ConfigureAwait(false);
        _references.Dispose();
        _http.Dispose();
    }

    /// <summary>
    /// Fetches the latest prices of a batch of tickers.
    /// </summary>
    /// <param name="codes">
    /// The tickers.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that cancels the request.
    /// </param>
    /// <returns>
    /// The prices the service has.
    /// </returns>
    private async Task<IReadOnlyList<ParsedQuote>> RequestQuotesAsync(string[] codes, CancellationToken cancellationToken)
    {
        // The previous close first, so that the first price of a new instrument already shows its change.
        await _references.EnsureAsync(codes, cancellationToken).ConfigureAwait(false);
        var uri = EndpointOptions.Combine(_apiBase, $"/api/1/market/quotes/latest?stocks={Uri.EscapeDataString(string.Join(',', codes))}");
        var quotes = EulerpoolParser.ParseQuotes(await GetStringAsync(uri, cancellationToken).ConfigureAwait(false));
        return [.. quotes.Select(q => q with { PreviousClose = _references.Get(q.Key) })];
    }

    /// <summary>
    /// Fetches the previous close of a ticker from its daily candles of the last two weeks.
    /// </summary>
    /// <param name="symbol">
    /// The ticker.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that cancels the request.
    /// </param>
    /// <returns>
    /// The previous close, or <see langword="null"/> when the ticker has no earlier candle.
    /// </returns>
    private async Task<decimal?> FetchPreviousCloseAsync(string symbol, CancellationToken cancellationToken)
    {
        var now = TimeProvider.GetUtcNow();
        var uri = EndpointOptions.Combine(
            _apiBase,
            $"/api/1/charting/ohlcv/{Uri.EscapeDataString(symbol)}?resolution=D&from={now.AddDays(-14).ToUnixTimeSeconds()}&to={now.ToUnixTimeSeconds()}");
        var json = await GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        return EulerpoolParser.ParsePreviousClose(json, now, EulerpoolExchanges.Of(symbol)?.Zone);
    }

    /// <summary>
    /// Gets the body of an authenticated request.
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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }
}
