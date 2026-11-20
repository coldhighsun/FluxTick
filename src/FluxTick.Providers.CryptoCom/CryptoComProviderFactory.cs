using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.CryptoCom;

/// <summary>
/// Creates <see cref="CryptoComProvider"/> instances.
/// </summary>
public sealed class CryptoComProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// The provider id.
    /// </summary>
    public const string ProviderId = "cryptocom";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Crypto.com",
        "Real-time crypto spot prices from the Crypto.com Exchange, pushed over a public WebSocket. No API key required.",
        [AssetClass.Crypto],
        ProviderUpdateMode.Streaming,
        Homepage: new Uri("https://crypto.com/exchange"),
        Options: ExchangeEndpoints.Options(
            "wss://stream.crypto.com/exchange/v1/market",
            "Change this if crypto.com is unreachable from your network.",
            "https://api.crypto.com"),
        EnabledByDefault: false);

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context) =>
        new CryptoComProvider(
            Descriptor,
            ExchangeEndpoints.Read(context.Options, Descriptor),
            ExchangeEndpoints.CreateHttpClient(context.LoggerFactory.CreateLogger<CryptoComProvider>()),
            context.TimeProvider,
            context.LoggerFactory.CreateLogger<CryptoComProvider>());
}
