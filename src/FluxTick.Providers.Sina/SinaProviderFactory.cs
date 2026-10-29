using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Sina;

/// <summary>
/// Creates <see cref="SinaProvider"/> instances.
/// </summary>
public sealed class SinaProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// The provider id.
    /// </summary>
    public const string ProviderId = "sina";

    /// <summary>
    /// The option key of the poll interval.
    /// </summary>
    public const string PollIntervalKey = ProviderOptionDefinition.PollIntervalKey;

    /// <summary>
    /// The option key of the stream endpoint.
    /// </summary>
    public const string WebSocketUrlKey = ExchangeEndpoints.WebSocketUrlKey;

    /// <summary>
    /// The option key of the quote endpoint.
    /// </summary>
    public const string QuoteUrlKey = "QuoteUrl";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Sina Finance",
        "A-share stocks, ETFs and indices (Shanghai, Shenzhen, Beijing), currency pairs and international commodity futures from the public quote stream of Sina, with the quote service as the first price and as a fallback while the stream is down. Symbols look like sh600000 or sz000001, the same as Tencent's; currency pairs and futures use the codes of Sina, such as fx_seurusd or hf_GC. Instruments stay with the source they were added from, so to use Sina add them again under it. No API key required. Unofficial; data may be delayed or rate limited.",
        [AssetClass.Equity, AssetClass.Index, AssetClass.Forex, AssetClass.Commodity],
        ProviderUpdateMode.Streaming,
        Homepage: new Uri("https://finance.sina.com.cn/"),
        Options:
        [
            new ProviderOptionDefinition(
                PollIntervalKey,
                "Fallback poll interval",
                ProviderOptionType.Duration,
                "00:00:05",
                "While the live stream is down, how often prices are fetched from the API instead. Nothing is fetched while the market is not open.",
                Min: 2,
                Max: 300),
            StreamingOptions.QuietPollInterval,
            new ProviderOptionDefinition(
                WebSocketUrlKey,
                "WebSocket endpoint",
                ProviderOptionType.String,
                "wss://hq.sinajs.cn/wskt",
                "Used for live prices. If it is unreachable from your network, prices come from the quote endpoint instead."),
            new ProviderOptionDefinition(
                QuoteUrlKey,
                "Quote endpoint",
                ProviderOptionType.String,
                "https://hq.sinajs.cn",
                "Used for prices. Symbol search uses suggest3.sinajs.cn."),
        ],
        EnabledByDefault: false);

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context)
    {
        var quoteUri = EndpointOptions.GetHttpUri(context.Options, Descriptor, QuoteUrlKey);
        var streamUri = EndpointOptions.GetWebSocketUri(context.Options, Descriptor, WebSocketUrlKey);
        var http = AppHttpClient.Create(decompress: true, logger: context.LoggerFactory.CreateLogger<SinaProvider>());
        return new SinaProvider(
            Descriptor,
            quoteUri,
            streamUri,
            context.Options.GetDuration(PollIntervalKey),
            http,
            context.TimeProvider,
            context.LoggerFactory.CreateLogger<SinaProvider>(),
            context.Options.GetDuration(ProviderOptionDefinition.QuietPollIntervalKey));
    }
}
