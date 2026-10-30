using System.Globalization;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using FluxTick.Providers.Eulerpool;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the Eulerpool parser.
/// </summary>
public class EulerpoolTests
{
    /// <summary>
    /// The answer of the latest quotes endpoint as captured: the time of one ticker is a number of seconds, that of the
    /// other a string, and one entry has no price.
    /// </summary>
    private const string LatestResponse = """
        {"AAPL":{"ticker":"AAPL","timestamp":1793189029,"price":345.694026186237,"source":"eulerpool"},"SAP.DE":{"ticker":"SAP.DE","timestamp":"1793088000","price":213.14999389648438},"NONE":{"ticker":"NONE"}}
        """;

    /// <summary>
    /// The daily candles of AAPL as captured: the candles of Thursday 23, Friday 24, Monday 26 and Tuesday 27 October 2026,
    /// each at the opening time in New York (13:30 UTC), with the close in `c`.
    /// </summary>
    private const string Candles = """
        {"t":[1792675800,1792762200,1793021400,1793107800],"o":[348.22,347.75,342.75,347.1],"h":[348.22,347.75,342.75,347.1],"l":[348.22,347.75,342.75,347.1],"c":[348.22,347.75,342.75,347.1],"v":[46718600,13520255,15377414,39051747]}
        """;

    /// <summary>
    /// A search answer with a stock, an ETF, a bond and an entry without a ticker.
    /// </summary>
    private const string SearchResponse = """
        {"count":4,"results":[{"name":"SAP SE","isin":"DE0007164600","ticker":"SAP.DE","type":"stock","currency":"EUR"},{"name":"SAP SE ADRhedged","isin":"US74016W8174","ticker":"SAPH","type":"etf","currency":"USD"},{"name":"Some Bond","isin":"XS0000000000","ticker":"BOND1","type":"bond","currency":"EUR"},{"name":"No ticker","isin":"XS1","ticker":null,"type":"stock","currency":null}]}
        """;

    /// <summary>
    /// Verifies a one-second bar gives its close, dated by the end of the bar.
    /// </summary>
    [Fact]
    public void TryParseTick_Bar_ReturnsCloseAtEndOfBar()
    {
        const string message = """{"ev":"A","ticker":"AAPL","window":"1s","start":1793189054000,"end":1793189055000,"open":345.8,"high":345.8,"low":345.8,"close":345.8,"volume":120,"n":3}""";

        EulerpoolParser.TryParseTick(message, out var tick).Should().BeTrue();

        tick.Symbol.Should().Be("AAPL");
        tick.Price.Should().Be(345.8m);
        tick.Time.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1793189055000));
    }

    /// <summary>
    /// Verifies a trade gives its price and its time in milliseconds.
    /// </summary>
    [Fact]
    public void TryParseTick_Trade_ReturnsPriceAndTime()
    {
        const string message = """{"ev":"T","ticker":"SAP.DE","price":213.15,"size":10,"ts":1793189055123}""";

        EulerpoolParser.TryParseTick(message, out var tick).Should().BeTrue();

        tick.Symbol.Should().Be("SAP.DE");
        tick.Price.Should().Be(213.15m);
        tick.Time.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1793189055123));
    }

    /// <summary>
    /// Verifies a trade without a time still gives its price, so the provider can date it itself.
    /// </summary>
    [Fact]
    public void TryParseTick_TradeWithoutTime_ReturnsPriceWithoutTime()
    {
        EulerpoolParser.TryParseTick("""{"ev":"T","ticker":"AAPL","price":1.5}""", out var tick).Should().BeTrue();

        tick.Time.Should().BeNull();
    }

    /// <summary>
    /// Verifies status messages, quotes and bars without a price carry no price.
    /// </summary>
    [Theory]
    [InlineData("""{"ev":"status","status":"connected","delayed":true,"channels":["T","Q","A","L2"]}""")]
    [InlineData("""{"ev":"status","status":"subscribed","tickers":["AAPL"],"channels":["A"]}""")]
    [InlineData("""{"ev":"Q","ticker":"AAPL","bid":1.0,"ask":1.2}""")]
    [InlineData("""{"ev":"A","ticker":"AAPL","close":0}""")]
    [InlineData("""{"ev":"T","price":1.5}""")]
    [InlineData("[]")]
    public void TryParseTick_NoPrice_ReturnsFalse(string message) =>
        EulerpoolParser.TryParseTick(message, out _).Should().BeFalse();

    /// <summary>
    /// Verifies the latest quotes are read whether the time is a number or a string, and an entry without a price is left out.
    /// </summary>
    [Fact]
    public void ParseQuotes_Latest_ReturnsPricesWithTimes()
    {
        var quotes = EulerpoolParser.ParseQuotes(LatestResponse);

        quotes.Select(q => q.Key).Should().Equal("AAPL", "SAP.DE");
        quotes[0].Price.Should().BeApproximately(345.694026186237m, 0.000001m);
        quotes[0].Time.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1793189029));
        quotes[1].Time.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1793088000));
    }

    /// <summary>
    /// Verifies the search keeps stocks and ETFs as equities and skips what cannot be priced.
    /// </summary>
    [Fact]
    public void ParseSearch_KeepsStocksAndEtfs()
    {
        var assets = EulerpoolParser.ParseSearch(SearchResponse, "eulerpool");

        assets.Should().Equal(
            new Asset("eulerpool", "SAP.DE", "SAP SE", AssetClass.Equity),
            new Asset("eulerpool", "SAPH", "SAP SE ADRhedged", AssetClass.Equity));
    }

    /// <summary>
    /// Verifies a price traded before the open, such as one of the pre-market, is measured against the close of the day before.
    /// </summary>
    [Fact]
    public void PreviousCloseBefore_TradedBeforeTheOpen_IsTheLastCandle()
    {
        var wednesdayMorning = new DateTimeOffset(2026, 10, 28, 13, 0, 0, TimeSpan.Zero);

        PreviousClose(Candles, 347.5m, wednesdayMorning, ExchangeHours.Us).Should().Be(347.1m);
    }

    /// <summary>
    /// Verifies the candle of the trading day, which is partial while the market trades, is not taken for the previous close.
    /// </summary>
    [Fact]
    public void PreviousCloseBefore_WhenTheTradingDayHasACandle_SkipsIt()
    {
        var tuesdayAfternoon = new DateTimeOffset(2026, 10, 27, 18, 0, 0, TimeSpan.Zero);

        PreviousClose(Candles, 346m, tuesdayAfternoon, ExchangeHours.Us).Should().Be(342.75m);
    }

    /// <summary>
    /// Verifies a price that is still the last close, whatever the clock says, is measured against the close before it
    /// and not against itself.
    /// </summary>
    [Theory]
    [InlineData(2026, 10, 27, 20, 30, "347.1", "342.75")]
    [InlineData(2026, 10, 23, 20, 30, "347.75", "348.22")]
    [InlineData(2026, 10, 28, 13, 0, "347.1", "347.1")]
    public void PreviousCloseBefore_TradedAtTheLastClose_IsTheCloseBeforeIt(int year, int month, int day, int hour, int minute, string price, string expected)
    {
        var traded = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);

        PreviousClose(Candles, decimal.Parse(price, CultureInfo.InvariantCulture), traded, ExchangeHours.Us).Should().Be(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Verifies an answer without usable candles gives no previous close.
    /// </summary>
    [Theory]
    [InlineData("""{"error":"not found"}""")]
    [InlineData("""{"t":[],"c":[]}""")]
    [InlineData("""{"t":[1793107800],"c":[0]}""")]
    public void PreviousCloseBefore_NoUsableCandle_ReturnsNull(string json) =>
        PreviousClose(json, 347m, new DateTimeOffset(2026, 10, 28, 13, 0, 0, TimeSpan.Zero), null).Should().BeNull();

    /// <summary>
    /// Verifies the closes are ordered by day whatever the order of the answer, so that the previous close does not depend
    /// on it.
    /// </summary>
    [Fact]
    public void PreviousCloseBefore_CandlesNewestFirst_StillPicksTheLatestEarlierClose()
    {
        const string newestFirst = """{"t":[1793107800,1793021400,1792762200],"c":[347.1,342.75,347.75]}""";

        PreviousClose(newestFirst, 350m, new DateTimeOffset(2026, 10, 28, 13, 0, 0, TimeSpan.Zero), ExchangeHours.Us).Should().Be(347.1m);
    }

    /// <summary>
    /// Verifies a candle stamped at exactly midnight UTC keeps its UTC date as the trading day at an exchange west of UTC,
    /// instead of moving to the evening before.
    /// </summary>
    [Fact]
    public void PreviousCloseBefore_MidnightUtcCandle_KeepsItsUtcDay()
    {
        const string json = """{"t":[1792713600],"c":[100]}"""; // Fri 23 Oct 00:00 UTC.

        PreviousClose(json, 101m, new DateTimeOffset(2026, 10, 23, 14, 0, 0, TimeSpan.Zero), ExchangeHours.Us)
            .Should().BeNull("the midnight candle is Friday's own, not Thursday's");
        PreviousClose(json, 101m, new DateTimeOffset(2026, 10, 24, 14, 0, 0, TimeSpan.Zero), ExchangeHours.Us).Should().Be(100m);
    }

    /// <summary>
    /// Verifies a price traded after the regular session and different from its close is measured against that close.
    /// </summary>
    [Fact]
    public void PreviousCloseBefore_AfterHoursPrice_IsMeasuredAgainstTheCloseOfThatDay()
    {
        var afterHours = new DateTimeOffset(2026, 10, 27, 20, 30, 0, TimeSpan.Zero);

        PreviousClose(Candles, 348.5m, afterHours, ExchangeHours.Us).Should().Be(347.1m);
    }

    /// <summary>
    /// Verifies an after-hours price has no previous close while the candle of its day is not final.
    /// </summary>
    [Fact]
    public void PreviousCloseBefore_AfterHoursPriceOnAnUnsettledDay_ReturnsNull()
    {
        var afterHours = new DateTimeOffset(2026, 10, 27, 20, 30, 0, TimeSpan.Zero);
        var closes = EulerpoolParser.ToDailyCloses(EulerpoolParser.ParseHistory(Candles), ExchangeHours.Us.Zone);

        EulerpoolParser.PreviousCloseBefore(closes, 348.5m, afterHours, ExchangeHours.Us, new DateOnly(2026, 10, 27)).Should().BeNull();
        EulerpoolParser.PreviousCloseBefore(closes, 348.5m, afterHours, ExchangeHours.Us, new DateOnly(2026, 10, 28)).Should().Be(347.1m);
    }

    /// <summary>
    /// Verifies a trade at exactly midnight UTC is an instant like any other, dated at the exchange: Friday evening in New York.
    /// </summary>
    [Fact]
    public void PreviousCloseBefore_TradedAtMidnightUtc_IsDatedAtTheExchange()
    {
        const string json = """{"t":[1792713600,1792800000],"c":[100,110]}"""; // Fri 23 and Sat 24 Oct 00:00 UTC.

        PreviousClose(json, 100m, new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero), ExchangeHours.Us)
            .Should().BeNull("it was traded on Friday, at the close of Friday's own candle");
    }

    /// <summary>
    /// Verifies a price traded before the oldest close fetched has no previous close.
    /// </summary>
    [Fact]
    public void PreviousCloseBefore_TradedBeforeTheOldestClose_ReturnsNull() =>
        PreviousClose(Candles, 340m, new DateTimeOffset(2026, 9, 1, 15, 0, 0, TimeSpan.Zero), ExchangeHours.Us).Should().BeNull();

    /// <summary>
    /// Verifies an exchange that is not known dates candles and trades by UTC.
    /// </summary>
    [Fact]
    public void PreviousCloseBefore_WithoutAnExchange_DatesByUtc() =>
        PreviousClose(Candles, 347.1m, new DateTimeOffset(2026, 10, 27, 20, 0, 0, TimeSpan.Zero), null).Should().Be(342.75m);

    /// <summary>
    /// Verifies a price from the stream is measured against the close before the day it was traded, not before the current
    /// day.
    /// </summary>
    [Fact]
    public async Task Stream_BarTradedOnAnEarlierDay_CarriesTheCloseBeforeThatDay()
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHttpHandler(request =>
            StubHttpHandler.Json(request.RequestUri!.AbsolutePath.Contains("/charting/", StringComparison.Ordinal) ? Candles : "{}"));
        await using var provider = CreateProvider(server.Uri, handler);
        var apple = new Asset("eulerpool", "AAPL", "Apple", AssetClass.Equity);
        using var reading = new CancellationTokenSource(TestWait.Patience);
        var reference = new TaskCompletionSource<decimal?>();
        var reader = Task.Run(
            async () =>
            {
                try
                {
                    await foreach (var quote in provider.ReadQuotesAsync(reading.Token))
                    {
                        if (quote.Reference is not null)
                        {
                            reference.TrySetResult(quote.Reference);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // The test is over.
                }
            },
            reading.Token);

        await provider.SetSubscriptionsAsync([apple]);
        await server.WaitForMessageAsync("""{"action":"subscribe","params":{"tickers":["AAPL"],"channels":["T","A"]}}""");
        var end = 1793131200000L; // Tuesday 27 October 2026, 20:00 UTC: after the close.
        await TestWait.EventuallyAsync(() =>
        {
            _ = server.SendAsync("{\"ev\":\"A\",\"ticker\":\"AAPL\",\"end\":" + (end += 1000) + ",\"close\":347.1}");
            return reference.Task.IsCompleted;
        });

        (await reference.Task).Should().Be(342.75m);
        await reading.CancelAsync();
        await reader.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies a price from the latest quotes endpoint is measured against the close before the day it was traded.
    /// </summary>
    [Fact]
    public async Task Provider_PriceTradedOnAnEarlierDay_CarriesTheCloseBeforeThatDay()
    {
        const string latest = """{"AAPL":{"ticker":"AAPL","timestamp":1793131200,"price":347.1}}""";
        var handler = new StubHttpHandler(request =>
            StubHttpHandler.Json(request.RequestUri!.AbsolutePath.Contains("/charting/", StringComparison.Ordinal) ? Candles : latest));
        await using var provider = CreateProvider(new Uri("ws://127.0.0.1:1/unreachable"), handler);

        await provider.SetSubscriptionsAsync([new Asset("eulerpool", "AAPL", "Apple", AssetClass.Equity)]);
        var quotes = await TestWait.ReadQuotesAsync(provider, 1);

        quotes.Single().Reference.Should().Be(342.75m);
    }

    /// <summary>
    /// Verifies the daily closes are asked for far enough back to reach the close before a price traded a while ago.
    /// </summary>
    [Fact]
    public async Task Provider_NewInstrument_AsksForFortyFiveDaysOfDailyCloses()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 15, 0, 0, TimeSpan.Zero));
        var handler = new StubHttpHandler(request =>
            StubHttpHandler.Json(request.RequestUri!.AbsolutePath.Contains("/charting/", StringComparison.Ordinal) ? Candles : LatestResponse));
        await using var provider = CreateProvider(new Uri("ws://127.0.0.1:1/unreachable"), handler, time);
        var now = time.GetUtcNow();

        await provider.SetSubscriptionsAsync([new Asset("eulerpool", "AAPL", "Apple", AssetClass.Equity)]);
        await TestWait.ReadQuotesAsync(provider, 1);

        handler.Requests.First(r => r.RequestUri!.Query.Contains("resolution=D", StringComparison.Ordinal)).RequestUri!.Query
            .Should().Be($"?resolution=D&from={now.AddDays(-45).ToUnixTimeSeconds()}&to={now.ToUnixTimeSeconds()}");
    }

    /// <summary>
    /// Verifies an error answer without results gives an empty list.
    /// </summary>
    [Fact]
    public void ParseSearch_ErrorBody_ReturnsEmpty() =>
        EulerpoolParser.ParseSearch("""{"error":"Token not valid"}""", "eulerpool").Should().BeEmpty();

    /// <summary>
    /// Verifies removing an instrument unsubscribes from its ticker on the same connection, and that subscribing names the
    /// channels.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_InstrumentRemoved_UnsubscribesItsTicker()
    {
        await using var server = LoopbackServer.Start();
        await using var provider = CreateProvider(server.Uri);
        var apple = new Asset("eulerpool", "AAPL", "Apple", AssetClass.Equity);
        var sap = new Asset("eulerpool", "SAP.DE", "SAP", AssetClass.Equity);

        await provider.SetSubscriptionsAsync([apple, sap]);
        await server.WaitForMessageAsync("""{"action":"subscribe","params":{"tickers":["AAPL","SAP.DE"],"channels":["T","A"]}}""");
        await provider.SetSubscriptionsAsync([apple]);

        await server.WaitForMessageAsync("""{"action":"unsubscribe","params":{"tickers":["SAP.DE"]}}""");
        server.ConnectionCount.Should().Be(1, "the stream was not dropped to remove a ticker");
    }

    /// <summary>
    /// Verifies a ticker that is still subscribed under another case is not unsubscribed with the one that goes.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_SameTickerInAnotherCase_IsNotUnsubscribed()
    {
        await using var server = LoopbackServer.Start();
        await using var provider = CreateProvider(server.Uri);
        var upper = new Asset("eulerpool", "AAPL", "Apple", AssetClass.Equity);
        var lower = new Asset("eulerpool", "aapl", "Apple", AssetClass.Equity);

        await provider.SetSubscriptionsAsync([upper, lower]);
        await server.WaitForMessageAsync("""{"action":"subscribe","params":{"tickers":["AAPL"],"channels":["T","A"]}}""");
        await provider.SetSubscriptionsAsync([lower]);
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        server.Count("""{"action":"unsubscribe","params":{"tickers":["AAPL"]}}""").Should().Be(0);
    }

    /// <summary>
    /// Verifies the key is sent as a bearer token when the stream is opened, and a bar from the stream is published under
    /// the subscribed ticker.
    /// </summary>
    [Fact]
    public async Task Stream_SendsTheKeyAndPublishesBars()
    {
        await using var server = LoopbackServer.Start();
        await using var provider = CreateProvider(server.Uri);
        var apple = new Asset("eulerpool", "AAPL", "Apple", AssetClass.Equity);
        await provider.SetSubscriptionsAsync([apple]);
        await server.WaitForMessageAsync("""{"action":"subscribe","params":{"tickers":["AAPL"],"channels":["T","A"]}}""");

        await server.SendAsync("""{"ev":"status","status":"subscribed","tickers":["AAPL"],"channels":["A"]}""");
        await server.SendAsync("""{"ev":"A","ticker":"aapl","window":"1s","start":1793189054000,"end":1793189055000,"open":345.8,"high":345.8,"low":345.8,"close":345.8,"volume":120,"n":3}""");
        var quotes = await TestWait.ReadQuotesAsync(provider, 1);

        server.Authorizations.Should().Equal("Bearer test-key");
        quotes.Single().AssetId.Should().Be(apple.Id, "the quote is published under the subscribed ticker, whatever case the stream uses");
        quotes.Single().Price.Should().Be(345.8m);
        quotes.Single().Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1793189055000));
    }

    /// <summary>
    /// Verifies the first price of a new instrument is fetched over REST with the key, from the latest quotes endpoint,
    /// and that it carries the previous close from the daily candles, which is what gives the change and its color.
    /// </summary>
    [Fact]
    public async Task Provider_NewInstrument_FetchesItsFirstPriceOverRest()
    {
        var handler = new StubHttpHandler(request =>
            StubHttpHandler.Json(request.RequestUri!.AbsolutePath.Contains("/charting/", StringComparison.Ordinal) ? Candles : LatestResponse));
        await using var provider = CreateProvider(new Uri("ws://127.0.0.1:1/unreachable"), handler);
        var apple = new Asset("eulerpool", "AAPL", "Apple", AssetClass.Equity);

        await provider.SetSubscriptionsAsync([apple]);
        var quotes = await TestWait.ReadQuotesAsync(provider, 1);

        quotes.Single().AssetId.Should().Be(apple.Id);
        quotes.Single().Reference.Should().Be(347.1m);
        quotes.Single().Change.Should().NotBeNull();
        var latest = handler.Requests.Single(r => r.RequestUri!.AbsolutePath.Contains("/market/", StringComparison.Ordinal));
        latest.RequestUri!.PathAndQuery.Should().Be("/api/1/market/quotes/latest?stocks=AAPL");
        latest.Headers.Authorization!.ToString().Should().Be("Bearer test-key");
        handler.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath == "/api/1/charting/ohlcv/AAPL");
    }

    /// <summary>
    /// Verifies a price from the stream carries the previous close once it is known, so that the change shows without
    /// waiting for REST.
    /// </summary>
    [Fact]
    public async Task Stream_Bar_CarriesThePreviousClose()
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHttpHandler(request =>
            StubHttpHandler.Json(request.RequestUri!.AbsolutePath.Contains("/charting/", StringComparison.Ordinal) ? Candles : "{}"));
        await using var provider = CreateProvider(server.Uri, handler);
        var apple = new Asset("eulerpool", "AAPL", "Apple", AssetClass.Equity);
        using var reading = new CancellationTokenSource(TestWait.Patience);
        var reference = new TaskCompletionSource<decimal?>();
        var reader = Task.Run(
            async () =>
            {
                try
                {
                    await foreach (var quote in provider.ReadQuotesAsync(reading.Token))
                    {
                        if (quote.Reference is not null)
                        {
                            reference.TrySetResult(quote.Reference);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // The test is over.
                }
            },
            reading.Token);

        await provider.SetSubscriptionsAsync([apple]);
        await server.WaitForMessageAsync("""{"action":"subscribe","params":{"tickers":["AAPL"],"channels":["T","A"]}}""");
        var end = 1793189055000L;
        await TestWait.EventuallyAsync(() =>
        {
            _ = server.SendAsync("{\"ev\":\"A\",\"ticker\":\"AAPL\",\"end\":" + (end += 1000) + ",\"close\":345.8}");
            return reference.Task.IsCompleted;
        });

        (await reference.Task).Should().Be(347.1m);
        await reading.CancelAsync();
        await reader.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies the candles become prices at their time, oldest first, skipping candles without a usable close.
    /// </summary>
    [Fact]
    public void ParseHistory_Candles_ReturnsClosesAtTheirTime()
    {
        const string json = """{"t":[1793189000,1793189060,1793189120,1793189180],"o":[1,1,1,1],"h":[1,1,1,1],"l":[1,1,1,1],"c":[345.5,0,345.7,345.9],"v":[1,1,1,1]}""";

        var points = EulerpoolParser.ParseHistory(json);

        points.Select(p => p.Price).Should().Equal(345.5m, 345.7m, 345.9m);
        points[0].Timestamp.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1793189000));
        points.Select(p => p.Timestamp).Should().BeInAscendingOrder();
    }

    /// <summary>
    /// Verifies an answer without candles, with arrays of different lengths or with an error, gives what it can.
    /// </summary>
    [Theory]
    [InlineData("""{"t":[],"c":[]}""", 0)]
    [InlineData("""{"t":[1793189000,1793189060],"c":[345.5]}""", 1)]
    [InlineData("""{"error":"Token not valid"}""", 0)]
    [InlineData("[]", 0)]
    public void ParseHistory_OddAnswers_DoNotFail(string json, int expected) =>
        EulerpoolParser.ParseHistory(json).Should().HaveCount(expected);

    /// <summary>
    /// Verifies the history asks for the one-minute candles of the last 65 minutes only, with the key.
    /// </summary>
    [Fact]
    public async Task Provider_History_AsksForTheLastHourOnly()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Candles));
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 15, 0, 0, TimeSpan.Zero));
        await using var provider = CreateProvider(new Uri("ws://127.0.0.1:1/unreachable"), handler, time);
        var apple = new Asset("eulerpool", "AAPL", "Apple", AssetClass.Equity);
        var now = time.GetUtcNow().ToUnixTimeSeconds();

        var points = await provider.GetHistoryAsync(apple);

        points.Should().HaveCount(4);
        var request = handler.Requests.Single();
        request.RequestUri!.PathAndQuery.Should().Be($"/api/1/charting/ohlcv/AAPL?resolution=1&from={now - 3900}&to={now}");
        request.Headers.Authorization!.ToString().Should().Be("Bearer test-key");
    }

    /// <summary>
    /// Verifies the search goes to the search endpoint with the key and keeps what can be priced.
    /// </summary>
    [Fact]
    public async Task Provider_Search_UsesTheEndpointAndTheKey()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(SearchResponse));
        await using var provider = CreateProvider(new Uri("ws://127.0.0.1:1/unreachable"), handler);

        var found = await provider.SearchAsync("sap se", null);

        found.Select(a => a.Symbol).Should().Equal("SAP.DE", "SAPH");
        handler.Requests.Single().RequestUri!.PathAndQuery.Should().Be("/api/1/equity/search?q=sap%20se");
        handler.Requests.Single().Headers.Authorization!.ToString().Should().Be("Bearer test-key");
        (await provider.SearchAsync("sap", AssetClass.Index)).Should().BeEmpty();
    }

    /// <summary>
    /// Picks the previous close out of the candles of an answer for a price traded at a time.
    /// </summary>
    /// <param name="json">
    /// The answer of the candles endpoint.
    /// </param>
    /// <param name="price">
    /// The price.
    /// </param>
    /// <param name="traded">
    /// When the price was traded.
    /// </param>
    /// <param name="exchange">
    /// The exchange, or <see langword="null"/> for UTC.
    /// </param>
    /// <returns>
    /// The previous close, or <see langword="null"/> when there is none.
    /// </returns>
    private static decimal? PreviousClose(string json, decimal price, DateTimeOffset traded, ExchangeHours? exchange) =>
        EulerpoolParser.PreviousCloseBefore(EulerpoolParser.ToDailyCloses(EulerpoolParser.ParseHistory(json), exchange?.Zone), price, traded, exchange);

    /// <summary>
    /// Creates a provider that streams from a loopback server and fetches nothing over REST.
    /// </summary>
    /// <param name="streamUri">
    /// The stream endpoint.
    /// </param>
    /// <param name="handler">
    /// The stand-in for the REST API, or <see langword="null"/> for one that answers with an empty object.
    /// </param>
    /// <param name="time">
    /// The clock, or <see langword="null"/> for the system clock.
    /// </param>
    /// <returns>
    /// The provider.
    /// </returns>
    private static EulerpoolProvider CreateProvider(Uri streamUri, StubHttpHandler? handler = null, TimeProvider? time = null) => new(
        new EulerpoolProviderFactory().Descriptor,
        new Uri("https://api.example.test"),
        streamUri,
        "test-key",
        TimeSpan.FromSeconds(10),
        new HttpClient(handler ?? new StubHttpHandler(_ => StubHttpHandler.Json("{}"))),
        time ?? TimeProvider.System,
        NullLogger.Instance);
}
