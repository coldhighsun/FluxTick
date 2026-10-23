using System.Net;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Yahoo;

/// <summary>
/// Polls the Yahoo Finance chart API for every subscribed instrument. Backs off when rate limited.
/// </summary>
public sealed class YahooProvider : PollingProviderBase, IHistoryProvider, ISymbolSearchProvider
{
    /// <summary>
    /// The maximum number of requests in flight.
    /// </summary>
    private const int MaxConcurrency = 4;

    /// <summary>
    /// The API endpoint.
    /// </summary>
    private readonly Uri _baseUri;

    /// <summary>
    /// The HTTP client; owned by the provider.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// Initializes a new instance of the <see cref="YahooProvider"/> class.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    /// <param name="baseUri">
    /// The API endpoint.
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
    internal YahooProvider(
        ProviderDescriptor descriptor,
        Uri baseUri,
        TimeSpan interval,
        HttpClient http,
        TimeProvider timeProvider,
        ILogger logger)
        : base(descriptor, interval, timeProvider, logger)
    {
        _baseUri = baseUri;
        _http = http;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PricePoint>> GetHistoryAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        var data = await FetchChartAsync(asset.Symbol, cancellationToken).ConfigureAwait(false);
        return data.History;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Asset>> SearchAsync(string query, AssetClass? assetClass, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var uri = EndpointOptions.Combine(_baseUri, $"/v1/finance/search?q={Uri.EscapeDataString(query.Trim())}&quotesCount=15&newsCount=0");
        var json = await _http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        return YahooParser.ParseSearch(json, Descriptor.Id)
            .Where(a => a.AssetClass != AssetClass.Crypto && (assetClass is null || a.AssetClass == assetClass))
            .ToArray();
    }

    /// <inheritdoc />
    protected override ValueTask DisposeResourcesAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Fetches the given instruments once and publishes their quotes. Backs off after HTTP 429 (rate limited).
    /// </summary>
    /// <param name="assets">
    /// The instruments to fetch.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the cycle.
    /// </param>
    /// <returns>
    /// How the cycle went.
    /// </returns>
    protected override async Task<PollOutcome> PollOnceAsync(IReadOnlyCollection<Asset> assets, CancellationToken cancellationToken)
    {
        var succeeded = 0;
        var failed = 0;
        var limited = 0;
        string? lastError = null;

        await Parallel.ForEachAsync(
            assets,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency, CancellationToken = cancellationToken },
            async (asset, token) =>
            {
                try
                {
                    var data = await FetchChartAsync(asset.Symbol, token).ConfigureAwait(false);
                    Publish(data.Quote);
                    Interlocked.Increment(ref succeeded);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    Interlocked.Increment(ref limited);
                    lastError = "Rate limited by Yahoo";
                }
                catch (Exception ex) when (ex is HttpRequestException or YahooApiException or System.Text.Json.JsonException or TaskCanceledException)
                {
                    if (token.IsCancellationRequested)
                    {
                        throw;
                    }

                    Interlocked.Increment(ref failed);
                    lastError = $"{asset.Symbol}: {ex.Message}";
                    Logger.LogWarning(ex, "Yahoo request for {Symbol} failed", asset.Symbol);
                }
            }).ConfigureAwait(false);

        return new PollOutcome(succeeded, failed + limited, lastError, BackOff: limited > 0);
    }

    /// <summary>
    /// Downloads and parses the 1-day, 1-minute chart of a symbol.
    /// </summary>
    /// <param name="symbol">
    /// The Yahoo symbol.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the request.
    /// </param>
    /// <returns>
    /// The parsed chart.
    /// </returns>
    private async Task<YahooChartData> FetchChartAsync(string symbol, CancellationToken cancellationToken)
    {
        var uri = EndpointOptions.Combine(_baseUri, $"/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval=1m&range=1d");
        using var response = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new YahooApiException($"Unknown symbol '{symbol}'.");
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return YahooParser.ParseChart(json, Descriptor.Id, symbol);
    }
}
