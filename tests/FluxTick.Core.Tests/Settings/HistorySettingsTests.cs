using FluxTick.Core.Application;
using FluxTick.Core.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for how the stored price history settings are kept.
/// </summary>
public sealed class HistorySettingsTests : IDisposable
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
    /// Verifies that the defaults are on and a year, also for a file that has no history section.
    /// </summary>
    [Fact]
    public async Task LoadAsync_FileWithoutHistory_UsesDefaults()
    {
        var loaded = await LoadAsync("""{ "general": { "startMinimized": true } }""");

        loaded.History.Enabled.Should().BeTrue();
        loaded.History.RetentionDays.Should().Be(365);
        loaded.History.Retention.Should().Be(TimeSpan.FromDays(365));
    }

    /// <summary>
    /// Verifies that values in the file are read.
    /// </summary>
    [Fact]
    public async Task LoadAsync_HistorySection_IsRead()
    {
        var loaded = await LoadAsync("""{ "history": { "enabled": false, "retentionDays": 30 } }""");

        loaded.History.Enabled.Should().BeFalse();
        loaded.History.RetentionDays.Should().Be(30);
    }

    /// <summary>
    /// Verifies that a null value falls back to the default.
    /// </summary>
    [Fact]
    public async Task LoadAsync_NullValues_UseDefaults()
    {
        var loaded = await LoadAsync("""{ "history": { "enabled": null, "retentionDays": null } }""");

        loaded.History.Enabled.Should().BeTrue();
        loaded.History.RetentionDays.Should().Be(365);
    }

    /// <summary>
    /// Verifies that a retention outside the allowed range is brought back into it, and counts as a change.
    /// </summary>
    /// <param name="days">
    /// The retention that was set.
    /// </param>
    /// <param name="expected">
    /// The retention that remains.
    /// </param>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(3650, 3650)]
    [InlineData(100000, 3650)]
    public void Normalize_RetentionOutsideRange_IsClamped(int days, int expected)
    {
        var settings = new AppSettings();

        // A new settings object still lacks its default group, which Normalize adds and reports as a change.
        settings.Normalize();
        settings.History.RetentionDays = days;

        var changed = settings.Normalize();

        settings.History.RetentionDays.Should().Be(expected);
        changed.Should().Be(days != expected);
    }

    /// <summary>
    /// Verifies that the history settings survive a copy.
    /// </summary>
    [Fact]
    public void Clone_KeepsHistorySettings()
    {
        var settings = new AppSettings();
        settings.History.Enabled = false;
        settings.History.RetentionDays = 7;

        var copy = settings.Clone();

        copy.History.Enabled.Should().BeFalse();
        copy.History.RetentionDays.Should().Be(7);
    }

    /// <summary>
    /// Writes a settings file and loads it.
    /// </summary>
    /// <param name="json">
    /// The content of the file.
    /// </param>
    /// <returns>
    /// The loaded settings.
    /// </returns>
    private async Task<AppSettings> LoadAsync(string json)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, json);
        var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);
        return (await store.LoadAsync())!;
    }
}
