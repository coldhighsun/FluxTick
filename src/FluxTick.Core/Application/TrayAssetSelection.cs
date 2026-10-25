using FluxTick.Abstractions;

namespace FluxTick.Core.Application;

/// <summary>
/// Pure helpers that pick the instruments the tray list shows and the order they appear in. An instrument is
/// identified everywhere in the tray list by the key made by <see cref="Key(string, string)"/>.
/// </summary>
public static class TrayAssetSelection
{
    /// <summary>
    /// The most instruments the tray list holds.
    /// </summary>
    public const int MaxAssets = 5;

    /// <summary>
    /// Makes the key that identifies an instrument in <see cref="GeneralSettings.TrayAssets"/>.
    /// </summary>
    /// <param name="providerId">
    /// The provider that serves the instrument.
    /// </param>
    /// <param name="symbol">
    /// The provider-native symbol.
    /// </param>
    /// <returns>
    /// The key.
    /// </returns>
    public static string Key(string providerId, string symbol) => $"{providerId}|{symbol}";

    /// <summary>
    /// Makes the key of a watchlist entry.
    /// </summary>
    /// <param name="entry">
    /// The entry.
    /// </param>
    /// <returns>
    /// The key.
    /// </returns>
    public static string Key(WatchlistEntry entry) => Key(entry.ProviderId, entry.Symbol);

    /// <summary>
    /// Makes the key of an asset.
    /// </summary>
    /// <param name="asset">
    /// The asset.
    /// </param>
    /// <returns>
    /// The key.
    /// </returns>
    public static string Key(Asset asset) => Key(asset.ProviderId, asset.Symbol);

    /// <summary>
    /// Picks the items the tray list shows.
    /// </summary>
    /// <typeparam name="T">
    /// The item type, such as a watchlist entry or a row.
    /// </typeparam>
    /// <param name="watchlist">
    /// The watchlist in its own order.
    /// </param>
    /// <param name="keyOf">
    /// Gets the key of an item.
    /// </param>
    /// <param name="trayAssets">
    /// The chosen keys in display order.
    /// </param>
    /// <returns>
    /// The chosen items in the chosen order. Keys without an item, and repeated keys, are skipped.
    /// </returns>
    public static IReadOnlyList<T> Resolve<T>(IReadOnlyList<T> watchlist, Func<T, string> keyOf, IReadOnlyList<string> trayAssets)
    {
        // Nothing is chosen by default, and the popup asks on every hover, so do not index the watchlist for nothing.
        if (trayAssets.Count == 0)
        {
            return [];
        }

        var byKey = new Dictionary<string, T>();
        foreach (var item in watchlist)
        {
            byKey.TryAdd(keyOf(item), item);
        }

        var picked = new List<T>();
        var seen = new HashSet<string>();
        foreach (var key in trayAssets)
        {
            if (byKey.TryGetValue(key, out var item) && seen.Add(key))
            {
                picked.Add(item);
            }
        }

        return picked;
    }

    /// <summary>
    /// Puts an instrument at the end of the tray list. A full list takes nothing more, and an instrument is on it once.
    /// </summary>
    /// <param name="trayAssets">
    /// The keys of the tray list. It is modified in place.
    /// </param>
    /// <param name="key">
    /// The key of the instrument.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the instrument was added.
    /// </returns>
    public static bool Add(List<string> trayAssets, string key)
    {
        if (trayAssets.Count >= MaxAssets || trayAssets.Contains(key))
        {
            return false;
        }

        trayAssets.Add(key);
        return true;
    }

    /// <summary>
    /// Takes an instrument off the tray list.
    /// </summary>
    /// <param name="trayAssets">
    /// The keys of the tray list. It is modified in place.
    /// </param>
    /// <param name="key">
    /// The key of the instrument.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the instrument was on the list.
    /// </returns>
    public static bool Remove(List<string> trayAssets, string key) => trayAssets.Remove(key);

    /// <summary>
    /// Takes an instrument off the tray list, or puts it at the end when it is not on it.
    /// </summary>
    /// <param name="trayAssets">
    /// The keys of the tray list. It is modified in place.
    /// </param>
    /// <param name="key">
    /// The key of the instrument.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the list changed; it does not when the instrument is not on a full list.
    /// </returns>
    public static bool Toggle(List<string> trayAssets, string key) => Remove(trayAssets, key) || Add(trayAssets, key);

    /// <summary>
    /// Moves an instrument to a position of the tray list.
    /// </summary>
    /// <param name="trayAssets">
    /// The keys of the tray list. It is modified in place.
    /// </param>
    /// <param name="key">
    /// The key of the instrument.
    /// </param>
    /// <param name="index">
    /// The zero-based target position; values outside the list are clamped to its ends.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the order changed.
    /// </returns>
    public static bool Move(List<string> trayAssets, string key, int index)
    {
        // An instrument that is not on the list (so possibly none is) has no position to move to; clamping to the ends of
        // an empty list would fail.
        var current = trayAssets.IndexOf(key);
        if (current < 0)
        {
            return false;
        }

        var target = Math.Clamp(index, 0, trayAssets.Count - 1);
        if (current == target)
        {
            return false;
        }

        trayAssets.RemoveAt(current);
        trayAssets.Insert(target, key);
        return true;
    }

    /// <summary>
    /// Makes a tray list valid: it forgets instruments that left the watchlist and keys that repeat, and keeps no more
    /// than <see cref="MaxAssets"/>.
    /// </summary>
    /// <param name="trayAssets">
    /// The keys of the tray list. It is modified in place.
    /// </param>
    /// <param name="watchlist">
    /// The watchlist.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when something had to be removed.
    /// </returns>
    public static bool Sanitize(List<string> trayAssets, IEnumerable<WatchlistEntry> watchlist)
    {
        // This runs on every save and the list is empty unless the user filled it, so only look at the watchlist when
        // there is something to check.
        if (trayAssets.Count == 0)
        {
            return false;
        }

        var onWatchlist = watchlist.Select(Key).ToHashSet();
        var seen = new HashSet<string>();
        var changed = trayAssets.RemoveAll(key => !onWatchlist.Contains(key) || !seen.Add(key)) > 0;
        if (trayAssets.Count > MaxAssets)
        {
            trayAssets.RemoveRange(MaxAssets, trayAssets.Count - MaxAssets);
            changed = true;
        }

        return changed;
    }
}
