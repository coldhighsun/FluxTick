using FluxTick.Abstractions;
using FluxTick.Core.Application;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for the trading state of an instrument derived from its market's hours.
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
    /// Instruments without a known market have no state.
    /// </summary>
    [Theory]
    [InlineData("simulated", "SIM-GOLD", AssetClass.Commodity)]
    [InlineData("yahoo", "GC=F", AssetClass.Commodity)]
    [InlineData("yahoo", "VOD.L", AssetClass.Equity)]
    [InlineData("tencent", "xx1234", AssetClass.Equity)]
    public void GetState_ForAnUnknownMarket_IsUnknown(string provider, string symbol, AssetClass assetClass)
    {
        MarketCalendar.GetState(Make(provider, symbol, assetClass), At("2026-10-28 03:00")).Should().Be(MarketState.Unknown);
    }

    /// <summary>
    /// The sessions of the Shanghai and Shenzhen markets, on a Wednesday (UTC+8).
    /// </summary>
    [Theory]
    [InlineData("2026-10-28 01:20", MarketState.PreMarket)] // 09:20
    [InlineData("2026-10-28 01:29", MarketState.PreMarket)] // 09:29
    [InlineData("2026-10-28 01:30", MarketState.Open)] // 09:30
    [InlineData("2026-10-28 03:29", MarketState.Open)] // 11:29
    [InlineData("2026-10-28 03:30", MarketState.Break)] // 11:30
    [InlineData("2026-10-28 04:59", MarketState.Break)] // 12:59
    [InlineData("2026-10-28 05:00", MarketState.Open)] // 13:00
    [InlineData("2026-10-28 06:59", MarketState.Open)] // 14:59
    [InlineData("2026-10-28 07:00", MarketState.Closed)] // 15:00
    [InlineData("2026-10-28 01:00", MarketState.Closed)] // 09:00
    [InlineData("2026-10-31 02:00", MarketState.Closed)] // Saturday
    public void GetState_ForAChineseShare_FollowsTheSessions(string utc, MarketState expected)
    {
        MarketCalendar.GetState(Make("tencent", "sh600000", AssetClass.Equity), At(utc)).Should().Be(expected);
        MarketCalendar.GetState(Make("sina", "sz000001", AssetClass.Equity), At(utc)).Should().Be(expected);
        MarketCalendar.GetState(Make("yahoo", "600000.SS", AssetClass.Equity), At(utc)).Should().Be(expected);
    }

    /// <summary>
    /// The sessions of the Hong Kong market, on a Wednesday (UTC+8).
    /// </summary>
    [Theory]
    [InlineData("2026-10-28 00:59", MarketState.Closed)] // 08:59
    [InlineData("2026-10-28 01:00", MarketState.PreMarket)] // 09:00
    [InlineData("2026-10-28 01:29", MarketState.PreMarket)] // 09:29
    [InlineData("2026-10-28 01:30", MarketState.Open)] // 09:30
    [InlineData("2026-10-28 03:59", MarketState.Open)] // 11:59
    [InlineData("2026-10-28 04:00", MarketState.Break)] // 12:00
    [InlineData("2026-10-28 05:00", MarketState.Open)] // 13:00
    [InlineData("2026-10-28 07:59", MarketState.Open)] // 15:59
    [InlineData("2026-10-28 08:09", MarketState.Open)] // 16:09, the closing auction
    [InlineData("2026-10-28 08:10", MarketState.Closed)] // 16:10
    public void GetState_ForAHongKongShare_FollowsTheSessions(string utc, MarketState expected)
    {
        MarketCalendar.GetState(Make("tencent", "hk00700", AssetClass.Equity), At(utc)).Should().Be(expected);
        MarketCalendar.GetState(Make("yahoo", "0700.HK", AssetClass.Equity), At(utc)).Should().Be(expected);
    }

    /// <summary>
    /// The sessions of the US market, in summer time (UTC-4) and, after the clocks went back on 1 November 2026, in
    /// winter time (UTC-5).
    /// </summary>
    [Theory]
    [InlineData("2026-10-28 07:59", MarketState.Closed)] // 03:59 EDT
    [InlineData("2026-10-28 08:00", MarketState.PreMarket)] // 04:00 EDT
    [InlineData("2026-10-28 13:29", MarketState.PreMarket)] // 09:29 EDT
    [InlineData("2026-10-28 13:30", MarketState.Open)] // 09:30 EDT
    [InlineData("2026-10-28 19:59", MarketState.Open)] // 15:59 EDT
    [InlineData("2026-10-28 20:00", MarketState.AfterHours)] // 16:00 EDT
    [InlineData("2026-10-29 00:00", MarketState.Closed)] // 20:00 EDT
    [InlineData("2026-11-02 14:29", MarketState.PreMarket)] // 09:29 EST
    [InlineData("2026-11-02 14:30", MarketState.Open)] // 09:30 EST
    [InlineData("2026-11-02 21:00", MarketState.AfterHours)] // 16:00 EST
    [InlineData("2026-10-31 15:00", MarketState.Closed)] // Saturday
    public void GetState_ForAUsShare_FollowsTheSessions(string utc, MarketState expected)
    {
        MarketCalendar.GetState(Make("tencent", "usAAPL", AssetClass.Equity), At(utc)).Should().Be(expected);
        MarketCalendar.GetState(Make("yahoo", "AAPL", AssetClass.Equity), At(utc)).Should().Be(expected);
        MarketCalendar.GetState(Make("yahoo", "^GSPC", AssetClass.Index), At(utc)).Should().Be(expected);
    }

    /// <summary>
    /// Forex trades from Sunday 17:00 to Friday 17:00 New York time.
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

    /// <summary>
    /// A weekday on which a market is shut, which the hours alone do not know, is found by the last trade being from an
    /// earlier day. 26 November 2026 is Thanksgiving.
    /// </summary>
    [Fact]
    public void GetState_WhenTheLastTradeIsFromAnEarlierDay_IsClosedOnceTheSessionIsWellUnderWay()
    {
        var apple = Make("yahoo", "AAPL", AssetClass.Equity);

        MarketCalendar.GetState(apple, At("2026-11-26 15:00"), At("2026-11-25 21:00")).Should().Be(MarketState.Closed);
    }

    /// <summary>
    /// A longer grace period, for a source that lags behind the market, keeps the session open for longer.
    /// </summary>
    [Fact]
    public void GetState_WithALongerGrace_StaysOpenForLonger()
    {
        var apple = Make("yahoo", "AAPL", AssetClass.Equity);
        var open = At("2026-11-26 14:30"); // Thanksgiving, 09:30 EST, the open
        var traded = At("2026-11-25 21:00");

        MarketCalendar.GetState(apple, open.AddMinutes(30), traded).Should().Be(MarketState.Closed);
        MarketCalendar.GetState(apple, open.AddMinutes(30), traded, TimeSpan.FromMinutes(45)).Should().Be(MarketState.Open);
        MarketCalendar.GetState(apple, open.AddMinutes(45), traded, TimeSpan.FromMinutes(45)).Should().Be(MarketState.Closed);
    }

    /// <summary>
    /// Yahoo symbols with a one letter share class belong to the US market, mutual funds and other suffixes do not.
    /// </summary>
    [Theory]
    [InlineData("BRK.B", MarketState.Open)]
    [InlineData("BF.B", MarketState.Open)]
    [InlineData("BRK-B", MarketState.Open)]
    [InlineData("VFIAX", MarketState.Unknown)]
    [InlineData("VOD.L", MarketState.Unknown)]
    public void GetState_ForAYahooSymbol_FindsTheUsMarketOnlyForSharesOfIt(string symbol, MarketState expected)
    {
        MarketCalendar.GetState(Make("yahoo", symbol, AssetClass.Equity), At("2026-10-28 15:00")).Should().Be(expected);
    }

    /// <summary>
    /// A trade from earlier the same day, or none known, does not make a session look shut.
    /// </summary>
    [Fact]
    public void GetState_WhenTheLastTradeIsFromToday_OrUnknown_StaysOpen()
    {
        var apple = Make("yahoo", "AAPL", AssetClass.Equity);

        MarketCalendar.GetState(apple, At("2026-11-27 15:00"), At("2026-11-27 14:59")).Should().Be(MarketState.Open);
        MarketCalendar.GetState(apple, At("2026-11-27 15:00")).Should().Be(MarketState.Open);
    }

    /// <summary>
    /// Right after the open the first trade of the day may not have come in yet.
    /// </summary>
    [Fact]
    public void GetState_JustAfterTheOpen_DoesNotYetLookForTheLastTrade()
    {
        var apple = Make("yahoo", "AAPL", AssetClass.Equity);

        MarketCalendar.GetState(apple, At("2026-10-28 13:40"), At("2026-10-27 20:00")).Should().Be(MarketState.Open);
    }

    /// <summary>
    /// Outside the regular session the last trade is from an earlier day as a matter of course.
    /// </summary>
    [Fact]
    public void GetState_InPreMarket_IgnoresTheDayOfTheLastTrade()
    {
        var apple = Make("yahoo", "AAPL", AssetClass.Equity);

        MarketCalendar.GetState(apple, At("2026-10-28 12:00"), At("2026-10-27 20:00")).Should().Be(MarketState.PreMarket);
    }
}
