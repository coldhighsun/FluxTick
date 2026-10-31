using FluxTick.Abstractions;
using FluxTick.App.Localization;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for what a row of the list layout shows when the market is shut or the data is not live.
/// </summary>
public class RowListDisplayTests
{
    /// <summary>
    /// The instrument used by the tests.
    /// </summary>
    private static readonly Asset Stock = new("p", "600519", "Moutai", AssetClass.Equity);

    /// <summary>
    /// Builds a snapshot with a quote.
    /// </summary>
    /// <param name="health">
    /// The data health.
    /// </param>
    /// <param name="market">
    /// The trading state of the market.
    /// </param>
    /// <returns>
    /// The snapshot.
    /// </returns>
    private static AssetSnapshot Snapshot(AssetHealth health, MarketState market) =>
        new(Stock, new Quote(Stock.Id, 110m, 100m, DateTimeOffset.UnixEpoch), [100m, 110m], health, MarketState: market);

    /// <summary>
    /// Verifies a shut market keeps its change in percent, tells the state in the chip and is dimmed.
    /// </summary>
    [Fact]
    public void Update_MarketClosed_ShowsChangeAndNeutralBadgeAndDims()
    {
        var row = new AssetRowViewModel(Stock, "p");

        row.Update(Snapshot(AssetHealth.Stale, MarketState.Closed));

        row.ChangeDisplayText.Should().Be("+10.00%");
        row.HasRowBadge.Should().BeTrue();
        row.RowBadgeText.Should().Be(Loc.Get("MarketState_Closed"));
        row.RowBadgeIsWarn.Should().BeFalse();
        row.IsMarketShut.Should().BeTrue();
        row.IsDimmed.Should().BeTrue("a shut market is dimmed in the list, the cards and the tray alike");
    }

    /// <summary>
    /// Verifies a problem with the data is told as a warning chip and the last change stays in view.
    /// </summary>
    [Fact]
    public void Update_FailedSource_ShowsWarningBadgeAndKeepsChange()
    {
        var row = new AssetRowViewModel(Stock, "p");

        row.Update(Snapshot(AssetHealth.Failed, MarketState.Open));

        row.RowBadgeIsWarn.Should().BeTrue();
        row.RowBadgeText.Should().Be(Loc.Get("Status_SourceError"));
        row.ChangeDisplayText.Should().Be("+10.00%");
        row.IsDimmed.Should().BeTrue();
    }

    /// <summary>
    /// Verifies delayed prices give way to the trading state of the market in the chip, and are shown when there is none.
    /// </summary>
    [Fact]
    public void Update_DelayedInAfterHours_ShowsTheTradingStateNotTheDelay()
    {
        var row = new AssetRowViewModel(Stock, "p");

        row.Update(Snapshot(AssetHealth.Delayed, MarketState.AfterHours));

        row.RowBadgeText.Should().Be(Loc.Get("MarketState_AfterHours"));
        row.RowBadgeIsWarn.Should().BeFalse();

        row.Update(Snapshot(AssetHealth.Delayed, MarketState.Open));

        row.RowBadgeText.Should().Be(Loc.Get("Status_Delayed"));
        row.RowBadgeIsWarn.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a fault of the data still wins over the trading state of the market.
    /// </summary>
    [Fact]
    public void Update_FailedInAfterHours_ShowsTheFault()
    {
        var row = new AssetRowViewModel(Stock, "p");

        row.Update(Snapshot(AssetHealth.Failed, MarketState.AfterHours));

        row.RowBadgeText.Should().Be(Loc.Get("Status_SourceError"));
        row.RowBadgeIsWarn.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the profit or loss in percent is available for sorting only for a row with a holding.
    /// </summary>
    [Fact]
    public void PnlPercent_WithAndWithoutHolding_IsThePercentOrNull()
    {
        var held = new AssetRowViewModel(Stock, "p");
        var free = new AssetRowViewModel(Stock, "p");
        held.SetHolding(100m, 2m, "CNY");
        held.Update(Snapshot(AssetHealth.Live, MarketState.Open));
        free.Update(Snapshot(AssetHealth.Live, MarketState.Open));

        held.PnlPercent.Should().Be(10m);
        free.PnlPercent.Should().BeNull();
    }

    /// <summary>
    /// Verifies a regular session shows no chip.
    /// </summary>
    [Fact]
    public void Update_RegularSession_ShowsNoBadge()
    {
        var row = new AssetRowViewModel(Stock, "p");

        row.Update(Snapshot(AssetHealth.Live, MarketState.Open));

        row.HasRowBadge.Should().BeFalse();
        row.ChangeDisplayText.Should().Be("+10.00%");
    }

    /// <summary>
    /// Verifies a row without a quote shows a dash instead of an empty line, which would make the row lower.
    /// </summary>
    [Fact]
    public void ChangeDisplayText_BeforeFirstQuote_IsADash()
    {
        var row = new AssetRowViewModel(Stock, "p");

        row.ChangeDisplayText.Should().Be("—");
    }
}
