using FluxTick.Abstractions;

namespace FluxTick.Providers.Simulated;

/// <summary>
/// Creates <see cref="SimulatedProvider"/> instances.
/// </summary>
public sealed class SimulatedProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// The provider id.
    /// </summary>
    public const string ProviderId = "simulated";

    /// <summary>
    /// The option key of the tick interval.
    /// </summary>
    public const string TickIntervalKey = "TickInterval";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Simulated",
        "Demo data: random-walk prices generated locally. Useful for demos, screenshots and offline development.",
        [AssetClass.Crypto, AssetClass.Equity, AssetClass.Index, AssetClass.Forex, AssetClass.Commodity],
        ProviderUpdateMode.Streaming,
        Options:
        [
            new ProviderOptionDefinition(
                TickIntervalKey,
                "Tick interval",
                ProviderOptionType.Duration,
                "00:00:01",
                "How often new prices are generated.",
                Min: 0.1,
                Max: 60),
        ],
        EnabledByDefault: false);

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context) =>
        new SimulatedProvider(Descriptor, context.Options.GetDuration(TickIntervalKey), context.TimeProvider);
}
