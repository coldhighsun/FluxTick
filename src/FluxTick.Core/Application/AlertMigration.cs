using FluxTick.Abstractions;

namespace FluxTick.Core.Application;

/// <summary>
/// Moves the alerts of an instrument along with it when it changes its data source. An instrument has at most one alert
/// per condition, so the alerts that are moved must not clash with those the new source already has.
/// </summary>
public static class AlertMigration
{
    /// <summary>
    /// Finds the conditions that both the instrument that moves and the one it moves to have an alert for. The alerts
    /// cannot be moved while there are any; one of each pair has to be deleted first.
    /// </summary>
    /// <param name="alerts">
    /// All alerts.
    /// </param>
    /// <param name="from">
    /// The instrument at the old data source.
    /// </param>
    /// <param name="to">
    /// The instrument at the new data source.
    /// </param>
    /// <returns>
    /// The conditions that clash, in the order of the enumeration; empty when the alerts can be moved.
    /// </returns>
    public static IReadOnlyList<AlertCondition> ConditionsInConflict(IEnumerable<AlertRule> alerts, AssetId from, AssetId to)
    {
        if (from == to)
        {
            return [];
        }

        var list = alerts as IReadOnlyCollection<AlertRule> ?? [.. alerts];
        var moving = list.Where(a => a.AssetId == from).Select(a => a.Condition).ToHashSet();
        return [.. list.Where(a => a.AssetId == to && moving.Contains(a.Condition)).Select(a => a.Condition).Distinct().Order()];
    }

    /// <summary>
    /// Gives the alerts of an instrument to the instrument it moves to.
    /// </summary>
    /// <param name="alerts">
    /// All alerts; the ones of the instrument that moves are changed.
    /// </param>
    /// <param name="from">
    /// The instrument at the old data source.
    /// </param>
    /// <param name="to">
    /// The instrument at the new data source.
    /// </param>
    /// <returns>
    /// How many alerts were moved.
    /// </returns>
    public static int Move(IEnumerable<AlertRule> alerts, AssetId from, AssetId to)
    {
        var moved = 0;
        foreach (var alert in alerts.Where(a => a.AssetId == from).ToList())
        {
            alert.ProviderId = to.ProviderId;
            alert.Symbol = to.Symbol;
            moved++;
        }

        return moved;
    }
}
