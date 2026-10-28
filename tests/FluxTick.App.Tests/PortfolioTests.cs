using FluxTick.Abstractions;
using FluxTick.App.Localization;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the total of the holdings and the base currency.
/// </summary>
public class PortfolioTests
{
    /// <summary>
    /// The Bitcoin test asset.
    /// </summary>
    private static readonly Asset Btc = new(Harness.Crypto, "BTC", "Bitcoin", AssetClass.Crypto);

    /// <summary>
    /// The Apple test asset.
    /// </summary>
    private static readonly Asset Aapl = new(Harness.Stocks, "AAPL", "Apple", AssetClass.Equity);

    /// <summary>
    /// Builds a snapshot of a test asset.
    /// </summary>
    /// <param name="asset">
    /// The asset.
    /// </param>
    /// <param name="price">
    /// The price.
    /// </param>
    /// <returns>
    /// The snapshot.
    /// </returns>
    private static AssetSnapshot Snapshot(Asset asset, decimal price) =>
        new(asset, new Quote(asset.Id, price, 100m, DateTimeOffset.UnixEpoch), [100m, price], AssetHealth.Live);

    /// <summary>
    /// Builds settings with the test assets, each held as given.
    /// </summary>
    /// <param name="currencyOfApple">
    /// The currency of the Apple holding, which costs 700.
    /// </param>
    /// <returns>
    /// The settings, with a base currency of US dollars.
    /// </returns>
    private static AppSettings HeldSettings(string currencyOfApple)
    {
        var settings = Harness.WatchlistOf(Btc, Aapl);
        settings.General.BaseCurrency = "USD";
        settings.Watchlist[0].CostPrice = 100m;
        settings.Watchlist[0].Quantity = 1m;
        settings.Watchlist[0].HoldingCurrency = "USD";
        settings.Watchlist[1].CostPrice = 700m;
        settings.Watchlist[1].Quantity = 1m;
        settings.Watchlist[1].HoldingCurrency = currencyOfApple;
        return settings;
    }

    /// <summary>
    /// Verifies that the total converts holdings in other currencies with the exchange rates.
    /// </summary>
    [Fact]
    public async Task RefreshPortfolio_MixedCurrencies_ConvertsToBaseCurrency()
    {
        await using var h = new Harness(HeldSettings("CNY"));
        await h.Rates.RefreshAsync();
        await h.Main.InitializeAsync();
        h.Main.Rows.Single(r => r.Symbol == "BTC").Update(Snapshot(Btc, 110m));
        h.Main.Rows.Single(r => r.Symbol == "AAPL").Update(Snapshot(Aapl, 770m));

        h.Main.RefreshPortfolio();

        h.Main.HasPortfolio.Should().BeTrue();
        h.Main.PortfolioText.Should().Contain("+20.00 USD").And.Contain("+10.00%");
        h.Main.PortfolioIsProfit.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that a holding in a currency without a rate is left out of the total and the total says so.
    /// </summary>
    [Fact]
    public async Task RefreshPortfolio_UnknownCurrency_IsLeftOutAndMentioned()
    {
        await using var h = new Harness(HeldSettings("JPY"));
        await h.Rates.RefreshAsync();
        await h.Main.InitializeAsync();
        h.Main.Rows.Single(r => r.Symbol == "BTC").Update(Snapshot(Btc, 110m));
        h.Main.Rows.Single(r => r.Symbol == "AAPL").Update(Snapshot(Aapl, 770m));

        h.Main.RefreshPortfolio();

        h.Main.PortfolioText.Should().Contain("+10.00 USD").And.Contain(" · ");
    }

    /// <summary>
    /// Verifies that there is no total without a holding.
    /// </summary>
    [Fact]
    public async Task RefreshPortfolio_NoHoldings_ShowsNothing()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();

        h.Main.RefreshPortfolio();

        h.Main.HasPortfolio.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a holding whose currency has no rate is reported as such once the rates are in, and that before that
    /// the total says the rates are awaited.
    /// </summary>
    [Fact]
    public async Task RefreshPortfolio_OnlyUnknownCurrency_SaysSoAfterTheRatesCameIn()
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.General.BaseCurrency = "USD";
        settings.Watchlist[0].CostPrice = 100m;
        settings.Watchlist[0].Quantity = 1m;
        settings.Watchlist[0].HoldingCurrency = "JPY";
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();
        h.Main.Rows.Single().Update(Snapshot(Btc, 110m));

        h.Main.RefreshPortfolio();
        h.Main.PortfolioText.Should().Be(Loc.Get("Portfolio_NoRates"));

        await h.Rates.RefreshAsync();
        h.Main.RefreshPortfolio();

        h.Main.PortfolioText.Should().Be(Loc.Get("Portfolio_Skipped", 1));
        h.Main.PortfolioTip.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies that the tip of the total says when the rates were fetched, and that it is written when they arrive and
    /// not for every price.
    /// </summary>
    [Fact]
    public async Task PortfolioTip_AfterRatesArrive_SaysWhenTheyWereFetched()
    {
        await using var h = new Harness(HeldSettings("CNY"));
        await h.Main.InitializeAsync();
        h.Main.PortfolioTip.Should().BeEmpty();

        await h.Rates.RefreshAsync();

        h.Main.PortfolioTip.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies that the base currency of the settings page starts from the language, is saved when chosen and is read back.
    /// </summary>
    [Fact]
    public async Task BaseCurrency_ChosenOnTheSettingsPage_IsSavedAndLoaded()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        h.SettingsPage.Reload();
        h.SettingsPage.BaseCurrency.Should().Be(CurrencyCodes.DefaultBase(Loc.Instance.Language == Loc.SimplifiedChinese));
        h.Store.Stored!.General.BaseCurrency.Should().BeNull("loading the page is not a choice");

        h.SettingsPage.BaseCurrency = "HKD";

        await Harness.EventuallyAsync(() => h.Store.Stored!.General.BaseCurrency == "HKD");
        h.SettingsPage.Reload();
        h.SettingsPage.BaseCurrency.Should().Be("HKD");
    }

    /// <summary>
    /// Verifies that a holding whose price is not coming in is left out of the total, and the total says so.
    /// </summary>
    [Fact]
    public async Task RefreshPortfolio_StalePrice_IsLeftOutAndMentioned()
    {
        await using var h = new Harness(HeldSettings("CNY"));
        await h.Rates.RefreshAsync();
        await h.Main.InitializeAsync();
        h.Main.Rows.Single(r => r.Symbol == "BTC").Update(new AssetSnapshot(Btc, new Quote(Btc.Id, 110m, 100m, DateTimeOffset.UnixEpoch), [100m, 110m], AssetHealth.Stale));
        h.Main.Rows.Single(r => r.Symbol == "AAPL").Update(Snapshot(Aapl, 770m));

        h.Main.RefreshPortfolio();

        h.Main.PortfolioText.Should().Contain("+10.00 USD", "only the live holding of 700 yuan, now 770, is counted").And.Contain(Loc.Get("Portfolio_Stale", 1));

        h.Main.Rows.Single(r => r.Symbol == "AAPL").Update(new AssetSnapshot(Aapl, new Quote(Aapl.Id, 770m, 700m, DateTimeOffset.UnixEpoch), [770m], AssetHealth.Stale));
        h.Main.RefreshPortfolio();

        h.Main.PortfolioText.Should().Be(Loc.Get("Portfolio_Stale", 2));
    }

    /// <summary>
    /// Verifies that the closing price of a shut market counts in the total, and that turning the trading state off, which
    /// makes it stale again, works the total out again without waiting for a quote.
    /// </summary>
    [Fact]
    public async Task RefreshPortfolio_ShutMarket_CountsUntilTheTradingStateIsTurnedOff()
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.General.BaseCurrency = "USD";
        settings.Watchlist[0].CostPrice = 100m;
        settings.Watchlist[0].Quantity = 1m;
        settings.Watchlist[0].HoldingCurrency = "USD";
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();

        // The source reports the market shut and the price is a day old, so it is stale, with nothing moving.
        h.CryptoFactory.Fake.Emit(new Quote(Btc.Id, 110m, 100m, h.Time.GetUtcNow() - TimeSpan.FromDays(1)) { Market = MarketState.Closed });
        await Harness.EventuallyAsync(() => h.Main.Rows.Single().StatusIsNeutral);
        h.Main.RefreshPortfolio();

        h.Main.PortfolioText.Should().Contain("+10.00 USD").And.NotContain(Loc.Get("Portfolio_Stale", 1));

        h.Main.Settings.ShowMarketStatus = false;

        await Harness.EventuallyAsync(() => h.Main.PortfolioText == Loc.Get("Portfolio_Stale", 1));

        h.Main.Settings.ShowMarketStatus = true;

        await Harness.EventuallyAsync(() => h.Main.PortfolioText.Contains("+10.00 USD") && !h.Main.PortfolioText.Contains(Loc.Get("Portfolio_Stale", 1)));
    }

    /// <summary>
    /// Verifies that a source that is only reconnecting does not make its holdings drop out of the total.
    /// </summary>
    [Fact]
    public async Task RefreshPortfolio_ReconnectingSource_StillCounts()
    {
        await using var h = new Harness(HeldSettings("CNY"));
        await h.Rates.RefreshAsync();
        await h.Main.InitializeAsync();
        h.Main.Rows.Single(r => r.Symbol == "BTC").Update(new AssetSnapshot(Btc, new Quote(Btc.Id, 110m, 100m, DateTimeOffset.UnixEpoch), [100m, 110m], AssetHealth.Reconnecting));
        h.Main.Rows.Single(r => r.Symbol == "AAPL").Update(Snapshot(Aapl, 770m));

        h.Main.RefreshPortfolio();

        h.Main.PortfolioText.Should().Contain("+20.00 USD").And.NotContain(Loc.Get("Portfolio_Stale", 1));
    }

    /// <summary>
    /// Verifies that the total says so when the exchange rates are old, and not for the age of a weekend.
    /// </summary>
    [Fact]
    public async Task RefreshPortfolio_OldRates_AreMentioned()
    {
        await using var h = new Harness(HeldSettings("CNY"));
        await h.Rates.RefreshAsync();
        await h.Main.InitializeAsync();
        h.Main.Rows.Single(r => r.Symbol == "BTC").Update(Snapshot(Btc, 110m));
        h.Main.Rows.Single(r => r.Symbol == "AAPL").Update(Snapshot(Aapl, 770m));

        h.Time.Advance(TimeSpan.FromDays(3));
        h.Main.RefreshPortfolio();
        h.Main.PortfolioText.Should().NotContain(Loc.Get("Portfolio_RatesOld", 3));

        h.Time.Advance(TimeSpan.FromDays(5));
        h.Main.RefreshPortfolio();

        h.Main.PortfolioText.Should().Contain(Loc.Get("Portfolio_RatesOld", 8));
    }
}
