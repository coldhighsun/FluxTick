using FluxTick.Abstractions;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Binance;

/// <summary>
/// Creates <see cref="BinanceProvider"/> instances.
/// </summary>
public sealed class BinanceProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// The provider id.
    /// </summary>
    public const string ProviderId = "binance";

    /// <summary>
    /// The option key of the WebSocket endpoint.
    /// </summary>
    public const string WebSocketUrlKey = "WebSocketUrl";

    /// <summary>
    /// The option key of the REST endpoint.
    /// </summary>
    public const string RestBaseUrlKey = "RestBaseUrl";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Binance",
        "Real-time crypto spot prices pushed over a public WebSocket. No API key required.",
        [AssetClass.Crypto],
        ProviderUpdateMode.Streaming,
        Homepage: new Uri("https://developers.binance.com/docs/binance-spot-api-docs"),
        Options:
        [
            new ProviderOptionDefinition(
                WebSocketUrlKey,
                "WebSocket endpoint",
                ProviderOptionType.String,
                "wss://stream.binance.com:9443/ws",
                "Use wss://data-stream.binance.vision/ws if binance.com is unreachable from your network."),
            new ProviderOptionDefinition(
                RestBaseUrlKey,
                "REST endpoint",
                ProviderOptionType.String,
                "https://api.binance.com",
                "Used for history and symbol search. Alternative: https://data-api.binance.vision"),
        ]);

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context)
    {
        var webSocket = ParseUri(context.Options.GetString(WebSocketUrlKey), Descriptor, WebSocketUrlKey);
        var rest = ParseUri(context.Options.GetString(RestBaseUrlKey), Descriptor, RestBaseUrlKey);
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        return new BinanceProvider(
            Descriptor,
            webSocket,
            rest,
            http,
            context.TimeProvider,
            context.LoggerFactory.CreateLogger<BinanceProvider>());
    }

    /// <summary>
    /// Parses an endpoint option, falling back to the declared default when it is not a valid absolute URI.
    /// </summary>
    /// <param name="value">
    /// The configured value.
    /// </param>
    /// <param name="descriptor">
    /// The descriptor holding the default.
    /// </param>
    /// <param name="key">
    /// The option key.
    /// </param>
    /// <returns>
    /// A valid absolute URI.
    /// </returns>
    private static Uri ParseUri(string value, ProviderDescriptor descriptor, string key) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : new Uri(descriptor.OptionDefinitions.First(o => o.Key == key).DefaultValue);
}
