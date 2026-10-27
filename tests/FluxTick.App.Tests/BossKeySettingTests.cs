using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

public sealed class BossKeySettingTests
{
    /// <summary>
    /// Verifies that a valid key is saved and shown in its normal spelling.
    /// </summary>
    [Fact]
    public async Task BossKey_ValidText_IsSavedNormalized()
    {
        await using var h = new Harness();

        h.SettingsPage.BossKey = "ctrl + shift + f9";

        h.SettingsPage.BossKey.Should().Be("Ctrl+Shift+F9");
        await h.Settings.WhenIdleAsync();
        h.Store.Stored!.General.BossKey.Should().Be("Ctrl+Shift+F9");
    }

    /// <summary>
    /// Verifies that text that is not a hotkey is put back to the key in use and nothing else is saved.
    /// </summary>
    [Fact]
    public async Task BossKey_InvalidText_GoesBackToTheKeyInUse()
    {
        await using var h = new Harness();

        h.SettingsPage.BossKey = "H";

        h.SettingsPage.BossKey.Should().Be("Ctrl+Alt+H");
        await h.Settings.WhenIdleAsync();
        h.Store.Stored?.General.BossKey.Should().NotBe("H");
    }

    /// <summary>
    /// Verifies that clearing the box turns the boss key off.
    /// </summary>
    [Fact]
    public async Task BossKey_Empty_TurnsItOff()
    {
        await using var h = new Harness();

        h.SettingsPage.BossKey = string.Empty;

        await h.Settings.WhenIdleAsync();
        h.Store.Stored!.General.BossKey.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a stored key that cannot be registered is the default again when the settings are loaded.
    /// </summary>
    [Fact]
    public async Task BossKey_UnusableStoredText_IsTheDefaultAgain()
    {
        var settings = new AppSettings();
        settings.General.BossKey = "Ctrl+Banana";
        await using var h = new Harness(settings);

        h.SettingsPage.BossKey.Should().Be("Ctrl+Alt+H");
    }

    /// <summary>
    /// Verifies that text that is not a hotkey goes back to the key in use, which is the default after an unusable one.
    /// </summary>
    [Fact]
    public async Task BossKey_InvalidTextOverUnusableStoredKey_GoesBackToTheDefault()
    {
        var settings = new AppSettings();
        settings.General.BossKey = "Ctrl+Banana";
        await using var h = new Harness(settings);

        h.SettingsPage.BossKey = "H";

        h.SettingsPage.BossKey.Should().Be("Ctrl+Alt+H");
    }

    /// <summary>
    /// Verifies that a box that hands over no text at all turns the boss key off instead of failing.
    /// </summary>
    [Fact]
    public async Task BossKey_Null_TurnsItOff()
    {
        await using var h = new Harness();

        h.SettingsPage.BossKey = null!;

        await h.Settings.WhenIdleAsync();
        h.Store.Stored!.General.BossKey.Should().BeEmpty();
    }
}
