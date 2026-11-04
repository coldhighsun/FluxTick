using FluxTick.Core.Application;

namespace FluxTick.App.Views;

/// <summary>
/// Pure rules for the smallest height of the main window. The list layout has a fixed smallest height; in the card
/// layout the window is as low as what one card of the shown size needs.
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
    /// Gets the width of a card.
    /// </summary>
    /// <param name="size">
    /// The card size.
    /// </param>
    /// <returns>
    /// The width in device independent pixels, without the margin.
    /// </returns>
    public static double CardWidth(CardSize size) => size switch
    {
        CardSize.Small => CardLayout.SmallCardWidth,
        CardSize.Large => CardLayout.LargeCardWidth,
        _ => CardLayout.MediumCardWidth,
    };

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
    /// margin, the header of its group and the padding of the list, which for a small card is below
    /// <see cref="ListMinHeight"/>.
    /// </returns>
    public static double MinHeight(WatchlistLayout layout, CardSize size, bool grouped, double chrome) => layout switch
    {
        WatchlistLayout.Cards =>
            chrome + CardLayout.ListPadding.Top + CardLayout.ListPadding.Bottom + (2 * CardLayout.CardMargin) + CardHeight(size) + (grouped ? SectionHeaderSpace : 0),
        _ => ListMinHeight,
    };

    /// <summary>
    /// Rounds a height up to whole screen pixels and adds one pixel for the rounding of the layout. At a scale such as
    /// 150% the margins of a card are not whole pixels and the layout rounds each of them up, so a window that is only
    /// as high as the sum of the sizes is a fraction of a pixel too low and the list shows a scroll bar.
    /// </summary>
    /// <param name="height">
    /// The height in device independent pixels.
    /// </param>
    /// <param name="renderScaling">
    /// The number of screen pixels in one device independent pixel.
    /// </param>
    /// <returns>
    /// A height in device independent pixels that is a whole number of screen pixels and at least one pixel above
    /// <paramref name="height"/>.
    /// </returns>
    public static double RoundUpToPixels(double height, double renderScaling) =>
        renderScaling <= 0 ? height : (Math.Ceiling((height * renderScaling) - 1e-6) + 1) / renderScaling;

    /// <summary>
    /// Gets the smallest height of the window in the list layout: the height at which every row is shown without a
    /// scroll bar.
    /// </summary>
    /// <param name="content">
    /// The height of all rows together.
    /// </param>
    /// <param name="chrome">
    /// The height of the window around the list: the header, the footer and the frame.
    /// </param>
    /// <param name="limit">
    /// The most the window can be high, which is the height of the screen it is on.
    /// </param>
    /// <returns>
    /// The height that shows all rows, but at least <see cref="ListMinHeight"/>, and not more than <paramref name="limit"/>
    /// once there are so many rows that they need a scroll bar anyway.
    /// </returns>
    public static double ListContentMinHeight(double content, double chrome, double limit) =>
        Math.Max(ListMinHeight, Math.Min(chrome + content, limit));
}
