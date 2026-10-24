using FluxTick.Abstractions;
using FluxTick.Core.Application;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for <see cref="AlertEvaluator"/> and the threshold rules of <see cref="AlertRule"/>.
/// </summary>
public class AlertEvaluatorTests
{
    /// <summary>
    /// Builds a quote.
    /// </summary>
    /// <param name="price">
    /// The price.
    /// </param>
    /// <param name="reference">
    /// The reference price.
    /// </param>
    /// <returns>
    /// The quote.
    /// </returns>
    private static Quote QuoteOf(decimal price, decimal? reference = null) =>
        new(new AssetId("crypto", "BTC"), price, reference, DateTimeOffset.UnixEpoch);

    /// <summary>
    /// Verifies the price conditions: above and below are strict, so a price equal to the threshold is neither.
    /// </summary>
    /// <param name="condition">
    /// The condition.
    /// </param>
    /// <param name="price">
    /// The price of the quote.
    /// </param>
    /// <param name="expected">
    /// Whether the condition holds.
    /// </param>
    [Theory]
    [InlineData(AlertCondition.PriceAbove, 99.99, false)]
    [InlineData(AlertCondition.PriceAbove, 100, false)]
    [InlineData(AlertCondition.PriceAbove, 100.01, true)]
    [InlineData(AlertCondition.PriceBelow, 100.01, false)]
    [InlineData(AlertCondition.PriceBelow, 100, false)]
    [InlineData(AlertCondition.PriceBelow, 99.99, true)]
    public void IsSatisfied_PriceCondition_IsStrict(AlertCondition condition, double price, bool expected)
    {
        var rule = new AlertRule { Condition = condition, Threshold = 100 };

        AlertEvaluator.IsSatisfied(rule, QuoteOf((decimal)price)).Should().Be(expected);
    }

    /// <summary>
    /// Verifies a rule is saved without the instrument id that is worked out from its provider and symbol, and loads back.
    /// </summary>
    [Fact]
    public void Serialization_DoesNotWriteTheComputedAssetId()
    {
        var settings = new AppSettings();
        settings.Alerts.Add(new AlertRule { Id = "r", ProviderId = "p", Symbol = "S", Threshold = 5 });

        var json = System.Text.Json.JsonSerializer.Serialize(settings, AppSettings.JsonOptions);
        var back = settings.Clone().Alerts.Single();

        json.Should().NotContain("assetId");
        back.SameAs(settings.Alerts.Single()).Should().BeTrue();
        back.AssetId.Should().Be(new AssetId("p", "S"));
    }

    /// <summary>
    /// Verifies a rule is the same as another only when every value is the same.
    /// </summary>
    [Fact]
    public void SameAs_ComparesEveryValue()
    {
        var rule = new AlertRule { Id = "r", ProviderId = "p", Symbol = "S", Threshold = 5 };

        rule.SameAs(rule.Clone()).Should().BeTrue();
        rule.SameAs(null).Should().BeFalse();
        foreach (var changed in new Action<AlertRule>[]
        {
            r => r.Id = "x", r => r.ProviderId = "x", r => r.Symbol = "x", r => r.Condition = AlertCondition.PriceBelow,
            r => r.Threshold = 6, r => r.Enabled = false, r => r.Repeat = true,
        })
        {
            var other = rule.Clone();
            changed(other);
            rule.SameAs(other).Should().BeFalse();
        }
    }

    /// <summary>
    /// Verifies a rule only fires on the change from not satisfied to satisfied.
    /// </summary>
    /// <param name="previous">
    /// Whether the condition held before.
    /// </param>
    /// <param name="satisfied">
    /// Whether it holds now.
    /// </param>
    /// <param name="expected">
    /// Whether the rule fires.
    /// </param>
    [Theory]
    [InlineData(null, true, false)]
    [InlineData(null, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public void Fires_OnlyOnRisingEdge(bool? previous, bool satisfied, bool expected) =>
        AlertEvaluator.Fires(previous, satisfied).Should().Be(expected);

    /// <summary>
    /// Verifies a threshold is a price, which must be positive.
    /// </summary>
    /// <param name="threshold">
    /// The threshold.
    /// </param>
    /// <param name="expected">
    /// Whether the threshold is valid.
    /// </param>
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void IsValidThreshold_MustBePositive(int threshold, bool expected) =>
        AlertRule.IsValidThreshold(threshold).Should().Be(expected);
}
