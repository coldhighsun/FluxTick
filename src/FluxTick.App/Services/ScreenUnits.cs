namespace FluxTick.App.Services;

/// <summary>
/// The unit the positions of windows and the screens are measured in. Windows and Linux report them in device pixels,
/// while the size of a window is in device independent pixels; macOS reports both in points.
/// </summary>
public static class ScreenUnits
{
    /// <summary>
    /// Gets how many units of window positions and of screens one device independent pixel makes.
    /// </summary>
    /// <param name="scaling">
    /// The scaling of the screen or the window.
    /// </param>
    /// <returns>
    /// The number of units: one on macOS, the scaling elsewhere.
    /// </returns>
    public static double PerPixel(double scaling) => OperatingSystem.IsMacOS() ? 1 : scaling;
}
