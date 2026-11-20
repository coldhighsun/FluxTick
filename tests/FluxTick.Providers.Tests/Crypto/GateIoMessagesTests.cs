using AwesomeAssertions;
using FluxTick.Providers.GateIo;
using Xunit;

namespace FluxTick.Providers.Tests.Crypto;

/// <summary>
/// Tests for the pure Gate.io payload functions.
/// </summary>
public sealed class GateIoMessagesTests
{
    /// <summary>
    /// The clock used when a message carries no time.
    /// </summary>
    private static readonly DateTimeOffset Fallback = new(2026, 10, 22, 16, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies that every symbol gets a request of its own.
    /// </summary>
    [Fact]
    public void ControlMessages_ManySymbols_OneRequestEach()
    {
        var messages = GateIoMessages.ControlMessages("subscribe", ["BTC_USDT", "ETH_USDT"], Fallback).ToList();

        messages.Should().HaveCount(2);
        messages[1].Should().Contain("\"payload\":[\"ETH_USDT\"]").And.Contain("\"event\":\"subscribe\"");
    }

    /// <summary>
    /// Verifies that the reference price is derived from the change in percent.
    /// </summary>
    [Fact]
    public void TryParseTicker_Update_DerivesReferenceFromPercent()
    {
        const string json = """{"time_ms":1792683284127,"channel":"spot.tickers","event":"update","result":{"currency_pair":"BTC_USDT","last":"90","change_percentage":"-10"}}""";

        var parsed = GateIoMessages.TryParseTicker(json, "gateio", Fallback, out var quote);

        parsed.Should().BeTrue();
        quote.AssetId.Symbol.Should().Be("BTC_USDT");
        quote.Price.Should().Be(90m);
        quote.Reference.Should().Be(100m);
        quote.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1792683284127));
    }

    /// <summary>
    /// Verifies that replies to control requests are not read as quotes.
    /// </summary>
    [Fact]
    public void TryParseTicker_SubscribeReply_ReturnsFalse()
    {
        const string json = """{"channel":"spot.tickers","event":"subscribe","result":{"status":"success"}}""";

        GateIoMessages.TryParseTicker(json, "gateio", Fallback, out _).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a failed request reports its message.
    /// </summary>
    [Fact]
    public void TryParseError_FailedSubscribe_ReturnsMessage()
    {
        const string json = """{"channel":"spot.tickers","event":"subscribe","error":{"code":2,"message":"unknown currency pair: X_USDT"},"result":{"status":"fail"}}""";

        GateIoMessages.TryParseError(json).Should().Be("unknown currency pair: X_USDT");
    }

    /// <summary>
    /// Verifies that candles are read as close prices in the order given.
    /// </summary>
    [Fact]
    public void ParseCandles_Candles_ReturnsCloses()
    {
        const string json = """[["1795145160","1","62171.8","1","1","1","1","true"],["1795145220","1","62111.3","1","1","1","1","false"]]""";

        var points = GateIoMessages.ParseCandles(json);

        points.Should().HaveCount(2);
        points[0].Price.Should().Be(62171.8m);
        points[1].Timestamp.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1795145220));
    }

    /// <summary>
    /// Verifies that an error object from the REST API becomes an exception.
    /// </summary>
    [Fact]
    public void ParseCandles_ErrorObject_Throws()
    {
        var act = () => GateIoMessages.ParseCandles("""{"label":"INVALID_CURRENCY_PAIR","message":"bad"}""");

        act.Should().Throw<HttpRequestException>();
    }

    /// <summary>
    /// Verifies that only tradable pairs are listed.
    /// </summary>
    [Fact]
    public void ParsePairs_MixedStatus_KeepsTradable()
    {
        const string json = """[{"id":"BTC_USDT","base":"BTC","quote":"USDT","trade_status":"tradable"},{"id":"OLD_USDT","base":"OLD","quote":"USDT","trade_status":"untradable"}]""";

        var symbols = GateIoMessages.ParsePairs(json);

        symbols.Should().ContainSingle().Which.Symbol.Should().Be("BTC_USDT");
    }
}
