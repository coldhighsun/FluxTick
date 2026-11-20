using AwesomeAssertions;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using FluxTick.Providers.TwelveData;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the Twelve Data parser and provider.
/// </summary>
public class TwelveDataTests
{
    /// <summary>
    /// A quote answer for one symbol as captured from the service.
    /// </summary>
    private const string SingleQuote =
        """{"symbol":"AAPL","name":"Apple Inc.","exchange":"NASDAQ","datetime":"2026-11-19","timestamp":1795098600,"last_quote_at":1795121940,"open":"352.91000","close":"349.20999","previous_close":"352.45001","is_market_open":false}""";

    /// <summary>
    /// A quote answer for several symbols, one of which failed.
    /// </summary>
    private const string BatchQuote =
        """{"AAPL":{"symbol":"AAPL","close":"349.21","previous_close":"352.45","timestamp":1795098600},"MSFT":{"symbol":"MSFT","close":"410.5","previous_close":"405.0","last_quote_at":1795121900},"ZZZZ":{"code":400,"message":"symbol not found","status":"error"}}""";

    /// <summary>
    /// Verifies the answer for one symbol is read as the quote itself, dated at the last quote.
    /// </summary>
    [Fact]
    public void ParseQuotes_SingleSymbol_UsesLastQuoteTime()
    {
        var quote = TwelveDataParser.ParseQuotes(SingleQuote).Single();

        quote.Key.Should().Be("AAPL");
        quote.Price.Should().Be(349.20999m);
        quote.PreviousClose.Should().Be(352.45001m);
        quote.Time.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1795121940));
    }

    /// <summary>
    /// Verifies the answer for several symbols is read per symbol, a failed one is skipped and the day's start is the
    /// fallback time.
    /// </summary>
    [Fact]
    public void ParseQuotes_Batch_ReadsEachSymbolAndSkipsFailures()
    {
        var quotes = TwelveDataParser.ParseQuotes(BatchQuote);

        quotes.Select(q => q.Key).Should().Equal("AAPL", "MSFT");
        quotes[0].Time.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1795098600));
        quotes[1].Time.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1795121900));
    }

    /// <summary>
    /// Verifies an error of the whole request, such as a wrong key, is raised.
    /// </summary>
    [Fact]
    public void ParseQuotes_ErrorBody_Throws()
    {
        var act = () => TwelveDataParser.ParseQuotes("""{"code":401,"message":"bad key","status":"error"}""");

        act.Should().Throw<HttpRequestException>().WithMessage("*401*");
    }

    /// <summary>
    /// Verifies candles are read in the time zone of the exchange, oldest first.
    /// </summary>
    [Fact]
    public void ParseTimeSeries_Candles_AreInExchangeTime()
    {
        const string json = """{"meta":{"exchange_timezone":"America/New_York"},"values":[{"datetime":"2026-11-19 15:59:00","close":"349.11"},{"datetime":"2026-11-19 15:58:00","close":"349.23"}],"status":"ok"}""";

        var points = TwelveDataParser.ParseTimeSeries(json);

        points.Select(p => p.Price).Should().Equal(349.23m, 349.11m);

        // 15:59 Eastern Standard Time on 19 November is 20:59 UTC.
        points[1].Timestamp.UtcDateTime.Should().Be(new DateTime(2026, 11, 19, 20, 59, 0, DateTimeKind.Utc));
    }

    /// <summary>
    /// Verifies a daily candle is dated at the close of its day.
    /// </summary>
    [Fact]
    public void ParseTimeSeries_DailyCandle_IsDatedAtTheClose()
    {
        const string json = """{"meta":{"exchange_timezone":"America/New_York"},"values":[{"datetime":"2026-11-19","close":"349.21"}],"status":"ok"}""";

        TwelveDataParser.ParseTimeSeries(json).Single().Timestamp.UtcDateTime
            .Should().Be(new DateTime(2026, 11, 19, 21, 0, 0, DateTimeKind.Utc));
    }

    /// <summary>
    /// Verifies search lists each symbol once and leaves currencies out.
    /// </summary>
    [Fact]
    public void ParseSearch_Results_AreDeduplicatedAndFiltered()
    {
        const string json = """{"data":[{"symbol":"APP","instrument_name":"AppLovin","instrument_type":"Common Stock"},{"symbol":"APP","instrument_name":"AppLovin MX","instrument_type":"Common Stock"},{"symbol":"SPX","instrument_name":"S&P 500","instrument_type":"Index"},{"symbol":"APPL/USD","instrument_name":"x","instrument_type":"Digital Currency"}],"status":"ok"}""";

        var assets = TwelveDataParser.ParseSearch(json, "twelvedata");

        assets.Select(a => a.Symbol).Should().Equal("APP", "SPX");
        assets[1].AssetClass.Should().Be(AssetClass.Index);
    }

    /// <summary>
    /// Verifies the key goes in a header and never in the address.
    /// </summary>
    [Fact]
    public async Task Provider_SendsTheKeyInAHeaderOnly()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(BatchQuote));
        await using var provider = CreateProvider(handler);
        var apple = new Asset("twelvedata", "AAPL", "Apple", AssetClass.Equity);
        var msft = new Asset("twelvedata", "MSFT", "Microsoft", AssetClass.Equity);

        await provider.SetSubscriptionsAsync([apple, msft]);
        var quotes = await TestWait.ReadQuotesAsync(provider, 2);

        quotes.Select(q => q.AssetId).Should().BeEquivalentTo([apple.Id, msft.Id]);
        var request = handler.Requests.First();
        request.RequestUri!.AbsoluteUri.Should().Contain("symbol=AAPL%2CMSFT").And.NotContain("secret-key");
        request.Headers.Authorization!.ToString().Should().Be("apikey secret-key");
    }

    /// <summary>
    /// Verifies the history requests count candles of the width each range needs.
    /// </summary>
    [Fact]
    public async Task GetHistoryAsync_Ranges_AskForTheCandlesOfTheRange()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{"values":[],"status":"ok"}"""));
        await using var provider = CreateProvider(handler);
        var asset = new Asset("twelvedata", "AAPL", "Apple", AssetClass.Equity);

        await provider.GetHistoryAsync(asset);
        await provider.GetHistoryAsync(asset, HistoryRange.Day);
        await provider.GetHistoryAsync(asset, HistoryRange.Year);

        var queries = handler.Requests.Select(r => r.RequestUri!.Query).ToList();
        queries[0].Should().Contain("interval=1min").And.Contain("outputsize=65");
        queries[1].Should().Contain("interval=5min").And.Contain($"outputsize={HistoryRangeSpec.For(HistoryRange.Day).Capacity}");
        queries[2].Should().Contain("interval=1day").And.Contain($"outputsize={HistoryRangeSpec.For(HistoryRange.Year).Capacity}");
    }

    /// <summary>
    /// Creates a provider with a key.
    /// </summary>
    /// <param name="handler">
    /// The stub that answers the requests.
    /// </param>
    /// <returns>
    /// The provider.
    /// </returns>
    private static TwelveDataProvider CreateProvider(StubHttpHandler handler) =>
        new(
            new TwelveDataProviderFactory().Descriptor,
            new Uri("https://api.example.test"),
            "secret-key",
            TimeSpan.FromSeconds(60),
            new HttpClient(handler),
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 15, 0, 0, TimeSpan.Zero)),
            NullLogger.Instance);
}
