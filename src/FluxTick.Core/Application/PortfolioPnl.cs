namespace FluxTick.Core.Application;

/// <summary>
/// The total of all holdings in one currency.
/// </summary>
/// <param name="CostTotal">
/// What the holdings cost.
/// </param>
/// <param name="MarketValue">
/// What the holdings are worth.
/// </param>
/// <param name="Amount">
/// The profit, or the loss when negative.
/// </param>
/// <param name="Percent">
/// The profit as a percentage of the cost.
/// </param>
/// <param name="Counted">
/// The number of holdings in the total.
/// </param>
/// <param name="Skipped">
/// The number of holdings left out because there is no exchange rate for their currency.
/// </param>
public readonly record struct PortfolioPnl(decimal CostTotal, decimal MarketValue, decimal Amount, decimal Percent, int Counted, int Skipped)
{
    /// <summary>
    /// Adds up holdings, converting each to the base currency.
    /// </summary>
    /// <param name="holdings">
    /// The figures of each holding with the currency they are in.
    /// </param>
    /// <param name="baseCurrency">
    /// The currency of the total.
    /// </param>
    /// <param name="convert">
    /// Converts an amount from one currency to another, returning <see langword="null"/> when no rate is known.
    /// </param>
    /// <returns>
    /// The total, or <see langword="null"/> when there is no holding to count.
    /// </returns>
    public static PortfolioPnl? Sum(
        IReadOnlyList<(HoldingPnl Pnl, string Currency)> holdings,
        string baseCurrency,
        Func<decimal, string, string, decimal?> convert)
    {
        var cost = 0m;
        var value = 0m;
        var counted = 0;
        var skipped = 0;
        for (var i = 0; i < holdings.Count; i++)
        {
            var (pnl, currency) = holdings[i];
            if (convert(pnl.CostTotal, currency, baseCurrency) is not { } c || convert(pnl.MarketValue, currency, baseCurrency) is not { } v)
            {
                skipped++;
                continue;
            }

            cost += c;
            value += v;
            counted++;
        }

        if (counted == 0 && skipped == 0)
        {
            return null;
        }

        var amount = value - cost;
        return new PortfolioPnl(cost, value, amount, cost == 0 ? 0m : amount / cost * 100m, counted, skipped);
    }
}
