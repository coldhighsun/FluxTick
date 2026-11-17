using FluxTick.Abstractions;
using FluxTick.App.Localization;
using FluxTick.App.Services;
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
    /// Verifies the headline change is the one against the reference price in every layout, and the change over the range
    /// of the chart is a figure of its own that only the card shows, so that rows of sources with and without the range
    /// stay comparable.
    /// </summary>
    [Fact]
    public void Update_ChartOfARange_HeadlineKeepsTheDailyChangeAndRangeChangeIsSeparate()
    {
        var asOf = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset[] times = [asOf.AddHours(-20), asOf.AddHours(-1)];
        var timeline = new SparklineTimeline(times, asOf, TimeSpan.FromDays(1), TimeSpan.FromMinutes(5), range: HistoryRange.Day);
        var row = new AssetRowViewModel(Stock, "p");

        // The price is 10% above the reference and 45% below the start of the chart.
        row.Update(new AssetSnapshot(Stock, new Quote(Stock.Id, 110m, 100m, asOf), [200m, 110m], AssetHealth.Live, Timeline: timeline));

        row.ChangePercent.Should().Be(10m);
        row.ChangeDisplayText.Should().Be("+10.00%");
        row.IsUp.Should().BeTrue();
        row.ChangeText.Should().Be("+10.00%");
        row.RangeChangeText.Should().Be("-45.00%");
        row.HasRangeChange.Should().BeTrue();
        row.IsRangeDown.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the card shows no change over a range while the chart has no range of its own, and that the chart is
    /// coloured by the change against the reference price then.
    /// </summary>
    [Fact]
    public void Update_NoRange_NoRangeChangeAndChartFollowsTheQuote()
    {
        var row = new AssetRowViewModel(Stock, "p");

        row.Update(Snapshot(AssetHealth.Live, MarketState.Open));

        row.ChangeText.Should().Be("+10.00%");
        row.HasRangeChange.Should().BeFalse();
        row.RangeChangeText.Should().BeEmpty();
        row.IsRangeUp.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the change over the range is cleared when a snapshot has no quote.
    /// </summary>
    [Fact]
    public void Update_QuoteLost_ClearsTheRangeChange()
    {
        var asOf = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset[] times = [asOf.AddHours(-20), asOf.AddHours(-1)];
        var timeline = new SparklineTimeline(times, asOf, TimeSpan.FromDays(1), TimeSpan.FromMinutes(5), range: HistoryRange.Day);
        var row = new AssetRowViewModel(Stock, "p");
        row.Update(new AssetSnapshot(Stock, new Quote(Stock.Id, 110m, 100m, asOf), [200m, 110m], AssetHealth.Live, Timeline: timeline));

        row.Update(new AssetSnapshot(Stock, null, [200m, 110m], AssetHealth.Live, Timeline: timeline));

        row.HasRangeChange.Should().BeFalse();
        row.RangeChangeText.Should().BeEmpty();
        row.ChangeOverRange.Should().BeNull();
        row.ReferenceText.Should().Be(PriceFormatter.Unavailable);
        row.ReferenceLabel.Should().Be(Loc.Get("Row_Reference"));
        row.IsRangeUp.Should().BeFalse();
        row.IsRangeDown.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the text that names the baseline follows the basis of the quote.
    /// </summary>
    /// <param name="basis">
    /// The basis of the reference price.
    /// </param>
    /// <param name="key">
    /// The key of the expected text.
    /// </param>
    [Theory]
    [InlineData(ReferenceBasis.PreviousClose, "Row_BasisPrevClose")]
    [InlineData(ReferenceBasis.Rolling24Hours, "Row_Basis24h")]
    public void Update_Basis_NamesTheBaseline(ReferenceBasis basis, string key)
    {
        var row = new AssetRowViewModel(Stock, "p");
        var quote = new Quote(Stock.Id, 110m, 100m, DateTimeOffset.UnixEpoch) { Basis = basis };

        row.Update(new AssetSnapshot(Stock, quote, [100m, 110m], AssetHealth.Live));

        row.BasisText.Should().Be(Loc.Get(key));
    }

    /// <summary>
    /// Verifies no basis is named when the quote has no change to measure.
    /// </summary>
    [Fact]
    public void Update_QuoteWithoutReference_BasisTextIsEmpty()
    {
        var row = new AssetRowViewModel(Stock, "p");

        row.Update(new AssetSnapshot(Stock, new Quote(Stock.Id, 110m, null, DateTimeOffset.UnixEpoch), [100m, 110m], AssetHealth.Live));

        row.BasisText.Should().BeEmpty();
    }

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
    /// Verifies a shut market is dimmed even when its source goes on sending prices that look live, and is not dimmed
    /// again once the market opens.
    /// </summary>
    [Fact]
    public void Update_MarketClosedWithLivePrices_DimsUntilItOpens()
    {
        var row = new AssetRowViewModel(Stock, "p");

        row.Update(Snapshot(AssetHealth.Live, MarketState.Closed));

        row.IsStopped.Should().BeFalse();
        row.IsDimmed.Should().BeTrue();

        row.Update(Snapshot(AssetHealth.Live, MarketState.Open));

        row.IsDimmed.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the closing price of a shut market is not outdated, and is once the market opens with the prices still stale.
    /// </summary>
    [Fact]
    public void Update_StaleInShutMarket_PriceIsOutdatedOnlyOnceTheMarketOpens()
    {
        var row = new AssetRowViewModel(Stock, "p");

        row.Update(Snapshot(AssetHealth.Stale, MarketState.Closed));

        row.IsPriceOutdated.Should().BeFalse();

        row.Update(Snapshot(AssetHealth.Stale, MarketState.Open));

        row.IsPriceOutdated.Should().BeTrue();
    }

    /// <summary>
    /// Verifies IsDimmed is notified when only the market state changes and the health stays the same.
    /// </summary>
    [Fact]
    public void Update_MarketOpensWithSameHealth_NotifiesIsDimmed()
    {
        var row = new AssetRowViewModel(Stock, "p");
        row.Update(Snapshot(AssetHealth.Live, MarketState.Closed));
        var notified = new List<string?>();
        row.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        row.Update(Snapshot(AssetHealth.Live, MarketState.Open));

        notified.Should().Contain(nameof(AssetRowViewModel.IsDimmed));
        row.IsDimmed.Should().BeFalse();
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
