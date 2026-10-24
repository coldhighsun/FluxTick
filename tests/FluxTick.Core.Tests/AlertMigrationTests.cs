using FluxTick.Abstractions;
using FluxTick.Core.Application;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for <see cref="AlertMigration"/>.
/// </summary>
public class AlertMigrationTests
{
    /// <summary>
    /// The instrument at the old data source.
    /// </summary>
    private static readonly AssetId Old = new("old", "BTC");

    /// <summary>
    /// The instrument at the new data source.
    /// </summary>
    private static readonly AssetId New = new("new", "BTC");

    /// <summary>
    /// Builds an alert.
    /// </summary>
    /// <param name="id">
    /// The id of the alert.
    /// </param>
    /// <param name="asset">
    /// The instrument.
    /// </param>
    /// <param name="condition">
    /// The condition.
    /// </param>
    /// <returns>
    /// The alert.
    /// </returns>
    private static AlertRule Alert(string id, AssetId asset, AlertCondition condition) =>
        new() { Id = id, ProviderId = asset.ProviderId, Symbol = asset.Symbol, Condition = condition, Threshold = 10 };

    /// <summary>
    /// Verifies the alerts clash when both instruments have one for the same condition, and only for those conditions.
    /// </summary>
    [Fact]
    public void ConditionsInConflict_SameConditionOnBoth_ReportsOnlyThatCondition()
    {
        List<AlertRule> alerts =
        [
            Alert("1", Old, AlertCondition.PriceAbove),
            Alert("2", Old, AlertCondition.PriceBelow),
            Alert("3", New, AlertCondition.PriceBelow),
        ];

        AlertMigration.ConditionsInConflict(alerts, Old, New).Should().Equal(AlertCondition.PriceBelow);
    }

    /// <summary>
    /// Verifies alerts of other instruments, and conditions only one side has, do not clash.
    /// </summary>
    [Fact]
    public void ConditionsInConflict_NoCommonCondition_IsEmpty()
    {
        List<AlertRule> alerts =
        [
            Alert("1", Old, AlertCondition.PriceAbove),
            Alert("2", New, AlertCondition.PriceBelow),
            Alert("3", new AssetId("other", "ETH"), AlertCondition.PriceAbove),
        ];

        AlertMigration.ConditionsInConflict(alerts, Old, New).Should().BeEmpty();
        AlertMigration.ConditionsInConflict(alerts, Old, Old).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the alerts of the instrument that moves go to the new one, and the others stay.
    /// </summary>
    [Fact]
    public void Move_ChangesOnlyTheAlertsOfTheInstrument()
    {
        List<AlertRule> alerts =
        [
            Alert("1", Old, AlertCondition.PriceAbove),
            Alert("2", Old, AlertCondition.PriceBelow),
            Alert("3", new AssetId("other", "ETH"), AlertCondition.PriceAbove),
        ];

        var moved = AlertMigration.Move(alerts, Old, New);

        moved.Should().Be(2);
        alerts.Select(a => a.AssetId).Should().Equal(New, New, new AssetId("other", "ETH"));
    }
}
