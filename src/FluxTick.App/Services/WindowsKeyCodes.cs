using FluxTick.Core.Application;

namespace FluxTick.App.Services;

/// <summary>
/// Says what a <see cref="HotkeyGesture"/> means to Windows: the virtual key code of its key and the modifier flags that
/// <c>RegisterHotKey</c> takes.
/// </summary>
public static class WindowsKeyCodes
{
    /// <summary>
    /// The flag for the Alt key.
    /// </summary>
    private const uint AltFlag = 0x1;

    /// <summary>
    /// The flag for the Ctrl key.
    /// </summary>
    private const uint CtrlFlag = 0x2;

    /// <summary>
    /// The flag for the Shift key.
    /// </summary>
    private const uint ShiftFlag = 0x4;

    /// <summary>
    /// The flag for the Windows key.
    /// </summary>
    private const uint WinFlag = 0x8;

    /// <summary>
    /// The virtual key codes of the keys in <see cref="HotkeyGesture.NamedKeys"/>. The codes of punctuation keys follow the
    /// keyboard layout, so such a key is the same key on every layout and not the same character.
    /// </summary>
    private static readonly Dictionary<string, int> Named = new()
    {
        ["Space"] = 0x20,
        ["Left"] = 0x25,
        ["Up"] = 0x26,
        ["Right"] = 0x27,
        ["Down"] = 0x28,
        ["Home"] = 0x24,
        ["End"] = 0x23,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["Insert"] = 0x2D,
        ["Delete"] = 0x2E,
        ["`"] = 0xC0,
        ["-"] = 0xBD,
        ["="] = 0xBB,
        ["["] = 0xDB,
        ["]"] = 0xDD,
        ["\\"] = 0xDC,
        [";"] = 0xBA,
        ["'"] = 0xDE,
        [","] = 0xBC,
        ["."] = 0xBE,
        ["/"] = 0xBF,
        ["IntlBackslash"] = 0xE2,
        ["Num0"] = 0x60,
        ["Num1"] = 0x61,
        ["Num2"] = 0x62,
        ["Num3"] = 0x63,
        ["Num4"] = 0x64,
        ["Num5"] = 0x65,
        ["Num6"] = 0x66,
        ["Num7"] = 0x67,
        ["Num8"] = 0x68,
        ["Num9"] = 0x69,
    };

    /// <summary>
    /// Gets the virtual key code of the key of a hotkey.
    /// </summary>
    /// <param name="gesture">
    /// The hotkey.
    /// </param>
    /// <returns>
    /// The code, or zero for a key that has none.
    /// </returns>
    public static int VirtualKey(HotkeyGesture gesture)
    {
        var key = gesture.Key ?? string.Empty;
        if (key.Length == 1 && (key[0] is >= 'A' and <= 'Z' || key[0] is >= '0' and <= '9'))
        {
            return key[0];
        }

        if (Named.TryGetValue(key, out var code))
        {
            return code;
        }

        // F1 to F24 are the codes 0x70 and up.
        return key.Length is 2 or 3 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out var number) && number is >= 1 and <= 24
            ? 0x70 + number - 1
            : 0;
    }

    /// <summary>
    /// Gets the modifier flags that <c>RegisterHotKey</c> takes.
    /// </summary>
    /// <param name="modifiers">
    /// The modifiers of a hotkey.
    /// </param>
    /// <returns>
    /// The flags.
    /// </returns>
    public static uint ModifierFlags(HotkeyModifiers modifiers) =>
        (modifiers.HasFlag(HotkeyModifiers.Ctrl) ? CtrlFlag : 0)
        | (modifiers.HasFlag(HotkeyModifiers.Alt) ? AltFlag : 0)
        | (modifiers.HasFlag(HotkeyModifiers.Shift) ? ShiftFlag : 0)
        | (modifiers.HasFlag(HotkeyModifiers.Win) ? WinFlag : 0);
}
