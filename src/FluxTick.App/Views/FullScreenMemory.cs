using Avalonia;
using Avalonia.Controls;

namespace FluxTick.App.Views;

/// <summary>
/// What a window state change that ended full screen asks for.
/// </summary>
/// <param name="Left">
/// Whether the window was full screen until this change.
/// </param>
/// <param name="RestoreSize">
/// Whether the remembered height is put back.
/// </param>
/// <param name="RestorePosition">
/// Whether the remembered position is put back.
/// </param>
internal readonly record struct FullScreenLeave(bool Left, bool RestoreSize, bool RestorePosition);

/// <summary>
/// Remembers how the window looked before it went full screen and decides what it gets back when it leaves, as a
/// sequence of the state changes the window goes through. It is the one place that knows whether the window is full
/// screen, and says whether a change really entered or left it. It holds no window, so the orders in which the changes
/// arrive (full screen, minimized and back, maximized, dragged out) can be tested.
/// </summary>
internal sealed class FullScreenMemory
{
    /// <summary>
    /// Whether the window is full screen, or was and is only minimized for now.
    /// </summary>
    private bool _isFullScreen;

    /// <summary>
    /// Gets what the window looked like before full screen, or <see langword="null"/> when nothing is known and the
    /// default size is used.
    /// </summary>
    public RestoreBounds? Bounds { get; private set; }

    /// <summary>
    /// Records the normal window as it goes full screen. A window that comes back to full screen from the minimized
    /// state keeps the record it has: what is on the screen then is the full screen window.
    /// </summary>
    /// <param name="position">
    /// The position of the window now, in screen pixels.
    /// </param>
    /// <param name="height">
    /// The height of the window now, in device independent pixels.
    /// </param>
    /// <param name="positionIsHidden">
    /// Whether the window rests beyond its screen edge, so that <paramref name="position"/> is not where it is shown; the
    /// position of the last record is kept then.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the window entered full screen with this call, <see langword="false"/> when it was
    /// full screen already.
    /// </returns>
    public bool Enter(PixelPoint position, double height, bool positionIsHidden)
    {
        if (_isFullScreen)
        {
            return false;
        }

        var place = positionIsHidden && Bounds is { } known ? known.Position : position;
        Bounds = new RestoreBounds(place, height);
        _isFullScreen = true;
        return true;
    }

    /// <summary>
    /// Takes the window out of full screen, or notes that it left it for another state.
    /// </summary>
    /// <param name="newState">
    /// The state the window has now.
    /// </param>
    /// <param name="exit">
    /// How the window leaves full screen.
    /// </param>
    /// <returns>
    /// Whether this change ended full screen, and what is put back.
    /// </returns>
    public FullScreenLeave Leave(WindowState newState, FullScreenExit exit)
    {
        var left = _isFullScreen;
        var (size, position) = FullScreenRestore.Restores(left, newState, exit);
        _isFullScreen = false;
        return new FullScreenLeave(left, size, position);
    }
}
