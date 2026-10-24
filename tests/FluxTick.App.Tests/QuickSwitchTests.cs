using FluxTick.Abstractions;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the main window actions that switch the layout and grouping and create or rename groups.
/// </summary>
public class QuickSwitchTests
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
    /// Verifies the toggle flips between list and cards, rebuilds the sections at once and persists the choice.
    /// </summary>
    [Fact]
    public async Task ToggleLayoutAsync_FlipsLayoutAndPersists()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Aapl));
        await h.Main.InitializeAsync();

        await h.Main.ToggleLayoutAsync();

        h.Main.IsCardLayout.Should().BeTrue();
        h.Main.Sections.Should().ContainSingle();
        await Harness.EventuallyAsync(() => h.Store.Stored!.Appearance.Layout == WatchlistLayout.Cards);

        await h.Main.ToggleLayoutAsync();

        h.Main.IsCardLayout.Should().BeFalse();
        h.Main.Sections.Should().BeEmpty();
        await Harness.EventuallyAsync(() => h.Store.Stored!.Appearance.Layout == WatchlistLayout.List);
    }

    /// <summary>
    /// Verifies choosing a grouping in the main window regroups the cards and persists the choice.
    /// </summary>
    [Fact]
    public async Task SetGroupingAsync_RegroupsAndPersists()
    {
        var settings = Harness.WatchlistOf(Btc, Aapl);
        settings.Appearance.Layout = WatchlistLayout.Cards;
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();

        await h.Main.SetGroupingAsync(CardGrouping.ByType);

        h.Main.Grouping.Should().Be(CardGrouping.ByType);
        h.Main.Sections.Select(s => s.Key).Should().Equal("type:Crypto", "type:Equity");
        await Harness.EventuallyAsync(() => h.Store.Stored!.Appearance.Grouping == CardGrouping.ByType);
    }

    /// <summary>
    /// Verifies a new group is added, the custom grouping is switched on, and the previous grouping is remembered
    /// for the undo hint.
    /// </summary>
    [Fact]
    public async Task CreateGroupAsync_InAnotherGrouping_AddsGroupAndSwitchesToCustom()
    {
        var settings = Harness.WatchlistOf(Btc, Aapl);
        settings.Appearance.Layout = WatchlistLayout.Cards;
        settings.Appearance.Grouping = CardGrouping.ByType;
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();

        var id = await h.Main.CreateGroupAsync("  Watch  ");

        id.Should().NotBeNull();
        h.Main.Grouping.Should().Be(CardGrouping.Custom);
        h.Main.GroupingSwitchedFrom.Should().Be(CardGrouping.ByType);
        h.Main.Sections.Select(s => s.Title).Should().Equal(AppSettings.DefaultGroupName, "Watch");
        h.Main.Sections[1].Items.Should().BeEmpty();
        await Harness.EventuallyAsync(() => h.Store.Stored!.Groups.Any(g => g.Id == id && g.Name == "Watch"));
    }

    /// <summary>
    /// Verifies creating a group while the custom grouping is already active does not offer an undo.
    /// </summary>
    [Fact]
    public async Task CreateGroupAsync_InCustomGrouping_DoesNotReportSwitch()
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.Appearance.Layout = WatchlistLayout.Cards;
        settings.Appearance.Grouping = CardGrouping.Custom;
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();

        await h.Main.CreateGroupAsync("Watch");

        h.Main.GroupingSwitchedFrom.Should().BeNull();
        h.Main.Sections.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies the instrument given to the call lands in the new group.
    /// </summary>
    [Fact]
    public async Task CreateGroupAsync_WithRow_MovesRowIntoNewGroup()
    {
        var settings = Harness.WatchlistOf(Btc, Aapl);
        settings.Appearance.Layout = WatchlistLayout.Cards;
        settings.Appearance.Grouping = CardGrouping.Custom;
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();
        var row = h.Main.Rows.Single(r => r.Symbol == "AAPL");

        var id = await h.Main.CreateGroupAsync("Stocks", row);

        row.GroupId.Should().Be(id);
        h.Main.Sections.Select(s => string.Join(",", s.Items.Select(i => i.Symbol))).Should().Equal("BTC", "AAPL");
        await Harness.EventuallyAsync(() => h.Store.Stored!.Watchlist.Single(e => e.Symbol == "AAPL").GroupId == id);
    }

    /// <summary>
    /// Verifies a blank name creates nothing.
    /// </summary>
    /// <param name="name">
    /// The rejected name.
    /// </param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateGroupAsync_BlankName_CreatesNothing(string? name)
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();

        var id = await h.Main.CreateGroupAsync(name);

        id.Should().BeNull();
        h.Settings.Current.Groups.Should().ContainSingle();
        h.Main.GroupingSwitchedFrom.Should().BeNull();
    }

    /// <summary>
    /// Verifies renaming a group shows the new title and persists it.
    /// </summary>
    [Fact]
    public async Task RenameGroupAsync_ChangesTitleAndPersists()
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.Appearance.Layout = WatchlistLayout.Cards;
        settings.Appearance.Grouping = CardGrouping.Custom;
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();
        var group = h.Main.Sections.Single();

        await h.Main.RenameGroupAsync(group.GroupId!, " Core ");

        group.Title.Should().Be("Core");
        await Harness.EventuallyAsync(() => h.Store.Stored!.Groups.Single().Name == "Core");
    }

    /// <summary>
    /// Verifies a blank name keeps the old title.
    /// </summary>
    [Fact]
    public async Task RenameGroupAsync_BlankName_KeepsTitle()
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.Appearance.Layout = WatchlistLayout.Cards;
        settings.Appearance.Grouping = CardGrouping.Custom;
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();
        var group = h.Main.Sections.Single();

        await h.Main.RenameGroupAsync(group.GroupId!, "  ");

        group.Title.Should().Be(AppSettings.DefaultGroupName);
    }

    /// <summary>
    /// Creates settings for the card layout grouped by data source.
    /// </summary>
    /// <param name="assets">
    /// The instruments on the watchlist.
    /// </param>
    /// <returns>
    /// The settings.
    /// </returns>
    private static AppSettings ByProvider(params Asset[] assets)
    {
        var settings = Harness.WatchlistOf(assets);
        settings.Appearance.Layout = WatchlistLayout.Cards;
        settings.Appearance.Grouping = CardGrouping.ByProvider;
        return settings;
    }

    /// <summary>
    /// Verifies grouping by data source makes one section per provider, in order of first appearance, and keeps
    /// the watchlist order inside each.
    /// </summary>
    [Fact]
    public async Task Sections_ByProvider_OneSectionPerDataSource()
    {
        var eth = new Asset(Harness.Crypto, "ETH", "Ether", AssetClass.Crypto);
        await using var h = new Harness(ByProvider(Btc, Aapl, eth));

        await h.Main.InitializeAsync();

        h.Main.Sections.Select(s => $"{s.Key}: {string.Join(",", s.Items.Select(i => i.Symbol))}").Should().Equal(
            $"provider:{Harness.Crypto}: BTC,ETH",
            $"provider:{Harness.Stocks}: AAPL");
        h.Main.Sections.Should().OnlyContain(s => s.ShowHeader && s.Title.Length > 0);
    }

    /// <summary>
    /// Verifies collapsing a data source section is persisted and restored.
    /// </summary>
    [Fact]
    public async Task IsCollapsed_ProviderSectionToggled_PersistsAndRestores()
    {
        await using var h = new Harness(ByProvider(Btc, Aapl));
        await h.Main.InitializeAsync();

        h.Main.Sections[0].IsCollapsed = true;

        await Harness.EventuallyAsync(() => h.Store.Stored!.Appearance.CollapsedProviders.Contains(Harness.Crypto));

        var restored = ByProvider(Btc, Aapl);
        restored.Appearance.CollapsedProviders.Add(Harness.Stocks);
        await using var h2 = new Harness(restored);
        await h2.Main.InitializeAsync();
        h2.Main.Sections.Select(s => s.IsCollapsed).Should().Equal(false, true);
    }

    /// <summary>
    /// Verifies a card cannot be moved into the section of another data source, because the data source is not
    /// chosen by dragging.
    /// </summary>
    [Fact]
    public async Task MoveCardAsync_IntoOtherProviderSection_IsRejected()
    {
        await using var h = new Harness(ByProvider(Btc, Aapl));
        await h.Main.InitializeAsync();
        var aapl = h.Main.Rows.Single(r => r.Symbol == "AAPL");

        await h.Main.MoveCardAsync(aapl, h.Main.Sections[0], 0);

        h.Settings.Current.Watchlist.Select(e => e.Symbol).Should().Equal("BTC", "AAPL");
        h.Main.Sections[1].Items.Should().Contain(aapl);
    }

    /// <summary>
    /// Verifies a card can still be reordered inside its own data source section.
    /// </summary>
    [Fact]
    public async Task MoveCardAsync_WithinProviderSection_Reorders()
    {
        var eth = new Asset(Harness.Crypto, "ETH", "Ether", AssetClass.Crypto);
        await using var h = new Harness(ByProvider(Btc, eth));
        await h.Main.InitializeAsync();

        await h.Main.MoveCardAsync(h.Main.Rows.Single(r => r.Symbol == "ETH"), h.Main.Sections[0], 0);

        h.Main.Sections[0].Items.Select(i => i.Symbol).Should().Equal("ETH", "BTC");
    }

    /// <summary>
    /// Verifies the new group placeholder is offered only for cards with the custom grouping.
    /// </summary>
    [Fact]
    public async Task IsCustomCardGrouping_OnlyForCardsWithCustomGrouping()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();
        h.Main.IsCustomCardGrouping.Should().BeFalse();

        await h.Main.SetLayoutAsync(WatchlistLayout.Cards);
        h.Main.IsCustomCardGrouping.Should().BeFalse();

        await h.Main.SetGroupingAsync(CardGrouping.Custom);
        h.Main.IsCustomCardGrouping.Should().BeTrue();

        await h.Main.SetLayoutAsync(WatchlistLayout.List);
        h.Main.IsCustomCardGrouping.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the undo hint goes back to the previous grouping and keeps the new group.
    /// </summary>
    [Fact]
    public async Task UndoGroupingSwitchAsync_RestoresPreviousGroupingAndKeepsGroup()
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.Appearance.Layout = WatchlistLayout.Cards;
        settings.Appearance.Grouping = CardGrouping.ByType;
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();
        await h.Main.CreateGroupAsync("Watch");

        await h.Main.UndoGroupingSwitchAsync();

        h.Main.Grouping.Should().Be(CardGrouping.ByType);
        h.Main.GroupingSwitchedFrom.Should().BeNull();
        h.Settings.Current.Groups.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies picking a grouping by hand drops a pending undo hint.
    /// </summary>
    [Fact]
    public async Task SetGroupingAsync_AfterAutomaticSwitch_ClearsHint()
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.Appearance.Layout = WatchlistLayout.Cards;
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();
        await h.Main.CreateGroupAsync("Watch");
        h.Main.GroupingSwitchedFrom.Should().Be(CardGrouping.None);

        await h.Main.SetGroupingAsync(CardGrouping.ByType);

        h.Main.GroupingSwitchedFrom.Should().BeNull();
    }

    /// <summary>
    /// Creates the custom grouping with the default group holding Bitcoin and an empty group "Empty" after it.
    /// </summary>
    /// <returns>
    /// The harness and the id of the empty group.
    /// </returns>
    private static async Task<(Harness Harness, string EmptyId)> WithEmptyGroupAsync()
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.Appearance.Layout = WatchlistLayout.Cards;
        settings.Appearance.Grouping = CardGrouping.Custom;
        var h = new Harness(settings);
        await h.Main.InitializeAsync();
        var id = await h.Main.CreateGroupAsync("Empty");
        return (h, id!);
    }

    /// <summary>
    /// Verifies an empty group can be deleted when another group remains, and the deletion is persisted.
    /// </summary>
    [Fact]
    public async Task DeleteGroupAsync_EmptyGroup_IsDeletedAndPersisted()
    {
        var (h, emptyId) = await WithEmptyGroupAsync();
        await using var _ = h;
        var section = h.Main.Sections.Single(s => s.GroupId == emptyId);
        section.CanDelete.Should().BeTrue();
        section.DeleteBlockedReason.Should().BeEmpty();
        section.DeleteMenuHeader.Should().Be("Delete group");

        await h.Main.DeleteGroupAsync(section);

        h.Main.Sections.Should().ContainSingle().Which.GroupId.Should().NotBe(emptyId);
        await Harness.EventuallyAsync(() => h.Store.Stored!.Groups.All(g => g.Id != emptyId));
    }

    /// <summary>
    /// Verifies a group that holds an instrument cannot be deleted, and says why.
    /// </summary>
    [Fact]
    public async Task DeleteGroupAsync_GroupWithInstrument_IsRefused()
    {
        var (h, _) = await WithEmptyGroupAsync();
        await using var _ = h;
        var section = h.Main.Sections[0];

        section.CanDelete.Should().BeFalse();
        section.DeleteBlockedReason.Should().NotBeEmpty();
        section.DeleteMenuHeader.Should().StartWith("Delete group (");

        await h.Main.DeleteGroupAsync(section);

        h.Main.Sections.Should().HaveCount(2);
        h.Settings.Current.Groups.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies the last group stays even when it is empty.
    /// </summary>
    [Fact]
    public async Task DeleteGroupAsync_LastGroup_IsRefused()
    {
        var settings = Harness.WatchlistOf();
        settings.Appearance.Layout = WatchlistLayout.Cards;
        settings.Appearance.Grouping = CardGrouping.Custom;
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();
        var section = h.Main.Sections.Single();

        section.CanDelete.Should().BeFalse();
        await h.Main.DeleteGroupAsync(section);

        h.Main.Sections.Should().ContainSingle();
        h.Settings.Current.Groups.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies moving the instrument away makes its group deletable.
    /// </summary>
    [Fact]
    public async Task CanDelete_AfterInstrumentMovedAway_BecomesTrue()
    {
        var (h, emptyId) = await WithEmptyGroupAsync();
        await using var _ = h;
        var first = h.Main.Sections[0];
        first.CanDelete.Should().BeFalse();

        await h.Main.MoveToGroupAsync(h.Main.Rows.Single(), emptyId);

        first.CanDelete.Should().BeTrue();
        h.Main.Sections.Single(s => s.GroupId == emptyId).CanDelete.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a view that is out of date cannot delete a group that received an instrument in the meantime.
    /// </summary>
    [Fact]
    public async Task DeleteGroupAsync_GroupFilledAfterSectionWasShown_IsRefused()
    {
        var (h, emptyId) = await WithEmptyGroupAsync();
        await using var _ = h;
        var staleSection = h.Main.Sections.Single(s => s.GroupId == emptyId);
        await h.Settings.UpdateAsync(s => s.Watchlist[0].GroupId = emptyId);

        await h.Main.DeleteGroupAsync(staleSection);

        h.Settings.Current.Groups.Should().Contain(g => g.Id == emptyId);
    }

    /// <summary>
    /// Verifies the command ignores sections that are not user-defined groups.
    /// </summary>
    [Fact]
    public async Task DeleteGroupAsync_TypeSection_IsIgnored()
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.Appearance.Layout = WatchlistLayout.Cards;
        settings.Appearance.Grouping = CardGrouping.ByType;
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();
        var section = h.Main.Sections.Single();

        section.CanDelete.Should().BeFalse();
        await h.Main.DeleteGroupAsync(section);
        await h.Main.DeleteGroupAsync(null);

        h.Settings.Current.Groups.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies the card grouping setting is available only while the card layout is selected in the settings.
    /// </summary>
    [Fact]
    public async Task IsCardLayoutSelected_FollowsSelectedLayout()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();
        var settingsPage = h.Main.Settings;
        var changes = new List<string?>();
        settingsPage.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        settingsPage.IsCardLayoutSelected.Should().BeFalse();

        settingsPage.SelectedLayout = WatchlistLayout.Cards;
        settingsPage.IsCardLayoutSelected.Should().BeTrue();
        changes.Should().Contain(nameof(settingsPage.IsCardLayoutSelected));

        settingsPage.SelectedLayout = WatchlistLayout.List;
        settingsPage.IsCardLayoutSelected.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the header's lock button flips the position lock and the choice is persisted.
    /// </summary>
    [Fact]
    public async Task ToggleLock_FlipsLockAndPersists()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();
        h.Main.Settings.Locked.Should().BeFalse();

        h.Main.ToggleLockCommand.Execute(null);

        h.Main.Settings.Locked.Should().BeTrue();
        await Harness.EventuallyAsync(() => h.Store.Stored!.Appearance.Locked);

        h.Main.ToggleLockCommand.Execute(null);

        h.Main.Settings.Locked.Should().BeFalse();
        await Harness.EventuallyAsync(() => !h.Store.Stored!.Appearance.Locked);
    }

    /// <summary>
    /// Verifies the flash entry of the right-click menu turns the flash on and that it is saved with the other appearance settings.
    /// </summary>
    [Fact]
    public async Task FlashOnUpdate_ToggledFromMenu_Persists()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();

        h.Main.ToggleRollCommand.Execute(null);

        await Harness.EventuallyAsync(() => h.Store.Stored!.Appearance.RollPriceOnUpdate);
        h.Main.Rows.Should().OnlyContain(r => r.RollEnabled);
    }

    /// <summary>
    /// Verifies layout toggles that arrive while a save is in progress each flip the layout once, so three clicks end
    /// on the card layout.
    /// </summary>
    [Fact]
    public async Task ToggleLayoutAsync_ClicksDuringSave_EachFlipOnce()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();
        var release = new TaskCompletionSource();
        h.Store.SaveGate = release.Task;

        var first = h.Main.ToggleLayoutAsync();
        var second = h.Main.ToggleLayoutAsync();
        var third = h.Main.ToggleLayoutAsync();
        release.SetResult();
        await Task.WhenAll(first, second, third);

        h.Settings.Current.Appearance.Layout.Should().Be(WatchlistLayout.Cards);
        await Harness.EventuallyAsync(() => h.Main.IsCardLayout);
    }

    /// <summary>
    /// Verifies the compact entry of the right-click menu hides and shows the sparklines and is saved.
    /// </summary>
    [Fact]
    public async Task ToggleCompact_FromMenu_HidesSparklinesAndPersists()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();

        h.Main.ToggleCompactCommand.Execute(null);

        h.Main.Settings.Compact.Should().BeTrue();
        h.Main.Rows.Should().OnlyContain(r => !r.ShowSparkline);
        await Harness.EventuallyAsync(() => h.Store.Stored!.Appearance.Compact);

        h.Main.ToggleCompactCommand.Execute(null);

        h.Main.Rows.Should().OnlyContain(r => r.ShowSparkline);
        await Harness.EventuallyAsync(() => !h.Store.Stored!.Appearance.Compact);
    }

    /// <summary>
    /// Verifies the list gets rows only in the list layout, so that the list hidden behind the cards builds no views.
    /// </summary>
    [Fact]
    public async Task ListItems_FollowsLayout()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Aapl));
        await h.Main.InitializeAsync();
        h.Main.ListItems.Should().HaveCount(2);

        await h.Main.SetLayoutAsync(WatchlistLayout.Cards);

        h.Main.ListItems.Should().BeEmpty();

        await h.Main.SetLayoutAsync(WatchlistLayout.List);

        h.Main.ListItems.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies a collapsed section shows no cards, so that they build no views, and shows them again when expanded.
    /// </summary>
    [Fact]
    public async Task ShownItems_CollapsedSection_IsEmptyUntilExpanded()
    {
        var settings = Harness.WatchlistOf(Btc, Aapl);
        settings.Appearance.Layout = WatchlistLayout.Cards;
        settings.Appearance.Grouping = CardGrouping.ByType;
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();
        var section = h.Main.Sections[0];
        List<string?> changes = [];
        section.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        section.ShownItems.Should().HaveCount(1);

        section.IsCollapsed = true;

        section.ShownItems.Should().BeEmpty();
        section.Items.Should().HaveCount(1, "collapsing hides the cards, it does not remove them");
        changes.Should().Contain(nameof(section.ShownItems));

        section.IsCollapsed = false;

        section.ShownItems.Should().HaveCount(1);
    }
}
