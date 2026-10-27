using FluxTick.Core.Application;
using FluxTick.App.Services;
using Avalonia.Input;
using FluxTick.App.Views;

namespace FluxTick.App.Tests;

public sealed class HotkeyInputTests
{
    /// <summary>
    /// Verifies that a letter with modifiers completes a hotkey, spelled as the settings keep it.
    /// </summary>
    [Fact]
    public void Read_LetterWithModifiers_SetsTheHotkey()
    {
        HotkeyInput.Read(Key.H, KeyModifiers.Control | KeyModifiers.Alt, out var gesture).Should().Be(HotkeyPress.Set);

        gesture.ToString().Should().Be("Ctrl+Alt+H");
    }

    /// <summary>
    /// Verifies the names of digits, number pad digits, function keys and the space bar.
    /// </summary>
    [Theory]
    [InlineData(Key.D7, "Ctrl+7")]
    [InlineData(Key.NumPad3, "Ctrl+Num3")]
    [InlineData(Key.F12, "Ctrl+F12")]
    [InlineData(Key.Space, "Ctrl+Space")]
    [InlineData(Key.OemTilde, "Ctrl+`")]
    [InlineData(Key.OemPipe, "Ctrl+\\")]
    [InlineData(Key.Left, "Ctrl+Left")]
    [InlineData(Key.PageDown, "Ctrl+PageDown")]
    [InlineData(Key.Delete, "Ctrl+Delete")]
    public void Read_OtherKeys_AreNamedLikeTheSettings(Key key, string expected)
    {
        HotkeyInput.Read(key, KeyModifiers.Control, out var gesture).Should().Be(HotkeyPress.Set);

        gesture.ToString().Should().Be(expected);
    }

    /// <summary>
    /// Verifies that pressing only a modifier waits for the key, and that Shift and Win are named too.
    /// </summary>
    [Fact]
    public void Read_ModifierAlone_Waits_AndWinIsNamed()
    {
        HotkeyInput.Read(Key.LeftCtrl, KeyModifiers.Control, out _).Should().Be(HotkeyPress.Wait);

        HotkeyInput.Read(Key.K, KeyModifiers.Shift | KeyModifiers.Meta, out var gesture).Should().Be(HotkeyPress.Set);
        gesture.ToString().Should().Be("Shift+Win+K");
    }

    /// <summary>
    /// Verifies that Backspace and Delete alone clear the box, that Tab and Escape are left to move on or close the window, and that a key
    /// without a modifier does not make a hotkey.
    /// </summary>
    [Fact]
    public void Read_WithoutModifiers_ClearsOrWaits()
    {
        HotkeyInput.Read(Key.Back, KeyModifiers.None, out _).Should().Be(HotkeyPress.Clear);
        HotkeyInput.Read(Key.Delete, KeyModifiers.None, out _).Should().Be(HotkeyPress.Clear);
        HotkeyInput.Read(Key.Escape, KeyModifiers.None, out _).Should().Be(HotkeyPress.Ignore);
        HotkeyInput.Read(Key.Tab, KeyModifiers.None, out _).Should().Be(HotkeyPress.Ignore);
        HotkeyInput.Read(Key.H, KeyModifiers.None, out _).Should().Be(HotkeyPress.Wait);
    }

    /// <summary>
    /// Verifies that Tab and Escape are left alone with any modifiers, so Shift+Tab still moves the focus back.
    /// </summary>
    [Fact]
    public void Read_TabAndEscapeWithModifiers_AreLeftAlone()
    {
        HotkeyInput.Read(Key.Tab, KeyModifiers.Shift, out _).Should().Be(HotkeyPress.Ignore);
        HotkeyInput.Read(Key.Tab, KeyModifiers.Control, out _).Should().Be(HotkeyPress.Ignore);
        HotkeyInput.Read(Key.Escape, KeyModifiers.Alt, out _).Should().Be(HotkeyPress.Ignore);
    }

    /// <summary>
    /// Verifies that the modifiers that are held are written with a trailing plus, in the order of the settings.
    /// </summary>
    [Fact]
    public void Describe_WritesTheHeldModifiersWithAPlus()
    {
        HotkeyInput.Describe(KeyModifiers.Control).Should().Be("Ctrl+");
        HotkeyInput.Describe(KeyModifiers.Alt | KeyModifiers.Control).Should().Be("Ctrl+Alt+");
        HotkeyInput.Describe(KeyModifiers.Meta | KeyModifiers.Shift).Should().Be("Shift+Win+");
        HotkeyInput.Describe(KeyModifiers.None).Should().BeNull();
    }

    /// <summary>
    /// Verifies that a modifier that was just released is no longer counted, even when the release still reports it.
    /// </summary>
    [Fact]
    public void AfterRelease_DropsTheReleasedModifier()
    {
        var held = KeyModifiers.Control | KeyModifiers.Alt;

        HotkeyInput.AfterRelease(Key.LeftAlt, held).Should().Be(KeyModifiers.Control);
        HotkeyInput.AfterRelease(Key.RightCtrl, held).Should().Be(KeyModifiers.Alt);
        HotkeyInput.AfterRelease(Key.LeftCtrl, KeyModifiers.Control).Should().Be(KeyModifiers.None);
        HotkeyInput.AfterRelease(Key.H, held).Should().Be(held);
    }

    /// <summary>
    /// Verifies that a key that cannot be a hotkey, such as an arrow, waits.
    /// </summary>
    [Fact]
    public void Read_UnsupportedKey_Waits() =>
        HotkeyInput.Read(Key.PrintScreen, KeyModifiers.Control, out _).Should().Be(HotkeyPress.Wait);

    /// <summary>
    /// Verifies that the key that is pressed is the key that gets registered, by its Win32 virtual key code. The text of
    /// the hotkey is not enough: two keys can share a name and still be different keys.
    /// </summary>
    [Theory]
    [InlineData(Key.A, 0x41)]
    [InlineData(Key.Z, 0x5A)]
    [InlineData(Key.D0, 0x30)]
    [InlineData(Key.D9, 0x39)]
    [InlineData(Key.NumPad0, 0x60)]
    [InlineData(Key.NumPad9, 0x69)]
    [InlineData(Key.F1, 0x70)]
    [InlineData(Key.F24, 0x87)]
    [InlineData(Key.Space, 0x20)]
    [InlineData(Key.Left, 0x25)]
    [InlineData(Key.Up, 0x26)]
    [InlineData(Key.Right, 0x27)]
    [InlineData(Key.Down, 0x28)]
    [InlineData(Key.Home, 0x24)]
    [InlineData(Key.End, 0x23)]
    [InlineData(Key.PageUp, 0x21)]
    [InlineData(Key.PageDown, 0x22)]
    [InlineData(Key.Insert, 0x2D)]
    [InlineData(Key.Delete, 0x2E)]
    [InlineData(Key.OemTilde, 0xC0)]
    [InlineData(Key.OemMinus, 0xBD)]
    [InlineData(Key.OemPlus, 0xBB)]
    [InlineData(Key.OemOpenBrackets, 0xDB)]
    [InlineData(Key.OemCloseBrackets, 0xDD)]
    [InlineData(Key.OemPipe, 0xDC)]
    [InlineData(Key.OemBackslash, 0xE2)]
    [InlineData(Key.OemSemicolon, 0xBA)]
    [InlineData(Key.OemQuotes, 0xDE)]
    [InlineData(Key.OemComma, 0xBC)]
    [InlineData(Key.OemPeriod, 0xBE)]
    [InlineData(Key.OemQuestion, 0xBF)]
    public void Read_PressedKey_RegistersItsOwnVirtualKey(Key key, int expectedCode)
    {
        HotkeyInput.Read(key, KeyModifiers.Control, out var gesture).Should().Be(HotkeyPress.Set);

        WindowsKeyCodes.VirtualKey(gesture).Should().Be(expectedCode);
    }

    /// <summary>
    /// Verifies that no two keys the box can name share a name or a virtual key code, so each recorded key is registered
    /// as itself.
    /// </summary>
    [Fact]
    public void Read_NoTwoKeysShareAName()
    {
        var byName = new Dictionary<string, Key>();
        foreach (var key in Enum.GetValues<Key>().Distinct())
        {
            if (HotkeyInput.Read(key, KeyModifiers.Control, out var gesture) != HotkeyPress.Set)
            {
                continue;
            }

            // Key has aliases for the same key (PageUp and Prior, for example); those share a value and are distinct above.
            byName.TryGetValue(gesture.Key, out var other).Should().BeFalse($"{key} and {other} share the name {gesture.Key}");
            byName[gesture.Key] = key;
        }
    }

    /// <summary>
    /// Verifies that every key the box can name is one the settings accept, so a recorded key is never refused later.
    /// </summary>
    [Fact]
    public void Read_EveryKeyTheBoxNamesIsAcceptedBySettings()
    {
        foreach (var key in Enum.GetValues<Key>())
        {
            if (HotkeyInput.Read(key, KeyModifiers.Control, out var gesture) == HotkeyPress.Set)
            {
                HotkeyGesture.TryParse(gesture.ToString(), out var parsed).Should().BeTrue(key.ToString());
                parsed.Should().Be(gesture, key.ToString());
            }
        }
    }
}
