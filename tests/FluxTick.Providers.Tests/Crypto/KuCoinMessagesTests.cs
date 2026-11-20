using AwesomeAssertions;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using FluxTick.Providers.KuCoin;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace FluxTick.Providers.Tests.Crypto;

/// <summary>
/// Tests for the pure KuCoin payload functions and the requests of the provider.
/// </summary>
public sealed class KuCoinMessagesTests
{
    /// <summary>
    /// The clock used when a message carries no time.
    /// </summary>
    private static readonly DateTimeOffset Fallback = new(2026, 10, 22, 16, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies that the symbols share one topic.
    /// </summary>
    [Fact]
    public void ControlMessages_Symbols_ShareOneSnapshotTopic()
    {
        var messages = KuCoinMessages.ControlMessages("subscribe", ["BTC-USDT", "ETH-USDT"]).ToList();

        messages.Should().ContainSingle().Which.Should()
            .Contain("\"type\":\"subscribe\"")
            .And.Contain("\"topic\":\"/market/snapshot:BTC-USDT,ETH-USDT\"")
            .And.Contain("\"response\":true");
    }

    /// <summary>
    /// Verifies that the connection address carries the token and takes the endpoint from the response.
    /// </summary>
    [Fact]
    public void BuildConnectionUri_TokenResponse_UsesServerEndpoint()
    {
        const string json = """{"code":"200000","data":{"token":"a+b/c=","instanceServers":[{"endpoint":"wss://ws-api-spot.kucoin.com/","protocol":"websocket","pingInterval":18000}]}}""";

        var uri = KuCoinMessages.BuildConnectionUri(json, new Uri("wss://fallback.example.test/"), "abc");

        uri.Host.Should().Be("ws-api-spot.kucoin.com");
        uri.Query.Should().Be("?token=a%2Bb%2Fc%3D&connectId=abc");
    }

    /// <summary>
    /// Verifies that the configured address is used when the response names no usable endpoint.
    /// </summary>
    [Fact]
    public void BuildConnectionUri_NoEndpoint_UsesFallback()
    {
        const string json = """{"code":"200000","data":{"token":"t","instanceServers":[]}}""";

        var uri = KuCoinMessages.BuildConnectionUri(json, new Uri("wss://fallback.example.test/"), "abc");

        uri.Host.Should().Be("fallback.example.test");
    }

    /// <summary>
    /// Verifies that a response without a token is an error.
    /// </summary>
    [Fact]
    public void BuildConnectionUri_ErrorResponse_Throws()
    {
        var act = () => KuCoinMessages.BuildConnectionUri("""{"code":"400100","msg":"busy"}""", new Uri("wss://x.example.test/"), "abc");

        act.Should().Throw<HttpRequestException>();
    }

    /// <summary>
    /// Verifies that the open of the snapshot is the reference.
    /// </summary>
    [Fact]
    public void TryParseSnapshot_Push_UsesOpen24hAsReference()
    {
        const string json = """{"topic":"/market/snapshot:BTC-USDT","type":"message","subject":"trade.snapshot","data":{"sequence":"1","data":{"lastTradedPrice":61771.2,"datetime":1795152506162,"marketChange24h":{"open":63459.6,"changeRate":-0.0266}}}}""";

        var parsed = KuCoinMessages.TryParseSnapshot(json, "kucoin", Fallback, out var quote);

        parsed.Should().BeTrue();
        quote.AssetId.Symbol.Should().Be("BTC-USDT");
        quote.Price.Should().Be(61771.2m);
        quote.Reference.Should().Be(63459.6m);
        quote.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1795152506162));
    }

    /// <summary>
    /// Verifies that acknowledgements and pongs are not quotes.
    /// </summary>
    [Theory]
    [InlineData("""{"id":"1","type":"ack"}""")]
    [InlineData("""{"id":"3","type":"pong","timestamp":1795152507290476}""")]
    [InlineData("""{"id":"probe1","type":"welcome"}""")]
    public void TryParseSnapshot_ControlMessage_ReturnsFalse(string text)
    {
        KuCoinMessages.TryParseSnapshot(text, "kucoin", Fallback, out _).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that an error message is reported.
    /// </summary>
    [Fact]
    public void TryParseError_ErrorMessage_ReturnsText()
    {
        KuCoinMessages.TryParseError("""{"id":"1","type":"error","code":404,"data":"topic is not found"}""").Should().Be("topic is not found");
    }

    /// <summary>
    /// Verifies that candles, which arrive newest first, are returned oldest first.
    /// </summary>
    [Fact]
    public void ParseCandles_NewestFirst_ReturnsOldestFirst()
    {
        const string json = """{"code":"200000","data":[["1795152480","1","61783.8","1","1","1","1"],["1795152420","1","61801.4","1","1","1","1"]]}""";

        var points = KuCoinMessages.ParseCandles(json);

        points.Should().HaveCount(2);
        points[0].Price.Should().Be(61801.4m);
        points[1].Timestamp.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1795152480));
    }

    /// <summary>
    /// Verifies that only symbols with trading enabled are listed.
    /// </summary>
    [Fact]
    public void ParseSymbols_Mixed_KeepsEnabled()
    {
        const string json = """{"code":"200000","data":[{"symbol":"BTC-USDT","baseCurrency":"BTC","quoteCurrency":"USDT","enableTrading":true},{"symbol":"OLD-USDT","baseCurrency":"OLD","quoteCurrency":"USDT","enableTrading":false}]}""";

        var symbols = KuCoinMessages.ParseSymbols(json);

        symbols.Should().ContainSingle().Which.Symbol.Should().Be("BTC-USDT");
    }

    /// <summary>
    /// Verifies that the history requests ask by time, over the window each range shows.
    /// </summary>
    [Fact]
    public async Task GetHistoryAsync_Ranges_AskByTime()
    {
        var now = new DateTimeOffset(2026, 10, 22, 16, 0, 0, TimeSpan.Zero);
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{"code":"200000","data":[]}"""));
        var endpoints = new ExchangeEndpoints(new Uri("wss://ws.example.test"), new Uri("https://rest.example.test"));
        await using var provider = new KuCoinProvider(
            new KuCoinProviderFactory().Descriptor, endpoints, new HttpClient(handler), new FakeTimeProvider(now), NullLogger.Instance);
        var asset = new Asset("kucoin", "BTC-USDT", "BTC/USDT", AssetClass.Crypto);

        await provider.GetHistoryAsync(asset);
        await provider.GetHistoryAsync(asset, HistoryRange.Day);

        var end = now.ToUnixTimeSeconds();
        handler.Requests.Select(r => r.RequestUri!.PathAndQuery).Should().Equal(
            $"/api/v1/market/candles?symbol=BTC-USDT&type=1min&startAt={end - (65 * 60)}&endAt={end}",
            $"/api/v1/market/candles?symbol=BTC-USDT&type=5min&startAt={end - (24 * 3600)}&endAt={end}");
    }
}
