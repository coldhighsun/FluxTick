using AwesomeAssertions;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using FluxTick.Providers.Eastmoney;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the Eastmoney parser and provider.
/// </summary>
public class EastmoneyTests
{
    /// <summary>
    /// A batch quote answer as captured from the service: a Shanghai stock, the Shanghai index with the same code as a
    /// Shenzhen stock, a Hong Kong stock, a US stock and a suspended one.
    /// </summary>
    private const string QuoteResponse =
        """{"rc":0,"data":{"total":5,"diff":[{"f2":1446.7,"f12":"600519","f13":1,"f18":1460.21,"f124":1795152782},{"f2":12.17,"f12":"000001","f13":0,"f18":12.04,"f124":1795152783},{"f2":340.2,"f12":"00700","f13":116,"f18":348.4,"f124":1795152781},{"f2":349.21,"f12":"AAPL","f13":105,"f18":352.45,"f124":1795122000},{"f2":"-","f12":"600000","f13":1,"f18":"-","f124":0}]}}""";

    /// <summary>
    /// The start of the clock of the tests: Wednesday 10:00 in Shanghai with the market open.
    /// </summary>
    private static readonly DateTimeOffset TradingHoursStart = new(2026, 10, 28, 2, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies quotes are keyed by market and code, so equal codes of two markets do not collide.
    /// </summary>
    [Fact]
    public void ParseQuotes_Batch_KeysByMarketAndCode()
    {
        var quotes = EastmoneyParser.ParseQuotes(QuoteResponse);

        quotes.Select(q => q.Key).Should().Equal("1.600519", "0.000001", "116.00700", "105.AAPL");
        quotes[0].Price.Should().Be(1446.7m);
        quotes[0].PreviousClose.Should().Be(1460.21m);
    }

    /// <summary>
    /// Verifies a suspended instrument, whose price is a dash, is left out.
    /// </summary>
    [Fact]
    public void ParseQuotes_SuspendedInstrument_IsLeftOut()
    {
        EastmoneyParser.ParseQuotes(QuoteResponse).Should().NotContain(q => q.Key == "1.600000");
    }

    /// <summary>
    /// Verifies a time after the end of a session is pulled back to the end of that session.
    /// </summary>
    [Fact]
    public void ParseQuotes_TimeAfterTheClose_IsClampedToTheSessionEnd()
    {
        // 16:30 in Shanghai on Wednesday 28 October 2026.
        var stamp = new DateTimeOffset(2026, 10, 28, 8, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var json = $$$"""{"data":{"diff":[{"f2":10.5,"f12":"600000","f13":1,"f18":10.0,"f124":{{{stamp}}}}]}}""";

        var quote = EastmoneyParser.ParseQuotes(json).Single();

        quote.Time.Should().Be(new DateTimeOffset(2026, 10, 28, 7, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// Verifies an answer without data is read as no quotes.
    /// </summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"rc":0,"data":null}""")]
    [InlineData("""{"data":{"diff":[]}}""")]
    public void ParseQuotes_NoData_ReturnsEmpty(string json)
    {
        EastmoneyParser.ParseQuotes(json).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the exchange follows the market id of the symbol.
    /// </summary>
    [Theory]
    [InlineData("1.600519", true)]
    [InlineData("0.000001", true)]
    [InlineData("116.00700", true)]
    [InlineData("105.AAPL", true)]
    [InlineData("90.BK1189", false)]
    [InlineData("600519", false)]
    public void ExchangeOf_Symbol_FollowsTheMarketId(string symbol, bool known)
    {
        (EastmoneyParser.ExchangeOf(symbol) is not null).Should().Be(known);
    }

    /// <summary>
    /// Verifies minute candles are read in Beijing time.
    /// </summary>
    [Fact]
    public void ParseKlines_MinuteCandles_AreInBeijingTime()
    {
        const string json = """{"data":{"klines":["2026-11-20 09:31,1452.90","2026-11-20 09:32,1450.00"]}}""";

        var points = EastmoneyParser.ParseKlines(json, "1.600519");

        points.Select(p => p.Price).Should().Equal(1452.90m, 1450.00m);
        points[0].Timestamp.Should().Be(new DateTimeOffset(2026, 11, 20, 1, 31, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// Verifies a daily candle is dated at the close of its exchange.
    /// </summary>
    [Fact]
    public void ParseKlines_DailyCandle_IsDatedAtTheClose()
    {
        const string json = """{"data":{"klines":["2026-11-19,348.400","2026-11-20,340.200"]}}""";

        var points = EastmoneyParser.ParseKlines(json, "116.00700");

        points.Should().HaveCount(2);
        points[1].Timestamp.Should().Be(new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// Verifies rows that are not candles are skipped.
    /// </summary>
    [Fact]
    public void ParseKlines_BadRows_AreSkipped()
    {
        const string json = """{"data":{"klines":["nonsense","2026-11-20 09:31,-","2026-11-20 09:32,5.5"]}}""";

        EastmoneyParser.ParseKlines(json, "1.600519").Select(p => p.Price).Should().Equal(5.5m);
    }

    /// <summary>
    /// Verifies search results are mapped to asset classes and limited to the known markets.
    /// </summary>
    [Fact]
    public void ParseSearch_Results_AreMappedAndFiltered()
    {
        const string json = """{"QuotationCodeTable":{"Data":[{"Code":"600519","Name":"贵州茅台","Classify":"AStock","MktNum":"1","QuoteID":"1.600519"},{"Code":"000001","Name":"上证指数","Classify":"Index","MktNum":"1","QuoteID":"1.000001"},{"Code":"00700","Name":"腾讯控股","Classify":"HK","MktNum":"116","QuoteID":"116.00700"},{"Code":"BK1189","Name":"腾讯概念","Classify":"BK","MktNum":"90","QuoteID":"90.BK1189"},{"Code":"000700","Name":"Eusu","Classify":"KRX","MktNum":"177","QuoteID":"177.000700"},{"Code":"600519","Name":"dup","Classify":"AStock","MktNum":"1","QuoteID":"1.600519"}]}}""";

        var assets = EastmoneyParser.ParseSearch(json, "eastmoney");

        assets.Select(a => a.Symbol).Should().Equal("1.600519", "1.000001", "116.00700");
        assets[1].AssetClass.Should().Be(AssetClass.Index);
        assets[0].DisplayName.Should().Be("贵州茅台");
    }

    /// <summary>
    /// Verifies the provider requests all subscriptions in one batch and publishes under the subscribed symbol.
    /// </summary>
    [Fact]
    public async Task Provider_BatchesSubscriptionsAndPublishesQuotes()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(QuoteResponse));
        await using var provider = CreateProvider(handler);
        var maotai = new Asset("eastmoney", "1.600519", "Kweichow Moutai", AssetClass.Equity);
        var tencent = new Asset("eastmoney", "116.00700", "Tencent", AssetClass.Equity);

        await provider.SetSubscriptionsAsync([maotai, tencent]);
        var quotes = await TestWait.ReadQuotesAsync(provider, 2);

        quotes.Select(q => q.AssetId).Should().BeEquivalentTo([maotai.Id, tencent.Id]);
        quotes.Single(q => q.AssetId == maotai.Id).Price.Should().Be(1446.7m);
        var uri = handler.Requests.First().RequestUri!.AbsoluteUri;
        uri.Should().Contain("secids=1.600519,116.00700").And.Contain("fltt=2");
    }

    /// <summary>
    /// Verifies the history requests count candles of the width each range needs.
    /// </summary>
    [Fact]
    public async Task GetHistoryAsync_Ranges_AskForTheCandlesOfTheRange()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{"data":{"klines":[]}}"""));
        await using var provider = CreateProvider(handler);
        var asset = new Asset("eastmoney", "1.600519", "Kweichow Moutai", AssetClass.Equity);

        await provider.GetHistoryAsync(asset);
        await provider.GetHistoryAsync(asset, HistoryRange.Day);
        await provider.GetHistoryAsync(asset, HistoryRange.Year);

        var queries = handler.Requests.Select(r => r.RequestUri!.Query).ToList();
        queries[0].Should().Contain("klt=1&").And.Contain("lmt=65&");
        queries[1].Should().Contain("klt=5&").And.Contain($"lmt={HistoryRangeSpec.For(HistoryRange.Day).Capacity}&");
        queries[2].Should().Contain("klt=101&").And.Contain($"lmt={HistoryRangeSpec.For(HistoryRange.Year).Capacity}&");
    }

    /// <summary>
    /// Verifies an instrument of an unknown market gets no history and costs no request.
    /// </summary>
    [Fact]
    public async Task GetHistoryAsync_UnknownMarket_ReturnsEmptyWithoutRequest()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("{}"));
        await using var provider = CreateProvider(handler);

        var points = await provider.GetHistoryAsync(new Asset("eastmoney", "90.BK1189", "Concept", AssetClass.Equity), HistoryRange.Month);

        points.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the provider searches with the query URL-encoded and filters by asset class.
    /// </summary>
    [Fact]
    public async Task SearchAsync_Query_IsEncodedAndFilteredByClass()
    {
        const string json = """{"QuotationCodeTable":{"Data":[{"Name":"A","Classify":"AStock","MktNum":"1","QuoteID":"1.600519"},{"Name":"I","Classify":"Index","MktNum":"1","QuoteID":"1.000001"}]}}""";
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(json));
        await using var provider = CreateProvider(handler);

        var found = await provider.SearchAsync("贵 州", AssetClass.Index);

        found.Select(a => a.Symbol).Should().Equal("1.000001");
        handler.Requests.Single().RequestUri!.AbsoluteUri.Should().Contain("input=%E8%B4%B5%20%E5%B7%9E");
    }

    /// <summary>
    /// Creates a provider with the clock at the start of the trading hours.
    /// </summary>
    /// <param name="handler">
    /// The stub that answers the requests.
    /// </param>
    /// <returns>
    /// The provider.
    /// </returns>
    private static EastmoneyProvider CreateProvider(StubHttpHandler handler) =>
        new(
            new EastmoneyProviderFactory().Descriptor,
            new Uri("https://quote.example.test"),
            TimeSpan.FromSeconds(5),
            new HttpClient(handler),
            new FakeTimeProvider(TradingHoursStart),
            NullLogger.Instance);
}
