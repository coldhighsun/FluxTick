using FluxTick.Abstractions;
using FluxTick.Core.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.Core.Tests.MarketData;

/// <summary>
/// Tests for <see cref="QuoteHistoryDump"/>.
/// </summary>
public sealed class QuoteHistoryDumpTests : IDisposable
{
    /// <summary>
    /// The folder of this test run.
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"fluxtick-dump-{Guid.NewGuid():N}");

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// Checks that the prices a store wrote are read back from its file, and that a record cut short is skipped.
    /// </summary>
    [Fact]
    public async Task Read_FileOfStoreWithCutRecord_ReturnsWholeRecords()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));
        var id = new AssetId("binance", "BTCUSDT");
        var t = time.GetUtcNow().AddMinutes(-5);
        await using (var store = new FileQuoteHistoryStore(_directory, time, NullLogger<FileQuoteHistoryStore>.Instance))
        {
            store.Record(id, 100.5m, t);
            store.Record(id, 101.25m, t.AddSeconds(20));
            await store.FlushAsync(includeOpen: true);
        }

        var path = Directory.GetFiles(_directory, "*.bin", SearchOption.AllDirectories).Single();
        await using (var stream = new FileStream(path, FileMode.Append))
        {
            stream.Write(new byte[10]);
        }

        var points = QuoteHistoryDump.Read(path);

        points.Select(p => p.Price).Should().Equal(100.5m, 101.25m);
        points[0].Timestamp.Should().Be(t);
        QuoteHistoryDump.RecordCountOf(new FileInfo(path).Length).Should().Be(2);
    }

    /// <summary>
    /// Checks the CSV layout.
    /// </summary>
    [Fact]
    public void WriteCsv_Prices_WritesHeaderAndInvariantLines()
    {
        var writer = new StringWriter();
        var point = new PricePoint(new DateTimeOffset(2026, 6, 15, 12, 0, 1, 250, TimeSpan.FromHours(8)), 1234.5m);

        QuoteHistoryDump.WriteCsv([point], writer);

        writer.ToString().ReplaceLineEndings("\n").Should().Be("time_utc,price\n2026-06-15T04:00:01.250Z,1234.5\n");
    }
}
