using Avalonia;

namespace FluxTick.App.Views;

/// <summary>
/// Decides where a tool window opens.
/// </summary>
public static class ToolWindowPlacement
{
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
    /// Places a tool window in the middle of an area, for example the work area of the screen of the main window. The window
    /// stays inside the area as far as it fits.
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
    /// Places a tool window in the middle of the work area of a screen.
    /// </summary>
    /// <param name="size">
    /// The tool window size in device independent pixels.
    /// </param>
    /// <param name="scaling">
    /// The scale of the screen: screen pixels per device independent pixel.
    /// </param>
    /// <param name="workArea">
    /// The usable area of the screen in screen pixels.
    /// </param>
    /// <returns>
    /// The top-left position of the tool window.
    /// </returns>
    public static PixelPoint CenterOnScreen(Size size, double scaling, PixelRect workArea) =>
        Center(PixelSize.FromSize(size, scaling), workArea);

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
