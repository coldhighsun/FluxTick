using FluxTick.Abstractions;
using FluxTick.Providers.Eulerpool;
using Microsoft.Extensions.Logging.Abstractions;

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
    /// Verifies the previous close is the close of the latest candle from before today at the exchange.
    /// </summary>
    [Fact]
    public void ParsePreviousClose_BeforeTheOpen_IsTheLastCandle()
    {
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var wednesdayMorning = new DateTimeOffset(2026, 10, 28, 13, 0, 0, TimeSpan.Zero);

        EulerpoolParser.ParsePreviousClose(Candles, wednesdayMorning, newYork).Should().Be(347.1m);
    }

    /// <summary>
    /// Verifies the candle of today, which is partial while the market trades, is not taken for the previous close.
    /// </summary>
    [Fact]
    public void ParsePreviousClose_WhenTodayHasACandle_SkipsIt()
    {
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var tuesdayAfternoon = new DateTimeOffset(2026, 10, 27, 18, 0, 0, TimeSpan.Zero);

        EulerpoolParser.ParsePreviousClose(Candles, tuesdayAfternoon, newYork).Should().Be(342.75m);
    }

    /// <summary>
    /// Verifies an answer without usable candles gives no previous close.
    /// </summary>
    [Theory]
    [InlineData("""{"error":"not found"}""")]
    [InlineData("""{"t":[],"c":[]}""")]
    [InlineData("""{"t":[1793107800],"c":[0]}""")]
    public void ParsePreviousClose_NoUsableCandle_ReturnsNull(string json) =>
        EulerpoolParser.ParsePreviousClose(json, new DateTimeOffset(2026, 10, 28, 13, 0, 0, TimeSpan.Zero), null).Should().BeNull();

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
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 15, 0, 0, TimeSpan.Zero));
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
