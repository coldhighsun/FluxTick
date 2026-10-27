using FluxTick.Core.Application;

namespace FluxTick.App.Views;

/// <summary>
/// Pure rules for the smallest height of the main window. The list layout has a fixed smallest height; in the card
/// layout the window is never lower than what one card of the shown size needs.
/// </summary>
internal static class WindowHeightRules
{
    /// <summary>
    /// The smallest height of the list layout in device independent pixels.
    /// </summary>
    public const double ListMinHeight = 180;

    /// <summary>
    /// The space the header of a group takes above its cards.
    /// </summary>
    private static readonly double SectionHeaderSpace =
        CardLayout.SectionHeaderHeight + CardLayout.SectionHeaderMargin.Top + CardLayout.SectionHeaderMargin.Bottom;

    /// <summary>
    /// Gets the height of a card.
    /// </summary>
    /// <param name="size">
    /// The card size.
    /// </param>
    /// <returns>
    /// The height in device independent pixels, without the margin.
    /// </returns>
    public static double CardHeight(CardSize size) => size switch
    {
        CardSize.Small => CardLayout.SmallCardHeight,
        CardSize.Large => CardLayout.LargeCardHeight,
        _ => CardLayout.MediumCardHeight,
    };

    /// <summary>
    /// Gets the smallest height of the window for a layout.
    /// </summary>
    /// <param name="layout">
    /// The watchlist layout.
    /// </param>
    /// <param name="size">
    /// The card size.
    /// </param>
    /// <param name="grouped">
    /// Whether the cards are in groups, each of which has a header above its cards.
    /// </param>
    /// <param name="chrome">
    /// The height of the window around the list of cards: the header, the footer and the frame.
    /// </param>
    /// <returns>
    /// <see cref="ListMinHeight"/> in the list layout; in the card layout the height that shows one whole card with its
    /// margin, the header of its group and the padding of the list, but never less than <see cref="ListMinHeight"/>.
    /// </returns>
    public static double MinHeight(WatchlistLayout layout, CardSize size, bool grouped, double chrome) => layout switch
    {
        WatchlistLayout.Cards => Math.Max(
            ListMinHeight,
            chrome + CardLayout.ListPadding.Top + CardLayout.ListPadding.Bottom + (2 * CardLayout.CardMargin) + CardHeight(size) + (grouped ? SectionHeaderSpace : 0)),
        _ => ListMinHeight,
    };
}
