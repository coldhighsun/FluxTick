using FluxTick.Abstractions;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the sort state of the list in <see cref="FluxTick.App.ViewModels.MainViewModel"/>.
/// </summary>
public class MainSortTests
{
    /// <summary>
    /// An instrument without a holding.
    /// </summary>
    private static readonly Asset Plain = new(Harness.Stocks, "A", "A", AssetClass.Equity);

    /// <summary>
    /// An instrument that is held.
    /// </summary>
    private static readonly Asset Held = new(Harness.Stocks, "B", "B", AssetClass.Equity);

    /// <summary>
    /// Creates settings with a plain and a held instrument.
    /// </summary>
    /// <param name="held">
    /// Whether the second instrument has a holding.
    /// </param>
    /// <returns>
    /// The settings.
    /// </returns>
    private static AppSettings Settings(bool held)
    {
        var settings = Harness.WatchlistOf(Plain, Held);
        if (held)
        {
            settings.Watchlist[1].CostPrice = 10m;
            settings.Watchlist[1].Quantity = 5m;
        }

        return settings;
    }

    /// <summary>
    /// Gets the entry of the sort menus for the daily change.
    /// </summary>
    /// <param name="h">
    /// The harness.
    /// </param>
    /// <returns>
    /// The entry.
    /// </returns>
    private static FluxTick.App.ViewModels.SortOptionViewModel ChangeOption(Harness h) =>
        h.Main.SortOptions.Single(o => o.Key == WatchlistSortKey.ChangePercent);

    /// <summary>
    /// Verifies picking the key in use reverses the direction and the choice is saved.
    /// </summary>
    [Fact]
    public async Task ChooseSortKeyAsync_KeyInUse_ReversesDirectionAndSaves()
    {
        await using var h = new Harness(Settings(held: true));
        await h.Main.InitializeAsync();

        await h.Main.ChooseSortKeyAsync(WatchlistSortKey.ChangePercent);
        h.Main.SortKey.Should().Be(WatchlistSortKey.ChangePercent);
        h.Main.SortDescending.Should().BeTrue();
        ChangeOption(h).Text.Should().EndWith("↓");

        await h.Main.ChooseSortKeyAsync(WatchlistSortKey.ChangePercent);
        h.Main.SortDescending.Should().BeFalse();
        ChangeOption(h).Text.Should().EndWith("↑");
        h.Settings.Current.Appearance.SortKey.Should().Be(WatchlistSortKey.ChangePercent);
        h.Settings.Current.Appearance.SortDescending.Should().BeFalse();
        h.Main.IsListSorted.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the profit or loss cannot be picked while no instrument has a holding.
    /// </summary>
    [Fact]
    public async Task ChooseSortKeyAsync_PnlWithoutHolding_IsIgnored()
    {
        await using var h = new Harness(Settings(held: false));
        await h.Main.InitializeAsync();

        h.Main.HasAnyHolding.Should().BeFalse();
        await h.Main.ChooseSortKeyAsync(WatchlistSortKey.HoldingPnlPercent);

        h.Main.SortKey.Should().Be(WatchlistSortKey.None);
        h.Main.IsListSorted.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the list leaves the profit or loss sort once the last holding is gone, without forgetting the choice.
    /// </summary>
    [Fact]
    public async Task RemovingTheLastHolding_WhileSortedByPnl_ShowsWatchlistOrderButKeepsTheChoice()
    {
        await using var h = new Harness(Settings(held: true));
        await h.Main.InitializeAsync();
        await h.Main.ChooseSortKeyAsync(WatchlistSortKey.HoldingPnlPercent);
        h.Main.SortKey.Should().Be(WatchlistSortKey.HoldingPnlPercent);

        await h.Settings.UpdateAsync(s =>
        {
            s.Watchlist[1].CostPrice = null;
            s.Watchlist[1].Quantity = null;
        });

        await Harness.EventuallyAsync(() => h.Main.SortKey == WatchlistSortKey.None);
        h.Main.HasAnyHolding.Should().BeFalse();
        var pnl = h.Main.SortOptions.Single(o => o.Key == WatchlistSortKey.HoldingPnlPercent);
        pnl.IsEnabled.Should().BeFalse();
        pnl.Tip.Should().NotBeNullOrEmpty();
        h.Settings.Current.Appearance.SortKey.Should().Be(WatchlistSortKey.HoldingPnlPercent);

        await h.Settings.UpdateAsync(s =>
        {
            s.Watchlist[1].CostPrice = 10m;
            s.Watchlist[1].Quantity = 5m;
        });

        await Harness.EventuallyAsync(() => h.Main.SortKey == WatchlistSortKey.HoldingPnlPercent);
    }

    /// <summary>
    /// Verifies a second click that arrives while the first pick waits for a save moves on from the first pick.
    /// </summary>
    [Fact]
    public async Task ChooseSortKeyAsync_WhileASaveIsUnderWay_MovesOnFromThePreviousPick()
    {
        await using var h = new Harness(Settings(held: true));
        await h.Main.InitializeAsync();
        var release = new TaskCompletionSource();
        h.Store.SaveGate = release.Task;
        var blocking = h.Settings.UpdateAsync(s => s.Appearance.CardSize = CardSize.Large);

        var first = h.Main.ChooseSortKeyAsync(WatchlistSortKey.ChangePercent);
        var second = h.Main.ChooseSortKeyAsync(WatchlistSortKey.ChangePercent);

        h.Main.SortKey.Should().Be(WatchlistSortKey.ChangePercent);
        h.Main.SortDescending.Should().BeFalse();
        release.SetResult();
        await Task.WhenAll(blocking, first, second);
        h.Settings.Current.Appearance.SortDescending.Should().BeFalse();
        h.Main.SortDescending.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a save that fails does not throw out of the sort command, and the pick stays shown.
    /// </summary>
    [Fact]
    public async Task ChooseSortKeyAsync_WhenTheSaveFails_DoesNotThrow()
    {
        await using var h = new Harness(Settings(held: true));
        await h.Main.InitializeAsync();
        h.Store.SaveGate = Task.FromException(new IOException("disk full"));

        var act = () => h.Main.ChooseSortKeyAsync(WatchlistSortKey.ChangePercent);

        await act.Should().NotThrowAsync();
        h.Main.SortKey.Should().Be(WatchlistSortKey.ChangePercent);
    }

    /// <summary>
    /// Verifies the list shows the rows in the watchlist order again after picking no key.
    /// </summary>
    [Fact]
    public async Task ChooseSortKeyAsync_None_ShowsWatchlistOrder()
    {
        await using var h = new Harness(Settings(held: true));
        await h.Main.InitializeAsync();
        await h.Main.ChooseSortKeyAsync(WatchlistSortKey.ChangePercent);

        await h.Main.ChooseSortKeyAsync(WatchlistSortKey.None);

        h.Main.ListItems.Should().Equal(h.Main.Rows);
        h.Main.IsSortLabelVisible.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the list reads one collection whether or not a sort is on, so that turning a sort on or off does not
    /// swap the items of the list and rebuild every row.
    /// </summary>
    /// <returns>
    /// A task that completes when the check is done.
    /// </returns>
    [Fact]
    public async Task ListItems_IsTheSameCollectionWithAndWithoutSort()
    {
        await using var h = new Harness(Settings(held: true));
        await h.Main.InitializeAsync();
        var plain = h.Main.ListItems;

        await h.Main.ChooseSortKeyAsync(WatchlistSortKey.ChangePercent);
        var sorted = h.Main.ListItems;
        await h.Main.ChooseSortKeyAsync(WatchlistSortKey.None);

        sorted.Should().BeSameAs(plain);
        h.Main.ListItems.Should().BeSameAs(plain);
    }

    /// <summary>
    /// Verifies moving a row in the watchlist order is shown by the list.
    /// </summary>
    /// <returns>
    /// A task that completes when the check is done.
    /// </returns>
    [Fact]
    public async Task MoveToAsync_WithoutSort_ListFollowsTheRows()
    {
        await using var h = new Harness(Settings(held: true));
        await h.Main.InitializeAsync();

        await h.Main.MoveToAsync(h.Main.Rows[1], 0);

        h.Main.ListItems.Should().Equal(h.Main.Rows);
        h.Main.ListItems[0].Asset.Symbol.Should().Be("B");
    }

    /// <summary>
    /// Verifies the list shows the current rows as soon as the card layout gives way to it, also when a row was removed
    /// while the cards were on screen.
    /// </summary>
    /// <returns>
    /// A task that completes when the check is done.
    /// </returns>
    [Fact]
    public async Task SetLayoutAsync_BackToList_ListItemsAreCurrentWhenTheLayoutChanges()
    {
        await using var h = new Harness(Settings(held: true));
        await h.Main.InitializeAsync();
        await h.Main.SetLayoutAsync(WatchlistLayout.Cards);
        await h.Main.RemoveAsync(h.Main.Rows[0]);
        var seenWhenLayoutChanged = new List<string>();
        h.Main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FluxTick.App.ViewModels.MainViewModel.ListItems))
            {
                seenWhenLayoutChanged.AddRange(h.Main.ListItems.Select(r => r.Asset.Symbol));
            }
        };

        await h.Main.SetLayoutAsync(WatchlistLayout.List);

        seenWhenLayoutChanged.Should().OnlyContain(symbol => symbol == "B");
        h.Main.ListItems.Select(r => r.Asset.Symbol).Should().Equal("B");
    }

    /// <summary>
    /// Verifies the entries of the sort menus show the check mark and the arrow of the sort in use only.
    /// </summary>
    /// <returns>
    /// A task that completes when the check is done.
    /// </returns>
    [Fact]
    public async Task SortOptions_FollowTheSort()
    {
        await using var h = new Harness(Settings(held: true));
        await h.Main.InitializeAsync();

        await h.Main.ChooseSortKeyAsync(WatchlistSortKey.HoldingPnlPercent);

        h.Main.SortOptions.Where(o => o.IsChecked).Select(o => o.Key).Should().Equal(WatchlistSortKey.HoldingPnlPercent);
        h.Main.SortOptions.Where(o => o.Text.Contains('↓')).Should().ContainSingle();
        h.Main.SortOptions.Should().OnlyContain(o => o.IsEnabled);
    }
}
