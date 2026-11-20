using AwesomeAssertions;
using FluxTick.Providers.CryptoCom;
using Xunit;

namespace FluxTick.Providers.Tests.Crypto;

/// <summary>
/// Tests for the pure Crypto.com payload functions.
/// </summary>
public sealed class CryptoComMessagesTests
{
    /// <summary>
    /// The clock used when a message carries no time.
    /// </summary>
    private static readonly DateTimeOffset Fallback = new(2026, 10, 22, 16, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies the subscribe request names the ticker channels.
    /// </summary>
    [Fact]
    public void ControlMessages_Symbols_NamesTickerChannels()
    {
        var messages = CryptoComMessages.ControlMessages("subscribe", ["BTC_USDT", "ETH_USDT"]).ToList();

        messages.Should().ContainSingle().Which.Should()
            .Contain("\"method\":\"subscribe\"")
            .And.Contain("\"ticker.BTC_USDT\",\"ticker.ETH_USDT\"");
    }

    /// <summary>
    /// Verifies that the heartbeat reply echoes the id of the server request.
    /// </summary>
    [Fact]
    public void HeartbeatReply_Heartbeat_EchoesId()
    {
        var reply = CryptoComMessages.HeartbeatReply("""{"id":1795152250000,"method":"public/heartbeat","code":0}""");

        reply.Should().Be("""{"id":1795152250000,"method":"public/respond-heartbeat"}""");
    }

    /// <summary>
    /// Verifies that other messages get no reply.
    /// </summary>
    [Fact]
    public void HeartbeatReply_Ticker_ReturnsNull()
    {
        CryptoComMessages.HeartbeatReply("""{"id":-1,"method":"subscribe","code":0,"result":{"channel":"ticker","data":[]}}""").Should().BeNull();
    }

    /// <summary>
    /// Verifies that the reference price is derived from the change fraction.
    /// </summary>
    [Fact]
    public void TryParseTicker_Push_DerivesReferenceFromChange()
    {
        const string json = """{"id":-1,"method":"subscribe","code":0,"result":{"instrument_name":"BTC_USDT","subscription":"ticker.BTC_USDT","channel":"ticker","data":[{"i":"BTC_USDT","a":"90","c":"-0.1","t":1795152250019}]}}""";

        var parsed = CryptoComMessages.TryParseTicker(json, "cryptocom", Fallback, out var quote);

        parsed.Should().BeTrue();
        quote.AssetId.Symbol.Should().Be("BTC_USDT");
        quote.Price.Should().Be(90m);
        quote.Reference.Should().Be(100m);
        quote.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1795152250019));
    }

    /// <summary>
    /// Verifies that a failed subscription reports its message and is not a quote.
    /// </summary>
    [Fact]
    public void TryParseError_UnknownSymbol_ReturnsMessage()
    {
        const string json = """{"id":1,"method":"subscribe","code":40003,"channel":"ticker.NOPE_USDT","message":"Unknown symbol"}""";

        CryptoComMessages.TryParseError(json).Should().Be("Unknown symbol (code 40003)");
        CryptoComMessages.TryParseTicker(json, "cryptocom", Fallback, out _).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that candles are read as closes, oldest first.
    /// </summary>
    [Fact]
    public void ParseCandles_Candles_ReturnsCloses()
    {
        const string json = """{"id":-1,"method":"public/get-candlestick","code":0,"result":{"interval":"1m","data":[{"o":"1","h":"1","l":"1","c":"61582.05","v":"1","t":1795152180000},{"o":"1","h":"1","l":"1","c":"61623.34","v":"1","t":1795152240000}],"instrument_name":"BTC_USDT"}}""";

        var points = CryptoComMessages.ParseCandles(json);

        points.Should().HaveCount(2);
        points[0].Price.Should().Be(61582.05m);
        points[1].Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1795152240000));
    }

    /// <summary>
    /// Verifies that an error code becomes an exception.
    /// </summary>
    [Fact]
    public void ParseCandles_ErrorCode_Throws()
    {
        var act = () => CryptoComMessages.ParseCandles("""{"code":10004,"message":"BAD_REQUEST"}""");

        act.Should().Throw<HttpRequestException>();
    }

    /// <summary>
    /// Verifies that only tradable spot pairs are listed.
    /// </summary>
    [Fact]
    public void ParseInstruments_Mixed_KeepsTradableSpotPairs()
    {
        const string json = """{"code":0,"result":{"data":[{"symbol":"BTC_USDT","inst_type":"CCY_PAIR","base_ccy":"BTC","quote_ccy":"USDT","tradable":true},{"symbol":"BTCUSD-PERP","inst_type":"PERPETUAL_SWAP","base_ccy":"BTC","quote_ccy":"USD","tradable":true},{"symbol":"OLD_USDT","inst_type":"CCY_PAIR","base_ccy":"OLD","quote_ccy":"USDT","tradable":false}]}}""";

        var symbols = CryptoComMessages.ParseInstruments(json);

        symbols.Should().ContainSingle().Which.Symbol.Should().Be("BTC_USDT");
    }
}
