using FluxTick.Abstractions;
using FluxTick.Providers.Eulerpool;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the previous close cache of the Eulerpool provider.
/// </summary>
public class EulerpoolReferenceCacheTests
{
    /// <summary>
    /// A weekday at noon UTC.
    /// </summary>
    private static readonly DateTimeOffset Start = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies a call after disposal starts no fetch and does not throw.
    /// </summary>
    [Fact]
    public async Task Get_AfterDispose_FetchesNothing()
    {
        var fetches = 0;
        var cache = new EulerpoolReferenceCache(
            (_, _) =>
            {
                Interlocked.Increment(ref fetches);
                return Task.FromResult<IReadOnlyList<PricePoint>?>([new PricePoint(Start.AddDays(-1), 1m)]);
            },
            new FakeTimeProvider(Start),
            NullLogger.Instance);
        cache.Dispose();

        cache.Get("AAPL", 1m).Should().BeNull();
        await cache.EnsureAsync(["AAPL"], CancellationToken.None);

        fetches.Should().Be(0);
    }

    /// <summary>
    /// Verifies a ticker without an earlier candle is not asked for again before six hours have passed.
    /// </summary>
    [Fact]
    public async Task EnsureAsync_NoEarlierCandle_RetriesAfterSixHours()
    {
        var fetches = 0;
        var time = new FakeTimeProvider(Start);
        using var cache = new EulerpoolReferenceCache(
            (_, _) =>
            {
                Interlocked.Increment(ref fetches);
                return Task.FromResult<IReadOnlyList<PricePoint>?>(null);
            },
            time,
            NullLogger.Instance);

        await cache.EnsureAsync(["AAPL"], CancellationToken.None);
        time.Advance(TimeSpan.FromHours(5));
        await cache.EnsureAsync(["AAPL"], CancellationToken.None);
        fetches.Should().Be(1);

        time.Advance(TimeSpan.FromHours(2));
        await cache.EnsureAsync(["AAPL"], CancellationToken.None);
        fetches.Should().Be(2);
    }

    /// <summary>
    /// Verifies a failed fetch is not retried within 30 minutes and is retried after them.
    /// </summary>
    [Fact]
    public async Task EnsureAsync_FetchFailed_RetriesAfterThirtyMinutes()
    {
        var fetches = 0;
        var time = new FakeTimeProvider(Start);
        using var cache = new EulerpoolReferenceCache(
            (_, _) =>
            {
                Interlocked.Increment(ref fetches);
                throw new HttpRequestException("down");
            },
            time,
            NullLogger.Instance);

        await cache.EnsureAsync(["AAPL"], CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(29));
        await cache.EnsureAsync(["AAPL"], CancellationToken.None);
        fetches.Should().Be(1);

        time.Advance(TimeSpan.FromMinutes(2));
        await cache.EnsureAsync(["AAPL"], CancellationToken.None);
        fetches.Should().Be(2);
    }

    /// <summary>
    /// Verifies the previous close follows the day the price was traded: a price that is still the last close is measured
    /// against the close before it, one traded on a later day against that close.
    /// </summary>
    [Fact]
    public async Task Get_ByTradeTime_PicksTheCloseBeforeTheTradingDay()
    {
        var friday = new DateTimeOffset(2026, 10, 23, 20, 0, 0, TimeSpan.Zero);
        var saturday = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        using var cache = new EulerpoolReferenceCache(
            (_, _) => Task.FromResult<IReadOnlyList<PricePoint>?>(
            [
                new PricePoint(new DateTimeOffset(2026, 10, 22, 20, 0, 0, TimeSpan.Zero), 348.22m),
                new PricePoint(friday, 347.75m),
            ]),
            new FakeTimeProvider(saturday),
            NullLogger.Instance);

        await cache.EnsureAsync(["AAPL"], CancellationToken.None);

        cache.Get("AAPL", 347.75m, friday).Should().Be(348.22m);
        cache.Get("AAPL", 347.75m, saturday).Should().Be(347.75m);
    }

    /// <summary>
    /// Verifies a call without a trade time measures against the current time.
    /// </summary>
    [Fact]
    public async Task Get_WithoutTradeTime_UsesTheCurrentTime()
    {
        var monday = new DateTimeOffset(2026, 10, 26, 20, 0, 0, TimeSpan.Zero);
        var tuesday = new DateTimeOffset(2026, 10, 27, 20, 0, 0, TimeSpan.Zero);
        using var cache = new EulerpoolReferenceCache(
            (_, _) => Task.FromResult<IReadOnlyList<PricePoint>?>([new PricePoint(monday, 342.75m)]),
            new FakeTimeProvider(tuesday),
            NullLogger.Instance);

        await cache.EnsureAsync(["AAPL"], CancellationToken.None);

        cache.Get("AAPL", 342.75m).Should().Be(342.75m);
        cache.Get("AAPL", 342.75m, monday).Should().BeNull("no close lies before the day of the only candle");
    }

    /// <summary>
    /// Verifies closes fetched while the session was on, with a partial candle of today, are fetched again once it has
    /// ended, and not again after that.
    /// </summary>
    [Fact]
    public async Task EnsureAsync_FetchedDuringTheSession_FetchesOnceMoreAfterTheClose()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero)); // 11:00 New York.
        var fetches = 0;
        using var cache = new EulerpoolReferenceCache(
            (_, _) =>
            {
                Interlocked.Increment(ref fetches);
                return Task.FromResult<IReadOnlyList<PricePoint>?>([new PricePoint(time.GetUtcNow().AddDays(-1), 1m)]);
            },
            time,
            NullLogger.Instance);

        await cache.EnsureAsync(["AAPL"], CancellationToken.None);
        await cache.EnsureAsync(["AAPL"], CancellationToken.None);
        fetches.Should().Be(1);

        time.Advance(TimeSpan.FromHours(5.5)); // 16:30 New York.
        await cache.EnsureAsync(["AAPL"], CancellationToken.None);
        await cache.EnsureAsync(["AAPL"], CancellationToken.None);
        fetches.Should().Be(2);
    }

    /// <summary>
    /// Verifies an after-hours price has no previous close while the candle of its day is the partial one fetched during
    /// the session, and gets the final close once the day has been fetched again.
    /// </summary>
    [Fact]
    public async Task Get_AfterHoursPriceWithPartialCandle_WaitsForTheFinalClose()
    {
        var monday = new DateTimeOffset(2026, 10, 26, 20, 0, 0, TimeSpan.Zero);
        var tuesdayAfterClose = new DateTimeOffset(2026, 10, 27, 20, 30, 0, TimeSpan.Zero); // 16:30 New York.
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero)); // 11:00 New York.
        var todaysClose = 346m;
        using var cache = new EulerpoolReferenceCache(
            (_, _) => Task.FromResult<IReadOnlyList<PricePoint>?>(
            [
                new PricePoint(monday, 342.75m),
                new PricePoint(time.GetUtcNow(), todaysClose),
            ]),
            time,
            NullLogger.Instance);
        await cache.EnsureAsync(["AAPL"], CancellationToken.None);

        cache.Get("AAPL", 348.5m, tuesdayAfterClose).Should().BeNull();

        time.SetUtcNow(tuesdayAfterClose);
        todaysClose = 347.1m;
        await cache.EnsureAsync(["AAPL"], CancellationToken.None);

        cache.Get("AAPL", 348.5m, tuesdayAfterClose).Should().Be(347.1m);
    }

    /// <summary>
    /// Verifies a fetch made within the settle delay after the close is not taken as final, and is made again after it.
    /// </summary>
    [Fact]
    public async Task EnsureAsync_FetchedJustAfterTheClose_FetchesAgainOnceTheCandleSettles()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 20, 5, 0, TimeSpan.Zero)); // 16:05 New York.
        var fetches = 0;
        using var cache = new EulerpoolReferenceCache(
            (_, _) =>
            {
                Interlocked.Increment(ref fetches);
                return Task.FromResult<IReadOnlyList<PricePoint>?>([new PricePoint(time.GetUtcNow().AddDays(-1), 1m)]);
            },
            time,
            NullLogger.Instance);

        await cache.EnsureAsync(["AAPL"], CancellationToken.None);
        await cache.EnsureAsync(["AAPL"], CancellationToken.None);
        fetches.Should().Be(1);

        time.Advance(TimeSpan.FromMinutes(15));
        await cache.EnsureAsync(["AAPL"], CancellationToken.None);
        await cache.EnsureAsync(["AAPL"], CancellationToken.None);
        fetches.Should().Be(2);
    }

    /// <summary>
    /// Verifies an after-hours price of a day fetched over a weekend is measured against that day's final close.
    /// </summary>
    [Fact]
    public async Task Get_FetchedOverTheWeekend_UsesTheFridayClose()
    {
        var thursday = new DateTimeOffset(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);
        var friday = new DateTimeOffset(2026, 10, 23, 20, 0, 0, TimeSpan.Zero);
        using var cache = new EulerpoolReferenceCache(
            (_, _) => Task.FromResult<IReadOnlyList<PricePoint>?>([new PricePoint(thursday, 348.22m), new PricePoint(friday, 347.75m)]),
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero)),
            NullLogger.Instance);
        await cache.EnsureAsync(["AAPL"], CancellationToken.None);

        cache.Get("AAPL", 348.5m, new DateTimeOffset(2026, 10, 23, 22, 0, 0, TimeSpan.Zero)).Should().Be(347.75m);
    }
}
