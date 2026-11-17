namespace FluxTick.Abstractions;

/// <summary>
/// The time span a sparkline shows.
/// </summary>
public enum HistoryRange
{
    /// <summary>
    /// The last hour, one point per minute. The only range every source supports.
    /// </summary>
    Hour,

    /// <summary>
    /// The last day, one point per five minutes.
    /// </summary>
    Day,

    /// <summary>
    /// The last five days, one point per half hour.
    /// </summary>
    FiveDays,

    /// <summary>
    /// The last month, one point per four hours.
    /// </summary>
    Month,

    /// <summary>
    /// The last half year, one point per day.
    /// </summary>
    HalfYear,

    /// <summary>
    /// The last year, one point per day.
    /// </summary>
    Year,
}

/// <summary>
/// The shape of the history of a <see cref="HistoryRange" />.
/// </summary>
/// <param name="Bucket">
/// The time one point stands for.
/// </param>
/// <param name="Capacity">
/// The number of points the range holds.
/// </param>
public readonly record struct HistoryRangeSpec(TimeSpan Bucket, int Capacity)
{
    /// <summary>
    /// Gets the time the range covers.
    /// </summary>
    public TimeSpan Window => Bucket * Capacity;

    /// <summary>
    /// Gets the shape of a range.
    /// </summary>
    /// <param name="range">
    /// The range.
    /// </param>
    /// <returns>
    /// The bucket and capacity of the range.
    /// </returns>
    public static HistoryRangeSpec For(HistoryRange range) => range switch
    {
        HistoryRange.Day => new HistoryRangeSpec(TimeSpan.FromMinutes(5), 288),
        HistoryRange.FiveDays => new HistoryRangeSpec(TimeSpan.FromMinutes(30), 240),
        HistoryRange.Month => new HistoryRangeSpec(TimeSpan.FromHours(4), 180),
        HistoryRange.HalfYear => new HistoryRangeSpec(TimeSpan.FromDays(1), 183),
        HistoryRange.Year => new HistoryRangeSpec(TimeSpan.FromDays(1), 365),
        _ => new HistoryRangeSpec(TimeSpan.FromMinutes(1), 60),
    };
}
