using FluxTick.Abstractions;

namespace FluxTick.Core.Application;

/// <summary>
/// Decides which data source serves an asset class, based on the user's routing preference and the enabled providers.
/// </summary>
/// <param name="registry">
/// The available providers.
/// </param>
public sealed class ProviderRouter(ProviderRegistry registry)
{
    /// <summary>
    /// Lists the usable providers for an asset class in priority order. Providers the user ranked come first,
    /// followed by every other enabled provider that supports the class.
    /// </summary>
    /// <param name="assetClass">
    /// The asset class.
    /// </param>
    /// <param name="settings">
    /// The current settings.
    /// </param>
    /// <returns>
    /// The provider descriptors, best first; empty when no enabled provider supports the class.
    /// </returns>
    public IReadOnlyList<ProviderDescriptor> GetProviders(AssetClass assetClass, AppSettings settings)
    {
        var usable = registry.Descriptors
            .Where(d => d.Supports(assetClass) && settings.IsProviderEnabled(d))
            .ToList();

        var ordered = new List<ProviderDescriptor>();
        if (settings.Routing.TryGetValue(assetClass, out var preferred))
        {
            foreach (var id in preferred)
            {
                var match = usable.FirstOrDefault(d => d.Id == id);
                if (match is not null && !ordered.Contains(match))
                {
                    ordered.Add(match);
                }
            }
        }

        ordered.AddRange(usable.Where(d => !ordered.Contains(d)));
        return ordered;
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
