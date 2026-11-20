using AwesomeAssertions;
using FluxTick.Abstractions;
using FluxTick.Providers.Finnhub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the Finnhub parser and provider.
/// </summary>
public class FinnhubTests
{
    /// <summary>
    /// A quote answer of the REST API.
    /// </summary>
    private const string QuoteJson = """{"c":261.74,"d":2.1,"dp":0.81,"h":263.31,"l":260.68,"o":261.07,"pc":259.64,"t":1795121940}""";

    /// <summary>
    /// The start of the clock of the tests: Wednesday 10:00 in New York with the market open.
    /// </summary>
    private static readonly DateTimeOffset TradingHoursStart = new(2026, 10, 28, 14, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies the subscribe message names one symbol.
    /// </summary>
    [Fact]
    public void ControlMessage_Symbol_NamesItAlone()
    {
        FinnhubParser.ControlMessage("subscribe", "AAPL").Should().Be("""{"type":"subscribe","symbol":"AAPL"}""");
    }

    /// <summary>
    /// Verifies the trades of a message are read.
    /// </summary>
    [Fact]
    public void ParseTrades_TradeMessage_ReturnsTrades()
    {
        const string json = """{"data":[{"p":261.7,"s":"AAPL","t":1795121940123,"v":100},{"p":261.8,"s":"AAPL","t":1795121941123,"v":10}],"type":"trade"}""";

        var trades = FinnhubParser.ParseTrades(json);

        trades.Select(t => t.Price).Should().Equal(261.7m, 261.8m);
        trades[0].Symbol.Should().Be("AAPL");
        trades[1].Time.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1795121941123));
    }

    /// <summary>
    /// Verifies pings and errors carry no trades.
    /// </summary>
    [Theory]
    [InlineData("""{"type":"ping"}""")]
    [InlineData("""{"type":"error","msg":"Invalid token"}""")]
    [InlineData("""{"data":[{"p":0,"s":"AAPL","t":1,"v":1}],"type":"trade"}""")]
    public void ParseTrades_OtherMessages_ReturnsNone(string json)
    {
        FinnhubParser.ParseTrades(json).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the error of the stream is reported.
    /// </summary>
    [Fact]
    public void TryParseError_ErrorMessage_ReturnsText()
    {
        FinnhubParser.TryParseError("""{"type":"error","msg":"Invalid token"}""").Should().Be("Invalid token");
        FinnhubParser.TryParseError("""{"type":"ping"}""").Should().BeNull();
    }

    /// <summary>
    /// Verifies a quote answer is read, and an unknown symbol, which is answered with zeros, is not.
    /// </summary>
    [Fact]
    public void ParseQuote_Answer_ReadsPriceAndPreviousClose()
    {
        var quote = FinnhubParser.ParseQuote("AAPL", QuoteJson)!;

        quote.Key.Should().Be("AAPL");
        quote.Price.Should().Be(261.74m);
        quote.PreviousClose.Should().Be(259.64m);
        quote.Time.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1795121940));
        FinnhubParser.ParseQuote("ZZZZ", """{"c":0,"d":null,"dp":null,"h":0,"l":0,"o":0,"pc":0,"t":0}""").Should().BeNull();
    }

    /// <summary>
    /// Verifies search keeps plain US tickers, once each.
    /// </summary>
    [Fact]
    public void ParseSearch_Results_KeepPlainTickers()
    {
        const string json = """{"count":4,"result":[{"description":"APPLE INC","displaySymbol":"AAPL","symbol":"AAPL","type":"Common Stock"},{"description":"APPLE INC","displaySymbol":"AAPL.SW","symbol":"AAPL.SW","type":"Common Stock"},{"description":"APPLE","displaySymbol":"AAPL","symbol":"AAPL","type":"Common Stock"},{"description":"x","displaySymbol":"x","symbol":"BINANCE:BTCUSDT","type":""}]}""";

        var assets = FinnhubParser.ParseSearch(json, "finnhub");

        assets.Should().ContainSingle().Which.Symbol.Should().Be("AAPL");
        assets[0].DisplayName.Should().Be("APPLE INC");
    }

    /// <summary>
    /// Verifies the first price comes over REST with the key in a header, never in the address.
    /// </summary>
    [Fact]
    public async Task Provider_FirstPrice_UsesTheKeyInAHeaderOnly()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(QuoteJson));
        await using var provider = new FinnhubProvider(
            new FinnhubProviderFactory().Descriptor,
            new Uri("https://api.example.test"),
            new Uri("ws://127.0.0.1:1/"),
            "secret-key",
            TimeSpan.FromSeconds(15),
            new HttpClient(handler),
            new FakeTimeProvider(TradingHoursStart),
            NullLogger.Instance);
        var apple = new Asset("finnhub", "AAPL", "Apple", AssetClass.Equity);

        await provider.SetSubscriptionsAsync([apple]);
        var quote = (await TestWait.ReadQuotesAsync(provider, 1)).Single();

        quote.AssetId.Should().Be(apple.Id);
        quote.Price.Should().Be(261.74m);
        quote.Reference.Should().Be(259.64m);
        var request = handler.Requests.First();
        request.RequestUri!.AbsoluteUri.Should().EndWith("/api/v1/quote?symbol=AAPL").And.NotContain("secret-key");
        request.Headers.GetValues("X-Finnhub-Token").Should().Equal("secret-key");
    }
}
