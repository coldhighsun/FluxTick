using FluxTick.Core.Application;

namespace FluxTick.App.Services;

/// <summary>
/// Listens for one hotkey that works in every program.
/// </summary>
public interface IGlobalHotkey : IDisposable
{
    /// <summary>
    /// Occurs when the hotkey is pressed, on any thread.
    /// </summary>
    event EventHandler? Pressed;

    /// <summary>
    /// Registers a hotkey, replacing the one registered before.
    /// </summary>
    /// <param name="gesture">
    /// The hotkey.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the system accepted it; <see langword="false"/> when another program holds it.
    /// </returns>
    bool Register(HotkeyGesture gesture);

    /// <summary>
    /// Gives the hotkey back, so other programs can use it.
    /// </summary>
    void Unregister();
}

/// <summary>
/// Says whether this platform has a global hotkey and makes one.
/// </summary>
public static class GlobalHotkeyAvailability
{
    /// <summary>
    /// Gets a value indicating whether this platform can listen for a global hotkey.
    /// </summary>
    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <summary>
    /// Makes the global hotkey of this platform.
    /// </summary>
    /// <returns>
    /// The hotkey, or <see langword="null"/> when the platform has none.
    /// </returns>
    public static IGlobalHotkey? Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsGlobalHotkey();
        }

        return OperatingSystem.IsMacOS() ? new MacGlobalHotkey() : null;
    }
}
