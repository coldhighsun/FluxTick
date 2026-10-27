using FluxTick.Core.Application;
using FluxTick.App.Services;

namespace FluxTick.App.Tests;

public sealed class HotkeyGestureTests
{
    /// <summary>
    /// Verifies that names are case-insensitive, any order is accepted and the result is spelled the normal way.
    /// </summary>
    [Theory]
    [InlineData("Ctrl+Alt+H", "Ctrl+Alt+H")]
    [InlineData("alt + control + h", "Ctrl+Alt+H")]
    [InlineData("Ctrl+space", "Ctrl+Space")]
    [InlineData("Win+Shift+F12", "Shift+Win+F12")]
    public void TryParse_NormalizesTheSpelling(string text, string expected)
    {
        HotkeyGesture.TryParse(text, out var gesture).Should().BeTrue();
        gesture.ToString().Should().Be(expected);
    }

    /// <summary>
    /// Verifies that text without a modifier, without a key, with two keys or with an unknown key is refused.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("H")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+H+J")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+Banana")]
    public void TryParse_RefusesWhatIsNotAHotkey(string text) => HotkeyGesture.TryParse(text, out _).Should().BeFalse();

    /// <summary>
    /// Verifies the modifier flags and the virtual key code that Windows is given.
    /// </summary>
    [Fact]
    public void TryParse_GivesTheWin32Values()
    {
        HotkeyGesture.TryParse("Ctrl+Alt+H", out var gesture).Should().BeTrue();

        gesture.Modifiers.Should().Be(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt);
        WindowsKeyCodes.ModifierFlags(gesture.Modifiers).Should().Be(3);
        WindowsKeyCodes.VirtualKey(gesture).Should().Be(0x48);

        HotkeyGesture.TryParse("Ctrl+F5", out var function).Should().BeTrue();
        WindowsKeyCodes.VirtualKey(function).Should().Be(0x74);
    }

    /// <summary>
    /// Verifies the keys with a name of their own: arrows, navigation keys and punctuation, with their virtual key codes.
    /// </summary>
    [Theory]
    [InlineData("Ctrl+`", "Ctrl+`", 0xC0)]
    [InlineData("alt+left", "Alt+Left", 0x25)]
    [InlineData("Ctrl+Shift+PageUp", "Ctrl+Shift+PageUp", 0x21)]
    [InlineData("Ctrl+\\", "Ctrl+\\", 0xDC)]
    [InlineData("Win+/", "Win+/", 0xBF)]
    [InlineData("Ctrl+-", "Ctrl+-", 0xBD)]
    [InlineData("ctrl+num5", "Ctrl+Num5", 0x65)]
    [InlineData("Ctrl+IntlBackslash", "Ctrl+IntlBackslash", 0xE2)]
    public void TryParse_NamedKeys(string text, string expected, int code)
    {
        HotkeyGesture.TryParse(text, out var gesture).Should().BeTrue();

        gesture.ToString().Should().Be(expected);
        WindowsKeyCodes.VirtualKey(gesture).Should().Be(code);
    }

    /// <summary>
    /// Verifies that every named key can be written and read back as it is, and that Windows has a code of its own for each.
    /// </summary>
    [Fact]
    public void NamedKeys_RoundTrip_AndEachHasItsOwnWindowsCode()
    {
        var codes = new HashSet<int>();
        foreach (var name in HotkeyGesture.NamedKeys)
        {
            HotkeyGesture.TryParse($"Ctrl+{name}", out var gesture).Should().BeTrue(name);
            gesture.Key.Should().Be(name);
            var code = WindowsKeyCodes.VirtualKey(gesture);
            code.Should().NotBe(0, name);
            codes.Add(code).Should().BeTrue($"{name} must not share a code with another key");
        }
    }

    /// <summary>
    /// Verifies the flags that RegisterHotKey takes for each modifier.
    /// </summary>
    [Fact]
    public void ModifierFlags_AreTheOnesRegisterHotKeyTakes()
    {
        WindowsKeyCodes.ModifierFlags(HotkeyModifiers.Alt).Should().Be(0x1u);
        WindowsKeyCodes.ModifierFlags(HotkeyModifiers.Ctrl).Should().Be(0x2u);
        WindowsKeyCodes.ModifierFlags(HotkeyModifiers.Shift).Should().Be(0x4u);
        WindowsKeyCodes.ModifierFlags(HotkeyModifiers.Win).Should().Be(0x8u);
        WindowsKeyCodes.ModifierFlags(HotkeyModifiers.None).Should().Be(0u);
    }
}
