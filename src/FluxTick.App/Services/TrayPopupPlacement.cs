using Avalonia;
using FluxTick.App.Views;

namespace FluxTick.App.Services;

/// <summary>
/// Decides where the tray popup opens relative to the tray icon.
/// </summary>
public static class TrayPopupPlacement
{
    /// <summary>
    /// The distance between the tray icon and the popup in screen pixels.
    /// </summary>
    private const int Gap = 8;

    /// <summary>
    /// Places the popup next to the tray icon, on the side facing the work area, and keeps it inside the work area.
    /// The taskbar is the part of the screen outside the work area, so the edge the icon lies beyond tells where it is.
    /// </summary>
    /// <param name="icon">
    /// The tray icon bounds in screen pixels.
    /// </param>
    /// <param name="size">
    /// The popup size in screen pixels.
    /// </param>
    /// <param name="workArea">
    /// The usable screen area in screen pixels.
    /// </param>
    /// <returns>
    /// The top-left position of the popup.
    /// </returns>
    public static PixelPoint Place(PixelRect icon, PixelSize size, PixelRect workArea)
    {
        int x;
        int y;
        if (icon.X >= workArea.Right)
        {
            x = icon.X - Gap - size.Width;
            y = icon.Center.Y - (size.Height / 2);
        }
        else if (icon.Right <= workArea.X)
        {
            x = icon.Right + Gap;
            y = icon.Center.Y - (size.Height / 2);
        }
        else
        {
            x = icon.Center.X - (size.Width / 2);
            y = icon.Y >= workArea.Bottom || icon.Center.Y > workArea.Center.Y
                ? icon.Y - Gap - size.Height
                : icon.Bottom + Gap;
        }

        return new PixelPoint(
            ToolWindowPlacement.Clamp(x, workArea.X, workArea.Right - size.Width),
            ToolWindowPlacement.Clamp(y, workArea.Y, workArea.Bottom - size.Height));
    }
}
