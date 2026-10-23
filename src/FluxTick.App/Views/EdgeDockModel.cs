using Avalonia;

namespace FluxTick.App.Views;

/// <summary>
/// What to do when the pointer has been away from a shown, docked window for a while.
/// </summary>
internal enum DockLeave
{
    /// <summary>
    /// Nothing to do: the window is not docked, is hidden already, or the pointer is back.
    /// </summary>
    Stop,

    /// <summary>
    /// Something keeps the window open; look again later.
    /// </summary>
    Wait,

    /// <summary>
    /// Slide the window to its edge.
    /// </summary>
    Hide,
}

/// <summary>
/// The dock state of the main window: which edge it rests at and whether it is hidden there. It holds no timers and
/// touches no window, so the transitions can be tested on their own.
/// </summary>
internal sealed class EdgeDockModel
{
    /// <summary>
    /// Gets the edge the window is docked to.
    /// </summary>
    public DockEdge Edge { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the window rests, or is on its way to rest, in its hidden position.
    /// </summary>
    public bool Hidden { get; private set; }

    /// <summary>
    /// Gets the work area the window was docked in. It is remembered because a hidden window has only a thin strip on
    /// the screen, which the system may not attribute to a screen.
    /// </summary>
    public PixelRect Area { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the window is docked to an edge.
    /// </summary>
    public bool IsDocked => Edge != DockEdge.None;

    /// <summary>
    /// Gets a value indicating whether the pointer leaving the window should start the wait to hide it.
    /// </summary>
    public bool ShouldWaitToHide => IsDocked && !Hidden;

    /// <summary>
    /// Takes in where the window came to rest and decides whether it is docked there. A hidden window does not change.
    /// </summary>
    /// <param name="window">
    /// The window bounds in screen pixels.
    /// </param>
    /// <param name="area">
    /// The work area of the screen the window is on.
    /// </param>
    /// <param name="otherScreens">
    /// The bounds of every other screen.
    /// </param>
    /// <param name="snap">
    /// The distance to an edge that still counts, in screen pixels.
    /// </param>
    /// <param name="sliver">
    /// The width of the strip that stays visible when hidden, in screen pixels.
    /// </param>
    /// <returns>
    /// The position to move the window to when it is docked now, or <see langword="null"/> when it is not docked.
    /// </returns>
    public PixelPoint? Settle(PixelRect window, PixelRect area, IEnumerable<PixelRect> otherScreens, int snap, int sliver)
    {
        if (Hidden)
        {
            return null;
        }

        var edge = EdgeDockRules.DetectEdge(window, area, snap);
        if (edge != DockEdge.None)
        {
            var hidden = new PixelRect(EdgeDockRules.HiddenPosition(window, area, edge, sliver), window.Size);
            if (!EdgeDockRules.StaysOffOtherScreens(hidden, otherScreens))
            {
                edge = DockEdge.None;
            }
        }

        Edge = edge;
        Area = area;
        return edge == DockEdge.None ? null : EdgeDockRules.ShownPosition(window, area, edge);
    }

    /// <summary>
    /// Gets the position the window rests at when shown.
    /// </summary>
    /// <param name="window">
    /// The window bounds in screen pixels.
    /// </param>
    /// <returns>
    /// The top-left position of the window.
    /// </returns>
    public PixelPoint ShownPosition(PixelRect window) => EdgeDockRules.ShownPosition(window, Area, Edge);

    /// <summary>
    /// Gets the position the window rests at when hidden.
    /// </summary>
    /// <param name="window">
    /// The window bounds in screen pixels.
    /// </param>
    /// <param name="sliver">
    /// The width of the strip that stays visible, in screen pixels.
    /// </param>
    /// <returns>
    /// The top-left position of the window.
    /// </returns>
    public PixelPoint HiddenPosition(PixelRect window, int sliver) => EdgeDockRules.HiddenPosition(window, Area, Edge, sliver);

    /// <summary>
    /// Records that the window is, or is on its way to be, hidden or shown. Only a docked window can be hidden.
    /// </summary>
    /// <param name="hidden">
    /// <see langword="true"/> for hidden.
    /// </param>
    public void SetHidden(bool hidden) => Hidden = hidden && IsDocked;

    /// <summary>
    /// Decides what to do after the pointer stayed away from the window.
    /// </summary>
    /// <param name="pointerOver">
    /// Whether the pointer is on the window.
    /// </param>
    /// <param name="canHide">
    /// Whether nothing needs the window to stay out.
    /// </param>
    /// <returns>
    /// The decision.
    /// </returns>
    public DockLeave Leave(bool pointerOver, bool canHide)
    {
        if (!ShouldWaitToHide || pointerOver)
        {
            return DockLeave.Stop;
        }

        return canHide ? DockLeave.Hide : DockLeave.Wait;
    }

    /// <summary>
    /// Forgets the dock, so the window is a plain window again.
    /// </summary>
    public void Reset()
    {
        Edge = DockEdge.None;
        Hidden = false;
    }
}
