using FluxTick.Abstractions;
using FluxTick.Providers.Coinbase;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the pure Coinbase payload helpers.
/// </summary>
public class CoinbaseMessagesTests
{
    /// <summary>
    /// A ticker message as captured from the public <c>ticker</c> channel.
    /// </summary>
    private const string Ticker =
        """{"type":"ticker","sequence":138160163977,"product_id":"BTC-USD","price":"81421.96","open_24h":"84474.36","volume_24h":"7228.89857199","low_24h":"81222","high_24h":"84856.07","best_bid":"81421.83","best_ask":"81421.97","side":"sell","time":"2026-10-22T15:34:42.413298Z","trade_id":1109389605,"last_size":"0.00000031"}""";

    /// <summary>
    /// The clock value used when a message carries no timestamp.
    /// </summary>
    private static readonly DateTimeOffset Fallback = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies a ticker becomes a quote with the 24h open as reference.
    /// </summary>
    [Fact]
    public void TryParseTicker_Ticker_ReturnsQuote()
    {
        CoinbaseMessages.TryParseTicker(Ticker, "coinbase", Fallback, out var quote).Should().BeTrue();

        quote.AssetId.Should().Be(new AssetId("coinbase", "BTC-USD"));
        quote.Price.Should().Be(81421.96m);
        quote.Reference.Should().Be(84474.36m);
        quote.Timestamp.Should().Be(new DateTimeOffset(2026, 10, 22, 15, 34, 42, TimeSpan.Zero).AddTicks(4132980));
    }

    /// <summary>
    /// Verifies a zero 24h open is dropped instead of being used as a reference price.
    /// </summary>
    [Fact]
    public void TryParseTicker_ZeroOpen_HasNoReference()
    {
        const string json =
            """{"type":"ticker","product_id":"NEW-USD","price":"1.5","open_24h":"0","time":"2026-10-22T15:34:24Z"}""";

        CoinbaseMessages.TryParseTicker(json, "coinbase", Fallback, out var quote).Should().BeTrue();

        quote.Reference.Should().BeNull();
    }

    /// <summary>
    /// Verifies the nanosecond timestamps of the REST API do not break parsing.
    /// </summary>
    [Fact]
    public void TryParseTicker_NanosecondTimestamp_IsAccepted()
    {
        const string json =
            """{"type":"ticker","product_id":"ETH-USD","price":"3000","time":"2026-10-22T15:34:24.289770452Z"}""";

        CoinbaseMessages.TryParseTicker(json, "coinbase", Fallback, out var quote).Should().BeTrue();

        quote.Timestamp.Year.Should().Be(2026);
        quote.Timestamp.Should().NotBe(Fallback);
        quote.Reference.Should().BeNull();
    }

    /// <summary>
    /// Verifies a ticker without a timestamp uses the fallback.
    /// </summary>
    [Fact]
    public void TryParseTicker_WithoutTime_UsesFallback()
    {
        const string json = """{"type":"ticker","product_id":"ETH-USD","price":"3000"}""";

        CoinbaseMessages.TryParseTicker(json, "coinbase", Fallback, out var quote).Should().BeTrue();

        quote.Timestamp.Should().Be(Fallback);
    }

    /// <summary>
    /// Verifies control replies and other message types are not quotes.
    /// </summary>
    [Theory]
    [InlineData("""{"type":"subscriptions","channels":[{"name":"ticker","product_ids":["BTC-USD"]}]}""")]
    [InlineData("""{"type":"heartbeat","product_id":"BTC-USD"}""")]
    [InlineData("""{"type":"ticker","product_id":"BTC-USD","price":"abc"}""")]
    [InlineData("""[1,2,3]""")]
    public void TryParseTicker_NonQuoteMessages_ReturnsFalse(string json)
    {
        CoinbaseMessages.TryParseTicker(json, "coinbase", Fallback, out _).Should().BeFalse();
    }

    /// <summary>
    /// Verifies error messages are reported with their reason.
    /// </summary>
    [Fact]
    public void TryParseError_ReportsOnlyErrors()
    {
        const string error = """{"type":"error","message":"Failed to subscribe","reason":"BAD-USD is not a valid product"}""";

        CoinbaseMessages.TryParseError(error).Should().Be("Failed to subscribe (BAD-USD is not a valid product)");
        CoinbaseMessages.TryParseError(Ticker).Should().BeNull();
    }

    /// <summary>
    /// Verifies the control message has the documented shape.
    /// </summary>
    [Fact]
    public void ControlMessages_BuildsDocumentedShape()
    {
        var messages = CoinbaseMessages.ControlMessages("subscribe", ["BTC-USD", "ETH-USD"]).ToArray();

        messages.Should().Equal("""{"type":"subscribe","product_ids":["BTC-USD","ETH-USD"],"channels":["ticker"]}""");
    }

    /// <summary>
    /// Verifies long product lists are split into several requests.
    /// </summary>
    [Fact]
    public void ControlMessages_SplitsLongLists()
    {
        var symbols = Enumerable.Range(0, CoinbaseMessages.MaxProductsPerMessage + 1).Select(i => $"C{i}-USD");

        CoinbaseMessages.ControlMessages("subscribe", symbols).Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies candles are reduced to the close column, oldest first.
    /// </summary>
    [Fact]
    public void ParseCandles_ReturnsOldestFirst()
    {
        const string json =
            "[[1792683420,81500.36,81535,81514.23,81530.8,1.27],[1792683360,81496.09,81539.81,81521.98,81515.88,2.97]]";

        var points = CoinbaseMessages.ParseCandles(json);

        points.Select(p => p.Price).Should().Equal(81515.88m, 81530.8m);
        points[0].Timestamp.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1792683360));
    }

    /// <summary>
    /// Verifies only the most recent hour of candles is kept.
    /// </summary>
    [Fact]
    public void ParseCandles_KeepsTheMostRecentSamples()
    {
        var candles = Enumerable.Range(0, 300).Select(i => $"[{1792683420 - (i * 60)},1,1,1,{300 - i},1]");
        var json = $"[{string.Join(',', candles)}]";

        var points = CoinbaseMessages.ParseCandles(json);

        points.Should().HaveCount(CoinbaseMessages.HistoryLength);
        points[^1].Price.Should().Be(300m);
    }

    /// <summary>
    /// Verifies only online, tradable products are listed.
    /// </summary>
    [Fact]
    public void ParseProducts_KeepsOnlineProducts()
    {
        const string json =
            """[{"id":"BTC-USD","status":"online"},{"id":"OLD-USD","status":"delisted"},{"id":"HALT-USD","status":"online","trading_disabled":true},{"id":"ETH-EUR"}]""";

        var listed = CoinbaseMessages.ParseProducts(json);

        listed.Select(s => s.Symbol).Should().Equal("BTC-USD", "ETH-EUR");
        listed[0].DisplayName.Should().Be("BTC/USD");
    }
}
