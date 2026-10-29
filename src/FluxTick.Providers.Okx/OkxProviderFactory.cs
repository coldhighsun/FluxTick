using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Okx;

/// <summary>
/// Creates <see cref="OkxProvider"/> instances.
/// </summary>
public sealed class OkxProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// The provider id.
    /// </summary>
    public const string ProviderId = "okx";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "OKX",
        "Real-time crypto spot prices pushed over a public WebSocket. No API key required.",
        [AssetClass.Crypto],
        ProviderUpdateMode.Streaming,
        Homepage: new Uri("https://www.okx.com/"),
        Options: ExchangeEndpoints.Options(
            "wss://ws.okx.com:8443/ws/v5/public",
            "Change this if okx.com is unreachable from your network.",
            "https://www.okx.com"),
        EnabledByDefault: false);

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context) =>
        new OkxProvider(
            Descriptor,
            ExchangeEndpoints.Read(context.Options, Descriptor),
            ExchangeEndpoints.CreateHttpClient(context.LoggerFactory.CreateLogger<OkxProvider>()),
            context.TimeProvider,
            context.LoggerFactory.CreateLogger<OkxProvider>());
}
