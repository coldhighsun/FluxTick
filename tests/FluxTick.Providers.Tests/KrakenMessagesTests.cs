using FluxTick.Abstractions;
using FluxTick.Providers.Kraken;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the pure Kraken payload helpers.
/// </summary>
public class KrakenMessagesTests
{
    /// <summary>
    /// A ticker snapshot as captured from the v2 <c>ticker</c> channel.
    /// </summary>
    private const string Ticker =
        """{"channel":"ticker","type":"snapshot","data":[{"symbol":"BTC/USD","bid":81418.8,"bid_qty":0.00062059,"ask":81418.9,"ask_qty":3.77219105,"last":81418.9,"volume":2832.12467927,"vwap":83060.7,"low":81247.1,"high":84840.0,"change":-3140.2,"change_pct":-3.71,"trades":166544,"timestamp":"2026-10-22T15:34:43.229705Z"}]}""";

    /// <summary>
    /// The clock value used when a message carries no timestamp.
    /// </summary>
    private static readonly DateTimeOffset Fallback = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies a ticker becomes a quote whose reference is the last price minus the 24h change.
    /// </summary>
    [Fact]
    public void ParseTickers_Snapshot_ReturnsQuoteWithDerivedReference()
    {
        var quote = KrakenMessages.Parse(Ticker, "kraken", Fallback).Quotes.Should().ContainSingle().Subject;

        quote.AssetId.Should().Be(new AssetId("kraken", "BTC/USD"));
        quote.Price.Should().Be(81418.9m);
        quote.Reference.Should().Be(84559.1m);
        quote.Timestamp.Should().Be(new DateTimeOffset(2026, 10, 22, 15, 34, 43, TimeSpan.Zero).AddTicks(2297050));
    }

    /// <summary>
    /// Verifies a missing change leaves the reference empty and a missing timestamp uses the fallback.
    /// </summary>
    [Fact]
    public void ParseTickers_WithoutChangeOrTimestamp_UsesDefaults()
    {
        const string json = """{"channel":"ticker","type":"update","data":[{"symbol":"DOGE/USD","last":0.0791286}]}""";

        var quote = KrakenMessages.Parse(json, "kraken", Fallback).Quotes.Should().ContainSingle().Subject;

        quote.Price.Should().Be(0.0791286m);
        quote.Reference.Should().BeNull();
        quote.Timestamp.Should().Be(Fallback);
    }

    /// <summary>
    /// Verifies heartbeats, status messages and control replies yield no quotes.
    /// </summary>
    [Theory]
    [InlineData("""{"channel":"heartbeat"}""")]
    [InlineData("""{"channel":"status","type":"update","data":[{"system":"online"}]}""")]
    [InlineData("""{"method":"subscribe","result":{"channel":"ticker","symbol":"BTC/USD"},"success":true}""")]
    [InlineData("""[1,2,3]""")]
    public void ParseTickers_NonTickerMessages_ReturnsEmpty(string json)
    {
        KrakenMessages.Parse(json, "kraken", Fallback).Quotes.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a failed subscription reply is reported.
    /// </summary>
    [Fact]
    public void TryParseError_FailedReply_ReturnsError()
    {
        const string json = """{"error":"Currency pair not supported XBT/USD","method":"subscribe","success":false,"symbol":"XBT/USD"}""";

        KrakenMessages.Parse(json, "kraken", Fallback).Error.Should().Be("Currency pair not supported XBT/USD");
        KrakenMessages.Parse("""{"method":"subscribe","success":true}""", "kraken", Fallback).Error.Should().BeNull();
        KrakenMessages.Parse(Ticker, "kraken", Fallback).Error.Should().BeNull();
    }

    /// <summary>
    /// Verifies the control message has the documented shape.
    /// </summary>
    [Fact]
    public void ControlMessages_BuildsDocumentedShape()
    {
        var messages = KrakenMessages.ControlMessages("subscribe", ["BTC/USD", "ETH/USD"]).ToArray();

        messages.Should().Equal("""{"method":"subscribe","params":{"channel":"ticker","symbol":["BTC/USD","ETH/USD"]}}""");
    }

    /// <summary>
    /// Verifies REST names are converted to the symbols the v2 WebSocket accepts.
    /// </summary>
    [Theory]
    [InlineData("XBT/USD", "BTC/USD")]
    [InlineData("XDG/USD", "DOGE/USD")]
    [InlineData("ETH/XBT", "ETH/BTC")]
    [InlineData("SOL/USD", "SOL/USD")]
    public void ToV2Symbol_RenamesLegacyCodes(string wsname, string expected)
    {
        KrakenMessages.ToV2Symbol(wsname).Should().Be(expected);
    }

    /// <summary>
    /// Verifies the pair list keeps online pairs, drops dark pool pairs and uses v2 names.
    /// </summary>
    [Fact]
    public void ParsePairs_KeepsOnlinePairsWithV2Names()
    {
        const string json =
            """{"error":[],"result":{"XXBTZUSD":{"wsname":"XBT/USD","status":"online"},"XBTUSD.d":{"wsname":"XBT/USD","status":"online"},"OLDUSD":{"wsname":"OLD/USD","status":"cancel_only"},"SOLUSD":{"wsname":"SOL/USD"},"NOWS":{"status":"online"}}}""";

        var listed = KrakenMessages.ParsePairs(json);

        listed.Select(s => s.Symbol).Should().Equal("BTC/USD", "SOL/USD");
        listed[0].DisplayName.Should().Be("BTC/USD");
    }

    /// <summary>
    /// Verifies an error response surfaces as an HTTP failure the UI already handles.
    /// </summary>
    [Fact]
    public void ParsePairs_ErrorResponse_ThrowsHttpRequestException()
    {
        const string json = """{"error":["EGeneral:Too many requests"]}""";

        var act = () => KrakenMessages.ParsePairs(json);

        act.Should().Throw<HttpRequestException>().WithMessage("*Too many requests*");
    }

    /// <summary>
    /// Verifies OHLC data is reduced to close prices and the numeric cursor is ignored.
    /// </summary>
    [Fact]
    public void ParseOhlc_ReturnsCloses_IgnoringCursor()
    {
        const string json =
            """{"error":[],"result":{"BTC/USD":[[1792640280,"82721.5","82736.5","82718.4","82722.4","82726.8","1.0",110],[1792640340,"82722.3","82726.7","82700.2","82710.7","82716.3","0.9",94]],"last":1792640280}}""";

        var points = KrakenMessages.ParseOhlc(json);

        points.Select(p => p.Price).Should().Equal(82722.4m, 82710.7m);
        points[0].Timestamp.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1792640280));
    }

    /// <summary>
    /// Verifies only the most recent hour of candles is kept.
    /// </summary>
    [Fact]
    public void ParseOhlc_KeepsTheMostRecentSamples()
    {
        var candles = Enumerable.Range(0, 720).Select(i => $"[{1792640280 + (i * 60)},\"1\",\"1\",\"1\",\"{i}\",\"1\",\"1\",1]");
        var json = "{\"error\":[],\"result\":{\"BTC/USD\":[" + string.Join(',', candles) + "],\"last\":1}}";

        var points = KrakenMessages.ParseOhlc(json);

        points.Should().HaveCount(KrakenMessages.HistoryLength);
        points[^1].Price.Should().Be(719m);
    }
}
