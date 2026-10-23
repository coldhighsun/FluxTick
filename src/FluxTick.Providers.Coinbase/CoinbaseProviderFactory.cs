using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Coinbase;

/// <summary>
/// Creates <see cref="CoinbaseProvider"/> instances.
/// </summary>
public sealed class CoinbaseProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// The provider id.
    /// </summary>
    public const string ProviderId = "coinbase";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Coinbase",
        "Real-time crypto spot prices from Coinbase Exchange, pushed over a public WebSocket. No API key required.",
        [AssetClass.Crypto],
        ProviderUpdateMode.Streaming,
        Homepage: new Uri("https://docs.cdp.coinbase.com/exchange/docs/welcome"),
        Options: ExchangeEndpoints.Options(
            "wss://ws-feed.exchange.coinbase.com",
            "Change this if coinbase.com is unreachable from your network.",
            "https://api.exchange.coinbase.com"),
        EnabledByDefault: false);

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context) =>
        new CoinbaseProvider(
            Descriptor,
            ExchangeEndpoints.Read(context.Options, Descriptor),
            ExchangeEndpoints.CreateHttpClient(),
            context.TimeProvider,
            context.LoggerFactory.CreateLogger<CoinbaseProvider>());
}
