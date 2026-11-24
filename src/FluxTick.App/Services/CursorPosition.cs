using System.Runtime.InteropServices;
using Avalonia;

namespace FluxTick.App.Services;

/// <summary>
/// Reads where the mouse cursor is on the desktop, independent of any window. A position computed from the pointer event
/// and the window moves with the window, so a window that follows the cursor flickers; the desktop position does not.
/// </summary>
internal static class CursorPosition
{
    /// <summary>
    /// Gets the position of the cursor in screen pixels.
    /// </summary>
    /// <param name="position">The position, when it could be read.</param>
    /// <returns>
    /// <see langword="true"/> when the platform gave the position; <see langword="false"/> elsewhere, where the caller has to
    /// work it out from the pointer event.
    /// </returns>
    public static bool TryGet(out PixelPoint position)
    {
        position = default;
        if (!OperatingSystem.IsWindows() || !GetCursorPos(out var point))
        {
            return false;
        }

        position = new PixelPoint(point.X, point.Y);
        return true;
    }

    /// <summary>
    /// Reads the cursor position from Windows.
    /// </summary>
    /// <param name="point">The position, in screen pixels.</param>
    /// <returns>
    /// <see langword="true"/> on success.
    /// </returns>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    /// <summary>
    /// A point of the Windows API.
    /// </summary>
    /// <param name="X">The horizontal coordinate.</param>
    /// <param name="Y">The vertical coordinate.</param>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);
}
