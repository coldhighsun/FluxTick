using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using Microsoft.Extensions.Logging;

namespace FluxTick.App.Services;

/// <summary>
/// Opens a folder, or shows a file, in the file manager of the platform.
/// </summary>
public static class FileManager
{
    /// <summary>
    /// Opens a folder in the file manager, creating it first when it does not exist yet.
    /// </summary>
    /// <param name="directory">
    /// The folder.
    /// </param>
    /// <param name="start">
    /// Starts the process, or <see langword="null"/> for the real one; replaceable for tests.
    /// </param>
    /// <param name="logger">
    /// Receives the reason when it fails, or <see langword="null"/> to keep it to itself.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the file manager was asked to open the folder; <see langword="false"/> when the folder
    /// could not be created or no file manager could be started.
    /// </returns>
    public static bool TryOpen(string directory, Action<ProcessStartInfo>? start = null, ILogger? logger = null)
    {
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or SecurityException or ArgumentException)
        {
            // The folder cannot be made, so there is nothing to show.
            logger?.LogWarning(ex, "Could not create the folder {Folder}", directory);
            return false;
        }

        var info = CreateStartInfo();
        info.ArgumentList.Add(directory);
        return Launch(info, start, logger);
    }

    /// <summary>
    /// Shows a file in the file manager, selected in its folder where the platform can do so.
    /// </summary>
    /// <param name="file">
    /// The file.
    /// </param>
    /// <param name="start">
    /// Starts the process, or <see langword="null"/> for the real one; replaceable for tests.
    /// </param>
    /// <param name="logger">
    /// Receives the reason when it fails, or <see langword="null"/> to keep it to itself.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the file manager was asked to show the file; <see langword="false"/> when no file
    /// manager could be started.
    /// </returns>
    public static bool TryReveal(string file, Action<ProcessStartInfo>? start = null, ILogger? logger = null)
    {
        var info = CreateStartInfo();
        if (OperatingSystem.IsWindows())
        {
            // ArgumentList would quote the switch and the path together, which explorer does not understand.
            info.Arguments = $"/select,\"{file}\"";
        }
        else if (OperatingSystem.IsMacOS())
        {
            info.ArgumentList.Add("-R");
            info.ArgumentList.Add(file);
        }
        else
        {
            info.ArgumentList.Add(Path.GetDirectoryName(file) ?? file);
        }

        return Launch(info, start, logger);
    }

    /// <summary>
    /// Creates the start info of the file manager of the platform, without an argument. The program is named by its full
    /// path where the platform has a fixed one: a bare name is searched for in the folder of the app and the current
    /// folder first, where a planted program of that name would be started instead.
    /// </summary>
    /// <returns>
    /// The start info.
    /// </returns>
    private static ProcessStartInfo CreateStartInfo()
    {
        var info = new ProcessStartInfo { UseShellExecute = false };
        if (OperatingSystem.IsWindows())
        {
            info.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        }
        else if (OperatingSystem.IsMacOS())
        {
            info.FileName = "/usr/bin/open";
        }
        else
        {
            info.FileName = "xdg-open";
        }

        return info;
    }

    /// <summary>
    /// Starts the file manager.
    /// </summary>
    /// <param name="info">
    /// The start info.
    /// </param>
    /// <param name="start">
    /// Starts the process, or <see langword="null"/> for the real one.
    /// </param>
    /// <param name="logger">
    /// Receives the reason when it fails, or <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when it was started; <see langword="false"/> when there is no file manager.
    /// </returns>
    private static bool Launch(ProcessStartInfo info, Action<ProcessStartInfo>? start, ILogger? logger)
    {
        try
        {
            if (start is null)
            {
                using var process = Process.Start(info);
            }
            else
            {
                start(info);
            }

            return true;
        }
        catch (Win32Exception ex)
        {
            // There is no file manager, or it may not be started; there is nothing the caller can do about it. Other
            // exceptions, such as an empty file name, are defects and are left to surface.
            logger?.LogWarning(ex, "Could not start the file manager {Program}", info.FileName);
            return false;
        }
    }
}
