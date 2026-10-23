using FluxTick.Abstractions;

namespace FluxTick.Core.Application;

/// <summary>
/// Pure helpers that reorder the persisted watchlist.
/// </summary>
public static class WatchlistReorder
{
    /// <summary>
    /// Moves an entry so that it becomes the item at a given position of a section.
    /// </summary>
    /// <param name="list">
    /// The watchlist in display order. It is modified in place.
    /// </param>
    /// <param name="entry">
    /// The entry to move. It must be an element of <paramref name="list"/>.
    /// </param>
    /// <param name="inTargetSection">
    /// Tells whether an entry belongs to the target section. It is evaluated on the entries other than
    /// <paramref name="entry"/>, so it does not matter whether the moved entry matches it.
    /// </param>
    /// <param name="indexInSection">
    /// The zero-based position the entry should take among the other entries of the target section. Values past the
    /// end append it after the last one.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the order of the list changed.
    /// </returns>
    public static bool Move(List<WatchlistEntry> list, WatchlistEntry entry, Func<WatchlistEntry, bool> inTargetSection, int indexInSection)
    {
        var current = list.IndexOf(entry);
        if (current < 0)
        {
            return false;
        }

        list.RemoveAt(current);

        var members = list.Where(inTargetSection).ToList();
        var position = list.Count;
        if (members.Count > 0)
        {
            var clamped = Math.Clamp(indexInSection, 0, members.Count);
            position = clamped < members.Count
                ? list.IndexOf(members[clamped])
                : list.IndexOf(members[^1]) + 1;
        }

        list.Insert(position, entry);
        return position != current;
    }

    /// <summary>
    /// Moves an asset class section to the place of another visible section.
    /// </summary>
    /// <param name="order">
    /// Every asset class in display order. The list is modified in place.
    /// </param>
    /// <param name="type">
    /// The class to move. It must be one of <paramref name="visible"/>.
    /// </param>
    /// <param name="visible">
    /// The classes that currently have a section, in display order. Classes without instruments are not shown, so
    /// positions are counted among these.
    /// </param>
    /// <param name="newIndex">
    /// The zero-based target position among <paramref name="visible"/>; values outside the list are clamped.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the order of the list changed.
    /// </returns>
    public static bool MoveType(List<AssetClass> order, AssetClass type, IReadOnlyList<AssetClass> visible, int newIndex)
    {
        var current = visible.ToList().IndexOf(type);
        if (current < 0 || !order.Contains(type))
        {
            return false;
        }

        var target = Math.Clamp(newIndex, 0, visible.Count - 1);
        if (target == current)
        {
            return false;
        }

        // The class takes the place of the one that is at the target position now: before it when moving up and
        // after it when moving down, so that the classes in between keep their order.
        var anchor = visible[target];
        order.Remove(type);
        order.Insert(order.IndexOf(anchor) + (target > current ? 1 : 0), type);
        return true;
    }

    /// <summary>
    /// Moves a group to an absolute position in the list of groups.
    /// </summary>
    /// <param name="groups">
    /// The groups in display order. The list is modified in place.
    /// </param>
    /// <param name="groupId">
    /// The id of the group to move.
    /// </param>
    /// <param name="newIndex">
    /// The zero-based target position; values outside the list are clamped to its ends.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the order of the list changed.
    /// </returns>
    public static bool MoveGroup(List<WatchGroup> groups, string groupId, int newIndex)
    {
        var current = groups.FindIndex(g => g.Id == groupId);
        if (current < 0)
        {
            return false;
        }

        var target = Math.Clamp(newIndex, 0, groups.Count - 1);
        if (target == current)
        {
            return false;
        }

        var group = groups[current];
        groups.RemoveAt(current);
        groups.Insert(target, group);
        return true;
    }
}
