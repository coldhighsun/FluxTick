using Avalonia;
using FluxTick.App.Services;

namespace FluxTick.App.Views;

/// <summary>
/// Decides how the screen-filling view of one instrument is dragged to another monitor: when a press becomes a drag, how
/// large the window is while it is carried, and where it appears under the pointer.
/// </summary>
internal static class AssetFocusDragRules
{
    /// <summary>
    /// Gets how far the pointer has to move, in device independent pixels, before a press counts as a drag.
    /// </summary>
    public const double DragThreshold = 6;

    /// <summary>
    /// Gets the share of the screen the window takes while it is carried.
    /// </summary>
    public const double CarriedShare = 0.6;

    /// <summary>
    /// Gets the smallest width of the window while it is carried, in device independent pixels.
    /// </summary>
    public const double MinCarriedWidth = 640;

    /// <summary>
    /// Gets the smallest height of the window while it is carried, in device independent pixels.
    /// </summary>
    public const double MinCarriedHeight = AssetFocusRules.MinHeight;

    /// <summary>
    /// Tells whether the pointer has moved far enough from where it was pressed to start a drag.
    /// </summary>
    /// <param name="start">Where the pointer was pressed.</param>
    /// <param name="current">Where the pointer is now.</param>
    /// <returns>
    /// <see langword="true"/> when the distance is at least <see cref="DragThreshold"/>.
    /// </returns>
    public static bool ExceedsThreshold(Point start, Point current)
    {
        var dx = current.X - start.X;
        var dy = current.Y - start.Y;
        return (dx * dx) + (dy * dy) >= DragThreshold * DragThreshold;
    }

    /// <summary>
    /// Gets the size of the window while it is carried: a share of the screen, never smaller than a floor and never larger
    /// than the screen.
    /// </summary>
    /// <param name="screenBounds">The bounds of the screen, in pixels.</param>
    /// <param name="scaling">The scaling of the screen.</param>
    /// <returns>The size, in device independent pixels.</returns>
    public static Size CarriedSize(PixelRect screenBounds, double scaling)
    {
        var perPixel = ScreenUnits.PerPixel(scaling);
        var screenWidth = screenBounds.Width / perPixel;
        var screenHeight = screenBounds.Height / perPixel;
        var width = Math.Min(screenWidth, Math.Max(MinCarriedWidth, screenWidth * CarriedShare));
        var height = Math.Min(screenHeight, Math.Max(MinCarriedHeight, screenHeight * CarriedShare));
        return new Size(width, height);
    }

    /// <summary>
    /// Gets where the top-left corner of the carried window goes, so that the pointer stays at the same relative spot of
    /// the window as it had on the full-screen one.
    /// </summary>
    /// <param name="pointer">The pointer, in screen pixels.</param>
    /// <param name="screenBounds">The bounds of the screen the full-screen window filled, in pixels.</param>
    /// <param name="carriedSize">The size of the carried window, in device independent pixels.</param>
    /// <param name="scaling">The scaling of the screen.</param>
    /// <returns>The position, in screen pixels.</returns>
    public static PixelPoint CarriedOrigin(PixelPoint pointer, PixelRect screenBounds, Size carriedSize, double scaling)
    {
        if (screenBounds.Width <= 0 || screenBounds.Height <= 0)
        {
            return pointer;
        }

        var perPixel = ScreenUnits.PerPixel(scaling);
        var shareX = Math.Clamp((pointer.X - screenBounds.X) / (double)screenBounds.Width, 0, 1);
        var shareY = Math.Clamp((pointer.Y - screenBounds.Y) / (double)screenBounds.Height, 0, 1);
        var x = pointer.X - (shareX * carriedSize.Width * perPixel);
        var y = pointer.Y - (shareY * carriedSize.Height * perPixel);
        return new PixelPoint((int)Math.Round(x), (int)Math.Round(y));
    }
}
