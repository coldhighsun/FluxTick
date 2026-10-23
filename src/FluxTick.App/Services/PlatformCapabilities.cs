namespace FluxTick.App.Services;

/// <summary>
/// What the platform the application runs on can do, as far as it is known without trying.
/// </summary>
internal static class PlatformCapabilities
{
    /// <summary>
    /// Gets a value indicating whether the platform lets a window be placed beyond the screen edge.
    /// </summary>
    public static bool CanPlaceWindowsOffScreen { get; } = Evaluate(
        OperatingSystem.IsLinux(),
        Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
        Environment.GetEnvironmentVariable("DISPLAY"));

    /// <summary>
    /// Decides whether a window can be placed beyond the screen edge. A Wayland compositor decides where a window
    /// goes, but a window that runs through XWayland, which is the case when an X11 display is available, is placed
    /// as it asks.
    /// </summary>
    /// <param name="isLinux">
    /// Whether the system is Linux.
    /// </param>
    /// <param name="sessionType">
    /// The value of <c>XDG_SESSION_TYPE</c>, if any.
    /// </param>
    /// <param name="display">
    /// The value of <c>DISPLAY</c>, if any.
    /// </param>
    /// <returns>
    /// <see langword="false"/> on a Wayland session without an X11 display; otherwise <see langword="true"/>.
    /// </returns>
    public static bool Evaluate(bool isLinux, string? sessionType, string? display) =>
        !(isLinux
            && string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(display));
}
