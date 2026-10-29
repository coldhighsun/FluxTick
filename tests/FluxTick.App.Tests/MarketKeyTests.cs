using FluxTick.Abstractions;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the key that decides whether settings are applied to the market data service again.
/// </summary>
public class MarketKeyTests
{
    /// <summary>
    /// Verifies changes that do not concern the instruments or the data sources leave the key as it was.
    /// </summary>
    [Fact]
    public void MarketKeyOf_UnrelatedChange_StaysTheSame()
    {
        var before = Settings();
        var after = before.Clone();
        after.Window.X = 40;
        after.Window.Width = 500;
        after.Appearance.Layout = WatchlistLayout.Cards;
        after.Groups.Add(new WatchGroup { Name = "other" });
        after.Watchlist[0].GroupId = "somewhere";
        after.Watchlist[0].Quantity = 3;

        MainViewModel.MarketKeyOf(after).Should().Be(MainViewModel.MarketKeyOf(before));
    }

    /// <summary>
    /// Verifies adding, removing and reordering instruments changes the key.
    /// </summary>
    [Fact]
    public void MarketKeyOf_WatchlistChanged_Differs()
    {
        var before = Settings();
        var added = before.Clone();
        added.Watchlist.Add(WatchlistEntry.From(new Asset("yahoo", "MSFT", "Microsoft", AssetClass.Equity)));
        var reordered = added.Clone();
        reordered.Watchlist.Reverse();

        var keys = new[] { before, added, reordered }.Select(MainViewModel.MarketKeyOf).ToArray();

        keys.Distinct().Should().HaveCount(3);
    }

    /// <summary>
    /// Verifies the settings of a data source change the key.
    /// </summary>
    [Fact]
    public void MarketKeyOf_ProviderSettingChanged_Differs()
    {
        var before = Settings();
        var after = before.Clone();
        after.Providers["yahoo"] = new ProviderSettings { Enabled = false };

        MainViewModel.MarketKeyOf(after).Should().NotBe(MainViewModel.MarketKeyOf(before));
    }

    /// <summary>
    /// Verifies a change that leaves the instruments and the data sources as they were is not applied to the market data
    /// service again, which would raise every card, while a change of the instruments is.
    /// </summary>
    [Fact]
    public async Task ApplySettings_UnrelatedChange_IsNotAppliedAgain()
    {
        await using var h = new Harness(Harness.WatchlistOf(new Asset(Harness.Stocks, "A", "A", AssetClass.Equity)));
        await h.Main.InitializeAsync();
        var raised = 0;
        h.Market.SnapshotChanged += (_, _) => Interlocked.Increment(ref raised);

        await h.Settings.UpdateAsync(s => s.Window.X = 40);
        await Task.Delay(300);
        raised.Should().Be(0, "the window moving leaves the market data as it was");

        await h.Settings.UpdateAsync(s => s.Watchlist.Add(WatchlistEntry.From(new Asset(Harness.Stocks, "B", "B", AssetClass.Equity))));
        await Harness.EventuallyAsync(() => Volatile.Read(ref raised) > 0);
    }

    /// <summary>
    /// Creates settings with one instrument.
    /// </summary>
    /// <returns>
    /// The settings.
    /// </returns>
    private static AppSettings Settings()
    {
        var settings = new AppSettings();
        settings.Watchlist.Add(WatchlistEntry.From(new Asset("yahoo", "AAPL", "Apple", AssetClass.Equity)));
        return settings;
    }
}
