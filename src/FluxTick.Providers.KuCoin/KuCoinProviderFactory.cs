using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.KuCoin;

/// <summary>
/// Creates <see cref="KuCoinProvider"/> instances.
/// </summary>
public sealed class KuCoinProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// The provider id.
    /// </summary>
    public const string ProviderId = "kucoin";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "KuCoin",
        "Real-time crypto spot prices pushed over a public WebSocket. No API key required: a temporary public token is requested for every connection.",
        [AssetClass.Crypto],
        ProviderUpdateMode.Streaming,
        Homepage: new Uri("https://www.kucoin.com/"),
        Options: ExchangeEndpoints.Options(
            "wss://ws-api-spot.kucoin.com/",
            "Only used when the server does not name an address itself. KuCoin tells the app where to connect when it hands out the temporary token.",
            "https://api.kucoin.com"),
        EnabledByDefault: false,
        Recommendation: 4);

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context) =>
        new KuCoinProvider(
            Descriptor,
            ExchangeEndpoints.Read(context.Options, Descriptor),
            ExchangeEndpoints.CreateHttpClient(context.LoggerFactory.CreateLogger<KuCoinProvider>()),
            context.TimeProvider,
            context.LoggerFactory.CreateLogger<KuCoinProvider>());
}
