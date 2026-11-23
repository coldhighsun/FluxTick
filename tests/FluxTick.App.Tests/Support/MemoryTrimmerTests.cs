using FluxTick.Abstractions;
using FluxTick.App.Services;
using FluxTick.Core.Application;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests of <see cref="MemoryTrimmer"/> and of when the main window asks it to give memory back.
/// </summary>
public sealed class MemoryTrimmerTests
{
    /// <summary>
    /// The first instrument used by the tests.
    /// </summary>
    private static readonly Asset Btc = new(Harness.Crypto, "BTC", "Bitcoin", AssetClass.Crypto);

    /// <summary>
    /// The second instrument used by the tests.
    /// </summary>
    private static readonly Asset Eth = new(Harness.Crypto, "ETH", "Ethereum", AssetClass.Crypto);

    /// <summary>
    /// The third instrument used by the tests.
    /// </summary>
    private static readonly Asset Sol = new(Harness.Crypto, "SOL", "Solana", AssetClass.Crypto);

    /// <summary>
    /// Verifies a burst of requests trims once, and only after the quiet period.
    /// </summary>
    [Fact]
    public async Task Schedule_Burst_TrimsOnceAfterQuietPeriod()
    {
        var time = new FakeTimeProvider();
        var trims = 0;
        var trimmer = new MemoryTrimmer(time, () => Interlocked.Increment(ref trims));

        trimmer.Schedule();
        time.Advance(MemoryTrimmer.QuietPeriod / 2);
        trimmer.Schedule();
        time.Advance(MemoryTrimmer.QuietPeriod / 2);
        await Task.Delay(50);
        Volatile.Read(ref trims).Should().Be(0);

        time.Advance(MemoryTrimmer.QuietPeriod);
        await Harness.EventuallyAsync(() => Volatile.Read(ref trims) == 1);
        await Task.Delay(50);

        Volatile.Read(ref trims).Should().Be(1);
    }

    /// <summary>
    /// Verifies collecting does not throw, so the default action of the trimmer is safe to run.
    /// </summary>
    [Fact]
    public void Collect_Always_Completes()
    {
        var act = MemoryTrimmer.Collect;

        act.Should().NotThrow();
    }

    /// <summary>
    /// Verifies the rows of the saved watchlist, shown at startup, do not ask for memory to be given back.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_SavedWatchlist_DoesNotTrim()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Eth));

        await h.Main.InitializeAsync();
        h.Time.Advance(MemoryTrimmer.QuietPeriod);
        await Task.Delay(50);

        h.Main.Rows.Should().HaveCount(2);
        h.TrimCount.Should().Be(0);
    }

    /// <summary>
    /// Verifies removing an instrument asks for memory to be given back.
    /// </summary>
    [Fact]
    public async Task SyncRows_InstrumentRemoved_Trims()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Eth));
        await h.Main.InitializeAsync();

        await h.Settings.UpdateAsync(s => s.Watchlist.RemoveAll(e => e.Symbol == "ETH"));
        await Harness.EventuallyAsync(() => h.Main.Rows.Count == 1);
        h.Time.Advance(MemoryTrimmer.QuietPeriod);

        await Harness.EventuallyAsync(() => h.TrimCount == 1);
    }

    /// <summary>
    /// Verifies adding an instrument asks for memory to be given back.
    /// </summary>
    [Fact]
    public async Task SyncRows_InstrumentAdded_Trims()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();

        await h.Settings.UpdateAsync(s => s.Watchlist.Add(WatchlistEntry.From(Eth)));
        await Harness.EventuallyAsync(() => h.Main.Rows.Count == 2);
        h.Time.Advance(MemoryTrimmer.QuietPeriod);

        await Harness.EventuallyAsync(() => h.TrimCount == 1);
    }

    /// <summary>
    /// Verifies swapping one instrument for another, which leaves the number of rows as it was, still asks for memory to be
    /// given back.
    /// </summary>
    [Fact]
    public async Task SyncRows_InstrumentReplaced_Trims()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Eth));
        await h.Main.InitializeAsync();

        await h.Settings.UpdateAsync(s =>
        {
            s.Watchlist.RemoveAll(e => e.Symbol == "ETH");
            s.Watchlist.Add(WatchlistEntry.From(Sol));
        });
        await Harness.EventuallyAsync(() => h.Main.Rows.Any(r => r.Asset.Symbol == "SOL"));
        h.Time.Advance(MemoryTrimmer.QuietPeriod);

        h.Main.Rows.Should().HaveCount(2);
        await Harness.EventuallyAsync(() => h.TrimCount == 1);
    }

    /// <summary>
    /// Verifies reordering the instruments gives nothing back, as no row came or went.
    /// </summary>
    [Fact]
    public async Task SyncRows_InstrumentsReordered_DoesNotTrim()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Eth));
        await h.Main.InitializeAsync();

        await h.Settings.UpdateAsync(s =>
        {
            var first = s.Watchlist[0];
            s.Watchlist.RemoveAt(0);
            s.Watchlist.Add(first);
        });
        await Harness.EventuallyAsync(() => h.Main.Rows[0].Asset.Symbol == "ETH");
        h.Time.Advance(MemoryTrimmer.QuietPeriod);
        await Task.Delay(50);

        h.TrimCount.Should().Be(0);
    }
}
