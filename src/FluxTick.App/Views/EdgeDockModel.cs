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
/// What a click on the tray icon does.
/// </summary>
internal enum TrayToggle
{
    /// <summary>
    /// Show the window that was hidden to the tray.
    /// </summary>
    Show,

    /// <summary>
    /// Hide the window to the tray.
    /// </summary>
    Hide,

    /// <summary>
    /// Slide the docked window out of its edge.
    /// </summary>
    SlideOut,

    /// <summary>
    /// Slide the docked window back to its edge.
    /// </summary>
    SlideIn,
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
    /// Gets a value indicating whether edge hiding is on: the setting is on, the window stays above others and the
    /// platform can place a window beyond the screen.
    /// </summary>
    public bool Enabled { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the platform keeps a window where it is sent. It stops being the case after the
    /// window was not kept beyond the screen twice in a row, and then edge hiding stays off.
    /// </summary>
    public bool Supported { get; private set; } = true;

    /// <summary>
    /// The number of times in a row the platform did not keep the window beyond the screen.
    /// </summary>
    private int _placementFailures;

    /// <summary>
    /// Gets a value indicating whether the window moves by itself, so that the changes of position it causes are not
    /// taken for the user's.
    /// </summary>
    public bool Sliding { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the user is dragging the window by its header.
    /// </summary>
    public bool Moving { get; private set; }

    /// <summary>
    /// Gets the position the window was last sent to. A move that ends there is the window's own, even when the
    /// system reports it after the slide is over.
    /// </summary>
    public PixelPoint? Expected { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a docking decision may be taken now.
    /// </summary>
    public bool CanSettle => Enabled && !Sliding && !Moving;

    /// <summary>
    /// Gets a value indicating whether the geometry should not be saved: a window that slides or rests beyond its edge
    /// would be saved off the screen, and the position it is shown at is the one that is remembered.
    /// </summary>
    public bool SuppressesGeometry => Sliding || Hidden;

    /// <summary>
    /// Gets a value indicating whether the window is docked to an edge.
    /// </summary>
    public bool IsDocked => Edge != DockEdge.None;

    /// <summary>
    /// Gets a value indicating whether the pointer leaving the window should start the wait to hide it.
    /// </summary>
    public bool ShouldWaitToHide => IsDocked && !Hidden;

    /// <summary>
    /// Turns edge hiding on or off. It stays off once the platform turned out not to support it.
    /// </summary>
    /// <param name="on">
    /// Whether the settings ask for edge hiding.
    /// </param>
    public void SetEnabled(bool on) => Enabled = on && Supported;

    /// <summary>
    /// Records that the user started or stopped dragging the window by its header.
    /// </summary>
    /// <param name="moving">
    /// <see langword="true"/> while the drag lasts.
    /// </param>
    public void SetMoving(bool moving) => Moving = moving;

    /// <summary>
    /// Records that the window starts moving by itself to a position.
    /// </summary>
    /// <param name="target">
    /// The position it is sent to.
    /// </param>
    public void BeginSlide(PixelPoint target)
    {
        Sliding = true;
        Expected = target;
    }

    /// <summary>
    /// Records that the window stopped moving by itself.
    /// </summary>
    public void EndSlide() => Sliding = false;

    /// <summary>
    /// Determines whether a change of position is the user's, so that the window has to settle again.
    /// </summary>
    /// <param name="position">
    /// The position the window has now.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when edge hiding is on and the window did not move by itself.
    /// </returns>
    public bool IsUserMove(PixelPoint position) => Enabled && !Sliding && position != Expected;

    /// <summary>
    /// Records whether the platform kept the window beyond the screen where it was sent. A window that was not kept
    /// there is in plain sight, so the dock ends; a window that was not kept twice in a row turns edge hiding off,
    /// because one miss can be a slow window manager.
    /// </summary>
    /// <param name="honored">
    /// <see langword="true"/> when the window was where it was sent.
    /// </param>
    public void RecordPlacement(bool honored)
    {
        if (honored)
        {
            _placementFailures = 0;
            return;
        }

        Reset();
        if (++_placementFailures >= 2)
        {
            Supported = false;
            Enabled = false;
        }
    }

    /// <summary>
    /// Decides what a click on the tray icon does.
    /// </summary>
    /// <param name="visible">
    /// Whether the window is shown, as opposed to hidden to the tray.
    /// </param>
    /// <returns>
    /// A window that is docked slides out of and back to its edge; any other window is shown and hidden.
    /// </returns>
    public TrayToggle ToggleFromTray(bool visible)
    {
        if (!visible)
        {
            return TrayToggle.Show;
        }

        if (!IsDocked)
        {
            return TrayToggle.Hide;
        }

        return Hidden ? TrayToggle.SlideOut : TrayToggle.SlideIn;
    }

    /// <summary>
    /// Decides whether the wait to hide the window starts again after something that kept it open went away.
    /// </summary>
    /// <param name="visible">
    /// Whether the window is shown.
    /// </param>
    /// <param name="pointerOver">
    /// Whether the pointer is on the window.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for a shown, docked window that the pointer is not on.
    /// </returns>
    public bool ShouldResumeLeave(bool visible, bool pointerOver) => ShouldWaitToHide && visible && !pointerOver;

    /// <summary>
    /// Keeps the remembered work area in step with the screens that exist now.
    /// </summary>
    /// <param name="workAreas">
    /// The work areas of all screens.
    /// </param>
    /// <returns>
    /// <see langword="false"/> when the screen the window was docked to is gone and the dock cannot be kept; otherwise
    /// <see langword="true"/>, with the area replaced by the one that took its place when it changed, for example
    /// because the taskbar moved.
    /// </returns>
    public bool Rebase(IReadOnlyList<PixelRect> workAreas)
    {
        if (workAreas.Count == 0 || workAreas.Contains(Area))
        {
            return true;
        }

        PixelRect? best = null;
        long bestShare = 0;
        foreach (var candidate in workAreas)
        {
            var overlap = candidate.Intersect(Area);
            var share = (long)overlap.Width * overlap.Height;
            if (share > bestShare)
            {
                best = candidate;
                bestShare = share;
            }
        }

        if (best is null)
        {
            return false;
        }

        Area = best.Value;
        return true;
    }

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
