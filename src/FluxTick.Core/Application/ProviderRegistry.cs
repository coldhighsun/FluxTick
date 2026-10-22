using FluxTick.Abstractions;

namespace FluxTick.Core.Application;

/// <summary>
/// The set of data sources available to the application, collected from every registered factory.
/// </summary>
public sealed class ProviderRegistry
{
    /// <summary>
    /// The factories keyed by provider id.
    /// </summary>
    private readonly Dictionary<string, IMarketDataProviderFactory> _factories;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderRegistry"/> class.
    /// </summary>
    /// <param name="factories">
    /// All available factories.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Two factories declare the same provider id.
    /// </exception>
    public ProviderRegistry(IEnumerable<IMarketDataProviderFactory> factories)
    {
        _factories = new Dictionary<string, IMarketDataProviderFactory>(StringComparer.Ordinal);
        foreach (var factory in factories)
        {
            if (!_factories.TryAdd(factory.Descriptor.Id, factory))
            {
                throw new ArgumentException($"Duplicate provider id '{factory.Descriptor.Id}'.", nameof(factories));
            }
        }
    }

    /// <summary>
    /// Gets the descriptors of all providers in registration order.
    /// </summary>
    public IReadOnlyList<ProviderDescriptor> Descriptors => _factories.Values.Select(f => f.Descriptor).ToArray();

    /// <summary>
    /// Finds a factory by provider id.
    /// </summary>
    /// <param name="providerId">
    /// The provider id.
    /// </param>
    /// <param name="factory">
    /// The factory when found.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the provider is known.
    /// </returns>
    public bool TryGetFactory(string providerId, out IMarketDataProviderFactory factory) =>
        _factories.TryGetValue(providerId, out factory!);

    /// <summary>
    /// Finds a descriptor by provider id.
    /// </summary>
    /// <param name="providerId">
    /// The provider id.
    /// </param>
    /// <returns>
    /// The descriptor, or <see langword="null"/> when the provider is unknown.
    /// </returns>
    public ProviderDescriptor? GetDescriptor(string providerId) =>
        _factories.TryGetValue(providerId, out var factory) ? factory.Descriptor : null;
}
