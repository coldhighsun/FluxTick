using FluxTick.Abstractions;
using FluxTick.Core.Application;
using FluxTick.Core.Domain;
using FluxTick.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for how <see cref="MarketDataService"/> records prices in a history store and starts the charts from it.
/// </summary>
public class StoredHistoryTests
{
    /// <summary>
    /// The instrument the tests track.
    /// </summary>
    private static readonly Asset Btc = new("crypto", "BTC", "Bitcoin", AssetClass.Crypto);

    /// <summary>
    /// The clock of the tests.
    /// </summary>
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Verifies that a quote that moves the chart is handed to the store with the time the chart places it at.
    /// </summary>
    [Fact]
    public async Task HandleQuote_NewPrice_IsRecorded()
    {
        var store = new MemoryStore();
        var factory = new StoreFactory(_time.GetUtcNow());
        await using var service = CreateService(factory, store);
        await service.ApplyAsync(SettingsFor());
        var at = _time.GetUtcNow().AddSeconds(-5);

        factory.Provider!.Emit(new Quote(Btc.Id, 123.5m, null, at));
        await WaitUntilAsync(() => store.Recorded.Count == 1);

        store.Recorded.Single().Should().Be((Btc.Id, 123.5m, at));
    }

    /// <summary>
    /// Verifies that the settings of the user reach the store.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_HistorySettings_ConfigureTheStore()
    {
        var store = new MemoryStore();
        await using var service = CreateService(new StoreFactory(_time.GetUtcNow()), store);
        var settings = SettingsFor();
        settings.History.Enabled = false;
        settings.History.RetentionDays = 30;

        await service.ApplyAsync(settings);

        store.Configured.Should().Be((false, TimeSpan.FromDays(30)));
    }

    /// <summary>
    /// Verifies that stored prices that cover the last hour make the chart without asking the source.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_StoredHourIsComplete_DoesNotAskTheSource()
    {
        var store = new MemoryStore();
        var now = _time.GetUtcNow();
        store.Stored.AddRange(Enumerable.Range(0, 12).Select(i => new PricePoint(now.AddMinutes(-55 + (i * 5)), i + 1)));
        var factory = new StoreFactory(now);
        await using var service = CreateService(factory, store);

        await service.ApplyAsync(SettingsFor());
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 12);

        service.GetSnapshots().Single().Sparkline.Should().Equal(Enumerable.Range(1, 12).Select(i => (decimal)i));
        factory.Provider!.HistoryRequests.Should().Be(0);
    }

    /// <summary>
    /// Verifies that a hole in the stored prices is filled from the source, and that what the source answered is stored.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_StoredHourHasHole_AsksSourceMergesAndStoresAnswer()
    {
        var store = new MemoryStore();
        var now = _time.GetUtcNow();
        store.Stored.AddRange([new PricePoint(now.AddMinutes(-30), 1m), new PricePoint(now.AddMinutes(-29), 2m)]);
        var factory = new StoreFactory(now);
        await using var service = CreateService(factory, store);

        await service.ApplyAsync(SettingsFor());
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 4);
        await WaitUntilAsync(() => store.Merged.Count == 1);

        service.GetSnapshots().Single().Sparkline.Should().Equal(1m, 2m, 100m, 200m);
        factory.Provider!.HistoryRequests.Should().Be(1);
        store.Merged.Single().Select(p => p.Price).Should().Equal(100m, 200m);
    }

    /// <summary>
    /// Verifies that a source that cannot be reached still leaves the chart with the stored prices.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_SourceFails_ChartStartsFromStoredPrices()
    {
        var store = new MemoryStore();
        var now = _time.GetUtcNow();
        store.Stored.AddRange([new PricePoint(now.AddMinutes(-30), 1m), new PricePoint(now.AddMinutes(-29), 2m)]);
        var factory = new StoreFactory(now) { Failure = new HttpRequestException("offline") };
        await using var service = CreateService(factory, store);

        await service.ApplyAsync(SettingsFor());
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 2);

        service.GetSnapshots().Single().Sparkline.Should().Equal(1m, 2m);
        store.Merged.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a source without history of its own still gives the chart the stored prices.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_NothingFromSource_ChartUsesStoredPrices()
    {
        var store = new MemoryStore();
        var now = _time.GetUtcNow();
        store.Stored.AddRange([new PricePoint(now.AddMinutes(-30), 1m), new PricePoint(now.AddMinutes(-29), 2m)]);
        var factory = new StoreFactory(now) { History = [] };
        await using var service = CreateService(factory, store);

        await service.ApplyAsync(SettingsFor());
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 2);

        service.GetSnapshots().Single().Sparkline.Should().Equal(1m, 2m);
    }

    /// <summary>
    /// Verifies that nothing is read from the store while the user keeps no history.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_HistoryOff_IgnoresTheStore()
    {
        var store = new MemoryStore();
        var now = _time.GetUtcNow();
        store.Stored.AddRange(Enumerable.Range(0, 12).Select(i => new PricePoint(now.AddMinutes(-55 + (i * 5)), i + 1)));
        var factory = new StoreFactory(now);
        await using var service = CreateService(factory, store);
        var settings = SettingsFor();
        settings.History.Enabled = false;

        await service.ApplyAsync(settings);
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 2);

        store.Reads.Should().Be(0);
        store.Merged.Should().BeEmpty();
        factory.Provider!.HistoryRequests.Should().Be(1);
    }

    /// <summary>
    /// Verifies that stored prices that cover the window of a range make its chart without asking the source.
    /// </summary>
    [Fact]
    public async Task SetChartRange_StoredRangeIsComplete_DoesNotAskTheSource()
    {
        var store = new MemoryStore();
        var now = _time.GetUtcNow();
        store.Stored.AddRange(Enumerable.Range(0, 144).Select(i => new PricePoint(now.AddMinutes(-1430 + (i * 10)), i + 1)));
        var loads = 0;
        var factory = new StoreFactory(now, supportsDay: true) { RangeHook = () => Interlocked.Increment(ref loads) };
        await using var service = CreateService(factory, store);
        service.SetChartRange(HistoryRange.Day);

        await service.ApplyAsync(SettingsFor());
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 144);

        service.GetSnapshots().Single().Timeline!.CoversRange.Should().BeTrue();
        Volatile.Read(ref loads).Should().Be(0);
    }

    /// <summary>
    /// Verifies that a range with a hole in the stored prices is filled from the source, and the answer is stored.
    /// </summary>
    [Fact]
    public async Task SetChartRange_StoredRangeHasHole_AsksSourceMergesAndStoresAnswer()
    {
        var store = new MemoryStore();
        var now = _time.GetUtcNow();
        store.Stored.AddRange([new PricePoint(now.AddHours(-23), 5m), new PricePoint(now.AddHours(-22), 6m)]);
        var loads = 0;
        var factory = new StoreFactory(now, supportsDay: true) { RangeHook = () => Interlocked.Increment(ref loads) };
        await using var service = CreateService(factory, store);
        service.SetChartRange(HistoryRange.Day);

        await service.ApplyAsync(SettingsFor());
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 5);

        // The last hour was merged as well, from the source's answer for it.
        await WaitUntilAsync(() => store.MergedPrices().Any(p => p.SequenceEqual([10m, 20m, 30m])));
        service.GetSnapshots().Single().Sparkline.Should().Equal(5m, 6m, 10m, 20m, 30m);
        Volatile.Read(ref loads).Should().Be(1);
    }

    /// <summary>
    /// Creates the service under test.
    /// </summary>
    /// <param name="factory">
    /// The provider factory.
    /// </param>
    /// <param name="store">
    /// The history store.
    /// </param>
    /// <returns>
    /// The service.
    /// </returns>
    private MarketDataService CreateService(StoreFactory factory, MemoryStore store) =>
        new(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time, history: store);

    /// <summary>
    /// Creates settings that track the instrument of the tests.
    /// </summary>
    /// <returns>
    /// The settings.
    /// </returns>
    private static AppSettings SettingsFor()
    {
        var settings = new AppSettings();
        settings.Watchlist.Add(WatchlistEntry.From(Btc));
        return settings;
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
    /// A store that keeps prices in memory and notes what it was asked.
    /// </summary>
    private sealed class MemoryStore : IQuoteHistoryStore
    {
        /// <summary>
        /// Guards the lists, which the service touches from several threads.
        /// </summary>
        private readonly Lock _lock = new();

        /// <summary>
        /// The number of reads.
        /// </summary>
        private int _reads;

        /// <summary>
        /// Gets the prices that are given back by a read.
        /// </summary>
        public List<PricePoint> Stored { get; } = [];

        /// <summary>
        /// Gets the prices that were recorded.
        /// </summary>
        public List<(AssetId Id, decimal Price, DateTimeOffset Time)> Recorded { get; } = [];

        /// <summary>
        /// Gets the lists of prices that were merged.
        /// </summary>
        public List<IReadOnlyList<PricePoint>> Merged { get; } = [];

        /// <summary>
        /// Gets the last configuration, or <see langword="null"/> when there was none.
        /// </summary>
        public (bool Enabled, TimeSpan Retention)? Configured { get; private set; }

        /// <summary>
        /// Gets the number of reads.
        /// </summary>
        public int Reads => Volatile.Read(ref _reads);

        /// <inheritdoc />
        public void Record(AssetId assetId, decimal price, DateTimeOffset time)
        {
            lock (_lock)
            {
                Recorded.Add((assetId, price, time));
            }
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<PricePoint>> ReadAsync(AssetId assetId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _reads);
            lock (_lock)
            {
                return Task.FromResult<IReadOnlyList<PricePoint>>([.. Stored.Where(p => p.Timestamp >= from && p.Timestamp <= to).OrderBy(p => p.Timestamp)]);
            }
        }

        /// <inheritdoc />
        public Task MergeAsync(AssetId assetId, IReadOnlyList<PricePoint> points, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                Merged.Add(points);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Gets the prices of each merge, as they were merged.
        /// </summary>
        /// <returns>
        /// One list of prices per merge.
        /// </returns>
        public List<List<decimal>> MergedPrices()
        {
            lock (_lock)
            {
                return [.. Merged.Select(m => m.Select(p => p.Price).ToList())];
            }
        }

        /// <inheritdoc />
        public void Configure(bool enabled, TimeSpan retention) => Configured = (enabled, retention);
    }

    /// <summary>
    /// A factory whose provider has a few minutes of history and, if asked to, three prices of the last day.
    /// </summary>
    /// <param name="now">
    /// The current time used to build the history.
    /// </param>
    /// <param name="supportsDay">
    /// Whether the provider has the history of the last day.
    /// </param>
    private sealed class StoreFactory(DateTimeOffset now, bool supportsDay = false) : IMarketDataProviderFactory
    {
        /// <inheritdoc />
        public ProviderDescriptor Descriptor { get; } = new("crypto", "Crypto", "Test", [AssetClass.Crypto], ProviderUpdateMode.Streaming);

        /// <summary>
        /// Gets the provider created last.
        /// </summary>
        public FakeProvider? Provider { get; private set; }

        /// <summary>
        /// Gets or sets the history of the last hour the provider answers with.
        /// </summary>
        public IReadOnlyList<PricePoint> History { get; set; } = [new PricePoint(now.AddMinutes(-2), 100m), new PricePoint(now.AddMinutes(-1), 200m)];

        /// <summary>
        /// Gets or sets what the history of the last hour fails with, or <see langword="null"/> for an answer.
        /// </summary>
        public Exception? Failure { get; set; }

        /// <summary>
        /// Gets or sets what runs when the history of a range is asked for.
        /// </summary>
        public Action? RangeHook { get; set; }

        /// <inheritdoc />
        public IMarketDataProvider Create(ProviderCreationContext context)
        {
            var provider = new FakeProvider(Descriptor, context.Options)
            {
                History = History,
                HistoryFailure = Failure,
                RangeHistory = [new PricePoint(now.AddHours(-3), 10m), new PricePoint(now.AddHours(-2), 20m), new PricePoint(now.AddHours(-1), 30m)],
                RangeHistoryHook = () =>
                {
                    RangeHook?.Invoke();
                    return Task.CompletedTask;
                },
            };
            if (supportsDay)
            {
                provider.SupportedRanges.Add(HistoryRange.Day);
            }

            Provider = provider;
            return provider;
        }
    }
}
