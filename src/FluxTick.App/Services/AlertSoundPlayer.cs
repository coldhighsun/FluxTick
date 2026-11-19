using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace FluxTick.App.Services;

/// <summary>
/// Tells whether this platform can play the sound of an alert, so the setting for it is only offered when it can.
/// </summary>
public static class AlertSoundAvailability
{
    /// <summary>
    /// Gets a value indicating whether an alert can be heard here: Windows and macOS have a system sound to play, other
    /// platforms have no common way to play one.
    /// </summary>
    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
}

/// <summary>
/// Plays the sound that goes with the notification of an alert.
/// </summary>
public interface IAlertSoundPlayer
{
    /// <summary>
    /// Plays the sound once. It returns at once and does not wait for the sound to end. A sound that cannot be played is
    /// logged and otherwise ignored, since the notification has been shown anyway.
    /// </summary>
    void Play();
}

/// <summary>
/// An <see cref="IAlertSoundPlayer"/> for platforms without support. It plays nothing.
/// </summary>
public sealed class NullAlertSoundPlayer : IAlertSoundPlayer
{
    /// <inheritdoc />
    public void Play()
    {
    }
}

/// <summary>
/// An <see cref="IAlertSoundPlayer"/> that plays the system sound for an asterisk, which the user can change in the sound
/// settings of Windows.
/// </summary>
/// <param name="logger">
/// The logger.
/// </param>
[SupportedOSPlatform("windows")]
public sealed class WindowsAlertSoundPlayer(ILogger<WindowsAlertSoundPlayer> logger) : IAlertSoundPlayer
{
    /// <summary>
    /// The sound type of the system sound for an asterisk, which is the one for information.
    /// </summary>
    private const uint IconAsterisk = 0x00000040;

    /// <inheritdoc />
    public void Play()
    {
        if (!MessageBeep(IconAsterisk))
        {
            logger.LogWarning("The alert sound could not be played (error {Error})", Marshal.GetLastWin32Error());
        }
    }

    /// <summary>
    /// Plays a system sound.
    /// </summary>
    /// <param name="type">
    /// The type of the sound.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the sound was started.
    /// </returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MessageBeep(uint type);
}

/// <summary>
/// An <see cref="IAlertSoundPlayer"/> that plays a system sound of macOS with the <c>afplay</c> tool.
/// </summary>
/// <param name="logger">
/// The logger.
/// </param>
/// <param name="soundPath">
/// The sound file to play, or <see langword="null"/> for the default one.
/// </param>
/// <param name="start">
/// Starts the process that plays the sound, or <see langword="null"/> to start it for real. It is given for tests.
/// </param>
public sealed class MacAlertSoundPlayer(
    ILogger<MacAlertSoundPlayer> logger,
    string? soundPath = null,
    Func<ProcessStartInfo, Process?>? start = null) : IAlertSoundPlayer
{
    /// <summary>
    /// The system sound that is played unless another file is given.
    /// </summary>
    public const string DefaultSoundPath = "/System/Library/Sounds/Glass.aiff";

    /// <summary>
    /// The place of the tool that plays a file. It is named in full, so that the PATH of the process, which is short for
    /// an app started from the Finder, does not decide which tool runs.
    /// </summary>
    public const string ToolPath = "/usr/bin/afplay";

    /// <summary>
    /// The sound file to play.
    /// </summary>
    private readonly string _soundPath = soundPath ?? DefaultSoundPath;

    /// <summary>
    /// Starts the process that plays the sound.
    /// </summary>
    private readonly Func<ProcessStartInfo, Process?> _start = start ?? Process.Start;

    /// <summary>
    /// Describes the process that plays the sound.
    /// </summary>
    /// <param name="soundPath">
    /// The sound file to play.
    /// </param>
    /// <returns>
    /// The start info of the <c>afplay</c> tool, which gets the file as an argument and not through a shell.
    /// </returns>
    public static ProcessStartInfo CreateStartInfo(string soundPath) => new(ToolPath)
    {
        ArgumentList = { soundPath },
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    /// <inheritdoc />
    public void Play()
    {
        try
        {
            // The tool ends by itself when the sound is over; the handle is not needed.
            _start(CreateStartInfo(_soundPath))?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "The alert sound could not be played");
        }
    }
}
