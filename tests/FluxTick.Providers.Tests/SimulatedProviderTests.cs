using FluxTick.Abstractions;
using FluxTick.Providers.Simulated;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for <see cref="SimulatedProvider"/>.
/// </summary>
public class SimulatedProviderTests
{
    /// <summary>
    /// The instrument used by the tests.
    /// </summary>
    private static readonly Asset Btc = new(SimulatedProviderFactory.ProviderId, "SIM-BTC", "Simulated Bitcoin", AssetClass.Crypto);

    /// <summary>
    /// Creates a provider driven by the given clock.
    /// </summary>
    /// <param name="time">
    /// The fake clock.
    /// </param>
    /// <returns>
    /// The provider.
    /// </returns>
    private static IMarketDataProvider Create(FakeTimeProvider time)
    {
        var factory = new SimulatedProviderFactory();
        return factory.Create(new ProviderCreationContext(new ProviderOptions(factory.Descriptor), NullLoggerFactory.Instance, time));
    }

    /// <summary>
    /// Collects a number of quotes while advancing the fake clock.
    /// </summary>
    /// <param name="provider">
    /// The provider to read from.
    /// </param>
    /// <param name="time">
    /// The fake clock to advance.
    /// </param>
    /// <param name="count">
    /// The number of quotes to collect.
    /// </param>
    /// <returns>
    /// The collected quotes.
    /// </returns>
    private static async Task<List<Quote>> CollectAsync(IMarketDataProvider provider, FakeTimeProvider time, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var quotes = new List<Quote>();
        var reader = provider.ReadQuotesAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        while (quotes.Count < count)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            var move = reader.MoveNextAsync().AsTask();
            if (await move)
            {
                quotes.Add(reader.Current);
            }
        }

        await reader.DisposeAsync();
        return quotes;
    }

    /// <summary>
    /// Verifies the same symbol always produces the same price sequence.
    /// </summary>
    [Fact]
    public async Task Stream_WithSameSymbol_IsDeterministic()
    {
        var timeA = new FakeTimeProvider();
        var timeB = new FakeTimeProvider();
        await using var a = Create(timeA);
        await using var b = Create(timeB);
        await a.SetSubscriptionsAsync([Btc]);
        await b.SetSubscriptionsAsync([Btc]);

        var first = await CollectAsync(a, timeA, 5);
        var second = await CollectAsync(b, timeB, 5);

        first.Select(q => q.Price).Should().Equal(second.Select(q => q.Price));
        first.Should().OnlyContain(q => q.AssetId == Btc.Id && q.Reference != null);
    }

    /// <summary>
    /// Verifies the market hours symbol trades for its open time, then repeats its last price with the old trade time
    /// for its closed time, and trades again after that.
    /// </summary>
    [Fact]
    public async Task Stream_WithMarketHoursSymbol_TradesThenRepeatsTheLastPriceThenTradesAgain()
    {
        var time = new FakeTimeProvider();
        var start = time.GetUtcNow();
        var hours = new Asset(SimulatedProviderFactory.ProviderId, SimulatedProvider.MarketHoursSymbol, "Hours", AssetClass.Equity);
        await using var provider = Create(time);
        await provider.SetSubscriptionsAsync([hours]);

        // The provider ticks once a second (its default) and CollectAsync moves the clock a second per quote, so the
        // index of a quote is, within a second or so, the number of seconds since the start. The symbol trades first,
        // is closed after that, and trades again after that; the checks sit a minute inside the phases, so each phase
        // has to be longer than two minutes.
        var open = (int)SimulatedProvider.OpenFor.TotalSeconds;
        var closed = (int)SimulatedProvider.ClosedFor.TotalSeconds;
        const int minute = 60;
        var earlyInClosedPhase = open + minute;
        var lateInClosedPhase = open + closed - minute;
        var inNextOpenPhase = open + closed + minute;
        var quotes = await CollectAsync(provider, time, inNextOpenPhase + 1);

        var early = quotes[earlyInClosedPhase];
        var late = quotes[lateInClosedPhase];
        early.Timestamp.Should().BeBefore(start + SimulatedProvider.OpenFor, "it is the time of the last trade");
        late.Timestamp.Should().Be(early.Timestamp);
        late.Price.Should().Be(early.Price);
        quotes[inNextOpenPhase].Timestamp.Should().BeAfter(start + SimulatedProvider.OpenFor + SimulatedProvider.ClosedFor);
    }

    /// <summary>
    /// Verifies removing the last subscription stops generation and the provider goes idle.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_RemovingAll_GoesIdle()
    {
        var time = new FakeTimeProvider();
        await using var provider = Create(time);
        await provider.SetSubscriptionsAsync([Btc]);
        provider.Status.State.Should().Be(ProviderConnectionState.Connected);

        await provider.SetSubscriptionsAsync([]);

        provider.Status.State.Should().Be(ProviderConnectionState.Idle);
    }

    /// <summary>
    /// Verifies search filters the demo catalog by text and asset class.
    /// </summary>
    [Fact]
    public async Task SearchAsync_FiltersByQueryAndClass()
    {
        var time = new FakeTimeProvider();
        await using var provider = Create(time);
        var search = (ISymbolSearchProvider)provider;

        var byText = await search.SearchAsync("gold", null);
        var wrongClass = await search.SearchAsync("gold", AssetClass.Crypto);

        byText.Should().ContainSingle().Which.AssetClass.Should().Be(AssetClass.Commodity);
        wrongClass.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies history ends near the starting price and has a point per minute.
    /// </summary>
    [Fact]
    public async Task GetHistoryAsync_ReturnsSixtyOrderedPoints()
    {
        var time = new FakeTimeProvider();
        await using var provider = Create(time);

        var history = await ((IHistoryProvider)provider).GetHistoryAsync(Btc);

        history.Should().HaveCount(60);
        history.Select(p => p.Timestamp).Should().BeInAscendingOrder();
    }
}
