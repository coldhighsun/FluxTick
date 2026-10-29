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
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart));

        await provider.SetSubscriptionsAsync([Aapl, Msft]);
        var quotes = await ReadAsync(provider, 1);

        quotes.Single().AssetId.Symbol.Should().Be("AAPL");
    }

    /// <summary>
    /// Verifies a server error for every request doubles the delay, not only HTTP 429.
    /// </summary>
    [Fact]
    public async Task Fallback_ServerErrors_BackOff()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time);

        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => handler.Requests.Count == 1);
        await AdvanceUntilRequestsAsync(handler, time, 2);

        var elapsed = await AdvanceUntilRequestsAsync(handler, time, 3);

        elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10), "the 5 second delay doubled");
    }

    /// <summary>
    /// Verifies the poll asks for the cheap daily chart while the history asks for the minute series.
    /// </summary>
    [Fact]
    public async Task Provider_Poll_RequestsDailyInterval()
    {
        var handler = new StubHandler(_ => Json(ChartJson));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart));

        await provider.SetSubscriptionsAsync([Aapl]);
        await ReadAsync(provider, 1);
        await provider.GetHistoryAsync(Aapl);

        // The history asks for the last 65 minutes by time, not for the whole day.
        handler.Requests.Select(r => r.RequestUri!.Query).Should().Equal(
            "?interval=1d&range=1d",
            "?interval=1m&period1=1699970100&period2=1699974000");
    }

    /// <summary>
    /// Verifies a window without trades, which the service answers with a chart that has no candles, gives an empty
    /// history rather than a failure.
    /// </summary>
    [Fact]
    public async Task Provider_History_WindowWithoutTrades_IsEmpty()
    {
        const string noCandles = """{"chart":{"result":[{"meta":{"regularMarketPrice":190.5,"chartPreviousClose":188.0,"regularMarketTime":1700000000}}],"error":null}}""";
        var handler = new StubHandler(_ => Json(noCandles));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart));

        (await provider.GetHistoryAsync(Aapl)).Should().BeEmpty();
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
    /// Verifies a search finds cryptocurrencies, and an asset class filter keeps only that class.
    /// </summary>
    [Fact]
    public async Task Provider_Search_FindsCryptoAndFiltersByAssetClass()
    {
        const string json = """{"quotes":[{"symbol":"BTC-USD","quoteType":"CRYPTOCURRENCY","shortname":"Bitcoin USD"},{"symbol":"AAPL","quoteType":"EQUITY","shortname":"Apple Inc."}]}""";
        var handler = new StubHandler(_ => Json(json));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart));
        var search = (ISymbolSearchProvider)provider;

        var all = await search.SearchAsync("b", null);
        var crypto = await search.SearchAsync("b", AssetClass.Crypto);

        all.Select(a => a.Symbol).Should().Equal("BTC-USD", "AAPL");
        crypto.Select(a => a.Symbol).Should().Equal("BTC-USD");
        new YahooProviderFactory().Descriptor.SupportedAssetClasses.Should().Contain(AssetClass.Crypto);
    }

    /// <summary>
    /// Verifies the provider requests the chart endpoint of each subscribed symbol and publishes the quotes.
    /// </summary>
    [Fact]
    public async Task Provider_Subscribed_PublishesQuotes()
    {
        var handler = new StubHandler(_ => Json(ChartJson));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time);

        await provider.SetSubscriptionsAsync([Aapl, Msft]);
        var quotes = await ReadAsync(provider, 2);

        quotes.Select(q => q.AssetId.Symbol).Should().BeEquivalentTo(["AAPL", "MSFT"]);
        handler.Requests.Select(r => r.RequestUri!.AbsolutePath).Should().BeEquivalentTo(
            ["/v8/finance/chart/AAPL", "/v8/finance/chart/MSFT"]);
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
            Unreachable(),
            TimeSpan.FromSeconds(5),
            new HttpClient(handler),
            new FakeTimeProvider(TradingHoursStart),
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
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart));

        await provider.SetSubscriptionsAsync([Aapl, Msft]);
        var quotes = await ReadAsync(provider, 1);

        quotes.Single().AssetId.Symbol.Should().Be("AAPL");
    }

    /// <summary>
    /// Verifies HTTP 429 doubles the delay before the next poll.
    /// </summary>
    [Fact]
    public async Task Fallback_RateLimited_BacksOff()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time);

        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => handler.Requests.Count == 1);
        await AdvanceUntilRequestsAsync(handler, time, 2);

        // Stepping the clock until the next request shows how long the provider waited, whenever its timer was registered.
        var elapsed = await AdvanceUntilRequestsAsync(handler, time, 3);

        elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10), "the 5 second delay doubled");
    }

    /// <summary>
    /// A price update recorded from the streamer: BTC-USD at 80824.90625, 996.1875 up, a cryptocurrency in the regular
    /// session, traded at 1793164677000 ms.
    /// </summary>
    private const string RecordedUpdate =
        "CgdCVEMtVVNEFXTcnUcYkL6Mj7BoIgNVU0QqA0NDQzApOAFFY7ufP0iAwLG6pQFVP1ueR11xTZ1HZQAMeUR9NtCdR7ABgMCxuqUB2AEE4AGAwLG6pQHoAYDAsbqlAfIBA0JUQ4ECAAAAoFwsc0GJAgAAIoBxp3dC9QIADHlE/QJju58/";

    /// <summary>
    /// Verifies a recorded update of the streamer is read.
    /// </summary>
    [Fact]
    public void TryParse_RecordedUpdate_ReadsTheFields()
    {
        var read = YahooStreamParser.TryParse(RecordedUpdate, out var update);

        read.Should().BeTrue();
        update!.Symbol.Should().Be("BTC-USD");
        update.Price.Should().Be(80824.91m);
        update.Change.Should().Be(996.1875m);
        update.Time.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1793164677000));
        update.QuoteType.Should().Be(41);
        update.MarketHours.Should().Be(1);
    }

    /// <summary>
    /// Verifies text that is not a price update is refused instead of throwing.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("CgdCVEMtVVNE")]
    [InlineData("FXTcnUc=")]
    [InlineData("CgdCVEM=")]
    public void TryParse_NotAPriceUpdate_ReturnsFalse(string text)
    {
        YahooStreamParser.TryParse(text, out var update).Should().BeFalse();
        update.Should().BeNull();
    }

    /// <summary>
    /// Verifies an update without a time or change still gives its price, and a time no date can hold counts as absent.
    /// </summary>
    [Fact]
    public void TryParse_NoTimeOrChange_ReadsThePriceOnly()
    {
        var text = Convert.ToBase64String(Encode("AAPL", 190.5f));

        YahooStreamParser.TryParse(text, out var update).Should().BeTrue();

        update!.Price.Should().Be(190.5m);
        update.Time.Should().BeNull();
        update.Change.Should().BeNull();
    }

    /// <summary>
    /// Verifies a pushed update is published under the subscribed symbol, with the change turned into a reference.
    /// </summary>
    [Fact]
    public async Task Stream_Update_IsPublishedUnderTheSubscribedSymbol()
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart), server.Uri);
        var lower = new Asset("yahoo", "aapl", "Apple", AssetClass.Equity);
        await provider.SetSubscriptionsAsync([lower]);
        await server.WaitForMessageAsync("""{"subscribe":["AAPL"]}""");

        await server.SendAsync(Convert.ToBase64String(Encode("AAPL", 190.5f, 1700000000000, 2.5f, quoteType: 8, marketHours: 2)));

        var quote = (await ReadAsync(provider, 1)).Single();
        quote.AssetId.Should().Be(lower.Id);
        quote.Price.Should().Be(190.5m);
        quote.Reference.Should().Be(188m);
        quote.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000));
        quote.Market.Should().Be(MarketState.AfterHours);
    }

    /// <summary>
    /// Verifies the streamer's JSON wrapper is understood as well as the bare message.
    /// </summary>
    [Fact]
    public async Task Stream_WrappedUpdate_IsPublished()
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart), server.Uri);
        await provider.SetSubscriptionsAsync([Aapl]);
        await server.WaitForMessageAsync("""{"subscribe":["AAPL"]}""");

        await server.SendAsync("{\"type\":\"pricing\",\"message\":\"" + Convert.ToBase64String(Encode("AAPL", 191f, 1700000000000)) + "\"}");

        (await ReadAsync(provider, 1)).Single().Price.Should().Be(191m);
    }

    /// <summary>
    /// Verifies the sessions of a pushed update map to market states, including the overnight session, which the
    /// calendar of the app does not know and would call closed while the price keeps moving.
    /// </summary>
    [Theory]
    [InlineData(0, MarketState.PreMarket)]
    [InlineData(1, MarketState.Open)]
    [InlineData(2, MarketState.AfterHours)]
    [InlineData(3, MarketState.AfterHours)]
    [InlineData(4, MarketState.AfterHours)]
    public async Task Stream_Update_MapsTheSessionToTheMarketState(int marketHours, MarketState expected)
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart), server.Uri);
        var tesla = new Asset("yahoo", "TSLA", "Tesla", AssetClass.Equity);
        await provider.SetSubscriptionsAsync([tesla]);
        await server.WaitForMessageAsync("""{"subscribe":["TSLA"]}""");

        await server.SendAsync(Convert.ToBase64String(Encode("TSLA", 411.75f, 1700000000000, quoteType: 8, marketHours: marketHours)));

        (await ReadAsync(provider, 1)).Single().Market.Should().Be(expected);
    }

    /// <summary>
    /// Verifies the previous close derived from a pushed update is rounded to the digits of the price, so the float
    /// noise of the streamer does not make it jitter.
    /// </summary>
    [Fact]
    public async Task Stream_Update_RoundsTheDerivedReference()
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart), server.Uri);
        var pair = new Asset("yahoo", "EURUSD=X", "EUR/USD", AssetClass.Forex);
        await provider.SetSubscriptionsAsync([pair]);
        await server.WaitForMessageAsync("""{"subscribe":["EURUSD=X"]}""");

        await server.SendAsync(Convert.ToBase64String(Encode("EURUSD=X", 1.11181f, 1700000000000, -0.00096f)));

        (await ReadAsync(provider, 1)).Single().Reference.Should().Be(1.11277m);
    }

    /// <summary>
    /// Verifies an update of an instrument nobody subscribed is dropped, and later changes are sent as messages.
    /// </summary>
    [Fact]
    public async Task Stream_SubscriptionChanges_AreSentAsMessages()
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart), server.Uri);
        await provider.SetSubscriptionsAsync([Aapl]);
        await server.WaitForMessageAsync("""{"subscribe":["AAPL"]}""");

        await provider.SetSubscriptionsAsync([Msft]);

        await server.WaitForMessageAsync("""{"subscribe":["MSFT"]}""");
        await server.WaitForMessageAsync("""{"unsubscribe":["AAPL"]}""");
    }

    /// <summary>
    /// Verifies a new instrument gets its first price from the chart API even while the stream is up, because the stream
    /// is silent for a market that is closed.
    /// </summary>
    [Fact]
    public async Task Snapshot_NewInstrument_IsFetchedFromTheChartApi()
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHandler(_ => Json(ChartJson));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart), server.Uri);

        await provider.SetSubscriptionsAsync([Aapl]);
        var quote = (await ReadAsync(provider, 1)).Single();

        quote.AssetId.Should().Be(Aapl.Id);
        quote.Price.Should().Be(190.5m);
    }

    /// <summary>
    /// Verifies the chart API is not polled again while the stream is connected.
    /// </summary>
    [Fact]
    public async Task Fallback_StreamConnected_DoesNotPoll()
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHandler(_ => Json(ChartJson));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time, server.Uri);
        await provider.SetSubscriptionsAsync([Aapl]);
        await ReadAsync(provider, 1);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);

        for (var i = 0; i < 4; i++)
        {
            time.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(TimeSpan.FromMilliseconds(30));
        }

        handler.Requests.Should().HaveCount(1, "only the snapshot of the new instrument was requested");
    }

    /// <summary>
    /// Verifies nothing is polled while the market is shut, even though the stream is down: on a weekend; the first price
    /// of a new instrument is still fetched.
    /// </summary>
    [Theory]
    [InlineData(2023, 11, 18, 15, 0)] // Saturday
    public async Task Fallback_MarketNotOpen_DoesNotPoll(int year, int month, int day, int hour, int minute)
    {
        var handler = new StubHandler(_ => Json(ChartJson));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time);
        time.SetUtcNow(new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero));
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => handler.Requests.Count == 1);

        await StepAsync(time, TimeSpan.FromMinutes(2));

        handler.Requests.Should().HaveCount(1, "only the snapshot of the new instrument was requested");
    }

    /// <summary>
    /// Verifies a crypto instrument is polled whatever the hours of the stock exchanges: the symbol of a crypto pair has no
    /// suffix, so the hours would call it a US share.
    /// </summary>
    [Fact]
    public async Task Fallback_CryptoOnAWeekend_StillPolls()
    {
        var bitcoin = new Asset("yahoo", "BTC-USD", "Bitcoin", AssetClass.Crypto);
        var handler = new StubHandler(_ => Json(ChartJson));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time);
        time.SetUtcNow(new DateTimeOffset(2023, 11, 18, 15, 0, 0, TimeSpan.Zero)); // Saturday
        await provider.SetSubscriptionsAsync([bitcoin]);
        await EventuallyAsync(() => handler.Requests.Count == 1);

        await AdvanceUntilRequestsAsync(handler, time, 2);
    }

    /// <summary>
    /// Verifies a source whose stream is down is not reported as reconnecting for as long as no market is open: nothing is
    /// polled then, which says nothing about the source.
    /// </summary>
    [Fact]
    public async Task Status_StreamDownAndNoMarketOpen_IsNotReconnecting()
    {
        var handler = new StubHandler(_ => Json(ChartJson));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time);
        time.SetUtcNow(new DateTimeOffset(2023, 11, 18, 15, 0, 0, TimeSpan.Zero)); // Saturday
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => handler.Requests.Count == 1);

        await EventuallyAsync(() =>
        {
            time.Advance(TimeSpan.FromSeconds(5));
            return provider.Status.State == ProviderConnectionState.Connected;
        });

        provider.Status.Message.Should().Contain("no market is open");
        handler.Requests.Should().HaveCount(1);
    }

    /// <summary>
    /// Verifies the pre-market and after hours are polled while the stream is down, since only the stream would deliver them.
    /// </summary>
    /// <param name="hour">
    /// The hour in UTC on 2023-11-15.
    /// </param>
    [Theory]
    [InlineData(13)] // 08:00 in New York, the pre-market
    [InlineData(22)] // 17:00 in New York, after hours
    public async Task Fallback_PreMarketOrAfterHoursWithStreamDown_StillPolls(int hour)
    {
        var handler = new StubHandler(_ => Json(ChartJson));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time);
        time.SetUtcNow(new DateTimeOffset(2023, 11, 15, hour, 0, 0, TimeSpan.Zero));
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => handler.Requests.Count == 1);

        await AdvanceUntilRequestsAsync(handler, time, 2);
    }

    /// <summary>
    /// Verifies polling starts again once the market opens.
    /// </summary>
    [Fact]
    public async Task Fallback_MarketOpens_PollsAgain()
    {
        var handler = new StubHandler(_ => Json(ChartJson));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time);
        time.SetUtcNow(new DateTimeOffset(2023, 11, 18, 15, 0, 0, TimeSpan.Zero)); // Saturday
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => handler.Requests.Count == 1);
        await StepAsync(time, TimeSpan.FromMinutes(1));
        handler.Requests.Should().HaveCount(1);

        time.SetUtcNow(new DateTimeOffset(2023, 11, 20, 14, 31, 0, TimeSpan.Zero)); // Monday 09:31, the market is open

        await AdvanceUntilRequestsAsync(handler, time, 2);
    }

    /// <summary>
    /// Verifies only the instruments whose market is open are polled.
    /// </summary>
    [Fact]
    public async Task Fallback_OnlyOneMarketOpen_PollsOnlyThatOne()
    {
        var hongKong = new Asset("yahoo", "0700.HK", "Tencent", AssetClass.Equity);
        var handler = new StubHandler(_ => Json(ChartJson));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time);
        time.SetUtcNow(new DateTimeOffset(2023, 11, 14, 15, 0, 0, TimeSpan.Zero)); // New York open, Hong Kong shut
        await provider.SetSubscriptionsAsync([Aapl, hongKong]);
        await EventuallyAsync(() => handler.Requests.Count == 2);
        var snapshot = handler.Requests.Count;

        await AdvanceUntilRequestsAsync(handler, time, snapshot + 1);
        await StepAsync(time, TimeSpan.FromSeconds(5));

        handler.Requests.Skip(snapshot).Should().OnlyContain(r => r.RequestUri!.ToString().Contains("AAPL"), "Hong Kong is shut");
    }

    /// <summary>
    /// Verifies a connected but quiet stream is polled after five minutes unless the option says otherwise.
    /// </summary>
    [Fact]
    public async Task Fallback_QuietStream_PollsAfterTheConfiguredInterval()
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHandler(_ => Json(ChartJson));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = new YahooProvider(
            new YahooProviderFactory().Descriptor,
            new Uri("https://example.test"),
            server.Uri,
            TimeSpan.FromSeconds(5),
            new HttpClient(handler),
            time,
            NullLogger.Instance);
        await provider.SetSubscriptionsAsync([Aapl]);
        await ReadAsync(provider, 1);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);

        await StepAsync(time, TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(30));
        handler.Requests.Should().HaveCount(1, "the stream has been quiet for less than five minutes");

        await AdvanceUntilRequestsAsync(handler, time, 2);
    }

    /// <summary>
    /// Verifies the chart API takes over while the stream is down.
    /// </summary>
    [Fact]
    public async Task Fallback_StreamDown_PollsTheChartApi()
    {
        var handler = new StubHandler(_ => Json(ChartJson));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time);
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => handler.Requests.Count == 1);

        await AdvanceUntilRequestsAsync(handler, time, 2);
        await AdvanceUntilRequestsAsync(handler, time, 3);

        (await ReadAsync(provider, 3)).Should().OnlyContain(q => q.Price == 190.5m);
    }

    /// <summary>
    /// Verifies a stream that is connected but quiet is not trusted for ever: REST is polled once for every quiet period,
    /// and not at the fast rate of a stream that is down.
    /// </summary>
    [Fact]
    public async Task Fallback_StreamConnectedButQuiet_PollsOncePerQuietPeriod()
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHandler(_ => Json(ChartJson));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time, server.Uri);
        await provider.SetSubscriptionsAsync([Aapl]);
        await ReadAsync(provider, 1);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);

        var connected = time.GetTimestamp();

        await StepAsync(time, TimeSpan.FromSeconds(55));
        handler.Requests.Should().HaveCount(1, "the stream has been quiet for less than the quiet period");

        // Stepping until the request arrives does not depend on the loop having registered its timer already.
        await AdvanceUntilRequestsAsync(handler, time, 2);
        time.GetElapsedTime(connected).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(1));
        var firstPoll = time.GetTimestamp();
        await StepAsync(time, TimeSpan.FromSeconds(30));
        handler.Requests.Should().HaveCount(2, "the next poll is a quiet period away");

        await AdvanceUntilRequestsAsync(handler, time, 3);
        time.GetElapsedTime(firstPoll).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Verifies a stream that keeps delivering is never polled for being quiet.
    /// </summary>
    [Fact]
    public async Task Fallback_StreamDelivers_DoesNotPollForQuiet()
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHandler(_ => Json(ChartJson));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time, server.Uri);
        await provider.SetSubscriptionsAsync([Aapl]);
        await ReadAsync(provider, 1);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
        await server.WaitForMessageAsync("""{"subscribe":["AAPL"]}""");

        for (var i = 0; i < 6; i++)
        {
            await StepAsync(time, TimeSpan.FromSeconds(30));
            await server.SendAsync(Convert.ToBase64String(Encode("AAPL", 190.5f + i, 1700000000000 + i)));
            await ReadAsync(provider, 1);
        }

        handler.Requests.Should().HaveCount(1, "only the snapshot of the new instrument was requested");
    }

    /// <summary>
    /// Verifies an instrument that goes quiet while the stream keeps delivering for another is topped up over REST on its
    /// own, and the busy one is not polled.
    /// </summary>
    [Fact]
    public async Task Fallback_OneInstrumentQuietOnABusyStream_PollsOnlyThatOne()
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHandler(_ => Json(ChartJson));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time, server.Uri);
        await provider.SetSubscriptionsAsync([Aapl, Msft]);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
        await EventuallyAsync(() => handler.Requests.Count == 2);
        var snapshots = handler.Requests.Count;

        for (var i = 0; i < 6 && handler.Requests.Count == snapshots; i++)
        {
            await StepAsync(time, TimeSpan.FromSeconds(30));
            await server.SendAsync(Convert.ToBase64String(Encode("AAPL", 190.5f + i, 1700000000000 + i)));
        }

        await EventuallyAsync(() => handler.Requests.Count > snapshots);
        handler.Requests.Skip(snapshots).Should().OnlyContain(r => r.RequestUri!.ToString().Contains("MSFT"));
    }

    /// <summary>
    /// Verifies the top-up of a quiet instrument backs off when the service pushes back, instead of asking again at the
    /// pace of the quiet period.
    /// </summary>
    [Fact]
    public async Task Fallback_QuietInstrumentTopUpRateLimited_WaitsLongerBeforeAskingAgain()
    {
        await using var server = LoopbackServer.Start();
        var snapshotsDone = false;
        var handler = new StubHandler(_ => snapshotsDone ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : Json(ChartJson));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time, server.Uri);
        await provider.SetSubscriptionsAsync([Aapl, Msft]);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
        await EventuallyAsync(() => handler.Requests.Count == 2);
        snapshotsDone = true;

        var tick = 0;
        async Task BusyForAsync(TimeSpan total)
        {
            for (var elapsed = TimeSpan.Zero; elapsed < total; elapsed += TimeSpan.FromSeconds(30))
            {
                await StepAsync(time, TimeSpan.FromSeconds(30));
                await server.SendAsync(Convert.ToBase64String(Encode("AAPL", 190.5f + tick, 1700000000000 + tick)));
                tick++;
            }
        }

        while (handler.Requests.Count == 2)
        {
            await BusyForAsync(TimeSpan.FromSeconds(30));
        }

        var afterFirst = handler.Requests.Count;

        await BusyForAsync(TimeSpan.FromSeconds(90));
        handler.Requests.Should().HaveCount(afterFirst, "after a refusal the top-up waits twice the quiet period");

        await BusyForAsync(TimeSpan.FromSeconds(90));
        handler.Requests.Count.Should().BeGreaterThan(afterFirst);
    }

    /// <summary>
    /// Verifies an instrument of a market that is not trading is not topped up while the stream is busy with another.
    /// </summary>
    [Fact]
    public async Task Fallback_ClosedMarketInstrumentOnABusyStream_IsNotToppedUp()
    {
        await using var server = LoopbackServer.Start();
        var hongKong = new Asset("yahoo", "0700.HK", "Tencent", AssetClass.Equity);
        var handler = new StubHandler(_ => Json(ChartJson));
        var time = new FakeTimeProvider(TradingHoursStart); // New York open, Hong Kong shut
        await using var provider = CreateProvider(handler, time, server.Uri);
        await provider.SetSubscriptionsAsync([Aapl, hongKong]);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
        await EventuallyAsync(() => handler.Requests.Count == 2);

        for (var i = 0; i < 6; i++)
        {
            await StepAsync(time, TimeSpan.FromSeconds(30));
            await server.SendAsync(Convert.ToBase64String(Encode("AAPL", 190.5f + i, 1700000000000 + i)));
        }

        handler.Requests.Should().HaveCount(2, "Hong Kong is shut and Apple keeps trading");
    }

    /// <summary>
    /// Verifies removing one of two instruments that differ only in the case of the symbol does not unsubscribe the symbol
    /// of the one that stays, and that the symbol is unsubscribed once the last one is gone.
    /// </summary>
    [Fact]
    public async Task Stream_CaseVariantSymbols_UnsubscribeOnlyWhenTheLastOneGoes()
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart), server.Uri);
        var lower = new Asset("yahoo", "aapl", "Apple", AssetClass.Equity);
        await provider.SetSubscriptionsAsync([lower, Aapl]);
        await server.WaitForMessageAsync("""{"subscribe":["AAPL"]}""");

        await provider.SetSubscriptionsAsync([Aapl, Msft]);
        await server.WaitForMessageAsync("""{"subscribe":["MSFT"]}""");
        server.Count("""{"unsubscribe":["AAPL"]}""").Should().Be(0, "the other instrument still uses the symbol");

        await provider.SetSubscriptionsAsync([Msft]);
        await server.WaitForMessageAsync("""{"unsubscribe":["AAPL"]}""");
    }

    /// <summary>
    /// Verifies prices that keep arriving through the chart API while the stream is down make the source connected, not
    /// reconnecting, so alerts and holdings keep working.
    /// </summary>
    [Fact]
    public async Task Status_StreamDownButPollingWorks_IsConnected()
    {
        var handler = new StubHandler(_ => Json(ChartJson));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time);
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => handler.Requests.Count == 1);

        await AdvanceUntilRequestsAsync(handler, time, 2);

        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
        provider.Status.Message.Should().StartWith("Live stream unavailable");
    }

    /// <summary>
    /// Verifies the source is reported as failed when neither the stream nor the chart API delivers, as a polled source was.
    /// </summary>
    [Fact]
    public async Task Status_StreamDownAndPollingFailsRepeatedly_IsFailed()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time);

        await provider.SetSubscriptionsAsync([Aapl]);

        // Stepping until the status flips does not depend on the loop having registered its timer already.
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
    /// Verifies a symbol the chart API does not know is shown in the status while the stream is up and the others keep
    /// updating.
    /// </summary>
    [Fact]
    public async Task Status_SymbolWithoutData_IsShownWhileTheStreamIsUp()
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath.EndsWith("MSFT", StringComparison.Ordinal)
            ? Json(ErrorJson)
            : Json(ChartJson));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart), server.Uri);

        await provider.SetSubscriptionsAsync([Aapl, Msft]);

        await EventuallyAsync(() => provider.Status is { State: ProviderConnectionState.Connected, Message: { } message } && message.Contains("MSFT", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies a connected stream that stays silent while polling keeps finding newer prices is dropped and connected
    /// again, once enough quiet rounds in a row have passed, without the status ever leaving Connected.
    /// </summary>
    [Fact]
    public async Task Stream_SilentWhilePollingFindsNewerPrices_ConnectsAgain()
    {
        await using var server = LoopbackServer.Start();
        var time = new FakeTimeProvider(TradingHoursStart);
        var handler = new StubHandler(_ => Json(ChartAt(time.GetUtcNow().ToUnixTimeSeconds() - 5)));
        await using var provider = CreateProvider(handler, time, server.Uri);
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
        var connected = time.GetTimestamp();
        var states = new List<ProviderConnectionState>();
        provider.StatusChanged += (_, status) => states.Add(status.State);

        await EventuallyAsync(() =>
        {
            time.Advance(TimeSpan.FromSeconds(5));
            return server.ConnectionCount >= 2;
        });

        time.GetElapsedTime(connected).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(3), "three quiet rounds are needed");
        states.Should().NotContain(ProviderConnectionState.Reconnecting);
    }

    /// <summary>
    /// Verifies a quiet stream is left alone while polling finds nothing newer, as when the market is closed.
    /// </summary>
    [Fact]
    public async Task Stream_SilentWhilePollingFindsNothingNewer_StaysConnected()
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHandler(_ => Json(ChartJson));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time, server.Uri);
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);

        await StepAsync(time, TimeSpan.FromMinutes(8));

        handler.Requests.Count.Should().BeGreaterThanOrEqualTo(5, "the quiet stream was polled every minute");
        server.ConnectionCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies a round that finds nothing newer starts the count over, so newer prices must be found again and again
    /// without a gap.
    /// </summary>
    [Fact]
    public async Task Stream_SilentWithAGapInTheNewerPrices_StaysConnected()
    {
        await using var server = LoopbackServer.Start();
        var round = 0;

        // Requests: the snapshot, two newer rounds, one that repeats, two newer rounds, then nothing newer.
        long[] times = [1700000000, 1700000060, 1700000120, 1700000120, 1700000180, 1700000240];
        var handler = new StubHandler(_ => Json(ChartAt(times[Math.Min(Interlocked.Increment(ref round) - 1, times.Length - 1)])));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time, server.Uri);
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);

        await StepAsync(time, TimeSpan.FromMinutes(10));

        handler.Requests.Count.Should().BeGreaterThanOrEqualTo(6);
        server.ConnectionCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies a round that fails neither counts nor starts the count over.
    /// </summary>
    [Fact]
    public async Task Stream_SilentWithAFailedRoundInBetween_StillConnectsAgain()
    {
        await using var server = LoopbackServer.Start();
        var round = 0;
        var handler = new StubHandler(_ =>
        {
            var n = Interlocked.Increment(ref round);
            return n == 3 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Json(ChartAt(1700000000 + (60 * n)));
        });
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time, server.Uri);
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);

        await EventuallyAsync(() =>
        {
            time.Advance(TimeSpan.FromSeconds(5));
            return server.ConnectionCount >= 2;
        });

        handler.Requests.Count.Should().BeGreaterThanOrEqualTo(5, "the snapshot, two good rounds, the failed one and the third good round");
    }

    /// <summary>
    /// Verifies a stream that delivers between the rounds is never dropped, however new the polled prices are.
    /// </summary>
    [Fact]
    public async Task Stream_DeliversBetweenTheRounds_StaysConnected()
    {
        await using var server = LoopbackServer.Start();
        var round = 0;
        var handler = new StubHandler(_ => Json(ChartAt(1700000000 + (60 * Interlocked.Increment(ref round)))));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time, server.Uri);
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
        await server.WaitForMessageAsync("""{"subscribe":["AAPL"]}""");

        for (var i = 0; i < 16; i++)
        {
            await StepAsync(time, TimeSpan.FromSeconds(30));
            await server.SendAsync(Convert.ToBase64String(Encode("AAPL", 190.5f + i, 1700100000000 + i)));
        }

        handler.Requests.Count.Should().Be(1, "a stream that delivers is not polled at all");
        server.ConnectionCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies prices that move forward with old trades (a delayed feed, a backfill) are no sign that data should be
    /// arriving now, so a quiet stream is left alone.
    /// </summary>
    [Fact]
    public async Task Stream_SilentWhilePollingFindsOnlyOldTrades_StaysConnected()
    {
        await using var server = LoopbackServer.Start();
        var time = new FakeTimeProvider(TradingHoursStart);
        var round = 0;
        var handler = new StubHandler(_ => Json(ChartAt(time.GetUtcNow().ToUnixTimeSeconds() - 7200 + Interlocked.Increment(ref round))));
        await using var provider = CreateProvider(handler, time, server.Uri);
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);

        await StepAsync(time, TimeSpan.FromMinutes(8));

        handler.Requests.Count.Should().BeGreaterThanOrEqualTo(5, "the quiet stream was polled every minute");
        server.ConnectionCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies recent, newer trades of a market the source reports as shut do not count either.
    /// </summary>
    [Fact]
    public async Task Stream_SilentWhileTheSourceReportsTheMarketShut_StaysConnected()
    {
        await using var server = LoopbackServer.Start();
        var time = new FakeTimeProvider(TradingHoursStart);
        var handler = new StubHandler(_ => Json(ChartAt(time.GetUtcNow().ToUnixTimeSeconds() - 5, closed: true)));
        await using var provider = CreateProvider(handler, time, server.Uri);
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);

        await StepAsync(time, TimeSpan.FromMinutes(8));

        handler.Requests.Count.Should().BeGreaterThanOrEqualTo(5, "the quiet stream was polled every minute");
        server.ConnectionCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies a reconnect that does not make the stream deliver is followed by a longer wait, not by the same wait
    /// again, so a stream that stays silent whatever is done is not reconnected every few minutes.
    /// </summary>
    [Fact]
    public async Task Stream_StillSilentAfterAReconnect_NeedsTwiceAsManyRounds()
    {
        await using var server = LoopbackServer.Start();
        var time = new FakeTimeProvider(TradingHoursStart);
        var handler = new StubHandler(_ => Json(ChartAt(time.GetUtcNow().ToUnixTimeSeconds() - 5)));
        await using var provider = CreateProvider(handler, time, server.Uri);
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
        await EventuallyAsync(() =>
        {
            time.Advance(TimeSpan.FromSeconds(5));
            return server.ConnectionCount >= 2;
        });
        var second = time.GetTimestamp();

        await StepAsync(time, TimeSpan.FromMinutes(5));
        server.ConnectionCount.Should().Be(2, "the same three rounds would have reconnected by now");
        await EventuallyAsync(() =>
        {
            time.Advance(TimeSpan.FromSeconds(5));
            return server.ConnectionCount >= 3;
        });

        time.GetElapsedTime(second).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(6));
    }

    /// <summary>
    /// Verifies the stream saying something puts the wait for a reconnect back to the short one.
    /// </summary>
    [Fact]
    public async Task Stream_SpeaksAfterAReconnect_NeedsTheShortWaitAgain()
    {
        await using var server = LoopbackServer.Start();
        var time = new FakeTimeProvider(TradingHoursStart);
        var handler = new StubHandler(_ => Json(ChartAt(time.GetUtcNow().ToUnixTimeSeconds() - 5)));
        await using var provider = CreateProvider(handler, time, server.Uri);
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
        await EventuallyAsync(() =>
        {
            time.Advance(TimeSpan.FromSeconds(5));
            return server.ConnectionCount >= 2;
        });
        await server.WaitForMessageCountAsync("""{"subscribe":["AAPL"]}""", 2);
        await server.SendAsync(Convert.ToBase64String(Encode("AAPL", 190.5f, time.GetUtcNow().ToUnixTimeMilliseconds() - 10000)));
        await StepAsync(time, TimeSpan.FromSeconds(10));
        var spoke = time.GetTimestamp();

        await EventuallyAsync(() =>
        {
            time.Advance(TimeSpan.FromSeconds(5));
            return server.ConnectionCount >= 3;
        });

        time.GetElapsedTime(spoke).Should().BeLessThan(TimeSpan.FromMinutes(5), "the doubled wait would be six rounds");
    }

    /// <summary>
    /// Verifies messages that carry no price, such as acknowledgements, do not count as the stream delivering.
    /// </summary>
    [Fact]
    public async Task Stream_OnlyAcknowledgementsWhilePollingFindsNewerPrices_ConnectsAgain()
    {
        await using var server = LoopbackServer.Start();
        var time = new FakeTimeProvider(TradingHoursStart);
        var handler = new StubHandler(_ => Json(ChartAt(time.GetUtcNow().ToUnixTimeSeconds() - 5)));
        await using var provider = CreateProvider(handler, time, server.Uri);
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);

        // Acknowledgements keep coming on the first connection, and still the silent stream is dropped.
        for (var i = 0; i < 200 && server.ConnectionCount < 2; i++)
        {
            time.Advance(TimeSpan.FromSeconds(5));
            try
            {
                await server.SendAsync("""{"type":"ack"}""");
            }
            catch (Exception ex) when (ex is System.Net.WebSockets.WebSocketException or TimeoutException)
            {
                // The provider dropped the connection just now, which is what the test waits for.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(5));
        }

        server.ConnectionCount.Should().BeGreaterThanOrEqualTo(2);
    }

    /// <summary>
    /// Verifies the end of an after-hours session, which the stream does not announce, is published once the chart says the
    /// market is shut: the last pushed trade comes again, with its own time, and the shut state.
    /// </summary>
    [Fact]
    public async Task Stream_AfterHoursSessionEndsWhileTheChartSaysShut_PublishesTheLastTradeAsClosed()
    {
        await using var server = LoopbackServer.Start();
        var time = new FakeTimeProvider(TradingHoursStart);
        var handler = new StubHandler(_ => Json(ChartAt(time.GetUtcNow().ToUnixTimeSeconds() - 7200, closed: true)));
        await using var provider = CreateProvider(handler, time, server.Uri);
        var quotes = new System.Collections.Concurrent.ConcurrentQueue<Quote>();
        using var reading = new CancellationTokenSource();
        var reader = ReadInto(provider, quotes, reading.Token);
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
        await server.WaitForMessageAsync("""{"subscribe":["AAPL"]}""");
        await server.SendAsync(Convert.ToBase64String(
            Encode("AAPL", 191.25f, time.GetUtcNow().ToUnixTimeMilliseconds() - 60000, quoteType: 8, marketHours: 2)));
        await EventuallyAsync(() => quotes.Any(q => q.Price == 191.25m));

        await StepAsync(time, TimeSpan.FromMinutes(2));

        await EventuallyAsync(() => quotes.Any(q => q is { Market: MarketState.Closed, TimeIsEstimated: false, Price: 191.25m }));
        var pushed = quotes.First(q => q.Price == 191.25m && q.Market == MarketState.AfterHours);
        quotes.Single(q => q is { Market: MarketState.Closed, Price: 191.25m }).Timestamp.Should().Be(pushed.Timestamp);

        // The chart quotes that follow stay older than that trade, so the service keeps ignoring them; the end is
        // published once and not again at every round.
        await StepAsync(time, TimeSpan.FromMinutes(3));
        quotes.Count(q => q is { Market: MarketState.Closed, Price: 191.25m }).Should().Be(1);
        await reading.CancelAsync();
        await reader;
    }

    /// <summary>
    /// Verifies the end of a pre-market session, such as on a holiday, is published like that of an after-hours session.
    /// </summary>
    [Fact]
    public async Task Stream_PreMarketSessionEndsWhileTheChartSaysShut_PublishesTheLastTradeAsClosed()
    {
        await using var server = LoopbackServer.Start();
        var time = new FakeTimeProvider(TradingHoursStart);
        var handler = new StubHandler(_ => Json(ChartAt(time.GetUtcNow().ToUnixTimeSeconds() - 7200, closed: true)));
        await using var provider = CreateProvider(handler, time, server.Uri);
        var quotes = new System.Collections.Concurrent.ConcurrentQueue<Quote>();
        using var reading = new CancellationTokenSource();
        var reader = ReadInto(provider, quotes, reading.Token);
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
        await server.WaitForMessageAsync("""{"subscribe":["AAPL"]}""");
        await server.SendAsync(Convert.ToBase64String(
            Encode("AAPL", 191.25f, time.GetUtcNow().ToUnixTimeMilliseconds() - 60000, quoteType: 8, marketHours: 0)));
        await EventuallyAsync(() => quotes.Any(q => q.Price == 191.25m));

        await StepAsync(time, TimeSpan.FromMinutes(2));

        await EventuallyAsync(() => quotes.Any(q => q is { Market: MarketState.Closed, Price: 191.25m }));
        await reading.CancelAsync();
        await reader;
    }

    /// <summary>
    /// Verifies a chart of another exchange than the US ones, which gives no extended periods, says nothing about the end of
    /// a session, so the stream is believed.
    /// </summary>
    [Fact]
    public async Task Stream_ChartWithoutExtendedPeriods_DoesNotEndTheSession()
    {
        await using var server = LoopbackServer.Start();
        var time = new FakeTimeProvider(TradingHoursStart);
        var handler = new StubHandler(_ => Json(ChartAt(time.GetUtcNow().ToUnixTimeSeconds() - 7200, closed: true, extendedPeriods: false)));
        await using var provider = CreateProvider(handler, time, server.Uri);
        var quotes = new System.Collections.Concurrent.ConcurrentQueue<Quote>();
        using var reading = new CancellationTokenSource();
        var reader = ReadInto(provider, quotes, reading.Token);
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
        await server.WaitForMessageAsync("""{"subscribe":["AAPL"]}""");
        await server.SendAsync(Convert.ToBase64String(
            Encode("AAPL", 191.25f, time.GetUtcNow().ToUnixTimeMilliseconds() - 60000, quoteType: 8, marketHours: 2)));
        await EventuallyAsync(() => quotes.Any(q => q.Price == 191.25m));

        await StepAsync(time, TimeSpan.FromMinutes(5));

        handler.Requests.Count.Should().BeGreaterThanOrEqualTo(3, "the quiet stream was polled");
        quotes.Should().NotContain(q => q.Price == 191.25m && q.Market == MarketState.Closed);
        await reading.CancelAsync();
        await reader;
    }

    /// <summary>
    /// Verifies a US chart without extended periods ends a session by the fixed length of the sessions, counted from the
    /// regular session.
    /// </summary>
    /// <param name="marketHours">
    /// The session of the pushed update: 0 pre-market, 2 post-market.
    /// </param>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Stream_UsChartWithoutExtendedPeriodsAfterTheSession_EndsTheSession(int marketHours)
    {
        await using var server = LoopbackServer.Start();
        var time = new FakeTimeProvider(TradingHoursStart);
        var now = time.GetUtcNow().ToUnixTimeSeconds();
        var handler = new StubHandler(_ => Json(UsChart(now - 7200, now + 86400, now + 110000)));
        await using var provider = CreateProvider(handler, time, server.Uri);
        var quotes = new System.Collections.Concurrent.ConcurrentQueue<Quote>();
        using var reading = new CancellationTokenSource();
        var reader = ReadInto(provider, quotes, reading.Token);
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
        await server.WaitForMessageAsync("""{"subscribe":["AAPL"]}""");
        await server.SendAsync(Convert.ToBase64String(
            Encode("AAPL", 191.25f, time.GetUtcNow().ToUnixTimeMilliseconds() - 60000, quoteType: 8, marketHours: marketHours)));
        await EventuallyAsync(() => quotes.Any(q => q.Price == 191.25m));

        await StepAsync(time, TimeSpan.FromMinutes(2));

        await EventuallyAsync(() => quotes.Any(q => q is { Market: MarketState.Closed, Price: 191.25m }));
        await reading.CancelAsync();
        await reader;
    }

    /// <summary>
    /// Verifies the post-market session of a US chart that closed early is counted from that close, not from a fixed hour,
    /// so it is still under way an hour after it.
    /// </summary>
    [Fact]
    public async Task Stream_UsChartThatClosedEarlyWithinThePostMarketSession_DoesNotEndIt()
    {
        await using var server = LoopbackServer.Start();
        var time = new FakeTimeProvider(TradingHoursStart);
        var now = time.GetUtcNow().ToUnixTimeSeconds();
        var handler = new StubHandler(_ => Json(UsChart(now - 7200, now - 30000, now - 3600)));
        await using var provider = CreateProvider(handler, time, server.Uri);
        var quotes = new System.Collections.Concurrent.ConcurrentQueue<Quote>();
        using var reading = new CancellationTokenSource();
        var reader = ReadInto(provider, quotes, reading.Token);
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
        await server.WaitForMessageAsync("""{"subscribe":["AAPL"]}""");
        await server.SendAsync(Convert.ToBase64String(
            Encode("AAPL", 191.25f, time.GetUtcNow().ToUnixTimeMilliseconds() - 60000, quoteType: 8, marketHours: 2)));
        await EventuallyAsync(() => quotes.Any(q => q.Price == 191.25m));

        await StepAsync(time, TimeSpan.FromMinutes(5));

        handler.Requests.Count.Should().BeGreaterThanOrEqualTo(3, "the quiet stream was polled");
        quotes.Should().NotContain(q => q.Price == 191.25m && q.Market == MarketState.Closed);
        await reading.CancelAsync();
        await reader;
    }

    /// <summary>
    /// Verifies an instrument that was removed and added again does not inherit the pushed trade it had before.
    /// </summary>
    [Fact]
    public async Task Stream_SymbolRemovedAndAddedAgain_ForgetsThePushedTrade()
    {
        await using var server = LoopbackServer.Start();
        var time = new FakeTimeProvider(TradingHoursStart);
        var handler = new StubHandler(_ => Json(ChartAt(time.GetUtcNow().ToUnixTimeSeconds() - 7200, closed: true)));
        await using var provider = CreateProvider(handler, time, server.Uri);
        var quotes = new System.Collections.Concurrent.ConcurrentQueue<Quote>();
        using var reading = new CancellationTokenSource();
        var reader = ReadInto(provider, quotes, reading.Token);
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
        await server.WaitForMessageAsync("""{"subscribe":["AAPL"]}""");
        await server.SendAsync(Convert.ToBase64String(
            Encode("AAPL", 191.25f, time.GetUtcNow().ToUnixTimeMilliseconds() - 60000, quoteType: 8, marketHours: 2)));
        await EventuallyAsync(() => quotes.Any(q => q.Price == 191.25m));
        await provider.SetSubscriptionsAsync([]);
        await provider.SetSubscriptionsAsync([Aapl]);

        await StepAsync(time, TimeSpan.FromMinutes(5));

        quotes.Should().NotContain(q => q.Price == 191.25m && q.Market == MarketState.Closed);
        await reading.CancelAsync();
        await reader;
    }

    /// <summary>
    /// Verifies the overnight session, which the chart does not know, is not ended by a chart that says the market is shut.
    /// </summary>
    [Fact]
    public async Task Stream_OvernightSessionWhileTheChartSaysShut_IsNotClosed()
    {
        await using var server = LoopbackServer.Start();
        var time = new FakeTimeProvider(TradingHoursStart);
        var handler = new StubHandler(_ => Json(ChartAt(time.GetUtcNow().ToUnixTimeSeconds() - 7200, closed: true)));
        await using var provider = CreateProvider(handler, time, server.Uri);
        var quotes = new System.Collections.Concurrent.ConcurrentQueue<Quote>();
        using var reading = new CancellationTokenSource();
        var reader = ReadInto(provider, quotes, reading.Token);
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
        await server.WaitForMessageAsync("""{"subscribe":["AAPL"]}""");
        await server.SendAsync(Convert.ToBase64String(
            Encode("AAPL", 191.25f, time.GetUtcNow().ToUnixTimeMilliseconds() - 60000, quoteType: 8, marketHours: 4)));
        await EventuallyAsync(() => quotes.Any(q => q.Price == 191.25m));

        await StepAsync(time, TimeSpan.FromMinutes(5));

        handler.Requests.Count.Should().BeGreaterThanOrEqualTo(3, "the quiet stream was polled");
        quotes.Should().NotContain(q => q.Price == 191.25m && q.Market == MarketState.Closed);
        await reading.CancelAsync();
        await reader;
    }

    /// <summary>
    /// Verifies the problems of several instruments show together, so one failing symbol or a rate limit does not hide
    /// another that is broken as well.
    /// </summary>
    [Fact]
    public async Task Status_SeveralSymbolsWithProblems_ShowsAllOfThem()
    {
        await using var server = LoopbackServer.Start();
        var tesla = new Asset("yahoo", "TSLA", "Tesla", AssetClass.Equity);
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath.EndsWith("AAPL", StringComparison.Ordinal)
            ? Json(ChartJson)
            : Json(ErrorJson));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart), server.Uri);
        await provider.SetSubscriptionsAsync([Aapl, Msft, tesla]);

        await EventuallyAsync(() => provider.Status is { State: ProviderConnectionState.Connected, Message: { } message }
            && message.Contains("MSFT", StringComparison.Ordinal)
            && message.Contains("TSLA", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies a problem REST reported for an instrument leaves the status once the instrument is removed.
    /// </summary>
    [Fact]
    public async Task Status_SymbolWithoutDataRemoved_ClearsTheProblem()
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath.EndsWith("MSFT", StringComparison.Ordinal)
            ? Json(ErrorJson)
            : Json(ChartJson));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart), server.Uri);
        await provider.SetSubscriptionsAsync([Aapl, Msft]);
        await EventuallyAsync(() => provider.Status is { State: ProviderConnectionState.Connected, Message: { } message } && message.Contains("MSFT", StringComparison.Ordinal));

        await provider.SetSubscriptionsAsync([Aapl]);

        await EventuallyAsync(() => provider.Status is { State: ProviderConnectionState.Connected, Message: null });
    }

    /// <summary>
    /// Verifies the rounds that failed while the stream was down are forgotten once it connects, so the next outage does
    /// not start with the source already failed.
    /// </summary>
    [Fact]
    public async Task Status_StreamBackAfterFailedRounds_NextOutageStartsReconnecting()
    {
        var port = TestWait.FreePort();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time, new Uri($"ws://127.0.0.1:{port}/"));
        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() =>
        {
            time.Advance(TimeSpan.FromSeconds(5));
            return provider.Status.State == ProviderConnectionState.Failed;
        });
        var server = LoopbackServer.Start(port);
        await EventuallyAsync(() =>
        {
            time.Advance(TimeSpan.FromSeconds(1));
            return provider.Status.State == ProviderConnectionState.Connected;
        });

        server.DropCurrentConnection();
        await server.DisposeAsync();

        await EventuallyAsync(() => provider.Status.State != ProviderConnectionState.Connected);
        provider.Status.State.Should().Be(ProviderConnectionState.Reconnecting);
    }

    /// <summary>
    /// Verifies the factory reads the streamer endpoint and turns the provider into a streaming source.
    /// </summary>
    [Fact]
    public void Descriptor_IsStreamingWithAStreamerEndpoint()
    {
        var descriptor = new YahooProviderFactory().Descriptor;

        descriptor.UpdateMode.Should().Be(ProviderUpdateMode.Streaming);
        descriptor.OptionDefinitions.Should().Contain(o => o.Key == YahooProviderFactory.WebSocketUrlKey);
    }

    /// <summary>
    /// The start of the clock of the tests: a Tuesday morning in New York with the market open, just before the times of the
    /// charts the tests serve. The fallback only polls while the market is open, and the default of the fake clock is a
    /// Saturday.
    /// </summary>
    private static readonly DateTimeOffset TradingHoursStart = new(2023, 11, 14, 15, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Creates a provider over a stub HTTP handler.
    /// </summary>
    /// <param name="handler">
    /// The stub handler.
    /// </param>
    /// <param name="time">
    /// The fake clock.
    /// </param>
    /// <param name="streamUri">
    /// The streamer endpoint; an address nobody listens on when omitted, so the stream is down.
    /// </param>
    /// <returns>
    /// The provider.
    /// </returns>
    private static YahooProvider CreateProvider(StubHandler handler, FakeTimeProvider time, Uri? streamUri = null)
    {
        var descriptor = new YahooProviderFactory().Descriptor;
        return new YahooProvider(
            descriptor,
            new Uri("https://example.test"),
            streamUri ?? Unreachable(),
            TimeSpan.FromSeconds(5),
            new HttpClient(handler),
            time,
            NullLogger.Instance,
            TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Advances the fake clock in steps of five seconds, giving the provider's timers a moment to run after each step.
    /// </summary>
    /// <param name="time">
    /// The fake clock.
    /// </param>
    /// <param name="total">
    /// How far to advance.
    /// </param>
    /// <returns>
    /// A task that completes when the clock was advanced.
    /// </returns>
    private static async Task StepAsync(FakeTimeProvider time, TimeSpan total)
    {
        for (var elapsed = TimeSpan.Zero; elapsed < total; elapsed += TimeSpan.FromSeconds(5))
        {
            time.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(TimeSpan.FromMilliseconds(30));
        }
    }

    /// <summary>
    /// Builds the chart response of a US share that gives only its regular session.
    /// </summary>
    /// <param name="seconds">
    /// The trade time in Unix seconds.
    /// </param>
    /// <param name="regularStart">
    /// When the regular session starts, in Unix seconds.
    /// </param>
    /// <param name="regularEnd">
    /// When the regular session ends, in Unix seconds.
    /// </param>
    /// <returns>
    /// The response body.
    /// </returns>
    private static string UsChart(long seconds, long regularStart, long regularEnd) =>
        $$$$"""{"chart":{"result":[{"meta":{"regularMarketPrice":190.5,"chartPreviousClose":188.0,"previousClose":187.5,"regularMarketTime":{{{{seconds}}}},"instrumentType":"EQUITY","exchangeTimezoneName":"America/New_York","currentTradingPeriod":{"regular":{"start":{{{{regularStart}}}},"end":{{{{regularEnd}}}}}}},"timestamp":[{{{{seconds}}}}],"indicators":{"quote":[{"close":[190.5]}]}}],"error":null}}""";

    /// <summary>
    /// Builds a chart response whose last trade was at a given time.
    /// </summary>
    /// <param name="seconds">
    /// The trade time in Unix seconds.
    /// </param>
    /// <param name="closed">
    /// Whether the response says the market is shut.
    /// </param>
    /// <param name="extendedPeriods">
    /// Whether a response that says the market is shut also describes the pre-market and post-market periods.
    /// </param>
    /// <returns>
    /// The response body.
    /// </returns>
    private static string ChartAt(long seconds, bool closed = false, bool extendedPeriods = true)
    {
        // A market is shut for the parser when the regular session of the day is not under way; this one is far away.
        var extended = extendedPeriods
            ? ""","pre":{"start":3999990000,"end":4000000000},"post":{"start":4000030000,"end":4000040000}"""
            : string.Empty;
        var session = closed
            ? $$$""","instrumentType":"EQUITY","currentTradingPeriod":{"regular":{"start":4000000000,"end":4000030000}{{{extended}}}}"""
            : string.Empty;
        return $$$"""{"chart":{"result":[{"meta":{"regularMarketPrice":190.5,"chartPreviousClose":188.0,"previousClose":187.5,"regularMarketTime":{{{seconds}}}{{{session}}}},"timestamp":[{{{seconds}}}],"indicators":{"quote":[{"close":[190.5]}]}}],"error":null}}""";
    }

    /// <summary>
    /// Gets an address nobody listens on, so the stream cannot connect.
    /// </summary>
    /// <returns>
    /// A WebSocket address on a free local port.
    /// </returns>
    private static Uri Unreachable() => new($"ws://127.0.0.1:{TestWait.FreePort()}/");

    /// <summary>
    /// Advances the fake clock in one-second steps until the stub has received a number of requests, and returns how much
    /// fake time that took.
    /// </summary>
    /// <param name="handler">
    /// The stub that counts requests.
    /// </param>
    /// <param name="time">
    /// The fake clock the provider waits on.
    /// </param>
    /// <param name="requests">
    /// The request count to wait for.
    /// </param>
    /// <returns>
    /// The fake time that passed.
    /// </returns>
    private static async Task<TimeSpan> AdvanceUntilRequestsAsync(StubHandler handler, FakeTimeProvider time, int requests)
    {
        var start = time.GetUtcNow();
        await EventuallyAsync(() =>
        {
            if (handler.Requests.Count >= requests)
            {
                return true;
            }

            time.Advance(TimeSpan.FromSeconds(1));
            return false;
        });

        return time.GetUtcNow() - start;
    }

    /// <summary>
    /// Encodes a price update the way the streamer does.
    /// </summary>
    /// <param name="symbol">
    /// The symbol.
    /// </param>
    /// <param name="price">
    /// The price.
    /// </param>
    /// <param name="timeMilliseconds">
    /// The trade time in Unix milliseconds, or <see langword="null"/> to leave it out.
    /// </param>
    /// <param name="change">
    /// The change against the previous close, or <see langword="null"/> to leave it out.
    /// </param>
    /// <param name="quoteType">
    /// The instrument type, or <see langword="null"/> to leave it out.
    /// </param>
    /// <param name="marketHours">
    /// The session, or <see langword="null"/> to leave it out.
    /// </param>
    /// <returns>
    /// The protocol buffer message.
    /// </returns>
    private static byte[] Encode(string symbol, float price, long? timeMilliseconds = null, float? change = null, int? quoteType = null, int? marketHours = null)
    {
        var bytes = new List<byte>();
        var name = System.Text.Encoding.UTF8.GetBytes(symbol);
        bytes.Add(1 << 3 | 2);
        bytes.Add((byte)name.Length);
        bytes.AddRange(name);
        bytes.Add(2 << 3 | 5);
        bytes.AddRange(BitConverter.GetBytes(price));
        if (timeMilliseconds is { } time)
        {
            bytes.Add(3 << 3);
            AddVarint(bytes, (ulong)((time << 1) ^ (time >> 63)));
        }

        if (quoteType is { } type)
        {
            bytes.Add(6 << 3);
            AddVarint(bytes, (ulong)type);
        }

        if (marketHours is { } hours)
        {
            bytes.Add(7 << 3);
            AddVarint(bytes, (ulong)hours);
        }

        if (change is { } delta)
        {
            // Field 12 needs a two-byte key.
            AddVarint(bytes, 12 << 3 | 5);
            bytes.AddRange(BitConverter.GetBytes(delta));
        }

        return [.. bytes];
    }

    /// <summary>
    /// Appends a variable-length integer.
    /// </summary>
    /// <param name="bytes">
    /// The message to append to.
    /// </param>
    /// <param name="value">
    /// The integer.
    /// </param>
    private static void AddVarint(List<byte> bytes, ulong value)
    {
        while (value >= 0x80)
        {
            bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }

        bytes.Add((byte)value);
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
    /// Collects every quote of a provider until the token is cancelled.
    /// </summary>
    /// <param name="provider">
    /// The provider to read from.
    /// </param>
    /// <param name="into">
    /// Where the quotes go.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that ends the reading.
    /// </param>
    /// <returns>
    /// A task that completes when the reading ended.
    /// </returns>
    private static Task ReadInto(IMarketDataProvider provider, System.Collections.Concurrent.ConcurrentQueue<Quote> into, CancellationToken cancellationToken) =>
        Task.Run(
            async () =>
            {
                try
                {
                    await foreach (var quote in provider.ReadQuotesAsync(cancellationToken))
                    {
                        into.Enqueue(quote);
                    }
                }
                catch (OperationCanceledException)
                {
                    // The test ended the reading.
                }
            },
            CancellationToken.None);

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
