using Avalonia;
using FluxTick.Core.Application;

namespace FluxTick.App.Views;

/// <summary>
/// A section of cards as far as the fit of the window needs to know it.
/// </summary>
/// <param name="Count">
/// The number of cards in the section; an empty section still reserves the room of one row.
/// </param>
/// <param name="IsCollapsed">
/// Whether the cards of the section are hidden, in which case only its header takes room.
/// </param>
internal readonly record struct FitSection(int Count, bool IsCollapsed);

/// <summary>
/// Pure rules that fit the size of the main window to its content when the layout changes: the height of the list
/// layout, and the width and height of the card layout, whose columns are chosen so that the window has a shape
/// close to a common screen ratio.
/// </summary>
internal static class WindowFitRules
{
    /// <summary>
    /// The width to height ratios a fitted card window aims for.
    /// </summary>
    public static readonly IReadOnlyList<double> TargetAspectRatios = [4d / 3d, 16d / 9d];

    /// <summary>
    /// Gets the size of the card layout that shows every card and has a shape close to <see cref="TargetAspectRatios"/>.
    /// </summary>
    /// <param name="sections">
    /// The sections in display order.
    /// </param>
    /// <param name="grouped">
    /// Whether the sections have a header above their cards.
    /// </param>
    /// <param name="size">
    /// The card size.
    /// </param>
    /// <param name="chrome">
    /// The height of the window around the list of cards: the header, the footer and the frame.
    /// </param>
    /// <param name="maxWidth">
    /// The most the window can be wide, which is the width of the work area of its screen.
    /// </param>
    /// <param name="maxHeight">
    /// The most the window can be high, which is the height of the work area of its screen.
    /// </param>
    /// <param name="renderScaling">
    /// The number of screen pixels in one device independent pixel. The layout rounds the size and the margins of every
    /// card and header up to whole screen pixels, so at a scale such as 150% each row is a pixel higher than the sum of
    /// the sizes; the sizes are rounded the same way here.
    /// </param>
    /// <returns>
    /// The width and height in device independent pixels. The width is at least
    /// <see cref="WindowWidthRules.CardsMinWidth"/>; the height is below <paramref name="maxHeight"/> unless the cards need a
    /// scroll bar at every number of columns.
    /// </returns>
    public static Size CardsSize(
        IReadOnlyList<FitSection> sections,
        bool grouped,
        CardSize size,
        double chrome,
        double maxWidth,
        double maxHeight,
        double renderScaling = 1)
    {
        var cardWidth = Pixels(WindowHeightRules.CardWidth(size), renderScaling) + (2 * Pixels(CardLayout.CardMargin, renderScaling));
        var cardHeight = Pixels(WindowHeightRules.CardHeight(size), renderScaling) + (2 * Pixels(CardLayout.CardMargin, renderScaling));
        var sideSpace = Pixels(CardLayout.ListPadding.Left, renderScaling) + Pixels(CardLayout.ListPadding.Right, renderScaling)
            + (2 * Pixels(CardLayout.WindowFrame, renderScaling));
        var headerSpace = Pixels(CardLayout.SectionHeaderHeight, renderScaling)
            + Pixels(CardLayout.SectionHeaderMargin.Top, renderScaling) + Pixels(CardLayout.SectionHeaderMargin.Bottom, renderScaling);
        var fixedHeight = chrome + Pixels(CardLayout.ListPadding.Top, renderScaling) + Pixels(CardLayout.ListPadding.Bottom, renderScaling)
            + (grouped ? sections.Count * headerSpace : 0);
        var minHeight = Math.Min(WindowHeightRules.MinHeight(WatchlistLayout.Cards, size, grouped, chrome), maxHeight);

        // More columns than the fullest section has cards, or than fit the screen, add only empty space.
        var widest = Math.Max(1, sections.Where(section => !section.IsCollapsed).Select(section => section.Count).DefaultIfEmpty(1).Max());
        var fitting = (int)Math.Floor((maxWidth - sideSpace) / cardWidth);
        var mostColumns = Math.Max(1, Math.Min(widest, fitting));

        var best = default(Size);
        var bestFits = false;
        var bestScore = double.PositiveInfinity;
        for (var columns = 1; columns <= mostColumns; columns++)
        {
            // The window is never narrower than one large card, which may leave room for more columns than asked for.
            var width = Math.Max((columns * cardWidth) + sideSpace, WindowWidthRules.CardsMinWidth);
            var flowing = Math.Max(columns, (int)Math.Floor(((width - sideSpace) / cardWidth) + 1e-6));
            var rows = sections.Where(section => !section.IsCollapsed).Sum(section => Math.Max(1, (section.Count + flowing - 1) / flowing));
            var wanted = Math.Max(fixedHeight + (rows * cardHeight), minHeight);
            var fits = wanted <= maxHeight;
            var height = Math.Min(wanted, maxHeight);
            var score = Score(width, height);

            // A candidate that shows every card beats one that needs a scroll bar; among equals the closer shape wins, and
            // the first, which has fewer columns, on a tie.
            if ((fits && !bestFits) || (fits == bestFits && score < bestScore - 1e-9))
            {
                best = new Size(width, height);
                bestFits = fits;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>
    /// Rounds a size up to whole screen pixels, the way the layout does.
    /// </summary>
    /// <param name="value">
    /// The size in device independent pixels.
    /// </param>
    /// <param name="renderScaling">
    /// The number of screen pixels in one device independent pixel.
    /// </param>
    /// <returns>
    /// The size in device independent pixels that is a whole number of screen pixels.
    /// </returns>
    private static double Pixels(double value, double renderScaling) =>
        renderScaling <= 0 ? value : Math.Ceiling((value * renderScaling) - 1e-6) / renderScaling;

    /// <summary>
    /// Gets how far the shape of a window is from the nearest of <see cref="TargetAspectRatios"/>.
    /// </summary>
    /// <param name="width">
    /// The width of the window.
    /// </param>
    /// <param name="height">
    /// The height of the window.
    /// </param>
    /// <returns>
    /// The distance between the logarithms of the ratios; zero for a perfect match.
    /// </returns>
    public static double Score(double width, double height)
    {
        var ratio = Math.Log(width / height);
        return TargetAspectRatios.Min(target => Math.Abs(ratio - Math.Log(target)));
    }

    /// <summary>
    /// Moves a window up so that its bottom lies inside the work area. A window higher than the work area is aligned to
    /// the top of it.
    /// </summary>
    /// <param name="top">
    /// The top edge of the window in screen pixels.
    /// </param>
    /// <param name="height">
    /// The height of the window in screen pixels.
    /// </param>
    /// <param name="areaTop">
    /// The top edge of the work area.
    /// </param>
    /// <param name="areaBottom">
    /// The bottom edge of the work area.
    /// </param>
    /// <returns>
    /// The top edge that keeps the window inside the work area where possible.
    /// </returns>
    public static int ClampTop(int top, double height, int areaTop, int areaBottom) =>
        Math.Max(areaTop, Math.Min(top, (int)Math.Floor(areaBottom - height)));
}
