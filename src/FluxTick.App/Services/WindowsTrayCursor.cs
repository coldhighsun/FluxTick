using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;

namespace FluxTick.App.Services;

/// <summary>
/// Asks Windows about the pointer, which is on the tray icon at the moment the icon is clicked.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsTrayCursor
{
    /// <summary>
    /// The virtual key code of the left mouse button.
    /// </summary>
    private const int LeftButton = 0x01;

    /// <summary>
    /// The virtual key code of the right mouse button.
    /// </summary>
    private const int RightButton = 0x02;

    /// <summary>
    /// Gets the longest time between two clicks that still make a double-click, as the user set it in Windows.
    /// </summary>
    public static TimeSpan DoubleClickTime => TimeSpan.FromMilliseconds(GetDoubleClickTime());

    /// <summary>
    /// Gets the position of the pointer.
    /// </summary>
    /// <param name="position">
    /// The position in screen pixels.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the position is known.
    /// </returns>
    public static bool TryGetPosition(out PixelPoint position)
    {
        if (GetCursorPos(out var point))
        {
            position = new PixelPoint(point.X, point.Y);
            return true;
        }

        position = default;
        return false;
    }

    /// <summary>
    /// Gets a value indicating whether the left or the right mouse button is held down right now, in whichever window.
    /// </summary>
    public static bool IsButtonDown => ((GetAsyncKeyState(LeftButton) | GetAsyncKeyState(RightButton)) & 0x8000) != 0;

    /// <summary>
    /// Gets the state of a key or mouse button.
    /// </summary>
    /// <param name="key">
    /// The virtual key code.
    /// </param>
    /// <returns>
    /// The state; the high bit is set while the key is down.
    /// </returns>
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    /// <summary>
    /// Gets the double-click time of the system.
    /// </summary>
    /// <returns>
    /// The time in milliseconds.
    /// </returns>
    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    /// <summary>
    /// Gets the position of the pointer.
    /// </summary>
    /// <param name="point">
    /// The position in screen pixels.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when it succeeded.
    /// </returns>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    /// <summary>
    /// The Win32 POINT.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        /// <summary>
        /// The horizontal position.
        /// </summary>
        public int X;

        /// <summary>
        /// The vertical position.
        /// </summary>
        public int Y;
    }
}
