using FluxTick.Abstractions;
using FluxTick.Providers.Binance;
using FluxTick.Providers.Bybit;
using FluxTick.Providers.Coinbase;
using FluxTick.Providers.Common;
using FluxTick.Providers.Kraken;
using FluxTick.Providers.Okx;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Drives each crypto exchange provider end to end against a loopback WebSocket server and a stub REST endpoint:
/// the subscribe message it sends, the ticker it publishes, and the REST requests behind history and search.
/// These are the parts the parser tests cannot see: a typo in a path or a swapped message would fail here.
/// </summary>
public class CryptoProviderWiringTests
{
    /// <summary>
    /// Verifies the OKX provider.
    /// </summary>
    [Fact]
    public Task Okx_IsWiredCorrectly() => VerifyAsync(new Wiring(
        (descriptor, endpoints, http) => new OkxProvider(descriptor, endpoints, http, TimeProvider.System, NullLogger.Instance),
        new OkxProviderFactory().Descriptor,
        "BTC-USDT",
        """{"op":"subscribe","args":[{"channel":"tickers","instId":"BTC-USDT"}]}""",
        """{"arg":{"channel":"tickers","instId":"BTC-USDT"},"data":[{"instId":"BTC-USDT","last":"81519.3","open24h":"84593.9","ts":"1792683280970"}]}""",
        81519.3m,
        "/api/v5/market/candles?instId=BTC-USDT&bar=1m&limit=60",
        """{"code":"0","msg":"","data":[["1792683240000","1","1","1","81525.7","1"],["1792683180000","1","1","1","81530.7","1"]]}""",
        "/api/v5/public/instruments?instType=SPOT",
        """{"code":"0","msg":"","data":[{"instId":"BTC-USDT","state":"live"},{"instId":"ETH-USDT","state":"live"}]}"""));

    /// <summary>
    /// Verifies the Coinbase provider.
    /// </summary>
    [Fact]
    public Task Coinbase_IsWiredCorrectly() => VerifyAsync(new Wiring(
        (descriptor, endpoints, http) => new CoinbaseProvider(descriptor, endpoints, http, new FakeTimeProvider(new DateTimeOffset(2026, 10, 22, 16, 0, 0, TimeSpan.Zero)), NullLogger.Instance),
        new CoinbaseProviderFactory().Descriptor,
        "BTC-USD",
        """{"type":"subscribe","product_ids":["BTC-USD"],"channels":["ticker"]}""",
        """{"type":"ticker","product_id":"BTC-USD","price":"81421.96","open_24h":"84474.36","time":"2026-10-22T15:34:42.413298Z"}""",
        81421.96m,
        "/products/BTC-USD/candles?granularity=60&start=2026-10-22T14:55:00Z&end=2026-10-22T16:00:00Z",
        "[[1792683420,1,1,1,81530.8,1],[1792683360,1,1,1,81515.88,2]]",
        "/products",
        """[{"id":"BTC-USD","status":"online"},{"id":"ETH-USD","status":"online"}]"""));

    /// <summary>
    /// Verifies the Kraken provider.
    /// </summary>
    [Fact]
    public Task Kraken_IsWiredCorrectly() => VerifyAsync(new Wiring(
        (descriptor, endpoints, http) => new KrakenProvider(descriptor, endpoints, http, new FakeTimeProvider(new DateTimeOffset(2026, 10, 22, 16, 0, 0, TimeSpan.Zero)), NullLogger.Instance),
        new KrakenProviderFactory().Descriptor,
        "BTC/USD",
        """{"method":"subscribe","params":{"channel":"ticker","symbol":["BTC/USD"]}}""",
        """{"channel":"ticker","type":"update","data":[{"symbol":"BTC/USD","last":81418.9,"change":-3140.2,"timestamp":"2026-10-22T15:34:43.229705Z"}]}""",
        81418.9m,
        "/0/public/OHLC?pair=BTC%2FUSD&interval=1&since=1792680900",
        """{"error":[],"result":{"BTC/USD":[[1792640280,"1","1","1","82722.4","1","1",1]],"last":1792640280}}""",
        "/0/public/AssetPairs",
        """{"error":[],"result":{"XXBTZUSD":{"wsname":"XBT/USD","status":"online"},"XETHZUSD":{"wsname":"ETH/USD","status":"online"}}}"""));

    /// <summary>
    /// Verifies the Bybit provider.
    /// </summary>
    [Fact]
    public Task Bybit_IsWiredCorrectly() => VerifyAsync(new Wiring(
        (descriptor, endpoints, http) => new BybitProvider(descriptor, endpoints, http, TimeProvider.System, NullLogger.Instance),
        new BybitProviderFactory().Descriptor,
        "BTCUSDT",
        """{"op":"subscribe","args":["tickers.BTCUSDT"]}""",
        """{"topic":"tickers.BTCUSDT","ts":1792683284127,"type":"snapshot","data":{"symbol":"BTCUSDT","lastPrice":"81513.7","prevPrice24h":"84625.6"}}""",
        81513.7m,
        "/v5/market/kline?category=spot&symbol=BTCUSDT&interval=1&limit=60",
        """{"retCode":0,"retMsg":"OK","result":{"list":[["1792683480000","1","1","1","81641.1","1","1"]]}}""",
        "/v5/market/instruments-info?category=spot&limit=1000",
        """{"retCode":0,"retMsg":"OK","result":{"list":[{"symbol":"BTCUSDT","baseCoin":"BTC","quoteCoin":"USDT","status":"Trading"},{"symbol":"ETHUSDT","baseCoin":"ETH","quoteCoin":"USDT","status":"Trading"}]}}"""));

    /// <summary>
    /// Verifies the Binance provider.
    /// </summary>
    [Fact]
    public Task Binance_IsWiredCorrectly() => VerifyAsync(new Wiring(
        (descriptor, endpoints, http) => new BinanceProvider(descriptor, endpoints, http, TimeProvider.System, NullLogger.Instance),
        new BinanceProviderFactory().Descriptor,
        "BTCUSDT",
        """{"method":"SUBSCRIBE","params":["btcusdt@miniTicker"],"id":1}""",
        """{"e":"24hrMiniTicker","E":1700000000000,"s":"BTCUSDT","c":"67000.10","o":"66000.00"}""",
        67000.10m,
        "/api/v3/klines?symbol=BTCUSDT&interval=1m&limit=60",
        """[[1700000000000,"1","1","1","67000.5","1"]]""",
        "/api/v3/ticker/price",
        """[{"symbol":"BTCUSDT","price":"1"},{"symbol":"ETHUSDT","price":"2"}]"""));

    /// <summary>
    /// Verifies the keep-alive messages the exchanges that need one are configured with.
    /// </summary>
    [Fact]
    public void KeepAliveMessages_AreTheDocumentedOnes()
    {
        OkxMessages.Ping.Should().Be("ping");
        BybitMessages.Ping.Should().Be("""{"op":"ping"}""");
    }

    /// <summary>
    /// Verifies a REST endpoint with a path, such as a proxy, keeps that path in front of every request.
    /// </summary>
    [Fact]
    public async Task GetHistoryAsync_RestEndpointWithPath_KeepsItsPrefix()
    {
        var handler = new StubHttpHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/ticker/price", StringComparison.Ordinal)
            ? StubHttpHandler.Json("""[{"symbol":"BTCUSDT","price":"1"}]""")
            : StubHttpHandler.Json("[]"));
        var endpoints = new ExchangeEndpoints(new Uri("ws://127.0.0.1:1/"), new Uri("https://proxy.example.test/binance/"));
        await using var provider = new BinanceProvider(
            new BinanceProviderFactory().Descriptor,
            endpoints,
            new HttpClient(handler),
            TimeProvider.System,
            NullLogger.Instance);

        await provider.GetHistoryAsync(new Asset("binance", "BTCUSDT", "BTC/USDT", AssetClass.Crypto));
        await provider.SearchAsync("btc", null);

        handler.Requests.Select(r => r.RequestUri!.PathAndQuery).Should().Equal(
            "/binance/api/v3/klines?symbol=BTCUSDT&interval=1m&limit=60",
            "/binance/api/v3/ticker/price");
    }

    /// <summary>
    /// Runs one provider through subscribe, quote, history and search.
    /// </summary>
    /// <param name="wiring">
    /// The provider and the payloads to use.
    /// </param>
    /// <returns>
    /// A task that completes when all checks passed.
    /// </returns>
    private static async Task VerifyAsync(Wiring wiring)
    {
        await using var server = LoopbackServer.Start();
        var handler = new StubHttpHandler(request =>
        {
            var target = request.RequestUri!.PathAndQuery;
            if (target == wiring.HistoryRequest)
            {
                return StubHttpHandler.Json(wiring.HistoryJson);
            }

            return target == wiring.SymbolsRequest
                ? StubHttpHandler.Json(wiring.SymbolsJson)
                : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var endpoints = new ExchangeEndpoints(server.Uri, new Uri("https://rest.example.test"));
        await using var provider = wiring.Create(wiring.Descriptor, endpoints, new HttpClient(handler));
        var asset = new Asset(wiring.Descriptor.Id, wiring.Symbol, wiring.Symbol, AssetClass.Crypto);

        await provider.SetSubscriptionsAsync([asset]);
        await server.WaitForMessageAsync(wiring.SubscribeMessage);
        await server.SendAsync(wiring.TickerMessage);
        using var timeout = new CancellationTokenSource(TestWait.Patience);
        Quote? quote = null;
        await foreach (var received in provider.ReadQuotesAsync(timeout.Token))
        {
            quote = received;
            break;
        }

        quote.Should().NotBeNull();
        quote!.AssetId.Should().Be(asset.Id);
        quote.Price.Should().Be(wiring.ExpectedPrice);

        var history = await ((IHistoryProvider)provider).GetHistoryAsync(asset);
        history.Should().NotBeEmpty();

        var found = await ((ISymbolSearchProvider)provider).SearchAsync("btc", null);
        found.Select(a => a.Symbol).Should().Contain(wiring.Symbol);
        found.Should().OnlyContain(a => a.ProviderId == wiring.Descriptor.Id && a.AssetClass == AssetClass.Crypto);

        handler.Requests.Select(r => r.RequestUri!.PathAndQuery).Should().BeEquivalentTo([wiring.HistoryRequest, wiring.SymbolsRequest]);
        var forex = await ((ISymbolSearchProvider)provider).SearchAsync("btc", AssetClass.Forex);
        forex.Should().BeEmpty("a crypto exchange has no forex pairs");
    }

    /// <summary>
    /// One provider and the payloads that drive it.
    /// </summary>
    /// <param name="Create">
    /// Creates the provider.
    /// </param>
    /// <param name="Descriptor">
    /// The provider descriptor.
    /// </param>
    /// <param name="Symbol">
    /// The instrument to subscribe.
    /// </param>
    /// <param name="SubscribeMessage">
    /// The exact subscribe message the provider must send.
    /// </param>
    /// <param name="TickerMessage">
    /// A ticker push the server sends back.
    /// </param>
    /// <param name="ExpectedPrice">
    /// The price of the ticker push.
    /// </param>
    /// <param name="HistoryRequest">
    /// The exact path and query of the history request.
    /// </param>
    /// <param name="HistoryJson">
    /// The history response.
    /// </param>
    /// <param name="SymbolsRequest">
    /// The exact path and query of the pair listing request.
    /// </param>
    /// <param name="SymbolsJson">
    /// The pair listing response.
    /// </param>
    private sealed record Wiring(
        Func<ProviderDescriptor, ExchangeEndpoints, HttpClient, IMarketDataProvider> Create,
        ProviderDescriptor Descriptor,
        string Symbol,
        string SubscribeMessage,
        string TickerMessage,
        decimal ExpectedPrice,
        string HistoryRequest,
        string HistoryJson,
        string SymbolsRequest,
        string SymbolsJson);
}
