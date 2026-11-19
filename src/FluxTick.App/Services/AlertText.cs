using System.Globalization;
using FluxTick.Abstractions;
using FluxTick.App.Localization;
using FluxTick.Core.Application;

namespace FluxTick.App.Services;

/// <summary>
/// The words of an alert in the language of the interface.
/// </summary>
public static class AlertText
{
    /// <summary>
    /// Describes the condition of a rule.
    /// </summary>
    /// <param name="rule">
    /// The rule.
    /// </param>
    /// <returns>
    /// The condition and threshold, for example "Price above 100".
    /// </returns>
    public static string Summary(AlertRule rule)
    {
        var number = rule.Threshold.ToString("0.########", CultureInfo.InvariantCulture);
        return rule.Condition switch
        {
            // The label of a change condition ends in "(%)", which a sentence with the number does not need.
            AlertCondition.ChangePercentAbove => Loc.Get("Alert_SummaryChangeAbove", number),
            AlertCondition.ChangePercentBelow => Loc.Get("Alert_SummaryChangeBelow", number),
            _ => Loc.Get("Alert_Summary", EnumLabel.Of(rule.Condition), number),
        };
    }

    /// <summary>
    /// Describes what a rule waits for, as the tooltip of an instrument tells it.
    /// </summary>
    /// <param name="rule">
    /// The rule.
    /// </param>
    /// <param name="assetClass">
    /// The class of the instrument, which decides how a price is written.
    /// </param>
    /// <returns>
    /// The sentence, for example "Alert when the price rises to 100".
    /// </returns>
    public static string Detail(AlertRule rule, AssetClass assetClass)
    {
        var percent = rule.Threshold.ToString("0.########", CultureInfo.InvariantCulture);
        return rule.Condition switch
        {
            AlertCondition.PriceAbove => Loc.Get("Row_AlertAbove", PriceFormatter.FormatPrice(rule.Threshold, assetClass)),
            AlertCondition.PriceBelow => Loc.Get("Row_AlertBelow", PriceFormatter.FormatPrice(rule.Threshold, assetClass)),
            AlertCondition.ChangePercentAbove => Loc.Get("Row_AlertChangeAbove", percent),
            AlertCondition.ChangePercentBelow => Loc.Get("Row_AlertChangeBelow", percent),
            _ => Summary(rule),
        };
    }

    /// <summary>
    /// Describes the rules of an instrument, one line for each.
    /// </summary>
    /// <param name="rules">
    /// The rules of the instrument.
    /// </param>
    /// <param name="assetClass">
    /// The class of the instrument.
    /// </param>
    /// <returns>
    /// The lines, in the order of the conditions, or an empty text when there are no rules.
    /// </returns>
    public static string Details(IEnumerable<AlertRule> rules, AssetClass assetClass) =>
        string.Join('\n', rules.OrderBy(r => r.Condition).Select(r => Detail(r, assetClass)));

    /// <summary>
    /// Gets the title of the notification of an alert that fired.
    /// </summary>
    /// <param name="e">
    /// The alert.
    /// </param>
    /// <returns>
    /// The title.
    /// </returns>
    public static string Title(AlertTriggeredEventArgs e) => Loc.Get("Alert_ToastTitle", e.Asset.DisplayName);

    /// <summary>
    /// Gets the text of the notification of an alert that fired: what was set, and the price now.
    /// </summary>
    /// <param name="e">
    /// The alert.
    /// </param>
    /// <returns>
    /// The text, in two lines.
    /// </returns>
    public static string Body(AlertTriggeredEventArgs e) =>
        $"{Summary(e.Rule)}\n{Loc.Get("Alert_ToastNow", PriceFormatter.FormatPrice(e.Quote.Price, e.Asset.AssetClass))}";
}
