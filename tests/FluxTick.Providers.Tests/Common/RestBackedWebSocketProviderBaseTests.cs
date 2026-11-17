using System.Collections.Concurrent;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests of <see cref="RestBackedWebSocketProviderBase"/> against a loopback server.
/// </summary>
public class RestBackedWebSocketProviderBaseTests
{
    /// <summary>
    /// Verifies a caller that stops waiting for a subscription change while a connect holds the stream's lock still
    /// gets the first price of the new instrument over REST: the change is recorded, so it is never reported again.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_CallerCancelsWhileWaitingForTheLock_StillFetchesTheFirstPrice()
    {
        await using var server = LoopbackServer.Start();
        await using var provider = new TestProvider(server.Uri);
        using var gate = new ManualResetEventSlim();
        provider.BlockNextSubscribe(gate);
        await provider.SetSubscriptionsAsync([Coin("A")]);
        await TestWait.EventuallyAsync(() => provider.SubscribeBlocked);
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var change = () => provider.SetSubscriptionsAsync([Coin("A"), Coin("C")], caller.Token);

        await change.Should().ThrowAsync<OperationCanceledException>();
        gate.Set();
        await TestWait.EventuallyAsync(() => provider.Fetched.Contains("C"));
    }

    /// <summary>
    /// Verifies a source whose stream accepts each connection and hangs up at once, while polling fails too, shows as
    /// failed and stays so: a handshake that delivers no prices does not clear the failed rounds.
    /// </summary>
    [Fact]
    public async Task Status_StreamHangsUpAndPollingFails_StaysFailed()
    {
        await using var server = LoopbackServer.Start();
        server.CloseOnConnect = true;
        await using var provider = new TestProvider(server.Uri, TimeSpan.FromMilliseconds(20)) { RestFails = true };
        var states = new System.Collections.Concurrent.ConcurrentQueue<ProviderConnectionState>();
        provider.StatusChanged += (_, status) => states.Enqueue(status.State);

        await provider.SetSubscriptionsAsync([Coin("A")]);
        await TestWait.EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Failed);
        var connections = server.ConnectionCount;
        await TestWait.EventuallyAsync(() => server.ConnectionCount >= connections + 3);

        states.SkipWhile(s => s != ProviderConnectionState.Failed).Should().NotContain(ProviderConnectionState.Reconnecting);
    }

    /// <summary>
    /// Creates a test instrument.
    /// </summary>
    /// <param name="symbol">
    /// The symbol.
    /// </param>
    /// <returns>
    /// The asset.
    /// </returns>
    private static Asset Coin(string symbol) => new("test", symbol, symbol, AssetClass.Crypto);

    /// <summary>
    /// A provider that subscribes with <c>sub:A,B</c>, records which instruments it fetched over REST, and can hold its
    /// next subscribe back while it owns the stream's lock.
    /// </summary>
    /// <param name="uri">
    /// The address of the stream.
    /// </param>
    /// <param name="fallbackInterval">
    /// How often REST is polled while the stream is down, or <see langword="null"/> for an hour.
    /// </param>
    private sealed class TestProvider(Uri uri, TimeSpan? fallbackInterval = null)
        : RestBackedWebSocketProviderBase(
            new ProviderDescriptor("test", "Test", "A test source.", [AssetClass.Crypto], ProviderUpdateMode.Streaming),
            uri,
            fallbackInterval ?? TimeSpan.FromHours(1),
            TimeProvider.System,
            NullLogger.Instance,
            TimeSpan.FromHours(1))
    {
        /// <summary>
        /// The gate that holds the next subscribe messages back while the provider owns its state lock.
        /// </summary>
        private ManualResetEventSlim? _blockNextSubscribe;

        /// <summary>
        /// Whether a subscribe is waiting at the gate.
        /// </summary>
        private int _subscribeBlocked;

        /// <summary>
        /// Gets or sets a value indicating whether every REST request fails.
        /// </summary>
        public bool RestFails { get; set; }

        /// <summary>
        /// Gets the symbols fetched over REST so far.
        /// </summary>
        public ConcurrentBag<string> Fetched { get; } = [];

        /// <summary>
        /// Gets a value indicating whether a subscribe is waiting at the gate set by <see cref="BlockNextSubscribe"/>.
        /// </summary>
        public bool SubscribeBlocked => Volatile.Read(ref _subscribeBlocked) == 1;

        /// <inheritdoc />
        protected override TimeSpan InitialBackoff => TimeSpan.FromMilliseconds(30);

        /// <summary>
        /// Holds the next subscribe messages back, while the provider owns its state lock, until the gate is set.
        /// </summary>
        /// <param name="gate">
        /// The gate to wait at.
        /// </param>
        public void BlockNextSubscribe(ManualResetEventSlim gate) => Volatile.Write(ref _blockNextSubscribe, gate);

        /// <inheritdoc />
        protected override Task<PollOutcome> FetchQuotesAsync(IReadOnlyCollection<Asset> assets, CancellationToken cancellationToken)
        {
            foreach (var asset in assets)
            {
                Fetched.Add(asset.Symbol);
            }

            return Task.FromResult(RestFails
                ? new PollOutcome(0, assets.Count, "down", BackOff: false)
                : new PollOutcome(assets.Count, 0, null, BackOff: false));
        }

        /// <inheritdoc />
        protected override IEnumerable<string> BuildSubscribeMessages(IReadOnlyCollection<Asset> assets)
        {
            if (Interlocked.Exchange(ref _blockNextSubscribe, null) is { } gate)
            {
                Volatile.Write(ref _subscribeBlocked, 1);
                gate.Wait(TestWait.Patience);
            }

            return [$"sub:{string.Join(',', assets.Select(a => a.Symbol).Order())}"];
        }

        /// <inheritdoc />
        protected override IEnumerable<string> BuildUnsubscribeMessages(IReadOnlyCollection<Asset> assets) =>
            [$"unsub:{string.Join(',', assets.Select(a => a.Symbol).Order())}"];

        /// <inheritdoc />
        protected override void HandleMessage(string text)
        {
        }
    }
}
