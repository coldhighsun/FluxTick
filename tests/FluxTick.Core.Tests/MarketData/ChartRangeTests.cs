using FluxTick.Abstractions;
using FluxTick.Core.Application;
using FluxTick.Core.Tests.TestDoubles;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for the time span of the charts: the shape of a range, the gaps of its timeline and the history behind it.
/// </summary>
public class ChartRangeTests
{
    /// <summary>
    /// The instrument the tests track.
    /// </summary>
    private static readonly Asset Btc = new("crypto", "BTC", "Bitcoin", AssetClass.Crypto);

    /// <summary>
    /// A stock, whose market has hours.
    /// </summary>
    private static readonly Asset Stock = new("crypto", "sh600000", "Pudong", AssetClass.Equity);

    /// <summary>
    /// The clock of the tests.
    /// </summary>
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Verifies the window of each range.
    /// </summary>
    /// <param name="range">
    /// The range.
    /// </param>
    /// <param name="hours">
    /// The expected window in hours.
    /// </param>
    [Theory]
    [InlineData(HistoryRange.Hour, 1)]
    [InlineData(HistoryRange.Day, 24)]
    [InlineData(HistoryRange.FiveDays, 120)]
    [InlineData(HistoryRange.Month, 720)]
    [InlineData(HistoryRange.HalfYear, 4392)]
    [InlineData(HistoryRange.Year, 8760)]
    public void For_Range_CoversItsWindow(HistoryRange range, int hours)
    {
        var spec = HistoryRangeSpec.For(range);

        spec.Window.Should().Be(TimeSpan.FromHours(hours));
        spec.Capacity.Should().BeGreaterThan(1);
    }

    /// <summary>
    /// Verifies prices of a longer bucket may be several buckets apart without a gap, as a night or a weekend is not a
    /// lost stretch of the chart.
    /// </summary>
    [Fact]
    public void IsGap_LongBucket_NeedsSeveralBucketsWithoutPrice()
    {
        var asOf = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var bucket = TimeSpan.FromMinutes(30);
        DateTimeOffset[] times = [asOf.AddHours(-1), asOf.AddMinutes(-30)];

        var timeline = new SparklineTimeline(times, asOf, TimeSpan.FromDays(5), bucket);

        // The window is five days, so a quarter of it, 30 hours, is the least that counts as a gap.
        timeline.IsGap(asOf.AddMinutes(-30), asOf.AddMinutes(-30).AddHours(29)).Should().BeFalse();
        timeline.IsGap(asOf.AddMinutes(-30), asOf.AddMinutes(-30).AddHours(31)).Should().BeTrue();
    }

    /// <summary>
    /// Verifies the nights of a market do not break the line of a month, and a lost week does.
    /// </summary>
    [Fact]
    public void IsGap_MonthOfAMarket_NightsAndWeekendsAreNotGaps()
    {
        var asOf = new DateTimeOffset(2026, 1, 30, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset[] times = [asOf.AddHours(-8), asOf.AddHours(-4)];

        var timeline = new SparklineTimeline(times, asOf, TimeSpan.FromDays(30), TimeSpan.FromHours(4));

        timeline.IsGap(asOf.AddHours(-30), asOf.AddHours(-12)).Should().BeFalse("a night is 18 hours");
        timeline.IsGap(asOf.AddHours(-100), asOf.AddHours(-34)).Should().BeFalse("a weekend is 66 hours");
        timeline.IsGap(asOf.AddDays(-20), asOf.AddDays(-10)).Should().BeTrue();
    }

    /// <summary>
    /// Verifies a stretch without prices in which the market was shut is joined by the line, and one in which it was open is
    /// a gap.
    /// </summary>
    /// <param name="shut">
    /// Whether the market is shut at every time.
    /// </param>
    /// <param name="expectedGap">
    /// Whether the stretch is a gap.
    /// </param>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void IsGapAt_StretchWithoutPrices_IsAGapOnlyWhileTheMarketIsOpen(bool shut, bool expectedGap)
    {
        var asOf = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset[] times = [asOf.AddHours(-30), asOf.AddHours(-29), asOf.AddHours(-2), asOf.AddHours(-1)];

        var timeline = new SparklineTimeline(times, asOf, TimeSpan.FromDays(1), TimeSpan.FromMinutes(5), isMarketShut: _ => shut);

        timeline.IsGapAt(2).Should().Be(expectedGap);
        timeline.IsGapAt(1).Should().BeFalse("the prices are close");
        timeline.IsGapAt(0).Should().BeFalse();
    }

    /// <summary>
    /// Verifies a timeline built for the same times again takes the shut stretches from the memo without asking the hours
    /// of the market, and one built for other times asks again.
    /// </summary>
    [Fact]
    public void Constructor_ShutGapMemo_IsUsedOnlyForTheSameTimes()
    {
        var asOf = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
        IReadOnlyList<DateTimeOffset> times = [asOf.AddHours(-30), asOf.AddHours(-29), asOf.AddHours(-2), asOf.AddHours(-1)];
        var memo = new ShutGapMemo();
        var asked = 0;
        bool Shut(DateTimeOffset _)
        {
            asked++;
            return true;
        }

        var first = new SparklineTimeline(times, asOf, TimeSpan.FromDays(1), TimeSpan.FromMinutes(5), isMarketShut: Shut, shutGapMemo: memo);
        var askedForFirst = asked;
        var second = new SparklineTimeline(times, asOf.AddSeconds(1), TimeSpan.FromDays(1), TimeSpan.FromMinutes(5), isMarketShut: Shut, shutGapMemo: memo);

        askedForFirst.Should().BeGreaterThan(0);
        asked.Should().Be(askedForFirst, "the times are the same array");
        second.IsGapAt(2).Should().Be(first.IsGapAt(2)).And.BeFalse();

        IReadOnlyList<DateTimeOffset> moved = [.. times];
        _ = new SparklineTimeline(moved, asOf, TimeSpan.FromDays(1), TimeSpan.FromMinutes(5), isMarketShut: Shut, shutGapMemo: memo);

        asked.Should().BeGreaterThan(askedForFirst, "another array of times is another answer");
    }

    /// <summary>
    /// Verifies the stretch after the last price is a gap only while the market was open in it: a stock that closed, or a
    /// weekend, is not a lack of prices on a long range.
    /// </summary>
    /// <param name="coversRange">
    /// Whether the prices are the history of a longer range.
    /// </param>
    /// <param name="shut">
    /// Whether the market is shut at every time.
    /// </param>
    /// <param name="expected">
    /// Whether the stretch after the last price is a gap.
    /// </param>
    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void HasTrailingGap_LastPriceLongAgo_IsAGapOnlyWhileTheMarketIsOpen(bool coversRange, bool shut, bool expected)
    {
        var asOf = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset[] times = [asOf.AddHours(-20), asOf.AddHours(-19), asOf.AddHours(-18)];

        var timeline = new SparklineTimeline(times, asOf, TimeSpan.FromDays(1), TimeSpan.FromMinutes(5), coversRange ? HistoryRange.Day : null, _ => shut);

        timeline.HasTrailingGap.Should().Be(expected);
    }

    /// <summary>
    /// Verifies a last price that is recent is not a gap whatever the hours say.
    /// </summary>
    [Fact]
    public void HasTrailingGap_RecentLastPrice_IsNotAGap()
    {
        var asOf = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset[] times = [asOf.AddMinutes(-10), asOf.AddMinutes(-5)];

        var timeline = new SparklineTimeline(times, asOf, TimeSpan.FromDays(1), TimeSpan.FromMinutes(5), HistoryRange.Day, _ => false);

        timeline.HasTrailingGap.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a market that was open for most of the stretch makes it a gap, although it was shut for a short while.
    /// </summary>
    [Fact]
    public void IsGapAt_MarketShutForAShareOfTheStretch_IsStillAGap()
    {
        var asOf = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset[] times = [asOf.AddHours(-31), asOf.AddHours(-30), asOf.AddHours(-2), asOf.AddHours(-1)];

        // Shut for the first six of the 28 hours between the prices only.
        var timeline = new SparklineTimeline(times, asOf, TimeSpan.FromDays(2), TimeSpan.FromMinutes(5), isMarketShut: t => t < asOf.AddHours(-24));

        timeline.IsGapAt(2).Should().BeTrue();
    }

    /// <summary>
    /// Verifies the night between two sessions of a market with hours is joined, and a source that is silent during the
    /// session is a gap.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_NightBetweenSessions_IsJoinedAndAnOutageIsNot()
    {
        // Tuesday 14:00 in Beijing, with the Monday afternoon and the Tuesday morning in the history of the day.
        var now = new DateTimeOffset(2026, 1, 6, 6, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var beijing = TimeSpan.FromHours(8);
        var factory = new HoursFactory(
        ExchangeHours.ChinaA,
        [
            new PricePoint(new DateTimeOffset(2026, 1, 5, 14, 30, 0, beijing), 10m),
            new PricePoint(new DateTimeOffset(2026, 1, 5, 14, 55, 0, beijing), 11m),
            new PricePoint(new DateTimeOffset(2026, 1, 6, 9, 35, 0, beijing), 12m),
            new PricePoint(new DateTimeOffset(2026, 1, 6, 9, 40, 0, beijing), 13m),
            new PricePoint(new DateTimeOffset(2026, 1, 6, 10, 0, 0, beijing), 14m),
            new PricePoint(new DateTimeOffset(2026, 1, 6, 13, 30, 0, beijing), 15m),
        ]);
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, clock);
        service.SetChartRange(HistoryRange.Day);
        var seeded = new TaskCompletionSource<AssetSnapshot>();
        service.SnapshotChanged += (_, s) =>
        {
            if (s.Sparkline.Count == 6)
            {
                seeded.TrySetResult(s);
            }
        };

        await service.ApplyAsync(SettingsFor(new Asset("crypto", "sh600000", "Pudong", AssetClass.Equity)));
        var timeline = (await seeded.Task.WaitAsync(TimeSpan.FromSeconds(5))).Timeline!;

        timeline.IsGapAt(2).Should().BeFalse("the market was shut overnight");
        timeline.IsGapAt(5).Should().BeTrue("the market was open between 10:00 and 13:30, minus the midday break, with no prices");
    }

    /// <summary>
    /// Verifies the night of a US stock is joined although the hours before and after the regular session are not closed:
    /// the candles of a source cover the regular session only.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_UsNightWithExtendedHours_IsJoined()
    {
        // Tuesday 10:00 in New York, with the end of Monday and the start of Tuesday in the history of the day.
        var eastern = TimeSpan.FromHours(-5);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 6, 10, 0, 0, eastern));
        var factory = new HoursFactory(
            ExchangeHours.Us,
            [
                new PricePoint(new DateTimeOffset(2026, 1, 5, 15, 50, 0, eastern), 10m),
                new PricePoint(new DateTimeOffset(2026, 1, 5, 15, 55, 0, eastern), 11m),
                new PricePoint(new DateTimeOffset(2026, 1, 6, 9, 35, 0, eastern), 12m),
                new PricePoint(new DateTimeOffset(2026, 1, 6, 9, 40, 0, eastern), 13m),
            ]);
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, clock);
        service.SetChartRange(HistoryRange.Day);

        await service.ApplyAsync(SettingsFor(Stock));
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 4);

        service.GetSnapshots().Single().Timeline!.IsGapAt(2).Should().BeFalse("the regular session ended at 16:00 and started at 09:30");
    }

    /// <summary>
    /// Verifies a market shut for longer than the window shows its last session, in a chart that ends with it, and that the
    /// passing time then raises no snapshot for every tick of the timer.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_MarketShutLongerThanTheWindow_ShowsTheLastSession()
    {
        // Sunday noon in Beijing; the last session ended on Friday.
        var beijing = TimeSpan.FromHours(8);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 11, 12, 0, 0, beijing));
        var factory = new HoursFactory(ExchangeHours.ChinaA, FridaySession(beijing));
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, clock);
        service.SetChartRange(HistoryRange.Day);

        await service.ApplyAsync(SettingsFor(Stock));
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 3);

        var snapshot = service.GetSnapshots().Single();
        snapshot.Sparkline.Should().Equal(10m, 11m, 12m);
        snapshot.Timeline!.CoversRange.Should().BeTrue();
        snapshot.Timeline.AsOf.Should().Be(new DateTimeOffset(2026, 1, 9, 14, 55, 0, beijing));
        snapshot.Timeline.HasTrailingGap.Should().BeFalse();

        clock.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(50);
        var raised = 0;
        service.SnapshotChanged += (_, _) => Interlocked.Increment(ref raised);
        clock.Advance(TimeSpan.FromSeconds(30));
        await Task.Delay(50);
        clock.Advance(TimeSpan.FromSeconds(30));
        await Task.Delay(50);

        Volatile.Read(ref raised).Should().Be(0, "nothing changed within the bucket");
    }

    /// <summary>
    /// Verifies a shut market whose window would start inside its last session keeps that session whole, in a chart that ends
    /// with it, instead of losing it bit by bit until the window has passed it.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WindowStartsInsideTheLastSession_ShowsItWhole()
    {
        // Saturday 11:00 in Beijing; 24 hours back the market was open, so the window would cut Friday morning off.
        var beijing = TimeSpan.FromHours(8);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 10, 11, 0, 0, beijing));
        PricePoint[] session = [new(new DateTimeOffset(2026, 1, 9, 9, 35, 0, beijing), 9m), .. FridaySession(beijing)];
        var factory = new HoursFactory(ExchangeHours.ChinaA, session);
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, clock);
        service.SetChartRange(HistoryRange.Day);

        await service.ApplyAsync(SettingsFor(Stock));
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 4);

        var snapshot = service.GetSnapshots().Single();
        snapshot.Sparkline.Should().Equal(9m, 10m, 11m, 12m);
        snapshot.Timeline!.AsOf.Should().Be(new DateTimeOffset(2026, 1, 9, 14, 55, 0, beijing));
    }

    /// <summary>
    /// Verifies the chart of a night keeps moving with the clock: the window starts in the night before, so it cuts nothing.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_NightAfterASession_EndsAtTheClock()
    {
        // Friday 20:00 in Beijing, after the close.
        var beijing = TimeSpan.FromHours(8);
        var now = new DateTimeOffset(2026, 1, 9, 20, 0, 0, beijing);
        var clock = new FakeTimeProvider(now);
        var factory = new HoursFactory(ExchangeHours.ChinaA, FridaySession(beijing));
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, clock);
        service.SetChartRange(HistoryRange.Day);

        await service.ApplyAsync(SettingsFor(Stock));
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 3);

        service.GetSnapshots().Single().Timeline!.AsOf.Should().Be(now);
    }

    /// <summary>
    /// Verifies history older than the window stays out of the chart when nothing says the market is shut (a holiday, or a
    /// source that stopped), and the chart stays with its range.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_OldHistoryAndUnknownHours_ShowsAnEmptyRange()
    {
        var beijing = TimeSpan.FromHours(8);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 11, 12, 0, 0, beijing));
        var loaded = new TaskCompletionSource();
        var factory = new HoursFactory(null, FridaySession(beijing), () => loaded.TrySetResult());
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, clock);
        service.SetChartRange(HistoryRange.Day);

        await service.ApplyAsync(SettingsFor(Stock));
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);

        var snapshot = service.GetSnapshots().Single();
        snapshot.Sparkline.Should().BeEmpty();
        snapshot.Timeline!.CoversRange.Should().BeTrue();
        snapshot.Timeline.Window.Should().Be(TimeSpan.FromDays(1));
        snapshot.Timeline.AsOf.Should().Be(clock.GetUtcNow());
    }

    /// <summary>
    /// Verifies what a timeline found while the source was away, when the hours are not known, is not remembered for the
    /// time the source is back.
    /// </summary>
    [Fact]
    public async Task GetSnapshots_SourceAway_DoesNotRememberTheNightAsAGap()
    {
        var now = new DateTimeOffset(2026, 1, 6, 6, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var beijing = TimeSpan.FromHours(8);
        var factory = new HoursFactory(
            ExchangeHours.ChinaA,
            [
                new PricePoint(new DateTimeOffset(2026, 1, 5, 14, 5, 0, beijing), 9m),
                new PricePoint(new DateTimeOffset(2026, 1, 5, 14, 50, 0, beijing), 10m),
                new PricePoint(new DateTimeOffset(2026, 1, 5, 14, 55, 0, beijing), 11m),
                new PricePoint(new DateTimeOffset(2026, 1, 6, 9, 35, 0, beijing), 12m),
                new PricePoint(new DateTimeOffset(2026, 1, 6, 9, 40, 0, beijing), 13m),
            ]);
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, clock);
        service.SetChartRange(HistoryRange.Day);
        var settings = SettingsFor(Stock);
        await service.ApplyAsync(settings);
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 5);

        // The first price leaves the window while the source is away, which makes new times to look at.
        settings.Providers["crypto"] = new ProviderSettings { Enabled = false };
        await service.ApplyAsync(settings);
        clock.Advance(TimeSpan.FromMinutes(30));
        var away = service.GetSnapshots().Single();
        away.Sparkline.Count.Should().Be(4);
        away.Timeline!.IsGapAt(2).Should().BeTrue("without the source the hours are not known");

        settings.Providers["crypto"] = new ProviderSettings { Enabled = true };
        await service.ApplyAsync(settings);

        service.GetSnapshots().Single().Timeline!.IsGapAt(2).Should().BeFalse();
    }

    /// <summary>
    /// Verifies a source without the range keeps nothing for it, and a quote does not go on filling a series nobody shows.
    /// </summary>
    [Fact]
    public async Task SetChartRange_SourceLacksRange_ShowsItsHourAndAsksForNothing()
    {
        var loads = 0;
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: false, rangeHook: () =>
        {
            Interlocked.Increment(ref loads);
            return Task.CompletedTask;
        });
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 2);

        service.SetChartRange(HistoryRange.Day);
        await Task.Delay(100);

        var snapshot = service.GetSnapshots().Single();
        snapshot.Sparkline.Should().Equal(1m, 2m);
        snapshot.Timeline!.Window.Should().Be(TimeSpan.FromHours(24));
        snapshot.Timeline.CoversRange.Should().BeFalse();
        Volatile.Read(ref loads).Should().Be(0);
    }

    /// <summary>
    /// Verifies the minute bucket keeps the gap of two minutes it always had.
    /// </summary>
    [Fact]
    public void IsGap_MinuteBucket_KeepsTwoMinutes()
    {
        var asOf = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset[] times = [asOf.AddMinutes(-2), asOf.AddMinutes(-1)];

        var timeline = new SparklineTimeline(times, asOf, TimeSpan.FromHours(1), TimeSpan.FromMinutes(1));

        timeline.GapAfter.Should().Be(TimeSpan.FromMinutes(2.5));
    }

    /// <summary>
    /// Verifies a source with the range seeds the chart from the history of the range, shown over the window of it.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_SourceHasRange_SeedsFromRangeHistory()
    {
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true);
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        service.SetChartRange(HistoryRange.Day);
        var seeded = new TaskCompletionSource<AssetSnapshot>();
        service.SnapshotChanged += (_, s) =>
        {
            if (s.Sparkline.Count == 3)
            {
                seeded.TrySetResult(s);
            }
        };

        await service.ApplyAsync(SettingsFor(Btc));
        var snapshot = await seeded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        snapshot.Sparkline.Should().Equal(10m, 20m, 30m);
        snapshot.Timeline!.Window.Should().Be(TimeSpan.FromHours(24));
        snapshot.Timeline.CoversRange.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a source without the range keeps the last hour, which stays where it was traded in the window of the range.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_SourceLacksRange_KeepsHourInWindowOfRange()
    {
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: false);
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        service.SetChartRange(HistoryRange.Year);
        var seeded = new TaskCompletionSource<AssetSnapshot>();
        service.SnapshotChanged += (_, s) =>
        {
            if (s.Sparkline.Count == 2)
            {
                seeded.TrySetResult(s);
            }
        };

        await service.ApplyAsync(SettingsFor(Btc));
        var snapshot = await seeded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        snapshot.Sparkline.Should().Equal(1m, 2m);
        snapshot.Timeline!.Window.Should().Be(TimeSpan.FromDays(365));
        snapshot.Timeline.CoversRange.Should().BeFalse("the last hour is not the start of the year");
        snapshot.Timeline.Times[0].Should().Be(_time.GetUtcNow().AddMinutes(-2));
    }

    /// <summary>
    /// Verifies a source that answers a range with a single price, which draws no chart, is taken to have no history for
    /// it, and the last hour stays.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_RangeAnswerOfOnePrice_KeepsTheLastHour()
    {
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, onlyOneRangePoint: true);
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        service.SetChartRange(HistoryRange.Day);
        var seeded = new TaskCompletionSource();
        service.SnapshotChanged += (_, s) =>
        {
            if (s.Sparkline.Count == 2)
            {
                seeded.TrySetResult();
            }
        };

        await service.ApplyAsync(SettingsFor(Btc));
        await seeded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);

        var snapshot = service.GetSnapshots().Single();
        snapshot.Sparkline.Should().Equal(1m, 2m);
        snapshot.Timeline!.CoversRange.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that once the loads of a range gave up after empty answers, the live prices that follow do not replace the
    /// last hour with a series that holds nothing else.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_RangeLoadsGaveUp_LivePriceKeepsTheLastHour()
    {
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, onlyOneRangePoint: true, quoteOnSubscribe: 5m);
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        service.SetChartRange(HistoryRange.Day);
        var settings = SettingsFor(Btc);

        for (var i = 0; i < 5; i++)
        {
            await service.ApplyAsync(settings);
            await Task.Delay(100);
        }

        var snapshot = service.GetSnapshots().Single();
        snapshot.Sparkline.Count.Should().BeGreaterThan(1);
        snapshot.Timeline!.CoversRange.Should().BeFalse();
    }

    /// <summary>
    /// Verifies changing the range loads its history, and changing back shows the last hour again.
    /// </summary>
    [Fact]
    public async Task SetChartRange_ThereAndBack_SwitchesSeries()
    {
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true);
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        var hourSeeded = new TaskCompletionSource();
        var daySeeded = new TaskCompletionSource();
        service.SnapshotChanged += (_, s) =>
        {
            if (s.Timeline!.Window == TimeSpan.FromHours(1) && s.Sparkline.Count == 2)
            {
                hourSeeded.TrySetResult();
            }

            if (s.Timeline.Window == TimeSpan.FromHours(24) && s.Sparkline.Count == 3)
            {
                daySeeded.TrySetResult();
            }
        };
        await service.ApplyAsync(SettingsFor(Btc));
        await hourSeeded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        service.SetChartRange(HistoryRange.Day);
        await daySeeded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.GetSnapshots().Single().Sparkline.Should().Equal(10m, 20m, 30m);

        service.SetChartRange(HistoryRange.Hour);
        var back = service.GetSnapshots().Single();
        back.Sparkline.Should().Equal(1m, 2m);
        back.Timeline!.Window.Should().Be(TimeSpan.FromHours(1));
    }

    /// <summary>
    /// Verifies the chart stays as it was, with its own window, while the history of the newly chosen range loads, and
    /// changes once instead of falling back to the last hour in between.
    /// </summary>
    [Fact]
    public async Task SetChartRange_HistoryLoading_KeepsTheChartAsItWas()
    {
        var loads = 0;
        var release = new TaskCompletionSource();
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, rangeHook: async () =>
        {
            Interlocked.Increment(ref loads);
            await release.Task;
        });
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 2);

        service.SetChartRange(HistoryRange.Day);
        await WaitUntilAsync(() => Volatile.Read(ref loads) == 1);

        var loading = service.GetSnapshots().Single();
        loading.Sparkline.Should().Equal(1m, 2m);
        loading.Timeline!.Window.Should().Be(TimeSpan.FromHours(1));
        loading.Timeline.Range.Should().BeNull();

        release.SetResult();
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 3);
        var loaded = service.GetSnapshots().Single();
        loaded.Timeline!.Window.Should().Be(TimeSpan.FromHours(24));
        loaded.Timeline.Range.Should().Be(HistoryRange.Day);
    }

    /// <summary>
    /// Verifies a chart of a range, not only the last hour, stays as it was while the next range loads, and that choosing the
    /// range that is on show again, or the last hour, takes effect at once.
    /// </summary>
    [Fact]
    public async Task SetChartRange_FromALoadedRange_KeepsItWhileTheNextLoads()
    {
        var loads = 0;
        var release = new TaskCompletionSource();
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, alsoSupports: HistoryRange.Month, rangeHook: async () =>
        {
            // The first load (the day) answers at once; the second (the month) waits.
            if (Interlocked.Increment(ref loads) > 1)
            {
                await release.Task;
            }
        });
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));
        service.SetChartRange(HistoryRange.Day);
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 3);

        service.SetChartRange(HistoryRange.Month);
        await WaitUntilAsync(() => Volatile.Read(ref loads) == 2);
        var loading = service.GetSnapshots().Single();
        loading.Sparkline.Should().Equal(10m, 20m, 30m);
        loading.Timeline!.Window.Should().Be(TimeSpan.FromHours(24));
        loading.Timeline.Range.Should().Be(HistoryRange.Day, "the chart of the day stays while the month loads");

        service.SetChartRange(HistoryRange.Day);
        var back = service.GetSnapshots().Single();
        back.Timeline!.Window.Should().Be(TimeSpan.FromHours(24));
        back.Timeline.CoversRange.Should().BeTrue();

        service.SetChartRange(HistoryRange.Hour);
        service.GetSnapshots().Single().Timeline!.Window.Should().Be(TimeSpan.FromHours(1));
        release.SetResult();
    }

    /// <summary>
    /// Verifies a chart held for history that no load will bring, because the source was disabled, stops waiting and shows
    /// the last hour in the window of the range.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_SourceLostWhileHolding_ReleasesTheChart()
    {
        var release = new TaskCompletionSource();
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, rangeHook: () => release.Task);
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        var settings = SettingsFor(Btc);
        await service.ApplyAsync(settings);
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 2);
        service.SetChartRange(HistoryRange.Day);
        service.GetSnapshots().Single().Timeline!.Window.Should().Be(TimeSpan.FromHours(1));

        settings.Providers["crypto"] = new ProviderSettings { Enabled = false };
        await service.ApplyAsync(settings);

        var released = service.GetSnapshots().Single();
        released.Timeline!.Window.Should().Be(TimeSpan.FromHours(24));
        released.Timeline.CoversRange.Should().BeFalse();
        release.SetResult();
    }

    /// <summary>
    /// Verifies the chart stays as it was while an empty answer is still asked again, and stops waiting, showing the last
    /// hour in the window of the range, once the empty answers are taken as the answer.
    /// </summary>
    [Fact]
    public async Task SetChartRange_AnswerWithoutHistory_ReleasesTheChartOnlyAfterTheRetries()
    {
        var loads = 0;
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, onlyOneRangePoint: true, rangeHook: () =>
        {
            Interlocked.Increment(ref loads);
            return Task.CompletedTask;
        });
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 2);

        service.SetChartRange(HistoryRange.Day);

        // A retry starts only after the answer before it was dealt with, so each new load tells that the earlier one is over;
        // this needs no pause to wait out the processing of an answer.
        await WaitUntilAsync(() => Volatile.Read(ref loads) == 1);
        _time.Advance(TimeSpan.FromSeconds(31));
        await WaitUntilAsync(() => Volatile.Read(ref loads) == 2);
        service.GetSnapshots().Single().Timeline!.Window.Should().Be(TimeSpan.FromHours(1), "an empty answer is asked again");

        // The third empty answer is the answer, so the chart is released as it is dealt with.
        _time.Advance(TimeSpan.FromSeconds(31));
        await WaitUntilAsync(() => service.GetSnapshots().Single().Timeline!.Window == TimeSpan.FromHours(24));
        service.GetSnapshots().Single().Timeline!.CoversRange.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a chart whose load is still running at the deadline waits one more time, so that the history that arrives
    /// then changes it once, and that the history lands in the chart of the range.
    /// </summary>
    [Fact]
    public async Task SetChartRange_SlowLoadPastTheDeadline_KeepsTheChartUntilTheHistoryArrives()
    {
        var loads = 0;
        var release = new TaskCompletionSource();
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, rangeHook: () =>
        {
            Interlocked.Increment(ref loads);
            return release.Task;
        });
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 2);
        service.SetChartRange(HistoryRange.Day);
        await WaitUntilAsync(() => Volatile.Read(ref loads) == 1);

        // Past the two minutes of the hold, but not past the extra wait of a load that is running.
        _time.Advance(TimeSpan.FromSeconds(150));

        service.GetSnapshots().Single().Timeline!.Window.Should().Be(TimeSpan.FromHours(1), "the load is still running");

        release.SetResult();
        await WaitUntilAsync(() => service.GetSnapshots().Single().Timeline!.CoversRange);
        service.GetSnapshots().Single().Timeline!.Range.Should().Be(HistoryRange.Day);
    }

    /// <summary>
    /// Verifies a chart held for a load that never answers stops waiting after a while, so that it does not stay as it was
    /// for ever.
    /// </summary>
    [Fact]
    public async Task SetChartRange_LoadNeverAnswers_ReleasesTheChartAfterTheDeadline()
    {
        var loads = 0;
        var release = new TaskCompletionSource();
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, rangeHook: () =>
        {
            Interlocked.Increment(ref loads);
            return release.Task;
        });
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 2);

        service.SetChartRange(HistoryRange.Day);

        await WaitUntilAsync(() => Volatile.Read(ref loads) == 1);
        service.GetSnapshots().Single().Timeline!.Window.Should().Be(TimeSpan.FromHours(1), "the load is still waited for");

        await WaitUntilAsync(() =>
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            return service.GetSnapshots().Single().Timeline!.Window == TimeSpan.FromHours(24);
        });
        service.GetSnapshots().Single().Timeline!.CoversRange.Should().BeFalse();
        release.SetResult();
    }

    /// <summary>
    /// Verifies choosing a range again after the deadline of its hanging load has passed starts a new wait, instead of
    /// dropping the chart at once on the old deadline.
    /// </summary>
    [Fact]
    public async Task SetChartRange_ChosenAgainAfterTheDeadline_WaitsAgain()
    {
        var loads = 0;
        var release = new TaskCompletionSource();
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, rangeHook: () =>
        {
            Interlocked.Increment(ref loads);
            return release.Task;
        });
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 2);
        service.SetChartRange(HistoryRange.Day);
        await WaitUntilAsync(() => Volatile.Read(ref loads) == 1);
        await WaitUntilAsync(() =>
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            return service.GetSnapshots().Single().Timeline!.Window == TimeSpan.FromHours(24);
        });

        service.SetChartRange(HistoryRange.Hour);
        service.SetChartRange(HistoryRange.Day);

        service.GetSnapshots().Single().Timeline!.Window.Should().Be(TimeSpan.FromHours(1), "the wait starts over");
        release.SetResult();
    }

    /// <summary>
    /// Verifies a hold ends at its deadline, or one wait later while the load it waits for is still to bring the history,
    /// whether or not that load happens to be running right now.
    /// </summary>
    [Fact]
    public void HoldExpired_PendingLoadOrNot_GivesOnlyAPendingLoadTheExtraWait()
    {
        var until = _time.GetUtcNow();
        var wait = TimeSpan.FromMinutes(2);

        MarketDataService.HoldExpired(until, loadPending: false, until - TimeSpan.FromSeconds(1)).Should().BeFalse();
        MarketDataService.HoldExpired(until, loadPending: false, until).Should().BeTrue();
        MarketDataService.HoldExpired(until, loadPending: true, until).Should().BeFalse("a queued or retrying load is waited for");
        MarketDataService.HoldExpired(until, loadPending: true, until + wait).Should().BeTrue("a hanging load is dropped at the end");
    }

    /// <summary>
    /// Verifies a timeline cannot name a range with a window that is not the one of that range.
    /// </summary>
    [Fact]
    public void Constructor_RangeWithAnotherWindow_Throws()
    {
        var asOf = _time.GetUtcNow();

        var act = () => new SparklineTimeline([asOf], asOf, TimeSpan.FromHours(1), range: HistoryRange.Day);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Verifies a timeline of the last hour does not count as covering a range.
    /// </summary>
    [Fact]
    public void CoversRange_HourRange_IsFalse()
    {
        var asOf = _time.GetUtcNow();

        new SparklineTimeline([asOf], asOf, TimeSpan.FromHours(1), range: HistoryRange.Hour).CoversRange.Should().BeFalse();
        new SparklineTimeline([asOf], asOf, TimeSpan.FromHours(24), range: HistoryRange.Day).CoversRange.Should().BeTrue();
        new SparklineTimeline([asOf], asOf, TimeSpan.FromHours(1)).CoversRange.Should().BeFalse();
    }

    /// <summary>
    /// Verifies going through the last hour and back to a range whose load hangs does not give the held chart a new
    /// deadline, so that toggling the range cannot keep the chart as it was for ever.
    /// </summary>
    [Fact]
    public async Task SetChartRange_ChosenAgainWhileHolding_KeepsTheLoadsDeadline()
    {
        var loads = 0;
        var release = new TaskCompletionSource();
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, rangeHook: () =>
        {
            Interlocked.Increment(ref loads);
            return release.Task;
        });
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 2);

        service.SetChartRange(HistoryRange.Day);
        await WaitUntilAsync(() => Volatile.Read(ref loads) == 1);
        _time.Advance(TimeSpan.FromSeconds(90));
        service.SetChartRange(HistoryRange.Hour);
        service.SetChartRange(HistoryRange.Day);

        // The load is running, so the chart waits two minutes and then two more, counted from the first choice: 150 s from now.
        // A fresh deadline from the second choice would be 240 s away. The chart must be released well before that.
        var elapsed = TimeSpan.Zero;
        await WaitUntilAsync(() =>
        {
            var step = TimeSpan.FromSeconds(10);
            _time.Advance(step);
            elapsed += step;
            return service.GetSnapshots().Single().Timeline!.Window == HistoryRangeSpec.For(HistoryRange.Day).Window;
        });
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(200));
        release.SetResult();
    }

    /// <summary>
    /// Verifies a load that was only just started for another range is not cut short by the deadline of the load of the
    /// range chosen before it: it gets its own wait.
    /// </summary>
    [Fact]
    public async Task SetChartRange_OtherRangeChosenWhileHolding_GetsItsOwnDeadline()
    {
        var loads = 0;
        var release = new TaskCompletionSource();
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, alsoSupports: HistoryRange.Month, rangeHook: () =>
        {
            Interlocked.Increment(ref loads);
            return release.Task;
        });
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 2);

        service.SetChartRange(HistoryRange.Day);
        await WaitUntilAsync(() => Volatile.Read(ref loads) == 1);
        _time.Advance(TimeSpan.FromSeconds(90));
        service.SetChartRange(HistoryRange.Month);
        await WaitUntilAsync(() => Volatile.Read(ref loads) == 2);

        // The deadline of the day is 30 s away; the month was chosen just now and still has most of its two minutes.
        for (var i = 0; i < 6; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(10));
            await Task.Delay(20);
        }

        service.GetSnapshots().Single().Timeline!.Window.Should().Be(TimeSpan.FromHours(1), "the month is still waited for");

        var elapsed = TimeSpan.FromSeconds(60);
        await WaitUntilAsync(() =>
        {
            var step = TimeSpan.FromSeconds(10);
            _time.Advance(step);
            elapsed += step;
            return service.GetSnapshots().Single().Timeline!.Window == HistoryRangeSpec.For(HistoryRange.Month).Window;
        });
        elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(100), "the wait of the month began when it was chosen");
        release.SetResult();
    }

    /// <summary>
    /// Verifies the hold of a load that gave up after empty answers ends when it does, not only at the deadline of the hold.
    /// </summary>
    [Fact]
    public async Task SetChartRange_AnswersWithoutHistory_ReleaseTheChartBeforeTheDeadline()
    {
        var loads = 0;
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, onlyOneRangePoint: true, rangeHook: () =>
        {
            Interlocked.Increment(ref loads);
            return Task.CompletedTask;
        });
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 2);

        service.SetChartRange(HistoryRange.Day);
        await WaitUntilAsync(() => Volatile.Read(ref loads) == 1);

        var elapsed = TimeSpan.Zero;
        await WaitUntilAsync(() =>
        {
            var step = TimeSpan.FromSeconds(10);
            _time.Advance(step);
            elapsed += step;
            return service.GetSnapshots().Single().Timeline!.Window == TimeSpan.FromHours(24);
        });
        elapsed.Should().BeLessThan(TimeSpan.FromMinutes(2), "the third empty answer is the answer");
        service.GetSnapshots().Single().Timeline!.CoversRange.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a timeline that names a range always has the window of that range, held or loaded, as the rows trust it.
    /// </summary>
    [Fact]
    public async Task GetSnapshots_TimelineNamingARange_HasTheWindowOfThatRange()
    {
        var loads = 0;
        var release = new TaskCompletionSource();
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, alsoSupports: HistoryRange.Month, rangeHook: async () =>
        {
            if (Interlocked.Increment(ref loads) > 1)
            {
                await release.Task;
            }
        });
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));
        service.SetChartRange(HistoryRange.Day);
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 3);

        service.SetChartRange(HistoryRange.Month);
        await WaitUntilAsync(() => Volatile.Read(ref loads) == 2);

        var held = service.GetSnapshots().Single().Timeline!;
        held.Range.Should().Be(HistoryRange.Day);
        held.Window.Should().Be(HistoryRangeSpec.For(HistoryRange.Day).Window);
        release.SetResult();
    }

    /// <summary>
    /// Verifies the deadline the load was given is kept while it is still ahead, and one that has passed starts a new wait.
    /// </summary>
    [Fact]
    public void HoldDeadline_AheadOrExpired_KeepsOnlyADeadlineAhead()
    {
        var now = _time.GetUtcNow();
        var ahead = now.AddSeconds(30);
        var passed = now.AddSeconds(-5);

        MarketDataService.HoldDeadline(ahead, now).Should().Be(ahead);
        MarketDataService.HoldDeadline(passed, now).Should().Be(now + TimeSpan.FromMinutes(2));
        MarketDataService.HoldDeadline(default, now).Should().Be(now + TimeSpan.FromMinutes(2));
    }

    /// <summary>
    /// Verifies the chart stays as it was while a failed load is still asked again, and stops waiting once the retries are
    /// used up.
    /// </summary>
    [Fact]
    public async Task SetChartRange_LoadFails_ReleasesTheChartOnlyAfterTheRetries()
    {
        var loads = 0;
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, rangeHook: () =>
        {
            Interlocked.Increment(ref loads);
            return Task.FromException(new HttpRequestException("Too many requests"));
        });
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 2);

        service.SetChartRange(HistoryRange.Day);

        await WaitUntilAsync(() => Volatile.Read(ref loads) == 1);
        service.GetSnapshots().Single().Timeline!.Window.Should().Be(TimeSpan.FromHours(1), "the first failure is asked again");

        await WaitUntilAsync(() =>
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            return service.GetSnapshots().Single().Timeline!.Window == TimeSpan.FromHours(24);
        });
        service.GetSnapshots().Single().Timeline!.CoversRange.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the held chart keeps receiving live prices when the load of its range was replaced meanwhile, so that its
    /// series is no longer among the kept ones.
    /// </summary>
    [Fact]
    public async Task SetChartRange_HeldSeriesReplacedWhileLoading_StillReceivesLivePrices()
    {
        var loads = 0;
        var release = new TaskCompletionSource();
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, alsoSupports: HistoryRange.Month, rangeHook: async () =>
        {
            // The first load (the day) answers at once; the others wait.
            if (Interlocked.Increment(ref loads) > 1)
            {
                await release.Task;
            }
        });
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));
        service.SetChartRange(HistoryRange.Day);
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 3);

        // The day is kept for a while only; past that, choosing it again starts a new load and drops the old series.
        _time.Advance(TimeSpan.FromMinutes(11));
        service.SetChartRange(HistoryRange.Month);
        await WaitUntilAsync(() => Volatile.Read(ref loads) == 2);
        service.SetChartRange(HistoryRange.Day);
        await WaitUntilAsync(() => Volatile.Read(ref loads) == 3);

        factory.Provider!.Emit(new Quote(Btc.Id, 99m, null, _time.GetUtcNow()));

        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline[^1] == 99m);
        service.GetSnapshots().Single().Timeline!.Window.Should().Be(TimeSpan.FromHours(24));
        release.SetResult();
    }

    /// <summary>
    /// Verifies choosing a range again soon shows the history loaded before and does not ask for it anew.
    /// </summary>
    [Fact]
    public async Task SetChartRange_BackToALoadedRange_ReusesItsHistory()
    {
        var loads = 0;
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, rangeHook: () =>
        {
            Interlocked.Increment(ref loads);
            return Task.CompletedTask;
        });
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));

        service.SetChartRange(HistoryRange.Day);
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 3);
        service.SetChartRange(HistoryRange.Hour);
        service.SetChartRange(HistoryRange.Day);

        service.GetSnapshots().Single().Sparkline.Should().Equal(10m, 20m, 30m);
        await Task.Delay(100);
        loads.Should().Be(1);
    }

    /// <summary>
    /// Verifies choosing a range again while its history is still loading uses that load and does not start another.
    /// </summary>
    [Fact]
    public async Task SetChartRange_BackWhileLoading_DoesNotLoadTwice()
    {
        var loads = 0;
        var release = new TaskCompletionSource();
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, rangeHook: async () =>
        {
            Interlocked.Increment(ref loads);
            await release.Task;
        });
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));

        service.SetChartRange(HistoryRange.Day);
        await WaitUntilAsync(() => Volatile.Read(ref loads) == 1);
        service.SetChartRange(HistoryRange.Hour);
        service.SetChartRange(HistoryRange.Day);
        release.SetResult();

        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 3);
        await Task.Delay(100);
        loads.Should().Be(1);
    }

    /// <summary>
    /// Verifies disposing the service cancels a load of range history and drops what it answers afterwards.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_RangeLoadInFlight_CancelsItAndDropsTheAnswer()
    {
        var loadToken = new TaskCompletionSource<CancellationToken>();
        var release = new TaskCompletionSource();
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, rangeHook: () => release.Task, rangeToken: t => loadToken.TrySetResult(t));
        var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));

        // The history of the last hour arrives in the background and raises a snapshot of its own; on a slow machine that
        // would land after the handler below is attached and be counted as the answer of the range load.
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count > 0);
        service.SetChartRange(HistoryRange.Day);
        var token = await loadToken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var raised = 0;
        service.SnapshotChanged += (_, _) => Interlocked.Increment(ref raised);

        await service.DisposeAsync();
        release.SetResult();
        await Task.Delay(100);

        token.IsCancellationRequested.Should().BeTrue();
        Volatile.Read(ref raised).Should().Be(0);
    }

    /// <summary>
    /// Verifies history that is too old is loaded again when the range is chosen once more.
    /// </summary>
    [Fact]
    public async Task SetChartRange_BackAfterLongTime_LoadsAgain()
    {
        var loads = 0;
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, rangeHook: () =>
        {
            Interlocked.Increment(ref loads);
            return Task.CompletedTask;
        });
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));

        service.SetChartRange(HistoryRange.Day);
        await WaitUntilAsync(() => Volatile.Read(ref loads) == 1 && service.GetSnapshots().Single().Sparkline.Count == 3);
        service.SetChartRange(HistoryRange.Hour);
        _time.Advance(TimeSpan.FromHours(1));
        service.SetChartRange(HistoryRange.Day);

        await WaitUntilAsync(() => Volatile.Read(ref loads) == 2);
    }

    /// <summary>
    /// Verifies an empty answer is asked again by the health timer, a few times, and then taken as the answer.
    /// </summary>
    [Fact]
    public async Task SetChartRange_EmptyAnswer_IsAskedAgainAFewTimes()
    {
        var loads = 0;
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, onlyOneRangePoint: true, rangeHook: () =>
        {
            Interlocked.Increment(ref loads);
            return Task.CompletedTask;
        });
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));

        service.SetChartRange(HistoryRange.Day);
        await WaitUntilAsync(() => Volatile.Read(ref loads) == 1);
        for (var i = 0; i < 10; i++)
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(50);
        }

        await WaitUntilAsync(() => Volatile.Read(ref loads) == 3);
        await Task.Delay(100);
        loads.Should().Be(3, "three empty answers in a row are taken as the answer");
    }

    /// <summary>
    /// Verifies a source that failed too often in a row is left alone, also when an empty answer came before the failures.
    /// </summary>
    [Fact]
    public async Task SetChartRange_FailuresAfterAnEmptyAnswer_StopAfterTheRetries()
    {
        var loads = 0;
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, onlyOneRangePoint: true, rangeHook: () =>
            Interlocked.Increment(ref loads) == 1 ? Task.CompletedTask : Task.FromException(new HttpRequestException("Too many requests")));
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));

        service.SetChartRange(HistoryRange.Day);
        await WaitUntilAsync(() => Volatile.Read(ref loads) == 1);
        for (var i = 0; i < 12; i++)
        {
            _time.Advance(TimeSpan.FromMinutes(11));
            await Task.Delay(50);
        }

        Volatile.Read(ref loads).Should().Be(6, "the empty answer and then five failures, after which the timer stops asking");
    }

    /// <summary>
    /// Verifies loads that gave up on a provider ask the provider that replaces it.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_OptionsChangedAfterLoadsGaveUp_LoadsAgain()
    {
        var loads = 0;
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, onlyOneRangePoint: true, rangeHook: () =>
        {
            Interlocked.Increment(ref loads);
            return Task.CompletedTask;
        });
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        var settings = SettingsFor(Btc);
        await service.ApplyAsync(settings);
        service.SetChartRange(HistoryRange.Day);
        await WaitUntilAsync(() => Volatile.Read(ref loads) == 1);
        for (var i = 0; i < 10; i++)
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(50);
        }

        await WaitUntilAsync(() => Volatile.Read(ref loads) == 3);

        settings.Providers["crypto"] = new ProviderSettings { Options = { ["Endpoint"] = "other" } };
        await service.ApplyAsync(settings);

        await WaitUntilAsync(() => Volatile.Read(ref loads) == 4);
    }

    /// <summary>
    /// Verifies a time without quotes that lost a point of the chart loads the range again, and a short one does not.
    /// </summary>
    [Fact]
    public async Task HandleQuote_AfterLongSilence_LoadsTheRangeAgain()
    {
        var loads = 0;
        var factory = new RangeFactory(_time.GetUtcNow(), supportsDay: true, rangeHook: () =>
        {
            Interlocked.Increment(ref loads);
            return Task.CompletedTask;
        });
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        var shown = new TaskCompletionSource<decimal>();
        var seeded = new TaskCompletionSource();
        service.SnapshotChanged += (_, s) =>
        {
            if (s.Quote is { } quote)
            {
                shown.TrySetResult(quote.Price);
            }

            if (s.Timeline is { CoversRange: true })
            {
                seeded.TrySetResult();
            }
        };
        service.SetChartRange(HistoryRange.Day);
        await service.ApplyAsync(SettingsFor(Btc));
        await seeded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        async Task EmitAsync(decimal price)
        {
            shown = new TaskCompletionSource<decimal>();
            factory.Provider!.Emit(new Quote(Btc.Id, price, null, _time.GetUtcNow()));
            await shown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        await EmitAsync(41m);
        _time.Advance(TimeSpan.FromMinutes(1));
        await EmitAsync(42m);
        _time.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(100);
        Volatile.Read(ref loads).Should().Be(1, "a minute without quotes loses nothing");

        _time.Advance(TimeSpan.FromHours(2));
        await EmitAsync(43m);
        _time.Advance(TimeSpan.FromSeconds(5));

        await WaitUntilAsync(() => Volatile.Read(ref loads) == 2);
    }

    /// <summary>
    /// Verifies a change of the range does not load the history of every instrument at once.
    /// </summary>
    [Fact]
    public async Task SetChartRange_ManyInstruments_LimitsConcurrentLoads()
    {
        var release = new TaskCompletionSource();
        var active = 0;
        var peak = 0;
        var started = 0;
        var factory = new RangeFactory(
            _time.GetUtcNow(),
            supportsDay: true,
            rangeHook: async () =>
            {
                var now = Interlocked.Increment(ref active);
                Interlocked.Increment(ref started);
                int seen;
                while (now > (seen = Volatile.Read(ref peak)) && Interlocked.CompareExchange(ref peak, now, seen) != seen)
                {
                }

                await release.Task;
                Interlocked.Decrement(ref active);
            });
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor([.. Enumerable.Range(0, 10).Select(i => new Asset("crypto", $"A{i}", $"Asset {i}", AssetClass.Crypto))]));

        service.SetChartRange(HistoryRange.Day);
        await WaitUntilAsync(() => Volatile.Read(ref started) >= 4);
        await Task.Delay(200);

        Volatile.Read(ref started).Should().Be(4, "the other loads wait for a turn");
        release.SetResult();
        await WaitUntilAsync(() => Volatile.Read(ref started) == 10);
        peak.Should().Be(4);
    }

    /// <summary>
    /// Verifies a slow source does not hold up the loads of another one.
    /// </summary>
    [Fact]
    public async Task SetChartRange_SlowSource_DoesNotHoldUpAnother()
    {
        var release = new TaskCompletionSource();
        var fastLoads = 0;
        var slow = new RangeFactory(_time.GetUtcNow(), supportsDay: true, rangeHook: () => release.Task);
        var fast = new RangeFactory(
            _time.GetUtcNow(),
            supportsDay: true,
            rangeHook: () =>
            {
                Interlocked.Increment(ref fastLoads);
                return Task.CompletedTask;
            },
            id: "other");
        await using var service = new MarketDataService(new ProviderRegistry([slow, fast]), NullLoggerFactory.Instance, _time);
        Asset[] assets =
        [
            .. Enumerable.Range(0, 6).Select(i => new Asset("crypto", $"A{i}", $"Asset {i}", AssetClass.Crypto)),
            new("other", "B", "Other", AssetClass.Crypto),
        ];
        await service.ApplyAsync(SettingsFor(assets));

        service.SetChartRange(HistoryRange.Day);

        await WaitUntilAsync(() => Volatile.Read(ref fastLoads) == 1);
        release.SetResult();
    }

    /// <summary>
    /// Waits until a condition holds.
    /// </summary>
    /// <param name="condition">
    /// The condition.
    /// </param>
    /// <returns>
    /// A task that completes when it holds, or fails after a few seconds.
    /// </returns>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("The condition did not hold in time.");
            }

            await Task.Delay(20);
        }
    }

    /// <summary>
    /// Creates the end of the session of Friday, January 9th.
    /// </summary>
    /// <param name="offset">
    /// The offset of the market.
    /// </param>
    /// <returns>
    /// The prices.
    /// </returns>
    private static PricePoint[] FridaySession(TimeSpan offset) =>
    [
        new(new DateTimeOffset(2026, 1, 9, 14, 40, 0, offset), 10m),
        new(new DateTimeOffset(2026, 1, 9, 14, 45, 0, offset), 11m),
        new(new DateTimeOffset(2026, 1, 9, 14, 50, 0, offset), 12m),
    ];

    /// <summary>
    /// Creates settings that track instruments.
    /// </summary>
    /// <param name="assets">
    /// The assets to track.
    /// </param>
    /// <returns>
    /// The settings.
    /// </returns>
    private static AppSettings SettingsFor(params Asset[] assets)
    {
        var settings = new AppSettings();
        settings.Watchlist.AddRange(assets.Select(WatchlistEntry.From));
        return settings;
    }

    /// <summary>
    /// A factory whose provider knows the hours of an exchange and has the history of the last day.
    /// </summary>
    /// <param name="hours">
    /// The hours of the exchange, or <see langword="null"/> when they are not known.
    /// </param>
    /// <param name="history">
    /// The history of the last day.
    /// </param>
    /// <param name="onRangeLoad">
    /// What runs when the history of the day is asked for.
    /// </param>
    private sealed class HoursFactory(ExchangeHours? hours, IReadOnlyList<PricePoint> history, Action? onRangeLoad = null) : IMarketDataProviderFactory
    {
        /// <inheritdoc />
        public ProviderDescriptor Descriptor { get; } = new("crypto", "Crypto", "Test", [AssetClass.Equity], ProviderUpdateMode.Streaming);

        /// <inheritdoc />
        public IMarketDataProvider Create(ProviderCreationContext context)
        {
            var provider = new FakeProvider(Descriptor, context.Options) { RangeHistory = history, Hours = hours };
            provider.RangeHistoryHook = () =>
            {
                onRangeLoad?.Invoke();
                return Task.CompletedTask;
            };
            provider.SupportedRanges.Add(HistoryRange.Day);
            return provider;
        }
    }

    /// <summary>
    /// A factory whose provider has the history of the last hour and, if asked to, of the last day.
    /// </summary>
    /// <param name="now">
    /// The current time used to build the history.
    /// </param>
    /// <param name="supportsDay">
    /// Whether the provider has the history of the last day.
    /// </param>
    /// <param name="onlyOneRangePoint">
    /// Whether the history of a range holds a single point.
    /// </param>
    /// <param name="quoteOnSubscribe">
    /// The price to publish when an instrument is subscribed.
    /// </param>
    /// <param name="rangeHook">
    /// What runs when the history of a range is asked for.
    /// </param>
    /// <param name="rangeToken">
    /// What is told the cancellation token of each load of a range.
    /// </param>
    /// <param name="id">
    /// The id of the provider.
    /// </param>
    private sealed class RangeFactory(DateTimeOffset now, bool supportsDay, bool onlyOneRangePoint = false, decimal? quoteOnSubscribe = null, Func<Task>? rangeHook = null, Action<CancellationToken>? rangeToken = null, string id = "crypto", HistoryRange? alsoSupports = null) : IMarketDataProviderFactory
    {
        /// <inheritdoc />
        public ProviderDescriptor Descriptor { get; } = new(
            id,
            "Crypto",
            "Test",
            [AssetClass.Crypto],
            ProviderUpdateMode.Streaming,
            Options: [new ProviderOptionDefinition("Endpoint", "Endpoint", ProviderOptionType.String, "default")]);

        /// <summary>
        /// Gets the provider created last.
        /// </summary>
        public FakeProvider? Provider { get; private set; }

        /// <inheritdoc />
        public IMarketDataProvider Create(ProviderCreationContext context)
        {
            var provider = new FakeProvider(Descriptor, context.Options)
            {
                History = [new PricePoint(now.AddMinutes(-2), 1m), new PricePoint(now.AddMinutes(-1), 2m)],
                QuoteOnSubscribe = quoteOnSubscribe,
                RangeHistoryHook = rangeHook,
                RangeHistoryTokenHook = rangeToken,
                RangeHistory = onlyOneRangePoint
                    ? [new PricePoint(now.AddHours(-1), 30m)]
                    :
                    [
                        new PricePoint(now.AddHours(-3), 10m),
                        new PricePoint(now.AddHours(-2), 20m),
                        new PricePoint(now.AddHours(-1), 30m),
                    ],
            };
            if (supportsDay)
            {
                provider.SupportedRanges.Add(HistoryRange.Day);
            }

            if (alsoSupports is { } more)
            {
                provider.SupportedRanges.Add(more);
            }

            Provider = provider;
            return provider;
        }
    }
}
