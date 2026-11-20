using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Bitget;

/// <summary>
/// Creates <see cref="BitgetProvider"/> instances.
/// </summary>
public sealed class BitgetProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// The provider id.
    /// </summary>
    public const string ProviderId = "bitget";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Bitget",
        "Real-time crypto spot prices pushed over a public WebSocket. No API key required.",
        [AssetClass.Crypto],
        ProviderUpdateMode.Streaming,
        Homepage: new Uri("https://www.bitget.com/"),
        Options: ExchangeEndpoints.Options(
            "wss://ws.bitget.com/v2/ws/public",
            "Change this if bitget.com is unreachable from your network. Bitget does not serve some regions.",
            "https://api.bitget.com"),
        EnabledByDefault: false);

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context) =>
        new BitgetProvider(
            Descriptor,
            ExchangeEndpoints.Read(context.Options, Descriptor),
            ExchangeEndpoints.CreateHttpClient(context.LoggerFactory.CreateLogger<BitgetProvider>()),
            context.TimeProvider,
            context.LoggerFactory.CreateLogger<BitgetProvider>());
}
