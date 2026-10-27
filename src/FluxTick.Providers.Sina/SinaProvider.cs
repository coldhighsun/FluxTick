using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Sina;

/// <summary>
/// Polls the Sina quote service for every subscribed instrument, one request per batch of instruments.
/// </summary>
public sealed class SinaProvider : BatchQuotePollingProviderBase, ISymbolSearchProvider
{
    /// <summary>
    /// The number of search results returned.
    /// </summary>
    private const int SearchLimit = 20;

    /// <summary>
    /// The page the service expects requests to come from; it answers 403 to requests without it.
    /// </summary>
    private static readonly Uri Referrer = new("https://finance.sina.com.cn/");

    /// <summary>
    /// The host of the symbol search service.
    /// </summary>
    private static readonly Uri SearchBase = new("https://suggest3.sinajs.cn");

    /// <summary>
    /// The quote endpoint.
    /// </summary>
    private readonly Uri _quoteBase;

    /// <summary>
    /// The HTTP client; owned by the provider.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// Initializes a new instance of the <see cref="SinaProvider"/> class.
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
    internal SinaProvider(
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
    public async Task<IReadOnlyList<Asset>> SearchAsync(string query, AssetClass? assetClass, CancellationToken cancellationToken = default)
    {
        if (assetClass is not (null or AssetClass.Equity or AssetClass.Index) || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        // Types 11 and 12 are the A-share and B-share listings, which include the indices.
        var uri = EndpointOptions.Combine(SearchBase, $"/suggest/type=11,12&key={Uri.EscapeDataString(query.Trim())}");
        var bytes = await GetBytesAsync(uri, cancellationToken).ConfigureAwait(false);
        return SinaParser.ParseSearch(GbkText.Decode(bytes), Descriptor.Id)
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
    protected override string ToRequestCode(string symbol) => SinaParser.ToRequestCode(symbol);

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<ParsedQuote>> RequestQuotesAsync(string[] codes, CancellationToken cancellationToken)
    {
        var uri = EndpointOptions.Combine(_quoteBase, $"/list={string.Join(',', codes.Select(Uri.EscapeDataString))}");
        var bytes = await GetBytesAsync(uri, cancellationToken).ConfigureAwait(false);
        return SinaParser.ParseQuotes(GbkText.Decode(bytes));
    }

    /// <summary>
    /// Requests a URL the way the service wants it and reads the body.
    /// </summary>
    /// <param name="uri">
    /// The address to request.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the request.
    /// </param>
    /// <returns>
    /// The response body.
    /// </returns>
    private async Task<byte[]> GetBytesAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Referrer = Referrer;
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }
}
