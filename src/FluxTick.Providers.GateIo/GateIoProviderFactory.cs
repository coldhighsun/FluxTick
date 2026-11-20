using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.GateIo;

/// <summary>
/// Creates <see cref="GateIoProvider"/> instances.
/// </summary>
public sealed class GateIoProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// The provider id.
    /// </summary>
    public const string ProviderId = "gateio";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Gate.io",
        "Real-time crypto spot prices pushed over a public WebSocket. No API key required.",
        [AssetClass.Crypto],
        ProviderUpdateMode.Streaming,
        Homepage: new Uri("https://www.gate.com/"),
        Options: ExchangeEndpoints.Options(
            "wss://api.gateio.ws/ws/v4/",
            "Change this if gateio.ws is unreachable from your network.",
            "https://api.gateio.ws"),
        EnabledByDefault: false);

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context) =>
        new GateIoProvider(
            Descriptor,
            ExchangeEndpoints.Read(context.Options, Descriptor),
            ExchangeEndpoints.CreateHttpClient(context.LoggerFactory.CreateLogger<GateIoProvider>()),
            context.TimeProvider,
            context.LoggerFactory.CreateLogger<GateIoProvider>());
}
