using Avalonia;

namespace FluxTick.App.Views;

/// <summary>
/// A card as it would be laid out once the dragged card is out of the way.
/// </summary>
/// <param name="SectionKey">
/// The key of the section that holds the card.
/// </param>
/// <param name="Index">
/// The position of the card among the cards of its section, not counting the card being dragged.
/// </param>
/// <param name="Bounds">
/// The area of the card, including its margin.
/// </param>
internal readonly record struct CardSlot(string SectionKey, int Index, Rect Bounds);

/// <summary>
/// A section as it would be laid out once the dragged card is out of the way.
/// </summary>
/// <param name="Key">
/// The key of the section.
/// </param>
/// <param name="Bounds">
/// The area of the whole section: its header and its cards.
/// </param>
/// <param name="Header">
/// The area of the header, or an empty rectangle when the section has none.
/// </param>
/// <param name="ItemCount">
/// The number of cards in the section, not counting the card being dragged. A collapsed section has no laid-out
/// cards, so this is the only way to know where the end of it is.
/// </param>
/// <param name="IsCollapsed">
/// Whether the cards of the section are hidden.
/// </param>
internal readonly record struct SectionArea(string Key, Rect Bounds, Rect Header, int ItemCount, bool IsCollapsed);

/// <summary>
/// Where a dragged card would land.
/// </summary>
/// <param name="SectionKey">
/// The key of the target section.
/// </param>
/// <param name="Index">
/// The position among the other cards of the target section.
/// </param>
/// <param name="IsHeaderHighlight">
/// Whether the card would be dropped onto the header of a collapsed section, which has no card to make room next to.
/// </param>
internal readonly record struct DropTarget(string SectionKey, int Index, bool IsHeaderHighlight);

/// <summary>
/// Pure geometry helpers used by the card layout's drag-to-reorder interaction.
/// </summary>
internal static class CardDropMath
{
    /// <summary>
    /// Finds where a dragged card would land for a pointer position.
    /// </summary>
    /// <param name="slots">
    /// The cards other than the dragged one, as laid out without it.
    /// </param>
    /// <param name="sections">
    /// The sections in display order, as laid out without the dragged card.
    /// </param>
    /// <param name="point">
    /// The pointer position in the same coordinate space as the slots.
    /// </param>
    /// <param name="lockedSectionKey">
    /// The only section the card may be dropped into, or <see langword="null"/> when any section is allowed.
    /// </param>
    /// <returns>
    /// The drop target, or <see langword="null"/> when there is no section to drop into.
    /// </returns>
    public static DropTarget? ComputeTarget(
        IReadOnlyList<CardSlot> slots,
        IReadOnlyList<SectionArea> sections,
        Point point,
        string? lockedSectionKey)
    {
        var area = lockedSectionKey is null
            ? PickSection(sections, point)
            : sections.Where(s => s.Key == lockedSectionKey).Select(s => (SectionArea?)s).FirstOrDefault();
        if (area is not { } section)
        {
            return null;
        }

        if (section.IsCollapsed)
        {
            return new DropTarget(section.Key, section.ItemCount, IsHeaderHighlight: true);
        }

        var cards = slots.Where(s => s.SectionKey == section.Key).OrderBy(s => s.Index).ToList();
        return new DropTarget(section.Key, cards.Count == 0 ? 0 : IndexFor(cards, section, point), IsHeaderHighlight: false);
    }

    /// <summary>
    /// Picks the section under the pointer, or the vertically nearest one when the pointer is in a gap.
    /// </summary>
    /// <param name="sections">
    /// The laid-out sections.
    /// </param>
    /// <param name="point">
    /// The pointer position.
    /// </param>
    /// <returns>
    /// The section, or <see langword="null"/> when there are none.
    /// </returns>
    private static SectionArea? PickSection(IReadOnlyList<SectionArea> sections, Point point)
    {
        SectionArea? best = null;
        var bestDistance = double.MaxValue;
        foreach (var section in sections)
        {
            var distance = point.Y < section.Bounds.Top
                ? section.Bounds.Top - point.Y
                : Math.Max(0, point.Y - section.Bounds.Bottom);
            if (distance < bestDistance)
            {
                best = section;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// Finds the insertion position among the cards of an expanded section.
    /// </summary>
    /// <param name="cards">
    /// The cards of the section in order; never empty.
    /// </param>
    /// <param name="section">
    /// The section.
    /// </param>
    /// <param name="point">
    /// The pointer position.
    /// </param>
    /// <returns>
    /// The position among the cards, where the number of cards means after the last one.
    /// </returns>
    private static int IndexFor(IReadOnlyList<CardSlot> cards, SectionArea section, Point point)
    {
        if (section.Header.Contains(point) || point.Y < cards.Min(c => c.Bounds.Top))
        {
            return 0;
        }

        if (point.Y > cards.Max(c => c.Bounds.Bottom))
        {
            return cards.Count;
        }

        // A pointer inside a row only competes with the cards of that row; in the gap between rows every card does.
        var row = cards.Where(c => point.Y >= c.Bounds.Top && point.Y <= c.Bounds.Bottom).ToList();
        var candidates = row.Count > 0 ? row : cards;
        var nearest = candidates[0];
        var nearestDistance = double.MaxValue;
        foreach (var card in candidates)
        {
            var dx = Math.Max(Math.Max(card.Bounds.Left - point.X, 0), point.X - card.Bounds.Right);
            var dy = Math.Max(Math.Max(card.Bounds.Top - point.Y, 0), point.Y - card.Bounds.Bottom);
            var distance = (dx * dx) + (dy * dy);
            if (distance < nearestDistance)
            {
                nearest = card;
                nearestDistance = distance;
            }
        }

        return point.X < nearest.Bounds.Center.X ? nearest.Index : nearest.Index + 1;
    }
}
