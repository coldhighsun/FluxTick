using FluxTick.Abstractions;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the tray list: adding from the main window and the editor of the settings page.
/// </summary>
public class TrayAssetsSettingsTests
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
    /// Gets the key of an asset.
    /// </summary>
    /// <param name="asset">
    /// The asset.
    /// </param>
    /// <returns>
    /// The key.
    /// </returns>
    private static string KeyOf(Asset asset) => TrayAssetSelection.Key(asset.ProviderId, asset.Symbol);

    /// <summary>
    /// Verifies the tray list starts empty although the watchlist is not.
    /// </summary>
    [Fact]
    public async Task Default_IsEmpty()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Aapl));

        h.SettingsPage.TrayAssets.Items.Should().BeEmpty();
        h.SettingsPage.TrayAssets.IsEmpty.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the menu command adds an instrument at the end of the list, marks its row, and takes it off again.
    /// </summary>
    [Fact]
    public async Task ToggleTray_AddsAtTheEndThenRemoves()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Aapl));
        await h.Main.InitializeAsync();
        var btc = h.Main.Rows.Single(r => r.Asset.Symbol == "BTC");
        var aapl = h.Main.Rows.Single(r => r.Asset.Symbol == "AAPL");

        await h.Main.Tray.ToggleAsync(aapl);
        await h.Main.Tray.ToggleAsync(btc);

        h.Store.Stored!.General.TrayAssets.Should().Equal(KeyOf(Aapl), KeyOf(Btc));
        await Harness.EventuallyAsync(() => aapl.InTray && btc.InTray);

        await h.Main.Tray.ToggleAsync(aapl);

        h.Store.Stored!.General.TrayAssets.Should().Equal(KeyOf(Btc));
        await Harness.EventuallyAsync(() => !aapl.InTray);
        btc.InTray.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the menu offers adding for an instrument that is not on the tray list and removing for one that is, and
    /// that the row marks the instrument and announces the change when the instrument moves on or off the list.
    /// </summary>
    [Fact]
    public async Task Menu_OffersAddingOrRemovingByMembership()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows can tell where the tray icon is.");

        await using var h = new Harness(Harness.WatchlistOf(Btc, Aapl));
        await h.Main.InitializeAsync();
        var btc = h.Main.Rows.Single(r => r.Asset.Symbol == "BTC");
        var aapl = h.Main.Rows.Single(r => r.Asset.Symbol == "AAPL");
        var announced = new List<string?>();
        btc.PropertyChanged += (_, e) => announced.Add(e.PropertyName);
        (btc.CanAddToTray, btc.CanRemoveFromTray).Should().Be((true, false));
        btc.ShowTrayMarker.Should().BeFalse();

        await h.Main.Tray.ToggleAsync(btc);

        await Harness.EventuallyAsync(() => btc.InTray);
        (btc.CanAddToTray, btc.CanRemoveFromTray).Should().Be((false, true));
        (aapl.CanAddToTray, aapl.CanRemoveFromTray).Should().Be((true, false));
        (btc.ShowTrayMarker, aapl.ShowTrayMarker).Should().Be((true, false));
        announced.Should().Contain([nameof(AssetRowViewModel.CanAddToTray), nameof(AssetRowViewModel.CanRemoveFromTray), nameof(AssetRowViewModel.ShowTrayMarker)]);
    }

    /// <summary>
    /// Verifies a full tray list explains why adding is not possible, and says nothing otherwise.
    /// </summary>
    [Fact]
    public async Task AddTip_ExplainsAFullList()
    {
        await using var h = await FullTrayAsync(extra: 0);

        h.Main.Tray.AddTip.Should().NotBeNullOrEmpty().And.Contain(TrayAssetSelection.MaxAssets.ToString());

        await h.Main.Tray.ToggleAsync(h.Main.Rows.First());

        await Harness.EventuallyAsync(() => h.Main.Tray.AddTip is null);
    }

    /// <summary>
    /// Verifies a full tray list takes nothing more, but still lets an instrument go.
    /// </summary>
    [Fact]
    public async Task Toggle_WhenFull_AddsNothingMore()
    {
        await using var h = await FullTrayAsync(extra: 1);
        var extra = h.Main.Rows.Single(r => !r.InTray);

        await h.Main.Tray.ToggleAsync(extra);

        h.Store.Stored!.General.TrayAssets.Should().HaveCount(TrayAssetSelection.MaxAssets).And.NotContain(TrayAssetSelection.Key(extra.Asset));

        await h.Main.Tray.ToggleAsync(h.Main.Rows.First(r => r.InTray));

        await Harness.EventuallyAsync(() => !h.Main.Tray.IsFull);
    }

    /// <summary>
    /// Verifies the menu command is disabled for an instrument that does not fit on a full list, and enabled for one
    /// that is on it.
    /// </summary>
    [Fact]
    public async Task ToggleCommand_WhenFull_IsDisabledOnlyForAdding()
    {
        await using var h = await FullTrayAsync(extra: 1);

        h.Main.Tray.ToggleCommand.CanExecute(h.Main.Rows.Single(r => !r.InTray)).Should().BeFalse();
        h.Main.Tray.ToggleCommand.CanExecute(h.Main.Rows.First(r => r.InTray)).Should().BeTrue();
    }

    /// <summary>
    /// Creates a started harness whose tray list holds as many instruments as it can, beside some that are not on it.
    /// </summary>
    /// <param name="extra">
    /// The number of instruments on the watchlist that are not on the tray list.
    /// </param>
    /// <returns>
    /// The harness, once the main window knows the list is full.
    /// </returns>
    private static async Task<Harness> FullTrayAsync(int extra)
    {
        var assets = Enumerable.Range(1, TrayAssetSelection.MaxAssets + extra)
            .Select(i => new Asset(Harness.Crypto, $"C{i}", $"Coin {i}", AssetClass.Crypto))
            .ToArray();
        var settings = Harness.WatchlistOf(assets);
        settings.General.TrayAssets = [.. assets.Take(TrayAssetSelection.MaxAssets).Select(KeyOf)];
        var h = new Harness(settings);
        try
        {
            await h.Main.InitializeAsync();
            await Harness.EventuallyAsync(() => h.Main.Tray.IsFull);
            return h;
        }
        catch
        {
            // The caller never gets the harness, so nobody else would dispose it.
            await h.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Verifies the editor lists the chosen instruments in order with their data source.
    /// </summary>
    [Fact]
    public async Task Reload_ListsTheChosenWithTheirSource()
    {
        var settings = Harness.WatchlistOf(Btc, Aapl);
        settings.General.TrayAssets = [KeyOf(Aapl), KeyOf(Btc)];
        await using var h = new Harness(settings);

        var items = h.SettingsPage.TrayAssets.Items;

        items.Select(i => i.Key).Should().Equal(KeyOf(Aapl), KeyOf(Btc));
        items.Should().OnlyContain(i => i.ProviderName.Length > 0);
        h.SettingsPage.TrayAssets.IsEmpty.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the data source names follow the language of the interface.
    /// </summary>
    [Fact]
    public async Task RefreshLanguage_RenamesTheDataSources()
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.General.TrayAssets = [KeyOf(Btc)];
        await using var h = new Harness(settings);
        var tray = h.SettingsPage.TrayAssets;
        var expected = tray.Items.Single().ProviderName;
        tray.Items.Single().ProviderName = "stale";

        tray.RefreshLanguage();

        tray.Items.Single().ProviderName.Should().Be(expected).And.NotBeEmpty();
    }

    /// <summary>
    /// Verifies dragging an instrument is persisted as the new order.
    /// </summary>
    [Fact]
    public async Task MoveTo_PersistsTheNewOrder()
    {
        var settings = Harness.WatchlistOf(Btc, Aapl);
        settings.General.TrayAssets = [KeyOf(Btc), KeyOf(Aapl)];
        await using var h = new Harness(settings);
        var tray = h.SettingsPage.TrayAssets;

        await tray.MoveToAsync(tray.Items[1], 0);

        tray.Items.Select(i => i.Key).Should().Equal(KeyOf(Aapl), KeyOf(Btc));
        h.Store.Stored!.General.TrayAssets.Should().Equal(KeyOf(Aapl), KeyOf(Btc));
    }

    /// <summary>
    /// Verifies an item that the settings no longer hold, because it was taken off the tray list elsewhere while the
    /// editor showed it, can still be dragged and removed without a failure and without changing the settings.
    /// </summary>
    [Fact]
    public async Task StaleItem_MoveAndRemove_DoNotFail()
    {
        var settings = Harness.WatchlistOf(Btc, Aapl);
        settings.General.TrayAssets = [KeyOf(Btc), KeyOf(Aapl)];
        await using var h = new Harness(settings);
        var tray = h.SettingsPage.TrayAssets;
        var stale = tray.Items.ToList();
        await h.Settings.UpdateAsync(s => s.General.TrayAssets.Clear());

        var move = () => tray.MoveToAsync(stale[1], 0);
        var remove = () => tray.RemoveAsync(stale[0]);

        await move.Should().NotThrowAsync();
        await remove.Should().NotThrowAsync();
        h.Settings.Current.General.TrayAssets.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies removing an instrument takes it off the list and persists that.
    /// </summary>
    [Fact]
    public async Task Remove_TakesItOffTheList()
    {
        var settings = Harness.WatchlistOf(Btc, Aapl);
        settings.General.TrayAssets = [KeyOf(Btc), KeyOf(Aapl)];
        await using var h = new Harness(settings);
        var tray = h.SettingsPage.TrayAssets;

        await tray.Items[0].RemoveCommand.ExecuteAsync(null);

        tray.Items.Select(i => i.Key).Should().Equal(KeyOf(Aapl));
        h.Store.Stored!.General.TrayAssets.Should().Equal(KeyOf(Aapl));
        tray.IsEmpty.Should().BeFalse();
    }
}
