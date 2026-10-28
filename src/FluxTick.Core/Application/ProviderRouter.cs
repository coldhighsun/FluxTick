using FluxTick.Abstractions;

namespace FluxTick.Core.Application;

/// <summary>
/// Decides which data sources can serve an asset class: the enabled ones that support it.
/// </summary>
/// <param name="registry">
/// The available providers.
/// </param>
public sealed class ProviderRouter(ProviderRegistry registry)
{
    /// <summary>
    /// Lists the usable providers for an asset class: the enabled providers that support the class, in the order they
    /// were registered. The caller orders them for display.
    /// </summary>
    /// <param name="assetClass">
    /// The asset class.
    /// </param>
    /// <param name="settings">
    /// The current settings.
    /// </param>
    /// <returns>
    /// The provider descriptors; empty when no enabled provider supports the class.
    /// </returns>
    public IReadOnlyList<ProviderDescriptor> GetProviders(AssetClass assetClass, AppSettings settings) =>
        [.. registry.Descriptors.Where(d => d.Supports(assetClass) && settings.IsProviderEnabled(d))];
}
