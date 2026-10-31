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
/// Pure rules for the width of the main window. The list layout has a fixed width; the card layout is free and remembers the width the user chose.
/// </summary>
internal static class WindowWidthRules
{
    /// <summary>
    /// The width of the list layout.
    /// </summary>
    public const double ListWidth = 360;

    /// <summary>
    /// The smallest width of the card layout: just wide enough for one large card, so that no space is left beside it.
    /// </summary>
    public static readonly double CardsMinWidth = CardLayout.OneLargeCardWindowWidth;

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
    /// <returns>
    /// The width limits. In the list layout both are the same, so the width is fixed.
    /// </returns>
    public static WidthRule For(WatchlistLayout layout) => layout switch
    {
        WatchlistLayout.Cards => new WidthRule(CardsMinWidth, double.PositiveInfinity),
        _ => new WidthRule(ListWidth, ListWidth),
    };

    /// <summary>
    /// Gets the width the window should have for a layout.
    /// </summary>
    /// <param name="layout">
    /// The watchlist layout.
    /// </param>
    /// <param name="cardsWidth">
    /// The width the user chose for the card layout.
    /// </param>
    /// <returns>
    /// The fixed width of the list layout, or the remembered width of the card layout kept within its limits.
    /// </returns>
    public static double TargetWidth(WatchlistLayout layout, double cardsWidth)
    {
        var rule = For(layout);
        return Math.Clamp(cardsWidth, rule.Min, rule.Max);
    }

    /// <summary>
    /// Gets the left edge a window needs to keep its center in place when its width changes.
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
    public static int LeftForCenter(int left, double oldWidth, double newWidth) =>
        (int)Math.Round(left + ((oldWidth - newWidth) / 2));

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
    /// Gets the left edge a window needs when its width changes around the anchor that belongs to the edge it is docked
    /// to: the left edge stays at a left edge, the right edge at a right edge, and the center elsewhere.
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
    /// <param name="edge">
    /// The edge the window is docked to.
    /// </param>
    /// <returns>
    /// The new left edge in screen pixels.
    /// </returns>
    public static int LeftForDock(int left, double oldWidth, double newWidth, DockEdge edge) => edge switch
    {
        DockEdge.Left => left,
        DockEdge.Right => LeftForRightEdge(left, oldWidth, newWidth),
        _ => LeftForCenter(left, oldWidth, newWidth),
    };

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
