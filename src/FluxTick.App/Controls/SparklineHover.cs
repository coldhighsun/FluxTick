using System.Globalization;
using FluxTick.Abstractions;

namespace FluxTick.App.Controls;

/// <summary>
/// The pointer over a chart: which price it is at, and how the time of that price is written.
/// </summary>
public static class SparklineHover
{
    /// <summary>
    /// Finds the price nearest to a place along the width of the chart, laid out the way the chart draws it.
    /// </summary>
    /// <param name="x">
    /// The place of the pointer, from the left edge of the control.
    /// </param>
    /// <param name="width">
    /// The width of the chart without the space around it.
    /// </param>
    /// <param name="pad">
    /// The space kept free at the left edge.
    /// </param>
    /// <param name="times">
    /// When each price was traded, oldest first.
    /// </param>
    /// <param name="asOf">
    /// The end of the window.
    /// </param>
    /// <param name="window">
    /// The length of the window.
    /// </param>
    /// <returns>
    /// The index of the nearest price, or -1 when there are none.
    /// </returns>
    public static int IndexAt(double x, double width, double pad, IReadOnlyList<DateTimeOffset> times, DateTimeOffset asOf, TimeSpan window)
    {
        var best = -1;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < times.Count; i++)
        {
            var distance = Math.Abs(pad + (width * SparklineScale.TimeX(times[i], asOf, window)) - x);

            // On a tie the later price wins, as prices pile up at the right edge when they are older than the window.
            if (distance <= bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// Writes the time of a price the way the range needs it: the clock for a short range (with the date when it is not
    /// today), the date and the clock for five days and the date alone for the longer ones. A point of a month stands for
    /// four hours, whose start need not be a time the market traded at, so it has no clock. A point of a half year or a year
    /// stands for a day that starts at midnight UTC, so that date is written as it is: moved to a zone west of UTC it would
    /// be the day before.
    /// </summary>
    /// <param name="time">
    /// When the price was traded.
    /// </param>
    /// <param name="range">
    /// The range the chart shows.
    /// </param>
    /// <param name="now">
    /// The current time.
    /// </param>
    /// <param name="zone">
    /// The time zone to write the time in.
    /// </param>
    /// <returns>
    /// The text.
    /// </returns>
    public static string TimeLabel(DateTimeOffset time, HistoryRange range, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (range is HistoryRange.HalfYear or HistoryRange.Year)
        {
            return time.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var local = TimeZoneInfo.ConvertTime(time, zone);
        var format = range switch
        {
            HistoryRange.Month => "MM-dd",
            HistoryRange.FiveDays => "MM-dd HH:mm",
            _ => local.Date == TimeZoneInfo.ConvertTime(now, zone).Date ? "HH:mm" : "MM-dd HH:mm",
        };
        return local.ToString(format, CultureInfo.InvariantCulture);
    }
}
