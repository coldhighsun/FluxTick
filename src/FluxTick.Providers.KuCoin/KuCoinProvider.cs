using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.KuCoin;

/// <summary>
/// Streams spot market snapshots from KuCoin. Every connection first asks the REST API for a temporary public token and
/// the address to connect to; the provider reconnects with exponential backoff when the connection drops.
/// </summary>
public sealed class KuCoinProvider : CryptoExchangeProvider
{
    /// <summary>
    /// The most candles one request returns.
    /// </summary>
    private const int MaxCandles = 1500;

    /// <summary>
    /// The REST endpoint, which also hands out the WebSocket token.
    /// </summary>
    private readonly Uri _rest;

    /// <summary>
    /// The HTTP client of the token request; owned by the base class.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// Initializes a new instance of the <see cref="KuCoinProvider"/> class.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    /// <param name="endpoints">
    /// The exchange addresses.
    /// </param>
    /// <param name="http">
    /// The HTTP client for REST calls; the provider takes ownership.
    /// </param>
    /// <param name="timeProvider">
    /// The clock.
    /// </param>
    /// <param name="logger">
    /// The logger.
    /// </param>
    internal KuCoinProvider(
        ProviderDescriptor descriptor,
        ExchangeEndpoints endpoints,
        HttpClient http,
        TimeProvider timeProvider,
        ILogger logger)
        : base(descriptor, endpoints, http, timeProvider, logger)
    {
        _rest = endpoints.Rest;
        _http = http;
    }

    /// <inheritdoc />
    protected override TimeSpan? HeartbeatInterval => TimeSpan.FromSeconds(15);

    /// <inheritdoc />
    protected override string? HeartbeatMessage => KuCoinMessages.Ping;

    /// <inheritdoc />
    protected override string SymbolsRequest => "/api/v2/symbols";

    /// <inheritdoc />
    protected override string HistoryRequest(Asset asset) =>
        CandlesRequest(asset, "1min", HistoryWindow.Span);

    /// <inheritdoc />
    public override bool SupportsRange(HistoryRange range) => true;

    /// <inheritdoc />
    protected override string HistoryRequest(Asset asset, HistoryRange range)
    {
        var spec = HistoryRangeSpec.For(range);
        var window = spec.Capacity > MaxCandles ? spec.Bucket * MaxCandles : spec.Window;
        return CandlesRequest(asset, TypeOf(range), window);
    }

    /// <summary>
    /// Builds a candles request over a time window ending now.
    /// </summary>
    /// <param name="asset">
    /// The instrument.
    /// </param>
    /// <param name="type">
    /// The candle width in the form the endpoint takes.
    /// </param>
    /// <param name="window">
    /// How far back to ask.
    /// </param>
    /// <returns>
    /// The path and query.
    /// </returns>
    private string CandlesRequest(Asset asset, string type, TimeSpan window)
    {
        var end = TimeProvider.GetUtcNow();
        return $"/api/v1/market/candles?symbol={Uri.EscapeDataString(asset.Symbol)}&type={type}&startAt={(end - window).ToUnixTimeSeconds()}&endAt={end.ToUnixTimeSeconds()}";
    }

    /// <summary>
    /// Names the candle width of a range the way the candles endpoint takes it.
    /// </summary>
    /// <param name="range">
    /// The range.
    /// </param>
    /// <returns>
    /// The type, whose width is the bucket of the range.
    /// </returns>
    private static string TypeOf(HistoryRange range) => range switch
    {
        HistoryRange.Day => "5min",
        HistoryRange.FiveDays => "30min",
        HistoryRange.Month => "4hour",
        HistoryRange.HalfYear or HistoryRange.Year => "1day",
        _ => "1min",
    };

    /// <inheritdoc />
    protected override IReadOnlyList<PricePoint> ParseHistory(string json) => KuCoinMessages.ParseCandles(json);

    /// <inheritdoc />
    protected override IReadOnlyList<ListedSymbol> ParseSymbols(string json) => KuCoinMessages.ParseSymbols(json);

    /// <inheritdoc />
    protected override async Task<Uri> ResolveConnectionUriAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointOptions.Combine(_rest, "/api/v1/bullet-public"));
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return KuCoinMessages.BuildConnectionUri(json, uri, Guid.NewGuid().ToString("N"));
    }

    /// <inheritdoc />
    protected override IEnumerable<string> BuildSubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        KuCoinMessages.ControlMessages("subscribe", assets.Select(a => a.Symbol));

    /// <inheritdoc />
    protected override IEnumerable<string> BuildUnsubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        KuCoinMessages.ControlMessages("unsubscribe", assets.Select(a => a.Symbol));

    /// <inheritdoc />
    protected override void HandleMessage(string text)
    {
        if (KuCoinMessages.TryParseSnapshot(text, Descriptor.Id, TimeProvider.GetUtcNow(), out var quote))
        {
            Publish(quote);
        }
        else if (KuCoinMessages.TryParseError(text) is { } error)
        {
            Logger.LogWarning("KuCoin rejected a request: {Error}", error);
        }
    }
}
