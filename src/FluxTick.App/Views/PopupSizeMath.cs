using Avalonia;

namespace FluxTick.App.Views;

/// <summary>
/// Works out where a menu window goes when it has to grow because its content turned out taller than the window.
/// </summary>
public static class PopupSizeMath
{
    /// <summary>
    /// How far the bottom of a window may be from the anchor, in screen pixels, and still count as hanging from it.
    /// </summary>
    private const int AnchorTolerance = 2;

    /// <summary>
    /// Tells whether a pop-up can have been sized for the wrong scaling. Pop-up windows are created on the primary screen, so
    /// only one that opens on a screen with another scaling can.
    /// </summary>
    /// <param name="screenScaling">
    /// The scaling of the screen the pop-up opened on.
    /// </param>
    /// <param name="primaryScaling">
    /// The scaling of the primary screen.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the two differ.
    /// </returns>
    public static bool MaySizedWrong(double screenScaling, double primaryScaling) => Math.Abs(screenScaling - primaryScaling) > 0.01;

    /// <summary>
    /// Makes a window taller. A window whose bottom edge sits on the anchor, which is how a menu that did not fit below the
    /// pointer is placed, grows upward so it keeps its place at the anchor; any other window grows downward. The result is
    /// kept inside the work area, and never taller than it.
    /// </summary>
    /// <param name="window">
    /// The window in screen pixels.
    /// </param>
    /// <param name="extra">
    /// How many pixels taller the window has to be.
    /// </param>
    /// <param name="anchorY">
    /// The vertical screen position the menu was opened at, or <see langword="null"/> when it is not known.
    /// </param>
    /// <param name="workArea">
    /// The usable area of the screen in screen pixels.
    /// </param>
    /// <returns>
    /// The new bounds of the window.
    /// </returns>
    public static PixelRect Grow(PixelRect window, int extra, int? anchorY, PixelRect workArea)
    {
        var height = Math.Min(window.Height + extra, workArea.Height);
        var top = anchorY is { } anchor && Math.Abs(window.Bottom - anchor) <= AnchorTolerance ? window.Bottom - height : window.Y;
        top = Math.Max(workArea.Y, Math.Min(top, workArea.Bottom - height));
        return new PixelRect(window.X, top, window.Width, height);
    }
}
