namespace FluxTick.App.Services;

/// <summary>
/// Tells whether clicking the tray icon can list the instruments here, so the settings for it are only offered when it can.
/// </summary>
public static class TrayPopupAvailability
{
    /// <summary>
    /// Gets a value indicating whether clicking the tray icon can list the instruments: only Windows tells where the pointer
    /// was when the icon was clicked, which is where the list opens.
    /// </summary>
    public static bool IsSupported => OperatingSystem.IsWindows();
}
