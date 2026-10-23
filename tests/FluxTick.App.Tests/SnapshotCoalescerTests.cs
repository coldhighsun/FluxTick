using FluxTick.Abstractions;
using FluxTick.App.Services;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for <see cref="SnapshotCoalescer"/>.
/// </summary>
public class SnapshotCoalescerTests
{
    /// <summary>
    /// The Bitcoin test asset.
    /// </summary>
    private static readonly Asset Btc = new(Harness.Crypto, "BTC", "Bitcoin", AssetClass.Crypto);

    /// <summary>
    /// The Ether test asset.
    /// </summary>
    private static readonly Asset Eth = new(Harness.Crypto, "ETH", "Ether", AssetClass.Crypto);

    /// <summary>
    /// A dispatcher that keeps background work until the test runs it, like a busy UI thread.
    /// </summary>
    private sealed class QueueDispatcher : IUiDispatcher
    {
        /// <summary>
        /// The background actions that have not run yet.
        /// </summary>
        public Queue<Action> Background { get; } = new();

        /// <inheritdoc />
        public void Post(Action action) => action();

        /// <summary>
        /// Gets or sets a value indicating whether queuing fails, like a dispatcher that is shutting down.
        /// </summary>
        public bool FailToQueue { get; set; }

        /// <inheritdoc />
        public void PostBackground(Action action)
        {
            if (FailToQueue)
            {
                throw new InvalidOperationException("The dispatcher is shutting down.");
            }

            Background.Enqueue(action);
        }

        /// <summary>
        /// Runs everything that is queued, including what the actions queue meanwhile.
        /// </summary>
        public void RunAll()
        {
            while (Background.TryDequeue(out var action))
            {
                action();
            }
        }
    }

    /// <summary>
    /// Creates a snapshot whose price tells the snapshots apart.
    /// </summary>
    /// <param name="asset">
    /// The instrument.
    /// </param>
    /// <param name="price">
    /// The price.
    /// </param>
    /// <returns>
    /// The snapshot.
    /// </returns>
    private static AssetSnapshot Snapshot(Asset asset, decimal price) =>
        new(asset, new Quote(asset.Id, price, null, DateTimeOffset.UnixEpoch), [], AssetHealth.Live);

    /// <summary>
    /// Verifies a burst of snapshots of one instrument is applied once, with the newest.
    /// </summary>
    [Fact]
    public void Submit_BurstForOneInstrument_AppliesOnlyTheLatestOnce()
    {
        var ui = new QueueDispatcher();
        List<decimal> applied = [];
        var coalescer = new SnapshotCoalescer(ui, s => applied.Add(s.Quote!.Price));

        for (var price = 1; price <= 100; price++)
        {
            coalescer.Submit(Snapshot(Btc, price));
        }

        applied.Should().BeEmpty("the UI thread has not got to it yet");
        ui.Background.Should().ContainSingle("one hand-over is enough however many prices arrive");

        ui.RunAll();

        applied.Should().Equal(100m);
    }

    /// <summary>
    /// Verifies the latest snapshot of every instrument is kept.
    /// </summary>
    [Fact]
    public void Submit_SeveralInstruments_AppliesTheLatestOfEach()
    {
        var ui = new QueueDispatcher();
        List<string> applied = [];
        var coalescer = new SnapshotCoalescer(ui, s => applied.Add($"{s.Asset.Symbol}={s.Quote!.Price}"));

        coalescer.Submit(Snapshot(Btc, 1));
        coalescer.Submit(Snapshot(Eth, 10));
        coalescer.Submit(Snapshot(Btc, 2));
        coalescer.Submit(Snapshot(Eth, 20));
        ui.RunAll();

        applied.Should().BeEquivalentTo(["BTC=2", "ETH=20"]);
    }

    /// <summary>
    /// Verifies a snapshot that arrives after a hand-over started is applied by the next one, not lost.
    /// </summary>
    [Fact]
    public void Submit_AfterHandOverStarted_QueuesAnotherHandOver()
    {
        var ui = new QueueDispatcher();
        List<decimal> applied = [];
        SnapshotCoalescer? coalescer = null;
        coalescer = new SnapshotCoalescer(ui, s =>
        {
            applied.Add(s.Quote!.Price);
            if (s.Quote.Price == 1m)
            {
                coalescer!.Submit(Snapshot(Btc, 2));
            }
        });

        coalescer.Submit(Snapshot(Btc, 1));
        ui.RunAll();

        applied.Should().Equal(1m, 2m);
    }

    /// <summary>
    /// Verifies snapshots that are left when applying one fails are not stranded until the next price arrives.
    /// </summary>
    [Fact]
    public void Drain_ApplyThrows_RemainingSnapshotsAreStillHandedOver()
    {
        var ui = new QueueDispatcher();
        List<string> applied = [];
        var failOnce = true;
        var coalescer = new SnapshotCoalescer(ui, s =>
        {
            if (failOnce)
            {
                failOnce = false;
                throw new InvalidOperationException("boom");
            }

            applied.Add(s.Asset.Symbol);
        });

        coalescer.Submit(Snapshot(Btc, 1));
        coalescer.Submit(Snapshot(Eth, 1));

        var act = ui.RunAll;

        act.Should().Throw<InvalidOperationException>();
        ui.Background.Should().ContainSingle("the leftover snapshot is queued again");
        ui.RunAll();
        applied.Should().HaveCount(1);
    }

    /// <summary>
    /// Verifies a hand-over that could not be queued is reported but not thrown at the caller, which is the quote loop
    /// of a data source, keeps the snapshot, and lets the next snapshot try again.
    /// </summary>
    [Fact]
    public void Submit_QueuingFailed_ReportsKeepsSnapshotAndNextOneTriesAgain()
    {
        var ui = new QueueDispatcher { FailToQueue = true };
        List<string> applied = [];
        List<Exception> errors = [];
        var coalescer = new SnapshotCoalescer(ui, s => applied.Add(s.Asset.Symbol), errors.Add);

        var failing = () => coalescer.Submit(Snapshot(Btc, 1));
        failing.Should().NotThrow();
        errors.Should().ContainSingle().Which.Should().BeOfType<InvalidOperationException>();

        ui.FailToQueue = false;
        coalescer.Submit(Snapshot(Eth, 1));
        ui.RunAll();

        applied.Should().BeEquivalentTo(["BTC", "ETH"], "the snapshot that could not be queued was kept");
    }
}
