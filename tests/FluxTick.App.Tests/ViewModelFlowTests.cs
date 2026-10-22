using FluxTick.Abstractions;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the main, add-instrument and settings view models working together.
/// </summary>
public class ViewModelFlowTests
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
    /// The Ether test asset.
    /// </summary>
    private static readonly Asset Eth = new(Harness.Crypto, "ETH", "Ether", AssetClass.Crypto);

    /// <summary>
    /// Verifies rows are built from the persisted watchlist in order.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_BuildsRowsInWatchlistOrder()
    {
        await using var h = new Harness(Harness.WatchlistOf(Aapl, Btc));

        await h.Main.InitializeAsync();

        h.Main.Rows.Select(r => r.Symbol).Should().Equal("AAPL", "BTC");
        h.Main.IsEmpty.Should().BeFalse();
        h.Main.Chips.Select(c => c.Name).Should().BeEquivalentTo(["STOCKS", "CRYPTO"]);
    }

    /// <summary>
    /// Verifies a live quote reaches the row.
    /// </summary>
    [Fact]
    public async Task Quote_FromProvider_UpdatesRow()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();

        h.CryptoFactory.Fake.Emit(new Quote(Btc.Id, 105m, 100m, h.Time.GetUtcNow()));

        await Harness.EventuallyAsync(() => h.Main.Rows.Single().PriceText == "105.00");
        h.Main.Rows.Single().ChangeText.Should().Be("+5.00%");
    }

    /// <summary>
    /// Verifies removing and moving rows persists the new watchlist.
    /// </summary>
    [Fact]
    public async Task RemoveAndMove_PersistWatchlist()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Aapl));
        await h.Main.InitializeAsync();

        await h.Main.MoveDownCommand.ExecuteAsync(h.Main.Rows[0]);
        await Harness.EventuallyAsync(() => h.Main.Rows[0].Symbol == "AAPL");
        h.Store.Stored!.Watchlist.Select(e => e.Symbol).Should().Equal("AAPL", "BTC");

        await h.Main.RemoveCommand.ExecuteAsync(h.Main.Rows[0]);
        await Harness.EventuallyAsync(() => h.Main.Rows.Count == 1);
        h.Store.Stored.Watchlist.Select(e => e.Symbol).Should().Equal("BTC");
    }

    /// <summary>
    /// Verifies moving a row to an absolute index reorders the rows and persists the watchlist.
    /// </summary>
    [Fact]
    public async Task MoveToAsync_FirstRowToLastIndex_ReordersAndPersists()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Aapl, Eth));
        await h.Main.InitializeAsync();

        await h.Main.MoveToAsync(h.Main.Rows[0], 2);

        h.Main.Rows.Select(r => r.Symbol).Should().Equal("AAPL", "ETH", "BTC");
        await Harness.EventuallyAsync(() => h.Store.Stored!.Watchlist[0].Symbol == "AAPL");
        h.Store.Stored!.Watchlist.Select(e => e.Symbol).Should().Equal("AAPL", "ETH", "BTC");
    }

    /// <summary>
    /// Verifies a target index past the end is clamped to the last position.
    /// </summary>
    [Fact]
    public async Task MoveToAsync_IndexOutOfRange_ClampsToEnds()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Aapl, Eth));
        await h.Main.InitializeAsync();

        await h.Main.MoveToAsync(h.Main.Rows[0], 99);
        await h.Main.MoveToAsync(h.Main.Rows[1], -5);

        await Harness.EventuallyAsync(() => h.Store.Stored!.Watchlist[0].Symbol == "ETH");
        h.Store.Stored!.Watchlist.Select(e => e.Symbol).Should().Equal("ETH", "AAPL", "BTC");
        h.Main.Rows.Select(r => r.Symbol).Should().Equal("ETH", "AAPL", "BTC");
    }

    /// <summary>
    /// Verifies moving a row to its current index leaves the order untouched.
    /// </summary>
    [Fact]
    public async Task MoveToAsync_SameIndex_KeepsOrder()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Aapl));
        await h.Main.InitializeAsync();

        await h.Main.MoveToAsync(h.Main.Rows[1], 1);

        h.Main.Rows.Select(r => r.Symbol).Should().Equal("BTC", "AAPL");
        h.Store.Stored.Should().NotBeNull();
        h.Store.Stored!.Watchlist.Select(e => e.Symbol).Should().Equal("BTC", "AAPL");
    }

    /// <summary>
    /// Verifies moving a row that has already been removed, leaving an empty watchlist, is ignored rather than throwing.
    /// </summary>
    [Fact]
    public async Task MoveToAsync_RowRemovedFromEmptyWatchlist_IsIgnored()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();
        var staleRow = h.Main.Rows.Single();
        await h.Main.RemoveCommand.ExecuteAsync(staleRow);
        await Harness.EventuallyAsync(() => h.Main.Rows.Count == 0);

        var act = () => h.Main.MoveToAsync(staleRow, 0);

        await act.Should().NotThrowAsync();
        h.Store.Stored!.Watchlist.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the compact setting hides every sparkline.
    /// </summary>
    [Fact]
    public async Task Compact_Toggled_HidesSparklines()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();

        h.Main.Settings.Compact = true;

        h.Main.Rows.Single().ShowSparkline.Should().BeFalse();
        await Harness.EventuallyAsync(() => h.Store.Stored!.Appearance.Compact);
    }

    /// <summary>
    /// Verifies navigation between pages and the derived visibility flags.
    /// </summary>
    [Fact]
    public async Task Navigation_SwitchesPagesAndFlags()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        h.Main.IsWatchlistPage.Should().BeTrue();
        h.Main.IsEmpty.Should().BeTrue();

        h.Main.ShowAddCommand.Execute(null);
        h.Main.IsAddPage.Should().BeTrue();
        h.Main.IsWatchlistPage.Should().BeFalse();

        h.Main.ShowSettingsCommand.Execute(null);
        h.Main.IsSettingsPage.Should().BeTrue();

        h.Main.ShowSettingsCommand.Execute(null);
        h.Main.IsWatchlistPage.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a disabled data source marks its rows instead of dropping them.
    /// </summary>
    [Fact]
    public async Task DisablingProvider_MarksRowsAsSourceOff()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Aapl));
        await h.Main.InitializeAsync();

        var stocks = h.SettingsPage.Providers.Single(p => p.Descriptor.Id == Harness.Stocks);
        stocks.Enabled = false;

        await Harness.EventuallyAsync(() => h.Main.Rows.Single(r => r.Symbol == "AAPL").StatusText == "Source off");
        h.Main.Rows.Single(r => r.Symbol == "BTC").StatusText.Should().BeEmpty();
        h.Store.Stored!.Providers[Harness.Stocks].Enabled.Should().BeFalse();
    }

    /// <summary>
    /// Verifies adding an instrument from search results saves it and returns to the watchlist.
    /// </summary>
    [Fact]
    public async Task AddAsync_FromSearchResult_SavesAndReturns()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        h.StocksFactory.Fake.SearchResults = [Aapl];
        h.Main.ShowAddCommand.Execute(null);
        h.AddAsset.SelectedAssetClass = AssetClass.Equity;
        h.AddAsset.SelectedProvider = h.AddAsset.AvailableProviders.Single(p => p.Id == Harness.Stocks);
        h.AddAsset.Query = "app";
        h.Time.Advance(TimeSpan.FromSeconds(1));

        await Harness.EventuallyAsync(() => h.AddAsset.Results.Count == 1);
        await h.AddAsset.AddCommand.ExecuteAsync(h.AddAsset.Results[0]);

        h.Store.Stored!.Watchlist.Should().ContainSingle().Which.Symbol.Should().Be("AAPL");
        h.Main.IsWatchlistPage.Should().BeTrue();
        await Harness.EventuallyAsync(() => h.Main.Rows.Count == 1);
    }

    /// <summary>
    /// Verifies adding an instrument that is already tracked is refused with a message.
    /// </summary>
    [Fact]
    public async Task AddAsync_Duplicate_ShowsMessageAndStays()
    {
        await using var h = new Harness(Harness.WatchlistOf(Aapl));
        await h.Main.InitializeAsync();
        h.Main.ShowAddCommand.Execute(null);

        await h.AddAsset.AddCommand.ExecuteAsync(Aapl);

        h.AddAsset.Message.Should().Contain("already");
        h.Main.IsAddPage.Should().BeTrue();
        h.Store.Stored!.Watchlist.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies the provider list is limited to enabled sources that support the class, best first.
    /// </summary>
    [Fact]
    public async Task BeginAdd_ListsOnlySourcesSupportingTheClass()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();

        h.AddAsset.BeginAdd();
        h.AddAsset.SelectedAssetClass = AssetClass.Crypto;

        h.AddAsset.AvailableProviders.Select(p => p.Id).Should().Equal(Harness.Crypto);
        h.AddAsset.SelectedProvider!.Id.Should().Be(Harness.Crypto);

        h.AddAsset.SelectedAssetClass = AssetClass.Equity;
        h.AddAsset.AvailableProviders.Select(p => p.Id).Should().Equal(Harness.Stocks, Harness.Plain);
    }

    /// <summary>
    /// Verifies a source that cannot search lets the user add the typed symbol.
    /// </summary>
    [Fact]
    public async Task AddTypedAsync_WithSourceWithoutSearch_AddsUppercasedSymbol()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        h.AddAsset.BeginAdd();
        h.AddAsset.SelectedAssetClass = AssetClass.Equity;
        h.AddAsset.SelectedProvider = h.AddAsset.AvailableProviders.Single(p => p.Id == Harness.Plain);
        h.AddAsset.Query = "msft";

        await Harness.EventuallyAsync(() => !h.AddAsset.CanSearch);
        await h.AddAsset.AddTypedCommand.ExecuteAsync(null);

        var entry = h.Store.Stored!.Watchlist.Should().ContainSingle().Subject;
        (entry.ProviderId, entry.Symbol).Should().Be((Harness.Plain, "MSFT"));
    }

    /// <summary>
    /// Verifies moving an instrument to another data source keeps its position.
    /// </summary>
    [Fact]
    public async Task ChangeSource_ReplacesEntryInPlace()
    {
        var first = new Asset(Harness.Stocks, "A", "A", AssetClass.Equity);
        var second = new Asset(Harness.Stocks, "B", "B", AssetClass.Equity);
        var third = new Asset(Harness.Stocks, "C", "C", AssetClass.Equity);
        await using var h = new Harness(Harness.WatchlistOf(first, second, third));
        await h.Main.InitializeAsync();
        var target = new Asset(Harness.Plain, "B", "B", AssetClass.Equity);

        h.Main.ChangeSourceCommand.Execute(h.Main.Rows[1]);
        h.AddAsset.SelectedProvider.Should().NotBeNull();
        h.AddAsset.SelectedProvider!.Id.Should().Be(Harness.Plain, "the current source is avoided when an alternative exists");
        await h.AddAsset.AddCommand.ExecuteAsync(target);

        h.Store.Stored!.Watchlist.Select(e => (e.ProviderId, e.Symbol)).Should().Equal(
            (Harness.Stocks, "A"),
            (Harness.Plain, "B"),
            (Harness.Stocks, "C"));
    }

    /// <summary>
    /// Verifies the routing choice is persisted as the preferred source followed by the others.
    /// </summary>
    [Fact]
    public async Task Routing_Selection_PersistsOrder()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        var row = h.SettingsPage.Routing.Single(r => r.AssetClass == AssetClass.Equity);

        row.Selected = row.Providers.Single(p => p.Id == Harness.Plain);

        await Harness.EventuallyAsync(() => h.Store.Stored!.Routing.ContainsKey(AssetClass.Equity));
        h.Store.Stored!.Routing[AssetClass.Equity].Should().Equal(Harness.Plain, Harness.Stocks);
        h.Router.GetPreferred(AssetClass.Equity, h.Settings.Current)!.Id.Should().Be(Harness.Plain);
    }

    /// <summary>
    /// Verifies a valid option edit is persisted and an invalid one is not.
    /// </summary>
    [Fact]
    public async Task ProviderOption_EditPersistsOnlyValidValues()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        var provider = h.SettingsPage.Providers.Single(p => p.Descriptor.Id == Harness.Crypto);
        var interval = provider.Options.Single(o => o.Definition.Key == "Interval");

        interval.Text = "30";
        await Harness.EventuallyAsync(() => h.Store.Stored!.Providers.ContainsKey(Harness.Crypto));
        h.Store.Stored!.Providers[Harness.Crypto].Options["Interval"].Should().Be("00:00:30");

        interval.Text = "1";
        interval.Error.Should().NotBeEmpty();
        h.Store.Stored.Providers[Harness.Crypto].Options["Interval"].Should().Be("00:00:30");
    }

    /// <summary>
    /// Verifies the opacity is saved once after the slider settled rather than on every step.
    /// </summary>
    [Fact]
    public async Task Opacity_Dragged_IsSavedOnceAfterQuietPeriod()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();

        h.SettingsPage.Opacity = 0.8;
        h.SettingsPage.Opacity = 0.7;
        h.SettingsPage.Opacity = 0.6;
        h.Store.Stored!.Appearance.Opacity.Should().NotBe(0.6);

        h.Time.Advance(TimeSpan.FromSeconds(1));

        await Harness.EventuallyAsync(() => h.Store.Stored!.Appearance.Opacity == 0.6);
    }
}
