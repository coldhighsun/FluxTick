using FluxTick.Abstractions;

namespace FluxTick.Providers.Common;

/// <summary>
/// Reads endpoint options of a provider.
/// </summary>
public static class EndpointOptions
{
    /// <summary>
    /// The schemes a WebSocket endpoint may use.
    /// </summary>
    private static readonly string[] WebSocketSchemes = [Uri.UriSchemeWs, Uri.UriSchemeWss];

    /// <summary>
    /// The schemes an HTTP endpoint may use.
    /// </summary>
    private static readonly string[] HttpSchemes = [Uri.UriSchemeHttp, Uri.UriSchemeHttps];

    /// <summary>
    /// Reads a WebSocket endpoint option, falling back to the declared default when the stored value is not an
    /// absolute <c>ws://</c> or <c>wss://</c> URI.
    /// </summary>
    /// <param name="options">
    /// The provider options.
    /// </param>
    /// <param name="descriptor">
    /// The descriptor holding the option definitions.
    /// </param>
    /// <param name="key">
    /// The option key.
    /// </param>
    /// <returns>
    /// A valid WebSocket URI.
    /// </returns>
    public static Uri GetWebSocketUri(ProviderOptions options, ProviderDescriptor descriptor, string key) =>
        GetUri(options, descriptor, key, WebSocketSchemes);

    /// <summary>
    /// Reads an HTTP endpoint option, falling back to the declared default when the stored value is not an
    /// absolute <c>http://</c> or <c>https://</c> URI.
    /// </summary>
    /// <param name="options">
    /// The provider options.
    /// </param>
    /// <param name="descriptor">
    /// The descriptor holding the option definitions.
    /// </param>
    /// <param name="key">
    /// The option key.
    /// </param>
    /// <returns>
    /// A valid HTTP URI.
    /// </returns>
    public static Uri GetHttpUri(ProviderOptions options, ProviderDescriptor descriptor, string key) =>
        GetUri(options, descriptor, key, HttpSchemes);

    /// <summary>
    /// Appends a request to an endpoint address. The request is added to the endpoint's own path, so an endpoint such as
    /// a proxy under <c>/binance</c> keeps its prefix, whereas resolving a request that starts with <c>/</c> against it
    /// with <see cref="Uri(Uri, string)"/> would replace that path. A query or fragment on the endpoint is dropped.
    /// </summary>
    /// <param name="endpoint">
    /// The endpoint address.
    /// </param>
    /// <param name="request">
    /// The path and query of the request, starting with <c>/</c>.
    /// </param>
    /// <returns>
    /// The absolute request address.
    /// </returns>
    public static Uri Combine(Uri endpoint, string request)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(request);

        return new Uri($"{endpoint.GetLeftPart(UriPartial.Path).TrimEnd('/')}{request}");
    }

    /// <summary>
    /// Reads a URL option and accepts it only with one of the given schemes. A stored value such as
    /// <c>ws.example.com:8443/path</c> parses as an absolute URI whose scheme is <c>ws.example.com</c>, and an
    /// <c>https://</c> address cannot be used for a WebSocket, so both fall back to the default instead of failing
    /// later inside a background loop.
    /// </summary>
    /// <param name="options">
    /// The provider options.
    /// </param>
    /// <param name="descriptor">
    /// The descriptor holding the option definitions.
    /// </param>
    /// <param name="key">
    /// The option key.
    /// </param>
    /// <param name="schemes">
    /// The accepted schemes.
    /// </param>
    /// <returns>
    /// A valid URI.
    /// </returns>
    private static Uri GetUri(ProviderOptions options, ProviderDescriptor descriptor, string key, string[] schemes)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(descriptor);

        return Uri.TryCreate(options.GetString(key), UriKind.Absolute, out var uri) && schemes.Contains(uri.Scheme)
            ? uri
            : new Uri(descriptor.OptionDefinitions.First(o => o.Key == key).DefaultValue);
    }
}
