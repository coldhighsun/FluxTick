using System.Net;
using FluxTick.Abstractions;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Yahoo;

/// <summary>
/// Polls the Yahoo Finance chart API for every subscribed instrument. Backs off when rate limited.
/// </summary>
public sealed class YahooProvider : MarketDataProviderBase, IHistoryProvider, ISymbolSearchProvider
{
    /// <summary>
    /// The upper bound for the delay after repeated rate limiting.
    /// </summary>
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The number of consecutive failed cycles before the provider reports <see cref="ProviderConnectionState.Failed"/>.
    /// </summary>
    private const int FailureThreshold = 3;

    /// <summary>
    /// The maximum number of requests in flight.
    /// </summary>
    private const int MaxConcurrency = 4;

    /// <summary>
    /// The API endpoint.
    /// </summary>
    private readonly Uri _baseUri;

    /// <summary>
    /// The configured poll interval.
    /// </summary>
    private readonly TimeSpan _interval;

    /// <summary>
    /// The HTTP client; owned by the provider.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// The polling loop, or <see langword="null"/> while idle.
    /// </summary>
    private Task? _loop;

    /// <summary>
    /// Stops the polling loop without disposing the provider.
    /// </summary>
    private CancellationTokenSource? _loopCts;

    /// <summary>
    /// The number of consecutive cycles without a single success.
    /// </summary>
    private int _failedCycles;

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
        : base(descriptor, timeProvider)
    {
        _baseUri = baseUri;
        _interval = interval;
        _http = http;
        _logger = logger;
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

        var uri = new Uri(_baseUri, $"/v1/finance/search?q={Uri.EscapeDataString(query.Trim())}&quotesCount=15&newsCount=0");
        var json = await _http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        return YahooParser.ParseSearch(json, Descriptor.Id)
            .Where(a => a.AssetClass != AssetClass.Crypto && (assetClass is null || a.AssetClass == assetClass))
            .ToArray();
    }

    /// <inheritdoc />
    protected override async Task OnSubscriptionsChangedAsync(
        IReadOnlyCollection<Asset> current,
        IReadOnlyCollection<Asset> added,
        IReadOnlyCollection<Asset> removed,
        CancellationToken cancellationToken)
    {
        if (current.Count == 0)
        {
            await StopLoopAsync().ConfigureAwait(false);
            SetStatus(ProviderConnectionState.Idle);
            return;
        }

        if (_loop is null)
        {
            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ShutdownToken);
            var token = _loopCts.Token;
            _loop = Task.Run(() => RunAsync(token), CancellationToken.None);
        }
        else if (added.Count > 0)
        {
            var token = ShutdownToken;
            _ = Task.Run(() => PollAsync(added, token), CancellationToken.None);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeCoreAsync()
    {
        await StopLoopAsync().ConfigureAwait(false);
        _http.Dispose();
    }

    /// <summary>
    /// Stops the polling loop and waits for it to finish.
    /// </summary>
    /// <returns>
    /// A task that completes when the loop has ended.
    /// </returns>
    private async Task StopLoopAsync()
    {
        var loop = _loop;
        var cts = _loopCts;
        _loop = null;
        _loopCts = null;
        if (loop is null || cts is null)
        {
            return;
        }

        await cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        cts.Dispose();
    }

    /// <summary>
    /// Polls all subscriptions repeatedly, slowing down while Yahoo rate limits the client.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token that stops the loop.
    /// </param>
    /// <returns>
    /// A task that completes when the loop is cancelled.
    /// </returns>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var delay = _interval;
        while (!cancellationToken.IsCancellationRequested)
        {
            var limited = await PollAsync(CurrentSubscriptions, cancellationToken).ConfigureAwait(false);
            delay = limited ? TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxBackoff.Ticks)) : _interval;
            try
            {
                await Task.Delay(delay, TimeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Fetches the given instruments once, publishes the quotes and updates the provider status.
    /// </summary>
    /// <param name="assets">
    /// The instruments to fetch.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the cycle.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when Yahoo answered with HTTP 429 (rate limited) at least once.
    /// </returns>
    private async Task<bool> PollAsync(IReadOnlyCollection<Asset> assets, CancellationToken cancellationToken)
    {
        var succeeded = 0;
        var failed = 0;
        var limited = 0;
        string? lastError = null;

        try
        {
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
                        _logger.LogWarning(ex, "Yahoo request for {Symbol} failed", asset.Symbol);
                    }
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        ReportCycle(succeeded, failed + limited, lastError);
        return limited > 0;
    }

    /// <summary>
    /// Translates the outcome of a polling cycle into a provider status.
    /// </summary>
    /// <param name="succeeded">
    /// The number of successful requests.
    /// </param>
    /// <param name="failed">
    /// The number of failed requests.
    /// </param>
    /// <param name="lastError">
    /// The most recent error message.
    /// </param>
    private void ReportCycle(int succeeded, int failed, string? lastError)
    {
        if (succeeded > 0)
        {
            _failedCycles = 0;
            SetStatus(ProviderConnectionState.Connected, failed > 0 ? lastError : null);
        }
        else if (failed > 0)
        {
            _failedCycles++;
            SetStatus(
                _failedCycles >= FailureThreshold ? ProviderConnectionState.Failed : ProviderConnectionState.Reconnecting,
                lastError);
        }
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
        var uri = new Uri(_baseUri, $"/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval=1m&range=1d");
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
