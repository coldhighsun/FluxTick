using FluxTick.Core.Application;
using Avalonia.Input;
using FluxTick.App.Services;

namespace FluxTick.App.Views;

/// <summary>
/// Reads the key presses of a <see cref="HotkeyBox"/>: which of them complete a hotkey, and what the box shows while the
/// keys are still held.
/// </summary>
internal static class HotkeyInput
{
    /// <summary>
    /// Tells what a key press asks of a hotkey box.
    /// </summary>
    /// <param name="key">
    /// The key.
    /// </param>
    /// <param name="modifiers">
    /// The modifiers held with it.
    /// </param>
    /// <param name="gesture">
    /// The hotkey, when the press completes one.
    /// </param>
    /// <returns>
    /// What to do with the press.
    /// </returns>
    public static HotkeyPress Read(Key key, KeyModifiers modifiers, out HotkeyGesture gesture)
    {
        gesture = default;
        if (IsModifier(key))
        {
            return HotkeyPress.Wait;
        }

        // Tab moves the focus on or back (with Shift), and Escape closes the window, whatever is held with them.
        if (key is Key.Tab or Key.Escape)
        {
            return HotkeyPress.Ignore;
        }

        if (modifiers == KeyModifiers.None)
        {
            return key is Key.Back or Key.Delete ? HotkeyPress.Clear : HotkeyPress.Wait;
        }

        var name = KeyName(key);
        if (name is null)
        {
            return HotkeyPress.Wait;
        }

        var held = HotkeyModifiers.None;
        held |= modifiers.HasFlag(KeyModifiers.Control) ? HotkeyModifiers.Ctrl : HotkeyModifiers.None;
        held |= modifiers.HasFlag(KeyModifiers.Alt) ? HotkeyModifiers.Alt : HotkeyModifiers.None;
        held |= modifiers.HasFlag(KeyModifiers.Shift) ? HotkeyModifiers.Shift : HotkeyModifiers.None;
        held |= modifiers.HasFlag(KeyModifiers.Meta) ? HotkeyModifiers.Win : HotkeyModifiers.None;
        gesture = new HotkeyGesture(held, name);
        return HotkeyPress.Set;
    }

    /// <summary>
    /// Writes the modifiers that are held, for the box to show while the main key is still to come.
    /// </summary>
    /// <param name="modifiers">
    /// The modifiers held.
    /// </param>
    /// <returns>
    /// The text, for example <c>Ctrl+Alt+</c>, or <see langword="null"/> when none is held.
    /// </returns>
    public static string? Describe(KeyModifiers modifiers)
    {
        var text = string.Empty;
        text += modifiers.HasFlag(KeyModifiers.Control) ? "Ctrl+" : string.Empty;
        text += modifiers.HasFlag(KeyModifiers.Alt) ? "Alt+" : string.Empty;
        text += modifiers.HasFlag(KeyModifiers.Shift) ? "Shift+" : string.Empty;
        text += modifiers.HasFlag(KeyModifiers.Meta) ? "Win+" : string.Empty;
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Works out which modifiers are still held after a key was released. The modifiers of a release may still include the
    /// key that was just let go.
    /// </summary>
    /// <param name="released">
    /// The key that was released.
    /// </param>
    /// <param name="modifiers">
    /// The modifiers reported with the release.
    /// </param>
    /// <returns>
    /// The modifiers that are still held.
    /// </returns>
    public static KeyModifiers AfterRelease(Key released, KeyModifiers modifiers) => released switch
    {
        Key.LeftCtrl or Key.RightCtrl => modifiers & ~KeyModifiers.Control,
        Key.LeftAlt or Key.RightAlt or Key.System => modifiers & ~KeyModifiers.Alt,
        Key.LeftShift or Key.RightShift => modifiers & ~KeyModifiers.Shift,
        Key.LWin or Key.RWin => modifiers & ~KeyModifiers.Meta,
        _ => modifiers,
    };

    /// <summary>
    /// Gets a value indicating whether a key is one of the modifier keys.
    /// </summary>
    /// <param name="key">
    /// The key.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for Ctrl, Alt, Shift and Win.
    /// </returns>
    private static bool IsModifier(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.System;

    /// <summary>
    /// Names a key the way <see cref="HotkeyGesture"/> does.
    /// </summary>
    /// <param name="key">
    /// The key.
    /// </param>
    /// <returns>
    /// The name, or <see langword="null"/> for a key that cannot be a hotkey.
    /// </returns>
    private static string? KeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => ((char)('A' + (key - Key.A))).ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => $"Num{key - Key.NumPad0}",
        >= Key.F1 and <= Key.F24 => $"F{key - Key.F1 + 1}",
        Key.Space => "Space",
        Key.Left => "Left",
        Key.Up => "Up",
        Key.Right => "Right",
        Key.Down => "Down",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        Key.Insert => "Insert",
        Key.Delete => "Delete",
        Key.OemTilde => "`",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe => "\\",
        Key.OemBackslash => "IntlBackslash",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        _ => null,
    };
}

/// <summary>
/// What a key press asks of a hotkey box.
/// </summary>
internal enum HotkeyPress
{
    /// <summary>
    /// The press is not for the box, such as Tab to move on.
    /// </summary>
    Ignore,

    /// <summary>
    /// The press does not complete a hotkey yet, or cannot be part of one.
    /// </summary>
    Wait,

    /// <summary>
    /// The press completes a hotkey.
    /// </summary>
    Set,

    /// <summary>
    /// The press clears the hotkey.
    /// </summary>
    Clear,
}
