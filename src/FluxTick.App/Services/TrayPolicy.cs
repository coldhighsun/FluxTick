using FluxTick.Core.Application;

namespace FluxTick.App.Services;

/// <summary>
/// Decides how the main window behaves at startup and when it is closed, in terms of the tray.
/// </summary>
public static class TrayPolicy
{
    /// <summary>
    /// The command line option that starts the app without showing its window.
    /// </summary>
    public const string MinimizedOption = "--minimized";

    /// <summary>
    /// Gets a value indicating whether the window stays hidden at startup. It does when the user chose to start in the
    /// tray, or the command line asks for it, but never without a tray icon, as there would be no way to bring it back.
    /// </summary>
    /// <param name="settings">
    /// The general settings.
    /// </param>
    /// <param name="args">
    /// The command line arguments.
    /// </param>
    /// <param name="hasTrayIcon">
    /// Tells whether the tray icon exists. It is asked only when hiding was requested, as finding out can take a moment.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the window is not shown at startup.
    /// </returns>
    public static bool StartHidden(GeneralSettings settings, IEnumerable<string> args, Func<bool> hasTrayIcon) =>
        (settings.StartMinimized || args.Contains(MinimizedOption, StringComparer.OrdinalIgnoreCase)) && hasTrayIcon();

    /// <summary>
    /// Gets a value indicating whether closing the window hides it to the tray. It does only when the user wants that and
    /// there is a tray to bring the window back from; otherwise closing the window exits the app.
    /// </summary>
    /// <param name="settings">
    /// The general settings.
    /// </param>
    /// <param name="hasTrayIcon">
    /// Tells whether the tray icon exists. It is asked only when the user wants the window hidden.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when closing hides the window.
    /// </returns>
    public static bool CloseHidesWindow(GeneralSettings settings, Func<bool> hasTrayIcon) => settings.CloseToTray && hasTrayIcon();
}
