using FluxTick.Abstractions;
using FluxTick.Providers.Binance;
using FluxTick.Providers.Bybit;
using FluxTick.Providers.Coinbase;
using FluxTick.Providers.Common;
using FluxTick.Providers.Kraken;
using FluxTick.Providers.Okx;
using FluxTick.Providers.Simulated;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the history of ranges longer than the last hour: the request each source makes for it, and how many points
/// it keeps.
/// </summary>
public class RangeHistoryTests
{
    /// <summary>
    /// The time of the clock the providers run on.
    /// </summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 16, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies the request of each exchange for each range.
    /// </summary>
    /// <param name="exchange">
    /// The exchange.
    /// </param>
    /// <param name="range">
    /// The range.
    /// </param>
    /// <param name="expected">
    /// The exact path and query that must be requested.
    /// </param>
    [Theory]
    [InlineData("binance", HistoryRange.Day, "/api/v3/klines?symbol=BTC&interval=5m&limit=288")]
    [InlineData("binance", HistoryRange.FiveDays, "/api/v3/klines?symbol=BTC&interval=30m&limit=240")]
    [InlineData("binance", HistoryRange.Month, "/api/v3/klines?symbol=BTC&interval=4h&limit=180")]
    [InlineData("binance", HistoryRange.HalfYear, "/api/v3/klines?symbol=BTC&interval=1d&limit=183")]
    [InlineData("binance", HistoryRange.Year, "/api/v3/klines?symbol=BTC&interval=1d&limit=365")]
    [InlineData("bybit", HistoryRange.Day, "/v5/market/kline?category=spot&symbol=BTC&interval=5&limit=288")]
    [InlineData("bybit", HistoryRange.FiveDays, "/v5/market/kline?category=spot&symbol=BTC&interval=30&limit=240")]
    [InlineData("bybit", HistoryRange.Month, "/v5/market/kline?category=spot&symbol=BTC&interval=240&limit=180")]
    [InlineData("bybit", HistoryRange.HalfYear, "/v5/market/kline?category=spot&symbol=BTC&interval=D&limit=183")]
    [InlineData("bybit", HistoryRange.Year, "/v5/market/kline?category=spot&symbol=BTC&interval=D&limit=365")]
    [InlineData("okx", HistoryRange.Day, "/api/v5/market/candles?instId=BTC&bar=5m&limit=288")]
    [InlineData("okx", HistoryRange.FiveDays, "/api/v5/market/candles?instId=BTC&bar=30m&limit=240")]
    [InlineData("okx", HistoryRange.Month, "/api/v5/market/candles?instId=BTC&bar=4H&limit=180")]
    [InlineData("okx", HistoryRange.HalfYear, "/api/v5/market/candles?instId=BTC&bar=1Dutc&limit=183")]
    [InlineData("okx", HistoryRange.Year, "/api/v5/market/candles?instId=BTC&bar=1Dutc&limit=300")]
    [InlineData("kraken", HistoryRange.Day, "/0/public/OHLC?pair=BTC&interval=5&since=1792598400")]
    [InlineData("kraken", HistoryRange.FiveDays, "/0/public/OHLC?pair=BTC&interval=30&since=1792252800")]
    [InlineData("kraken", HistoryRange.Month, "/0/public/OHLC?pair=BTC&interval=240&since=1790092800")]
    [InlineData("kraken", HistoryRange.HalfYear, "/0/public/OHLC?pair=BTC&interval=1440&since=1776873600")]
    [InlineData("kraken", HistoryRange.Year, "/0/public/OHLC?pair=BTC&interval=1440&since=1761148800")]
    [InlineData("coinbase", HistoryRange.Day, "/products/BTC/candles?granularity=300&start=2026-10-21T16:00:00Z&end=2026-10-22T16:00:00Z")]
    [InlineData("coinbase", HistoryRange.FiveDays, "/products/BTC/candles?granularity=3600&start=2026-10-17T16:00:00Z&end=2026-10-22T16:00:00Z")]
    [InlineData("coinbase", HistoryRange.Month, "/products/BTC/candles?granularity=21600&start=2026-09-22T16:00:00Z&end=2026-10-22T16:00:00Z")]
    [InlineData("coinbase", HistoryRange.HalfYear, "/products/BTC/candles?granularity=86400&start=2026-04-22T16:00:00Z&end=2026-10-22T16:00:00Z")]
    [InlineData("coinbase", HistoryRange.Year, "/products/BTC/candles?granularity=86400&start=2025-12-26T16:00:00Z&end=2026-10-22T16:00:00Z")]
    public async Task GetHistoryAsync_Range_AsksForItsCandles(string exchange, HistoryRange range, string expected)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(EmptyAnswerOf(exchange)));
        await using var provider = CreateProvider(exchange, new HttpClient(handler));
        var history = (IRangeHistoryProvider)provider;

        var points = await history.GetHistoryAsync(new Asset(exchange, "BTC", "BTC", AssetClass.Crypto), range);

        points.Should().BeEmpty();
        history.SupportsRange(range).Should().BeTrue();
        handler.Requests.Select(r => r.RequestUri!.PathAndQuery).Should().Equal(expected);
    }

    /// <summary>
    /// Verifies the last hour asked for through the range overload is the request that was always made.
    /// </summary>
    [Fact]
    public async Task GetHistoryAsync_HourRange_IsTheUsualRequest()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("[]"));
        await using var provider = CreateProvider("binance", new HttpClient(handler));

        await ((IRangeHistoryProvider)provider).GetHistoryAsync(new Asset("binance", "BTC", "BTC", AssetClass.Crypto), HistoryRange.Hour);

        handler.Requests.Select(r => r.RequestUri!.PathAndQuery).Should().Equal("/api/v3/klines?symbol=BTC&interval=1m&limit=60");
    }

    /// <summary>
    /// Verifies the candles of a day are not cut to the sixty of the last hour.
    /// </summary>
    [Fact]
    public async Task GetHistoryAsync_CoinbaseDay_KeepsMoreThanAnHour()
    {
        var candles = string.Join(',', Enumerable.Range(0, 200).Select(i => $"[{1792600000 + (i * 300)},1,1,1,{100 + i},1]"));
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json($"[{candles}]"));
        await using var provider = CreateProvider("coinbase", new HttpClient(handler));

        var points = await ((IRangeHistoryProvider)provider).GetHistoryAsync(new Asset("coinbase", "BTC", "BTC", AssetClass.Crypto), HistoryRange.Day);

        points.Should().HaveCount(200);
    }

    /// <summary>
    /// Verifies the simulated source gives a series that fills the range.
    /// </summary>
    /// <param name="range">
    /// The range.
    /// </param>
    [Theory]
    [InlineData(HistoryRange.Hour)]
    [InlineData(HistoryRange.Day)]
    [InlineData(HistoryRange.HalfYear)]
    [InlineData(HistoryRange.Year)]
    public async Task Simulated_GetHistoryAsync_FillsTheRange(HistoryRange range)
    {
        var time = new FakeTimeProvider(Now);
        await using var provider = new SimulatedProvider(new SimulatedProviderFactory().Descriptor, TimeSpan.FromSeconds(1), time);
        var spec = HistoryRangeSpec.For(range);

        var points = await provider.GetHistoryAsync(new Asset(SimulatedProviderFactory.ProviderId, "SIM-BTC", "Bitcoin", AssetClass.Crypto), range);

        points.Should().HaveCount(spec.Capacity);
        points.Should().BeInAscendingOrder(p => p.Timestamp);
        points[^1].Timestamp.Should().Be(Now - spec.Bucket);
        points[0].Timestamp.Should().Be(Now - spec.Window);
    }

    /// <summary>
    /// Creates one of the exchange providers.
    /// </summary>
    /// <param name="exchange">
    /// The exchange.
    /// </param>
    /// <param name="http">
    /// The HTTP client the provider takes.
    /// </param>
    /// <returns>
    /// The provider.
    /// </returns>
    private static IMarketDataProvider CreateProvider(string exchange, HttpClient http)
    {
        var endpoints = new ExchangeEndpoints(new Uri("wss://ws.example.test"), new Uri("https://rest.example.test"));
        var time = new FakeTimeProvider(Now);
        return exchange switch
        {
            "binance" => new BinanceProvider(new BinanceProviderFactory().Descriptor, endpoints, http, time, NullLogger.Instance),
            "bybit" => new BybitProvider(new BybitProviderFactory().Descriptor, endpoints, http, time, NullLogger.Instance),
            "okx" => new OkxProvider(new OkxProviderFactory().Descriptor, endpoints, http, time, NullLogger.Instance),
            "kraken" => new KrakenProvider(new KrakenProviderFactory().Descriptor, endpoints, http, time, NullLogger.Instance),
            _ => new CoinbaseProvider(new CoinbaseProviderFactory().Descriptor, endpoints, http, time, NullLogger.Instance),
        };
    }

    /// <summary>
    /// Gives the answer of an exchange that has no candles.
    /// </summary>
    /// <param name="exchange">
    /// The exchange.
    /// </param>
    /// <returns>
    /// The response body.
    /// </returns>
    private static string EmptyAnswerOf(string exchange) => exchange switch
    {
        "bybit" => """{"retCode":0,"retMsg":"OK","result":{"list":[]}}""",
        "okx" => """{"code":"0","msg":"","data":[]}""",
        "kraken" => """{"error":[],"result":{"last":1}}""",
        _ => "[]",
    };
}
