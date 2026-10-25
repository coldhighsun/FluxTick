using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace FluxTick.App.Services;

/// <summary>
/// Registers the app to start when the user signs in.
/// </summary>
public interface IAutoStartService
{
    /// <summary>
    /// Gets a value indicating whether the platform supports starting the app at sign-in.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Gets a value indicating whether the app is registered to start at sign-in.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when it is registered.
    /// </returns>
    bool IsEnabled();

    /// <summary>
    /// Registers or unregisters the app.
    /// </summary>
    /// <param name="enabled">
    /// Whether the app should start at sign-in.
    /// </param>
    void SetEnabled(bool enabled);

    /// <summary>
    /// Points the registration at this executable when it names one that no longer exists, as happens after the app was
    /// moved. A registration of another copy that still exists, and the absence of a registration, are left as they are.
    /// </summary>
    void RepairIfMoved();
}

/// <summary>
/// Builds the command line that starts the app at sign-in.
/// </summary>
public static class AutoStartCommand
{
    /// <summary>
    /// Gets a value indicating whether a process path is the app's own executable. The path of a process started with
    /// <c>dotnet FluxTick.dll</c> is the dotnet host, which would not start the app by itself.
    /// </summary>
    /// <param name="processPath">
    /// The path of the running process, or <see langword="null"/> when unknown.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the path can be registered to start the app.
    /// </returns>
    public static bool IsApplicationPath(string? processPath)
    {
        if (string.IsNullOrEmpty(processPath))
        {
            return false;
        }

        // Both separators are cut, as Path would only know the ones of the platform the code runs on.
        var file = processPath[(processPath.LastIndexOfAny(['\\', '/']) + 1)..];
        return !string.Equals(Path.GetFileNameWithoutExtension(file), "dotnet", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds the registry value that starts the executable; the path is quoted because it may contain spaces.
    /// </summary>
    /// <param name="processPath">
    /// The path of the executable.
    /// </param>
    /// <returns>
    /// The command line.
    /// </returns>
    public static string Build(string processPath) => $"\"{processPath}\"";

    /// <summary>
    /// Reads the executable out of a registered command line, which is quoted or ends at the first space.
    /// </summary>
    /// <param name="command">
    /// The registered command line.
    /// </param>
    /// <returns>
    /// The path of the executable, or <see langword="null"/> when the command line is empty.
    /// </returns>
    public static string? ExecutableOf(string command)
    {
        command = command.Trim();
        if (command.Length == 0)
        {
            return null;
        }

        if (command[0] == '"')
        {
            var end = command.IndexOf('"', 1);
            return end < 0 ? command[1..] : command[1..end];
        }

        var space = command.IndexOf(' ');
        return space < 0 ? command : command[..space];
    }
}

/// <summary>
/// An <see cref="IAutoStartService"/> for platforms without support.
/// </summary>
public sealed class NullAutoStartService : IAutoStartService
{
    /// <inheritdoc />
    public bool IsSupported => false;

    /// <inheritdoc />
    public bool IsEnabled() => false;

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
    }

    /// <inheritdoc />
    public void RepairIfMoved()
    {
    }
}

/// <summary>
/// An <see cref="IAutoStartService"/> that keeps a value under the per-user <c>Run</c> registry key.
/// </summary>
/// <param name="logger">
/// The logger.
/// </param>
[SupportedOSPlatform("windows")]
public sealed class WindowsAutoStartService(ILogger<WindowsAutoStartService> logger) : IAutoStartService
{
    /// <summary>
    /// The registry key that lists the programs started at sign-in.
    /// </summary>
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// The registry key where Windows records whether the user disabled an entry of the <c>Run</c> key.
    /// </summary>
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>
    /// The name of the value that starts this app.
    /// </summary>
    private const string ValueName = "FluxTick";

    /// <inheritdoc />
    public bool IsSupported => AutoStartCommand.IsApplicationPath(Environment.ProcessPath);

    /// <inheritdoc />
    public bool IsEnabled()
    {
        if (!IsSupported)
        {
            return false;
        }

        return ReadCommand() is not null && !IsDisabledInStartupSettings();
    }

    /// <summary>
    /// Determines whether the user turned the entry off in the Startup page of Windows Settings or Task Manager, which
    /// leaves the <c>Run</c> value in place and records the choice under <c>StartupApproved</c>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when Windows will not start the entry.
    /// </returns>
    private bool IsDisabledInStartupSettings()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);

            // The data starts with a flag byte whose lowest bit is set for a disabled entry (03, 07), and clear for an
            // enabled one (02, 06).
            return key?.GetValue(ValueName) is byte[] { Length: > 0 } data && (data[0] & 1) != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            logger.LogWarning(ex, "Could not read the start at sign-in approval");
            return false;
        }
    }

    /// <inheritdoc />
    public void RepairIfMoved()
    {
        if (!IsSupported || ReadCommand() is not { } command)
        {
            return;
        }

        var executable = AutoStartCommand.ExecutableOf(command);
        if (executable is null || !File.Exists(executable))
        {
            try
            {
                // Only the path is repaired: an entry the user turned off in Windows must stay off.
                using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
                key.SetValue(ValueName, AutoStartCommand.Build(Environment.ProcessPath!));
            }
            catch (Exception ex)
            {
                // Starting the app does not depend on the repair.
                logger.LogWarning(ex, "Could not repair the start at sign-in entry");
            }
        }
    }

    /// <summary>
    /// Reads the registered command line.
    /// </summary>
    /// <returns>
    /// The command line, or <see langword="null"/> when there is none or the registry cannot be read: a machine that
    /// forbids the access must not stop the app from starting.
    /// </returns>
    private string? ReadCommand()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            logger.LogWarning(ex, "Could not read the start at sign-in entry");
            return null;
        }
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        if (!IsSupported)
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                key.SetValue(ValueName, AutoStartCommand.Build(Environment.ProcessPath!));

                // Turning the setting on again must also clear a choice made in Windows, or it would have no effect.
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
                approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not {Action} the start at sign-in entry", enabled ? "create" : "remove");
            throw;
        }
    }
}
