using FluxTick.Abstractions;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the group editor of the settings page and the "Move to group" choices.
/// </summary>
public class GroupsSettingsTests
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
    /// Creates settings with the given instruments and groups, in the custom card grouping.
    /// </summary>
    /// <param name="groupIds">
    /// The ids of the groups to create; each group is named after its id.
    /// </param>
    /// <returns>
    /// The settings.
    /// </returns>
    private static AppSettings Custom(params string[] groupIds)
    {
        var settings = Harness.WatchlistOf(Btc, Aapl);
        settings.Appearance.Layout = WatchlistLayout.Cards;
        settings.Appearance.Grouping = CardGrouping.Custom;
        settings.Groups.AddRange(groupIds.Select(id => new WatchGroup { Id = id, Name = id }));
        return settings;
    }

    /// <summary>
    /// Verifies a typed name creates a persisted group at the end of the list and shows it as a section.
    /// </summary>
    [Fact]
    public async Task AddAsync_TypedName_CreatesGroupAndSection()
    {
        await using var h = new Harness(Custom());
        await h.Main.InitializeAsync();
        var groups = h.SettingsPage.Groups;

        await groups.AddAsync("  Favorites ");

        groups.Items.Select(i => i.Name).Should().Equal(AppSettings.DefaultGroupName, "Favorites");
        h.Store.Stored!.Groups.Select(g => g.Name).Should().Equal(AppSettings.DefaultGroupName, "Favorites");
        await Harness.EventuallyAsync(() => h.Main.Sections.Any(s => s.Kind == WatchSectionKind.Group && s.Title == "Favorites"));
    }

    /// <summary>
    /// Verifies a blank name adds nothing.
    /// </summary>
    /// <param name="name">
    /// The rejected name.
    /// </param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddAsync_BlankName_AddsNothing(string? name)
    {
        await using var h = new Harness(Custom());
        await h.Main.InitializeAsync();
        var groups = h.SettingsPage.Groups;

        await groups.AddAsync(name);

        groups.Items.Should().ContainSingle();
        h.Settings.Current.Groups.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies renaming a group persists the trimmed name and keeps the group's members.
    /// </summary>
    [Fact]
    public async Task Rename_NewName_PersistsAndKeepsId()
    {
        var settings = Custom("a");
        settings.Watchlist[0].GroupId = "a";
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();

        h.SettingsPage.Groups.Items[0].Name = " Crypto stuff ";

        await Harness.EventuallyAsync(() => h.Store.Stored!.Groups[0].Name == "Crypto stuff");
        h.Store.Stored!.Groups[0].Id.Should().Be("a");
        h.Store.Stored.Watchlist[0].GroupId.Should().Be("a");
        await Harness.EventuallyAsync(() => h.Main.Sections[0].Title == "Crypto stuff");
    }

    /// <summary>
    /// Verifies a blank name is rejected and the previous name is restored.
    /// </summary>
    [Fact]
    public async Task Rename_BlankName_RestoresPreviousName()
    {
        await using var h = new Harness(Custom("a"));
        await h.Main.InitializeAsync();

        h.SettingsPage.Groups.Items[0].Name = "  ";

        h.SettingsPage.Groups.Items[0].Name.Should().Be("a");
        h.Store.Stored!.Groups[0].Name.Should().Be("a");
    }

    /// <summary>
    /// Verifies deleting a group removes it and moves its instruments to the first remaining group.
    /// </summary>
    [Fact]
    public async Task DeleteCommand_GroupWithMembers_MovesThemToFirstRemainingGroup()
    {
        var settings = Custom("a", "b", "c");
        settings.Watchlist[0].GroupId = "a";
        settings.Watchlist[1].GroupId = "c";
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();

        await h.SettingsPage.Groups.Items[0].DeleteCommand.ExecuteAsync(null);

        h.SettingsPage.Groups.Items.Select(i => i.Id).Should().Equal("b", "c");
        h.Store.Stored!.Groups.Select(g => g.Id).Should().Equal("b", "c");
        h.Store.Stored.Watchlist.Select(e => e.GroupId).Should().Equal("b", "c");
        await Harness.EventuallyAsync(() => h.Main.Sections.Select(s => s.Key).SequenceEqual(["group:b", "group:c"]));
    }

    /// <summary>
    /// Verifies deleting the last group creates a default group that takes over the instruments.
    /// </summary>
    [Fact]
    public async Task DeleteCommand_LastGroup_CreatesDefaultGroupForTheInstruments()
    {
        var settings = Custom("a");
        settings.Watchlist[0].GroupId = "a";
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();

        await h.SettingsPage.Groups.Items[0].DeleteCommand.ExecuteAsync(null);

        var group = h.Store.Stored!.Groups.Should().ContainSingle().Subject;
        group.Id.Should().NotBe("a");
        group.Name.Should().Be(AppSettings.DefaultGroupName);
        h.Store.Stored.Watchlist.Should().OnlyContain(e => e.GroupId == group.Id);
        h.SettingsPage.Groups.Items.Should().ContainSingle().Which.Name.Should().Be(AppSettings.DefaultGroupName);
    }

    /// <summary>
    /// Verifies dragging a group to a position reorders the list at once and persists the new order.
    /// </summary>
    [Fact]
    public async Task MoveToAsync_Group_ReordersListAndPersists()
    {
        await using var h = new Harness(Custom("a", "b", "c"));
        await h.Main.InitializeAsync();
        var groups = h.SettingsPage.Groups;

        await groups.MoveToAsync(groups.Items[2], 0);

        groups.Items.Select(i => i.Id).Should().Equal("c", "a", "b");
        h.Store.Stored!.Groups.Select(g => g.Id).Should().Equal("c", "a", "b");

        await groups.MoveToAsync(groups.Items[0], 1);

        groups.Items.Select(i => i.Id).Should().Equal("a", "c", "b");
        h.Store.Stored!.Groups.Select(g => g.Id).Should().Equal("a", "c", "b");
    }

    /// <summary>
    /// Verifies a position outside the list is clamped to its ends, and dropping a group where it already is
    /// changes nothing.
    /// </summary>
    [Fact]
    public async Task MoveToAsync_PositionOutsideList_IsClampedAndSamePositionIsIgnored()
    {
        await using var h = new Harness(Custom("a", "b", "c"));
        await h.Main.InitializeAsync();
        var groups = h.SettingsPage.Groups;
        var first = groups.Items[0];

        await groups.MoveToAsync(first, 99);
        groups.Items.Select(i => i.Id).Should().Equal("b", "c", "a");

        await groups.MoveToAsync(groups.Items[1], 1);
        groups.Items.Select(i => i.Id).Should().Equal("b", "c", "a");

        await groups.MoveToAsync(first, -5);
        groups.Items.Select(i => i.Id).Should().Equal("a", "b", "c");
        h.Store.Stored!.Groups.Select(g => g.Id).Should().Equal("a", "b", "c");
    }

    /// <summary>
    /// Verifies the main window shows the order chosen in the settings page.
    /// </summary>
    [Fact]
    public async Task MoveToAsync_Group_ReordersSectionsInMainWindow()
    {
        var settings = Custom("a", "b", "c");
        settings.Appearance.Layout = WatchlistLayout.Cards;
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();
        var groups = h.SettingsPage.Groups;

        await groups.MoveToAsync(groups.Items[2], 0);

        h.Main.Sections.Select(s => s.GroupId).Should().Equal("c", "a", "b");
    }

    /// <summary>
    /// Verifies opening the settings page reloads the group list from the persisted settings.
    /// </summary>
    [Fact]
    public async Task Reload_AfterExternalChange_ShowsPersistedGroups()
    {
        await using var h = new Harness(Custom("a"));
        await h.Settings.UpdateAsync(s => s.Groups.Add(new WatchGroup { Id = "z", Name = "Z" }));

        h.SettingsPage.Reload();

        h.SettingsPage.Groups.Items.Select(i => i.Id).Should().Equal("a", "z");
    }

    /// <summary>
    /// Verifies the move menu lists every group and flags the current one.
    /// </summary>
    [Fact]
    public async Task GetGroupChoices_InstrumentInGroup_FlagsCurrentGroup()
    {
        var settings = Custom("a", "b");
        settings.Watchlist[0].GroupId = "b";
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();

        var inGroup = h.Main.GetGroupChoices(h.Main.Rows[0]);
        var other = h.Main.GetGroupChoices(h.Main.Rows[1]);

        inGroup.Select(c => (c.GroupId, c.Title, c.IsCurrent)).Should().Equal(("a", "a", false), ("b", "b", true));
        other.Select(c => c.IsCurrent).Should().Equal(true, false);
    }

    /// <summary>
    /// Verifies an instrument that refers to a deleted group is reported as being in the first group.
    /// </summary>
    [Fact]
    public async Task GetGroupChoices_UnknownGroupId_FlagsFirstGroup()
    {
        var settings = Custom("a", "b");
        settings.Watchlist[0].GroupId = "gone";
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();

        var choices = h.Main.GetGroupChoices(h.Main.Rows[0]);

        choices.Single(c => c.IsCurrent).GroupId.Should().Be("a");
    }

    /// <summary>
    /// Verifies a new group shows in the list at once even when its update waits for a save in progress.
    /// </summary>
    [Fact]
    public async Task AddAsync_WhileSaveInProgress_ShowsGroupInListAtOnce()
    {
        await using var h = new Harness(Custom("a"));
        await h.Main.InitializeAsync();
        var release = new TaskCompletionSource();
        h.Store.SaveGate = release.Task;
        var groups = h.SettingsPage.Groups;
        var blocker = h.Settings.UpdateAsync(s => s.Appearance.Locked = true);

        var add = groups.AddAsync("Late");

        groups.Items.Select(i => i.Name).Should().Equal("a", "Late");
        release.SetResult();
        await Task.WhenAll(blocker, add);
        h.Settings.Current.Groups.Select(g => g.Name).Should().Equal("a", "Late");
    }

    /// <summary>
    /// Verifies a deleted group leaves the list at once even when its update waits for a save in progress.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_WhileSaveInProgress_RemovesGroupFromListAtOnce()
    {
        await using var h = new Harness(Custom("a", "b"));
        await h.Main.InitializeAsync();
        var release = new TaskCompletionSource();
        h.Store.SaveGate = release.Task;
        var groups = h.SettingsPage.Groups;
        var blocker = h.Settings.UpdateAsync(s => s.Appearance.Locked = true);

        var delete = groups.DeleteAsync(groups.Items[1]);

        groups.Items.Select(i => i.Id).Should().Equal("a");
        release.SetResult();
        await Task.WhenAll(blocker, delete);
        h.Settings.Current.Groups.Select(g => g.Id).Should().NotContain("b");
    }
}
