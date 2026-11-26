using FluxTick.Abstractions;
using FluxTick.Core.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.Core.Tests.MarketData;

/// <summary>
/// Tests for <see cref="FileQuoteHistoryStore"/>.
/// </summary>
public sealed class FileQuoteHistoryStoreTests : IDisposable
{
    /// <summary>
    /// The instrument the tests write.
    /// </summary>
    private static readonly AssetId Btc = new("binance", "BTCUSDT");

    /// <summary>
    /// The folder of this test run.
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"fluxtick-history-{Guid.NewGuid():N}");

    /// <summary>
    /// The clock of the store.
    /// </summary>
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// Checks that a price written and flushed comes back.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AfterFlush_ReturnsRecordedPrices()
    {
        await using var store = Create();
        var t = _time.GetUtcNow().AddMinutes(-5);
        store.Record(Btc, 100.5m, t);
        store.Record(Btc, 101.25m, t.AddSeconds(20));
        await store.FlushAsync(includeOpen: true);

        var points = await store.ReadAsync(Btc, t.AddMinutes(-1), _time.GetUtcNow());

        points.Select(p => p.Price).Should().Equal(100.5m, 101.25m);
        points[0].Timestamp.Should().Be(t);
    }

    /// <summary>
    /// Checks that a price whose bucket is still open is part of a read.
    /// </summary>
    [Fact]
    public async Task ReadAsync_OpenBucket_IsIncluded()
    {
        await using var store = Create();
        var t = _time.GetUtcNow().AddSeconds(-3);
        store.Record(Btc, 7m, t);

        var points = await store.ReadAsync(Btc, t.AddMinutes(-1), _time.GetUtcNow());

        points.Select(p => p.Price).Should().Equal(7m);
    }

    /// <summary>
    /// Checks that prices in the same 10-second bucket are stored as the last one.
    /// </summary>
    [Fact]
    public async Task Record_SameBucket_KeepsLastPrice()
    {
        await using var store = Create();
        var bucketStart = new DateTimeOffset(2026, 6, 15, 11, 50, 0, TimeSpan.Zero);
        store.Record(Btc, 1m, bucketStart.AddSeconds(1));
        store.Record(Btc, 2m, bucketStart.AddSeconds(8));
        store.Record(Btc, 3m, bucketStart.AddSeconds(12));
        await store.FlushAsync(includeOpen: true);

        var points = await store.ReadAsync(Btc, bucketStart, _time.GetUtcNow());

        points.Select(p => p.Price).Should().Equal(2m, 3m);
    }

    /// <summary>
    /// Checks that the read is limited to the asked time range.
    /// </summary>
    [Fact]
    public async Task ReadAsync_TimeRange_FiltersPrices()
    {
        await using var store = Create();
        var now = _time.GetUtcNow();
        store.Record(Btc, 1m, now.AddMinutes(-30));
        store.Record(Btc, 2m, now.AddMinutes(-20));
        store.Record(Btc, 3m, now.AddMinutes(-10));
        await store.FlushAsync(includeOpen: true);

        var points = await store.ReadAsync(Btc, now.AddMinutes(-25), now.AddMinutes(-15));

        points.Select(p => p.Price).Should().Equal(2m);
    }

    /// <summary>
    /// Checks that instruments do not share prices.
    /// </summary>
    [Fact]
    public async Task ReadAsync_OtherAsset_ReturnsNothing()
    {
        await using var store = Create();
        store.Record(Btc, 1m, _time.GetUtcNow().AddMinutes(-2));
        await store.FlushAsync(includeOpen: true);

        var points = await store.ReadAsync(new AssetId("binance", "ETHUSDT"), DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        points.Should().BeEmpty();
    }

    /// <summary>
    /// Checks that a new store reads what an earlier one wrote.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_WritesOpenBuckets_ForNextStore()
    {
        var t = _time.GetUtcNow().AddMinutes(-4);
        await using (var first = Create())
        {
            first.Record(Btc, 42m, t);
        }

        await using var second = Create();
        var points = await second.ReadAsync(Btc, t.AddMinutes(-1), _time.GetUtcNow());

        points.Select(p => p.Price).Should().Equal(42m);
    }

    /// <summary>
    /// Checks that nothing is stored while the store is off, and that reading returns nothing.
    /// </summary>
    [Fact]
    public async Task Record_WhenDisabled_StoresNothing()
    {
        await using var store = Create();
        store.Configure(enabled: false, TimeSpan.FromDays(365));
        store.Record(Btc, 1m, _time.GetUtcNow().AddMinutes(-2));
        await store.FlushAsync(includeOpen: true);
        store.Configure(enabled: true, TimeSpan.FromDays(365));

        var points = await store.ReadAsync(Btc, DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        points.Should().BeEmpty();
        Directory.Exists(_directory).Should().BeFalse();
    }

    /// <summary>
    /// Checks that a record cut off at the end of a file is ignored and the next write continues cleanly.
    /// </summary>
    [Fact]
    public async Task ReadAsync_TornTail_IgnoresPartialRecord()
    {
        var t = _time.GetUtcNow().AddMinutes(-4);
        await using (var store = Create())
        {
            store.Record(Btc, 5m, t);
            await store.FlushAsync(includeOpen: true);
        }

        var file = Directory.GetFiles(_directory, "*.bin", SearchOption.AllDirectories).Single();
        await using (var stream = new FileStream(file, FileMode.Append, FileAccess.Write))
        {
            await stream.WriteAsync(new byte[10]);
        }

        await using var next = Create();
        next.Record(Btc, 6m, t.AddMinutes(1));
        await next.FlushAsync(includeOpen: true);
        var points = await next.ReadAsync(Btc, t.AddMinutes(-1), _time.GetUtcNow());

        points.Select(p => p.Price).Should().Equal(5m, 6m);
        new FileInfo(file).Length.Should().Be(48);
    }

    /// <summary>
    /// Checks that merging adds older prices in order and without duplicating a bucket.
    /// </summary>
    [Fact]
    public async Task MergeAsync_OlderPoints_AreSortedIntoStore()
    {
        await using var store = Create();
        var now = _time.GetUtcNow();
        store.Record(Btc, 3m, now.AddHours(-2));
        await store.FlushAsync(includeOpen: true);

        await store.MergeAsync(Btc, [new PricePoint(now.AddHours(-5), 1m), new PricePoint(now.AddHours(-4), 2m), new PricePoint(now.AddHours(-2), 99m)]);
        var points = await store.ReadAsync(Btc, now.AddHours(-6), now);

        points.Select(p => p.Price).Should().Equal(1m, 2m, 3m);
    }

    /// <summary>
    /// Checks that thinning out rewrites a file with fewer records and keeps what a chart of the range needs.
    /// </summary>
    [Fact]
    public async Task CompactAllAsync_OldDenseFile_ShrinksAndStaysReadable()
    {
        await using var store = Create();
        var start = _time.GetUtcNow().AddHours(-20);
        for (var i = 0; i < 120; i++)
        {
            store.Record(Btc, i, start.AddSeconds(i * 10));
        }

        await store.FlushAsync(includeOpen: true);
        var file = Directory.GetFiles(_directory, "*.bin", SearchOption.AllDirectories).Single();
        var before = new FileInfo(file).Length;

        await store.CompactAllAsync();

        new FileInfo(file).Length.Should().BeLessThan(before);
        var points = await store.ReadAsync(Btc, start.AddMinutes(-1), _time.GetUtcNow());
        points.Should().NotBeEmpty();
        points[^1].Price.Should().Be(119m);
    }

    /// <summary>
    /// Checks that a price past the retention is removed by thinning out.
    /// </summary>
    [Fact]
    public async Task CompactAllAsync_PastRetention_DeletesFile()
    {
        await using var store = Create();
        store.Configure(enabled: true, TimeSpan.FromDays(10));
        store.Record(Btc, 1m, _time.GetUtcNow().AddDays(-1));
        await store.FlushAsync(includeOpen: true);

        _time.Advance(TimeSpan.FromDays(20));
        await store.CompactAllAsync();

        Directory.GetFiles(_directory, "*.bin", SearchOption.AllDirectories).Should().BeEmpty();
    }

    /// <summary>
    /// Checks that a symbol with characters that are not allowed in a file name still gets its own file.
    /// </summary>
    [Theory]
    [InlineData("BTC/USDT")]
    [InlineData("600519.SS")]
    [InlineData("CON")]
    [InlineData("a:b*c?")]
    public async Task Record_UnsafeSymbol_IsStoredUnderSafeName(string symbol)
    {
        await using var store = Create();
        var id = new AssetId("p", symbol);
        store.Record(id, 1m, _time.GetUtcNow().AddMinutes(-1));
        await store.FlushAsync(includeOpen: true);

        var points = await store.ReadAsync(id, DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        points.Should().ContainSingle();
    }

    /// <summary>
    /// Checks that symbols that differ only in case get files that differ in more than case, as a file system that ignores
    /// case would otherwise give them one file.
    /// </summary>
    [Fact]
    public async Task Record_SymbolsDifferingInCase_StayApart()
    {
        await using var store = Create();
        var upper = new AssetId("p", "ABC");
        var lower = new AssetId("p", "abc");
        var t = _time.GetUtcNow().AddMinutes(-2);
        store.Record(upper, 1m, t);
        store.Record(lower, 2m, t);
        await store.FlushAsync(includeOpen: true);

        var files = Directory.GetFiles(_directory, "*.bin", SearchOption.AllDirectories);

        files.Should().HaveCount(2);
        files.Select(f => f.ToLowerInvariant()).Distinct().Should().HaveCount(2);
        (await store.ReadAsync(upper, DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).Select(p => p.Price).Should().Equal(1m);
        (await store.ReadAsync(lower, DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).Select(p => p.Price).Should().Equal(2m);
    }

    /// <summary>
    /// Checks that a very long symbol is named by a hash.
    /// </summary>
    [Fact]
    public void FileNamePart_LongValue_IsShortened()
    {
        var name = FileQuoteHistoryStore.FileNamePart(new string('A', 500));

        name.Length.Should().BeLessThan(100);
        name.Should().StartWith("h-");
    }

    /// <summary>
    /// Creates a store on the test folder.
    /// </summary>
    /// <returns>
    /// The store.
    /// </returns>
    private FileQuoteHistoryStore Create() => new(_directory, _time, NullLogger<FileQuoteHistoryStore>.Instance);
}
