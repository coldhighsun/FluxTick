using System.Diagnostics;

namespace FluxTick.App.Services;

/// <summary>
/// Finds out whether the desktop shows tray icons. Declaring a tray icon does not show one: a Linux desktop without a
/// status notifier host (such as stock GNOME) silently drops it.
/// </summary>
public static class TrayAvailability
{
    /// <summary>
    /// The well-known bus name a status notifier host owns while it is running.
    /// </summary>
    private const string WatcherName = "org.kde.StatusNotifierWatcher";

    /// <summary>
    /// How long to wait for the session bus to answer.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Whether the desktop was found to show tray icons. Only a positive answer is kept: at sign-in the tray host may start
    /// after this app, so a negative answer is asked again the next time it matters.
    /// </summary>
    private static volatile bool s_shown;

    /// <summary>
    /// Asks the desktop whether a tray is available. Windows and macOS always have one; on Linux the session bus is asked
    /// whether a status notifier host runs.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when tray icons are shown; <see langword="false"/> when they are not, or when that could not
    /// be found out, so that the window is never hidden to a tray that may not be there.
    /// </returns>
    public static bool Detect()
    {
        if (s_shown)
        {
            return true;
        }

        var shown = Probe();
        s_shown = shown;
        return shown;
    }

    /// <summary>
    /// Asks the desktop, without looking at what was found before.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when tray icons are shown.
    /// </returns>
    private static bool Probe()
    {
        if (!OperatingSystem.IsLinux())
        {
            return true;
        }

        try
        {
            var info = new ProcessStartInfo("dbus-send")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in new[]
            {
                "--session", "--print-reply", "--dest=org.freedesktop.DBus", "/org/freedesktop/DBus",
                "org.freedesktop.DBus.NameHasOwner", $"string:{WatcherName}",
            })
            {
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info);
            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(ProbeTimeout))
            {
                process.Kill();
                return false;
            }

            return process.ExitCode == 0 && ParseNameHasOwner(output.GetAwaiter().GetResult());
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the reply of <c>NameHasOwner</c> as printed by <c>dbus-send --print-reply</c>.
    /// </summary>
    /// <param name="output">
    /// The printed reply, which ends with <c>boolean true</c> or <c>boolean false</c>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the name has an owner.
    /// </returns>
    public static bool ParseNameHasOwner(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .LastOrDefault() is "boolean true";
}
