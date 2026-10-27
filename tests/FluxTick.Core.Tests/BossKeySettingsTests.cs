using FluxTick.Core.Application;
using FluxTick.Core.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for how the boss key is kept in the settings.
/// </summary>
public sealed class BossKeySettingsTests : IDisposable
{
    /// <summary>
    /// The folder of the settings files of the tests.
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"fluxtick-tests-{Guid.NewGuid():N}");

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that a key that is missing or null in the file is the default, and an empty one stays off.
    /// </summary>
    [Theory]
    [InlineData("""{ "general": { "startMinimized": true } }""", "Ctrl+Alt+H")]
    [InlineData("""{ "general": { "bossKey": null } }""", "Ctrl+Alt+H")]
    [InlineData("""{ "general": { "bossKey": "" } }""", "")]
    [InlineData("""{ "general": { "bossKey": "Alt+2" } }""", "Alt+2")]
    public async Task LoadAsync_ReadsTheBossKey(string json, string expected)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, json);
        var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        var loaded = await store.LoadAsync();

        loaded!.General.BossKey.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that a key that cannot be registered becomes the default again, and that this counts as a repair.
    /// </summary>
    [Theory]
    [InlineData("Ctrl+Banana")]
    [InlineData("H")]
    [InlineData("Ctrl+Alt")]
    [InlineData("  ")]
    [InlineData("Ctrl+H+J")]
    public void Normalize_UnusableBossKey_BecomesTheDefault(string text)
    {
        var settings = new AppSettings();
        settings.General.BossKey = text;

        settings.Normalize().Should().BeTrue();

        settings.General.BossKey.Should().Be("Ctrl+Alt+H");
    }

    /// <summary>
    /// Verifies that a key that is turned off stays off, a usable key stays, and a key written differently is written the
    /// usual way.
    /// </summary>
    [Fact]
    public void Normalize_KeepsOffAndUsableKeys_AndSpellsThemTheUsualWay()
    {
        var off = Settled();
        off.General.BossKey = string.Empty;
        off.Normalize().Should().BeFalse();
        off.General.BossKey.Should().BeEmpty();

        var usable = Settled();
        usable.General.BossKey = "Ctrl+Shift+F9";
        usable.Normalize().Should().BeFalse();
        usable.General.BossKey.Should().Be("Ctrl+Shift+F9");

        var spelled = Settled();
        spelled.General.BossKey = "alt + control + h";
        spelled.Normalize().Should().BeTrue();
        spelled.General.BossKey.Should().Be("Ctrl+Alt+H");
    }

    /// <summary>
    /// Makes settings that need no other repair, so that a repair reported by a test is the one of the boss key.
    /// </summary>
    /// <returns>
    /// The settings.
    /// </returns>
    private static AppSettings Settled()
    {
        var settings = new AppSettings();
        settings.Normalize();
        return settings;
    }
}
