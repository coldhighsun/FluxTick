using FluxTick.Abstractions;

namespace FluxTick.Core.Application;

/// <summary>
/// Decides whether a quote satisfies an alert rule. Pure, so that the rules of an alert can be tested without a
/// market.
/// </summary>
public static class AlertEvaluator
{
    /// <summary>
    /// Determines whether a quote satisfies the condition of a rule. The bounds are exclusive: a price equal to the
    /// threshold is neither above nor below it.
    /// </summary>
    /// <param name="rule">
    /// The rule.
    /// </param>
    /// <param name="quote">
    /// The quote of the instrument the rule watches.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the condition holds.
    /// </returns>
    public static bool IsSatisfied(AlertRule rule, Quote quote) => rule.Condition switch
    {
        AlertCondition.PriceAbove => quote.Price > rule.Threshold,
        AlertCondition.PriceBelow => quote.Price < rule.Threshold,
        _ => false,
    };

    /// <summary>
    /// Determines whether a rule fires: when its condition holds now and did not hold at the previous evaluation. The
    /// first evaluation of a rule only records the state, so a condition that is already true when the rule is created
    /// or the app starts does not fire.
    /// </summary>
    /// <param name="previous">
    /// Whether the condition held at the previous evaluation, or <see langword="null"/> when there was none.
    /// </param>
    /// <param name="satisfied">
    /// Whether the condition holds now.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the rule fires.
    /// </returns>
    public static bool Fires(bool? previous, bool satisfied) => satisfied && previous == false;
}
