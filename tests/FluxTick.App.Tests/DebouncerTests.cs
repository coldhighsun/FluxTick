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

    /// <summary>
    /// Verifies a flush that blocks the thread the action was scheduled on, as the exit handler does on the UI thread,
    /// does not deadlock while that action is in flight.
    /// </summary>
    [Fact]
    public async Task FlushAsync_BlockingTheSchedulingContextWhileActionInFlight_Completes()
    {
        var time = new FakeTimeProvider();
        var debouncer = new Debouncer(Delay, time);
        using var context = new SingleThreadContext();
        var started = false;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(() => debouncer.Run(async () =>
        {
            started = true;
            await gate.Task.ConfigureAwait(false);
        }));
        await Harness.EventuallyAsync(() => context.Processed == 1);
        time.Advance(Delay);
        await Harness.EventuallyAsync(() => started);

        var flushed = new TaskCompletionSource();
        context.Post(() =>
        {
            debouncer.FlushAsync().GetAwaiter().GetResult();
            flushed.SetResult();
        });
        await Task.Delay(50);
        gate.SetResult();

        (await Task.WhenAny(flushed.Task, Task.Delay(TimeSpan.FromSeconds(3)))).Should().BeSameAs(flushed.Task);
    }

    /// <summary>
    /// A synchronization context with one thread of its own that runs posted work in order, like a UI thread.
    /// </summary>
    private sealed class SingleThreadContext : SynchronizationContext, IDisposable
    {
        /// <summary>
        /// The work waiting to run.
        /// </summary>
        private readonly System.Collections.Concurrent.BlockingCollection<Action> _queue = [];

        /// <summary>
        /// The number of posted items that finished running.
        /// </summary>
        private int _processed;

        /// <summary>
        /// Initializes a new instance of the <see cref="SingleThreadContext"/> class and starts its thread.
        /// </summary>
        public SingleThreadContext()
        {
            var thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (var work in _queue.GetConsumingEnumerable())
                {
                    work();
                    Interlocked.Increment(ref _processed);
                }
            })
            {
                IsBackground = true,
            };
            thread.Start();
        }

        /// <summary>
        /// Gets the number of posted items that finished running.
        /// </summary>
        public int Processed => Volatile.Read(ref _processed);

        /// <summary>
        /// Queues work to run on the context's thread.
        /// </summary>
        /// <param name="work">
        /// The work to run.
        /// </param>
        public void Post(Action work) => _queue.Add(work);

        /// <inheritdoc />
        public override void Post(SendOrPostCallback d, object? state) => _queue.Add(() => d(state));

        /// <inheritdoc />
        public void Dispose() => _queue.CompleteAdding();
    }
}
