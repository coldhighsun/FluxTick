using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using FluxTick.App.Services;

namespace FluxTick.App.Views;

/// <summary>
/// Makes the menus and other pop-ups that open as windows tall enough after the main window moved to a screen with another
/// scaling. A pop-up is sized for the scaling its window had when it was created, which is that of the screen the window
/// was created on, and Avalonia does not size it again once the window is on the screen the pop-up opens on, so its last
/// entries are cut off behind a scroll arrow.
/// </summary>
public static class PopupSizeFix
{
    /// <summary>
    /// The Win32 flag that keeps the order of the windows.
    /// </summary>
    private const uint NoZOrder = 0x4;

    /// <summary>
    /// The Win32 flag that keeps the window from taking the focus.
    /// </summary>
    private const uint NoActivate = 0x10;

    /// <summary>
    /// Watches every pop-up window of the application.
    /// </summary>
    public static void Register()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Control.LoadedEvent.AddClassHandler<PopupRoot>((root, _) =>
        {
            if (OperatingSystem.IsWindows())
            {
                Fit(root);
            }
        });
    }

    /// <summary>
    /// Makes the window of a pop-up as tall as its content wants to be.
    /// </summary>
    /// <param name="root">
    /// The pop-up window.
    /// </param>
    [SupportedOSPlatform("windows")]
    private static void Fit(PopupRoot root)
    {
        if (root.TryGetPlatformHandle()?.Handle is not { } handle || handle == 0)
        {
            return;
        }

        if (!GetWindowRect(handle, out var rect))
        {
            return;
        }

        // A pop-up is created on the primary screen, so only one that opens on a screen with another scaling can have the
        // wrong size. The common case costs nothing more than this look.
        var window = new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        var screen = root.Screens.ScreenFromPoint(window.Position);
        if (screen is not null && !PopupSizeMath.MaySizedWrong(screen.Scaling, root.Screens.Primary?.Scaling ?? screen.Scaling))
        {
            return;
        }

        // The window is measured as if it had all the room it wants, which is the height its content needs.
        root.Measure(new Size(root.ClientSize.Width, double.PositiveInfinity));
        var wanted = root.DesiredSize.Height;
        root.InvalidateMeasure();
        if (wanted <= root.ClientSize.Height + 0.5)
        {
            return;
        }

        var extra = (int)Math.Ceiling((wanted - root.ClientSize.Height) * root.RenderScaling);
        int? anchorY = WindowsTrayCursor.TryGetPosition(out var cursor) ? cursor.Y : null;
        var grown = screen is null ? window.WithHeight(window.Height + extra) : PopupSizeMath.Grow(window, extra, anchorY, screen.WorkingArea);
        SetWindowPos(handle, 0, grown.X, grown.Y, grown.Width, grown.Height, NoZOrder | NoActivate);
    }

    /// <summary>
    /// Gets the bounds of a window.
    /// </summary>
    /// <param name="window">
    /// The window handle.
    /// </param>
    /// <param name="rect">
    /// The bounds in screen pixels.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when it succeeded.
    /// </returns>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    /// <summary>
    /// Moves and sizes a window.
    /// </summary>
    /// <param name="window">
    /// The window handle.
    /// </param>
    /// <param name="after">
    /// The window to place it after, when the order is changed.
    /// </param>
    /// <param name="x">
    /// The left edge in screen pixels.
    /// </param>
    /// <param name="y">
    /// The top edge in screen pixels.
    /// </param>
    /// <param name="width">
    /// The width in pixels.
    /// </param>
    /// <param name="height">
    /// The height in pixels.
    /// </param>
    /// <param name="flags">
    /// The flags of the call.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when it succeeded.
    /// </returns>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    /// <summary>
    /// The Win32 RECT.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        /// <summary>
        /// The left edge.
        /// </summary>
        public int Left;

        /// <summary>
        /// The top edge.
        /// </summary>
        public int Top;

        /// <summary>
        /// The right edge.
        /// </summary>
        public int Right;

        /// <summary>
        /// The bottom edge.
        /// </summary>
        public int Bottom;
    }
}
