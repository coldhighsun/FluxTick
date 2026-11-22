using AwesomeAssertions;
using FluxTick.App.Views;

namespace FluxTick.App.Tests.BossKey;

public sealed class HotkeyDisplayTests
{
    [Theory]
    [InlineData("Ctrl+Alt+H", "Ctrl+Option+H")]
    [InlineData("Shift+Win+F5", "Shift+Cmd+F5")]
    [InlineData("Ctrl+Alt+", "Ctrl+Option+")]
    [InlineData("Ctrl+Space", "Ctrl+Space")]
    [InlineData("", "")]
    public void Format_UsesMacNamesOnAMac(string text, string expected) =>
        HotkeyDisplay.Format(text, mac: true).Should().Be(expected);

    [Fact]
    public void Format_KeepsTheSettingsSpellingElsewhere() =>
        HotkeyDisplay.Format("Ctrl+Alt+Win+H", mac: false).Should().Be("Ctrl+Alt+Win+H");
}
