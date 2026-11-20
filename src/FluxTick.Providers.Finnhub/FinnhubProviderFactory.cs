using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Finnhub;

/// <summary>
/// Creates <see cref="FinnhubProvider"/> instances.
/// </summary>
public sealed class FinnhubProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// The provider id.
    /// </summary>
    public const string ProviderId = "finnhub";

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
    /// The key of the REST endpoint option.
    /// </summary>
    public const string BaseUrlKey = "BaseUrl";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Finnhub",
        "US stocks and ETFs from Finnhub: live trades over a WebSocket, with the REST API as the first price and as a fallback while the stream is down. Needs an API key; the free key allows 60 REST requests a minute and 50 streamed symbols. No chart history on the free plan.",
        [AssetClass.Equity],
        ProviderUpdateMode.Streaming,
        Homepage: new Uri("https://finnhub.io/"),
        Options:
        [
            new ProviderOptionDefinition(
                ApiKeyKey,
                "API key",
                ProviderOptionType.Secret,
                string.Empty,
                "Create a free key at finnhub.io. A free key allows 60 requests a minute and 50 streamed symbols.",
                Required: true),
            new ProviderOptionDefinition(
                PollIntervalKey,
                "Fallback poll interval",
                ProviderOptionType.Duration,
                "00:00:15",
                "While the live stream is down, how often prices are fetched from the API instead, one request per symbol. Nothing is fetched while the market is not open.",
                Min: 5,
                Max: 300),
            StreamingOptions.QuietPollInterval,
            new ProviderOptionDefinition(
                WebSocketUrlKey,
                "WebSocket endpoint",
                ProviderOptionType.String,
                "wss://ws.finnhub.io",
                "Used for live trades. The key is added to the address when connecting.",
                Advanced: true),
            new ProviderOptionDefinition(
                BaseUrlKey,
                "API endpoint",
                ProviderOptionType.String,
                "https://finnhub.io",
                "Used for symbol search, first prices and as a fallback.",
                Advanced: true),
        ],
        EnabledByDefault: false);

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context) =>
        new FinnhubProvider(
            Descriptor,
            EndpointOptions.GetHttpUri(context.Options, Descriptor, BaseUrlKey),
            EndpointOptions.GetWebSocketUri(context.Options, Descriptor, WebSocketUrlKey),
            context.Options.GetString(ApiKeyKey).Trim(),
            context.Options.GetDuration(PollIntervalKey),
            AppHttpClient.Create(decompress: true, logger: context.LoggerFactory.CreateLogger<FinnhubProvider>()),
            context.TimeProvider,
            context.LoggerFactory.CreateLogger<FinnhubProvider>(),
            context.Options.GetDuration(ProviderOptionDefinition.QuietPollIntervalKey));
}
