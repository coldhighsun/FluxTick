using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Yahoo;

/// <summary>
/// Creates <see cref="YahooProvider"/> instances.
/// </summary>
public sealed class YahooProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// The provider id.
    /// </summary>
    public const string ProviderId = "yahoo";

    /// <summary>
    /// The option key of the poll interval.
    /// </summary>
    public const string PollIntervalKey = ProviderOptionDefinition.PollIntervalKey;

    /// <summary>
    /// The option key of the streamer URL.
    /// </summary>
    public const string WebSocketUrlKey = ExchangeEndpoints.WebSocketUrlKey;

    /// <summary>
    /// The option key of the base URL.
    /// </summary>
    public const string BaseUrlKey = "BaseUrl";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Yahoo Finance",
        "Stocks, ETFs, indices, forex, commodities and crypto from the unofficial Yahoo Finance streamer, with the chart API as the first price and as a fallback while the stream is down. Data may be delayed.",
        [AssetClass.Equity, AssetClass.Index, AssetClass.Forex, AssetClass.Commodity, AssetClass.Crypto],
        ProviderUpdateMode.Streaming,
        Homepage: new Uri("https://finance.yahoo.com/"),
        Options:
        [
            new ProviderOptionDefinition(
                PollIntervalKey,
                "Poll interval",
                ProviderOptionType.Duration,
                "00:00:05",
                "How often prices are refreshed while the live stream is down. Polling too fast may get you rate limited.",
                Min: 2,
                Max: 300),
            new ProviderOptionDefinition(
                WebSocketUrlKey,
                "WebSocket endpoint",
                ProviderOptionType.String,
                "wss://streamer.finance.yahoo.com/?version=2",
                "Used for live prices. If it is unreachable from your network, prices come from the API endpoint instead."),
            new ProviderOptionDefinition(
                BaseUrlKey,
                "API endpoint",
                ProviderOptionType.String,
                "https://query1.finance.yahoo.com",
                "Alternative: https://query2.finance.yahoo.com"),
        ]);

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context)
    {
        var baseUri = EndpointOptions.GetHttpUri(context.Options, Descriptor, BaseUrlKey);
        var streamUri = EndpointOptions.GetWebSocketUri(context.Options, Descriptor, WebSocketUrlKey);

        var http = AppHttpClient.Create(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36",
            decompress: true);

        return new YahooProvider(
            Descriptor,
            baseUri,
            streamUri,
            context.Options.GetDuration(PollIntervalKey),
            http,
            context.TimeProvider,
            context.LoggerFactory.CreateLogger<YahooProvider>());
    }
}
