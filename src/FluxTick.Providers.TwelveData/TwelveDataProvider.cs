using System.Net.Http.Headers;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.TwelveData;

/// <summary>
/// Polls the Twelve Data quote service for every subscribed instrument, one request per batch of instruments.
/// </summary>
public sealed class TwelveDataProvider : BatchQuotePollingProviderBase, IRangeHistoryProvider, ISymbolSearchProvider
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
    /// The API key.
    /// </summary>
    private readonly string _apiKey;

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
    /// <param name="apiKey">
    /// The API key; blank until the user has entered one.
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
    internal TwelveDataProvider(
        ProviderDescriptor descriptor,
        Uri apiBase,
        string apiKey,
        TimeSpan interval,
        HttpClient http,
        TimeProvider timeProvider,
        ILogger logger)
        : base(descriptor, interval, timeProvider, logger)
    {
        _apiBase = apiBase;
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
    protected override string ToRequestCode(string symbol) => symbol.Trim();

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<ParsedQuote>> RequestQuotesAsync(string[] codes, CancellationToken cancellationToken)
    {
        var uri = EndpointOptions.Combine(_apiBase, $"/quote?symbol={Uri.EscapeDataString(string.Join(',', codes))}");
        return TwelveDataParser.ParseQuotes(await GetStringAsync(uri, cancellationToken).ConfigureAwait(false));
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
