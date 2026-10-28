using FluxTick.Abstractions;

namespace FluxTick.Core.Application;

/// <summary>
/// Decides which data source serves an asset class, based on the order of the data sources the user arranged and which are enabled.
/// </summary>
/// <param name="registry">
/// The available providers.
/// </param>
public sealed class ProviderRouter(ProviderRegistry registry)
{
    /// <summary>
    /// Lists the usable providers for an asset class in priority order: the enabled providers that support the class, in
    /// the order the user arranged the data sources.
    /// </summary>
    /// <param name="assetClass">
    /// The asset class.
    /// </param>
    /// <param name="settings">
    /// The current settings.
    /// </param>
    /// <returns>
    /// The provider descriptors, the best first; empty when no enabled provider supports the class.
    /// </returns>
    public IReadOnlyList<ProviderDescriptor> GetProviders(AssetClass assetClass, AppSettings settings)
    {
        var usable = registry.Descriptors
            .Where(d => d.Supports(assetClass) && settings.IsProviderEnabled(d))
            .ToDictionary(d => d.Id);
        return [.. ProviderOrdering.Sort(usable.Keys, settings.ProviderOrder).Select(id => usable[id])];
    }

    /// <summary>
    /// Gets the preferred provider for an asset class.
    /// </summary>
    /// <param name="assetClass">
    /// The asset class.
    /// </param>
    /// <param name="settings">
    /// The current settings.
    /// </param>
    /// <returns>
    /// The preferred provider, or <see langword="null"/> when none is available.
    /// </returns>
    public ProviderDescriptor? GetPreferred(AssetClass assetClass, AppSettings settings) =>
        GetProviders(assetClass, settings).FirstOrDefault();
}
