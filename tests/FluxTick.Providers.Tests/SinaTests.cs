using System.Net;
using System.Text;
using FluxTick.Abstractions;
using FluxTick.Providers.Sina;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the Sina parser and provider.
/// </summary>
public class SinaTests
{
    /// <summary>
    /// Quote lines as captured from the service: a Shanghai stock, a Shenzhen stock, an index, a stock that is
    /// suspended with a previous close, a stock with no data at all, and a code the service does not know.
    /// </summary>
    private const string QuoteLines = """
        var hq_str_sh600000="浦发银行,9.560,9.620,9.810,9.830,9.530,9.810,9.820,44050194,428084559.000,242400,9.810,63700,9.800,51200,9.790,30000,9.780,44300,9.770,230400,9.820,698200,9.830,458000,9.840,1059633,9.850,660415,9.860,2026-10-27,13:13:41,00,";
        var hq_str_sz000001="平安银行,11.270,11.300,11.510,11.520,11.240,11.500,11.510,97164359,1108130803.680,31000,11.500,447967,11.490,156700,11.480,169500,11.470,130700,11.460,1000596,11.510,1971600,11.520,1212900,11.530,801700,11.540,1165200,11.550,2026-10-27,13:13:39,00";
        var hq_str_sh000001="上证指数,3698.4405,3697.5199,3685.8207,3708.1981,3682.0332,0,0,258377258,418732222969,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,2026-10-27,13:13:38,00,";
        var hq_str_sh600001="邯郸钢铁,0.000,5.130,0.000,0.000,0.000,0.000,0.000,0,0.000,0,0.000,0,0.000,0,0.000,0,0.000,0,0.000,0,0.000,0,0.000,0,0.000,0,0.000,0,0.000,2026-09-22,11:45:01,-3";
        var hq_str_bj430047="诺思兰德,0.000,0.000,0.000,0.000,0.000,0.000,0.000,0,0.000,0,0.000,0,0.000,0,0.000,0,0.000,0,0.000,0,0.000,0,0.000,0,0.000,0,0.000,0,0.000,2026-09-22,11:45:01,-3";
        var hq_str_sh999999="";
        """;

    /// <summary>
    /// A Beijing line with a live price.
    /// </summary>
    private const string BeijingLine =
        "var hq_str_bj920950=\"迅安科技,20.100,20.000,20.500,20.600,20.000,20.400,20.500,1000,20500.000,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,2026-10-27,13:00:00,00,\";";

    /// <summary>
    /// A search response for a code that matches an index and a stock, and entries the provider must skip.
    /// </summary>
    private const string SearchResponse =
        """var suggestvalue="sh000001,11,000001,sh000001,上证指数,,上证指数,99,1,,,;sz000001,11,000001,sz000001,平安银行,,平安银行,99,1,ESG,,;of510300,22,510300,of510300,沪深300ETF华泰柏瑞,,x,99,1,,,;sz000001,11,000001,sz000001,dup,,dup,99,1,,,;sz399006,11,399006,sz399006,创业板指,,创业板指,99,1,,,";""";

    /// <summary>
    /// The Shanghai test asset.
    /// </summary>
    private static readonly Asset Pufa = new("sina", "sh600000", "浦发银行", AssetClass.Equity);

    /// <summary>
    /// Verifies a Shanghai line is parsed with its previous close and Beijing time.
    /// </summary>
    [Fact]
    public void ParseQuotes_AShare_ReturnsPriceReferenceAndBeijingTime()
    {
        var quote = SinaParser.ParseQuotes(QuoteLines).Single(q => q.Key == "sh600000");

        quote.Price.Should().Be(9.81m);
        quote.PreviousClose.Should().Be(9.62m);
        quote.Time.Should().Be(new DateTimeOffset(2026, 10, 27, 13, 13, 41, TimeSpan.FromHours(8)));
    }

    /// <summary>
    /// Verifies a line without the trailing comma, as Shenzhen sends it, is parsed as well.
    /// </summary>
    [Fact]
    public void ParseQuotes_ShenzhenLineWithoutTrailingComma_ReturnsQuote()
    {
        var quote = SinaParser.ParseQuotes(QuoteLines).Single(q => q.Key == "sz000001");

        quote.Price.Should().Be(11.51m);
        quote.PreviousClose.Should().Be(11.30m);
        quote.Time.Should().Be(new DateTimeOffset(2026, 10, 27, 13, 13, 39, TimeSpan.FromHours(8)));
    }

    /// <summary>
    /// Verifies an index line, whose bid and ask fields are zero, still yields its level.
    /// </summary>
    [Fact]
    public void ParseQuotes_Index_ReturnsLevel()
    {
        var quote = SinaParser.ParseQuotes(QuoteLines).Single(q => q.Key == "sh000001");

        quote.Price.Should().Be(3685.8207m);
        quote.PreviousClose.Should().Be(3697.5199m);
    }

    /// <summary>
    /// Verifies a zero price falls back to the previous close, with the old quote time so the app shows it as stale.
    /// </summary>
    [Fact]
    public void ParseQuotes_ZeroPrice_FallsBackToPreviousClose()
    {
        var quote = SinaParser.ParseQuotes(QuoteLines).Single(q => q.Key == "sh600001");

        quote.Price.Should().Be(5.13m);
        quote.PreviousClose.Should().Be(5.13m);
        quote.Time.Should().Be(new DateTimeOffset(2026, 9, 22, 11, 45, 1, TimeSpan.FromHours(8)));
    }

    /// <summary>
    /// Verifies lines with neither price nor previous close, empty bodies and noise are skipped.
    /// </summary>
    [Fact]
    public void ParseQuotes_SkipsLinesWithoutAPrice()
    {
        var keys = SinaParser.ParseQuotes(QuoteLines + "\nnoise\nvar hq_str_x=\"a,b\";").Select(q => q.Key);

        keys.Should().Equal("sh600000", "sz000001", "sh000001", "sh600001");
    }

    /// <summary>
    /// Verifies a line without a time field gets no quote time.
    /// </summary>
    [Fact]
    public void ParseQuotes_ShortLine_HasNoTime()
    {
        var quote = SinaParser.ParseQuotes("var hq_str_sz000001=\"平安银行,11.2,11.0,11.1\";").Should().ContainSingle().Subject;

        quote.Time.Should().BeNull();
    }

    /// <summary>
    /// Verifies a symbol typed in the wrong case is brought into the form the service expects.
    /// </summary>
    [Theory]
    [InlineData("SH600000", "sh600000")]
    [InlineData("sh600000", "sh600000")]
    [InlineData("  Sz000001 ", "sz000001")]
    public void ToRequestCode_NormalizesCase(string typed, string expected)
    {
        SinaParser.ToRequestCode(typed).Should().Be(expected);
    }

    /// <summary>
    /// Verifies search results are mapped to asset classes, and funds, duplicates and other markets are skipped.
    /// </summary>
    [Fact]
    public void ParseSearch_MapsTypesAndSkipsUnsupported()
    {
        var assets = SinaParser.ParseSearch(SearchResponse, "sina");

        assets.Select(a => (a.Symbol, a.AssetClass)).Should().Equal(
            ("sh000001", AssetClass.Index),
            ("sz000001", AssetClass.Equity),
            ("sz399006", AssetClass.Index));
        assets[0].DisplayName.Should().Be("上证指数");
        assets.Should().OnlyContain(a => a.ProviderId == "sina");
    }

    /// <summary>
    /// Verifies the no-match answer yields no results.
    /// </summary>
    [Theory]
    [InlineData("""var suggestvalue="";""")]
    [InlineData("")]
    [InlineData("garbage")]
    public void ParseSearch_NoMatch_ReturnsEmpty(string text)
    {
        SinaParser.ParseSearch(text, "sina").Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the provider publishes quotes under the symbol that was subscribed and sends the referrer the
    /// service insists on.
    /// </summary>
    [Fact]
    public async Task Provider_PublishesUnderTheSubscribedSymbol_AndSendsReferrer()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Gbk(QuoteLines));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart));
        var typed = new Asset("sina", "SH600000", "浦发银行", AssetClass.Equity);

        await provider.SetSubscriptionsAsync([typed]);
        var quote = (await TestWait.ReadQuotesAsync(provider, 1)).Single();

        quote.AssetId.Should().Be(typed.Id);
        quote.Price.Should().Be(9.81m);
        quote.Reference.Should().Be(9.62m);
        var request = handler.Requests.Single();
        request.RequestUri!.AbsoluteUri.Should().EndWith("/list=sh600000");
        request.Headers.Referrer.Should().Be(new Uri("https://finance.sina.com.cn/"));
    }

    /// <summary>
    /// Verifies all subscriptions are fetched in one request.
    /// </summary>
    [Fact]
    public async Task Provider_BatchesSubscriptionsIntoOneRequest()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Gbk(QuoteLines));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart));
        var ping = new Asset("sina", "sz000001", "平安银行", AssetClass.Equity);
        var composite = new Asset("sina", "sh000001", "上证指数", AssetClass.Index);

        await provider.SetSubscriptionsAsync([Pufa, ping, composite]);
        var quotes = await TestWait.ReadQuotesAsync(provider, 3);

        quotes.Select(q => q.AssetId.Symbol).Should().BeEquivalentTo("sh600000", "sz000001", "sh000001");
        handler.Requests.Should().ContainSingle();
        handler.Requests.Single().RequestUri!.AbsoluteUri.Should().Contain("sh600000,sz000001,sh000001");
    }

    /// <summary>
    /// Verifies a quote endpoint with a path keeps its prefix in the request.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_QuoteEndpointWithPath_KeepsItsPrefix()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Gbk(QuoteLines));
        await using var provider = new SinaProvider(
            new SinaProviderFactory().Descriptor,
            new Uri("https://proxy.example.test/hq/"),
            Unreachable(),
            TimeSpan.FromSeconds(5),
            new HttpClient(handler),
            new FakeTimeProvider(TradingHoursStart),
            NullLogger.Instance);

        await provider.SetSubscriptionsAsync([Pufa]);
        await TestWait.EventuallyAsync(() => !handler.Requests.IsEmpty);

        handler.Requests.First().RequestUri!.PathAndQuery.Should().Be("/hq/list=sh600000");
    }

    /// <summary>
    /// Lines pushed by the stream, as captured: no <c>var hq_str_</c> prefix and no quotes, one line per instrument.
    /// </summary>
    private const string StreamLines =
        "sh600000=浦发银行,9.800,9.820,9.740,9.930,9.730,9.730,9.740,39963535,393524574.000,300900,9.730,396400,9.720,230300,9.710,520000,9.700,86700,9.690,98500,9.740,73000,9.750,67300,9.760,20600,9.770,39000,9.780,2026-10-28,13:18:21,00,\n"
        + "sh000001=上证指数,3676.9185,3648.3010,3679.7057,3684.4906,3652.3658,0,0,297554048,522183357436,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,2026-10-28,13:19:02,00,\n";

    /// <summary>
    /// Verifies the lines of the stream are parsed like those of the quote service.
    /// </summary>
    [Fact]
    public void ParseStream_PushedLines_ReturnPriceReferenceAndBeijingTime()
    {
        var quotes = SinaParser.ParseStream(StreamLines);

        quotes.Select(q => q.Key).Should().Equal("sh600000", "sh000001");
        quotes[0].Price.Should().Be(9.74m);
        quotes[0].PreviousClose.Should().Be(9.82m);
        quotes[0].Time.Should().Be(new DateTimeOffset(2026, 10, 28, 13, 18, 21, TimeSpan.FromHours(8)));
        quotes[1].Price.Should().Be(3679.7057m);
    }

    /// <summary>
    /// Verifies text that is not a line of prices is skipped instead of throwing.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("=x,1")]
    [InlineData("sh600000=")]
    [InlineData("sh600000=name,1,2")]
    public void ParseStream_NotPriceLines_ReturnsEmpty(string text)
    {
        SinaParser.ParseStream(text).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a symbol the service does not know gives no quote while the others keep updating.
    /// </summary>
    [Fact]
    public async Task Provider_UnknownSymbol_StillPublishesOthers()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Gbk(QuoteLines));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart));
        var unknown = new Asset("sina", "sh999999", "?", AssetClass.Equity);

        await provider.SetSubscriptionsAsync([Pufa, unknown]);
        var quotes = await TestWait.ReadQuotesAsync(provider, 1);

        quotes.Single().AssetId.Symbol.Should().Be("sh600000");
    }

    /// <summary>
    /// Verifies the stream connects with the Origin it insists on and the instruments in the address, and publishes the
    /// pushed lines under the subscribed symbol.
    /// </summary>
    [Fact]
    public async Task Stream_Connect_SendsOriginAndCodesAndPublishesLines()
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart), server.Uri);
        var typed = new Asset("sina", "SH600000", "浦发银行", AssetClass.Equity);

        await provider.SetSubscriptionsAsync([typed]);
        await TestWait.EventuallyAsync(() => server.RequestUrls.Count == 1);
        await server.SendAsync(StreamLines);

        var quote = (await TestWait.ReadQuotesAsync(provider, 1)).Single();
        quote.AssetId.Should().Be(typed.Id);
        quote.Price.Should().Be(9.74m);
        quote.Reference.Should().Be(9.82m);
        server.RequestUrls.Should().Equal("/?list=sh600000");
        server.Origins.Should().Equal("https://finance.sina.com.cn");
    }

    /// <summary>
    /// Verifies a change of the watchlist connects again with the new list.
    /// </summary>
    [Fact]
    public async Task Stream_SubscriptionChange_ConnectsAgainWithTheNewList()
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time, server.Uri);
        var ping = new Asset("sina", "sz000001", "平安银行", AssetClass.Equity);
        await provider.SetSubscriptionsAsync([Pufa]);
        await TestWait.EventuallyAsync(() => server.ConnectionCount == 1);

        await provider.SetSubscriptionsAsync([Pufa, ping]);

        // The short wait that lets several changes share one reconnect runs on the fake clock.
        await TestWait.EventuallyAsync(() =>
        {
            time.Advance(TimeSpan.FromSeconds(1));
            return server.RequestUrls.Count == 2;
        });
        server.RequestUrls.Should().Equal("/?list=sh600000", "/?list=sh600000,sz000001");
    }

    /// <summary>
    /// Verifies a new instrument gets its first price from the quote service even while the stream is up, and the
    /// service is not polled again while the stream is connected.
    /// </summary>
    [Fact]
    public async Task Snapshot_NewInstrument_IsFetchedOnceWhileTheStreamIsUp()
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHttpHandler(_ => StubHttpHandler.Gbk(QuoteLines));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time, server.Uri);

        await provider.SetSubscriptionsAsync([Pufa]);
        var quote = (await TestWait.ReadQuotesAsync(provider, 1)).Single();
        await TestWait.EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
        for (var i = 0; i < 4; i++)
        {
            time.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(TimeSpan.FromMilliseconds(30));
        }

        quote.Price.Should().Be(9.81m);
        handler.Requests.Should().HaveCount(1, "only the snapshot of the new instrument was requested");
    }

    /// <summary>
    /// Verifies the quote service takes over while the stream is down, and each failed round doubles the wait.
    /// </summary>
    [Fact]
    public async Task Fallback_StreamDownAndRequestsFail_BackOff()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time);
        await provider.SetSubscriptionsAsync([Pufa]);
        await TestWait.EventuallyAsync(() => handler.Requests.Count == 1);

        await AdvanceUntilRequestsAsync(handler, time, 2);
        var elapsed = await AdvanceUntilRequestsAsync(handler, time, 3);

        elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10), "the 5 second wait doubled");
    }

    /// <summary>
    /// Verifies prices that keep arriving from the quote service while the stream is down make the source connected, not
    /// reconnecting, so alerts and holdings keep working.
    /// </summary>
    [Fact]
    public async Task Status_StreamDownButPollingWorks_IsConnected()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Gbk(QuoteLines));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time);
        await provider.SetSubscriptionsAsync([Pufa]);
        await TestWait.EventuallyAsync(() => handler.Requests.Count == 1);

        await AdvanceUntilRequestsAsync(handler, time, 2);

        await TestWait.EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
        provider.Status.Message.Should().StartWith("Live stream unavailable");
    }

    /// <summary>
    /// Verifies the source is reported as failed when neither the stream nor the quote service delivers, as when the
    /// referrer is refused.
    /// </summary>
    [Fact]
    public async Task Status_StreamDownAndPollingForbiddenRepeatedly_IsFailed()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time);

        await provider.SetSubscriptionsAsync([Pufa]);

        // Every failed round doubles the wait, so step the clock far enough to cross each one.
        await TestWait.EventuallyAsync(() =>
        {
            if (provider.Status.State == ProviderConnectionState.Failed)
            {
                return true;
            }

            time.Advance(TimeSpan.FromSeconds(30));
            return false;
        });
    }

    /// <summary>
    /// Verifies a symbol the service has no data for is shown in the status while the stream is up and the others keep
    /// updating.
    /// </summary>
    [Fact]
    public async Task Status_SymbolWithoutData_IsShownWhileTheStreamIsUp()
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHttpHandler(_ => StubHttpHandler.Gbk(QuoteLines));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart), server.Uri);
        var unknown = new Asset("sina", "sh999999", "?", AssetClass.Equity);

        await provider.SetSubscriptionsAsync([Pufa, unknown]);

        await TestWait.EventuallyAsync(() => provider.Status is { State: ProviderConnectionState.Connected, Message: "No data for sh999999" });
    }

    /// <summary>
    /// Verifies a silent stream whose address names the instruments is dropped and connected again to the same address
    /// when polling keeps finding newer prices.
    /// </summary>
    [Fact]
    public async Task Stream_SilentWhilePollingFindsNewerPrices_ConnectsAgainToTheSameAddress()
    {
        await using var server = LoopbackServer.Start();
        var round = 0;
        var handler = new StubHttpHandler(_ => StubHttpHandler.Gbk(
            QuoteLines.Replace("13:13:41", $"13:{13 + Math.Min(Interlocked.Increment(ref round), 40):00}:41", StringComparison.Ordinal)));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, time, server.Uri);
        await provider.SetSubscriptionsAsync([Pufa]);
        await TestWait.EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);

        await TestWait.EventuallyAsync(() =>
        {
            time.Advance(TimeSpan.FromSeconds(5));
            return server.RequestUrls.Count >= 2;
        });

        server.RequestUrls.Should().Equal("/?list=sh600000", "/?list=sh600000");
    }

    /// <summary>
    /// Verifies the descriptor now describes a streaming source with an endpoint for the stream.
    /// </summary>
    [Fact]
    public void Descriptor_IsStreamingWithAStreamEndpoint()
    {
        var descriptor = new SinaProviderFactory().Descriptor;

        descriptor.UpdateMode.Should().Be(ProviderUpdateMode.Streaming);
        descriptor.OptionDefinitions.Should().Contain(o => o.Key == SinaProviderFactory.WebSocketUrlKey);
    }

    /// <summary>
    /// Verifies the provider searches with the query URL-encoded, sends the referrer and filters by asset class.
    /// </summary>
    [Fact]
    public async Task Provider_Search_FiltersByAssetClass()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Gbk(SearchResponse));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart));
        var search = (ISymbolSearchProvider)provider;

        var indices = await search.SearchAsync("上证", AssetClass.Index);
        var forex = await search.SearchAsync("上证", AssetClass.Forex);

        indices.Select(a => a.Symbol).Should().Equal("sh000001", "sz399006");
        forex.Should().BeEmpty();
        var request = handler.Requests.Single();
        request.RequestUri!.AbsoluteUri.Should().Contain("key=%E4%B8%8A%E8%AF%81");
        request.Headers.Referrer.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies a zero price is skipped when the line carries no time, because the previous close would look current.
    /// </summary>
    [Fact]
    public void ParseQuotes_ZeroPriceWithoutTime_IsSkipped()
    {
        SinaParser.ParseQuotes("var hq_str_sh600001=\"x,0.000,5.130,0.000\";").Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a Beijing line is parsed like the others.
    /// </summary>
    [Fact]
    public void ParseQuotes_BeijingLine_ReturnsQuote()
    {
        var quote = SinaParser.ParseQuotes(BeijingLine).Should().ContainSingle().Subject;

        quote.Key.Should().Be("bj920950");
        quote.Price.Should().Be(20.5m);
        quote.PreviousClose.Should().Be(20m);
    }

    /// <summary>
    /// Verifies the indices of all three exchanges are told from stocks by their code range.
    /// </summary>
    [Fact]
    public void ParseSearch_IndicesOfEveryExchange_AreIndices()
    {
        const string text = """var suggestvalue="a,11,000300,sh000300,沪深300,,,99,1,,,;b,11,399001,sz399001,深证成指,,,99,1,,,;c,11,899050,bj899050,北证50,,,99,1,,,;d,11,920950,bj920950,迅安科技,,,99,1,,,";""";

        var assets = SinaParser.ParseSearch(text, "sina");

        assets.Select(a => a.AssetClass).Should().Equal(AssetClass.Index, AssetClass.Index, AssetClass.Index, AssetClass.Equity);
    }

    /// <summary>
    /// Verifies a watchlist larger than one batch is requested in several requests that together cover every code.
    /// </summary>
    [Fact]
    public async Task Provider_MoreThanOneBatch_SplitsIntoSeveralRequests()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Gbk(QuoteLines));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart));
        var assets = Enumerable.Range(0, 120)
            .Select(i => new Asset("sina", $"sh{600000 + i}", $"S{i}", AssetClass.Equity))
            .ToArray();

        await provider.SetSubscriptionsAsync(assets);
        await TestWait.EventuallyAsync(() => handler.Requests.Count == 3);

        var codes = handler.Requests
            .Select(r => r.RequestUri!.AbsoluteUri[(r.RequestUri.AbsoluteUri.IndexOf("/list=", StringComparison.Ordinal) + 6)..].Split(','))
            .ToArray();
        codes.Should().OnlyContain(c => c.Length <= 50);
        codes.SelectMany(c => c).Should().BeEquivalentTo(assets.Select(a => a.Symbol));
    }

    /// <summary>
    /// Verifies a Beijing symbol is requested and published like a Shanghai one.
    /// </summary>
    [Fact]
    public async Task Provider_BeijingSymbol_IsPublished()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Gbk(BeijingLine));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart));

        await provider.SetSubscriptionsAsync([new Asset("sina", "bj920950", "迅安科技", AssetClass.Equity)]);
        var quote = (await TestWait.ReadQuotesAsync(provider, 1)).Single();

        quote.Price.Should().Be(20.5m);
        handler.Requests.Single().RequestUri!.AbsoluteUri.Should().EndWith("/list=bj920950");
    }

    /// <summary>
    /// The start of the clock of the tests: Tuesday 13:13:41 in Shanghai, in the afternoon session and the time of the quote
    /// lines the tests serve. The fallback only polls while the market is open, and the default of the fake clock is a
    /// Saturday.
    /// </summary>
    private static readonly DateTimeOffset TradingHoursStart = new(2026, 10, 27, 5, 13, 41, TimeSpan.Zero);

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
    /// The stream endpoint; an address nobody listens on when omitted, so the stream is down.
    /// </param>
    /// <returns>
    /// The provider.
    /// </returns>
    private static SinaProvider CreateProvider(StubHttpHandler handler, FakeTimeProvider time, Uri? streamUri = null)
    {
        return new(
            new SinaProviderFactory().Descriptor,
            new Uri("https://quotes.example.test"),
            streamUri ?? Unreachable(),
            TimeSpan.FromSeconds(5),
            new HttpClient(handler),
            time,
            NullLogger.Instance,
            TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Lines of a currency pair and a future as captured: the pair has its bid, ask and last price, the future its last
    /// price first and the previous settlement later.
    /// </summary>
    private const string ForexAndFuturesLines = """
        var hq_str_fx_seurusd="14:11:00,1.111800,1.111900,1.112200,17,1.112600,1.113200,1.111500,1.111800,欧元兑美元即期汇率,-0.040000,-0.000400,0.001528,,1.207800,1.111400,,2026-10-28";
        var hq_str_hf_GC="4042.168,,4042.000,4042.200,4084.100,4041.800,14:11:08,4074.600,4073.600,0,3,1,2026-10-28,纽约黄金,0";
        var hq_str_hf_XAU="4029.27,4060.130,4029.27,4029.62,4070.74,4028.56,14:11:00,4060.13,4061.17,0,0,0,2026-10-28,伦敦金（现货黄金）";
        var hq_str_hf_SB="";
        """;

    /// <summary>
    /// Verifies a currency pair takes its last price, not the bid, and the previous close from the places of its own layout.
    /// </summary>
    [Fact]
    public void ParseQuotes_CurrencyPair_ReturnsLastPriceAndPreviousClose()
    {
        var quote = SinaParser.ParseQuotes(ForexAndFuturesLines).Single(q => q.Key == "fx_seurusd");

        quote.Price.Should().Be(1.1118m);
        quote.PreviousClose.Should().Be(1.1122m);
        quote.Time.Should().Be(new DateTimeOffset(2026, 10, 28, 14, 11, 0, TimeSpan.FromHours(8)));
    }

    /// <summary>
    /// Verifies a future takes its price first, the previous settlement as previous close, and its time and date from the
    /// places of its own layout, also when the second field is filled as it is for London spot gold.
    /// </summary>
    [Fact]
    public void ParseQuotes_Futures_ReturnPriceAndPreviousSettlement()
    {
        var quotes = SinaParser.ParseQuotes(ForexAndFuturesLines);

        var gold = quotes.Single(q => q.Key == "hf_gc");
        gold.Price.Should().Be(4042.168m);
        gold.PreviousClose.Should().Be(4074.6m);
        gold.Time.Should().Be(new DateTimeOffset(2026, 10, 28, 14, 11, 8, TimeSpan.FromHours(8)));
        quotes.Single(q => q.Key == "hf_xau").Price.Should().Be(4029.27m);
        quotes.Should().NotContain(q => q.Key == "hf_sb", "the service has no data for that code");
    }

    /// <summary>
    /// Verifies the stream pushes currency pairs and futures in the same lines.
    /// </summary>
    [Fact]
    public void ParseStream_CurrencyPairAndFuture_AreParsed()
    {
        const string text = "fx_susdcny=14:13:01,6.6674000000,6.6675000000,6.6670000000,34.0000000000,6.6672000000,6.6701000000,6.6667000000,6.6674000000,在岸人民币,0.0060,0.0004,0.0034,此行情由新浪财经计算得出,0.0000,0.0000,,2026-10-28\n"
            + "hf_CL=101.532,,101.520,101.530,101.640,99.750,14:11:09,99.900,100.170,0,1,2,2026-10-28,纽约原油,0\n";

        var quotes = SinaParser.ParseStream(text);

        quotes.Select(q => (q.Key, q.Price, q.PreviousClose)).Should().Equal(
            ("fx_susdcny", 6.6674m, 6.667m),
            ("hf_cl", 101.532m, 99.9m));
    }

    /// <summary>
    /// Verifies the codes are brought into the case the service answers to: futures upper, everything else lower.
    /// </summary>
    [Theory]
    [InlineData("SH600000", "sh600000")]
    [InlineData("FX_SEURUSD", "fx_seurusd")]
    [InlineData("hf_gc", "hf_GC")]
    [InlineData(" HF_Xau ", "hf_XAU")]
    public void ToRequestCode_Symbol_UsesTheCaseOfTheService(string symbol, string expected)
    {
        SinaParser.ToRequestCode(symbol).Should().Be(expected);
    }

    /// <summary>
    /// Verifies a search for a class the service cannot search needs no request and finds the listed instruments by code,
    /// name or English name.
    /// </summary>
    [Theory]
    [InlineData("gold", AssetClass.Commodity, new[] { "hf_GC", "hf_XAU" })]
    [InlineData("黄金", AssetClass.Commodity, new[] { "hf_GC", "hf_XAU" })]
    [InlineData("hf_c", AssetClass.Commodity, new[] { "hf_CL", "hf_CAD", "hf_C", "hf_CT" })]
    [InlineData("eur", AssetClass.Forex, new[] { "fx_seurusd", "fx_seurcny", "fx_seurjpy" })]
    [InlineData("gold", AssetClass.Forex, new string[0])]
    public async Task Provider_SearchCurrencyPairsAndFutures_UsesTheList(string query, AssetClass assetClass, string[] expected)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Gbk(SearchResponse));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart));

        var found = await ((ISymbolSearchProvider)provider).SearchAsync(query, assetClass);

        found.Select(a => a.Symbol).Should().BeEquivalentTo(expected);
        found.Should().OnlyContain(a => a.AssetClass == assetClass);
        handler.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a search without a class asks the service for stocks and adds the listed instruments.
    /// </summary>
    [Fact]
    public async Task Provider_SearchWithoutClass_AddsTheListToTheStocks()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Gbk(SearchResponse));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart));

        var found = await ((ISymbolSearchProvider)provider).SearchAsync("gold", null);

        found.Select(a => a.Symbol).Should().Equal("sh000001", "sz000001", "sz399006", "hf_GC", "hf_XAU");
        handler.Requests.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies a future typed in lower case is requested in the case the service answers to and published under the
    /// symbol that was subscribed.
    /// </summary>
    [Fact]
    public async Task Provider_FutureTypedInLowerCase_IsRequestedAndPublishedUnderTheSubscribedSymbol()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Gbk(ForexAndFuturesLines));
        await using var provider = CreateProvider(handler, new FakeTimeProvider(TradingHoursStart));
        var gold = new Asset("sina", "hf_gc", "纽约黄金", AssetClass.Commodity);

        await provider.SetSubscriptionsAsync([gold]);
        var quote = (await TestWait.ReadQuotesAsync(provider, 1)).Single();

        quote.AssetId.Should().Be(gold.Id);
        quote.Price.Should().Be(4042.168m);
        handler.Requests.Single().RequestUri!.AbsoluteUri.Should().EndWith("/list=hf_GC");
    }

    /// <summary>
    /// Verifies the descriptor offers currency pairs and commodities too.
    /// </summary>
    [Fact]
    public void Descriptor_OffersCurrencyPairsAndCommodities()
    {
        new SinaProviderFactory().Descriptor.SupportedAssetClasses.Should().Contain([AssetClass.Forex, AssetClass.Commodity]);
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
    private static async Task<TimeSpan> AdvanceUntilRequestsAsync(StubHttpHandler handler, FakeTimeProvider time, int requests)
    {
        var start = time.GetUtcNow();
        await TestWait.EventuallyAsync(() =>
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
}
