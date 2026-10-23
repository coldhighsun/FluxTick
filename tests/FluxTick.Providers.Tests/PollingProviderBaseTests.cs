using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the shared polling loop.
/// </summary>
public class PollingProviderBaseTests
{
    /// <summary>
    /// The interval of the test provider.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The test instrument.
    /// </summary>
    private static readonly Asset Coin = new("test", "A", "A", AssetClass.Equity);

    /// <summary>
    /// Verifies an exception escaping a cycle is a failed cycle, not the end of polling: the provider reports the
    /// problem, keeps polling, and recovers.
    /// </summary>
    [Fact]
    public async Task UnexpectedException_IsAFailedCycleAndPollingContinues()
    {
        var time = new FakeTimeProvider();
        await using var provider = new TestProvider(time, calls => calls == 1 ? throw new InvalidOperationException("surprise") : Ok());

        await provider.SetSubscriptionsAsync([Coin]);
        await TestWait.EventuallyAsync(() => provider.Status is { State: ProviderConnectionState.Reconnecting, Message: "surprise" });

        await AdvanceUntilAsync(time, TimeSpan.FromSeconds(10), () => provider.Status.State == ProviderConnectionState.Connected);
        provider.Calls.Should().BeGreaterThanOrEqualTo(2);
    }

    /// <summary>
    /// Verifies repeated failed cycles end in the Failed state.
    /// </summary>
    [Fact]
    public async Task RepeatedFailures_ReportFailed()
    {
        var time = new FakeTimeProvider();
        await using var provider = new TestProvider(time, _ => new PollOutcome(0, 1, "down", BackOff: true));

        await provider.SetSubscriptionsAsync([Coin]);

        await AdvanceUntilAsync(time, TimeSpan.FromSeconds(30), () => provider.Status.State == ProviderConnectionState.Failed);
        provider.Status.Message.Should().Be("down");
    }

    /// <summary>
    /// Verifies an instrument added while polling is fetched at once, without waiting for the next tick.
    /// </summary>
    [Fact]
    public async Task AddedInstrument_IsPolledImmediately()
    {
        var time = new FakeTimeProvider();
        await using var provider = new TestProvider(time, _ => Ok());
        await provider.SetSubscriptionsAsync([Coin]);
        await TestWait.EventuallyAsync(() => provider.Calls == 1);

        await provider.SetSubscriptionsAsync([Coin, new Asset("test", "B", "B", AssetClass.Equity)]);

        await TestWait.EventuallyAsync(() => provider.Calls == 2);
        provider.LastAssets.Select(a => a.Symbol).Should().Equal("B");
    }

    /// <summary>
    /// Verifies removing every instrument stops polling and goes idle.
    /// </summary>
    [Fact]
    public async Task EmptySubscriptions_GoIdle()
    {
        var time = new FakeTimeProvider();
        await using var provider = new TestProvider(time, _ => Ok());
        await provider.SetSubscriptionsAsync([Coin]);
        await TestWait.EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);

        await provider.SetSubscriptionsAsync([]);

        provider.Status.State.Should().Be(ProviderConnectionState.Idle);
    }

    /// <summary>
    /// Verifies an immediate poll that finishes after every instrument was removed does not replace the idle status.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_ImmediatePollFinishesAfterStop_KeepsIdleStatus()
    {
        var time = new FakeTimeProvider();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var provider = new TestProvider(time, _ => Ok(), call => call == 2 ? release.Task : Task.CompletedTask);
        await provider.SetSubscriptionsAsync([Coin]);
        await TestWait.EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
        var added = new Asset("test", "B", "B", AssetClass.Equity);
        await provider.SetSubscriptionsAsync([Coin, added]);
        await TestWait.EventuallyAsync(() => provider.Calls == 2);

        await provider.SetSubscriptionsAsync([]);
        release.SetResult();
        await TestWait.EventuallyAsync(() => provider.Completed == 2);

        // The status is reported right after the cycle returns.
        await Task.Delay(200);
        provider.Status.State.Should().Be(ProviderConnectionState.Idle);
    }

    /// <summary>
    /// Verifies the immediate poll runs on the loop's token: it stays usable after the loop was stopped and its source
    /// released, and it reports the cancellation.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_AddedInstrument_PollsOnTheLoopToken()
    {
        var time = new FakeTimeProvider();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var provider = new TestProvider(time, _ => Ok(), call => call == 2 ? release.Task : Task.CompletedTask);
        await provider.SetSubscriptionsAsync([Coin]);
        await TestWait.EventuallyAsync(() => provider.Calls == 1);
        await provider.SetSubscriptionsAsync([Coin, new Asset("test", "B", "B", AssetClass.Equity)]);
        await TestWait.EventuallyAsync(() => provider.Calls == 2);
        var token = provider.LastToken;
        token.IsCancellationRequested.Should().BeFalse();

        await provider.SetSubscriptionsAsync([]);

        token.IsCancellationRequested.Should().BeTrue("stopping the loop cancels the poll that belongs to it");
        release.SetResult();
    }

    /// <summary>
    /// Creates a successful outcome.
    /// </summary>
    /// <returns>
    /// The outcome.
    /// </returns>
    private static PollOutcome Ok() => new(1, 0, null, BackOff: false);

    /// <summary>
    /// Advances a fake clock in steps until a condition holds. Unlike a single advance after a fixed sleep, this does
    /// not depend on the polling loop having registered its timer already.
    /// </summary>
    /// <param name="time">
    /// The fake clock.
    /// </param>
    /// <param name="step">
    /// How far to advance each time.
    /// </param>
    /// <param name="condition">
    /// The condition to wait for.
    /// </param>
    /// <returns>
    /// A task that completes when the condition holds.
    /// </returns>
    private static Task AdvanceUntilAsync(FakeTimeProvider time, TimeSpan step, Func<bool> condition) =>
        TestWait.EventuallyAsync(() =>
        {
            if (condition())
            {
                return true;
            }

            time.Advance(step);
            return false;
        });

    /// <summary>
    /// A polling provider whose cycles are scripted.
    /// </summary>
    /// <param name="time">
    /// The fake clock.
    /// </param>
    /// <param name="script">
    /// Produces the outcome of the n-th cycle (1-based), or throws.
    /// </param>
    /// <param name="gate">
    /// Holds the n-th cycle (1-based) back until the task it returns completes, or <see langword="null"/>.
    /// </param>
    private sealed class TestProvider(FakeTimeProvider time, Func<int, PollOutcome> script, Func<int, Task>? gate = null)
        : PollingProviderBase(
            new ProviderDescriptor("test", "Test", "A test source.", [AssetClass.Equity], ProviderUpdateMode.Polling),
            Interval,
            time,
            NullLogger.Instance)
    {
        /// <summary>
        /// The number of cycles started.
        /// </summary>
        private int _calls;

        /// <summary>
        /// Gets the number of cycles started.
        /// </summary>
        public int Calls => Volatile.Read(ref _calls);

        /// <summary>
        /// The number of cycles whose script returned.
        /// </summary>
        private int _completed;

        /// <summary>
        /// Gets the number of cycles whose script returned.
        /// </summary>
        public int Completed => Volatile.Read(ref _completed);

        /// <summary>
        /// Gets the instruments of the latest cycle.
        /// </summary>
        public IReadOnlyCollection<Asset> LastAssets { get; private set; } = [];

        /// <summary>
        /// Gets the token of the latest cycle.
        /// </summary>
        public CancellationToken LastToken { get; private set; }

        /// <inheritdoc />
        protected override async Task<PollOutcome> PollOnceAsync(IReadOnlyCollection<Asset> assets, CancellationToken cancellationToken)
        {
            LastAssets = assets;
            LastToken = cancellationToken;
            var call = Interlocked.Increment(ref _calls);
            if (gate is not null)
            {
                await gate(call);
            }

            var outcome = script(call);
            Interlocked.Increment(ref _completed);
            return outcome;
        }
    }
}
