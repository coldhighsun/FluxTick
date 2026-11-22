using FluxTick.Core.Application;

namespace FluxTick.App.Services;

/// <summary>
/// Says what a <see cref="HotkeyGesture"/> means to macOS: the (ANSI) virtual key code of its key and the Carbon modifier
/// flags that <c>RegisterEventHotKey</c> takes. <c>Win</c> is the Command key and <c>Alt</c> is Option.
/// </summary>
public static class MacKeyCodes
{
    /// <summary>
    /// The flag for the Command key.
    /// </summary>
    private const uint CommandFlag = 0x100;

    /// <summary>
    /// The flag for the Shift key.
    /// </summary>
    private const uint ShiftFlag = 0x200;

    /// <summary>
    /// The flag for the Option key.
    /// </summary>
    private const uint OptionFlag = 0x800;

    /// <summary>
    /// The flag for the Control key.
    /// </summary>
    private const uint ControlFlag = 0x1000;

    /// <summary>
    /// The key codes of letters and digits.
    /// </summary>
    private static readonly Dictionary<char, int> Characters = new()
    {
        ['A'] = 0, ['S'] = 1, ['D'] = 2, ['F'] = 3, ['H'] = 4, ['G'] = 5, ['Z'] = 6, ['X'] = 7, ['C'] = 8, ['V'] = 9,
        ['B'] = 11, ['Q'] = 12, ['W'] = 13, ['E'] = 14, ['R'] = 15, ['Y'] = 16, ['T'] = 17, ['O'] = 31, ['U'] = 32,
        ['I'] = 34, ['P'] = 35, ['L'] = 37, ['J'] = 38, ['K'] = 40, ['N'] = 45, ['M'] = 46,
        ['1'] = 18, ['2'] = 19, ['3'] = 20, ['4'] = 21, ['6'] = 22, ['5'] = 23, ['9'] = 25, ['7'] = 26, ['8'] = 28, ['0'] = 29,
    };

    /// <summary>
    /// The key codes of the keys in <see cref="HotkeyGesture.NamedKeys"/>. Punctuation keys are named by the character on
    /// a US keyboard, as on Windows. Mac keyboards have no Insert key; the Help key sits where it does.
    /// </summary>
    private static readonly Dictionary<string, int> Named = new()
    {
        ["Space"] = 49,
        ["Left"] = 123,
        ["Up"] = 126,
        ["Right"] = 124,
        ["Down"] = 125,
        ["Home"] = 115,
        ["End"] = 119,
        ["PageUp"] = 116,
        ["PageDown"] = 121,
        ["Insert"] = 114,
        ["Delete"] = 117,
        ["`"] = 50,
        ["-"] = 27,
        ["="] = 24,
        ["["] = 33,
        ["]"] = 30,
        ["\\"] = 42,
        [";"] = 41,
        ["'"] = 39,
        [","] = 43,
        ["."] = 47,
        ["/"] = 44,
        ["IntlBackslash"] = 10,
        ["Num0"] = 82,
        ["Num1"] = 83,
        ["Num2"] = 84,
        ["Num3"] = 85,
        ["Num4"] = 86,
        ["Num5"] = 87,
        ["Num6"] = 88,
        ["Num7"] = 89,
        ["Num8"] = 91,
        ["Num9"] = 92,
    };

    /// <summary>
    /// The key codes of F1 to F20, by number; a Mac keyboard has no F21 to F24.
    /// </summary>
    private static readonly int[] FunctionKeys =
    [
        122, 120, 99, 118, 96, 97, 98, 100, 101, 109, 103, 111, 105, 107, 113, 106, 64, 79, 80, 90,
    ];

    /// <summary>
    /// Gets the virtual key code of the key of a hotkey.
    /// </summary>
    /// <param name="gesture">
    /// The hotkey.
    /// </param>
    /// <returns>
    /// The code, or -1 for a key that macOS has none for.
    /// </returns>
    public static int KeyCode(HotkeyGesture gesture)
    {
        var key = gesture.Key ?? string.Empty;
        if (key.Length == 1 && Characters.TryGetValue(key[0], out var character))
        {
            return character;
        }

        if (Named.TryGetValue(key, out var code))
        {
            return code;
        }

        return key.Length is 2 or 3 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out var number)
            && number >= 1 && number <= FunctionKeys.Length
            ? FunctionKeys[number - 1]
            : -1;
    }

    /// <summary>
    /// Gets the modifier flags that <c>RegisterEventHotKey</c> takes.
    /// </summary>
    /// <param name="modifiers">
    /// The modifiers of a hotkey.
    /// </param>
    /// <returns>
    /// The flags.
    /// </returns>
    public static uint ModifierFlags(HotkeyModifiers modifiers) =>
        (modifiers.HasFlag(HotkeyModifiers.Ctrl) ? ControlFlag : 0)
        | (modifiers.HasFlag(HotkeyModifiers.Alt) ? OptionFlag : 0)
        | (modifiers.HasFlag(HotkeyModifiers.Shift) ? ShiftFlag : 0)
        | (modifiers.HasFlag(HotkeyModifiers.Win) ? CommandFlag : 0);
}
