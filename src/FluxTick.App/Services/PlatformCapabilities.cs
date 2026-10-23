namespace FluxTick.App.Services;

/// <summary>
/// What the platform the application runs on can do, as far as it is known without trying.
/// </summary>
internal static class PlatformCapabilities
{
    /// <summary>
    /// Gets a value indicating whether the platform lets a window be placed beyond the screen edge. A Wayland
    /// compositor decides where a window goes, so a window cannot hide at an edge there.
    /// </summary>
    public static bool CanPlaceWindowsOffScreen { get; } =
        !(OperatingSystem.IsLinux()
            && string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase));
}
