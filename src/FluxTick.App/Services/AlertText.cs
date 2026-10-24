using System.Globalization;
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
    public static string Summary(AlertRule rule) =>
        Loc.Get("Alert_Summary", EnumLabel.Of(rule.Condition), rule.Threshold.ToString("0.########", CultureInfo.InvariantCulture));

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
