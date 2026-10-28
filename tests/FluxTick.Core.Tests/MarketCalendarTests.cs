using FluxTick.Abstractions;
using FluxTick.Core.Application;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for the trading state of the asset classes whose hours do not depend on the source. The hours of stock
/// exchanges belong to the providers and are tested with them.
/// </summary>
public class MarketCalendarTests
{
    /// <summary>
    /// Creates an instrument.
    /// </summary>
    /// <param name="provider">
    /// The provider id.
    /// </param>
    /// <param name="symbol">
    /// The symbol.
    /// </param>
    /// <param name="assetClass">
    /// The asset class.
    /// </param>
    /// <returns>
    /// The instrument.
    /// </returns>
    private static Asset Make(string provider, string symbol, AssetClass assetClass) => new(provider, symbol, symbol, assetClass);

    /// <summary>
    /// Parses a UTC time.
    /// </summary>
    /// <param name="utc">
    /// The time as <c>yyyy-MM-dd HH:mm</c> in UTC.
    /// </param>
    /// <returns>
    /// The time.
    /// </returns>
    private static DateTimeOffset At(string utc) =>
        new(DateTime.SpecifyKind(DateTime.ParseExact(utc, "yyyy-MM-dd HH:mm", null), DateTimeKind.Utc));

    /// <summary>
    /// A crypto pair trades around the clock.
    /// </summary>
    [Fact]
    public void GetState_ForCrypto_IsAlwaysOpen()
    {
        var btc = Make("binance", "BTCUSDT", AssetClass.Crypto);

        MarketCalendar.GetState(btc, At("2026-10-31 03:00")).Should().Be(MarketState.Open);
    }

    /// <summary>
    /// Stocks, indices and commodities have hours that only the source can tell, so the calendar knows no state for them.
    /// </summary>
    [Theory]
    [InlineData("simulated", "SIM-GOLD", AssetClass.Commodity)]
    [InlineData("yahoo", "GC=F", AssetClass.Commodity)]
    [InlineData("yahoo", "AAPL", AssetClass.Equity)]
    [InlineData("tencent", "sh600000", AssetClass.Equity)]
    [InlineData("yahoo", "^GSPC", AssetClass.Index)]
    public void GetState_ForAClassWhoseHoursTheSourceTells_IsUnknown(string provider, string symbol, AssetClass assetClass)
    {
        MarketCalendar.GetState(Make(provider, symbol, assetClass), At("2026-10-28 03:00")).Should().Be(MarketState.Unknown);
    }

    /// <summary>
    /// Forex trades from Sunday evening to Friday evening in New York, whatever the source.
    /// </summary>
    [Theory]
    [InlineData("2026-10-28 03:00", MarketState.Open)] // Wednesday
    [InlineData("2026-10-31 03:00", MarketState.Closed)] // Saturday
    [InlineData("2026-11-01 20:59", MarketState.Closed)] // Sunday 16:59 EDT
    [InlineData("2026-11-01 22:00", MarketState.Open)] // Sunday 17:00 EST (the clocks went back that night)
    [InlineData("2026-10-30 20:59", MarketState.Open)] // Friday 16:59 EDT
    [InlineData("2026-10-30 21:00", MarketState.Closed)] // Friday 17:00 EDT
    public void GetState_ForForex_ClosesOverTheWeekend(string utc, MarketState expected)
    {
        MarketCalendar.GetState(Make("yahoo", "EURUSD=X", AssetClass.Forex), At(utc)).Should().Be(expected);
    }
}
