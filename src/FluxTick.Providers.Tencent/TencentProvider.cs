using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Tencent;

/// <summary>
/// Polls the Tencent quote service for every subscribed instrument, one request per batch of instruments.
/// </summary>
public sealed class TencentProvider : PollingProviderBase, IHistoryProvider, ISymbolSearchProvider
{
    /// <summary>
    /// The most codes asked for in one request, to keep the URL short.
    /// </summary>
    private const int BatchSize = 50;

    /// <summary>
    /// The number of search results returned.
    /// </summary>
    private const int SearchLimit = 20;

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

    /// <summary>
    /// Fetches the given instruments in batches and publishes their quotes.
    /// </summary>
    /// <param name="assets">
    /// The instruments to fetch.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the cycle.
    /// </param>
    /// <returns>
    /// How the cycle went; the next cycle backs off when no batch succeeded.
    /// </returns>
    protected override async Task<PollOutcome> PollOnceAsync(IReadOnlyCollection<Asset> assets, CancellationToken cancellationToken)
    {
        // A symbol may have been typed in another case than the service answers with; several subscriptions can
        // share one code. Quotes are published under the symbol that was subscribed.
        var byCode = new Dictionary<string, List<Asset>>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            var code = TencentParser.ToRequestCode(asset.Symbol);
            if (!byCode.TryGetValue(code, out var list))
            {
                byCode[code] = list = [];
            }

            list.Add(asset);
        }

        var succeeded = 0;
        var failed = 0;
        string? lastError = null;
        foreach (var batch in byCode.Keys.Chunk(BatchSize))
        {
            try
            {
                var answered = await FetchAsync(batch, byCode, cancellationToken).ConfigureAwait(false);
                succeeded++;
                foreach (var code in batch.Where(c => !answered.Contains(c)))
                {
                    lastError = $"No data for {byCode[code][0].Symbol}";
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failed++;
                lastError = ex.Message;
                Logger.LogWarning(ex, "Tencent request failed");
            }
        }

        return new PollOutcome(succeeded, failed, lastError, BackOff: succeeded == 0);
    }

    /// <summary>
    /// Requests one batch of codes and publishes the quotes of the answer.
    /// </summary>
    /// <param name="codes">
    /// The codes to request.
    /// </param>
    /// <param name="byCode">
    /// The subscribed instruments by code.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the request.
    /// </param>
    /// <returns>
    /// The codes the service answered for.
    /// </returns>
    private async Task<HashSet<string>> FetchAsync(
        string[] codes,
        Dictionary<string, List<Asset>> byCode,
        CancellationToken cancellationToken)
    {
        var uri = EndpointOptions.Combine(_quoteBase, $"/q={string.Join(',', codes.Select(Uri.EscapeDataString))}");
        using var response = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        var answered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var quote in TencentParser.ParseQuotes(TencentParser.DecodeGbk(bytes)))
        {
            if (!byCode.TryGetValue(quote.Key, out var subscribed))
            {
                continue;
            }

            answered.Add(quote.Key);
            foreach (var asset in subscribed)
            {
                Publish(new Quote(asset.Id, quote.Price, quote.PreviousClose, quote.Time ?? TimeProvider.GetUtcNow()));
            }
        }

        return answered;
    }
}
