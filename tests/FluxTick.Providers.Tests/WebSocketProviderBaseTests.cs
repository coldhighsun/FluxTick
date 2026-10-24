using System.Globalization;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests the shared WebSocket loop against a loopback server.
/// </summary>
public class WebSocketProviderBaseTests
{
    /// <summary>
    /// Verifies a server that accepts connections and hangs up at once keeps lengthening the wait between attempts.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_ServerHangsUpImmediately_KeepsBackoffGrowing()
    {
        await using var server = LoopbackServer.Start();
        server.CloseOnConnect = true;
        var time = new FakeTimeProvider();
        await using var provider = new TestProvider(server.Uri, time, TimeSpan.FromSeconds(10));

        await provider.SetSubscriptionsAsync([Coin("A")]);
        await TestWait.EventuallyAsync(() => server.ConnectionCount == 1);
        var firstWait = await AdvanceUntilConnectionsAsync(server, time, 2);
        var secondWait = await AdvanceUntilConnectionsAsync(server, time, 3);

        firstWait.Should().BeLessThan(TimeSpan.FromSeconds(16), "the first retry is due after 10 to 10.5 seconds");
        secondWait.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(20), "the second retry waits twice as long");
    }

    /// <summary>
    /// Verifies a connection that stayed up long enough starts the next attempt with the initial wait again.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_ConnectionStayedUp_ResetsBackoff()
    {
        await using var server = LoopbackServer.Start();
        var time = new FakeTimeProvider();
        await using var provider = new TestProvider(server.Uri, time, TimeSpan.FromSeconds(10));
        await provider.SetSubscriptionsAsync([Coin("A")]);
        await server.WaitForMessageCountAsync("sub:A", 1);

        // A connection that ends at once doubles the next wait.
        server.DropCurrentConnection();
        await AdvanceUntilConnectionsAsync(server, time, 2);
        await server.WaitForMessageCountAsync("sub:A", 2);

        // A connection that outlived the healthy uptime does not.
        time.Advance(TimeSpan.FromSeconds(11));
        server.DropCurrentConnection();
        var wait = await AdvanceUntilConnectionsAsync(server, time, 3);

        wait.Should().BeLessThan(TimeSpan.FromSeconds(16), "the wait started over at 10 seconds instead of doubling to 20");
    }

    /// <summary>
    /// Verifies a connection that answers with a frame and then closes does not count as healthy.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_ConnectionOnlyAnswered_KeepsBackoffGrowing()
    {
        await using var server = LoopbackServer.Start();
        var time = new FakeTimeProvider();
        await using var provider = new TestProvider(server.Uri, time, TimeSpan.FromSeconds(10));
        await provider.SetSubscriptionsAsync([Coin("A")]);
        await server.WaitForMessageCountAsync("sub:A", 1);
        await server.SendAsync("price:A:1");
        await ReadQuoteAsync(provider);
        server.DropCurrentConnection();
        await AdvanceUntilConnectionsAsync(server, time, 2);
        await server.WaitForMessageCountAsync("sub:A", 2);
        await server.SendAsync("price:A:2");
        await ReadQuoteAsync(provider);

        server.DropCurrentConnection();
        var wait = await AdvanceUntilConnectionsAsync(server, time, 3);

        wait.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(20), "a frame alone does not make the connection healthy");
    }

    /// <summary>
    /// Verifies a send that exceeds its timeout fails the connection, so subscription changes are not blocked by a
    /// stalled socket, and the provider keeps trying to reconnect.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_SendTimesOut_FailsTheConnection()
    {
        await using var server = LoopbackServer.Start();
        await using var provider = new TestProvider(server.Uri, sendTimeout: TimeSpan.Zero);

        await provider.SetSubscriptionsAsync([Coin("A")]);
        await TestWait.EventuallyAsync(() => server.ConnectionCount >= 2);

        server.Count("sub:A").Should().Be(0, "no message could be sent within the timeout");
        await TestWait.EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Reconnecting);
    }

    /// <summary>
    /// Verifies a caller that cancels its token while a subscription change is being sent neither fails the call nor
    /// costs the connection: the change is delivered on the open socket.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_CallerCancelsWhileSending_StillSubscribesOnTheOpenSocket()
    {
        await using var server = LoopbackServer.Start();
        await using var provider = new TestProvider(server.Uri);
        await provider.SetSubscriptionsAsync([Coin("A")]);
        await server.WaitForMessageCountAsync("sub:A", 1);
        using var caller = new CancellationTokenSource();
        provider.CancelCallerOnNextSubscribe(caller);

        await provider.SetSubscriptionsAsync([Coin("A"), Coin("C")], caller.Token);

        caller.IsCancellationRequested.Should().BeTrue();
        await server.WaitForMessageAsync("sub:C");
        server.ConnectionCount.Should().Be(1, "the connection must survive the caller's cancellation");
    }

    /// <summary>
    /// Verifies an infinite send timeout means no timeout: sends are not cancelled.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_InfiniteSendTimeout_SendsNormally()
    {
        await using var server = LoopbackServer.Start();
        await using var provider = new TestProvider(server.Uri, sendTimeout: Timeout.InfiniteTimeSpan);

        await provider.SetSubscriptionsAsync([Coin("A")]);
        await server.WaitForMessageAsync("sub:A");
        await provider.SetSubscriptionsAsync([Coin("A"), Coin("C")]);
        await server.WaitForMessageAsync("sub:C");

        server.ConnectionCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies a send that blocks because the peer does not read is cut off by a positive timeout, and the provider
    /// reconnects.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_SendBlocksLongerThanTimeout_ReconnectsAfterTheTimeout()
    {
        await using var server = LoopbackServer.Start();
        server.StallOnConnect = true;
        await using var provider = new TestProvider(server.Uri, sendTimeout: TimeSpan.FromMilliseconds(300));
        provider.SendHugeNextSubscribe();

        await provider.SetSubscriptionsAsync([Coin("A")]);

        await TestWait.EventuallyAsync(() => server.ConnectionCount >= 2);
    }

    /// <summary>
    /// Verifies a caller that stops waiting for a subscription change while a connect holds the state lock gets the
    /// cancellation, while the change is not lost and the connection is not disturbed: once the lock frees up, the
    /// difference is sent on the open socket.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_CallerCancelsWhileWaitingForTheLock_SendsTheChangeOnTheSameConnection()
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
        await server.WaitForMessageAsync("sub:A");
        await server.WaitForMessageAsync("sub:C");
        server.ConnectionCount.Should().Be(1, "the connection must not be dropped for a cancelled caller");
        server.Count("sub:C").Should().Be(1);
    }

    /// <summary>
    /// Verifies messages that cannot be built do not leave the instrument unsubscribed on an open socket: the call
    /// still completes, and the provider reconnects and subscribes everything.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_BuildingMessagesThrows_ReconnectsAndSubscribesEverything()
    {
        await using var server = LoopbackServer.Start();
        await using var provider = new TestProvider(server.Uri);
        await provider.SetSubscriptionsAsync([Coin("A")]);
        await server.WaitForMessageAsync("sub:A");

        provider.ThrowOnNextSubscribe();
        await provider.SetSubscriptionsAsync([Coin("A"), Coin("C")]);

        await server.WaitForMessageAsync("sub:A,C");
        server.ConnectionCount.Should().BeGreaterThanOrEqualTo(2);
    }

    /// <summary>
    /// Verifies the same repair for a change that a cancelled caller handed to the detached task.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_CallerCancelsAndBuildingMessagesThrows_ReconnectsAndSubscribesEverything()
    {
        await using var server = LoopbackServer.Start();
        await using var provider = new TestProvider(server.Uri);
        using var gate = new ManualResetEventSlim();
        provider.BlockNextSubscribe(gate);
        await provider.SetSubscriptionsAsync([Coin("A")]);
        await TestWait.EventuallyAsync(() => provider.SubscribeBlocked);
        provider.ThrowOnNextSubscribe();
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var change = () => provider.SetSubscriptionsAsync([Coin("A"), Coin("C")], caller.Token);

        await change.Should().ThrowAsync<OperationCanceledException>();
        gate.Set();
        await server.WaitForMessageAsync("sub:A,C");
        server.ConnectionCount.Should().BeGreaterThanOrEqualTo(2);
    }

    /// <summary>
    /// Verifies removals that landed while a connect was sending are also sent, so the socket ends up with exactly the
    /// current subscriptions.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_CallerCancelsRemovalWhileWaitingForTheLock_SendsTheUnsubscribe()
    {
        await using var server = LoopbackServer.Start();
        await using var provider = new TestProvider(server.Uri);
        using var gate = new ManualResetEventSlim();
        provider.BlockNextSubscribe(gate);
        await provider.SetSubscriptionsAsync([Coin("A"), Coin("B")]);
        await TestWait.EventuallyAsync(() => provider.SubscribeBlocked);
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var change = () => provider.SetSubscriptionsAsync([Coin("A")], caller.Token);

        await change.Should().ThrowAsync<OperationCanceledException>();
        gate.Set();
        await server.WaitForMessageAsync("sub:A,B");
        await server.WaitForMessageAsync("unsub:B");
        server.ConnectionCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies a heartbeat that cannot be sent in time drops the connection, so the provider reconnects instead of
    /// staying open without a keep-alive.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_HeartbeatTimesOut_Reconnects()
    {
        await using var server = LoopbackServer.Start();
        await using var provider = new TestProvider(server.Uri);
        await provider.SetSubscriptionsAsync([Coin("A")]);
        await server.WaitForMessageCountAsync("sub:A", 1);

        provider.SetSendsTimeOut(true);
        await TestWait.EventuallyAsync(() => server.ConnectionCount >= 2);

        provider.SetSendsTimeOut(false);
        await server.WaitForMessageCountAsync("sub:A", 2);
    }

    /// <summary>
    /// Verifies a message above the size limit ends the connection instead of growing the buffer, and a normal message
    /// is still handled after the reconnect.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_OversizedMessage_ReconnectsInsteadOfBuffering()
    {
        await using var server = LoopbackServer.Start();
        await using var provider = new TestProvider(server.Uri, maxMessageBytes: 1024);
        await provider.SetSubscriptionsAsync([Coin("A")]);
        await server.WaitForMessageCountAsync("sub:A", 1);

        await server.SendAsync(new string('x', 4096));
        await server.WaitForMessageCountAsync("sub:A", 2);
        await server.SendAsync("price:A:3");

        var quote = await ReadQuoteAsync(provider);
        quote.Price.Should().Be(3m);
        server.ConnectionCount.Should().BeGreaterThanOrEqualTo(2);
    }

    /// <summary>
    /// Verifies a subscription change that could not be sent drops the socket, and the reconnect subscribes the
    /// instrument.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_SendFails_ReconnectsAndSubscribesEverything()
    {
        await using var server = LoopbackServer.Start();
        await using var provider = new TestProvider(server.Uri);
        await provider.SetSubscriptionsAsync([Coin("A")]);
        await server.WaitForMessageAsync("sub:A");

        provider.FailNextSubscribe();
        await provider.SetSubscriptionsAsync([Coin("A"), Coin("C")]);

        await server.WaitForMessageAsync("sub:A,C");
        server.ConnectionCount.Should().BeGreaterThanOrEqualTo(2);
    }

    /// <summary>
    /// Verifies the full subscription set is sent on connect and pushed quotes are published.
    /// </summary>
    [Fact]
    public async Task Connect_SendsSubscriptionsAndPublishesQuotes()
    {
        await using var server = LoopbackServer.Start();
        await using var provider = new TestProvider(server.Uri);

        await provider.SetSubscriptionsAsync([Coin("B"), Coin("A")]);
        await server.WaitForMessageAsync("sub:A,B");
        await server.SendAsync("price:A:1.5");

        var quote = await ReadQuoteAsync(provider);
        quote.AssetId.Should().Be(new AssetId("test", "A"));
        quote.Price.Should().Be(1.5m);
        provider.Status.State.Should().Be(ProviderConnectionState.Connected);
    }

    /// <summary>
    /// Verifies instruments added or removed while connected are sent incrementally, and never twice.
    /// </summary>
    [Fact]
    public async Task SubscriptionChanges_AreSentWhileConnectedExactlyOnce()
    {
        await using var server = LoopbackServer.Start();
        await using var provider = new TestProvider(server.Uri);
        await provider.SetSubscriptionsAsync([Coin("A")]);
        await server.WaitForMessageAsync("sub:A");

        await provider.SetSubscriptionsAsync([Coin("A"), Coin("C")]);
        await server.WaitForMessageAsync("sub:C");
        await provider.SetSubscriptionsAsync([Coin("C")]);
        await server.WaitForMessageAsync("unsub:A");

        server.Count("sub:A").Should().Be(1, "A was subscribed by the connect and must not be sent again");
        server.Count("sub:C").Should().Be(1);
        server.Count("unsub:A").Should().Be(1);
    }

    /// <summary>
    /// Verifies a message that makes the handler throw costs only that message, not the connection.
    /// </summary>
    [Fact]
    public async Task HandlerException_DropsTheMessageAndKeepsTheConnection()
    {
        await using var server = LoopbackServer.Start();
        await using var provider = new TestProvider(server.Uri);
        await provider.SetSubscriptionsAsync([Coin("A")]);
        await server.WaitForMessageAsync("sub:A");

        await server.SendAsync("boom");
        await server.SendAsync("price:A:2");

        var quote = await ReadQuoteAsync(provider);
        quote.Price.Should().Be(2m);
        server.ConnectionCount.Should().Be(1);
        provider.Status.State.Should().Be(ProviderConnectionState.Connected);
    }

    /// <summary>
    /// Verifies the keep-alive message is sent repeatedly.
    /// </summary>
    [Fact]
    public async Task Heartbeat_IsSentRepeatedly()
    {
        await using var server = LoopbackServer.Start();
        await using var provider = new TestProvider(server.Uri);
        await provider.SetSubscriptionsAsync([Coin("A")]);

        await server.WaitForMessageAsync("hb");
        await server.WaitForMessageCountAsync("hb", 2);
    }

    /// <summary>
    /// Verifies a dropped connection is re-established and the subscriptions are sent again.
    /// </summary>
    [Fact]
    public async Task DroppedConnection_ReconnectsAndResubscribes()
    {
        await using var server = LoopbackServer.Start();
        await using var provider = new TestProvider(server.Uri);
        await provider.SetSubscriptionsAsync([Coin("A")]);
        await server.WaitForMessageCountAsync("sub:A", 1);

        server.DropCurrentConnection();

        await server.WaitForMessageCountAsync("sub:A", 2);
        server.ConnectionCount.Should().BeGreaterThanOrEqualTo(2);
    }

    /// <summary>
    /// Verifies removing every subscription goes idle and stops the connection loop.
    /// </summary>
    [Fact]
    public async Task EmptySubscriptions_GoIdle()
    {
        await using var server = LoopbackServer.Start();
        await using var provider = new TestProvider(server.Uri);
        await provider.SetSubscriptionsAsync([Coin("A")]);
        await server.WaitForMessageAsync("sub:A");

        await provider.SetSubscriptionsAsync([]);

        provider.Status.State.Should().Be(ProviderConnectionState.Idle);
    }

    /// <summary>
    /// Verifies the connection is opened again and the instrument subscribed again when instruments come back after an
    /// empty set, which is what the app does when somebody looks at the data again.
    /// </summary>
    [Fact]
    public async Task SubscriptionsAfterEmptySet_ConnectAndSubscribeAgain()
    {
        await using var server = LoopbackServer.Start();
        await using var provider = new TestProvider(server.Uri);
        await provider.SetSubscriptionsAsync([Coin("A")]);
        await server.WaitForMessageCountAsync("sub:A", 1);
        await provider.SetSubscriptionsAsync([]);

        await provider.SetSubscriptionsAsync([Coin("A")]);

        await server.WaitForMessageCountAsync("sub:A", 2);
        await TestWait.EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
    }

    /// <summary>
    /// Verifies an unreachable server reports a reconnecting state rather than throwing.
    /// </summary>
    [Fact]
    public async Task UnreachableServer_ReportsReconnecting()
    {
        await using var provider = new TestProvider(new Uri($"ws://127.0.0.1:{TestWait.FreePort()}/"));

        await provider.SetSubscriptionsAsync([Coin("A")]);

        await TestWait.EventuallyAsync(() => provider.Status is { State: ProviderConnectionState.Reconnecting, Message: not "Connecting" });
    }

    /// <summary>
    /// Verifies an address the socket cannot use is reported and retried, not left to kill the connection loop.
    /// </summary>
    [Fact]
    public async Task UnsupportedAddress_ReportsReconnectingInsteadOfDying()
    {
        await using var provider = new TestProvider(new Uri("ftp://example.invalid/feed"));

        await provider.SetSubscriptionsAsync([Coin("A")]);

        await TestWait.EventuallyAsync(() => provider.Status is { State: ProviderConnectionState.Reconnecting, Message: { Length: > 0 } message } && message != "Connecting");

        // Disposal must not rethrow a stored failure of the loop.
        await provider.DisposeAsync();
    }


    /// <summary>
    /// Advances the fake clock in one-second steps until the server has accepted a number of connections, and returns
    /// how much fake time that took. Stepping does not depend on the loop having registered its retry timer already, and
    /// the elapsed time shows how long the loop chose to wait.
    /// </summary>
    /// <param name="server">
    /// The server that counts connections.
    /// </param>
    /// <param name="time">
    /// The fake clock the provider waits on.
    /// </param>
    /// <param name="connections">
    /// The connection count to wait for.
    /// </param>
    /// <returns>
    /// The fake time that passed.
    /// </returns>
    private static async Task<TimeSpan> AdvanceUntilConnectionsAsync(LoopbackServer server, FakeTimeProvider time, int connections)
    {
        var start = time.GetUtcNow();
        await TestWait.EventuallyAsync(() =>
        {
            if (server.ConnectionCount >= connections)
            {
                return true;
            }

            time.Advance(TimeSpan.FromSeconds(1));
            return false;
        });

        return time.GetUtcNow() - start;
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
    /// Reads the next published quote.
    /// </summary>
    /// <param name="provider">
    /// The provider.
    /// </param>
    /// <returns>
    /// The quote.
    /// </returns>
    private static async Task<Quote> ReadQuoteAsync(IMarketDataProvider provider)
    {
        using var cts = new CancellationTokenSource(TestWait.Patience);
        await foreach (var quote in provider.ReadQuotesAsync(cts.Token))
        {
            return quote;
        }

        throw new InvalidOperationException("The quote stream ended.");
    }

    /// <summary>
    /// A provider speaking a trivial text protocol: <c>sub:A,B</c>, <c>unsub:A</c>, <c>hb</c> and <c>price:SYMBOL:VALUE</c>;
    /// the message <c>boom</c> makes its handler throw.
    /// </summary>
    private sealed class TestProvider(Uri uri, TimeProvider? time = null, TimeSpan? initialBackoff = null, int? maxMessageBytes = null, TimeSpan? sendTimeout = null)
        : WebSocketProviderBase(
            new ProviderDescriptor("test", "Test", "A test source.", [AssetClass.Crypto], ProviderUpdateMode.Streaming),
            uri,
            time ?? TimeProvider.System,
            NullLogger.Instance)
    {
        /// <summary>
        /// Whether the next subscribe message list fails while it is being sent.
        /// </summary>
        private int _failNextSubscribe;

        /// <summary>
        /// Whether every send times out at once, like a stalled socket.
        /// </summary>
        private int _sendsTimeOut;

        /// <summary>
        /// The source to cancel when the next subscribe messages are built.
        /// </summary>
        private CancellationTokenSource? _cancelOnSubscribe;

        /// <summary>
        /// The gate that holds the next subscribe messages back while the provider owns its state lock.
        /// </summary>
        private ManualResetEventSlim? _blockNextSubscribe;

        /// <summary>
        /// Whether a subscribe is waiting at the gate.
        /// </summary>
        private int _subscribeBlocked;

        /// <summary>
        /// Whether the next subscribe message is too large to be buffered by the peer.
        /// </summary>
        private int _hugeNextSubscribe;

        /// <summary>
        /// Whether building the next subscribe messages throws.
        /// </summary>
        private int _throwNextSubscribe;

        /// <inheritdoc />
        protected override TimeSpan InitialBackoff => initialBackoff ?? TimeSpan.FromMilliseconds(30);

        /// <inheritdoc />
        protected override int MaxMessageBytes => maxMessageBytes ?? base.MaxMessageBytes;

        /// <inheritdoc />
        protected override TimeSpan SendTimeout =>
            Volatile.Read(ref _sendsTimeOut) == 1 ? TimeSpan.Zero : sendTimeout ?? base.SendTimeout;

        /// <inheritdoc />
        protected override TimeSpan? HeartbeatInterval => TimeSpan.FromMilliseconds(50);

        /// <inheritdoc />
        protected override string? HeartbeatMessage => "hb";

        /// <summary>
        /// Makes the next subscribe message list throw while it is enumerated, like a send that is cancelled halfway.
        /// </summary>
        public void FailNextSubscribe() => Volatile.Write(ref _failNextSubscribe, 1);

        /// <summary>
        /// Cancels a source, standing for the caller's token, when the next subscribe messages are built, which is while
        /// the provider is already sending them.
        /// </summary>
        /// <param name="source">
        /// The source to cancel.
        /// </param>
        public void CancelCallerOnNextSubscribe(CancellationTokenSource source) => Volatile.Write(ref _cancelOnSubscribe, source);

        /// <summary>
        /// Gets a value indicating whether a subscribe is waiting at the gate set by <see cref="BlockNextSubscribe"/>.
        /// </summary>
        public bool SubscribeBlocked => Volatile.Read(ref _subscribeBlocked) == 1;

        /// <summary>
        /// Holds the next subscribe messages back, while the provider owns its state lock, until the gate is set.
        /// </summary>
        /// <param name="gate">
        /// The gate to wait at.
        /// </param>
        public void BlockNextSubscribe(ManualResetEventSlim gate) => Volatile.Write(ref _blockNextSubscribe, gate);

        /// <summary>
        /// Makes building the next subscribe messages throw, like a derived class with a bug.
        /// </summary>
        public void ThrowOnNextSubscribe() => Volatile.Write(ref _throwNextSubscribe, 1);

        /// <summary>
        /// Makes the next subscribe message far larger than a peer that does not read can buffer.
        /// </summary>
        public void SendHugeNextSubscribe() => Volatile.Write(ref _hugeNextSubscribe, 1);

        /// <summary>
        /// Switches the timing out of every send on or off.
        /// </summary>
        /// <param name="enabled">
        /// <see langword="true"/> to make all sends time out.
        /// </param>
        public void SetSendsTimeOut(bool enabled) => Volatile.Write(ref _sendsTimeOut, enabled ? 1 : 0);

        /// <inheritdoc />
        protected override IEnumerable<string> BuildSubscribeMessages(IReadOnlyCollection<Asset> assets)
        {
            Interlocked.Exchange(ref _cancelOnSubscribe, null)?.Cancel();
            if (Interlocked.Exchange(ref _throwNextSubscribe, 0) == 1)
            {
                throw new InvalidOperationException("The messages cannot be built.");
            }

            if (Interlocked.Exchange(ref _blockNextSubscribe, null) is { } gate)
            {
                Volatile.Write(ref _subscribeBlocked, 1);
                gate.Wait(TestWait.Patience);
            }

            if (Interlocked.Exchange(ref _hugeNextSubscribe, 0) == 1)
            {
                return [new string('x', 16_000_000)];
            }

            return Interlocked.Exchange(ref _failNextSubscribe, 0) == 1
                ? Failing()
                : [$"sub:{string.Join(',', assets.Select(a => a.Symbol).Order())}"];
        }

        /// <inheritdoc />
        protected override IEnumerable<string> BuildUnsubscribeMessages(IReadOnlyCollection<Asset> assets) =>
            [$"unsub:{string.Join(',', assets.Select(a => a.Symbol).Order())}"];

        /// <inheritdoc />
        protected override void HandleMessage(string text)
        {
            if (text == "boom")
            {
                throw new InvalidOperationException("The handler cannot digest this message.");
            }

            var parts = text.Split(':');
            if (parts is ["price", var symbol, var value])
            {
                Publish(new Quote(
                    new AssetId("test", symbol),
                    decimal.Parse(value, CultureInfo.InvariantCulture),
                    null,
                    TimeProvider.GetUtcNow()));
            }
        }

        /// <summary>
        /// Produces a message list that fails with a cancellation as soon as it is enumerated.
        /// </summary>
        /// <returns>
        /// A list that never yields.
        /// </returns>
        private static IEnumerable<string> Failing()
        {
            throw new OperationCanceledException();
#pragma warning disable CS0162 // The yield makes this an iterator, so the exception surfaces when it is enumerated.
            yield break;
#pragma warning restore CS0162
        }
    }
}
