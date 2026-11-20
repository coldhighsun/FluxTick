using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.TwelveData;

/// <summary>
/// Creates <see cref="TwelveDataProvider"/> instances.
/// </summary>
public sealed class TwelveDataProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// The provider id.
    /// </summary>
    public const string ProviderId = "twelvedata";

    /// <summary>
    /// The key of the API key option.
    /// </summary>
    public const string ApiKeyKey = "ApiKey";

    /// <summary>
    /// The key of the poll interval option.
    /// </summary>
    public const string PollIntervalKey = ProviderOptionDefinition.PollIntervalKey;

    /// <summary>
    /// The key of the WebSocket endpoint option.
    /// </summary>
    public const string WebSocketUrlKey = ExchangeEndpoints.WebSocketUrlKey;

    /// <summary>
    /// The key of the API endpoint option.
    /// </summary>
    public const string BaseUrlKey = "BaseUrl";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Twelve Data",
        "Stocks, ETFs and indices worldwide from Twelve Data: prices pushed over a WebSocket, with the REST API as the first price and as a fallback while the stream is down. Needs an API key; a free key allows 8 credits a minute and 800 a day for REST, and a WebSocket with a few symbols. Free prices may be delayed.",
        [AssetClass.Equity, AssetClass.Index],
        ProviderUpdateMode.Streaming,
        Homepage: new Uri("https://twelvedata.com/"),
        Options:
        [
            new ProviderOptionDefinition(
                ApiKeyKey,
                "API key",
                ProviderOptionType.Secret,
                string.Empty,
                "Create a free key at twelvedata.com. A free key allows 8 credits a minute and 800 a day; every symbol of every refresh, and every chart request, costs a credit.",
                Required: true),
            new ProviderOptionDefinition(
                PollIntervalKey,
                "Fallback poll interval",
                ProviderOptionType.Duration,
                "00:01:00",
                "While the live stream is down, how often prices are fetched from the API instead. A request costs one credit per symbol; with the free key, keep (symbols x 60 seconds / 8) below this value.",
                Min: 5,
                Max: 3600),
            StreamingOptions.QuietPollInterval with { DefaultValue = "00:30:00" },
            new ProviderOptionDefinition(
                WebSocketUrlKey,
                "WebSocket endpoint",
                ProviderOptionType.String,
                "wss://ws.twelvedata.com/v1/quotes/price",
                "Used for live prices. The key is added to the address when connecting.",
                Advanced: true),
            new ProviderOptionDefinition(
                BaseUrlKey,
                "API endpoint",
                ProviderOptionType.String,
                "https://api.twelvedata.com",
                "Used for first prices, history, symbol search and as a fallback.",
                Advanced: true),
        ],
        EnabledByDefault: false,
        Recommendation: 3);

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context) =>
        new TwelveDataProvider(
            Descriptor,
            EndpointOptions.GetHttpUri(context.Options, Descriptor, BaseUrlKey),
            EndpointOptions.GetWebSocketUri(context.Options, Descriptor, WebSocketUrlKey),
            context.Options.GetString(ApiKeyKey).Trim(),
            context.Options.GetDuration(PollIntervalKey),
            AppHttpClient.Create(decompress: true, logger: context.LoggerFactory.CreateLogger<TwelveDataProvider>()),
            context.TimeProvider,
            context.LoggerFactory.CreateLogger<TwelveDataProvider>(),
            context.Options.GetDuration(ProviderOptionDefinition.QuietPollIntervalKey));
}
