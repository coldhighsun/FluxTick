using FluxTick.Abstractions;

namespace FluxTick.Core.Domain;

/// <summary>
/// Tells whether the prices stored for a time range are enough to draw a chart of it, or whether the source has to be
/// asked for the part that is missing.
/// </summary>
public static class HistoryCoverage
{
    /// <summary>
    /// The shortest silence that counts as a hole; it is the same floor that decides when the live chart of a range has
    /// a hole to fill.
    /// </summary>
    private static readonly TimeSpan MinimumGap = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets the time without a price that counts as a hole in the chart of a range: twice its bucket, and not less than
    /// ten minutes.
    /// </summary>
    /// <param name="bucket">
    /// The time covered by one point of the chart.
    /// </param>
    /// <returns>
    /// The longest time between two prices that is not a hole.
    /// </returns>
    public static TimeSpan GapThreshold(TimeSpan bucket) => TimeSpan.FromTicks(Math.Max(MinimumGap.Ticks, bucket.Ticks * 2));

    /// <summary>
    /// Determines whether prices reach from the start of a time range to its end without a hole.
    /// </summary>
    /// <param name="points">
    /// The prices, oldest first.
    /// </param>
    /// <param name="from">
    /// The start of the range.
    /// </param>
    /// <param name="to">
    /// The end of the range.
    /// </param>
    /// <param name="gap">
    /// The longest time without a price that is still no hole; see <see cref="GapThreshold"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the first price is no later than <paramref name="gap"/> after the start, the last is no
    /// earlier than <paramref name="gap"/> before the end, and no two prices are further apart than <paramref name="gap"/>.
    /// </returns>
    public static bool IsComplete(IReadOnlyList<PricePoint> points, DateTimeOffset from, DateTimeOffset to, TimeSpan gap)
    {
        if (points.Count < 2 || points[0].Timestamp - from > gap || to - points[^1].Timestamp > gap)
        {
            return false;
        }

        for (var i = 1; i < points.Count; i++)
        {
            if (points[i].Timestamp - points[i - 1].Timestamp > gap)
            {
                return false;
            }
        }

        return true;
    }
}
