using FluxTick.Abstractions;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the sections of the card layout: grouping, collapsing and moving cards between them.
/// </summary>
public class CardSectionTests
{
    /// <summary>
    /// The Bitcoin test asset.
    /// </summary>
    private static readonly Asset Btc = new(Harness.Crypto, "BTC", "Bitcoin", AssetClass.Crypto);

    /// <summary>
    /// The Ether test asset.
    /// </summary>
    private static readonly Asset Eth = new(Harness.Crypto, "ETH", "Ether", AssetClass.Crypto);

    /// <summary>
    /// The Apple test asset.
    /// </summary>
    private static readonly Asset Aapl = new(Harness.Stocks, "AAPL", "Apple", AssetClass.Equity);

    /// <summary>
    /// The index test asset.
    /// </summary>
    private static readonly Asset Spx = new(Harness.Stocks, "SPX", "S&P 500", AssetClass.Index);

    /// <summary>
    /// Creates settings for the card layout with the given grouping.
    /// </summary>
    /// <param name="grouping">
    /// The grouping to use.
    /// </param>
    /// <param name="assets">
    /// The instruments on the watchlist.
    /// </param>
    /// <returns>
    /// The settings.
    /// </returns>
    private static AppSettings Cards(CardGrouping grouping, params Asset[] assets)
    {
        var settings = Harness.WatchlistOf(assets);
        settings.Appearance.Layout = WatchlistLayout.Cards;
        settings.Appearance.Grouping = grouping;
        return settings;
    }

    /// <summary>
    /// Adds a group to the settings and assigns instruments to it.
    /// </summary>
    /// <param name="settings">
    /// The settings to change.
    /// </param>
    /// <param name="id">
    /// The group id.
    /// </param>
    /// <param name="members">
    /// The symbols of the instruments that belong to the group.
    /// </param>
    private static void AddGroup(AppSettings settings, string id, params string[] members)
    {
        settings.Groups.Add(new WatchGroup { Id = id, Name = id.ToUpperInvariant() });
        foreach (var entry in settings.Watchlist.Where(e => members.Contains(e.Symbol)))
        {
            entry.GroupId = id;
        }
    }

    /// <summary>
    /// Describes the sections as "key: symbols" lines.
    /// </summary>
    /// <param name="main">
    /// The main view model.
    /// </param>
    /// <returns>
    /// One line per section.
    /// </returns>
    private static string[] Describe(MainViewModel main) =>
        [.. main.Sections.Select(s => $"{s.Key}: {string.Join(",", s.Items.Select(i => i.Symbol))}")];

    /// <summary>
    /// Verifies the list layout, which is the default, builds no sections.
    /// </summary>
    [Fact]
    public async Task Sections_ListLayout_AreEmpty()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Aapl));

        await h.Main.InitializeAsync();

        h.Main.IsCardLayout.Should().BeFalse();
        h.Main.Sections.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies cards without grouping form a single section without a header, in watchlist order.
    /// </summary>
    [Fact]
    public async Task Sections_CardsWithoutGrouping_AreOneHeaderlessSection()
    {
        await using var h = new Harness(Cards(CardGrouping.None, Btc, Aapl, Eth));

        await h.Main.InitializeAsync();

        h.Main.IsCardLayout.Should().BeTrue();
        var section = h.Main.Sections.Should().ContainSingle().Subject;
        section.ShowHeader.Should().BeFalse();
        section.Items.Select(i => i.Symbol).Should().Equal("BTC", "AAPL", "ETH");
        section.Count.Should().Be(3);
    }

    /// <summary>
    /// Verifies grouping by type orders the sections by asset class, keeps watchlist order inside each and hides
    /// the classes that have no instrument.
    /// </summary>
    [Fact]
    public async Task Sections_ByType_GroupsInClassOrderAndHidesEmptyClasses()
    {
        await using var h = new Harness(Cards(CardGrouping.ByType, Aapl, Btc, Spx, Eth));

        await h.Main.InitializeAsync();

        Describe(h.Main).Should().Equal("type:Crypto: BTC,ETH", "type:Equity: AAPL", "type:Index: SPX");
        h.Main.Sections.Select(s => s.Title).Should().Equal("Crypto", "Equity", "Index");
        h.Main.Sections.Should().OnlyContain(s => s.ShowHeader);
    }

    /// <summary>
    /// Verifies custom grouping lists every group in its order, including empty ones, and that instruments that
    /// were never put in a group join the first group.
    /// </summary>
    [Fact]
    public async Task Sections_Custom_ListsGroupsEmptyOnesAndPutsLooseInstrumentsInFirstGroup()
    {
        var settings = Cards(CardGrouping.Custom, Btc, Aapl, Eth, Spx);
        AddGroup(settings, "tech", "AAPL");
        AddGroup(settings, "empty");
        AddGroup(settings, "coins", "BTC", "ETH");
        await using var h = new Harness(settings);

        await h.Main.InitializeAsync();

        Describe(h.Main).Should().Equal("group:tech: AAPL,SPX", "group:empty: ", "group:coins: BTC,ETH");
        h.Main.Sections.Select(s => s.Title).Should().Equal("TECH", "EMPTY", "COINS");
    }

    /// <summary>
    /// Verifies a default group is created when there is none, and holds every instrument.
    /// </summary>
    [Fact]
    public async Task Sections_CustomWithoutGroups_CreatesDefaultGroup()
    {
        await using var h = new Harness(Cards(CardGrouping.Custom, Btc, Aapl));

        await h.Main.InitializeAsync();

        var section = h.Main.Sections.Should().ContainSingle().Subject;
        section.Title.Should().Be(AppSettings.DefaultGroupName);
        section.Items.Select(i => i.Symbol).Should().Equal("BTC", "AAPL");
        h.Store.Stored!.Groups.Should().ContainSingle().Which.Name.Should().Be(AppSettings.DefaultGroupName);
        h.Store.Stored.Watchlist.Should().OnlyContain(e => e.GroupId == h.Store.Stored.Groups[0].Id);
    }

    /// <summary>
    /// Verifies an instrument that refers to a group which no longer exists joins the first group.
    /// </summary>
    [Fact]
    public async Task Sections_CustomWithUnknownGroupId_JoinsFirstGroup()
    {
        var settings = Cards(CardGrouping.Custom, Btc, Aapl);
        AddGroup(settings, "coins", "BTC");
        settings.Watchlist[1].GroupId = "deleted";
        await using var h = new Harness(settings);

        await h.Main.InitializeAsync();

        Describe(h.Main).Should().Equal("group:coins: BTC,AAPL");
    }

    /// <summary>
    /// Verifies the layout and grouping chosen in the settings rebuild the sections and are persisted.
    /// </summary>
    [Fact]
    public async Task LayoutAndGrouping_ChangedInSettings_RebuildSectionsAndPersist()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Aapl));
        await h.Main.InitializeAsync();

        h.Main.Settings.SelectedLayout = WatchlistLayout.Cards;
        h.Main.Settings.SelectedGrouping = CardGrouping.ByType;

        h.Main.IsCardLayout.Should().BeTrue();
        h.Main.Grouping.Should().Be(CardGrouping.ByType);
        Describe(h.Main).Should().Equal("type:Crypto: BTC", "type:Equity: AAPL");
        await Harness.EventuallyAsync(() => h.Store.Stored!.Appearance is { Layout: WatchlistLayout.Cards, Grouping: CardGrouping.ByType });

        h.Main.Settings.SelectedLayout = WatchlistLayout.List;

        h.Main.Sections.Should().BeEmpty();
        await Harness.EventuallyAsync(() => h.Store.Stored!.Appearance.Layout == WatchlistLayout.List);
    }

    /// <summary>
    /// Verifies the persisted layout is applied when the application starts.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_WithPersistedCardLayout_BuildsSections()
    {
        await using var h = new Harness(Cards(CardGrouping.ByType, Btc));

        await h.Main.InitializeAsync();

        h.Main.IsCardLayout.Should().BeTrue();
        h.Main.Settings.SelectedLayout.Should().Be(WatchlistLayout.Cards);
        h.Main.Sections.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies collapsing a type section is persisted and restored.
    /// </summary>
    [Fact]
    public async Task IsCollapsed_TypeSectionToggled_PersistsAndRestores()
    {
        await using var h = new Harness(Cards(CardGrouping.ByType, Btc, Aapl));
        await h.Main.InitializeAsync();

        h.Main.Sections[0].IsCollapsed = true;

        await Harness.EventuallyAsync(() => h.Store.Stored!.Appearance.CollapsedTypes.Contains(AssetClass.Crypto));
        h.Store.Stored!.Appearance.CollapsedTypes.Should().Equal(AssetClass.Crypto);
        h.Main.Sections[1].IsCollapsed.Should().BeFalse();

        var restored = Cards(CardGrouping.ByType, Btc, Aapl);
        restored.Appearance.CollapsedTypes.Add(AssetClass.Equity);
        await using var h2 = new Harness(restored);
        await h2.Main.InitializeAsync();
        h2.Main.Sections.Select(s => s.IsCollapsed).Should().Equal(false, true);

        h.Main.Sections[0].IsCollapsed = false;
        await Harness.EventuallyAsync(() => h.Store.Stored!.Appearance.CollapsedTypes.Count == 0);
    }

    /// <summary>
    /// Verifies collapsing a custom group is persisted on the group.
    /// </summary>
    [Fact]
    public async Task IsCollapsed_CustomSectionsToggled_Persist()
    {
        var settings = Cards(CardGrouping.Custom, Btc, Aapl);
        AddGroup(settings, "coins", "BTC");
        AddGroup(settings, "stocks", "AAPL");
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();

        h.Main.Sections[1].IsCollapsed = true;

        await Harness.EventuallyAsync(() => h.Store.Stored!.Groups[1].Collapsed);
        h.Store.Stored!.Groups[0].Collapsed.Should().BeFalse();
        h.Store.Stored.Appearance.CollapsedTypes.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies rebuilding the sections after a price tick or an unrelated setting keeps the section instances.
    /// </summary>
    [Fact]
    public async Task Sections_UnrelatedSettingChanged_KeepsInstances()
    {
        await using var h = new Harness(Cards(CardGrouping.ByType, Btc, Aapl));
        await h.Main.InitializeAsync();
        var before = h.Main.Sections.ToList();
        var cards = before.SelectMany(s => s.Items).ToList();

        h.Main.Settings.Locked = true;
        await Harness.EventuallyAsync(() => h.Store.Stored!.Appearance.Locked);

        h.Main.Sections.Should().Equal(before);
        h.Main.Sections.SelectMany(s => s.Items).Should().Equal(cards);
    }

    /// <summary>
    /// Verifies assigning an instrument to a group appends it to that group and persists the assignment.
    /// </summary>
    [Fact]
    public async Task MoveToGroupAsync_Group_AppendsAndPersists()
    {
        var settings = Cards(CardGrouping.Custom, Btc, Aapl, Eth);
        AddGroup(settings, "coins", "BTC");
        AddGroup(settings, "other", "AAPL");
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();
        Describe(h.Main).Should().Equal("group:coins: BTC,ETH", "group:other: AAPL");

        await h.Main.MoveToGroupAsync(h.Main.Rows.Single(r => r.Symbol == "ETH"), "other");

        Describe(h.Main).Should().Equal("group:coins: BTC", "group:other: AAPL,ETH");
        h.Store.Stored!.Watchlist.Select(e => (e.Symbol, e.GroupId)).Should().Equal(("BTC", "coins"), ("AAPL", "other"), ("ETH", "other"));
    }

    /// <summary>
    /// Verifies assigning to a group that does not exist changes nothing.
    /// </summary>
    [Fact]
    public async Task MoveToGroupAsync_UnknownGroup_IsIgnored()
    {
        var settings = Cards(CardGrouping.Custom, Btc);
        AddGroup(settings, "home", "BTC");
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();

        await h.Main.MoveToGroupAsync(h.Main.Rows.Single(), "missing");

        h.Store.Stored!.Watchlist.Single().GroupId.Should().Be("home");
        Describe(h.Main).Should().Equal("group:home: BTC");
    }

    /// <summary>
    /// Verifies a card dropped inside its own type section takes the requested position and the watchlist follows.
    /// </summary>
    [Fact]
    public async Task MoveCardAsync_WithinTypeSection_ReordersAndPersists()
    {
        await using var h = new Harness(Cards(CardGrouping.ByType, Btc, Aapl, Eth));
        await h.Main.InitializeAsync();
        var crypto = h.Main.Sections[0];

        await h.Main.MoveCardAsync(crypto.Items[0], crypto, 1);

        Describe(h.Main).Should().Equal("type:Crypto: ETH,BTC", "type:Equity: AAPL");
        h.Store.Stored!.Watchlist.Select(e => e.Symbol).Should().Equal("AAPL", "ETH", "BTC");
    }

    /// <summary>
    /// Verifies a card cannot be dropped into the section of another type.
    /// </summary>
    [Fact]
    public async Task MoveCardAsync_ToOtherTypeSection_IsIgnored()
    {
        await using var h = new Harness(Cards(CardGrouping.ByType, Btc, Aapl));
        await h.Main.InitializeAsync();

        await h.Main.MoveCardAsync(h.Main.Sections[0].Items[0], h.Main.Sections[1], 0);

        Describe(h.Main).Should().Equal("type:Crypto: BTC", "type:Equity: AAPL");
        h.Store.Stored!.Watchlist.Select(e => e.Symbol).Should().Equal("BTC", "AAPL");
    }

    /// <summary>
    /// Verifies dropping a card into another custom group changes its group and places it at the position.
    /// </summary>
    [Fact]
    public async Task MoveCardAsync_AcrossCustomGroups_ChangesGroupAndPosition()
    {
        var settings = Cards(CardGrouping.Custom, Btc, Eth, Aapl, Spx);
        AddGroup(settings, "coins", "BTC", "ETH");
        AddGroup(settings, "stocks", "AAPL", "SPX");
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();
        var coins = h.Main.Sections[0];
        var stocks = h.Main.Sections[1];

        await h.Main.MoveCardAsync(coins.Items[0], stocks, 1);

        Describe(h.Main).Should().Equal("group:coins: ETH", "group:stocks: AAPL,BTC,SPX");
        h.Store.Stored!.Watchlist.Select(e => (e.Symbol, e.GroupId)).Should().Equal(
            ("ETH", "coins"),
            ("AAPL", "stocks"),
            ("BTC", "stocks"),
            ("SPX", "stocks"));
    }

    /// <summary>
    /// Verifies dropping a card into an empty group assigns it to that group.
    /// </summary>
    [Fact]
    public async Task MoveCardAsync_IntoEmptyGroup_AssignsGroup()
    {
        var settings = Cards(CardGrouping.Custom, Btc);
        AddGroup(settings, "home", "BTC");
        AddGroup(settings, "empty");
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();

        await h.Main.MoveCardAsync(h.Main.Rows.Single(), h.Main.Sections[1], 0);

        Describe(h.Main).Should().Equal("group:home: ", "group:empty: BTC");
        h.Store.Stored!.Watchlist.Single().GroupId.Should().Be("empty");
    }

    /// <summary>
    /// Verifies moving a group reorders the group sections right away and persists the order.
    /// </summary>
    [Fact]
    public async Task MoveSectionAsync_GroupSection_ReordersSectionsAndPersists()
    {
        var settings = Cards(CardGrouping.Custom, Btc, Aapl);
        AddGroup(settings, "a", "BTC");
        AddGroup(settings, "b", "AAPL");
        AddGroup(settings, "c");
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();

        await h.Main.MoveSectionAsync(h.Main.Sections[0], 2);

        Describe(h.Main).Should().Equal("group:b: AAPL", "group:c: ", "group:a: BTC");
        h.Store.Stored!.Groups.Select(g => g.Id).Should().Equal("b", "c", "a");
    }

    /// <summary>
    /// Verifies moving an asset class section reorders the sections right away and persists the order, which
    /// is kept for classes that are empty at the moment.
    /// </summary>
    [Fact]
    public async Task MoveSectionAsync_TypeSection_ReordersSectionsAndPersists()
    {
        await using var h = new Harness(Cards(CardGrouping.ByType, Btc, Aapl, Spx));
        await h.Main.InitializeAsync();

        await h.Main.MoveSectionAsync(h.Main.Sections[0], 2);

        Describe(h.Main).Should().Equal("type:Equity: AAPL", "type:Index: SPX", "type:Crypto: BTC");
        h.Store.Stored!.Appearance.EffectiveTypeOrder().Take(3).Should().Equal(AssetClass.Equity, AssetClass.Index, AssetClass.Crypto);
    }

    /// <summary>
    /// Verifies the persisted class order is applied at startup and that new classes keep their place.
    /// </summary>
    [Fact]
    public async Task Sections_ByTypeWithPersistedOrder_FollowsIt()
    {
        var settings = Cards(CardGrouping.ByType, Btc, Aapl, Spx);
        settings.Appearance.TypeOrder = [AssetClass.Index, AssetClass.Crypto];
        await using var h = new Harness(settings);

        await h.Main.InitializeAsync();

        Describe(h.Main).Should().Equal("type:Index: SPX", "type:Crypto: BTC", "type:Equity: AAPL");
    }

    /// <summary>
    /// Verifies the sections of the list layout and of no grouping cannot be moved.
    /// </summary>
    [Fact]
    public async Task MoveSectionAsync_AllSection_IsIgnored()
    {
        await using var h = new Harness(Cards(CardGrouping.None, Btc, Aapl));
        await h.Main.InitializeAsync();

        await h.Main.MoveSectionAsync(h.Main.Sections.Single(), 0);

        Describe(h.Main).Should().Equal("all: BTC,AAPL");
    }

    /// <summary>
    /// Verifies an empty group shows its hint normally and swaps it for the drop slot while a card is dragged, and
    /// that a collapsed group never shows the slot.
    /// </summary>
    [Fact]
    public async Task DropSlot_EmptyGroup_ReplacesHintWhileVisible()
    {
        var settings = Cards(CardGrouping.Custom, Btc);
        AddGroup(settings, "home", "BTC");
        AddGroup(settings, "empty");
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();
        var section = h.Main.Sections[1];

        section.ShowEmptyHint.Should().BeTrue();
        section.ShowDropSlot.Should().BeFalse();

        section.IsDropSlotVisible = true;

        section.ShowEmptyHint.Should().BeFalse();
        section.ShowDropSlot.Should().BeTrue();

        section.IsCollapsed = true;

        section.ShowDropSlot.Should().BeFalse();
    }

    /// <summary>
    /// Verifies moving an instrument to another data source keeps it in its group.
    /// </summary>
    [Fact]
    public async Task ChangeSource_InstrumentInGroup_KeepsGroup()
    {
        var settings = Cards(CardGrouping.Custom, new Asset(Harness.Stocks, "B", "B", AssetClass.Equity));
        AddGroup(settings, "mine", "B");
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();

        h.Main.ChangeSourceCommand.Execute(h.Main.Rows[0]);
        await h.AddAsset.AddCommand.ExecuteAsync(new Asset(Harness.Plain, "B", "B", AssetClass.Equity));

        var entry = h.Store.Stored!.Watchlist.Should().ContainSingle().Subject;
        (entry.ProviderId, entry.GroupId).Should().Be((Harness.Plain, "mine"));
    }
}
