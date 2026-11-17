using FluxTick.Core.Application;
using FluxTick.Core.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for who can read the settings file, which may hold secrets such as API keys.
/// </summary>
public sealed class SettingsFilePermissionTests : IDisposable
{
    /// <summary>
    /// The folder of the settings file of one test.
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"fluxtick-tests-{Guid.NewGuid():N}");

    /// <summary>
    /// Gets a value indicating whether the tests can run: Windows has no Unix file modes.
    /// </summary>
    public static bool IsNotWindows => !OperatingSystem.IsWindows();

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies the file can be read and written by its owner only.
    /// </summary>
    [Fact(SkipUnless = nameof(IsNotWindows), SkipType = typeof(SettingsFilePermissionTests), Skip = "Unix only")]
    public async Task SaveAsync_OnUnix_LimitsTheFileToItsOwner()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        await store.SaveAsync(new AppSettings());
        await store.SaveAsync(new AppSettings());

        File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
