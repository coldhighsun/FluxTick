namespace FluxTick.App.Controls;

/// <summary>
/// Chooses the price range a sparkline shows when alert levels are drawn on it.
/// </summary>
public static class SparklineScale
{
    /// <summary>
    /// How far beyond the prices, in times the span of the prices, the chart grows to show an alert level. A level
    /// further away is not shown at its place, because the line would shrink to a flat stroke.
    /// </summary>
    public const double Reach = 1.0;

    /// <summary>
    /// How far a flat series reaches, as a share of its price, as it has no span to measure by.
    /// </summary>
    private const double FlatReach = 0.02;

    /// <summary>
    /// Computes the range the chart shows.
    /// </summary>
    /// <param name="min">
    /// The lowest price.
    /// </param>
    /// <param name="max">
    /// The highest price.
    /// </param>
    /// <param name="levels">
    /// The alert levels.
    /// </param>
    /// <returns>
    /// The prices of the bottom and the top of the chart: the range of the prices, grown to include each level that is
    /// within <see cref="Reach"/> of it.
    /// </returns>
    public static (double Bottom, double Top) Range(double min, double max, IEnumerable<double> levels)
    {
        var span = max - min;
        var reach = (span > 0 ? span : Math.Abs(max) * FlatReach) * Reach;
        var bottom = min;
        var top = max;
        foreach (var level in levels)
        {
            if (level > max && level - max <= reach)
            {
                top = Math.Max(top, level);
            }
            else if (level < min && min - level <= reach)
            {
                bottom = Math.Min(bottom, level);
            }
        }

        return (bottom, top);
    }

    /// <summary>
    /// Computes where a price lies in the chart.
    /// </summary>
    /// <param name="price">
    /// The price.
    /// </param>
    /// <param name="bottom">
    /// The price at the bottom of the chart.
    /// </param>
    /// <param name="top">
    /// The price at the top of the chart.
    /// </param>
    /// <returns>
    /// The position between 0 (bottom) and 1 (top), and whether the price is outside the chart and so was moved to its
    /// edge. A chart without a span puts everything in the middle.
    /// </returns>
    public static (double Position, bool Clamped) Place(double price, double bottom, double top)
    {
        if (top <= bottom)
        {
            return (0.5, false);
        }

        var position = (price - bottom) / (top - bottom);
        return position is < 0 or > 1 ? (Math.Clamp(position, 0, 1), true) : (position, false);
    }
}
