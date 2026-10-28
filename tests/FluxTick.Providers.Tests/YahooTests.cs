using System.Net;
using FluxTick.Abstractions;
using FluxTick.Providers.Yahoo;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the Yahoo parser and provider.
/// </summary>
public class YahooTests
{
    /// <summary>
    /// A trimmed chart response.
    /// </summary>
    private const string ChartJson = """
        {"chart":{"result":[{"meta":{"regularMarketPrice":190.5,"chartPreviousClose":188.0,"previousClose":187.5,"regularMarketTime":1700000000},
        "timestamp":[1699999880,1699999940,1700000000],
        "indicators":{"quote":[{"close":[190.0,null,190.5]}]}}],"error":null}}
        """;

    /// <summary>
    /// A chart response carrying an error.
    /// </summary>
    private const string ErrorJson = """{"chart":{"result":null,"error":{"code":"Not Found","description":"No data found, symbol may be delisted"}}}""";

    /// <summary>
    /// The Apple test asset.
    /// </summary>
    private static readonly Asset Aapl = new("yahoo", "AAPL", "Apple", AssetClass.Equity);

    /// <summary>
    /// The Microsoft test asset.
    /// </summary>
    private static readonly Asset Msft = new("yahoo", "MSFT", "Microsoft", AssetClass.Equity);

    /// <summary>
    /// Verifies the chart is parsed into a quote and history, preferring <c>previousClose</c> as reference.
    /// </summary>
    [Fact]
    public void ParseChart_ValidResponse_ReturnsQuoteAndHistory()
    {
        var data = YahooParser.ParseChart(ChartJson, "yahoo", "AAPL");

        data.Quote.Price.Should().Be(190.5m);
        data.Quote.Reference.Should().Be(187.5m);
        data.Quote.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1700000000));
        data.History.Select(p => p.Price).Should().Equal(190.0m, 190.5m);
    }

    /// <summary>
    /// Builds a chart response of a given instrument type with trading periods like Yahoo's: pre-market from 08:00 to
    /// 13:30, the regular session to 20:00 and after hours to 24:00 (UTC, 1 Jan 2026 is 1767225600).
    /// </summary>
    /// <param name="instrumentType">
    /// The instrument type.
    /// </param>
    /// <returns>
    /// The response body.
    /// </returns>
    private static string ChartWithPeriods(string instrumentType) => """
        {"chart":{"result":[{"meta":{"instrumentType":"TYPE","regularMarketPrice":190.5,"regularMarketTime":1767225600,
        "currentTradingPeriod":{"pre":{"start":1767254400,"end":1767274200},"regular":{"start":1767274200,"end":1767297600},"post":{"start":1767297600,"end":1767312000}}},
        "timestamp":[1767274200],"indicators":{"quote":[{"close":[190.5]}]}}],"error":null}}
        """.Replace("TYPE", instrumentType);

    /// <summary>
    /// Verifies the trading state is read from the trading periods of the current day.
    /// </summary>
    [Theory]
    [InlineData(1767240000, MarketState.Closed)] // 04:00, before the pre-market
    [InlineData(1767254400, MarketState.PreMarket)] // 08:00
    [InlineData(1767274199, MarketState.PreMarket)]
    [InlineData(1767274200, MarketState.Open)] // 13:30
    [InlineData(1767297599, MarketState.Open)]
    [InlineData(1767297600, MarketState.AfterHours)] // 20:00
    [InlineData(1767312000, MarketState.Closed)] // 24:00
    public void ParseChart_WithTradingPeriods_ReportsTheStateAtTheTimeOfTheAnswer(long seconds, MarketState expected)
    {
        var data = YahooParser.ParseChart(ChartWithPeriods("EQUITY"), "yahoo", "AAPL", DateTimeOffset.FromUnixTimeSeconds(seconds));

        data.Quote.Market.Should().Be(expected);
    }

    /// <summary>
    /// Verifies a market without a pre-market period (Shanghai and Shenzhen) is told to be in its opening auction in the
    /// quarter of an hour before the regular session.
    /// </summary>
    [Theory]
    [InlineData(1767274200 - 901, MarketState.Closed)]
    [InlineData(1767274200 - 900, MarketState.PreMarket)]
    [InlineData(1767274200 - 1, MarketState.PreMarket)]
    [InlineData(1767274200, MarketState.Open)]
    public void ParseChart_WithoutAPreMarketPeriod_ReportsTheOpeningAuctionAsPreMarket(long seconds, MarketState expected)
    {
        var json = ChartWithPeriods("EQUITY").Replace("""{"start":1767254400,"end":1767274200}""", """{"start":1767274200,"end":1767274200}""");

        var data = YahooParser.ParseChart(json, "yahoo", "600000.SS", DateTimeOffset.FromUnixTimeSeconds(seconds));

        data.Quote.Market.Should().Be(expected);
    }

    /// <summary>
    /// Verifies only shares, funds and indices are told: the periods of other instruments cover the whole day.
    /// </summary>
    [Theory]
    [InlineData("ETF", MarketState.Open)]
    [InlineData("INDEX", MarketState.Open)]
    [InlineData("CURRENCY", null)]
    [InlineData("CRYPTOCURRENCY", null)]
    [InlineData("FUTURE", null)]
    [InlineData("MUTUALFUND", null)]
    public void ParseChart_ForAnInstrumentType_ReportsTheStateOnlyForSharesFundsAndIndices(string type, MarketState? expected)
    {
        var data = YahooParser.ParseChart(ChartWithPeriods(type), "yahoo", "X", DateTimeOffset.FromUnixTimeSeconds(1767280000));

        data.Quote.Market.Should().Be(expected);
    }

    /// <summary>
    /// Verifies the quote says its time is the time of the answer when Yahoo gives no time of the trade.
    /// </summary>
    [Fact]
    public void ParseChart_WithoutATradeTime_MarksTheTimeAsEstimated()
    {
        var withTime = YahooParser.ParseChart(ChartJson, "yahoo", "AAPL");
        var withoutTime = YahooParser.ParseChart(ChartJson.Replace(",\"regularMarketTime\":1700000000", string.Empty), "yahoo", "AAPL", DateTimeOffset.FromUnixTimeSeconds(1700000500));

        withTime.Quote.TimeIsEstimated.Should().BeFalse();
        withoutTime.Quote.TimeIsEstimated.Should().BeTrue();
        withoutTime.Quote.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1700000500));
    }

    /// <summary>
    /// Verifies a response without trading periods reports no state.
    /// </summary>
    [Fact]
    public void ParseChart_WithoutTradingPeriods_ReportsNoState()
    {
        YahooParser.ParseChart(ChartJson, "yahoo", "AAPL").Quote.Market.Should().BeNull();
    }

    /// <summary>
    /// Verifies an error object surfaces as a typed exception with Yahoo's message.
    /// </summary>
    [Fact]
    public void ParseChart_ErrorResponse_Throws()
    {
        var act = () => YahooParser.ParseChart(ErrorJson, "yahoo", "NOPE");

        act.Should().Throw<YahooApiException>().WithMessage("*delisted*");
    }

    /// <summary>
    /// Verifies responses with null or missing fields surface as a typed exception instead of an unrelated one.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    [Theory]
    [InlineData("""{"chart":{"result":[{"meta":{"regularMarketPrice":null}}],"error":null}}""")]
    [InlineData("""{"chart":{"result":[{}],"error":null}}""")]
    [InlineData("""{"chart":{"error":null}}""")]
    [InlineData("""{}""")]
    public void ParseChart_MalformedResponse_ThrowsYahooApiException(string json)
    {
        var act = () => YahooParser.ParseChart(json, "yahoo", "AAPL");

        act.Should().Throw<YahooApiException>();
    }

    /// <summary>
    /// Verifies a trade time that no date can hold does not throw: the quote takes the time of the answer, and a history
    /// point with such a time is left out.
    /// </summary>
    [Fact]
    public void ParseChart_TimeOutsideTheDateRange_IsHandledLikeAMissingOne()
    {
        const string json = """
            {"chart":{"result":[{"meta":{"regularMarketPrice":1.5,"regularMarketTime":99999999999999},
            "timestamp":[99999999999999,1700000000],
            "indicators":{"quote":[{"close":[2.0,3.0]}]}}],"error":null}}
            """;
        var before = DateTimeOffset.UtcNow;

        var data = YahooParser.ParseChart(json, "yahoo", "AAPL");

        data.Quote.Timestamp.Should().BeOnOrAfter(before);
        data.History.Select(p => p.Price).Should().Equal(3.0m);
    }

    /// <summary>
    /// Verifies a null close series is tolerated.
    /// </summary>
    [Fact]
    public void ParseChart_NullCloseSeries_ReturnsQuoteWithoutHistory()
    {
        const string json = """{"chart":{"result":[{"meta":{"regularMarketPrice":1.5},"timestamp":[1],"indicators":{"quote":[{"close":null}]}}],"error":null}}""";

        var data = YahooParser.ParseChart(json, "yahoo", "AAPL");

        data.Quote.Price.Should().Be(1.5m);
        data.History.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a symbol with a malformed response does not stop the others from updating.
    /// </summary>
    [Fact]
    public async Task Provider_OneSymbolMalformed_StillPublishesOthers()
    {
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath.EndsWith("MSFT", StringComparison.Ordinal)
            ? Json("""{"chart":{"result":[{"meta":{"regularMarketPrice":null}}],"error":null}}""")
            : Json(ChartJson));
        await using var provider = CreateProvider(handler, new FakeTimeProvider());

        await provider.SetSubscriptionsAsync([Aapl, Msft]);
        var quotes = await ReadAsync(provider, 1);

        quotes.Single().AssetId.Symbol.Should().Be("AAPL");
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
    }

    /// <summary>
    /// Verifies a server error for every request doubles the delay, not only HTTP 429.
    /// </summary>
    [Fact]
    public async Task Provider_ServerErrors_BackOff()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var time = new FakeTimeProvider();
        await using var provider = CreateProvider(handler, time);

        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => handler.Requests.Count == 1);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Reconnecting);

        var start = time.GetUtcNow();
        await EventuallyAsync(() =>
        {
            if (handler.Requests.Count == 2)
            {
                return true;
            }

            time.Advance(TimeSpan.FromSeconds(1));
            return false;
        });

        (time.GetUtcNow() - start).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10), "the 5 second delay doubled");
    }

    /// <summary>
    /// Verifies the poll asks for the cheap daily chart while the history asks for the minute series.
    /// </summary>
    [Fact]
    public async Task Provider_Poll_RequestsDailyInterval()
    {
        var handler = new StubHandler(_ => Json(ChartJson));
        await using var provider = CreateProvider(handler, new FakeTimeProvider());

        await provider.SetSubscriptionsAsync([Aapl]);
        await ReadAsync(provider, 1);
        await provider.GetHistoryAsync(Aapl);

        handler.Requests.Select(r => r.RequestUri!.Query).Should().Equal("?interval=1d&range=1d", "?interval=1m&range=1d");
    }

    /// <summary>
    /// Verifies search results are mapped to asset classes and unsupported types are skipped.
    /// </summary>
    [Fact]
    public void ParseSearch_MapsQuoteTypesToAssetClasses()
    {
        const string json = """
            {"quotes":[
            {"symbol":"AAPL","quoteType":"EQUITY","shortname":"Apple Inc.","longname":"Apple Inc."},
            {"symbol":"^GSPC","quoteType":"INDEX","shortname":"S&P 500"},
            {"symbol":"EURUSD=X","quoteType":"CURRENCY","shortname":"EUR/USD"},
            {"symbol":"GC=F","quoteType":"FUTURE","shortname":"Gold"},
            {"symbol":"AAPL250101C00100000","quoteType":"OPTION","shortname":"opt"}]}
            """;

        var assets = YahooParser.ParseSearch(json, "yahoo");

        assets.Select(a => (a.Symbol, a.AssetClass)).Should().Equal(
            ("AAPL", AssetClass.Equity),
            ("^GSPC", AssetClass.Index),
            ("EURUSD=X", AssetClass.Forex),
            ("GC=F", AssetClass.Commodity));
        assets[0].DisplayName.Should().Be("Apple Inc.");
    }

    /// <summary>
    /// Verifies the provider requests the chart endpoint of each subscribed symbol and publishes the quotes.
    /// </summary>
    [Fact]
    public async Task Provider_Subscribed_PublishesQuotes()
    {
        var handler = new StubHandler(_ => Json(ChartJson));
        var time = new FakeTimeProvider();
        await using var provider = CreateProvider(handler, time);

        await provider.SetSubscriptionsAsync([Aapl, Msft]);
        var quotes = await ReadAsync(provider, 2);

        quotes.Select(q => q.AssetId.Symbol).Should().BeEquivalentTo(["AAPL", "MSFT"]);
        handler.Requests.Select(r => r.RequestUri!.AbsolutePath).Should().BeEquivalentTo(
            ["/v8/finance/chart/AAPL", "/v8/finance/chart/MSFT"]);
        // The status is reported after the cycle returns, which can be after the quotes were read.
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
    }

    /// <summary>
    /// Verifies an endpoint with a path keeps its prefix in the request.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_EndpointWithPath_KeepsItsPrefix()
    {
        var handler = new StubHandler(_ => Json(ChartJson));
        await using var provider = new YahooProvider(
            new YahooProviderFactory().Descriptor,
            new Uri("https://proxy.example.test/yahoo/"),
            TimeSpan.FromSeconds(5),
            new HttpClient(handler),
            new FakeTimeProvider(),
            NullLogger.Instance);

        await provider.SetSubscriptionsAsync([Aapl]);
        await ReadAsync(provider, 1);

        handler.Requests.Select(r => r.RequestUri!.AbsolutePath).Should().Equal("/yahoo/v8/finance/chart/AAPL");
    }

    /// <summary>
    /// Verifies one bad symbol does not stop the others from updating.
    /// </summary>
    [Fact]
    public async Task Provider_OneSymbolFails_StillPublishesOthers()
    {
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath.EndsWith("MSFT", StringComparison.Ordinal)
            ? Json(ErrorJson)
            : Json(ChartJson));
        await using var provider = CreateProvider(handler, new FakeTimeProvider());

        await provider.SetSubscriptionsAsync([Aapl, Msft]);
        var quotes = await ReadAsync(provider, 1);

        quotes.Single().AssetId.Symbol.Should().Be("AAPL");
        await EventuallyAsync(() => provider.Status.Message?.Contains("MSFT", StringComparison.Ordinal) == true);
        provider.Status.State.Should().Be(ProviderConnectionState.Connected);
    }

    /// <summary>
    /// Verifies HTTP 429 doubles the delay before the next poll.
    /// </summary>
    [Fact]
    public async Task Provider_RateLimited_BacksOff()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var time = new FakeTimeProvider();
        await using var provider = CreateProvider(handler, time);

        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => handler.Requests.Count == 1);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Reconnecting);

        // Stepping the clock until the next request shows how long the provider waited, whenever its timer was registered.
        var start = time.GetUtcNow();
        await EventuallyAsync(() =>
        {
            if (handler.Requests.Count == 2)
            {
                return true;
            }

            time.Advance(TimeSpan.FromSeconds(1));
            return false;
        });

        (time.GetUtcNow() - start).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10), "the 5 second delay doubled");
    }

    /// <summary>
    /// Verifies the provider reports failure after repeated cycles without any success.
    /// </summary>
    [Fact]
    public async Task Provider_RepeatedFailures_ReportsFailed()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var time = new FakeTimeProvider();
        await using var provider = CreateProvider(handler, time);

        await provider.SetSubscriptionsAsync([Aapl]);

        // Advancing in steps until the status flips does not depend on the loop having registered its timer already,
        // which a fixed sleep before a single advance does under load.
        await EventuallyAsync(() =>
        {
            if (provider.Status.State == ProviderConnectionState.Failed)
            {
                return true;
            }

            time.Advance(TimeSpan.FromSeconds(5));
            return false;
        });
    }

    /// <summary>
    /// Creates a provider over a stub HTTP handler.
    /// </summary>
    /// <param name="handler">
    /// The stub handler.
    /// </param>
    /// <param name="time">
    /// The fake clock.
    /// </param>
    /// <returns>
    /// The provider.
    /// </returns>
    private static YahooProvider CreateProvider(StubHandler handler, FakeTimeProvider time)
    {
        var descriptor = new YahooProviderFactory().Descriptor;
        return new YahooProvider(
            descriptor,
            new Uri("https://example.test"),
            TimeSpan.FromSeconds(5),
            new HttpClient(handler),
            time,
            NullLogger.Instance);
    }

    /// <summary>
    /// Creates a successful JSON response.
    /// </summary>
    /// <param name="json">
    /// The body.
    /// </param>
    /// <returns>
    /// The response.
    /// </returns>
    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    /// <summary>
    /// Reads a number of quotes with a timeout.
    /// </summary>
    /// <param name="provider">
    /// The provider to read from.
    /// </param>
    /// <param name="count">
    /// The number of quotes to read.
    /// </param>
    /// <returns>
    /// The quotes.
    /// </returns>
    private static async Task<List<Quote>> ReadAsync(IMarketDataProvider provider, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var quotes = new List<Quote>();
        await foreach (var quote in provider.ReadQuotesAsync(timeout.Token))
        {
            quotes.Add(quote);
            if (quotes.Count == count)
            {
                break;
            }
        }

        return quotes;
    }

    /// <summary>
    /// Waits until a condition holds, failing on timeout.
    /// </summary>
    /// <param name="condition">
    /// The condition to wait for.
    /// </param>
    /// <returns>
    /// A task that completes when the condition holds.
    /// </returns>
    private static async Task EventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(10);
        }
    }

    /// <summary>
    /// An HTTP handler that answers from a delegate and records every request.
    /// </summary>
    /// <param name="respond">
    /// Produces the response for a request.
    /// </param>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        /// <summary>
        /// Gets the requests received so far.
        /// </summary>
        public System.Collections.Concurrent.ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(request);
            return Task.FromResult(respond(request));
        }
    }
}
