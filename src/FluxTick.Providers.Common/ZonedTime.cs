using System.Globalization;

namespace FluxTick.Providers.Common;

/// <summary>
/// Reads and builds times that a service gives as a wall clock of a time zone, with no offset: the candles of an
/// exchange are dated in the local time of the exchange.
/// </summary>
public static class ZonedTime
{
    /// <summary>
    /// Builds the instant of a wall clock time in a time zone.
    /// </summary>
    /// <param name="zone">
    /// The time zone, or <see langword="null"/> when the system does not know it.
    /// </param>
    /// <param name="fallbackOffset">
    /// The offset used when <paramref name="zone"/> is <see langword="null"/>.
    /// </param>
    /// <param name="year">
    /// The year.
    /// </param>
    /// <param name="month">
    /// The month.
    /// </param>
    /// <param name="day">
    /// The day.
    /// </param>
    /// <param name="hour">
    /// The hour.
    /// </param>
    /// <param name="minute">
    /// The minute.
    /// </param>
    /// <param name="second">
    /// The second.
    /// </param>
    /// <returns>
    /// The time with the offset the zone has at that wall clock time.
    /// </returns>
    public static DateTimeOffset At(TimeZoneInfo? zone, TimeSpan fallbackOffset, int year, int month, int day, int hour, int minute = 0, int second = 0)
    {
        // The zone is asked for its offset by wall clock time, the one place a DateTime is needed.
        var offset = zone?.GetUtcOffset(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified)) ?? fallbackOffset;
        return new DateTimeOffset(year, month, day, hour, minute, second, offset);
    }

    /// <summary>
    /// Reads a wall clock time text as a time in a time zone.
    /// </summary>
    /// <param name="text">
    /// The text.
    /// </param>
    /// <param name="formats">
    /// The formats the text may have; a format without a clock time gives <paramref name="dateOnlyClock"/>.
    /// </param>
    /// <param name="zone">
    /// The time zone, or <see langword="null"/> when the system does not know it.
    /// </param>
    /// <param name="fallbackOffset">
    /// The offset used when <paramref name="zone"/> is <see langword="null"/>.
    /// </param>
    /// <param name="dateOnlyClock">
    /// The hour of the day given to a text that holds a date only, for example the close of the market.
    /// </param>
    /// <param name="time">
    /// The time.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the text had one of the formats.
    /// </returns>
    public static bool TryParse(string text, string[] formats, TimeZoneInfo? zone, TimeSpan fallbackOffset, int dateOnlyClock, out DateTimeOffset time)
    {
        time = default;
        if (!DateTimeOffset.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var wall))
        {
            return false;
        }

        var hasClock = text.Contains(':', StringComparison.Ordinal);
        time = At(zone, fallbackOffset, wall.Year, wall.Month, wall.Day, hasClock ? wall.Hour : dateOnlyClock, hasClock ? wall.Minute : 0, hasClock ? wall.Second : 0);
        return true;
    }
}
