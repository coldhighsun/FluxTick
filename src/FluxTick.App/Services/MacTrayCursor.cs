using Avalonia;
using Avalonia.Controls;
using static FluxTick.App.Services.ObjC;

namespace FluxTick.App.Services;

/// <summary>
/// Asks macOS about the pointer, which is on the menu bar icon at the moment the icon is clicked.
/// </summary>
public static class MacTrayCursor
{
    /// <summary>
    /// The bits of <c>NSEvent.pressedMouseButtons</c> for the left and the right mouse button.
    /// </summary>
    private const ulong LeftOrRightButtons = 0b11;

    /// <summary>
    /// Gets a value indicating whether the left or the right mouse button is held down right now, in whichever window.
    /// </summary>
    public static bool IsButtonDown
    {
        get
        {
            try
            {
                return (SendUInt(GetClass("NSEvent"), Sel("pressedMouseButtons")) & LeftOrRightButtons) != 0;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Gets the position of the pointer in screen pixels.
    /// </summary>
    /// <param name="screens">
    /// The screens, whose primary one fixes the origin the position is converted to. macOS reports them in points.
    /// </param>
    /// <param name="position">
    /// The position in screen pixels.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the position is known.
    /// </returns>
    public static bool TryGetPosition(Screens? screens, out PixelPoint position)
    {
        position = default;
        if (screens?.Primary is not { } primary)
        {
            return false;
        }

        try
        {
            var point = SendPoint(GetClass("NSEvent"), Sel("mouseLocation"));
            position = ToScreenPixels(point.X, point.Y, primary.Bounds.Height, 1);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the position of the pointer in points, measured from the top left corner of the primary screen. Points are what
    /// the size of a window is measured in, so no scale is applied.
    /// </summary>
    /// <param name="screens">
    /// The screens, whose primary one fixes the origin.
    /// </param>
    /// <param name="x">
    /// The horizontal position in points.
    /// </param>
    /// <param name="y">
    /// The vertical position in points, counted from the top.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the position is known.
    /// </returns>
    public static bool TryGetPoints(Screens? screens, out double x, out double y)
    {
        x = 0;
        y = 0;
        if (screens?.Primary is not { } primary)
        {
            return false;
        }

        try
        {
            var point = SendPoint(GetClass("NSEvent"), Sel("mouseLocation"));
            x = point.X;
            y = primary.Bounds.Height - point.Y;
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Converts a position of Cocoa to screen pixels. Cocoa measures in points from the bottom left corner of the primary
    /// screen with the y axis pointing up; screen pixels start at the top left corner.
    /// </summary>
    /// <param name="x">
    /// The horizontal position in points.
    /// </param>
    /// <param name="y">
    /// The vertical position in points, counted from the bottom.
    /// </param>
    /// <param name="primaryHeight">
    /// The height of the primary screen in points.
    /// </param>
    /// <param name="scaling">
    /// The number of pixels in a point.
    /// </param>
    /// <returns>
    /// The position in screen pixels.
    /// </returns>
    public static PixelPoint ToScreenPixels(double x, double y, double primaryHeight, double scaling) =>
        new((int)Math.Round(x * scaling), (int)Math.Round((primaryHeight - y) * scaling));
}
