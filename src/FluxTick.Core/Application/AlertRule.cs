using System.Text.Json.Serialization;
using FluxTick.Abstractions;

namespace FluxTick.Core.Application;

/// <summary>
/// What an <see cref="AlertRule"/> compares a quote against.
/// </summary>
public enum AlertCondition
{
    /// <summary>
    /// The price is above the threshold.
    /// </summary>
    PriceAbove,

    /// <summary>
    /// The price is below the threshold.
    /// </summary>
    PriceBelow,

    /// <summary>
    /// The change since the reference price is above the threshold, a percentage.
    /// </summary>
    ChangePercentAbove,

    /// <summary>
    /// The change since the reference price is below minus the threshold, a percentage.
    /// </summary>
    ChangePercentBelow,
}

/// <summary>
/// Facts about an <see cref="AlertCondition"/>.
/// </summary>
public static class AlertConditionExtensions
{
    /// <summary>
    /// Determines whether the threshold of a condition is a percentage rather than a price.
    /// </summary>
    /// <param name="condition">
    /// The condition.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for the conditions on the change in percent.
    /// </returns>
    public static bool IsPercent(this AlertCondition condition) =>
        condition is AlertCondition.ChangePercentAbove or AlertCondition.ChangePercentBelow;

    /// <summary>
    /// Determines whether a condition fires when the price goes up.
    /// </summary>
    /// <param name="condition">
    /// The condition.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for the conditions that watch for a rise.
    /// </returns>
    public static bool IsUpward(this AlertCondition condition) =>
        condition is AlertCondition.PriceAbove or AlertCondition.ChangePercentAbove;
}

/// <summary>
/// A persisted price alert on one watched instrument.
/// </summary>
public sealed class AlertRule
{
    /// <summary>
    /// Gets or sets the stable identifier of the rule.
    /// </summary>
    public string Id { get; set; } = Identifier.New();

    /// <summary>
    /// Gets or sets the provider that serves the instrument.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the provider-native symbol.
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets what is compared with the threshold.
    /// </summary>
    public AlertCondition Condition { get; set; } = AlertCondition.PriceAbove;

    /// <summary>
    /// Gets or sets the threshold: a price, or a percentage for the conditions on the change.
    /// </summary>
    public decimal Threshold { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the rule is watched.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the rule fires again after the condition stopped and came true once more.
    /// Otherwise, the rule disables itself after it fired.
    /// </summary>
    public bool Repeat { get; set; }

    /// <summary>
    /// Gets the instrument the rule watches. It is worked out from the provider and the symbol and not saved.
    /// </summary>
    [JsonIgnore]
    public AssetId AssetId => new(ProviderId, Symbol);

    /// <summary>
    /// Creates a copy of the rule.
    /// </summary>
    /// <returns>
    /// The copy.
    /// </returns>
    public AlertRule Clone() => new()
    {
        Id = Id,
        ProviderId = ProviderId,
        Symbol = Symbol,
        Condition = Condition,
        Threshold = Threshold,
        Enabled = Enabled,
        Repeat = Repeat,
    };

    /// <summary>
    /// Determines whether another rule is the same rule with the same settings.
    /// </summary>
    /// <param name="other">
    /// The other rule.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when every value is the same.
    /// </returns>
    public bool SameAs(AlertRule? other) =>
        other is not null
        && Id == other.Id
        && ProviderId == other.ProviderId
        && Symbol == other.Symbol
        && Condition == other.Condition
        && Threshold == other.Threshold
        && Enabled == other.Enabled
        && Repeat == other.Repeat;

    /// <summary>
    /// Determines whether a threshold can be used.
    /// </summary>
    /// <param name="threshold">
    /// The threshold.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the threshold is a price or a percentage, which must be positive.
    /// </returns>
    public static bool IsValidThreshold(decimal threshold) => threshold > 0;
}
