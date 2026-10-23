using System.Net;
using System.Text;
using FluxTick.Abstractions;
using FluxTick.Providers.Tencent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the Tencent parser and provider.
/// </summary>
public class TencentTests
{
    /// <summary>
    /// Quote lines as captured from the service: a Shanghai stock, a Hong Kong stock and a US stock.
    /// </summary>
    private const string QuoteLines = """
        v_sh600000="1~浦发银行~600000~9.62~9.66~9.66~625299~355027~270272~9.62~442~9.61~576~9.60~1131~9.59~238~9.58~737~9.63~735~9.64~10~9.65~473~9.66~311~9.67~651~~20261022161440~-0.04~-0.41~9.73~9.55~9.62/625299/602885524~625299~60289~0.19~6.25~~9.73~9.55~1.86~3204.02~3204.02~0.43~10.63~8.69~0.97~944~9.64~5.18~6.41~~~0.00~60288.5524~15.8730~165~   A~GP-A~-19.97~-0.21~4.37~6.14~0.50~12.68~8.07~-0.82~5.71~6.77~33305838300~33305838300~17.80~-9.33~33305838300~~~-23.71~-1.03~~CNY~0~___D__F__N~9.55~1604~";
        v_hk00700="100~腾讯控股~00700~382.400~393.800~387.000~60791747.0~0~0~382.400~0~0~0~0~0~0~0~0~0~382.400~0~0~0~0~0~0~0~0~0~60791747.0~2026/10/22 16:08:40~-11.400~-2.89~391.200~378.400~382.400~60791747.0~23240805253.600~0~13.97~~0~0~3.25~34770.2738~34770.2738~TENCENT~1.39~658.700~378.400~2.53~6.57~0~0~0~0~0~12.82~2.68~0.67~100~-35.59~-9.68~GP~20.41~11.00~-9.08~-15.32~-14.49~9092644829.00~9092644829.00~13.23~5.315~382.302~-36.45~HKD~1~50";
        v_usAAPL="200~苹果~AAPL.OQ~344.69~343.99~344.94~11292101~0~0~0~0~0~0~0~0~0~0~0~0~0~0~0~0~0~0~0~0~0~0~~2026-10-22 11:34:19~0.70~0.20~347.28~342.28~USD~11292101~3892655138~0.08~39.53~~46.21~~1.45~50273.47013~50304.73742~Apple Inc.~8.72~348.15~242.76~0~46.79~0.31~50304.73742~27.13~0.79~GP~148.75~36.08~1.25~2.61~2.01~14594180000~14585108878~0.86~37.58~1.06~344.72~~~~~";
        """;

    /// <summary>
    /// A search response for a Chinese query, with names as unicode escapes and entries the provider must skip.
    /// </summary>
    private const string SearchResponse =
        """v_hint="sh~000847~腾讯济安~txja~ZS^hk~00700~腾讯控股~txkg~GP^us~tme.n~腾讯音乐~txyl~GP^hk~13005~腾讯法兴七三购A~txfxqsga~QZ^jj~000001~华夏成长混合~hxczhh~KJ^sh~510300~沪深300ETF华泰柏瑞~hs300etfhtbr~ETF^hk~00700~dup~dup~GP";""";

    /// <summary>
    /// The Shanghai test asset.
    /// </summary>
    private static readonly Asset Pufa = new("tencent", "sh600000", "浦发银行", AssetClass.Equity);

    /// <summary>
    /// Verifies a Shanghai line is parsed with its previous close and Beijing time.
    /// </summary>
    [Fact]
    public void ParseQuotes_AShare_ReturnsPriceReferenceAndBeijingTime()
    {
        var quote = TencentParser.ParseQuotes(QuoteLines).Single(q => q.Key == "sh600000");

        quote.Price.Should().Be(9.62m);
        quote.PreviousClose.Should().Be(9.66m);
        quote.Time.Should().Be(new DateTimeOffset(2026, 10, 22, 16, 14, 40, TimeSpan.FromHours(8)));
    }

    /// <summary>
    /// Verifies a Hong Kong line is parsed, including its slash-separated time.
    /// </summary>
    [Fact]
    public void ParseQuotes_HongKong_ReturnsPriceAndHongKongTime()
    {
        var quote = TencentParser.ParseQuotes(QuoteLines).Single(q => q.Key == "hk00700");

        quote.Price.Should().Be(382.4m);
        quote.PreviousClose.Should().Be(393.8m);
        quote.Time.Should().Be(new DateTimeOffset(2026, 10, 22, 16, 8, 40, TimeSpan.FromHours(8)));
    }

    /// <summary>
    /// Verifies a US line is parsed and its Eastern time converted (EDT is four hours behind UTC in October).
    /// </summary>
    [Fact]
    public void ParseQuotes_Us_ReturnsPriceAndEasternTime()
    {
        var quote = TencentParser.ParseQuotes(QuoteLines).Single(q => q.Key == "usAAPL");

        quote.Price.Should().Be(344.69m);
        quote.PreviousClose.Should().Be(343.99m);
        quote.Time!.Value.UtcDateTime.Should().Be(new DateTime(2026, 10, 22, 15, 34, 19, DateTimeKind.Utc));
    }

    /// <summary>
    /// Verifies lines for unknown codes, zero prices and malformed lines are skipped.
    /// </summary>
    [Fact]
    public void ParseQuotes_SkipsUnknownAndInvalidLines()
    {
        const string text = "v_pv_none_match=\"1\";\nv_sh600001=\"1~x~600001~0.00~0.00\";\nnoise\nv_sz000001=\"51~平安银行~000001~11.2~11.0\";";

        var quotes = TencentParser.ParseQuotes(text);

        var quote = quotes.Should().ContainSingle().Subject;
        quote.Key.Should().Be("sz000001");
        quote.Time.Should().BeNull();
    }

    /// <summary>
    /// Verifies GBK bytes are decoded to the Chinese text they carry.
    /// </summary>
    [Fact]
    public void DecodeGbk_DecodesChineseNames()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(936).GetBytes("v_sh600000=\"1~浦发银行~\";");

        TencentParser.DecodeGbk(bytes).Should().Be("v_sh600000=\"1~浦发银行~\";");
    }

    /// <summary>
    /// Verifies symbols are built in the canonical form from search results.
    /// </summary>
    [Theory]
    [InlineData("sh", "600000", "sh600000")]
    [InlineData("SZ", "000001", "sz000001")]
    [InlineData("hk", "00700", "hk00700")]
    [InlineData("hk", "hsi", "hkHSI")]
    [InlineData("us", "aapl.oq", "usAAPL")]
    [InlineData("us", "brk.b.n", "usBRK.B")]
    [InlineData("us", "tme.n", "usTME")]
    [InlineData("us", ".inx", "us.INX")]
    public void ToSymbol_BuildsCanonicalSymbol(string market, string code, string expected)
    {
        TencentParser.ToSymbol(market, code).Should().Be(expected);
    }

    /// <summary>
    /// Verifies a symbol typed in the wrong case is brought into the form the service expects.
    /// </summary>
    [Theory]
    [InlineData("SH600000", "sh600000")]
    [InlineData("sh600000", "sh600000")]
    [InlineData("HK00700", "hk00700")]
    [InlineData("usaapl", "usAAPL")]
    [InlineData("US.INX", "us.INX")]
    [InlineData("  sz000001 ", "sz000001")]
    public void ToRequestCode_NormalizesCase(string typed, string expected)
    {
        TencentParser.ToRequestCode(typed).Should().Be(expected);
    }

    /// <summary>
    /// Verifies search results are decoded, mapped to asset classes and filtered to supported types.
    /// </summary>
    [Fact]
    public void ParseSearch_MapsTypesAndSkipsUnsupported()
    {
        var assets = TencentParser.ParseSearch(SearchResponse, "tencent");

        assets.Select(a => (a.Symbol, a.AssetClass)).Should().Equal(
            ("sh000847", AssetClass.Index),
            ("hk00700", AssetClass.Equity),
            ("usTME", AssetClass.Equity),
            ("sh510300", AssetClass.Equity));
        assets[0].DisplayName.Should().Be("腾讯济安");
        assets[1].DisplayName.Should().Be("腾讯控股");
        assets.Should().OnlyContain(a => a.ProviderId == "tencent");
    }

    /// <summary>
    /// Verifies the no-match answer yields no results.
    /// </summary>
    [Theory]
    [InlineData("""v_hint="N";""")]
    [InlineData("")]
    [InlineData("garbage")]
    public void ParseSearch_NoMatch_ReturnsEmpty(string text)
    {
        TencentParser.ParseSearch(text, "tencent").Should().BeEmpty();
    }

    /// <summary>
    /// Verifies minute history becomes close prices stamped in Beijing time.
    /// </summary>
    [Fact]
    public void ParseMinuteHistory_ReturnsPointsInBeijingTime()
    {
        const string json =
            """{"code":0,"msg":"","data":{"sh600000":{"data":{"date":"20261022","data":["0930 9.66 1037 1001742.00","0931 9.65 2000 1900000.00"]},"qt":{}}}}""";

        var points = TencentParser.ParseMinuteHistory(json, "sh600000");

        points.Select(p => p.Price).Should().Equal(9.66m, 9.65m);
        points[0].Timestamp.Should().Be(new DateTimeOffset(2026, 10, 22, 9, 30, 0, TimeSpan.FromHours(8)));
    }

    /// <summary>
    /// Verifies a US instrument gets no history and costs no request, because the service has no full-day series for it.
    /// </summary>
    [Fact]
    public async Task GetHistoryAsync_UsInstrument_ReturnsEmptyWithoutRequest()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{"data":{"usAAPL":{"data":{"date":"","data":["1600 348.22 1"]}}}}"""));
        await using var provider = CreateProvider(handler, new FakeTimeProvider());

        var points = await provider.GetHistoryAsync(new Asset("tencent", "usAAPL", "Apple", AssetClass.Equity));

        points.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies only US codes are excluded from minute history.
    /// </summary>
    [Theory]
    [InlineData("sh600000", true)]
    [InlineData("hk00700", true)]
    [InlineData("usAAPL", false)]
    [InlineData("USAAPL", false)]
    public void HasMinuteHistory_Code_ExcludesOnlyUsCodes(string code, bool expected)
    {
        TencentParser.HasMinuteHistory(code).Should().Be(expected);
    }

    /// <summary>
    /// Verifies Hong Kong history keeps the UTC+8 offset.
    /// </summary>
    [Fact]
    public void ParseMinuteHistory_HongKongCode_ReturnsPointsInAsiaTime()
    {
        const string json = """{"data":{"hk00700":{"data":{"date":"20261022","data":["0930 500.0 1 1","0931 501.0 1 1"]}}}}""";

        var points = TencentParser.ParseMinuteHistory(json, "hk00700");

        points[0].Timestamp.Should().Be(new DateTimeOffset(2026, 10, 22, 9, 30, 0, TimeSpan.FromHours(8)));
    }

    /// <summary>
    /// Verifies a quote endpoint with a path keeps its prefix in the request.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_QuoteEndpointWithPath_KeepsItsPrefix()
    {
        var handler = new StubHttpHandler(_ => Gbk(QuoteLines));
        await using var provider = new TencentProvider(
            new TencentProviderFactory().Descriptor,
            new Uri("https://proxy.example.test/qt/"),
            TimeSpan.FromSeconds(5),
            new HttpClient(handler),
            new FakeTimeProvider(),
            NullLogger.Instance);

        await provider.SetSubscriptionsAsync([new Asset("tencent", "sh600000", "SPDB", AssetClass.Equity)]);
        await TestWait.EventuallyAsync(() => !handler.Requests.IsEmpty);

        handler.Requests.First().RequestUri!.PathAndQuery.Should().Be("/qt/q=sh600000");
    }

    /// <summary>
    /// Verifies a response without a date or with an error yields no history.
    /// </summary>
    [Theory]
    [InlineData("""{"code":0,"data":{"usAAPL":{"data":{"date":"","data":["1143 344.77 13028311"]}}}}""")]
    [InlineData("""{"code":-1,"msg":"code param error"}""")]
    [InlineData("""{"code":0,"data":{"other":{}}}""")]
    public void ParseMinuteHistory_NoSeries_ReturnsEmpty(string json)
    {
        TencentParser.ParseMinuteHistory(json, "usAAPL").Should().BeEmpty();
    }

    /// <summary>
    /// Verifies only the most recent hour of history is kept.
    /// </summary>
    [Fact]
    public void ParseMinuteHistory_KeepsTheMostRecentSamples()
    {
        var rows = Enumerable.Range(0, 120).Select(i => $"\"{9 + (i / 60):00}{i % 60:00} {i + 1} 1\"");
        var json = "{\"data\":{\"sh1\":{\"data\":{\"date\":\"20261022\",\"data\":[" + string.Join(',', rows) + "]}}}}";

        var points = TencentParser.ParseMinuteHistory(json, "sh1");

        points.Should().HaveCount(60);
        points[^1].Price.Should().Be(120m);
    }

    /// <summary>
    /// Verifies the provider publishes quotes under the symbol that was subscribed, however it was typed.
    /// </summary>
    [Fact]
    public async Task Provider_PublishesUnderTheSubscribedSymbol()
    {
        var handler = new StubHttpHandler(_ => Gbk(QuoteLines));
        await using var provider = CreateProvider(handler, new FakeTimeProvider());
        var typed = new Asset("tencent", "SH600000", "浦发银行", AssetClass.Equity);

        await provider.SetSubscriptionsAsync([typed]);
        var quote = (await ReadAsync(provider, 1)).Single();

        quote.AssetId.Should().Be(typed.Id);
        quote.Price.Should().Be(9.62m);
        quote.Reference.Should().Be(9.66m);
        handler.Requests.Single().RequestUri!.AbsoluteUri.Should().EndWith("/q=sh600000");
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
    }

    /// <summary>
    /// Verifies all subscriptions are fetched in one request.
    /// </summary>
    [Fact]
    public async Task Provider_BatchesSubscriptionsIntoOneRequest()
    {
        var handler = new StubHttpHandler(_ => Gbk(QuoteLines));
        await using var provider = CreateProvider(handler, new FakeTimeProvider());
        var tencent = new Asset("tencent", "hk00700", "腾讯控股", AssetClass.Equity);
        var apple = new Asset("tencent", "usAAPL", "苹果", AssetClass.Equity);

        await provider.SetSubscriptionsAsync([Pufa, tencent, apple]);
        var quotes = await ReadAsync(provider, 3);

        quotes.Select(q => q.AssetId.Symbol).Should().BeEquivalentTo("sh600000", "hk00700", "usAAPL");
        handler.Requests.Should().ContainSingle();
        handler.Requests.Single().RequestUri!.AbsoluteUri.Should().Contain("sh600000,hk00700,usAAPL");
    }

    /// <summary>
    /// Verifies a symbol the service does not know is reported while the others keep updating.
    /// </summary>
    [Fact]
    public async Task Provider_UnknownSymbol_IsReportedButOthersContinue()
    {
        var handler = new StubHttpHandler(_ => Gbk(QuoteLines + "\nv_pv_none_match=\"1\";"));
        await using var provider = CreateProvider(handler, new FakeTimeProvider());
        var unknown = new Asset("tencent", "sh999999", "?", AssetClass.Equity);

        await provider.SetSubscriptionsAsync([Pufa, unknown]);
        await ReadAsync(provider, 1);

        await EventuallyAsync(() => provider.Status.Message == "No data for sh999999");
        provider.Status.State.Should().Be(ProviderConnectionState.Connected);
    }

    /// <summary>
    /// Verifies the provider polls again after the interval.
    /// </summary>
    [Fact]
    public async Task Provider_PollsAgainAfterTheInterval()
    {
        var handler = new StubHttpHandler(_ => Gbk(QuoteLines));
        var time = new FakeTimeProvider();
        await using var provider = CreateProvider(handler, time);

        await provider.SetSubscriptionsAsync([Pufa]);
        await EventuallyAsync(() => handler.Requests.Count == 1);

        await AdvanceUntilAsync(time, TimeSpan.FromSeconds(5), () => handler.Requests.Count >= 2);
    }

    /// <summary>
    /// Verifies the provider reports failure after repeated cycles without any success, backing off in between.
    /// </summary>
    [Fact]
    public async Task Provider_RepeatedFailures_ReportsFailed()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var time = new FakeTimeProvider();
        await using var provider = CreateProvider(handler, time);

        await provider.SetSubscriptionsAsync([Pufa]);
        await EventuallyAsync(() => handler.Requests.Count == 1);
        provider.Status.State.Should().NotBe(ProviderConnectionState.Failed);

        // Every failed cycle doubles the delay, so step the clock far enough to cross each wait.
        await AdvanceUntilAsync(time, TimeSpan.FromSeconds(30), () => provider.Status.State == ProviderConnectionState.Failed);
    }

    /// <summary>
    /// Verifies an exception that is not an HTTP error is a failed cycle, not the end of polling, and the provider
    /// recovers on the next cycle.
    /// </summary>
    [Fact]
    public async Task Provider_UnexpectedException_ReportsAndKeepsPolling()
    {
        var calls = 0;
        var handler = new StubHttpHandler(_ =>
            Interlocked.Increment(ref calls) == 1 ? throw new InvalidOperationException("surprise") : Gbk(QuoteLines));
        var time = new FakeTimeProvider();
        await using var provider = CreateProvider(handler, time);

        await provider.SetSubscriptionsAsync([Pufa]);
        await EventuallyAsync(() => provider.Status is { State: ProviderConnectionState.Reconnecting, Message: "surprise" });

        await AdvanceUntilAsync(time, TimeSpan.FromSeconds(10), () => provider.Status.State == ProviderConnectionState.Connected);
        (await ReadAsync(provider, 1)).Single().Price.Should().Be(9.62m);
    }

    /// <summary>
    /// Verifies the provider searches with the query URL-encoded and filters by asset class.
    /// </summary>
    [Fact]
    public async Task Provider_Search_FiltersByAssetClass()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SearchResponse) });
        await using var provider = CreateProvider(handler, new FakeTimeProvider());
        var search = (ISymbolSearchProvider)provider;

        var indices = await search.SearchAsync("腾讯", AssetClass.Index);
        var forex = await search.SearchAsync("腾讯", AssetClass.Forex);

        indices.Select(a => a.Symbol).Should().Equal("sh000847");
        forex.Should().BeEmpty();
        handler.Requests.Single().RequestUri!.AbsoluteUri.Should().Contain("q=%E8%85%BE%E8%AE%AF");
    }

    /// <summary>
    /// Verifies history is empty when the service sends no full series.
    /// </summary>
    [Fact]
    public async Task Provider_History_IsEmptyWithoutSeries()
    {
        var handler = new StubHttpHandler(_ => Json("""{"code":0,"data":{"usAAPL":{"data":{"date":"","data":["1143 344.77 1"]}}}}"""));
        await using var provider = CreateProvider(handler, new FakeTimeProvider());

        var points = await ((IHistoryProvider)provider).GetHistoryAsync(new Asset("tencent", "usAAPL", "苹果", AssetClass.Equity));

        points.Should().BeEmpty();
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
    private static TencentProvider CreateProvider(StubHttpHandler handler, FakeTimeProvider time)
    {
        var descriptor = new TencentProviderFactory().Descriptor;
        return new TencentProvider(
            descriptor,
            new Uri("https://quotes.example.test"),
            TimeSpan.FromSeconds(5),
            new HttpClient(handler),
            time,
            NullLogger.Instance);
    }

    /// <summary>
    /// Creates a successful response whose body is encoded in GBK, as the service sends it.
    /// </summary>
    /// <param name="text">
    /// The body.
    /// </param>
    /// <returns>
    /// The response.
    /// </returns>
    private static HttpResponseMessage Gbk(string text)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.GetEncoding(936).GetBytes(text)) };
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
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

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
        EventuallyAsync(() =>
        {
            if (condition())
            {
                return true;
            }

            time.Advance(step);
            return false;
        });

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
}
