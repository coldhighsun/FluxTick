using Avalonia;

namespace FluxTick.App.Views;

/// <summary>
/// Decides where a tool window opens relative to the main window.
/// </summary>
public static class ToolWindowPlacement
{
    /// <summary>
    /// The distance between the main window and the tool window in screen pixels.
    /// </summary>
    private const int Gap = 8;

    /// <summary>
    /// Gets the height a tool window is placed with before it is shown.
    /// </summary>
    /// <param name="height">
    /// The height of the window, or <see cref="double.NaN"/> for a window that is as tall as its content.
    /// </param>
    /// <param name="desired">
    /// The height the content wants.
    /// </param>
    /// <param name="max">
    /// The tallest the window may be.
    /// </param>
    /// <returns>
    /// The height to place the window with.
    /// </returns>
    public static double PlacementHeight(double height, double desired, double max) => Math.Min(double.IsNaN(height) ? desired : height, max);

    /// <summary>
    /// Places a tool window beside the owner, preferring the left side, and keeps it inside the work area.
    /// </summary>
    /// <param name="owner">
    /// The main window bounds in screen pixels.
    /// </param>
    /// <param name="size">
    /// The tool window size in screen pixels.
    /// </param>
    /// <param name="workArea">
    /// The usable screen area in screen pixels.
    /// </param>
    /// <returns>
    /// The top-left position of the tool window.
    /// </returns>
    public static PixelPoint Place(PixelRect owner, PixelSize size, PixelRect workArea)
    {
        var left = owner.X - Gap - size.Width;
        var right = owner.Right + Gap;

        int x;
        if (left >= workArea.X)
        {
            x = left;
        }
        else if (right + size.Width <= workArea.Right)
        {
            x = right;
        }
        else
        {
            x = left;
        }

        return new PixelPoint(Clamp(x, workArea.X, workArea.Right - size.Width), Clamp(owner.Y, workArea.Y, workArea.Bottom - size.Height));
    }

    /// <summary>
    /// Places a tool window in the middle of an area, for example the screen of a full screen main window, beside
    /// which there is no room. The window stays inside the area as far as it fits.
    /// </summary>
    /// <param name="size">
    /// The tool window size in screen pixels.
    /// </param>
    /// <param name="area">
    /// The area to center in, in screen pixels.
    /// </param>
    /// <returns>
    /// The top-left position of the tool window.
    /// </returns>
    public static PixelPoint Center(PixelSize size, PixelRect area) =>
        new(
            Clamp(area.X + (area.Width - size.Width) / 2, area.X, area.Right - size.Width),
            Clamp(area.Y + (area.Height - size.Height) / 2, area.Y, area.Bottom - size.Height));

    /// <summary>
    /// Clamps a value into a range, favoring the lower bound when the range is empty.
    /// </summary>
    /// <param name="value">
    /// The value.
    /// </param>
    /// <param name="min">
    /// The lower bound.
    /// </param>
    /// <param name="max">
    /// The upper bound.
    /// </param>
    /// <returns>
    /// The clamped value.
    /// </returns>
    internal static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(value, max));
}
