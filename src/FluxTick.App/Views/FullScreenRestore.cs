using Avalonia;
using Avalonia.Controls;
using FluxTick.Core.Application;

namespace FluxTick.App.Views;

/// <summary>
/// Where the window was before it went full screen: the position and the height it comes back to. The width is not
/// part of it, because the layout decides that (a fixed one in the list layout, the remembered one in the card layout).
/// </summary>
/// <param name="Position">
/// The top-left corner in screen pixels.
/// </param>
/// <param name="Height">
/// The height in device independent pixels.
/// </param>
public sealed record RestoreBounds(PixelPoint Position, double Height);

/// <summary>
/// The ways the window leaves full screen, which decide what it gets back.
/// </summary>
public enum FullScreenExit
{
    /// <summary>
    /// The user left it (Escape, a double click, the button): the window gets its height and place back.
    /// </summary>
    Restore,

    /// <summary>
    /// The list layout was chosen, whose width is fixed: the layout decides the size, nothing is put back.
    /// </summary>
    KeepLayoutSize,

    /// <summary>
    /// The header was dragged: the height is put back, and the window goes where the pointer takes it.
    /// </summary>
    Dragged,
}

/// <summary>
/// Decides how the window comes back from full screen, and where it lands when the user drags it out by its header.
/// </summary>
public static class FullScreenRestore
{
    /// <summary>
    /// Gets the height the window has when nothing is known about the one it had before.
    /// </summary>
    public static double DefaultHeight { get; } = new WindowSettings().Height;

    /// <summary>
    /// The least width of the header, in screen pixels, that must be on a screen for the window to be reachable.
    /// </summary>
    private const int MinReachableWidth = 100;

    /// <summary>
    /// The least height of the header, in screen pixels, that must be on a screen for the window to be reachable.
    /// A few pixels above or below the work area, as a window flush with the screen's edge can be, do not matter.
    /// </summary>
    private const int MinReachableHeight = 10;

    /// <summary>
    /// Chooses the height the window gets when it leaves full screen.
    /// </summary>
    /// <param name="record">
    /// What was remembered before full screen, or <see langword="null"/> when nothing was.
    /// </param>
    /// <param name="minHeight">
    /// The smallest height the window may have.
    /// </param>
    /// <param name="maxHeight">
    /// The tallest height that fits the screen, in device independent pixels. It wins over the minimum, so a small screen
    /// never gets a window that does not fit on it.
    /// </param>
    /// <returns>
    /// The remembered height, or the default one without a record, not below the minimum and not above what the screen holds.
    /// </returns>
    public static double Height(RestoreBounds? record, double minHeight, double maxHeight = double.PositiveInfinity) =>
        Math.Min(maxHeight, Math.Max(minHeight, record is { Height: > 0 } known ? known.Height : DefaultHeight));

    /// <summary>
    /// Determines whether a window put at a place can still be grabbed by its header: enough of the header strip lies
    /// on one screen's work area. A remembered place can stop being so after the screens were rearranged.
    /// </summary>
    /// <param name="header">
    /// The strip of the header at the place, in screen pixels.
    /// </param>
    /// <param name="workAreas">
    /// The usable areas of the connected screens, in screen pixels.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a good part of the strip's width, and enough of its height to grab it, is on one area.
    /// </returns>
    public static bool IsReachable(PixelRect header, IEnumerable<PixelRect> workAreas)
    {
        var neededWidth = Math.Min(header.Width, MinReachableWidth);
        var neededHeight = Math.Min(header.Height, MinReachableHeight);
        foreach (var area in workAreas)
        {
            var shown = header.Intersect(area);
            if (shown.Width >= neededWidth && shown.Height >= neededHeight)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines what a change of the window state puts back of the window as it was before full screen.
    /// </summary>
    /// <param name="wasFullScreen">
    /// Whether the window was full screen until now. A restore from the minimized or maximized state is not a way out of
    /// full screen, and neither is going back to Normal from a state the window never left full screen for.
    /// </param>
    /// <param name="newState">
    /// The state the window has now.
    /// </param>
    /// <param name="exit">
    /// How the window leaves full screen.
    /// </param>
    /// <returns>
    /// Whether the remembered height is put back, and whether the remembered position is. A window that is dragged out
    /// goes where the pointer takes it instead of back to its place; one that keeps the layout's size gets nothing.
    /// </returns>
    public static (bool Size, bool Position) Restores(bool wasFullScreen, WindowState newState, FullScreenExit exit)
    {
        if (!wasFullScreen || newState != WindowState.Normal)
        {
            return (false, false);
        }

        return exit switch
        {
            FullScreenExit.Restore => (true, true),
            FullScreenExit.Dragged => (true, false),
            _ => (false, false),
        };
    }

    /// <summary>
    /// Places a window that was dragged out of full screen so that the pointer is in the middle of its header: centered
    /// across the window, at the given distance from the top.
    /// </summary>
    /// <param name="pointer">
    /// The pointer in screen pixels.
    /// </param>
    /// <param name="headerMiddleY">
    /// How far below the top edge the middle of the header is, in screen pixels.
    /// </param>
    /// <param name="size">
    /// The size the window has now, in screen pixels.
    /// </param>
    /// <param name="workArea">
    /// The usable area of the screen the pointer is on, in screen pixels.
    /// </param>
    /// <returns>
    /// The top-left position of the window, kept inside the work area as far as it fits.
    /// </returns>
    public static PixelPoint Place(PixelPoint pointer, int headerMiddleY, PixelSize size, PixelRect workArea)
    {
        var left = pointer.X - (int)Math.Round(size.Width / 2.0);
        var top = pointer.Y - headerMiddleY;
        return new PixelPoint(
            WindowWidthRules.ClampLeft(left, size.Width, workArea.X, workArea.Right),
            ToolWindowPlacement.Clamp(top, workArea.Y, workArea.Bottom - size.Height));
    }
}
