using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Bybit;

/// <summary>
/// Creates <see cref="BybitProvider"/> instances.
/// </summary>
public sealed class BybitProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// The provider id.
    /// </summary>
    public const string ProviderId = "bybit";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Bybit",
        "Real-time crypto spot prices pushed over a public WebSocket. No API key required.",
        [AssetClass.Crypto],
        ProviderUpdateMode.Streaming,
        Homepage: new Uri("https://bybit-exchange.github.io/docs/v5/intro"),
        Options: ExchangeEndpoints.Options(
            "wss://stream.bybit.com/v5/public/spot",
            "Change this if bybit.com is unreachable from your network. Bybit does not serve some regions.",
            "https://api.bybit.com"),
        EnabledByDefault: false);

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context) =>
        new BybitProvider(
            Descriptor,
            ExchangeEndpoints.Read(context.Options, Descriptor),
            ExchangeEndpoints.CreateHttpClient(),
            context.TimeProvider,
            context.LoggerFactory.CreateLogger<BybitProvider>());
}
