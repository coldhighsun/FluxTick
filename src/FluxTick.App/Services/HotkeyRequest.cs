using FluxTick.Core.Application;

namespace FluxTick.App.Services;

/// <summary>
/// Remembers the hotkey the settings asked for last, whether or not Windows accepted it, so that a key another program
/// holds is tried again only when the setting changes and not with every unrelated settings change.
/// </summary>
public sealed class HotkeyRequest
{
    /// <summary>
    /// The hotkey asked for last, or <see langword="null"/> when none is.
    /// </summary>
    private HotkeyGesture? _requested;

    /// <summary>
    /// Works out the hotkey to register from the setting.
    /// </summary>
    /// <param name="setting">
    /// The text of the setting.
    /// </param>
    /// <param name="suspended">
    /// Whether the hotkey is let go of for now, as while the settings page records a new one.
    /// </param>
    /// <returns>
    /// The hotkey, or <see langword="null"/> when none is to be registered: the setting is off or not a hotkey, or it is
    /// suspended.
    /// </returns>
    public static HotkeyGesture? Wanted(string? setting, bool suspended) =>
        !suspended && HotkeyGesture.TryParse(setting, out var gesture) ? gesture : null;

    /// <summary>
    /// Takes the hotkey the settings ask for now.
    /// </summary>
    /// <param name="wanted">
    /// The hotkey, or <see langword="null"/> for none.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when it differs from the one asked for before and has to be registered.
    /// </returns>
    public bool Change(HotkeyGesture? wanted)
    {
        if (wanted == _requested)
        {
            return false;
        }

        _requested = wanted;
        return true;
    }
}
