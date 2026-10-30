using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace FluxTick.App.Services;

/// <summary>
/// Asks macOS about the pointer, which is on the menu bar icon at the moment the icon is clicked.
/// </summary>
public static class MacTrayCursor
{
    /// <summary>
    /// The Objective-C runtime library.
    /// </summary>
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

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
                return (SendUInt(objc_getClass("NSEvent"), sel_registerName("pressedMouseButtons")) & LeftOrRightButtons) != 0;
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
    /// The screens, whose primary one fixes the scale and the origin the position is converted to.
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
            var point = SendPoint(objc_getClass("NSEvent"), sel_registerName("mouseLocation"));
            position = ToScreenPixels(point.X, point.Y, primary.Bounds.Height / primary.Scaling, primary.Scaling);
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

    /// <summary>
    /// Gets the Objective-C class with the given name.
    /// </summary>
    /// <param name="name">
    /// The class name.
    /// </param>
    /// <returns>
    /// The class, or zero.
    /// </returns>
    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);

    /// <summary>
    /// Gets the Objective-C selector with the given name.
    /// </summary>
    /// <param name="name">
    /// The selector name.
    /// </param>
    /// <returns>
    /// The selector.
    /// </returns>
    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);

    /// <summary>
    /// Sends a message that answers a point. A point is two doubles, which both the arm64 and the x86-64 conventions
    /// return in registers, so the plain send is right.
    /// </summary>
    /// <param name="receiver">
    /// The receiver.
    /// </param>
    /// <param name="selector">
    /// The selector.
    /// </param>
    /// <returns>
    /// The point.
    /// </returns>
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern CGPoint SendPoint(IntPtr receiver, IntPtr selector);

    /// <summary>
    /// Sends a message that answers an unsigned integer.
    /// </summary>
    /// <param name="receiver">
    /// The receiver.
    /// </param>
    /// <param name="selector">
    /// The selector.
    /// </param>
    /// <returns>
    /// The integer.
    /// </returns>
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern ulong SendUInt(IntPtr receiver, IntPtr selector);

    /// <summary>
    /// A point of Cocoa.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        /// <summary>
        /// The horizontal position.
        /// </summary>
        public double X;

        /// <summary>
        /// The vertical position.
        /// </summary>
        public double Y;
    }
}
