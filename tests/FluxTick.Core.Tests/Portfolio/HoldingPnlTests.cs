using FluxTick.Core.Application;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for the profit and loss of a holding and for the total of all holdings.
/// </summary>
public class HoldingPnlTests
{
    /// <summary>
    /// Rates against the US dollar used by the tests: one dollar buys seven yuan.
    /// </summary>
    private static readonly Dictionary<string, decimal> Rates = new() { ["USD"] = 1m, ["CNY"] = 7m };

    /// <summary>
    /// Converts an amount with the test rates.
    /// </summary>
    /// <param name="amount">
    /// The amount.
    /// </param>
    /// <param name="from">
    /// The currency of the amount.
    /// </param>
    /// <param name="to">
    /// The currency to convert to.
    /// </param>
    /// <returns>
    /// The converted amount, or <see langword="null"/> when a currency has no rate.
    /// </returns>
    private static decimal? Convert(decimal amount, string from, string to) =>
        Rates.TryGetValue(from, out var fromRate) && Rates.TryGetValue(to, out var toRate) ? amount / fromRate * toRate : null;

    /// <summary>
    /// Verifies the profit figures of a holding that gained.
    /// </summary>
    [Fact]
    public void Compute_PriceAboveCost_ReturnsProfit()
    {
        var pnl = HoldingPnl.Compute(12m, 10m, 100m);

        pnl.Should().Be(new HoldingPnl(1000m, 1200m, 200m, 20m));
    }

    /// <summary>
    /// Verifies the loss figures of a holding that lost.
    /// </summary>
    [Fact]
    public void Compute_PriceBelowCost_ReturnsNegativeAmountAndPercent()
    {
        var pnl = HoldingPnl.Compute(9m, 10m, 10m);

        pnl!.Value.Amount.Should().Be(-10m);
        pnl.Value.Percent.Should().Be(-10m);
    }

    /// <summary>
    /// Verifies that nothing is computed without a price or a usable holding.
    /// </summary>
    /// <param name="price">
    /// The price.
    /// </param>
    /// <param name="cost">
    /// The cost price.
    /// </param>
    /// <param name="quantity">
    /// The quantity.
    /// </param>
    [Theory]
    [InlineData(null, 10, 1)]
    [InlineData(10, null, 1)]
    [InlineData(10, 0, 1)]
    [InlineData(10, 10, null)]
    [InlineData(10, 10, 0)]
    [InlineData(10, -1, 1)]
    public void Compute_MissingOrInvalidInput_ReturnsNull(int? price, int? cost, int? quantity)
    {
        var pnl = HoldingPnl.Compute((decimal?)price, (decimal?)cost, (decimal?)quantity);

        pnl.Should().BeNull();
    }

    /// <summary>
    /// Verifies the one rule that says what a holding is.
    /// </summary>
    /// <param name="cost">
    /// The cost price.
    /// </param>
    /// <param name="quantity">
    /// The quantity.
    /// </param>
    /// <param name="expected">
    /// Whether they make a holding.
    /// </param>
    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, false)]
    [InlineData(null, 1, false)]
    [InlineData(1, null, false)]
    public void IsUsable_CostAndQuantity_RequiresBothAboveZero(int? cost, int? quantity, bool expected)
    {
        HoldingPnl.IsUsable(cost, quantity).Should().Be(expected);
    }

    /// <summary>
    /// Verifies that holdings in different currencies are added up in the base currency.
    /// </summary>
    [Fact]
    public void Sum_MixedCurrencies_ConvertsToBase()
    {
        var holdings = new (HoldingPnl, string)[]
        {
            (new HoldingPnl(70m, 77m, 7m, 10m), "CNY"),
            (new HoldingPnl(10m, 20m, 10m, 100m), "USD"),
        };

        var total = PortfolioPnl.Sum(holdings, "USD", Convert);

        total!.Value.CostTotal.Should().Be(20m);
        total.Value.MarketValue.Should().Be(31m);
        total.Value.Amount.Should().Be(11m);
        total.Value.Percent.Should().Be(55m);
        total.Value.Skipped.Should().Be(0);
    }

    /// <summary>
    /// Verifies that a holding whose currency has no rate is left out and counted.
    /// </summary>
    [Fact]
    public void Sum_UnknownCurrency_IsSkipped()
    {
        var holdings = new (HoldingPnl, string)[]
        {
            (new HoldingPnl(10m, 20m, 10m, 100m), "USD"),
            (new HoldingPnl(10m, 20m, 10m, 100m), "XXX"),
        };

        var total = PortfolioPnl.Sum(holdings, "USD", Convert);

        total!.Value.Counted.Should().Be(1);
        total.Value.Skipped.Should().Be(1);
    }

    /// <summary>
    /// Verifies that there is no total without holdings.
    /// </summary>
    [Fact]
    public void Sum_NoHoldings_ReturnsNull()
    {
        var total = PortfolioPnl.Sum([], "USD", (amount, _, _) => amount);

        total.Should().BeNull();
    }
}
