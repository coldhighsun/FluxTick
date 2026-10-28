using System.Text.Json;
using FluxTick.Abstractions;
using FluxTick.Providers.Okx;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the pure OKX payload helpers.
/// </summary>
public class OkxMessagesTests
{
    /// <summary>
    /// A ticker push as captured from the public <c>tickers</c> channel.
    /// </summary>
    private const string Ticker =
        """{"arg":{"channel":"tickers","instId":"BTC-USDT"},"data":[{"instType":"SPOT","instId":"BTC-USDT","last":"81519.3","lastSz":"0.000155","askPx":"81519.3","bidPx":"81519.2","open24h":"84593.9","high24h":"84899.6","low24h":"81314.2","ts":"1792683280970"}]}""";

    /// <summary>
    /// The clock value used when a message carries no timestamp.
    /// </summary>
    private static readonly DateTimeOffset Fallback = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies a ticker push becomes a quote with the 24h open as reference.
    /// </summary>
    [Fact]
    public void ParseTickers_TickerPush_ReturnsQuote()
    {
        var quotes = OkxMessages.Parse(Ticker, "okx", Fallback).Quotes;

        var quote = quotes.Should().ContainSingle().Subject;
        quote.AssetId.Should().Be(new AssetId("okx", "BTC-USDT"));
        quote.Price.Should().Be(81519.3m);
        quote.Reference.Should().Be(84593.9m);
        quote.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1792683280970));
        quote.TimeIsEstimated.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a zero 24h open is dropped instead of being used as a reference price.
    /// </summary>
    [Fact]
    public void ParseTickers_ZeroOpen_HasNoReference()
    {
        const string json =
            """{"arg":{"channel":"tickers","instId":"NEW-USDT"},"data":[{"instId":"NEW-USDT","last":"1.5","open24h":"0","ts":"1792683280970"}]}""";

        var quote = OkxMessages.Parse(json, "okx", Fallback).Quotes.Should().ContainSingle().Subject;

        quote.Reference.Should().BeNull();
    }

    /// <summary>
    /// Verifies a missing timestamp and a missing open fall back gracefully.
    /// </summary>
    [Fact]
    public void ParseTickers_WithoutTimestampOrOpen_UsesFallbackAndNoReference()
    {
        const string json = """{"arg":{"channel":"tickers","instId":"ETH-USDT"},"data":[{"instId":"ETH-USDT","last":"3000"}]}""";

        var quote = OkxMessages.Parse(json, "okx", Fallback).Quotes.Should().ContainSingle().Subject;

        quote.Price.Should().Be(3000m);
        quote.Reference.Should().BeNull();
        quote.Timestamp.Should().Be(Fallback);
        quote.TimeIsEstimated.Should().BeTrue();
    }

    /// <summary>
    /// Verifies an item with an unreadable price is skipped without dropping its neighbours.
    /// </summary>
    [Fact]
    public void ParseTickers_BadItem_IsSkipped()
    {
        const string json =
            """{"arg":{"channel":"tickers"},"data":[{"instId":"A-USDT","last":"abc"},{"instId":"B-USDT","last":"2"}]}""";

        var quotes = OkxMessages.Parse(json, "okx", Fallback).Quotes;

        quotes.Select(q => q.AssetId.Symbol).Should().Equal("B-USDT");
    }

    /// <summary>
    /// Verifies pongs, control replies and other channels yield no quotes.
    /// </summary>
    [Theory]
    [InlineData("pong")]
    [InlineData("""{"event":"subscribe","arg":{"channel":"tickers","instId":"BTC-USDT"},"connId":"x"}""")]
    [InlineData("""{"arg":{"channel":"trades","instId":"BTC-USDT"},"data":[{"instId":"BTC-USDT","last":"1"}]}""")]
    [InlineData("""[1,2,3]""")]
    [InlineData("""{"arg":"tickers","data":[]}""")]
    [InlineData("""{"arg":{"channel":5},"data":{}}""")]
    [InlineData("""{"event":5,"code":6}""")]
    public void ParseTickers_NonTickerMessages_ReturnsEmpty(string text)
    {
        OkxMessages.Parse(text, "okx", Fallback).Quotes.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies error events are reported and everything else is not.
    /// </summary>
    [Fact]
    public void TryParseError_ReportsOnlyErrorEvents()
    {
        const string error = """{"event":"error","code":"60018","msg":"Wrong URL or channel:tickers,instId:X-Y doesn't exist"}""";

        OkxMessages.Parse(error, "okx", Fallback).Error.Should().StartWith("60018 Wrong URL");
        OkxMessages.Parse(Ticker, "okx", Fallback).Error.Should().BeNull();
        OkxMessages.Parse("pong", "okx", Fallback).Error.Should().BeNull();
    }

    /// <summary>
    /// Verifies the control message has the documented shape.
    /// </summary>
    [Fact]
    public void ControlMessages_BuildsDocumentedShape()
    {
        var messages = OkxMessages.ControlMessages("subscribe", ["BTC-USDT", "ETH-USDT"]).ToArray();

        messages.Should().Equal(
            """{"op":"subscribe","args":[{"channel":"tickers","instId":"BTC-USDT"},{"channel":"tickers","instId":"ETH-USDT"}]}""");
    }

    /// <summary>
    /// Verifies long instrument lists are split into several requests.
    /// </summary>
    [Fact]
    public void ControlMessages_SplitsLongLists()
    {
        var symbols = Enumerable.Range(0, OkxMessages.MaxArgsPerMessage + 1).Select(i => $"C{i}-USDT").ToArray();

        var messages = OkxMessages.ControlMessages("subscribe", symbols).ToArray();

        messages.Should().HaveCount(2);
        using var last = JsonDocument.Parse(messages[1]);
        last.RootElement.GetProperty("args").GetArrayLength().Should().Be(1);
    }

    /// <summary>
    /// Verifies candles are reduced to close prices, oldest first.
    /// </summary>
    [Fact]
    public void ParseCandles_ReturnsOldestFirst()
    {
        const string json =
            """{"code":"0","msg":"","data":[["1792683240000","81530.7","81552.8","81525.7","81525.7","0.4"],["1792683180000","81485","81558","81485","81530.7","10.4"]]}""";

        var points = OkxMessages.ParseCandles(json);

        points.Select(p => p.Price).Should().Equal(81530.7m, 81525.7m);
        points[0].Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1792683180000));
    }

    /// <summary>
    /// Verifies the instrument list keeps only live pairs and builds readable names.
    /// </summary>
    [Fact]
    public void ParseInstruments_KeepsLivePairs()
    {
        const string json =
            """{"code":"0","msg":"","data":[{"instId":"BTC-USDT","state":"live"},{"instId":"OLD-USDT","state":"suspend"},{"instId":"ETH-BTC"}]}""";

        var listed = OkxMessages.ParseInstruments(json);

        listed.Select(s => s.Symbol).Should().Equal("BTC-USDT", "ETH-BTC");
        listed[0].DisplayName.Should().Be("BTC/USDT");
    }

    /// <summary>
    /// Verifies an error envelope surfaces as an HTTP failure the UI already handles.
    /// </summary>
    [Fact]
    public void ParseInstruments_ErrorCode_ThrowsHttpRequestException()
    {
        const string json = """{"code":"50011","msg":"Rate limit reached","data":[]}""";

        var act = () => OkxMessages.ParseInstruments(json);

        act.Should().Throw<HttpRequestException>().WithMessage("*50011*");
    }
}
