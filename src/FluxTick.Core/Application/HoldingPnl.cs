using System.Diagnostics.CodeAnalysis;

namespace FluxTick.Core.Application;

/// <summary>
/// The figures of a holding at the current price, all in the currency of the holding.
/// </summary>
/// <param name="CostTotal">
/// What the holding cost: cost price times quantity.
/// </param>
/// <param name="MarketValue">
/// What the holding is worth: price times quantity.
/// </param>
/// <param name="Amount">
/// The profit, or the loss when negative.
/// </param>
/// <param name="Percent">
/// The profit as a percentage of the cost, for example <c>12.5</c> for 12.5 %.
/// </param>
public readonly record struct HoldingPnl(decimal CostTotal, decimal MarketValue, decimal Amount, decimal Percent)
{
    /// <summary>
    /// Determines whether a cost price and a quantity make a holding: both are known and above zero. This is the one
    /// place that says so.
    /// </summary>
    /// <param name="costPrice">
    /// The price paid per unit.
    /// </param>
    /// <param name="quantity">
    /// The number of units held.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when they make a holding.
    /// </returns>
    public static bool IsUsable([NotNullWhen(true)] decimal? costPrice, [NotNullWhen(true)] decimal? quantity) => costPrice is > 0 && quantity is > 0;

    /// <summary>
    /// Computes the figures of a holding.
    /// </summary>
    /// <param name="price">
    /// The current price, or <see langword="null"/> when there is no quote yet.
    /// </param>
    /// <param name="costPrice">
    /// The price paid per unit.
    /// </param>
    /// <param name="quantity">
    /// The number of units held.
    /// </param>
    /// <returns>
    /// The figures, or <see langword="null"/> when the price is missing or the holding is not usable.
    /// </returns>
    public static HoldingPnl? Compute(decimal? price, decimal? costPrice, decimal? quantity)
    {
        if (price is not { } current || !IsUsable(costPrice, quantity))
        {
            return null;
        }

        var cost = costPrice.Value * quantity.Value;
        var value = current * quantity.Value;
        var amount = value - cost;
        return new HoldingPnl(cost, value, amount, amount / cost * 100m);
    }
}
