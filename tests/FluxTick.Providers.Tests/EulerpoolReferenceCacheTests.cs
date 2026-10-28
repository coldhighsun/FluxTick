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
                return Task.FromResult<decimal?>(1m);
            },
            new FakeTimeProvider(Start),
            NullLogger.Instance);
        cache.Dispose();

        cache.Get("AAPL").Should().BeNull();
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
                return Task.FromResult<decimal?>(null);
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
}
