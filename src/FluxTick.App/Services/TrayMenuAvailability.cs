namespace FluxTick.App.Services;

/// <summary>
/// Tells whether the tray icon carries a native menu. On macOS a menu on the icon opens on every click and takes the click
/// from the app, so the tray list could not open; there the entries of the menu are shown at the bottom of the list.
/// </summary>
public static class TrayMenuAvailability
{
    /// <summary>
    /// Gets a value indicating whether the tray icon has a native menu.
    /// </summary>
    public static bool HasNativeMenu => !OperatingSystem.IsMacOS();
}
