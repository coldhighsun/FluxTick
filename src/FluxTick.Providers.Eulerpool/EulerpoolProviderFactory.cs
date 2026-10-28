using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Eulerpool;

/// <summary>
/// Creates <see cref="EulerpoolProvider"/> instances.
/// </summary>
public sealed class EulerpoolProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// The provider id.
    /// </summary>
    public const string ProviderId = "eulerpool";

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
        "Eulerpool",
        "Stocks and ETFs (no indices) from the Eulerpool market data API, with the REST API as the first price and as a fallback while the stream is down. Needs an API key; a free key receives delayed one-second prices.",
        [AssetClass.Equity],
        ProviderUpdateMode.Streaming,
        Homepage: new Uri("https://eulerpool.com/"),
        Options:
        [
            new ProviderOptionDefinition(
                ApiKeyKey,
                "API key",
                ProviderOptionType.Secret,
                string.Empty,
                "Create a free key at eulerpool.com/developers. A free key allows 100,000 requests a month.",
                Required: true),
            new ProviderOptionDefinition(
                PollIntervalKey,
                "Fallback poll interval",
                ProviderOptionType.Duration,
                "00:00:10",
                "While the live stream is down, how often prices are fetched from the API instead. Nothing is fetched while the market is not open. Every fetch counts against the monthly request limit of the key.",
                Min: 5,
                Max: 300),
            StreamingOptions.QuietPollInterval,
            new ProviderOptionDefinition(
                WebSocketUrlKey,
                "WebSocket endpoint",
                ProviderOptionType.String,
                "wss://api.eulerpool.com/v1/subscribe",
                "Used for live prices. The documented host stream.eulerpool.com refuses connections from applications."),
            new ProviderOptionDefinition(
                BaseUrlKey,
                "API endpoint",
                ProviderOptionType.String,
                "https://api.eulerpool.com",
                "Used for symbol search, first prices and as a fallback."),
        ],
        EnabledByDefault: false);

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context)
    {
        var baseUri = EndpointOptions.GetHttpUri(context.Options, Descriptor, BaseUrlKey);
        var streamUri = EndpointOptions.GetWebSocketUri(context.Options, Descriptor, WebSocketUrlKey);

        return new EulerpoolProvider(
            Descriptor,
            baseUri,
            streamUri,
            context.Options.GetString(ApiKeyKey).Trim(),
            context.Options.GetDuration(PollIntervalKey),
            AppHttpClient.Create(decompress: true),
            context.TimeProvider,
            context.LoggerFactory.CreateLogger<EulerpoolProvider>(),
            context.Options.GetDuration(ProviderOptionDefinition.QuietPollIntervalKey));
    }
}
