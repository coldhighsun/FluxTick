using FluxTick.Providers.Binance;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the pure Binance payload helpers.
/// </summary>
public class BinanceMessagesTests
{
    /// <summary>
    /// A bare 24h mini ticker event as sent on a raw stream.
    /// </summary>
    private const string MiniTicker =
        """{"e":"24hrMiniTicker","E":1700000000000,"s":"BTCUSDT","c":"67000.10","o":"66000.00","h":"68000","l":"65000","v":"1","q":"2"}""";

    /// <summary>
    /// Verifies a bare event is parsed into a quote with the 24h open as reference.
    /// </summary>
    [Fact]
    public void TryParseMiniTicker_BareEvent_ReturnsQuote()
    {
        var ok = BinanceMessages.TryParseMiniTicker(MiniTicker, "binance", out var quote);

        ok.Should().BeTrue();
        quote.AssetId.Should().Be(new FluxTick.Abstractions.AssetId("binance", "BTCUSDT"));
        quote.Price.Should().Be(67000.10m);
        quote.Reference.Should().Be(66000m);
        quote.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000));
    }

    /// <summary>
    /// Verifies the combined-stream envelope is unwrapped.
    /// </summary>
    [Fact]
    public void TryParseMiniTicker_CombinedStreamEnvelope_ReturnsQuote()
    {
        var json = $$"""{"stream":"btcusdt@miniTicker","data":{{MiniTicker}}}""";

        BinanceMessages.TryParseMiniTicker(json, "binance", out var quote).Should().BeTrue();
        quote.Price.Should().Be(67000.10m);
    }

    /// <summary>
    /// Verifies control replies and other events are not treated as quotes.
    /// </summary>
    [Theory]
    [InlineData("""{"result":null,"id":1}""")]
    [InlineData("""{"e":"trade","E":1,"s":"BTCUSDT","c":"1"}""")]
    [InlineData("""[1,2,3]""")]
    [InlineData("""{"e":"24hrMiniTicker","E":1,"s":"BTCUSDT","c":"abc"}""")]
    public void TryParseMiniTicker_NonQuoteMessages_ReturnsFalse(string json)
    {
        BinanceMessages.TryParseMiniTicker(json, "binance", out _).Should().BeFalse();
    }

    /// <summary>
    /// Verifies subscription messages use lower-case stream names and the documented shape.
    /// </summary>
    [Fact]
    public void ControlMessage_BuildsDocumentedShape()
    {
        var json = BinanceMessages.ControlMessage("SUBSCRIBE", ["BTCUSDT", "ETHUSDT"], 7);

        json.Should().Be("""{"method":"SUBSCRIBE","params":["btcusdt@miniTicker","ethusdt@miniTicker"],"id":7}""");
    }

    /// <summary>
    /// Verifies klines are reduced to close prices with their open time.
    /// </summary>
    [Fact]
    public void ParseKlines_ReturnsClosePrices()
    {
        const string json = """[[1700000000000,"1","2","0.5","1.5","10",1700000059999,"5",1,"1","1","0"],[1700000060000,"1.5","2","1","1.8","10",1700000119999,"5",1,"1","1","0"]]""";

        var points = BinanceMessages.ParseKlines(json);

        points.Select(p => p.Price).Should().Equal(1.5m, 1.8m);
        points[0].Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000));
    }

    /// <summary>
    /// Verifies the symbol listing is parsed.
    /// </summary>
    [Fact]
    public void ParseSymbols_ReturnsSymbolNames()
    {
        BinanceMessages.ParseSymbols("""[{"symbol":"BTCUSDT","price":"1"},{"symbol":"ETHBTC","price":"2"}]""")
            .Should().Equal("BTCUSDT", "ETHBTC");
    }

    /// <summary>
    /// Verifies symbols are split into readable pairs.
    /// </summary>
    [Theory]
    [InlineData("BTCUSDT", "BTC/USDT")]
    [InlineData("ETHBTC", "ETH/BTC")]
    [InlineData("USDT", "USDT")]
    [InlineData("WEIRD", "WEIRD")]
    public void DisplayNameOf_SplitsKnownQuoteAssets(string symbol, string expected)
    {
        BinanceProvider.DisplayNameOf(symbol).Should().Be(expected);
    }
}
