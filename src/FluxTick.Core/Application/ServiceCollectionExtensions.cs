using FluxTick.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxTick.Core.Application;

/// <summary>
/// Dependency injection helpers for registering data sources.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a data source. This single line is all the application needs to offer a new provider.
    /// </summary>
    /// <typeparam name="TFactory">
    /// The factory type of the provider.
    /// </typeparam>
    /// <param name="services">
    /// The service collection.
    /// </param>
    /// <returns>
    /// The same service collection.
    /// </returns>
    public static IServiceCollection AddMarketDataProvider<TFactory>(this IServiceCollection services)
        where TFactory : class, IMarketDataProviderFactory
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMarketDataProviderFactory, TFactory>());
        return services;
    }

    /// <summary>
    /// Registers the provider registry, router and market data service.
    /// </summary>
    /// <param name="services">
    /// The service collection.
    /// </param>
    /// <returns>
    /// The same service collection.
    /// </returns>
    public static IServiceCollection AddFluxTickCore(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ProviderRegistry>();
        services.TryAddSingleton<ProviderRouter>();
        services.TryAddSingleton<MarketDataService>();
        services.TryAddSingleton<SettingsManager>();
        return services;
    }
}
