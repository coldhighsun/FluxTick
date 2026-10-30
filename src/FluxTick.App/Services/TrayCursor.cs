using Avalonia;
using Avalonia.Controls;

namespace FluxTick.App.Services;

/// <summary>
/// Asks the operating system about the pointer, which is on the tray icon at the moment the icon is clicked. Only Windows
/// and macOS tell.
/// </summary>
public static class TrayCursor
{
    /// <summary>
    /// The double-click time where the system does not tell it.
    /// </summary>
    private static readonly TimeSpan DefaultDoubleClickTime = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets the longest time between two clicks that still make a double-click.
    /// </summary>
    public static TimeSpan DoubleClickTime => OperatingSystem.IsWindows() ? WindowsTrayCursor.DoubleClickTime : DefaultDoubleClickTime;

    /// <summary>
    /// Gets a value indicating whether the left or the right mouse button is held down right now, in whichever window.
    /// </summary>
    public static bool IsButtonDown =>
        OperatingSystem.IsWindows() ? WindowsTrayCursor.IsButtonDown
        : OperatingSystem.IsMacOS() && MacTrayCursor.IsButtonDown;

    /// <summary>
    /// Gets the position of the pointer.
    /// </summary>
    /// <param name="screens">
    /// The screens of the app, which macOS needs to convert the position to screen pixels.
    /// </param>
    /// <param name="position">
    /// The position in screen pixels.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the position is known.
    /// </returns>
    public static bool TryGetPosition(Screens? screens, out PixelPoint position)
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsTrayCursor.TryGetPosition(out position);
        }

        if (OperatingSystem.IsMacOS())
        {
            return MacTrayCursor.TryGetPosition(screens, out position);
        }

        position = default;
        return false;
    }
}
