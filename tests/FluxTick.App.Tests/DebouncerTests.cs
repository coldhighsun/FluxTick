using AwesomeAssertions;
using FluxTick.App.Services;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests of <see cref="Debouncer"/>.
/// </summary>
public sealed class DebouncerTests
{
    /// <summary>
    /// The quiet period used by the tests.
    /// </summary>
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Verifies only the latest of several actions runs, after the quiet period.
    /// </summary>
    [Fact]
    public async Task Run_Repeated_RunsOnlyLatestAfterDelay()
    {
        var time = new FakeTimeProvider();
        var debouncer = new Debouncer(Delay, time);
        var ran = new List<int>();

        debouncer.Run(() => { ran.Add(1); return Task.CompletedTask; });
        debouncer.Run(() => { ran.Add(2); return Task.CompletedTask; });
        ran.Should().BeEmpty();

        time.Advance(Delay);
        await Harness.EventuallyAsync(() => ran.Count == 1);

        ran.Should().Equal(2);
    }

    /// <summary>
    /// Verifies a flush runs the waiting action at once, and only once.
    /// </summary>
    [Fact]
    public async Task FlushAsync_ActionWaiting_RunsNowAndNotAgain()
    {
        var time = new FakeTimeProvider();
        var debouncer = new Debouncer(Delay, time);
        var runs = 0;
        debouncer.Run(() => { runs++; return Task.CompletedTask; });

        await debouncer.FlushAsync();
        time.Advance(Delay);
        await Task.Delay(50);

        runs.Should().Be(1);
    }

    /// <summary>
    /// Verifies a flush with nothing waiting does nothing.
    /// </summary>
    [Fact]
    public async Task FlushAsync_NothingWaiting_DoesNothing()
    {
        var debouncer = new Debouncer(Delay, new FakeTimeProvider());

        await debouncer.FlushAsync();
    }

    /// <summary>
    /// Verifies a flush waits for an action that already started and is still running.
    /// </summary>
    [Fact]
    public async Task FlushAsync_ActionInFlight_WaitsForIt()
    {
        var time = new FakeTimeProvider();
        var debouncer = new Debouncer(Delay, time);
        var started = false;
        var gate = new TaskCompletionSource();
        debouncer.Run(async () =>
        {
            started = true;
            await gate.Task;
        });
        time.Advance(Delay);
        await Harness.EventuallyAsync(() => started);

        var flush = debouncer.FlushAsync();
        await Task.Delay(50);
        flush.IsCompleted.Should().BeFalse();

        gate.SetResult();
        await flush.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies an action that already ran is not run again by a flush.
    /// </summary>
    [Fact]
    public async Task FlushAsync_AfterActionRan_DoesNotRunItAgain()
    {
        var time = new FakeTimeProvider();
        var debouncer = new Debouncer(Delay, time);
        var runs = 0;
        debouncer.Run(() => { runs++; return Task.CompletedTask; });
        time.Advance(Delay);
        await Harness.EventuallyAsync(() => runs == 1);

        await debouncer.FlushAsync();

        runs.Should().Be(1);
    }
}
