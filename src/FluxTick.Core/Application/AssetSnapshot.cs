using FluxTick.Abstractions;

namespace FluxTick.Core.Application;

/// <summary>
/// How trustworthy the displayed price of an instrument currently is.
/// </summary>
public enum AssetHealth
{
    /// <summary>
    /// No quote received yet.
    /// </summary>
    Waiting,

    /// <summary>
    /// Fresh data is arriving.
    /// </summary>
    Live,

    /// <summary>
    /// The last quote is older than the staleness threshold.
    /// </summary>
    Stale,

    /// <summary>
    /// The provider lost its source and is retrying.
    /// </summary>
    Reconnecting,

    /// <summary>
    /// The provider has been reconnecting for longer than the staleness threshold, so what it last delivered is old.
    /// </summary>
    StaleReconnecting,

    /// <summary>
    /// The provider reports a failure that needs attention.
    /// </summary>
    Failed,

    /// <summary>
    /// The provider is switched off in settings.
    /// </summary>
    ProviderDisabled,

    /// <summary>
    /// The provider is not installed in this build.
    /// </summary>
    ProviderMissing,
}

/// <summary>
/// When the prices of a sparkline were traded, so that they can be drawn along a time axis.
/// </summary>
public sealed class SparklineTimeline
{
    /// <summary>
    /// How long two neighbouring prices may be apart, at least, before the stretch between them counts as without prices.
    /// </summary>
    private static readonly TimeSpan MinGap = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How many times the usual distance between prices may pass before the stretch counts as without prices, so that a
    /// source that is polled slowly is not drawn as one long gap.
    /// </summary>
    private const double GapFactor = 2.5;

    /// <summary>
    /// How many of the latest distances between prices tell the usual one.
    /// </summary>
    private const int Recent = 2;

    /// <summary>
    /// Initializes a new instance of the <see cref="SparklineTimeline"/> class.
    /// </summary>
    /// <param name="times">
    /// The start time of the bucket of each price, oldest first.
    /// </param>
    /// <param name="asOf">
    /// The end of the window.
    /// </param>
    /// <param name="window">
    /// The length of the window.
    /// </param>
    public SparklineTimeline(IReadOnlyList<DateTimeOffset> times, DateTimeOffset asOf, TimeSpan window)
    {
        Times = times;
        AsOf = asOf;
        Window = window;
        GapAfter = GapAfterOf(times);
    }

    /// <summary>
    /// Gets the start time of the bucket of each price, oldest first; as many as the prices.
    /// </summary>
    public IReadOnlyList<DateTimeOffset> Times { get; }

    /// <summary>
    /// Gets the end of the window, which is the time the snapshot was built.
    /// </summary>
    public DateTimeOffset AsOf { get; }

    /// <summary>
    /// Gets the length of the window; it ends at <see cref="AsOf"/>.
    /// </summary>
    public TimeSpan Window { get; }

    /// <summary>
    /// Gets how far two neighbouring prices, or the last price and <see cref="AsOf"/>, have to be apart for the stretch
    /// between them to count as without prices: more than <see cref="MinGap"/>, and more than a few times the usual
    /// distance between the latest prices. It is <see cref="TimeSpan.MaxValue"/> while there are not two prices to take
    /// a distance from, so that nothing counts as a gap yet.
    /// </summary>
    public TimeSpan GapAfter { get; }

    /// <summary>
    /// Determines whether the stretch between two times is too long to count as covered by prices.
    /// </summary>
    /// <param name="previous">
    /// The earlier time.
    /// </param>
    /// <param name="next">
    /// The later time.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when they are more than <see cref="GapAfter"/> apart.
    /// </returns>
    public bool IsGap(DateTimeOffset previous, DateTimeOffset next) => next - previous > GapAfter;

    /// <summary>
    /// Computes <see cref="GapAfter"/> from the times of the prices.
    /// </summary>
    /// <param name="times">
    /// When the prices were traded, oldest first.
    /// </param>
    /// <returns>
    /// The distance beyond which there is a gap.
    /// </returns>
    private static TimeSpan GapAfterOf(IReadOnlyList<DateTimeOffset> times)
    {
        if (times.Count < 2)
        {
            return TimeSpan.MaxValue;
        }

        // The smaller of the latest distances: the usual one of a source whose pace changed (a history a minute apart,
        // then slow live polls) is the one it has now, and a single long stretch must not raise the bar for itself.
        var usual = TimeSpan.MaxValue;
        for (var i = Math.Max(1, times.Count - Recent); i < times.Count; i++)
        {
            var distance = times[i] - times[i - 1];
            usual = distance < usual ? distance : usual;
        }

        return TimeSpan.FromTicks(Math.Max(MinGap.Ticks, (long)(usual.Ticks * GapFactor)));
    }
}

/// <summary>
/// An immutable view of one tracked instrument.
/// </summary>
/// <param name="Asset">
/// The instrument.
/// </param>
/// <param name="Quote">
/// The latest quote, or <see langword="null"/> when none arrived yet.
/// </param>
/// <param name="Sparkline">
/// The recent prices, oldest first.
/// </param>
/// <param name="Health">
/// The data health.
/// </param>
/// <param name="HealthDetail">
/// Optional detail about the health, such as the last provider error.
/// </param>
/// <param name="Timeline">
/// When the sparkline prices were traded, or <see langword="null"/> when that is not known.
/// </param>
/// <param name="MarketState">
/// Whether the market of the instrument is trading; independent of the health, which says how fresh the data is.
/// </param>
public sealed record AssetSnapshot(
    Asset Asset,
    Quote? Quote,
    IReadOnlyList<decimal> Sparkline,
    AssetHealth Health,
    string? HealthDetail = null,
    SparklineTimeline? Timeline = null,
    MarketState MarketState = MarketState.Unknown);
