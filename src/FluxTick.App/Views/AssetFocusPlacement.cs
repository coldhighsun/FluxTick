using Avalonia;

namespace FluxTick.App.Views;

/// <summary>
/// Decides which monitor each screen-filling view of an instrument goes to, so that several instruments can be shown at the
/// same time, each on a monitor of its own. A monitor is identified by its bounds.
/// </summary>
internal static class AssetFocusPlacement
{
    /// <summary>
    /// Picks the monitor for a view that is opened: the preferred one when it is free, else the first free one, and when
    /// every monitor already has a view, the preferred one, whose view is to be replaced.
    /// </summary>
    /// <param name="screens">The bounds of all monitors.</param>
    /// <param name="preferred">The bounds of the preferred monitor, usually the one of the main window.</param>
    /// <param name="occupied">The bounds of the monitors that already have a view.</param>
    /// <returns>
    /// The bounds of the monitor, and whether the view that is on it has to be closed.
    /// </returns>
    public static (PixelRect Screen, bool Replace) PickScreen(
        IReadOnlyList<PixelRect> screens,
        PixelRect preferred,
        IReadOnlyCollection<PixelRect> occupied)
    {
        if (!occupied.Contains(preferred))
        {
            return (preferred, false);
        }

        foreach (var screen in screens)
        {
            if (!occupied.Contains(screen))
            {
                return (screen, false);
            }
        }

        return (preferred, true);
    }

    /// <summary>
    /// Picks the monitor a dragged view settles on: the one it was dropped on when no other view is there, else the one it
    /// came from.
    /// </summary>
    /// <param name="target">The bounds of the monitor the view was dropped on.</param>
    /// <param name="home">The bounds of the monitor the view came from.</param>
    /// <param name="occupiedByOthers">The bounds of the monitors that have another view.</param>
    /// <returns>The bounds of the monitor to fill.</returns>
    public static PixelRect ResolveDrop(PixelRect target, PixelRect home, IReadOnlyCollection<PixelRect> occupiedByOthers)
    {
        return occupiedByOthers.Contains(target) ? home : target;
    }
}
