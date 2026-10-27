using Avalonia;

namespace FluxTick.App.Views;

/// <summary>
/// The sizes of the card layout that the XAML files and <see cref="WindowWidthRules"/> share, so that the smallest width
/// of the window fits one large card exactly. Theme.axaml, WatchlistView.axaml and MainWindow.axaml take them from here
/// through <c>x:Static</c>.
/// </summary>
internal static class CardLayout
{
    /// <summary>
    /// The width of a large card.
    /// </summary>
    public const double LargeCardWidth = 300;

    /// <summary>
    /// The height of a small card.
    /// </summary>
    public const double SmallCardHeight = 54;

    /// <summary>
    /// The height of a medium card.
    /// </summary>
    public const double MediumCardHeight = 96;

    /// <summary>
    /// The height of a large card.
    /// </summary>
    public const double LargeCardHeight = 176;

    /// <summary>
    /// The height of the header of a group.
    /// </summary>
    public const double SectionHeaderHeight = 20;

    /// <summary>
    /// The margin of the header of a group.
    /// </summary>
    public static readonly Thickness SectionHeaderMargin = new(6, 8, 6, 2);

    /// <summary>
    /// The space around a card on each side.
    /// </summary>
    public const double CardMargin = 3;

    /// <summary>
    /// The margin of a card.
    /// </summary>
    public static readonly Thickness CardMarginThickness = new(CardMargin);

    /// <summary>
    /// The padding of the list of cards. It is the same on both sides, so that the cards have the same space left and
    /// right.
    /// </summary>
    public static readonly Thickness ListPadding = new(10, 4, 10, 6);

    /// <summary>
    /// The margin around the content of the main window on each side.
    /// </summary>
    public const double WindowFrame = 1;

    /// <summary>
    /// The margin around the content of the main window.
    /// </summary>
    public static readonly Thickness WindowFrameMargin = new(WindowFrame);

    /// <summary>
    /// The width the window needs for one large card with the same space left and right of it.
    /// </summary>
    public static readonly double OneLargeCardWindowWidth =
        LargeCardWidth + (2 * CardMargin) + ListPadding.Left + ListPadding.Right + (2 * WindowFrame);
}
