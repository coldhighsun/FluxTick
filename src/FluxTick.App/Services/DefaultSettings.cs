using FluxTick.Abstractions;
using FluxTick.Core.Application;
using FluxTick.Providers.Binance;
using FluxTick.Providers.Simulated;
using FluxTick.Providers.Yahoo;

namespace FluxTick.App.Services;

/// <summary>
/// The settings used on first run.
/// </summary>
public static class DefaultSettings
{
    /// <summary>
    /// Creates the first-run settings: a small multi-asset watchlist, or a simulated one in demo mode.
    /// </summary>
    /// <param name="demo">
    /// Whether to create offline demo settings that use only the simulated provider.
    /// </param>
    /// <returns>
    /// The settings.
    /// </returns>
    public static AppSettings Create(bool demo)
    {
        var settings = new AppSettings();
        if (demo)
        {
            settings.Providers[BinanceProviderFactory.ProviderId] = new ProviderSettings { Enabled = false };
            settings.Providers[YahooProviderFactory.ProviderId] = new ProviderSettings { Enabled = false };
            settings.Providers[SimulatedProviderFactory.ProviderId] = new ProviderSettings { Enabled = true };
            const string sim = SimulatedProviderFactory.ProviderId;
            Add(settings, new Asset(sim, "SIM-BTC", "Simulated Bitcoin", AssetClass.Crypto));
            Add(settings, new Asset(sim, "SIM-ETH", "Simulated Ether", AssetClass.Crypto));
            Add(settings, new Asset(sim, "SIM-AAPL", "Simulated Apple", AssetClass.Equity));
            Add(settings, new Asset(sim, "SIM-SPX", "Simulated S&P 500", AssetClass.Index));
            Add(settings, new Asset(sim, "SIM-EURUSD", "Simulated EUR/USD", AssetClass.Forex));
            Add(settings, new Asset(sim, "SIM-GOLD", "Simulated Gold", AssetClass.Commodity));
            return settings;
        }

        const string binance = BinanceProviderFactory.ProviderId;
        const string yahoo = YahooProviderFactory.ProviderId;
        Add(settings, new Asset(binance, "BTCUSDT", "BTC/USDT", AssetClass.Crypto));
        Add(settings, new Asset(binance, "ETHUSDT", "ETH/USDT", AssetClass.Crypto));
        Add(settings, new Asset(yahoo, "AAPL", "Apple Inc.", AssetClass.Equity));
        Add(settings, new Asset(yahoo, "NVDA", "NVIDIA Corporation", AssetClass.Equity));
        Add(settings, new Asset(yahoo, "^GSPC", "S&P 500", AssetClass.Index));
        Add(settings, new Asset(yahoo, "EURUSD=X", "EUR/USD", AssetClass.Forex));
        Add(settings, new Asset(yahoo, "GC=F", "Gold", AssetClass.Commodity));
        return settings;
    }

    /// <summary>
    /// Appends an asset to the watchlist.
    /// </summary>
    /// <param name="settings">
    /// The settings to modify.
    /// </param>
    /// <param name="asset">
    /// The asset to add.
    /// </param>
    private static void Add(AppSettings settings, Asset asset) => settings.Watchlist.Add(WatchlistEntry.From(asset));
}
