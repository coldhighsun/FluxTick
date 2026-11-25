namespace FluxTick.App.ViewModels;

/// <summary>
/// The times the settings offer for keeping stored prices; the value of each is the number of days.
/// </summary>
public enum HistoryRetention
{
    /// <summary>
    /// A month.
    /// </summary>
    Month = 30,

    /// <summary>
    /// Three months.
    /// </summary>
    Quarter = 90,

    /// <summary>
    /// Half a year.
    /// </summary>
    HalfYear = 180,

    /// <summary>
    /// A year, which is as far back as the longest chart reaches.
    /// </summary>
    Year = 365,

    /// <summary>
    /// Two years.
    /// </summary>
    TwoYears = 730,

    /// <summary>
    /// Five years.
    /// </summary>
    FiveYears = 1825,
}

/// <summary>
/// Maps between the days kept in the settings file and the choices of <see cref="HistoryRetention"/>.
/// </summary>
public static class HistoryRetentionChoices
{
    /// <summary>
    /// Finds the choice that is closest to a number of days, as the file may hold any number.
    /// </summary>
    /// <param name="days">
    /// The days kept in the settings.
    /// </param>
    /// <returns>
    /// The choice with the least difference; the longer one when two are equally close.
    /// </returns>
    public static HistoryRetention Nearest(int days) =>
        Enum.GetValues<HistoryRetention>()
            .OrderBy(choice => Math.Abs((int)choice - days))
            .ThenByDescending(choice => (int)choice)
            .First();
}
