using FluxTick.Abstractions;
using FluxTick.App.Services;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the texts a row shows for a holding.
/// </summary>
public class HoldingRowTests
{
    /// <summary>
    /// The Bitcoin test asset.
    /// </summary>
    private static readonly Asset Btc = new(Harness.Crypto, "BTC", "Bitcoin", AssetClass.Crypto);

    /// <summary>
    /// Builds a snapshot of a test asset.
    /// </summary>
    /// <param name="asset">
    /// The asset.
    /// </param>
    /// <param name="price">
    /// The price.
    /// </param>
    /// <returns>
    /// The snapshot.
    /// </returns>
    private static AssetSnapshot Snapshot(Asset asset, decimal price) =>
        new(asset, new Quote(asset.Id, price, 100m, DateTimeOffset.UnixEpoch), [100m, price], AssetHealth.Live);

    /// <summary>
    /// Verifies the texts of a row with a holding at a gain.
    /// </summary>
    [Fact]
    public void Update_RowWithHolding_ShowsProfitAndLoss()
    {
        var row = new AssetRowViewModel(Btc, "Crypto Co");
        row.SetHolding(100m, 2m, "USD");

        row.Update(Snapshot(Btc, 110m));

        row.HasHolding.Should().BeTrue();
        row.IsProfit.Should().BeTrue();
        row.IsLoss.Should().BeFalse();
        row.PnlText.Should().Be("+20.00 USD (+10.00%)");
        row.HoldingText.Should().Be("2 × 100.00 USD");
        row.CostPriceText.Should().Be("100.00");
        row.PnlAmountText.Should().Be("+20.00");
        row.PnlCompactText.Should().Be("+20.00 (+10.00%)");
        row.PnlPercentText.Should().Be("+10.00%");
        row.Tooltip.Should().Contain("220.00");
    }

    /// <summary>
    /// Verifies that a row at a loss is marked and signed.
    /// </summary>
    [Fact]
    public void Update_RowAtLoss_ShowsNegativeFigures()
    {
        var row = new AssetRowViewModel(Btc, "Crypto Co");
        row.SetHolding(100m, 2m, "USD");

        row.Update(Snapshot(Btc, 90m));

        row.IsLoss.Should().BeTrue();
        row.PnlText.Should().Be("-20.00 USD (-10.00%)");
        row.PnlPercentText.Should().Be("-10.00%");
    }

    /// <summary>
    /// Verifies that the profit follows a holding that is set after the price came in, and goes away with the holding.
    /// </summary>
    [Fact]
    public void SetHolding_AfterQuote_RecalculatesAndCanBeRemoved()
    {
        var row = new AssetRowViewModel(Btc, "Crypto Co");
        row.Update(Snapshot(Btc, 110m));
        row.HasHolding.Should().BeFalse();

        row.SetHolding(100m, 1m, "USD");
        row.PnlText.Should().Be("+10.00 USD (+10.00%)");

        row.SetHolding(null, null, "USD");
        row.HasHolding.Should().BeFalse();
        row.Pnl.Should().BeNull();
    }

    /// <summary>
    /// Verifies that a holding without a price yet shows a placeholder.
    /// </summary>
    [Fact]
    public void SetHolding_WithoutQuote_ShowsPlaceholder()
    {
        var row = new AssetRowViewModel(Btc, "Crypto Co");

        row.SetHolding(100m, 1m, "USD");

        row.HasHolding.Should().BeTrue();
        row.PnlText.Should().Be(PriceFormatter.Unavailable);
    }

    /// <summary>
    /// Verifies that the texts of a holding are announced when the price moves and the profit changes, and that a row
    /// without a holding stays quiet.
    /// </summary>
    [Fact]
    public void Update_PriceMoves_AnnouncesHoldingTextsOnlyForHeldRows()
    {
        var held = new AssetRowViewModel(Btc, "Crypto Co");
        held.SetHolding(100m, 2m, "USD");
        var plain = new AssetRowViewModel(Btc, "Crypto Co");
        var heldChanges = new List<string?>();
        var plainChanges = new List<string?>();
        held.PropertyChanged += (_, e) => heldChanges.Add(e.PropertyName);
        plain.PropertyChanged += (_, e) => plainChanges.Add(e.PropertyName);

        held.Update(Snapshot(Btc, 110m));
        plain.Update(Snapshot(Btc, 110m));

        heldChanges.Should().Contain([nameof(AssetRowViewModel.PnlText), nameof(AssetRowViewModel.PnlAmountText), nameof(AssetRowViewModel.PnlPercentText), nameof(AssetRowViewModel.PnlCompactText)]);
        plainChanges.Should().NotContain(nameof(AssetRowViewModel.PnlText));
        plainChanges.Should().NotContain(nameof(AssetRowViewModel.HoldingText));

        heldChanges.Clear();
        held.Update(Snapshot(Btc, 110m));

        heldChanges.Should().NotContain(nameof(AssetRowViewModel.PnlText), "the profit did not change");
    }

    /// <summary>
    /// Verifies that the tooltip is announced with every price but only worked out when it is read.
    /// </summary>
    [Fact]
    public void Tooltip_AfterUpdate_IsAnnouncedAndFollowsTheLatestPrice()
    {
        var row = new AssetRowViewModel(Btc, "Crypto Co");
        row.SetHolding(100m, 2m, "USD");
        var announced = 0;
        row.PropertyChanged += (_, e) => announced += e.PropertyName == nameof(AssetRowViewModel.Tooltip) ? 1 : 0;

        row.Tooltip.Should().BeEmpty();
        row.Update(Snapshot(Btc, 110m));
        row.Update(Snapshot(Btc, 120m));

        announced.Should().BeGreaterThanOrEqualTo(2);
        row.Tooltip.Should().Contain("240.00").And.NotContain("220.00");
    }

    /// <summary>
    /// Verifies that the content of the tooltip reads the tooltip of the row, announces the text only while it is open and
    /// announces it as it opens.
    /// </summary>
    [Fact]
    public void TooltipContent_OpenAndClosed_IsAnnouncedOnlyWhileOpen()
    {
        var row = new AssetRowViewModel(Btc, "Crypto Co");
        var announced = 0;
        row.TooltipContent.PropertyChanged += (_, e) => announced += e.PropertyName == nameof(RowTooltip.Text) ? 1 : 0;

        row.Update(Snapshot(Btc, 110m));
        row.Update(Snapshot(Btc, 120m));
        announced.Should().Be(0, "nobody looks at a closed tooltip");

        row.TooltipContent.IsOpen = true;
        announced.Should().Be(1, "the text may have changed while it was closed");
        row.Update(Snapshot(Btc, 130m));
        announced.Should().Be(2);
        row.TooltipContent.Text.Should().Be(row.Tooltip);

        row.TooltipContent.IsOpen = false;
        row.Update(Snapshot(Btc, 140m));
        announced.Should().Be(2);
    }
}
