using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using FluxTick.Providers.Eulerpool;
using FluxTick.Providers.Sina;
using FluxTick.Providers.Tencent;
using FluxTick.Providers.Yahoo;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for how each source maps its symbols to the exchange whose hours apply.
/// </summary>
public class SourceExchangeTests
{
    /// <summary>
    /// Verifies the market prefixes of Tencent.
    /// </summary>
    [Theory]
    [InlineData("sh600000", "ChinaA")]
    [InlineData("sz000001", "ChinaA")]
    [InlineData("bj430047", "ChinaA")]
    [InlineData("hk00700", "HongKong")]
    [InlineData("usAAPL", "Us")]
    [InlineData("xx1234", null)]
    public void Tencent_MapsMarketPrefixes(string symbol, string? expected) =>
        Name(TencentProvider.ExchangeOf(symbol)).Should().Be(expected);

    /// <summary>
    /// Verifies Sina knows the A-share codes only; currency pairs and futures have no regular hours.
    /// </summary>
    [Theory]
    [InlineData("sh600000", "ChinaA")]
    [InlineData("sz000001", "ChinaA")]
    [InlineData("fx_seurusd", null)]
    [InlineData("hf_GC", null)]
    public void Sina_MapsAShareCodesOnly(string symbol, string? expected) =>
        Name(SinaProvider.ExchangeOf(symbol)).Should().Be(expected);

    /// <summary>
    /// Verifies the symbols of Yahoo: suffixes, indices, share classes of US companies, and what has no hours.
    /// </summary>
    [Theory]
    [InlineData("600000.SS", "ChinaA")]
    [InlineData("000001.SZ", "ChinaA")]
    [InlineData("0700.HK", "HongKong")]
    [InlineData("AAPL", "Us")]
    [InlineData("^GSPC", "Us")]
    [InlineData("^HSI", "HongKong")]
    [InlineData("BRK.B", "Us")]
    [InlineData("BF.B", "Us")]
    [InlineData("BRK-B", "Us")]
    [InlineData("VFIAX", null)]
    [InlineData("VOD.L", null)]
    [InlineData("GC=F", null)]
    [InlineData("EURUSD=X", null)]
    [InlineData("^FTSE", null)]
    public void Yahoo_MapsSymbols(string symbol, string? expected) =>
        Name(YahooProvider.ExchangeOf(symbol)).Should().Be(expected);

    /// <summary>
    /// Verifies the tickers of Eulerpool: no suffix is a US listing, the listed suffixes have their exchange, and an
    /// exchange that is not known has none rather than a wrong one.
    /// </summary>
    [Theory]
    [InlineData("AAPL", "Us")]
    [InlineData("0700.HK", "HongKong")]
    [InlineData("SAP.DE", "Other")]
    [InlineData("VOD.L", "Other")]
    [InlineData("0700.XYZ", null)]
    [InlineData("BRK.B", null)]
    public void Eulerpool_MapsTickerSuffixes(string symbol, string? expected) =>
        Name(EulerpoolExchanges.Of(symbol)).Should().Be(expected);

    /// <summary>
    /// Verifies each exchange is read in its own time zone: 11:00 in Berlin is open, 11:00 in New York is not yet.
    /// </summary>
    [Fact]
    public void Eulerpool_ReadsEachExchangeInItsOwnZone()
    {
        var utc = new DateTimeOffset(2026, 10, 28, 10, 0, 0, TimeSpan.Zero); // 11:00 Berlin, 06:00 New York

        EulerpoolExchanges.Of("SAP.DE")!.GetState(utc).Should().Be(MarketState.Open);
        EulerpoolExchanges.Of("AAPL")!.GetState(utc).Should().Be(MarketState.PreMarket);
    }

    /// <summary>
    /// Verifies a weekend is shut in Berlin.
    /// </summary>
    [Fact]
    public void Eulerpool_Saturday_IsClosed() =>
        EulerpoolExchanges.Of("SAP.DE")!.GetState(new DateTimeOffset(2026, 10, 31, 11, 0, 0, TimeSpan.FromHours(1)))
            .Should().Be(MarketState.Closed);

    /// <summary>
    /// Names a shared exchange, or <c>Other</c> for one a source defines itself.
    /// </summary>
    /// <param name="exchange">
    /// The exchange, or <see langword="null"/>.
    /// </param>
    /// <returns>
    /// The name, or <see langword="null"/> for no exchange.
    /// </returns>
    private static string? Name(ExchangeHours? exchange) =>
        exchange is null ? null
        : ReferenceEquals(exchange, ExchangeHours.ChinaA) ? "ChinaA"
        : ReferenceEquals(exchange, ExchangeHours.HongKong) ? "HongKong"
        : ReferenceEquals(exchange, ExchangeHours.Us) ? "Us"
        : "Other";
}
