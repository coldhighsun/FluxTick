using Avalonia;

namespace FluxTick.App.Views;

/// <summary>
/// The screen edge a window is docked to.
/// </summary>
internal enum DockEdge
{
    /// <summary>
    /// The window is not docked.
    /// </summary>
    None,

    /// <summary>
    /// The left edge of the work area.
    /// </summary>
    Left,

    /// <summary>
    /// The right edge of the work area.
    /// </summary>
    Right,

    /// <summary>
    /// The top edge of the work area. The bottom edge is not used, because that is where the taskbar usually sits.
    /// </summary>
    Top,
}

/// <summary>
/// Pure rules for hiding the main window at a screen edge: when it counts as docked and where it rests hidden and shown.
/// </summary>
internal static class EdgeDockRules
{
    /// <summary>
    /// How close to an edge the window has to be to count as docked, in device independent pixels.
    /// </summary>
    public const double SnapDistance = 8;

    /// <summary>
    /// How much of the window stays visible while it is hidden, in device independent pixels. The pointer reaching this
    /// strip is what slides the window out again.
    /// </summary>
    public const double Sliver = 4;

    /// <summary>
    /// Gets a value indicating whether the platform lets a window be placed beyond the screen. A Wayland compositor
    /// decides where a window goes, so edge hiding does not apply there.
    /// </summary>
    public static bool IsPlatformSupported { get; } =
        !(OperatingSystem.IsLinux()
            && string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// How long the window takes to slide in or out.
    /// </summary>
    public static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Gets the edge a window is docked to.
    /// </summary>
    /// <param name="window">
    /// The window bounds in screen pixels.
    /// </param>
    /// <param name="area">
    /// The work area of the screen the window is on, in screen pixels.
    /// </param>
    /// <param name="snap">
    /// The distance to the edge that still counts, in screen pixels.
    /// </param>
    /// <returns>
    /// The edge; the top wins over the sides and the left over the right when the window touches several. A window that
    /// does not overlap the work area is not docked.
    /// </returns>
    public static DockEdge DetectEdge(PixelRect window, PixelRect area, int snap)
    {
        if (!window.Intersects(area))
        {
            return DockEdge.None;
        }

        if (window.Y <= area.Y + snap)
        {
            return DockEdge.Top;
        }

        if (window.X <= area.X + snap)
        {
            return DockEdge.Left;
        }

        return window.Right >= area.Right - snap ? DockEdge.Right : DockEdge.None;
    }

    /// <summary>
    /// Gets where the window rests when it is shown: flush with its edge and inside the work area on the other axis.
    /// </summary>
    /// <param name="window">
    /// The window bounds in screen pixels.
    /// </param>
    /// <param name="area">
    /// The work area in screen pixels.
    /// </param>
    /// <param name="edge">
    /// The edge the window is docked to.
    /// </param>
    /// <returns>
    /// The top-left position of the window.
    /// </returns>
    public static PixelPoint ShownPosition(PixelRect window, PixelRect area, DockEdge edge) => edge switch
    {
        DockEdge.Left => new PixelPoint(area.X, ToolWindowPlacement.Clamp(window.Y, area.Y, area.Bottom - window.Height)),
        DockEdge.Right => new PixelPoint(area.Right - window.Width, ToolWindowPlacement.Clamp(window.Y, area.Y, area.Bottom - window.Height)),
        DockEdge.Top => new PixelPoint(ToolWindowPlacement.Clamp(window.X, area.X, area.Right - window.Width), area.Y),
        _ => window.Position,
    };

    /// <summary>
    /// Gets where the window rests when it is hidden: pushed beyond its edge so that only the sliver is left.
    /// </summary>
    /// <param name="window">
    /// The window bounds in screen pixels.
    /// </param>
    /// <param name="area">
    /// The work area in screen pixels.
    /// </param>
    /// <param name="edge">
    /// The edge the window is docked to.
    /// </param>
    /// <param name="sliver">
    /// The width of the strip that stays visible, in screen pixels.
    /// </param>
    /// <returns>
    /// The top-left position of the window.
    /// </returns>
    public static PixelPoint HiddenPosition(PixelRect window, PixelRect area, DockEdge edge, int sliver)
    {
        var shown = ShownPosition(window, area, edge);
        return edge switch
        {
            DockEdge.Left => new PixelPoint(area.X - window.Width + sliver, shown.Y),
            DockEdge.Right => new PixelPoint(area.Right - sliver, shown.Y),
            DockEdge.Top => new PixelPoint(shown.X, area.Y - window.Height + sliver),
            _ => window.Position,
        };
    }

    /// <summary>
    /// Determines whether a window pushed beyond an edge stays off the other screens. Next to another screen the hidden
    /// window would show up there, so the edge is not used for hiding.
    /// </summary>
    /// <param name="hidden">
    /// The bounds of the window in its hidden position, in screen pixels.
    /// </param>
    /// <param name="otherScreens">
    /// The bounds of every screen except the one the window is on.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the hidden window overlaps none of the other screens.
    /// </returns>
    public static bool StaysOffOtherScreens(PixelRect hidden, IEnumerable<PixelRect> otherScreens) =>
        !otherScreens.Any(screen => screen.Intersects(hidden));

}
