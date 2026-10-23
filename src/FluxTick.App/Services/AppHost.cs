using Avalonia.Threading;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;
using FluxTick.Core.Infrastructure;
using FluxTick.Providers.Binance;
using FluxTick.Providers.Bybit;
using FluxTick.Providers.Coinbase;
using FluxTick.Providers.Kraken;
using FluxTick.Providers.Okx;
using FluxTick.Providers.Simulated;
using FluxTick.Providers.Tencent;
using FluxTick.Providers.Yahoo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace FluxTick.App.Services;

/// <summary>
/// An <see cref="IUiDispatcher"/> backed by the Avalonia UI thread.
/// </summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}

/// <summary>
/// The composition root. Registering a data source is a single line below.
/// </summary>
public static class AppHost
{
    /// <summary>
    /// Builds the service provider.
    /// </summary>
    /// <param name="demo">
    /// Whether the app runs in offline demo mode.
    /// </param>
    /// <returns>
    /// The service provider.
    /// </returns>
    public static ServiceProvider Build(bool demo)
    {
        var services = new ServiceCollection();

        var serilog = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(AppPaths.LogFile, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();
        services.AddLogging(builder => builder.AddSerilog(serilog, dispose: true));

        services.AddFluxTickCore();
        services.AddSingleton<ISettingsStore>(sp =>
            new JsonSettingsStore(AppPaths.SettingsFile(demo), sp.GetRequiredService<ILogger<JsonSettingsStore>>()));

        // Data sources: add one line per provider. Nothing else in the app needs to change.
        services.AddMarketDataProvider<BinanceProviderFactory>();
        services.AddMarketDataProvider<YahooProviderFactory>();
        services.AddMarketDataProvider<TencentProviderFactory>();
        services.AddMarketDataProvider<OkxProviderFactory>();
        services.AddMarketDataProvider<CoinbaseProviderFactory>();
        services.AddMarketDataProvider<KrakenProviderFactory>();
        services.AddMarketDataProvider<BybitProviderFactory>();
        services.AddMarketDataProvider<SimulatedProviderFactory>();

        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<AddAssetViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider();
    }
}
