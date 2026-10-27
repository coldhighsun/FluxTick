using FluxTick.Abstractions;
using FluxTick.App.Services;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the holding editor.
/// </summary>
public class HoldingViewModelTests
{
    /// <summary>
    /// The Bitcoin test asset.
    /// </summary>
    private static readonly Asset Btc = new(Harness.Crypto, "BTC", "Bitcoin", AssetClass.Crypto);

    /// <summary>
    /// The Apple test asset.
    /// </summary>
    private static readonly Asset Aapl = new(Harness.Stocks, "AAPL", "Apple", AssetClass.Equity);

    /// <summary>
    /// Verifies that a valid form saves the holding of the instrument.
    /// </summary>
    [Fact]
    public async Task Save_ValidForm_PersistsHoldingOfTheSubject()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Aapl));
        h.Holding.Begin(Btc);
        h.Holding.CostText = " 10.5 ";
        h.Holding.QuantityText = "20";
        h.Holding.SelectedCurrency = "EUR";
        var completed = false;
        h.Holding.Completed += (_, _) => completed = true;

        await h.Holding.SaveCommand.ExecuteAsync(null);

        var saved = h.Store.Stored!.Watchlist.Single(e => e.Symbol == "BTC");
        saved.CostPrice.Should().Be(10.5m);
        saved.Quantity.Should().Be(20m);
        saved.HoldingCurrency.Should().Be("EUR");
        h.Store.Stored.Watchlist.Single(e => e.Symbol == "AAPL").HasHolding.Should().BeFalse();
        completed.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that values that are not above zero save nothing and say why.
    /// </summary>
    /// <param name="cost">
    /// The typed cost.
    /// </param>
    /// <param name="quantity">
    /// The typed quantity.
    /// </param>
    [Theory]
    [InlineData("", "1")]
    [InlineData("abc", "1")]
    [InlineData("0", "1")]
    [InlineData("5", "")]
    [InlineData("5", "-2")]
    public async Task Save_InvalidValues_SavesNothingAndExplains(string cost, string quantity)
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        h.Holding.Begin(Btc);
        h.Holding.CostText = cost;
        h.Holding.QuantityText = quantity;

        await h.Holding.SaveCommand.ExecuteAsync(null);

        h.Store.Stored!.Watchlist.Single().HasHolding.Should().BeFalse();
        h.Holding.HasMessage.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that the editor shows a saved holding, and that clearing removes it.
    /// </summary>
    [Fact]
    public async Task Clear_SavedHolding_RemovesIt()
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.Watchlist[0].CostPrice = 3m;
        settings.Watchlist[0].Quantity = 4m;
        settings.Watchlist[0].HoldingCurrency = "HKD";
        await using var h = new Harness(settings);

        h.Holding.Begin(Btc);

        h.Holding.CostText.Should().Be("3");
        h.Holding.QuantityText.Should().Be("4");
        h.Holding.SelectedCurrency.Should().Be("HKD");
        h.Holding.CanClear.Should().BeTrue();

        await h.Holding.ClearCommand.ExecuteAsync(null);

        var entry = h.Store.Stored!.Watchlist.Single();
        entry.CostPrice.Should().BeNull();
        entry.Quantity.Should().BeNull();
        entry.HoldingCurrency.Should().BeNull();
    }

    /// <summary>
    /// Verifies that a form for an instrument without a holding starts empty with the currency of its data source.
    /// </summary>
    [Fact]
    public async Task Begin_NoHolding_StartsEmptyWithGuessedCurrency()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));

        h.Holding.Begin(Btc);

        h.Holding.CostText.Should().BeEmpty();
        h.Holding.CanClear.Should().BeFalse();
        h.Holding.SelectedCurrency.Should().Be("USD");
    }

    /// <summary>
    /// Verifies that an empty cost price starts with the current price, and that a saved cost price is kept.
    /// </summary>
    [Fact]
    public async Task Begin_WithCurrentPrice_FillsOnlyAnEmptyCost()
    {
        var settings = Harness.WatchlistOf(Btc, Aapl);
        settings.Watchlist[1].CostPrice = 3m;
        settings.Watchlist[1].Quantity = 4m;
        await using var h = new Harness(settings);

        h.Holding.Begin(Btc, 123.45m);
        h.Holding.CostText.Should().Be("123.45");
        h.Holding.QuantityText.Should().BeEmpty();

        h.Holding.Begin(Aapl, 123.45m);
        h.Holding.CostText.Should().Be("3");

        h.Holding.Begin(Btc, null);
        h.Holding.CostText.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that the window is titled with the name of the instrument and not its symbol.
    /// </summary>
    [Fact]
    public async Task Begin_Subject_TitlesTheWindowWithTheName()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));

        h.Holding.Begin(Btc);

        h.Holding.Title.Should().Contain("Bitcoin").And.NotContain("BTC");
    }

    /// <summary>
    /// Verifies that the cost price prefilled in a comma culture can be edited in the number box.
    /// </summary>
    [Fact]
    public async Task Begin_CommaCulture_PrefilledCostIsAcceptedByTheNumberBox()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));

        TestCulture.Under("de-DE", () =>
        {
            h.Holding.Begin(Btc, 12.5m);

            h.Holding.CostText.Should().Be("12,5");
            NumberText.IsPartialNumber(h.Holding.CostText).Should().BeTrue();
        });
    }

    /// <summary>
    /// Verifies that the cost price is suggested from a live price only, not from one that stopped coming in.
    /// </summary>
    [Fact]
    public async Task EditHolding_StalePrice_SuggestsNoCost()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();
        var row = h.Main.Rows.Single();
        row.Update(new AssetSnapshot(Btc, new Quote(Btc.Id, 110m, 100m, DateTimeOffset.UnixEpoch), [100m, 110m], AssetHealth.Live));

        h.Main.EditHolding(row);
        h.Holding.CostText.Should().Be("110");

        row.Update(new AssetSnapshot(Btc, new Quote(Btc.Id, 110m, 100m, DateTimeOffset.UnixEpoch), [100m, 110m], AssetHealth.Stale));
        h.Main.EditHolding(row);

        h.Holding.CostText.Should().BeEmpty();
    }
}
