namespace FluxTick.App.Services;

/// <summary>
/// Tells whether listing the instruments while the pointer rests on the tray icon can work here, so the setting for it is
/// only offered when it can.
/// </summary>
public static class TrayHoverAvailability
{
    /// <summary>
    /// Whether the hover watcher could not be started although the platform supports it.
    /// </summary>
    private static volatile bool s_failed;

    /// <summary>
    /// Gets a value indicating whether hovering the tray icon can list the instruments: only Windows can tell where the
    /// icon is, and only while the watcher could be started.
    /// </summary>
    public static bool IsSupported => OperatingSystem.IsWindows() && !s_failed;

    /// <summary>
    /// Records that the watcher could not be started, after all attempts.
    /// </summary>
    public static void ReportFailure() => s_failed = true;

    /// <summary>
    /// Records that the watcher is running.
    /// </summary>
    public static void ReportSuccess() => s_failed = false;
}
