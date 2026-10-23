using FluxTick.Abstractions;

namespace FluxTick.Providers.Common;

/// <summary>
/// The addresses a crypto exchange provider talks to.
/// </summary>
/// <param name="WebSocket">
/// The streaming endpoint.
/// </param>
/// <param name="Rest">
/// The REST endpoint used for history and symbol search.
/// </param>
public sealed record ExchangeEndpoints(Uri WebSocket, Uri Rest)
{
    /// <summary>
    /// The option key of the WebSocket endpoint.
    /// </summary>
    public const string WebSocketUrlKey = "WebSocketUrl";

    /// <summary>
    /// The option key of the REST endpoint.
    /// </summary>
    public const string RestBaseUrlKey = "RestBaseUrl";

    /// <summary>
    /// Builds the option definitions every exchange provider declares.
    /// </summary>
    /// <param name="webSocketUrl">
    /// The default WebSocket endpoint.
    /// </param>
    /// <param name="webSocketHint">
    /// The help text of the WebSocket option.
    /// </param>
    /// <param name="restBaseUrl">
    /// The default REST endpoint.
    /// </param>
    /// <param name="restHint">
    /// The help text of the REST option.
    /// </param>
    /// <returns>
    /// The two option definitions.
    /// </returns>
    public static IReadOnlyList<ProviderOptionDefinition> Options(
        string webSocketUrl,
        string webSocketHint,
        string restBaseUrl,
        string restHint = "Used for history and symbol search.") =>
    [
        new ProviderOptionDefinition(WebSocketUrlKey, "WebSocket endpoint", ProviderOptionType.String, webSocketUrl, webSocketHint),
        new ProviderOptionDefinition(RestBaseUrlKey, "REST endpoint", ProviderOptionType.String, restBaseUrl, restHint),
    ];

    /// <summary>
    /// Reads the endpoints from the options, falling back to the defaults for values that are not valid addresses.
    /// </summary>
    /// <param name="options">
    /// The provider options.
    /// </param>
    /// <param name="descriptor">
    /// The descriptor holding the option definitions.
    /// </param>
    /// <returns>
    /// The endpoints.
    /// </returns>
    public static ExchangeEndpoints Read(ProviderOptions options, ProviderDescriptor descriptor) =>
        new(
            EndpointOptions.GetWebSocketUri(options, descriptor, WebSocketUrlKey),
            EndpointOptions.GetHttpUri(options, descriptor, RestBaseUrlKey));

    /// <summary>
    /// Creates the HTTP client of a provider: a 15 second timeout and a product user agent, which some exchange APIs
    /// require.
    /// </summary>
    /// <returns>
    /// A new client; the provider that receives it owns and disposes it.
    /// </returns>
    public static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FluxTick");
        return http;
    }
}
