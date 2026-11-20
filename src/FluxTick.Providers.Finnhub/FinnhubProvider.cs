using System.Collections.Concurrent;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Finnhub;

/// <summary>
/// Streams US stock trades from Finnhub. The REST API gives every new instrument its first price and the previous close
/// the change is measured against, and takes over while the stream is down.
/// </summary>
public sealed class FinnhubProvider : RestBackedWebSocketProviderBase, ISymbolSearchProvider, IMarketHoursProvider, IQuoteCurrencyProvider
{
    /// <summary>
    /// The number of search results returned.
    /// </summary>
    private const int SearchLimit = 20;

    /// <summary>
    /// The REST API endpoint.
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
    /// The HTTP client; owned by the provider.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// The previous close of each symbol, which gives the change; the trades of the stream do not carry it.
    /// </summary>
    private readonly ConcurrentDictionary<string, decimal> _previousCloses = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="FinnhubProvider"/> class.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    /// <param name="apiBase">
    /// The REST API endpoint.
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
    /// How long a connected stream may stay quiet during trading hours before REST is polled as well, or
    /// <see langword="null"/> for the default.
    /// </param>
    internal FinnhubProvider(
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
    public string? QuoteCurrencyOf(Asset asset) => "USD";

    /// <inheritdoc />
    public MarketState GetMarketState(Asset asset, DateTimeOffset now, DateTimeOffset? lastTradeTime = null, TimeSpan? holidayGrace = null) =>
        ExchangeHours.StateOf(ExchangeHours.Us, now, lastTradeTime, holidayGrace);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Asset>> SearchAsync(string query, AssetClass? assetClass, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || assetClass is not (null or AssetClass.Equity))
        {
            return [];
        }

        var uri = EndpointOptions.Combine(_apiBase, $"/api/v1/search?q={Uri.EscapeDataString(query.Trim())}&exchange=US");
        var json = await GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        return [.. FinnhubParser.ParseSearch(json, Descriptor.Id).Take(SearchLimit)];
    }

    /// <inheritdoc />
    protected override Uri BuildConnectionUri(IReadOnlyCollection<Asset> assets) =>
        new UriBuilder(_streamBase) { Query = $"token={Uri.EscapeDataString(_apiKey)}" }.Uri;

    /// <inheritdoc />
    protected override IEnumerable<string> BuildSubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        assets.Select(a => a.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Select(s => FinnhubParser.ControlMessage("subscribe", s));

    /// <inheritdoc />
    protected override IEnumerable<string> BuildUnsubscribeMessages(IReadOnlyCollection<Asset> assets)
    {
        // Symbols are told apart without regard to case by the stream; one that goes must not take the stream of another
        // subscription that stays under the same symbol.
        var kept = CurrentSubscriptions.Select(a => a.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return assets
            .Select(a => a.Symbol)
            .Where(s => !kept.Contains(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(s => FinnhubParser.ControlMessage("unsubscribe", s));
    }

    /// <inheritdoc />
    protected override void HandleMessage(string text)
    {
        var trades = FinnhubParser.ParseTrades(text);
        if (trades.Count == 0)
        {
            if (FinnhubParser.TryParseError(text) is { } error)
            {
                Logger.LogWarning("Finnhub reported an error: {Error}", error);
            }

            return;
        }

        // A message can carry several trades of one symbol; the newest decides the price.
        foreach (var trade in trades.GroupBy(t => t.Symbol, StringComparer.OrdinalIgnoreCase).Select(g => g.MaxBy(t => t.Time)!))
        {
            PublishToSubscribers(
                trade.Symbol,
                trade.Price,
                _previousCloses.TryGetValue(trade.Symbol, out var previous) ? previous : null,
                trade.Time);
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
    /// Fetches the latest prices of a batch of symbols, one request each: the service has no batch request. The previous
    /// close of each is kept for the trades of the stream.
    /// </summary>
    /// <param name="codes">
    /// The symbols.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that cancels the requests.
    /// </param>
    /// <returns>
    /// The prices the service has.
    /// </returns>
    private async Task<IReadOnlyList<ParsedQuote>> RequestQuotesAsync(string[] codes, CancellationToken cancellationToken)
    {
        var quotes = new List<ParsedQuote>(codes.Length);
        foreach (var code in codes)
        {
            var uri = EndpointOptions.Combine(_apiBase, $"/api/v1/quote?symbol={Uri.EscapeDataString(code)}");
            var json = await GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
            if (FinnhubParser.ParseQuote(code, json) is not { } quote)
            {
                continue;
            }

            if (quote.PreviousClose is { } previous)
            {
                _previousCloses[code] = previous;
            }

            quotes.Add(quote);
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
        request.Headers.Add("X-Finnhub-Token", _apiKey);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }
}
