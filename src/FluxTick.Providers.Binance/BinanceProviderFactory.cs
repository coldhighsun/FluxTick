using FluxTick.Abstractions;
using FluxTick.Providers.Common;
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

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Binance",
        "Real-time crypto spot prices pushed over a public WebSocket. No API key required.",
        [AssetClass.Crypto],
        ProviderUpdateMode.Streaming,
        Homepage: new Uri("https://www.binance.com/"),
        Options: ExchangeEndpoints.Options(
            "wss://stream.binance.com:9443/ws",
            "Use wss://data-stream.binance.vision/ws if binance.com is unreachable from your network.",
            "https://api.binance.com",
            "Used for history and symbol search. Alternative: https://data-api.binance.vision"));

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context) =>
        new BinanceProvider(
            Descriptor,
            ExchangeEndpoints.Read(context.Options, Descriptor),
            ExchangeEndpoints.CreateHttpClient(),
            context.TimeProvider,
            context.LoggerFactory.CreateLogger<BinanceProvider>());
}
