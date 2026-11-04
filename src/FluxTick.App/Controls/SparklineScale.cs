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
    public static (double Bottom, double Top) Range(double min, double max, IReadOnlyList<double> levels)
    {
        var span = max - min;
        var reach = (span > 0 ? span : Math.Abs(max) * FlatReach) * Reach;
        var bottom = min;
        var top = max;
        for (var i = 0; i < levels.Count; i++)
        {
            var level = levels[i];
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
    /// Finds the ticks of the percentage axis: the multiples of a step that lie in the range the chart shows, with the
    /// change measured from <paramref name="baseline"/>. The step is at least <paramref name="minStep"/> and grows
    /// (5, 10, 25, 50, 100, 250, ...) until no more than <paramref name="maxTicks"/> ticks are left.
    /// </summary>
    /// <param name="bottom">
    /// The price at the bottom of the chart.
    /// </param>
    /// <param name="top">
    /// The price at the top of the chart.
    /// </param>
    /// <param name="baseline">
    /// The price that stands for 0%, which is the first price of the window.
    /// </param>
    /// <param name="minStep">
    /// The smallest step between two ticks, in percent.
    /// </param>
    /// <param name="maxTicks">
    /// The most ticks to return.
    /// </param>
    /// <returns>
    /// The ticks from the bottom up: the change in percent and where it lies in the chart (0 at the bottom, 1 at the top);
    /// none when the baseline is not positive or the chart has no span.
    /// </returns>
    public static IReadOnlyList<(double Percent, double Position)> PercentTicks(double bottom, double top, double baseline, double minStep = 5, int maxTicks = 4)
    {
        if (baseline <= 0 || top <= bottom || minStep <= 0 || maxTicks < 1)
        {
            return [];
        }

        var low = ((bottom / baseline) - 1) * 100;
        var high = ((top / baseline) - 1) * 100;
        var step = minStep;
        var growth = 0;
        while (Math.Floor(high / step) - Math.Ceiling(low / step) + 1 > maxTicks)
        {
            step *= growth++ % 3 == 1 ? 2.5 : 2;
        }

        List<(double, double)> ticks = [];
        for (var n = Math.Ceiling(low / step); n * step <= high; n++)
        {
            var percent = n * step;
            ticks.Add((percent, Place(baseline * (1 + (percent / 100)), bottom, top).Position));
        }

        return ticks;
    }

    /// <summary>
    /// Writes a tick of the percentage axis.
    /// </summary>
    /// <param name="percent">
    /// The change in percent.
    /// </param>
    /// <returns>
    /// The label: a sign for a change, none for zero, such as <c>+5%</c>, <c>0%</c> and <c>-12.5%</c>.
    /// </returns>
    public static string PercentLabel(double percent) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{percent:+0.#;-0.#;0}%");

    /// <summary>
    /// Computes where a time lies along the width of the chart, which shows the window that ends at <paramref name="asOf"/>.
    /// </summary>
    /// <param name="time">
    /// The time.
    /// </param>
    /// <param name="asOf">
    /// The end of the window.
    /// </param>
    /// <param name="window">
    /// The length of the window.
    /// </param>
    /// <returns>
    /// The position between 0 (the start of the window) and 1 (its end); a time outside the window is moved to the edge.
    /// </returns>
    public static double TimeX(DateTimeOffset time, DateTimeOffset asOf, TimeSpan window) =>
        window <= TimeSpan.Zero ? 1 : Math.Clamp(1 - (asOf - time) / window, 0, 1);

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
