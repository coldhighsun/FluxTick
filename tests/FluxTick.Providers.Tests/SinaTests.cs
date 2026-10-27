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
        await using var provider = CreateProvider(handler, new FakeTimeProvider());
        var typed = new Asset("sina", "SH600000", "浦发银行", AssetClass.Equity);

        await provider.SetSubscriptionsAsync([typed]);
        var quote = (await TestWait.ReadQuotesAsync(provider, 1)).Single();

        quote.AssetId.Should().Be(typed.Id);
        quote.Price.Should().Be(9.81m);
        quote.Reference.Should().Be(9.62m);
        var request = handler.Requests.Single();
        request.RequestUri!.AbsoluteUri.Should().EndWith("/list=sh600000");
        request.Headers.Referrer.Should().Be(new Uri("https://finance.sina.com.cn/"));
        await TestWait.EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
    }

    /// <summary>
    /// Verifies all subscriptions are fetched in one request.
    /// </summary>
    [Fact]
    public async Task Provider_BatchesSubscriptionsIntoOneRequest()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Gbk(QuoteLines));
        await using var provider = CreateProvider(handler, new FakeTimeProvider());
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
            TimeSpan.FromSeconds(5),
            new HttpClient(handler),
            new FakeTimeProvider(),
            NullLogger.Instance);

        await provider.SetSubscriptionsAsync([Pufa]);
        await TestWait.EventuallyAsync(() => !handler.Requests.IsEmpty);

        handler.Requests.First().RequestUri!.PathAndQuery.Should().Be("/hq/list=sh600000");
    }

    /// <summary>
    /// Verifies a symbol the service does not know is reported while the others keep updating.
    /// </summary>
    [Fact]
    public async Task Provider_UnknownSymbol_IsReportedButOthersContinue()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Gbk(QuoteLines));
        await using var provider = CreateProvider(handler, new FakeTimeProvider());
        var unknown = new Asset("sina", "sh999999", "?", AssetClass.Equity);

        await provider.SetSubscriptionsAsync([Pufa, unknown]);
        await TestWait.ReadQuotesAsync(provider, 1);

        await TestWait.EventuallyAsync(() => provider.Status.Message == "No data for sh999999");
        provider.Status.State.Should().Be(ProviderConnectionState.Connected);
    }

    /// <summary>
    /// Verifies the provider reports failure after repeated rejected cycles, as when the referrer is refused.
    /// </summary>
    [Fact]
    public async Task Provider_RepeatedForbidden_ReportsFailed()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var time = new FakeTimeProvider();
        await using var provider = CreateProvider(handler, time);

        await provider.SetSubscriptionsAsync([Pufa]);
        await TestWait.EventuallyAsync(() => handler.Requests.Count == 1);

        // Every failed cycle doubles the delay, so step the clock far enough to cross each wait.
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
    /// Verifies the provider searches with the query URL-encoded, sends the referrer and filters by asset class.
    /// </summary>
    [Fact]
    public async Task Provider_Search_FiltersByAssetClass()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Gbk(SearchResponse));
        await using var provider = CreateProvider(handler, new FakeTimeProvider());
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
        await using var provider = CreateProvider(handler, new FakeTimeProvider());
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
        await using var provider = CreateProvider(handler, new FakeTimeProvider());

        await provider.SetSubscriptionsAsync([new Asset("sina", "bj920950", "迅安科技", AssetClass.Equity)]);
        var quote = (await TestWait.ReadQuotesAsync(provider, 1)).Single();

        quote.Price.Should().Be(20.5m);
        handler.Requests.Single().RequestUri!.AbsoluteUri.Should().EndWith("/list=bj920950");
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
    private static SinaProvider CreateProvider(StubHttpHandler handler, FakeTimeProvider time) =>
        new(
            new SinaProviderFactory().Descriptor,
            new Uri("https://quotes.example.test"),
            TimeSpan.FromSeconds(5),
            new HttpClient(handler),
            time,
            NullLogger.Instance);
}
