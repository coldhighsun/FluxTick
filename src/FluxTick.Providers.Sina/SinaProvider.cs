using System.Net.WebSockets;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Sina;

/// <summary>
/// Streams prices from the Sina quote stream, which pushes every subscribed instrument every few seconds. The instruments
/// are part of the address of the stream, so a change of the watchlist connects again. The quote service gives every new
/// instrument its first price and takes over while the stream is down.
/// </summary>
public sealed class SinaProvider : RestBackedWebSocketProviderBase, ISymbolSearchProvider
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
    /// The origin the stream expects the connection to come from; it refuses a handshake without it.
    /// </summary>
    private const string Origin = "https://finance.sina.com.cn";

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
    /// <param name="streamUri">
    /// The stream endpoint, without the list of instruments.
    /// </param>
    /// <param name="interval">
    /// How often the quote service is polled while the stream is down.
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
        Uri streamUri,
        TimeSpan interval,
        HttpClient http,
        TimeProvider timeProvider,
        ILogger logger)
        : base(descriptor, streamUri, interval, timeProvider, logger)
    {
        _quoteBase = quoteBase;
        _http = http;
    }

    /// <inheritdoc />
    protected override bool SubscribesByUri => true;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Asset>> SearchAsync(string query, AssetClass? assetClass, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        // The search of the service finds neither currency pairs nor futures; those come from a list.
        var listed = SinaInstruments.Search(query, assetClass, Descriptor.Id);
        if (assetClass is AssetClass.Forex or AssetClass.Commodity)
        {
            return listed;
        }

        if (assetClass is not (null or AssetClass.Equity or AssetClass.Index))
        {
            return [];
        }

        // Types 11 and 12 are the A-share and B-share listings, which include the indices.
        var uri = EndpointOptions.Combine(SearchBase, $"/suggest/type=11,12&key={Uri.EscapeDataString(query.Trim())}");
        var bytes = await GetBytesAsync(uri, cancellationToken).ConfigureAwait(false);
        var found = SinaParser.ParseSearch(GbkText.Decode(bytes), Descriptor.Id)
            .Where(a => assetClass is null || a.AssetClass == assetClass)
            .Take(SearchLimit);
        return [.. found, .. listed];
    }

    /// <inheritdoc />
    protected override void ConfigureSocket(ClientWebSocketOptions options) => options.SetRequestHeader("Origin", Origin);

    /// <inheritdoc />
    protected override Uri BuildConnectionUri(IReadOnlyCollection<Asset> assets)
    {
        var codes = assets.Select(a => SinaParser.ToRequestCode(a.Symbol)).Distinct();
        return new UriBuilder(base.BuildConnectionUri(assets)) { Query = $"list={string.Join(',', codes.Select(Uri.EscapeDataString))}" }.Uri;
    }

    /// <inheritdoc />
    protected override IEnumerable<string> BuildSubscribeMessages(IReadOnlyCollection<Asset> assets) => [];

    /// <inheritdoc />
    protected override IEnumerable<string> BuildUnsubscribeMessages(IReadOnlyCollection<Asset> assets) => [];

    /// <inheritdoc />
    protected override string KeyOf(Asset asset) => SinaParser.ToRequestCode(asset.Symbol);

    /// <inheritdoc />
    protected override void HandleMessage(string text)
    {
        foreach (var quote in SinaParser.ParseStream(text))
        {
            PublishToSubscribers(quote);
        }
    }

    /// <inheritdoc />
    protected override Task<PollOutcome> FetchQuotesAsync(IReadOnlyCollection<Asset> assets, CancellationToken cancellationToken) =>
        BatchQuoteFetcher.FetchAsync(
            assets,
            SinaParser.ToRequestCode,
            RequestQuotesAsync,
            Publish,
            Logger,
            Descriptor.DisplayName,
            cancellationToken);

    /// <inheritdoc />
    protected override async ValueTask DisposeResourcesAsync()
    {
        await base.DisposeResourcesAsync().ConfigureAwait(false);
        _http.Dispose();
    }

    /// <summary>
    /// Requests one batch of codes from the quote service and parses the answer.
    /// </summary>
    /// <param name="codes">
    /// The codes to request.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the request.
    /// </param>
    /// <returns>
    /// The lines the service answered with a price.
    /// </returns>
    private async Task<IReadOnlyList<ParsedQuote>> RequestQuotesAsync(string[] codes, CancellationToken cancellationToken)
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
