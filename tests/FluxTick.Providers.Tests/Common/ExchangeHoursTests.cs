using FluxTick.Abstractions;
using FluxTick.Providers.Common;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the trading hours of the exchanges the providers share.
/// </summary>
public class ExchangeHoursTests
{
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
    /// Verifies the sessions of the Chinese A-share exchanges, with the midday break.
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
    public void GetState_ChinaA_FollowsTheSessions(string utc, MarketState expected) =>
        ExchangeHours.ChinaA.GetState(At(utc)).Should().Be(expected);

    /// <summary>
    /// Verifies a time after a session ended is pulled back to the end of that session, and any other time is kept.
    /// </summary>
    [Theory]
    [InlineData("2026-10-28 07:29", "2026-10-28 07:00")] // 15:29 after the close
    [InlineData("2026-10-28 08:30", "2026-10-28 07:00")] // 16:30 after the close
    [InlineData("2026-10-28 04:00", "2026-10-28 03:30")] // 12:00 midday break
    [InlineData("2026-10-28 03:29", "2026-10-28 03:29")] // 11:29 morning session
    [InlineData("2026-10-28 06:59", "2026-10-28 06:59")] // 14:59 afternoon session
    [InlineData("2026-10-28 01:20", "2026-10-28 01:20")] // 09:20 before the first session
    [InlineData("2026-10-31 07:29", "2026-10-31 07:29")] // Saturday
    public void ClampToSessionEnd_ChinaA_DatesAfterHoursTimesToTheSessionEnd(string utc, string expected) =>
        ExchangeHours.ChinaA.ClampToSessionEnd(At(utc)).Should().Be(At(expected));

    /// <summary>
    /// Verifies a regular session has ended from its close on, on a weekday only.
    /// </summary>
    [Theory]
    [InlineData("2026-10-28 19:59", false)] // 15:59 New York
    [InlineData("2026-10-28 20:00", true)] // 16:00 New York
    [InlineData("2026-10-28 22:00", true)] // 18:00 New York, after hours
    [InlineData("2026-10-28 13:00", false)] // 09:00 New York, pre-market
    [InlineData("2026-10-31 20:30", false)] // Saturday
    public void HasRegularSessionEnded_Us_IsTrueFromTheCloseOnWeekdays(string utc, bool expected) =>
        ExchangeHours.Us.HasRegularSessionEnded(At(utc)).Should().Be(expected);

    /// <summary>
    /// Verifies the midday break of Hong Kong is not the end of the regular session.
    /// </summary>
    [Fact]
    public void HasRegularSessionEnded_HongKongMiddayBreak_IsFalse() =>
        ExchangeHours.HongKong.HasRegularSessionEnded(At("2026-10-28 04:30")).Should().BeFalse(); // 12:30

    /// <summary>
    /// Verifies the sessions of Hong Kong, including the closing auction.
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
    public void GetState_HongKong_FollowsTheSessions(string utc, MarketState expected) =>
        ExchangeHours.HongKong.GetState(At(utc)).Should().Be(expected);

    /// <summary>
    /// Verifies the sessions of the US exchanges across the change between daylight saving time and standard time.
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
    public void GetState_Us_FollowsTheSessions(string utc, MarketState expected) =>
        ExchangeHours.Us.GetState(At(utc)).Should().Be(expected);

    /// <summary>
    /// A regular session that is well under way with the last trade from an earlier day is a holiday.
    /// </summary>
    [Fact]
    public void GetState_WhenTheLastTradeIsFromAnEarlierDay_IsClosedOnceTheSessionIsWellUnderWay() =>
        ExchangeHours.Us.GetState(At("2026-11-26 15:00"), At("2026-11-25 21:00")).Should().Be(MarketState.Closed);

    /// <summary>
    /// A longer grace keeps the market open for longer after the open.
    /// </summary>
    [Fact]
    public void GetState_WithALongerGrace_StaysOpenForLonger()
    {
        var open = At("2026-11-26 14:30"); // Thanksgiving, 09:30 EST, the open
        var traded = At("2026-11-25 21:00");

        ExchangeHours.Us.GetState(open.AddMinutes(30), traded).Should().Be(MarketState.Closed);
        ExchangeHours.Us.GetState(open.AddMinutes(30), traded, TimeSpan.FromMinutes(45)).Should().Be(MarketState.Open);
        ExchangeHours.Us.GetState(open.AddMinutes(45), traded, TimeSpan.FromMinutes(45)).Should().Be(MarketState.Closed);
    }

    /// <summary>
    /// A last trade from today, or an unknown one, leaves the market open.
    /// </summary>
    [Fact]
    public void GetState_WhenTheLastTradeIsFromToday_OrUnknown_StaysOpen()
    {
        ExchangeHours.Us.GetState(At("2026-11-27 15:00"), At("2026-11-27 14:59")).Should().Be(MarketState.Open);
        ExchangeHours.Us.GetState(At("2026-11-27 15:00")).Should().Be(MarketState.Open);
    }

    /// <summary>
    /// Just after the open the first trades of the day may not have come in yet.
    /// </summary>
    [Fact]
    public void GetState_JustAfterTheOpen_DoesNotYetLookForTheLastTrade() =>
        ExchangeHours.Us.GetState(At("2026-10-28 13:40"), At("2026-10-27 20:00")).Should().Be(MarketState.Open);

    /// <summary>
    /// Outside the regular session the last regular trade is from an earlier day as a matter of course.
    /// </summary>
    [Fact]
    public void GetState_InPreMarket_IgnoresTheDayOfTheLastTrade() =>
        ExchangeHours.Us.GetState(At("2026-10-28 12:00"), At("2026-10-27 20:00")).Should().Be(MarketState.PreMarket);
}
