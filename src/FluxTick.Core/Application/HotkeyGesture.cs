using System.Globalization;

namespace FluxTick.Core.Application;

/// <summary>
/// A global hotkey: one key held together with at least one of Ctrl, Alt, Shift and Win. It reads and writes the
/// <c>Ctrl+Alt+H</c> form the settings file keeps. What the key names mean to the operating system is for the code that
/// registers the hotkey to say.
/// </summary>
/// <param name="Modifiers">
/// The held modifiers.
/// </param>
/// <param name="Key">
/// The name of the key: a letter, a digit, <c>F1</c> to <c>F24</c>, or one of the keys in <see cref="NamedKeys"/> such
/// as <c>Space</c>, <c>Left</c> or <c>`</c>.
/// </param>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    /// <summary>
    /// Gets the keys that have a name of their own: the space bar, the keys that move, the number pad digits, and the
    /// punctuation keys, written as the character on them on a US keyboard. A punctuation name stands for the key, not for
    /// the character, so it means a different character on another keyboard layout. <c>IntlBackslash</c> is the extra key
    /// next to the left Shift key of an ISO keyboard.
    /// </summary>
    public static IReadOnlyList<string> NamedKeys { get; } =
    [
        "Space", "Left", "Up", "Right", "Down", "Home", "End", "PageUp", "PageDown", "Insert", "Delete",
        "`", "-", "=", "[", "]", "\\", ";", "'", ",", ".", "/", "IntlBackslash",
        "Num0", "Num1", "Num2", "Num3", "Num4", "Num5", "Num6", "Num7", "Num8", "Num9",
    ];

    /// <summary>
    /// Reads a hotkey from text.
    /// </summary>
    /// <param name="text">
    /// The text, for example <c>Ctrl+Alt+H</c>; names are case-insensitive and may come in any order.
    /// </param>
    /// <param name="gesture">
    /// The hotkey, in its normal spelling.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the text names one key and at least one modifier.
    /// </returns>
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        string? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL":
                    modifiers |= HotkeyModifiers.Ctrl;
                    break;
                case "ALT":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "SHIFT":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "WIN" or "META":
                    modifiers |= HotkeyModifiers.Win;
                    break;
                default:
                    if (key is not null || !TryNormalizeKey(raw, out key))
                    {
                        return false;
                    }

                    break;
            }
        }

        if (key is null || modifiers == HotkeyModifiers.None)
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    /// <summary>
    /// Writes the hotkey in the form the settings file keeps.
    /// </summary>
    /// <returns>
    /// The text, for example <c>Ctrl+Alt+H</c>.
    /// </returns>
    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(Key);
        return string.Join('+', parts);
    }

    /// <summary>
    /// Spells a key name the normal way.
    /// </summary>
    /// <param name="raw">
    /// The name as written.
    /// </param>
    /// <param name="key">
    /// The name in its normal spelling.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the key can be a hotkey.
    /// </returns>
    private static bool TryNormalizeKey(string raw, out string key)
    {
        key = NamedKeys.FirstOrDefault(name => string.Equals(name, raw, StringComparison.OrdinalIgnoreCase)) ?? raw.ToUpperInvariant();
        return IsKnown(key);
    }

    /// <summary>
    /// Tells whether a key name, in its normal spelling, is one a hotkey can have.
    /// </summary>
    /// <param name="key">
    /// The name.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for a letter, a digit, <c>F1</c> to <c>F24</c> and the keys in <see cref="NamedKeys"/>.
    /// </returns>
    private static bool IsKnown(string key) =>
        (key.Length == 1 && (key[0] is >= 'A' and <= 'Z' || key[0] is >= '0' and <= '9'))
        || NamedKeys.Contains(key)
        || (key.Length is 2 or 3 && key[0] == 'F'
            && int.TryParse(key.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number is >= 1 and <= 24);
}

/// <summary>
/// The modifier keys of a hotkey.
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    /// <summary>
    /// No modifier.
    /// </summary>
    None = 0,

    /// <summary>
    /// The Ctrl key.
    /// </summary>
    Ctrl = 1,

    /// <summary>
    /// The Alt key.
    /// </summary>
    Alt = 2,

    /// <summary>
    /// The Shift key.
    /// </summary>
    Shift = 4,

    /// <summary>
    /// The Windows key.
    /// </summary>
    Win = 8,
}
