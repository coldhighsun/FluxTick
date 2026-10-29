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
    /// Verifies polling starts again, with every instrument fetched at once, when instruments come back after an empty
    /// set, which is what the app does when somebody looks at the data again.
    /// </summary>
    [Fact]
    public async Task SubscriptionsAfterEmptySet_StartPollingAgain()
    {
        var time = new FakeTimeProvider();
        await using var provider = new TestProvider(time, _ => Ok());
        await provider.SetSubscriptionsAsync([Coin]);
        await TestWait.EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
        await provider.SetSubscriptionsAsync([]);
        var callsBefore = provider.Calls;

        await provider.SetSubscriptionsAsync([Coin]);

        await TestWait.EventuallyAsync(() => provider.Calls > callsBefore);
        await TestWait.EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
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
    /// Verifies a shut market is not polled but its last quote is repeated with the original trade time, so that a
    /// listener does not take the silence for a source that went quiet.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_ShutMarket_RepeatsTheLastQuoteWithoutPolling()
    {
        var time = new FakeTimeProvider();
        await using var provider = new HoursProvider(time);
        await provider.SetSubscriptionsAsync([Coin]);
        await TestWait.EventuallyAsync(() => provider.Published.Count == 1);
        var first = provider.Published[0];
        provider.Shut(Coin.Symbol);

        await AdvanceUntilAsync(time, Interval, () => provider.Published.Count >= 4);

        provider.Calls.Should().Be(1, "a shut market has nothing new to fetch");
        provider.Published.Should().OnlyContain(q => q == first);
    }

    /// <summary>
    /// Verifies a market that is open is polled and its quotes are not repeated.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_OpenMarket_PollsAndDoesNotRepeat()
    {
        var time = new FakeTimeProvider();
        await using var provider = new HoursProvider(time);
        await provider.SetSubscriptionsAsync([Coin]);

        await AdvanceUntilAsync(time, Interval, () => provider.Published.Count >= 3);

        // Every poll publishes a price of its own, so a repeat would show as a price seen twice.
        provider.Published.Select(q => q.Price).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Verifies an instrument that has no price yet is polled even if its market is shut, and nothing is repeated.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_ShutMarketWithoutAPrice_PollsAndRepeatsNothing()
    {
        var time = new FakeTimeProvider();
        await using var provider = new HoursProvider(time, publish: false);
        provider.Shut(Coin.Symbol);
        await provider.SetSubscriptionsAsync([Coin]);

        await AdvanceUntilAsync(time, Interval, () => provider.Calls >= 2);

        provider.Published.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the sign of life of a shut market goes on at the poll interval while the polls of the instruments that are
    /// open are held back by a service that pushes back.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_PollsOfOpenMarketsBackOff_ShutMarketKeepsRepeatingEveryInterval()
    {
        var time = new FakeTimeProvider();
        var open = new Asset("test", "O", "O", AssetClass.Equity);
        await using var provider = new HoursProvider(time);
        await provider.SetSubscriptionsAsync([Coin, open]);
        await TestWait.EventuallyAsync(() => provider.Published.Count == 2);
        provider.Shut(Coin.Symbol);
        provider.FailPolls = true;

        await AdvanceUntilAsync(time, Interval, () => provider.Published.Count(q => q.AssetId == Coin.Id) >= 9);

        provider.Calls.Should().BeLessThan(6, "the failing polls back off, while the repeats keep their pace");
    }

    /// <summary>
    /// Verifies a repeated quote is not taken for news: the quote remembered stays the newest one, so a repeat does not
    /// bring an older quote back.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_ShutMarketAfterANewerQuote_RepeatsTheNewestQuote()
    {
        var time = new FakeTimeProvider();
        await using var provider = new HoursProvider(time);
        await provider.SetSubscriptionsAsync([Coin]);
        await TestWait.EventuallyAsync(() => provider.Published.Count == 1);
        time.Advance(Interval);
        await TestWait.EventuallyAsync(() => provider.Published.Count == 2);
        var newest = provider.Published[^1];
        provider.Shut(Coin.Symbol);

        await AdvanceUntilAsync(time, Interval, () => provider.Published.Count >= 4);

        provider.Published.Skip(2).Should().OnlyContain(q => q == newest);
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

    /// <summary>
    /// A polling provider that knows its market hours and publishes one quote per instrument and cycle, with a price of
    /// its own every time.
    /// </summary>
    /// <param name="time">
    /// The fake clock.
    /// </param>
    /// <param name="publish">
    /// Whether a cycle publishes quotes.
    /// </param>
    private sealed class HoursProvider(FakeTimeProvider time, bool publish = true)
        : PollingProviderBase(
            new ProviderDescriptor("test", "Test", "A test source.", [AssetClass.Equity], ProviderUpdateMode.Polling),
            Interval,
            time,
            NullLogger.Instance),
        IMarketHoursProvider
    {
        /// <summary>
        /// The number of cycles started.
        /// </summary>
        private int _calls;

        /// <summary>
        /// The quotes published so far, which are read from other threads.
        /// </summary>
        private readonly List<Quote> _published = [];

        /// <summary>
        /// The symbols whose market is shut, which are read from other threads.
        /// </summary>
        private readonly HashSet<string> _shut = [];

        /// <summary>
        /// Gets the number of cycles started.
        /// </summary>
        public int Calls => Volatile.Read(ref _calls);

        /// <summary>
        /// Gets or sets a value indicating whether the cycles fail with a push back.
        /// </summary>
        public bool FailPolls { get; set; }

        /// <summary>
        /// Gets a copy of the quotes published so far.
        /// </summary>
        public IReadOnlyList<Quote> Published
        {
            get
            {
                lock (_published)
                {
                    return [.. _published];
                }
            }
        }

        /// <summary>
        /// Shuts the market of a symbol.
        /// </summary>
        /// <param name="symbol">
        /// The symbol.
        /// </param>
        public void Shut(string symbol)
        {
            lock (_shut)
            {
                _shut.Add(symbol);
            }
        }

        /// <inheritdoc />
        public MarketState GetMarketState(Asset asset, DateTimeOffset now, DateTimeOffset? lastTradeTime = null, TimeSpan? holidayGrace = null)
        {
            lock (_shut)
            {
                return _shut.Contains(asset.Symbol) ? MarketState.Closed : MarketState.Open;
            }
        }

        /// <inheritdoc />
        protected override Task<PollOutcome> PollOnceAsync(IReadOnlyCollection<Asset> assets, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            if (FailPolls)
            {
                return Task.FromResult(new PollOutcome(0, 1, "pushed back", BackOff: true));
            }

            if (publish)
            {
                foreach (var asset in assets)
                {
                    Publish(new Quote(asset.Id, call, null, time.GetUtcNow()));
                }
            }

            return Task.FromResult(new PollOutcome(1, 0, null, BackOff: false));
        }

        /// <inheritdoc />
        protected override void OnQuotePublished(Quote quote)
        {
            base.OnQuotePublished(quote);
            lock (_published)
            {
                _published.Add(quote);
            }
        }
    }
}
