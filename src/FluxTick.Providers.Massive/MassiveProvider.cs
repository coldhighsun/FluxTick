using System.Net.Http.Headers;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Massive;

/// <summary>
/// Polls the Massive aggregates service for every subscribed instrument, one request per instrument spaced out to stay
/// within the request limit of the plan.
/// </summary>
public sealed class MassiveProvider : BatchQuotePollingProviderBase, IRangeHistoryProvider, ISymbolSearchProvider, IMarketHoursProvider
{
    /// <summary>
    /// The number of search results returned.
    /// </summary>
    private const int SearchLimit = 20;

    /// <summary>
    /// How far back a quote looks for its two bars: a long weekend with a holiday next to it holds no more than two
    /// sessions of a week.
    /// </summary>
    private static readonly TimeSpan QuoteLookBack = TimeSpan.FromDays(10);

    /// <summary>
    /// The API endpoint.
    /// </summary>
    private readonly Uri _apiBase;

    /// <summary>
    /// The API key.
    /// </summary>
    private readonly string _apiKey;

    /// <summary>
    /// The wait between two requests of one refresh.
    /// </summary>
    private readonly TimeSpan _requestSpacing;

    /// <summary>
    /// The HTTP client; owned by the provider.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// Initializes a new instance of the <see cref="MassiveProvider"/> class.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    /// <param name="apiBase">
    /// The API endpoint.
    /// </param>
    /// <param name="apiKey">
    /// The API key; blank until the user has entered one.
    /// </param>
    /// <param name="interval">
    /// The poll interval.
    /// </param>
    /// <param name="requestSpacing">
    /// The wait between two requests of one refresh.
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
    internal MassiveProvider(
        ProviderDescriptor descriptor,
        Uri apiBase,
        string apiKey,
        TimeSpan interval,
        TimeSpan requestSpacing,
        HttpClient http,
        TimeProvider timeProvider,
        ILogger logger)
        : base(descriptor, interval, timeProvider, logger)
    {
        _apiBase = apiBase;
        _apiKey = apiKey;
        _requestSpacing = requestSpacing;
        _http = http;
    }

    /// <inheritdoc />
    public MarketState GetMarketState(Asset asset, DateTimeOffset now, DateTimeOffset? lastTradeTime = null, TimeSpan? holidayGrace = null) =>
        ExchangeHours.StateOf(ExchangeHours.Us, now, lastTradeTime, holidayGrace);

    /// <inheritdoc />
    public Task<IReadOnlyList<PricePoint>> GetHistoryAsync(Asset asset, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PricePoint>>([]);

    /// <inheritdoc />
    public bool SupportsRange(HistoryRange range) => range is HistoryRange.Month or HistoryRange.HalfYear or HistoryRange.Year;

    /// <inheritdoc />
    public async Task<IReadOnlyList<PricePoint>> GetHistoryAsync(Asset asset, HistoryRange range, CancellationToken cancellationToken = default)
    {
        // Only the daily bars the free plan has; shorter ranges are left to the live prices.
        if (!SupportsRange(range))
        {
            return [];
        }

        var spec = HistoryRangeSpec.For(range);
        var json = await GetStringAsync(BarsUri(asset.Symbol, spec.Window, "asc", spec.Capacity), cancellationToken).ConfigureAwait(false);

        // The bar of today is dated at the close, ahead of the clock during the session.
        var now = TimeProvider.GetUtcNow();
        return [.. MassiveParser.ParseDailyBars(json).Where(p => p.Timestamp <= now)];
    }

    /// <summary>
    /// Builds the address of a request for daily bars that end today.
    /// </summary>
    /// <param name="symbol">
    /// The ticker.
    /// </param>
    /// <param name="window">
    /// How far back the bars start.
    /// </param>
    /// <param name="sort">
    /// <c>asc</c> or <c>desc</c>.
    /// </param>
    /// <param name="limit">
    /// The most bars to return.
    /// </param>
    /// <returns>
    /// The address.
    /// </returns>
    private Uri BarsUri(string symbol, TimeSpan window, string sort, int limit)
    {
        var now = TimeProvider.GetUtcNow();
        var to = DateOnly.FromDateTime(now.UtcDateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var from = DateOnly.FromDateTime((now - window).UtcDateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return EndpointOptions.Combine(
            _apiBase,
            $"/v2/aggs/ticker/{Uri.EscapeDataString(symbol)}/range/1/day/{from}/{to}?adjusted=true&sort={sort}&limit={limit}");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Asset>> SearchAsync(string query, AssetClass? assetClass, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || assetClass is not (null or AssetClass.Equity))
        {
            return [];
        }

        var uri = EndpointOptions.Combine(
            _apiBase,
            $"/v3/reference/tickers?search={Uri.EscapeDataString(query.Trim())}&market=stocks&active=true&limit={SearchLimit}");
        var json = await GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        return [.. MassiveParser.ParseSearch(json, Descriptor.Id).Take(SearchLimit)];
    }

    /// <inheritdoc />
    protected override string ToRequestCode(string symbol) => symbol.Trim();

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<ParsedQuote>> RequestQuotesAsync(string[] codes, CancellationToken cancellationToken)
    {
        var quotes = new List<ParsedQuote>(codes.Length);
        for (var i = 0; i < codes.Length; i++)
        {
            if (i > 0)
            {
                // The plan limits the requests per minute, not per refresh.
                await Task.Delay(_requestSpacing, TimeProvider, cancellationToken).ConfigureAwait(false);
            }

            // The newest two bars: the price, and the close the change is measured against.
            var json = await GetStringAsync(BarsUri(codes[i], QuoteLookBack, "desc", 2), cancellationToken).ConfigureAwait(false);
            if (MassiveParser.ParseQuote(codes[i], json) is { } quote)
            {
                quotes.Add(quote);
            }
        }

        return quotes;
    }

    /// <inheritdoc />
    protected override ValueTask DisposeResourcesAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        // An error, such as a wrong key, comes with a JSON body that says what is wrong.
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode && !body.StartsWith('{'))
        {
            response.EnsureSuccessStatusCode();
        }

        return body;
    }
}
