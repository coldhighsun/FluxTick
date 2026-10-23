using FluxTick.Core.Application;

namespace FluxTick.App.Views;

/// <summary>
/// The width limits of the main window.
/// </summary>
/// <param name="Min">
/// The smallest width in device independent pixels.
/// </param>
/// <param name="Max">
/// The largest width in device independent pixels; infinite when the width is free.
/// </param>
internal readonly record struct WidthRule(double Min, double Max);

/// <summary>
/// Pure rules for the width of the main window. The list layout has a fixed width that depends on whether the
/// sparkline column is shown; the card layout is free and remembers the width the user chose.
/// </summary>
internal static class WindowWidthRules
{
    /// <summary>
    /// The width of the list layout with the sparkline column.
    /// </summary>
    public const double ListWidthWithChart = 340;

    /// <summary>
    /// The width of the list layout in compact mode, where the sparkline column is hidden. The name column keeps
    /// the width it has in <see cref="ListWidthWithChart"/>: the 80 pixels of the sparkline column are gone.
    /// </summary>
    public const double ListWidthCompact = 260;

    /// <summary>
    /// The smallest width of the card layout.
    /// </summary>
    public const double CardsMinWidth = 300;

    /// <summary>
    /// How long the window takes to change its width.
    /// </summary>
    public static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(180);

    /// <summary>
    /// Gets the value between two numbers for a point in time of the width animation, eased out so that the window
    /// moves fast at first and settles gently.
    /// </summary>
    /// <param name="from">
    /// The value at the start.
    /// </param>
    /// <param name="to">
    /// The value at the end.
    /// </param>
    /// <param name="progress">
    /// The elapsed share of the animation; values outside 0 to 1 are clamped.
    /// </param>
    /// <returns>
    /// The value at that point in time.
    /// </returns>
    public static double Animate(double from, double to, double progress)
    {
        var remaining = 1 - Math.Clamp(progress, 0, 1);
        return from + ((to - from) * (1 - (remaining * remaining * remaining)));
    }

    /// <summary>
    /// Gets the width limits for a layout.
    /// </summary>
    /// <param name="layout">
    /// The watchlist layout.
    /// </param>
    /// <param name="compact">
    /// Whether compact mode is on, which hides the sparklines.
    /// </param>
    /// <returns>
    /// The width limits. In the list layout both are the same, so the width is fixed.
    /// </returns>
    public static WidthRule For(WatchlistLayout layout, bool compact) => layout switch
    {
        WatchlistLayout.Cards => new WidthRule(CardsMinWidth, double.PositiveInfinity),
        _ when compact => new WidthRule(ListWidthCompact, ListWidthCompact),
        _ => new WidthRule(ListWidthWithChart, ListWidthWithChart),
    };

    /// <summary>
    /// Gets the width the window should have for a layout.
    /// </summary>
    /// <param name="layout">
    /// The watchlist layout.
    /// </param>
    /// <param name="compact">
    /// Whether compact mode is on.
    /// </param>
    /// <param name="cardsWidth">
    /// The width the user chose for the card layout.
    /// </param>
    /// <returns>
    /// The fixed width of the list layout, or the remembered width of the card layout kept within its limits.
    /// </returns>
    public static double TargetWidth(WatchlistLayout layout, bool compact, double cardsWidth)
    {
        var rule = For(layout, compact);
        return Math.Clamp(cardsWidth, rule.Min, rule.Max);
    }

    /// <summary>
    /// Gets the left edge a window needs to keep its right edge in place when its width changes.
    /// </summary>
    /// <param name="left">
    /// The current left edge in screen pixels.
    /// </param>
    /// <param name="oldWidth">
    /// The current width in screen pixels.
    /// </param>
    /// <param name="newWidth">
    /// The new width in screen pixels.
    /// </param>
    /// <returns>
    /// The new left edge in screen pixels.
    /// </returns>
    public static int LeftForRightEdge(int left, double oldWidth, double newWidth) =>
        (int)Math.Round(left + oldWidth - newWidth);

    /// <summary>
    /// Moves a window sideways so that it lies inside the work area. A window wider than the work area is aligned
    /// to its left edge.
    /// </summary>
    /// <param name="left">
    /// The left edge of the window in screen pixels.
    /// </param>
    /// <param name="width">
    /// The width of the window in screen pixels.
    /// </param>
    /// <param name="areaLeft">
    /// The left edge of the work area.
    /// </param>
    /// <param name="areaRight">
    /// The right edge of the work area.
    /// </param>
    /// <returns>
    /// The left edge that keeps the window inside the work area where possible.
    /// </returns>
    public static int ClampLeft(int left, double width, int areaLeft, int areaRight) =>
        Math.Max(areaLeft, Math.Min(left, (int)Math.Floor(areaRight - width)));
}
