using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Kraken;

/// <summary>
/// Creates <see cref="KrakenProvider"/> instances.
/// </summary>
public sealed class KrakenProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// The provider id.
    /// </summary>
    public const string ProviderId = "kraken";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Kraken",
        "Real-time crypto spot prices pushed over a public WebSocket. No API key required.",
        [AssetClass.Crypto],
        ProviderUpdateMode.Streaming,
        Homepage: new Uri("https://www.kraken.com/"),
        Options: ExchangeEndpoints.Options(
            "wss://ws.kraken.com/v2",
            "Change this if kraken.com is unreachable from your network.",
            "https://api.kraken.com"),
        EnabledByDefault: false);

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context) =>
        new KrakenProvider(
            Descriptor,
            ExchangeEndpoints.Read(context.Options, Descriptor),
            ExchangeEndpoints.CreateHttpClient(context.LoggerFactory.CreateLogger<KrakenProvider>()),
            context.TimeProvider,
            context.LoggerFactory.CreateLogger<KrakenProvider>());
}
