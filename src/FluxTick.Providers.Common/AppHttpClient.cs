using System.Net;

namespace FluxTick.Providers.Common;

/// <summary>
/// Builds the HTTP clients of the app: those of the providers and the one of the exchange rate service, so that they share
/// one set-up. It is in the project every one of them can reach, which is this one.
/// </summary>
public static class AppHttpClient
{
    /// <summary>
    /// How long a request may take.
    /// </summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Creates an HTTP client with the common timeout.
    /// </summary>
    /// <param name="userAgent">
    /// The user agent to send, or <see langword="null"/> to send none.
    /// </param>
    /// <param name="decompress">
    /// Whether compressed answers are asked for and unpacked.
    /// </param>
    /// <param name="connectionLifetime">
    /// How long a connection is reused before a new one is made, which lets a client that lives for long follow a change
    /// of the address of the server, or <see langword="null"/> to keep the default (unlimited).
    /// </param>
    /// <returns>
    /// A new client, which owns its handler; whoever receives it disposes it.
    /// </returns>
    public static HttpClient Create(string? userAgent = null, bool decompress = false, TimeSpan? connectionLifetime = null)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = decompress ? DecompressionMethods.All : DecompressionMethods.None,
        };
        if (connectionLifetime is { } lifetime)
        {
            handler.PooledConnectionLifetime = lifetime;
        }

        var http = new HttpClient(handler) { Timeout = RequestTimeout };
        if (userAgent is not null)
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        }

        return http;
    }
}
