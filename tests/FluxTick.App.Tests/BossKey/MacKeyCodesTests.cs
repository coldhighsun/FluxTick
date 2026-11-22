using AwesomeAssertions;
using FluxTick.App.Services;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests.BossKey;

public sealed class MacKeyCodesTests
{
    [Fact]
    public void ModifierFlags_AreTheOnesRegisterEventHotKeyTakes()
    {
        MacKeyCodes.ModifierFlags(HotkeyModifiers.Win).Should().Be(0x100u);
        MacKeyCodes.ModifierFlags(HotkeyModifiers.Shift).Should().Be(0x200u);
        MacKeyCodes.ModifierFlags(HotkeyModifiers.Alt).Should().Be(0x800u);
        MacKeyCodes.ModifierFlags(HotkeyModifiers.Ctrl).Should().Be(0x1000u);
        MacKeyCodes.ModifierFlags(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt).Should().Be(0x1800u);
        MacKeyCodes.ModifierFlags(HotkeyModifiers.None).Should().Be(0u);
    }

    [Theory]
    [InlineData("A", 0)]
    [InlineData("H", 4)]
    [InlineData("Z", 6)]
    [InlineData("1", 18)]
    [InlineData("0", 29)]
    [InlineData("F1", 122)]
    [InlineData("F12", 111)]
    [InlineData("F20", 90)]
    [InlineData("Space", 49)]
    [InlineData("Left", 123)]
    public void KeyCode_GivesTheMacValues(string key, int expected) =>
        MacKeyCodes.KeyCode(new HotkeyGesture(HotkeyModifiers.Ctrl, key)).Should().Be(expected);

    [Theory]
    [InlineData("F21")]
    [InlineData("F24")]
    [InlineData("Nope")]
    public void KeyCode_RefusesAKeyMacHasNone(string key) =>
        MacKeyCodes.KeyCode(new HotkeyGesture(HotkeyModifiers.Ctrl, key)).Should().Be(-1);

    [Fact]
    public void KeyCode_EveryNamedKeyHasACode_AndNoTwoKeysShareOne()
    {
        var keys = HotkeyGesture.NamedKeys
            .Concat("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".Select(c => c.ToString()))
            .Concat(Enumerable.Range(1, 20).Select(n => $"F{n}"))
            .ToList();
        var codes = keys.Select(key => MacKeyCodes.KeyCode(new HotkeyGesture(HotkeyModifiers.Ctrl, key))).ToList();

        codes.Should().OnlyContain(code => code >= 0);
        codes.Should().OnlyHaveUniqueItems();
    }
}
