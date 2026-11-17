using FluxTick.Core.Application;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests of <see cref="SettingsManager.WhenIdleAsync"/>.
/// </summary>
public sealed class SettingsManagerIdleTests
{
    /// <summary>
    /// The longest a test waits for something that should happen.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Verifies the wait ends at once when no change is being saved.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task WhenIdleAsync_NothingBeingSaved_CompletesAtOnce()
    {
        var manager = await CreateAsync(new SlowStore());

        await manager.WhenIdleAsync().WaitAsync(Patience);
    }

    /// <summary>
    /// Verifies the wait covers a change whose caller does not wait for it, and ends once that change is saved.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task WhenIdleAsync_ChangeBeingSaved_WaitsUntilItIsSaved()
    {
        var store = new SlowStore();
        var manager = await CreateAsync(store);
        store.Release = new TaskCompletionSource();
        _ = manager.UpdateAsync(s => s.Updates.AutoCheck = false);

        var waiting = manager.WhenIdleAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        waiting.IsCompleted.Should().BeFalse();

        store.Release.SetResult();
        await waiting.WaitAsync(Patience);

        store.Saved.Should().NotBeNull();
        store.Saved!.Updates.AutoCheck.Should().BeFalse();
    }

    /// <summary>
    /// Creates a settings manager over a store and loads it.
    /// </summary>
    /// <param name="store">
    /// The store.
    /// </param>
    /// <returns>
    /// The settings manager.
    /// </returns>
    private static async Task<SettingsManager> CreateAsync(SlowStore store)
    {
        var manager = new SettingsManager(store, NullLogger<SettingsManager>.Instance);
        await manager.LoadAsync(new AppSettings());
        return manager;
    }

    /// <summary>
    /// A store whose saves can be held back.
    /// </summary>
    private sealed class SlowStore : ISettingsStore
    {
        /// <summary>
        /// Gets or sets what every save waits for, or <see langword="null"/> to save at once.
        /// </summary>
        public TaskCompletionSource? Release { get; set; }

        /// <summary>
        /// Gets the settings that were saved last.
        /// </summary>
        public AppSettings? Saved { get; private set; }

        /// <inheritdoc />
        public Task<AppSettings?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<AppSettings?>(new AppSettings());

        /// <inheritdoc />
        public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            if (Release is { } release)
            {
                await release.Task;
            }

            Saved = settings.Clone();
        }
    }
}
