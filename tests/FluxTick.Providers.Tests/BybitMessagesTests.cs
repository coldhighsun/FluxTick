using System.Text.Json;
using FluxTick.Abstractions;
using FluxTick.Providers.Bybit;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the pure Bybit payload helpers.
/// </summary>
public class BybitMessagesTests
{
    /// <summary>
    /// A spot ticker push as captured from the public <c>tickers</c> topic.
    /// </summary>
    private const string Ticker =
        """{"topic":"tickers.BTCUSDT","ts":1792683284127,"type":"snapshot","cs":115933698814,"data":{"symbol":"BTCUSDT","lastPrice":"81513.7","highPrice24h":"84906.1","lowPrice24h":"81303","prevPrice24h":"84625.6","volume24h":"10600.818965","turnover24h":"879483940.57","price24hPcnt":"-0.0368","usdIndexPrice":"81423.81"}}""";

    /// <summary>
    /// The clock value used when a message carries no timestamp.
    /// </summary>
    private static readonly DateTimeOffset Fallback = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies a ticker push becomes a quote with the 24h previous price as reference.
    /// </summary>
    [Fact]
    public void TryParseTicker_Push_ReturnsQuote()
    {
        BybitMessages.TryParseTicker(Ticker, "bybit", Fallback, out var quote).Should().BeTrue();

        quote.AssetId.Should().Be(new AssetId("bybit", "BTCUSDT"));
        quote.Price.Should().Be(81513.7m);
        quote.Reference.Should().Be(84625.6m);
        quote.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1792683284127));
        quote.TimeIsEstimated.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a push without the previous price or timestamp falls back gracefully.
    /// </summary>
    [Fact]
    public void TryParseTicker_WithoutOptionalFields_UsesDefaults()
    {
        const string json = """{"topic":"tickers.ETHUSDT","data":{"symbol":"ETHUSDT","lastPrice":"3000"}}""";

        BybitMessages.TryParseTicker(json, "bybit", Fallback, out var quote).Should().BeTrue();

        quote.Reference.Should().BeNull();
        quote.Timestamp.Should().Be(Fallback);
        quote.TimeIsEstimated.Should().BeTrue();
    }

    /// <summary>
    /// Verifies pongs, control replies and other topics are not quotes.
    /// </summary>
    [Theory]
    [InlineData("""{"success":true,"ret_msg":"pong","conn_id":"x","op":"ping"}""")]
    [InlineData("""{"success":true,"ret_msg":"subscribe","conn_id":"x","op":"subscribe"}""")]
    [InlineData("""{"topic":"publicTrade.BTCUSDT","data":{"symbol":"BTCUSDT","lastPrice":"1"}}""")]
    [InlineData("""{"topic":"tickers.BTCUSDT","data":{"symbol":"BTCUSDT"}}""")]
    [InlineData("""[1,2,3]""")]
    public void TryParseTicker_NonQuoteMessages_ReturnsFalse(string json)
    {
        BybitMessages.TryParseTicker(json, "bybit", Fallback, out _).Should().BeFalse();
    }

    /// <summary>
    /// Verifies a failed reply is reported and successful ones are not.
    /// </summary>
    [Fact]
    public void TryParseError_FailedReply_ReturnsMessage()
    {
        const string json = """{"success":false,"ret_msg":"error:handler not found,topic:tickers.NOPE","op":"subscribe"}""";

        BybitMessages.TryParseError(json).Should().StartWith("error:handler not found");
        BybitMessages.TryParseError("""{"success":true,"ret_msg":"pong"}""").Should().BeNull();
        BybitMessages.TryParseError(Ticker).Should().BeNull();
    }

    /// <summary>
    /// Verifies topics are prefixed and requests are split to the ten-topic limit.
    /// </summary>
    [Fact]
    public void ControlMessages_PrefixTopicsAndSplitToTheLimit()
    {
        var symbols = Enumerable.Range(0, BybitMessages.MaxArgsPerMessage + 1).Select(i => $"C{i}USDT").ToArray();

        var messages = BybitMessages.ControlMessages("subscribe", symbols).ToArray();

        messages.Should().HaveCount(2);
        using var first = JsonDocument.Parse(messages[0]);
        first.RootElement.GetProperty("op").GetString().Should().Be("subscribe");
        first.RootElement.GetProperty("args")[0].GetString().Should().Be("tickers.C0USDT");
        first.RootElement.GetProperty("args").GetArrayLength().Should().Be(BybitMessages.MaxArgsPerMessage);
    }

    /// <summary>
    /// Verifies the keep-alive is valid JSON in the documented shape.
    /// </summary>
    [Fact]
    public void Ping_IsTheDocumentedMessage()
    {
        using var document = JsonDocument.Parse(BybitMessages.Ping);

        document.RootElement.GetProperty("op").GetString().Should().Be("ping");
    }

    /// <summary>
    /// Verifies klines are reduced to close prices, oldest first.
    /// </summary>
    [Fact]
    public void ParseKlines_ReturnsOldestFirst()
    {
        const string json =
            """{"retCode":0,"retMsg":"OK","result":{"category":"spot","symbol":"BTCUSDT","list":[["1792683480000","81641.1","81659.1","81613.9","81617.2","6.8","558091"],["1792683420000","81611.3","81647.1","81599","81641.1","5.6","457032"]]}}""";

        var points = BybitMessages.ParseKlines(json);

        points.Select(p => p.Price).Should().Equal(81641.1m, 81617.2m);
        points[0].Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1792683420000));
    }

    /// <summary>
    /// Verifies only instruments in trading status are listed, with readable names.
    /// </summary>
    [Fact]
    public void ParseInstruments_KeepsTradingInstruments()
    {
        const string json =
            """{"retCode":0,"retMsg":"OK","result":{"category":"spot","list":[{"symbol":"BTCUSDT","baseCoin":"BTC","quoteCoin":"USDT","status":"Trading"},{"symbol":"OLDUSDT","baseCoin":"OLD","quoteCoin":"USDT","status":"PreLaunch"},{"symbol":"WEIRD","status":"Trading"}]}}""";

        var listed = BybitMessages.ParseInstruments(json);

        listed.Select(s => s.Symbol).Should().Equal("BTCUSDT", "WEIRD");
        listed.Select(s => s.DisplayName).Should().Equal("BTC/USDT", "WEIRD");
    }

    /// <summary>
    /// Verifies a non-zero return code surfaces as an HTTP failure the UI already handles.
    /// </summary>
    [Fact]
    public void ParseInstruments_ErrorCode_ThrowsHttpRequestException()
    {
        const string json = """{"retCode":10006,"retMsg":"Too many visits","result":{},"time":1}""";

        var act = () => BybitMessages.ParseInstruments(json);

        act.Should().Throw<HttpRequestException>().WithMessage("*10006*");
    }

    /// <summary>
    /// Verifies a return code that is not a number still surfaces as an HTTP failure instead of an invalid-operation error.
    /// </summary>
    [Theory]
    [InlineData("""{"retCode":"10001","retMsg":"bad","result":{}}""")]
    [InlineData("""{"retCode":null,"retMsg":5,"result":{}}""")]
    [InlineData("""{"result":{}}""")]
    public void ParseInstruments_NonNumericReturnCode_ThrowsHttpRequestException(string json)
    {
        var act = () => BybitMessages.ParseInstruments(json);

        act.Should().Throw<HttpRequestException>().WithMessage("Bybit error -1*");
    }
}
