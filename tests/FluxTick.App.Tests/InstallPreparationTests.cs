using FluxTick.App.Services;
using FluxTick.Core.Application;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests of <see cref="InstallPreparation"/>, which saves what is pending before an update ends the app.
/// </summary>
public sealed class InstallPreparationTests
{
    /// <summary>
    /// The longest a test waits for something that should happen.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The settings store of the test.
    /// </summary>
    private readonly InMemorySettingsStore _store = new() { Stored = new AppSettings() };

    /// <summary>
    /// Verifies a geometry save that still waits for its quiet period is run by the preparation.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task Create_GeometrySaveWaiting_RunsItNow()
    {
        var debouncer = new Debouncer(TimeSpan.FromMinutes(1), new FakeTimeProvider());
        var saved = 0;
        debouncer.Run(() =>
        {
            saved++;
            return Task.CompletedTask;
        });

        await InstallPreparation.Create(debouncer, await CreateSettingsAsync())().WaitAsync(Patience);

        saved.Should().Be(1);
    }

    /// <summary>
    /// Verifies a settings write that nobody waits for is finished before the preparation completes.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task Create_SettingsWriteUnderWay_WaitsForIt()
    {
        var settings = await CreateSettingsAsync();
        var release = new TaskCompletionSource();
        _store.SaveGate = release.Task;
        _ = settings.UpdateAsync(s => s.Updates.AutoCheck = false);

        var preparing = InstallPreparation.Create(null, settings)();
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        preparing.IsCompleted.Should().BeFalse();

        release.SetResult();
        await preparing.WaitAsync(Patience);

        _store.Stored!.Updates.AutoCheck.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the preparation completes at once when nothing is pending and there is no window yet.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task Create_NothingPending_CompletesAtOnce() =>
        await InstallPreparation.Create(null, await CreateSettingsAsync())().WaitAsync(Patience);

    /// <summary>
    /// Creates a settings manager over the store of the test.
    /// </summary>
    /// <returns>
    /// The settings manager.
    /// </returns>
    private async Task<SettingsManager> CreateSettingsAsync()
    {
        var settings = new SettingsManager(_store, NullLogger<SettingsManager>.Instance);
        await settings.LoadAsync(new AppSettings());
        return settings;
    }
}
