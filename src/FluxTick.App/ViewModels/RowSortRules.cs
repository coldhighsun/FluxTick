using FluxTick.Core.Application;

namespace FluxTick.App.ViewModels;

/// <summary>
/// Pure ordering rules of the list layout.
/// </summary>
internal static class RowSortRules
{
    /// <summary>
    /// Works out the sort key and direction after the user picked a key in the sort menu. Picking the key that is in use
    /// reverses the direction; picking another key starts with the largest value first.
    /// </summary>
    /// <param name="currentKey">
    /// The sort key in use.
    /// </param>
    /// <param name="currentDescending">
    /// Whether the largest value comes first at the moment.
    /// </param>
    /// <param name="chosenKey">
    /// The key the user picked.
    /// </param>
    /// <returns>
    /// The new key and direction. Picking <see cref="WatchlistSortKey.None"/> returns to the order of the watchlist.
    /// </returns>
    public static (WatchlistSortKey Key, bool Descending) Choose(WatchlistSortKey currentKey, bool currentDescending, WatchlistSortKey chosenKey)
    {
        if (chosenKey == WatchlistSortKey.None)
        {
            return (WatchlistSortKey.None, true);
        }

        return chosenKey == currentKey ? (chosenKey, !currentDescending) : (chosenKey, true);
    }

    /// <summary>
    /// Falls back to the order of the watchlist when the sort key cannot be used, which is the case for the profit or
    /// loss while no instrument has a holding.
    /// </summary>
    /// <param name="key">
    /// The sort key.
    /// </param>
    /// <param name="hasAnyHolding">
    /// Whether any instrument has a holding.
    /// </param>
    /// <returns>
    /// The key, or <see cref="WatchlistSortKey.None"/> when it cannot be used.
    /// </returns>
    public static WatchlistSortKey Normalize(WatchlistSortKey key, bool hasAnyHolding) =>
        key == WatchlistSortKey.HoldingPnlPercent && !hasAnyHolding ? WatchlistSortKey.None : key;

    /// <summary>
    /// Orders items by a sort key. The sort is stable. By the daily change, items whose market is open come first, then
    /// those whose market is shut, each by the value. By the profit or loss, items without a holding follow the others in
    /// the order of the watchlist. Items without a value come last in both directions.
    /// </summary>
    /// <typeparam name="T">
    /// The type of the items.
    /// </typeparam>
    /// <param name="items">
    /// The items in the order of the watchlist.
    /// </param>
    /// <param name="key">
    /// What to order by.
    /// </param>
    /// <param name="descending">
    /// Whether the largest value comes first.
    /// </param>
    /// <param name="changePercent">
    /// Gets the daily change in percent of an item, or <see langword="null"/> when it has none.
    /// </param>
    /// <param name="pnlPercent">
    /// Gets the profit or loss of the holding of an item in percent, or <see langword="null"/> when it has none.
    /// </param>
    /// <param name="isMarketShut">
    /// Tells whether the market of an item is shut, so that its change is the one of the last session. When it is not
    /// given, no market counts as shut.
    /// </param>
    /// <returns>
    /// The items in display order. The watchlist order is returned as it is for <see cref="WatchlistSortKey.None"/>.
    /// </returns>
    public static IReadOnlyList<T> Sort<T>(
        IReadOnlyList<T> items,
        WatchlistSortKey key,
        bool descending,
        Func<T, decimal?> changePercent,
        Func<T, decimal?> pnlPercent,
        Func<T, bool>? isMarketShut = null)
    {
        if (key == WatchlistSortKey.None)
        {
            return items;
        }

        // The profit or loss follows the latest price, which a shut market only holds: it has no groups of its own.
        var valueOf = key == WatchlistSortKey.HoldingPnlPercent ? pnlPercent : changePercent;
        var groupByMarket = key == WatchlistSortKey.ChangePercent;

        var open = new List<(T Item, decimal Value)>();
        var shut = new List<(T Item, decimal Value)>();
        var withoutValue = new List<T>();
        foreach (var item in items)
        {
            if (valueOf(item) is not { } value)
            {
                withoutValue.Add(item);
            }
            else if (groupByMarket && isMarketShut?.Invoke(item) == true)
            {
                shut.Add((item, value));
            }
            else
            {
                open.Add((item, value));
            }
        }

        var result = new List<T>(items.Count);
        result.AddRange(Order(open, descending));
        result.AddRange(Order(shut, descending));
        result.AddRange(withoutValue);
        return result;
    }

    /// <summary>
    /// Orders items that all have a value.
    /// </summary>
    /// <typeparam name="T">
    /// The type of the items.
    /// </typeparam>
    /// <param name="items">
    /// The items with their value, in the order of the watchlist.
    /// </param>
    /// <param name="descending">
    /// Whether the largest value comes first.
    /// </param>
    /// <returns>
    /// The items in display order.
    /// </returns>
    private static IEnumerable<T> Order<T>(List<(T Item, decimal Value)> items, bool descending)
    {
        // OrderBy is stable, so equal values keep the order of the watchlist.
        var ordered = descending
            ? items.OrderByDescending(x => x.Value)
            : items.OrderBy(x => x.Value);
        return ordered.Select(x => x.Item);
    }
}
