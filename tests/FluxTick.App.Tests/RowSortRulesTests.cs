using FluxTick.App.ViewModels;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the ordering of the list layout.
/// </summary>
public class RowSortRulesTests
{
    /// <summary>
    /// A row as the sort rules see it.
    /// </summary>
    /// <param name="Name">
    /// The name of the row.
    /// </param>
    /// <param name="Change">
    /// The daily change in percent, or <see langword="null"/>.
    /// </param>
    /// <param name="Pnl">
    /// The profit or loss of the holding in percent, or <see langword="null"/> without a holding.
    /// </param>
    /// <param name="Shut">
    /// Whether the market of the row is shut.
    /// </param>
    private sealed record Row(string Name, decimal? Change = null, decimal? Pnl = null, bool Shut = false);

    /// <summary>
    /// Rows in the order of the watchlist, with a missing value and a tie.
    /// </summary>
    private static readonly Row[] Rows =
    [
        new("a", 1m),
        new("b"),
        new("c", -2m),
        new("d", 3m),
        new("e", 1m),
    ];

    /// <summary>
    /// Sorts rows by a key.
    /// </summary>
    /// <param name="rows">
    /// The rows in the order of the watchlist.
    /// </param>
    /// <param name="key">
    /// What to order by.
    /// </param>
    /// <param name="descending">
    /// Whether the largest value comes first.
    /// </param>
    /// <returns>
    /// The names of the rows in display order.
    /// </returns>
    private static string[] Sorted(Row[] rows, WatchlistSortKey key, bool descending) =>
        [.. RowSortRules.Sort(rows, key, descending, r => r.Change, r => r.Pnl, r => r.Shut).Select(r => r.Name)];

    /// <summary>
    /// Verifies no key returns the watchlist order unchanged.
    /// </summary>
    [Fact]
    public void Sort_None_KeepsWatchlistOrder()
    {
        Sorted(Rows, WatchlistSortKey.None, true).Should().Equal("a", "b", "c", "d", "e");
    }

    /// <summary>
    /// Verifies the largest change comes first, ties keep their order and rows without a value come last.
    /// </summary>
    [Fact]
    public void Sort_ChangeDescending_PutsLargestFirstAndMissingLast()
    {
        Sorted(Rows, WatchlistSortKey.ChangePercent, true).Should().Equal("d", "a", "e", "c", "b");
    }

    /// <summary>
    /// Verifies the smallest change comes first and rows without a value still come last.
    /// </summary>
    [Fact]
    public void Sort_ChangeAscending_PutsSmallestFirstAndMissingLast()
    {
        Sorted(Rows, WatchlistSortKey.ChangePercent, false).Should().Equal("c", "a", "e", "d", "b");
    }

    /// <summary>
    /// Verifies rows without a value come last in both directions and keep the order of the watchlist.
    /// </summary>
    /// <param name="key">
    /// The sort key.
    /// </param>
    /// <param name="descending">
    /// Whether the largest value comes first.
    /// </param>
    [Theory]
    [InlineData(WatchlistSortKey.ChangePercent, true)]
    [InlineData(WatchlistSortKey.ChangePercent, false)]
    [InlineData(WatchlistSortKey.HoldingPnlPercent, true)]
    [InlineData(WatchlistSortKey.HoldingPnlPercent, false)]
    public void Sort_RowsWithoutValue_AreLastInWatchlistOrder(WatchlistSortKey key, bool descending)
    {
        Row[] rows = [new("x"), new("a", 1m, 1m), new("y"), new("b", 2m, 2m), new("z")];

        Sorted(rows, key, descending).TakeLast(3).Should().Equal("x", "y", "z");
    }

    /// <summary>
    /// Verifies rows of a shut market follow the rows of an open market when sorting by the change, each group ordered by
    /// the value, and rows without a change still come last.
    /// </summary>
    /// <param name="descending">
    /// Whether the largest value comes first.
    /// </param>
    /// <param name="expected">
    /// The expected order of the names.
    /// </param>
    [Theory]
    [InlineData(true, "o2", "o1", "s2", "s1", "n")]
    [InlineData(false, "o1", "o2", "s1", "s2", "n")]
    public void Sort_ChangeWithShutMarkets_ShutMarketsFollowOpenOnes(bool descending, params string[] expected)
    {
        Row[] rows = [new("s2", 9m, Shut: true), new("n"), new("o1", 1m), new("s1", 5m, Shut: true), new("o2", 2m)];

        Sorted(rows, WatchlistSortKey.ChangePercent, descending).Should().Equal(expected);
    }

    /// <summary>
    /// Verifies the profit or loss sort ignores whether the market is shut, since the profit follows the latest price.
    /// </summary>
    [Fact]
    public void Sort_PnlWithShutMarkets_DoesNotGroupByMarket()
    {
        Row[] rows = [new("s", Pnl: 9m, Shut: true), new("o", Pnl: 1m), new("none")];

        Sorted(rows, WatchlistSortKey.HoldingPnlPercent, true).Should().Equal("s", "o", "none");
    }

    /// <summary>
    /// Verifies the profit or loss sort orders by the percentage and puts rows without a holding last.
    /// </summary>
    /// <param name="descending">
    /// Whether the largest value comes first.
    /// </param>
    /// <param name="expected">
    /// The expected order of the names.
    /// </param>
    [Theory]
    [InlineData(true, "p3", "p1", "p2", "free1", "free2")]
    [InlineData(false, "p2", "p1", "p3", "free1", "free2")]
    public void Sort_Pnl_OrdersByPercentAndHoldingFreeRowsLast(bool descending, params string[] expected)
    {
        Row[] rows = [new("free1", 5m), new("p1", Pnl: 1.5m), new("p2", Pnl: -4m), new("free2", -5m), new("p3", Pnl: 20m)];

        Sorted(rows, WatchlistSortKey.HoldingPnlPercent, descending).Should().Equal(expected);
    }

    /// <summary>
    /// Verifies the list keeps the watchlist order when no row has a value.
    /// </summary>
    [Fact]
    public void Sort_NoRowHasValue_KeepsWatchlistOrder()
    {
        Row[] rows = [new("a"), new("b"), new("c")];

        Sorted(rows, WatchlistSortKey.ChangePercent, true).Should().Equal("a", "b", "c");
    }

    /// <summary>
    /// Verifies an empty list sorts to an empty list.
    /// </summary>
    [Fact]
    public void Sort_Empty_ReturnsEmpty()
    {
        Sorted([], WatchlistSortKey.ChangePercent, true).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies picking another key starts with the largest value first.
    /// </summary>
    [Fact]
    public void Choose_AnotherKey_StartsDescending()
    {
        var (key, descending) = RowSortRules.Choose(WatchlistSortKey.ChangePercent, false, WatchlistSortKey.HoldingPnlPercent);

        key.Should().Be(WatchlistSortKey.HoldingPnlPercent);
        descending.Should().BeTrue();
    }

    /// <summary>
    /// Verifies picking the key in use again reverses the direction, both ways.
    /// </summary>
    [Fact]
    public void Choose_KeyInUse_ReversesDirection()
    {
        var (key, descending) = RowSortRules.Choose(WatchlistSortKey.ChangePercent, true, WatchlistSortKey.ChangePercent);
        var (_, back) = RowSortRules.Choose(key, descending, WatchlistSortKey.ChangePercent);

        key.Should().Be(WatchlistSortKey.ChangePercent);
        descending.Should().BeFalse();
        back.Should().BeTrue();
    }

    /// <summary>
    /// Verifies picking no key returns to the order of the watchlist.
    /// </summary>
    [Fact]
    public void Choose_None_ReturnsToWatchlistOrder()
    {
        var (key, _) = RowSortRules.Choose(WatchlistSortKey.HoldingPnlPercent, false, WatchlistSortKey.None);

        key.Should().Be(WatchlistSortKey.None);
    }

    /// <summary>
    /// Verifies the profit or loss sort is dropped while no instrument has a holding and kept otherwise.
    /// </summary>
    /// <param name="key">
    /// The sort key.
    /// </param>
    /// <param name="hasAnyHolding">
    /// Whether any instrument has a holding.
    /// </param>
    /// <param name="expected">
    /// The expected key.
    /// </param>
    [Theory]
    [InlineData(WatchlistSortKey.HoldingPnlPercent, false, WatchlistSortKey.None)]
    [InlineData(WatchlistSortKey.HoldingPnlPercent, true, WatchlistSortKey.HoldingPnlPercent)]
    [InlineData(WatchlistSortKey.ChangePercent, false, WatchlistSortKey.ChangePercent)]
    [InlineData(WatchlistSortKey.None, false, WatchlistSortKey.None)]
    public void Normalize_WithoutHolding_DropsOnlyThePnlKey(WatchlistSortKey key, bool hasAnyHolding, WatchlistSortKey expected)
    {
        RowSortRules.Normalize(key, hasAnyHolding).Should().Be(expected);
    }
}
