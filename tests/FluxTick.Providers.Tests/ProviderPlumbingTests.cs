using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the plumbing the sources share: the background loop, the backoff and publishing to the subscribed
/// instruments of a key.
/// </summary>
public class ProviderPlumbingTests
{
    /// <summary>
    /// Verifies the wait doubles up to the limit and stays there.
    /// </summary>
    [Fact]
    public void Double_WaitBelowLimit_DoublesAndStopsAtTheLimit()
    {
        var limit = TimeSpan.FromSeconds(30);

        Backoff.Double(TimeSpan.FromSeconds(5), limit).Should().Be(TimeSpan.FromSeconds(10));
        Backoff.Double(TimeSpan.FromSeconds(20), limit).Should().Be(limit);
        Backoff.Double(limit, limit).Should().Be(limit);
    }

    /// <summary>
    /// Verifies a loop that was started runs, hands its token to the body, and stops when asked.
    /// </summary>
    [Fact]
    public async Task BackgroundLoop_StartedAndStopped_RunsUntilCancelled()
    {
        var loop = new BackgroundLoop(CancellationToken.None);
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        loop.Start(async token =>
        {
            started.SetResult(token);
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            finally
            {
                ended.SetResult();
            }
        });
        var token = await started.Task.WaitAsync(TestWait.Patience);

        loop.IsRunning.Should().BeTrue();
        loop.Token.Should().Be(token);
        await loop.StopAsync();
        await ended.Task.WaitAsync(TestWait.Patience);
        loop.IsRunning.Should().BeFalse();
        token.IsCancellationRequested.Should().BeTrue();
        loop.Token.IsCancellationRequested.Should().BeTrue("work that reads the token late after the stop is cancelled too");
    }

    /// <summary>
    /// Verifies starting a loop that already runs keeps the first one, and stopping one that does not run is harmless.
    /// </summary>
    [Fact]
    public async Task BackgroundLoop_StartedTwiceOrStoppedIdle_KeepsOneLoop()
    {
        var loop = new BackgroundLoop(CancellationToken.None);
        await loop.StopAsync();
        var starts = 0;

        loop.Start(token => { Interlocked.Increment(ref starts); return Task.Delay(Timeout.Infinite, token); });
        loop.Start(token => { Interlocked.Increment(ref starts); return Task.Delay(Timeout.Infinite, token); });
        await TestWait.EventuallyAsync(() => Volatile.Read(ref starts) >= 1);
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        starts.Should().Be(1);
        await loop.StopAsync();
    }

    /// <summary>
    /// Verifies the shutdown token of the provider ends the loop too.
    /// </summary>
    [Fact]
    public async Task BackgroundLoop_ShutdownCancelled_EndsTheLoop()
    {
        using var shutdown = new CancellationTokenSource();
        var loop = new BackgroundLoop(shutdown.Token);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        loop.Start(async token =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            finally
            {
                ended.SetResult();
            }
        });

        await shutdown.CancelAsync();

        await ended.Task.WaitAsync(TestWait.Patience);
        await loop.StopAsync();
    }

    /// <summary>
    /// Verifies a price is published for every instrument whose key matches without regard to case, under the symbol
    /// that was subscribed.
    /// </summary>
    [Fact]
    public async Task PublishToSubscribers_KeyInAnotherCase_PublishesUnderEverySubscribedSymbol()
    {
        await using var provider = new KeyedProvider();
        var lower = new Asset("keyed", "aapl", "Apple", AssetClass.Equity);
        var upper = new Asset("keyed", "AAPL", "Apple", AssetClass.Equity);
        await provider.SetSubscriptionsAsync([lower, upper]);

        var found = provider.PublishPrice("Aapl", 190.5m, 188m, DateTimeOffset.FromUnixTimeSeconds(1700000000), MarketState.AfterHours);

        found.Should().BeTrue();
        var quotes = await TestWait.ReadQuotesAsync(provider, 2);
        quotes.Select(q => q.AssetId).Should().BeEquivalentTo([lower.Id, upper.Id]);
        quotes.Should().OnlyContain(q => q.Price == 190.5m && q.Reference == 188m && q.Market == MarketState.AfterHours && !q.TimeIsEstimated);
    }

    /// <summary>
    /// Verifies a price without a trade time is stamped with the clock and marked as estimated.
    /// </summary>
    [Fact]
    public async Task PublishToSubscribers_NoTradeTime_UsesTheClockAndMarksItEstimated()
    {
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1700000500));
        await using var provider = new KeyedProvider(time);
        await provider.SetSubscriptionsAsync([new Asset("keyed", "AAPL", "Apple", AssetClass.Equity)]);

        provider.PublishPrice("AAPL", 1m, null, null, null);

        var quote = (await TestWait.ReadQuotesAsync(provider, 1)).Single();
        quote.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1700000500));
        quote.TimeIsEstimated.Should().BeTrue();
        quote.Market.Should().BeNull();
    }

    /// <summary>
    /// Verifies a key nobody subscribed publishes nothing and says so.
    /// </summary>
    [Fact]
    public async Task PublishToSubscribers_UnknownKey_ReturnsFalse()
    {
        await using var provider = new KeyedProvider();
        await provider.SetSubscriptionsAsync([new Asset("keyed", "AAPL", "Apple", AssetClass.Equity)]);

        provider.PublishPrice("MSFT", 1m, null, null, null).Should().BeFalse();
    }

    /// <summary>
    /// Verifies a source that names instruments differently from how they are stored finds them through its own key, and
    /// that the index follows later changes of the subscriptions.
    /// </summary>
    [Fact]
    public async Task PublishToSubscribers_CustomKey_FollowsTheSubscriptions()
    {
        await using var provider = new KeyedProvider(keyPrefix: "x_");
        var asset = new Asset("keyed", "AAPL", "Apple", AssetClass.Equity);
        await provider.SetSubscriptionsAsync([asset]);

        provider.PublishPrice("x_aapl", 1m, null, null, null).Should().BeTrue();
        provider.PublishPrice("AAPL", 1m, null, null, null).Should().BeFalse();
        await provider.SetSubscriptionsAsync([]);
        provider.PublishPrice("x_aapl", 1m, null, null, null).Should().BeFalse();
    }

    /// <summary>
    /// Verifies the hook is called for a quote that was accepted and not for one of an instrument nobody subscribed.
    /// </summary>
    [Fact]
    public async Task OnQuotePublished_AcceptedAndDroppedQuotes_OnlyAcceptedOnesAreReported()
    {
        await using var provider = new KeyedProvider();
        var apple = new Asset("keyed", "AAPL", "Apple", AssetClass.Equity);
        await provider.SetSubscriptionsAsync([apple]);

        provider.PublishRaw(new Quote(apple.Id, 1m, null, DateTimeOffset.FromUnixTimeSeconds(1700000000)));
        provider.PublishRaw(new Quote(new AssetId("keyed", "MSFT"), 1m, null, DateTimeOffset.FromUnixTimeSeconds(1700000000)));

        provider.PublishedSymbols.Should().Equal("AAPL");
    }

    /// <summary>
    /// A provider that only exposes publishing to subscribers.
    /// </summary>
    private sealed class KeyedProvider : MarketDataProviderBase
    {
        /// <summary>
        /// The text put in front of the symbol to make the key.
        /// </summary>
        private readonly string _keyPrefix;

        /// <summary>
        /// Initializes a new instance of the <see cref="KeyedProvider"/> class.
        /// </summary>
        /// <param name="time">
        /// The clock, or <see langword="null"/> for the system clock.
        /// </param>
        /// <param name="keyPrefix">
        /// The text put in front of the symbol to make the key.
        /// </param>
        public KeyedProvider(TimeProvider? time = null, string keyPrefix = "")
            : base(
                new ProviderDescriptor("keyed", "Keyed", "A test source.", [AssetClass.Equity], ProviderUpdateMode.Polling),
                time ?? TimeProvider.System)
        {
            _keyPrefix = keyPrefix;
        }

        /// <summary>
        /// Publishes a price to the subscribed instruments of a key.
        /// </summary>
        /// <param name="key">
        /// The key.
        /// </param>
        /// <param name="price">
        /// The price.
        /// </param>
        /// <param name="reference">
        /// The previous close.
        /// </param>
        /// <param name="tradedAt">
        /// The trade time.
        /// </param>
        /// <param name="market">
        /// The market state.
        /// </param>
        /// <returns>
        /// Whether an instrument with that key is subscribed.
        /// </returns>
        public bool PublishPrice(string key, decimal price, decimal? reference, DateTimeOffset? tradedAt, MarketState? market) =>
            PublishToSubscribers(key, price, reference, tradedAt, market);

        /// <summary>
        /// Gets the symbols of the quotes the hook was called for, in order.
        /// </summary>
        public List<string> PublishedSymbols { get; } = [];

        /// <summary>
        /// Publishes a quote as it is.
        /// </summary>
        /// <param name="quote">
        /// The quote.
        /// </param>
        public void PublishRaw(Quote quote) => Publish(quote);

        /// <inheritdoc />
        protected override void OnQuotePublished(Quote quote) => PublishedSymbols.Add(quote.AssetId.Symbol);

        /// <inheritdoc />
        protected override string KeyOf(Asset asset) => _keyPrefix + asset.Symbol;

        /// <inheritdoc />
        protected override Task OnSubscriptionsChangedAsync(
            IReadOnlyCollection<Asset> current,
            IReadOnlyCollection<Asset> added,
            IReadOnlyCollection<Asset> removed,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
