using Avalonia;

namespace FluxTick.App.Views;

/// <summary>
/// A section as it is laid out now, described by what is needed to predict how it flows once cards come and go.
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
/// <param name="BodyTop">
/// The top of the first row of cards. An empty section reserves the room of one row at this position.
/// </param>
/// <param name="OriginalRows">
/// The number of rows the cards occupy now, counting the dragged card; one for an empty section.
/// </param>
/// <param name="IsCollapsed">
/// Whether the cards of the section are hidden, in which case it takes no room for them.
/// </param>
/// <param name="OtherCount">
/// The number of cards in the section other than the dragged one.
/// </param>
internal readonly record struct FlowSection(string Key, Rect Bounds, Rect Header, double BodyTop, int OriginalRows, bool IsCollapsed, int OtherCount);

/// <summary>
/// A section after the cards have flowed into their new places.
/// </summary>
/// <param name="Key">
/// The key of the section.
/// </param>
/// <param name="Bounds">
/// The new area of the whole section.
/// </param>
/// <param name="Header">
/// The new area of the header.
/// </param>
/// <param name="ShiftY">
/// How far the section as a whole moved down because of the sections above it growing or shrinking.
/// </param>
/// <param name="Left">
/// The left edge of the cards.
/// </param>
/// <param name="BodyTop">
/// The top of the first row before <paramref name="ShiftY"/> is applied.
/// </param>
/// <param name="CardSize">
/// The size of a card, including its margin.
/// </param>
/// <param name="PerRow">
/// How many cards fit in a row.
/// </param>
internal readonly record struct SectionFlow(string Key, Rect Bounds, Rect Header, double ShiftY, double Left, double BodyTop, Size CardSize, int PerRow)
{
    /// <summary>
    /// Gets the area of a position within the section, not counting the shift of the section itself.
    /// </summary>
    /// <param name="position">
    /// The zero-based position among the cards of the section.
    /// </param>
    /// <returns>
    /// The area, in the coordinates of the section's own layout.
    /// </returns>
    public Rect LocalSlot(int position) => new(
        Left + ((position % PerRow) * CardSize.Width),
        BodyTop + ((position / PerRow) * CardSize.Height),
        CardSize.Width,
        CardSize.Height);

    /// <summary>
    /// Gets the area of a position within the section, including the shift of the section itself.
    /// </summary>
    /// <param name="position">
    /// The zero-based position among the cards of the section.
    /// </param>
    /// <returns>
    /// The area in host coordinates.
    /// </returns>
    public Rect Slot(int position) => LocalSlot(position).Translate(new Vector(0, ShiftY));
}

/// <summary>
/// Pure geometry helpers that predict how the cards flow while one of them is dragged. The cards of a section wrap
/// like a wrap panel of equally sized items, so the prediction only needs the card size and the width of the section.
/// </summary>
internal static class CardFlowMath
{
    /// <summary>
    /// Predicts the layout of the sections when the dragged card is out of its section and, optionally, a gap is
    /// left for it in another one.
    /// </summary>
    /// <param name="sections">
    /// The sections as they are laid out now, in display order.
    /// </param>
    /// <param name="card">
    /// The size of a card, including its margin.
    /// </param>
    /// <param name="gapSectionKey">
    /// The key of the section that keeps a gap for the dragged card, or <see langword="null"/> for no gap.
    /// </param>
    /// <returns>
    /// One entry per section, in the same order.
    /// </returns>
    public static IReadOnlyList<SectionFlow> Layout(IReadOnlyList<FlowSection> sections, Size card, string? gapSectionKey)
    {
        var result = new List<SectionFlow>(sections.Count);
        var shift = 0d;
        foreach (var section in sections)
        {
            var perRow = Math.Max(1, (int)Math.Floor((section.Bounds.Width / card.Width) + 1e-6));
            var count = section.OtherCount + (section.Key == gapSectionKey ? 1 : 0);
            var rows = section.IsCollapsed ? 0 : Math.Max(1, (count + perRow - 1) / perRow);
            var originalRows = section.IsCollapsed ? 0 : section.OriginalRows;
            var delta = (rows - originalRows) * card.Height;

            var offset = new Vector(0, shift);
            var bounds = new Rect(section.Bounds.X, section.Bounds.Y + shift, section.Bounds.Width, section.Bounds.Height + delta);
            result.Add(new SectionFlow(section.Key, bounds, section.Header.Translate(offset), shift, section.Bounds.Left, section.BodyTop, card, perRow));
            shift += delta;
        }

        return result;
    }

    /// <summary>
    /// Gets the position a card takes when a gap is left for the dragged card.
    /// </summary>
    /// <param name="index">
    /// The index of the card among the cards other than the dragged one.
    /// </param>
    /// <param name="gapIndex">
    /// The position of the gap, or <see langword="null"/> when there is none.
    /// </param>
    /// <returns>
    /// The index, moved one further when the card is at or behind the gap.
    /// </returns>
    public static int PositionOf(int index, int? gapIndex) => gapIndex is { } gap && index >= gap ? index + 1 : index;
}
