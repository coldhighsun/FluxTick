namespace FluxTick.App.Services;

/// <summary>
/// Tells whether the tray icon carries a native menu. On macOS the app makes the icon itself to get its clicks, because a
/// menu on the icon opens on every click and Avalonia raises no click event there; the entries of the menu are shown at the
/// bottom of the tray list then.
/// </summary>
public static class TrayMenuAvailability
{
    /// <summary>
    /// Gets a value indicating whether the tray icon has a native menu.
    /// </summary>
    public static bool HasNativeMenu => !MacStatusItem.IsActive;
}
