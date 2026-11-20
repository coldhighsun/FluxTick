using AwesomeAssertions;
using FluxTick.Providers.Bitget;
using Xunit;

namespace FluxTick.Providers.Tests.Crypto;

/// <summary>
/// Tests for the pure Bitget payload functions.
/// </summary>
public sealed class BitgetMessagesTests
{
    /// <summary>
    /// The clock used when a message carries no time.
    /// </summary>
    private static readonly DateTimeOffset Fallback = new(2026, 10, 22, 16, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies that many symbols are split into several requests.
    /// </summary>
    [Fact]
    public void ControlMessages_MoreThanOneChunk_SplitsRequests()
    {
        var symbols = Enumerable.Range(0, 25).Select(i => $"S{i}USDT");

        var messages = BitgetMessages.ControlMessages("subscribe", symbols).ToList();

        messages.Should().HaveCount(2);
        messages[0].Should().Contain("\"instType\":\"SPOT\"").And.Contain("\"channel\":\"ticker\"");
    }

    /// <summary>
    /// Verifies that the 24 hour open is the reference.
    /// </summary>
    [Fact]
    public void TryParseTicker_Push_UsesOpen24hAsReference()
    {
        const string json = """{"action":"snapshot","arg":{"instType":"SPOT","channel":"ticker","instId":"BTCUSDT"},"data":[{"instId":"BTCUSDT","lastPr":"61554.4","open24h":"63104.71","ts":"1795151892197"}],"ts":1795151892207}""";

        var parsed = BitgetMessages.TryParseTicker(json, "bitget", Fallback, out var quote);

        parsed.Should().BeTrue();
        quote.AssetId.Symbol.Should().Be("BTCUSDT");
        quote.Price.Should().Be(61554.4m);
        quote.Reference.Should().Be(63104.71m);
        quote.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1795151892197));
    }

    /// <summary>
    /// Verifies that the plain pong and subscribe replies are not quotes.
    /// </summary>
    [Theory]
    [InlineData("pong")]
    [InlineData("""{"event":"subscribe","arg":{"instType":"SPOT","channel":"ticker","instId":"BTCUSDT"}}""")]
    public void TryParseTicker_NonTicker_ReturnsFalse(string text)
    {
        BitgetMessages.TryParseTicker(text, "bitget", Fallback, out _).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that an error event reports its message.
    /// </summary>
    [Fact]
    public void TryParseError_ErrorEvent_ReturnsMessage()
    {
        const string json = """{"event":"error","code":30001,"msg":"instId doesn't exist","op":"subscribe"}""";

        BitgetMessages.TryParseError(json).Should().Be("instId doesn't exist");
    }

    /// <summary>
    /// Verifies that candles are read as closes, oldest first.
    /// </summary>
    [Fact]
    public void ParseCandles_Candles_ReturnsCloses()
    {
        const string json = """{"code":"00000","msg":"success","data":[["1795151820000","1","1","1","61542.93","1"],["1795151880000","1","1","1","61557.96","1"]]}""";

        var points = BitgetMessages.ParseCandles(json);

        points.Should().HaveCount(2);
        points[0].Price.Should().Be(61542.93m);
        points[1].Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1795151880000));
    }

    /// <summary>
    /// Verifies that an error code becomes an exception.
    /// </summary>
    [Fact]
    public void ParseCandles_ErrorCode_Throws()
    {
        var act = () => BitgetMessages.ParseCandles("""{"code":"40001","msg":"bad"}""");

        act.Should().Throw<HttpRequestException>();
    }

    /// <summary>
    /// Verifies that only online symbols are listed.
    /// </summary>
    [Fact]
    public void ParseSymbols_MixedStatus_KeepsOnline()
    {
        const string json = """{"code":"00000","data":[{"symbol":"BTCUSDT","baseCoin":"BTC","quoteCoin":"USDT","status":"online"},{"symbol":"OLDUSDT","baseCoin":"OLD","quoteCoin":"USDT","status":"offline"}]}""";

        var symbols = BitgetMessages.ParseSymbols(json);

        symbols.Should().ContainSingle().Which.Symbol.Should().Be("BTCUSDT");
    }
}
