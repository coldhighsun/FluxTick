using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Tencent;

/// <summary>
/// Polls the Tencent quote service for every subscribed instrument, one request per batch of instruments.
/// </summary>
public sealed class TencentProvider : BatchQuotePollingProviderBase, IHistoryProvider, ISymbolSearchProvider
{
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
