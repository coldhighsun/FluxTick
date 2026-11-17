using FluxTick.Core.Application;
using FluxTick.App.Services;

namespace FluxTick.App.Tests;

public sealed class HotkeyRequestTests
{
    /// <summary>
    /// Verifies that a key is registered once, however often the settings ask for it again, and again when it changes.
    /// </summary>
    [Fact]
    public void Change_ReportsOnlyWhenTheKeyDiffers()
    {
        HotkeyGesture.TryParse("Ctrl+Alt+H", out var first).Should().BeTrue();
        HotkeyGesture.TryParse("Ctrl+Alt+J", out var second).Should().BeTrue();
        var request = new HotkeyRequest();

        request.Change(first).Should().BeTrue();
        request.Change(first).Should().BeFalse();
        request.Change(second).Should().BeTrue();
        request.Change(null).Should().BeTrue();
        request.Change(null).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the hotkey to register is the setting, and none while it is suspended, off or not a hotkey.
    /// </summary>
    [Fact]
    public void Wanted_IsTheSetting_UnlessSuspendedOffOrUnusable()
    {
        HotkeyRequest.Wanted("ctrl+alt+h", suspended: false)?.ToString().Should().Be("Ctrl+Alt+H");
        HotkeyRequest.Wanted("Ctrl+Alt+H", suspended: true).Should().BeNull();
        HotkeyRequest.Wanted(string.Empty, suspended: false).Should().BeNull();
        HotkeyRequest.Wanted("Ctrl+Banana", suspended: false).Should().BeNull();
        HotkeyRequest.Wanted(null, suspended: false).Should().BeNull();
    }
}
