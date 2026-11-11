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
    /// Nothing was received (judged by the receive time) for longer than the source may stay silent, which is the usual
    /// staleness threshold or, for a polled source, its poll interval (a streaming source's quiet-stream poll interval
    /// as well) plus half of the threshold; or what keeps arriving was traded (judged by the trade time) more than 30
    /// minutes ago, which a lagging feed does not explain.
    /// </summary>
    Stale,

    /// <summary>
    /// The source still answers, but the latest price was traded longer ago than the staleness threshold (for a polled
    /// source, its poll interval plus half of the threshold, if longer) and no more than 30 minutes ago: a thin market
    /// or a lagging feed.
    /// </summary>
    Delayed,

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
    /// How long two neighboring prices may be apart, at least, before the stretch between them counts as without prices.
    /// </summary>
    private static readonly TimeSpan MinGap = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How many times the usual distance between prices may pass before the stretch counts as without prices, so that a
    /// source that is polled slowly is not drawn as one long gap.
    /// </summary>
    private const double GapFactor = 2.5;

    /// <summary>
    /// How many buckets of a bucket longer than a minute may pass between two prices before it counts as a gap.
    /// </summary>
    private const int BucketsWithoutGap = 4;

    /// <summary>
    /// How many times between two prices are looked at to see whether the market was shut.
    /// </summary>
    private const int ShutSamples = 12;

    /// <summary>
    /// The indexes of the prices that come after a stretch without prices in which the market was shut, or
    /// <see langword="null"/> when there are none or it is not known.
    /// </summary>
    private readonly HashSet<int>? _shutGaps;

    /// <summary>
    /// Whether the market was shut for most of the stretch between the last price and the end of the window, for a range
    /// longer than the last hour whose hours are known.
    /// </summary>
    private readonly bool _trailingShut;

    /// <summary>
    /// The share of the window that no price may cover before it counts as a gap, for a window of up to a day and for a
    /// longer one: a night, a weekend or a holiday of a market is part of the chart of a long range, not a lost stretch
    /// of it, and would otherwise break the line again and again.
    /// </summary>
    private const int ShortWindowGapShare = 8;

    /// <summary>
    /// The share of a window longer than a day that no price may cover before it counts as a gap.
    /// </summary>
    private const int LongWindowGapShare = 4;

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
    /// <param name="bucket">
    /// The time one price stands for, or <see langword="null"/> for a minute. Prices of a longer bucket may be a few
    /// buckets apart without a gap, as a night or a weekend of a market is not a lost stretch of the chart.
    /// </param>
    /// <param name="range">
    /// The range whose history the prices are, so that the chart of the range starts at the first of them, or
    /// <see langword="null"/> for the last hour, and for a source that has no longer range, whose last hour only stands where
    /// it was traded.
    /// </param>
    /// <param name="isMarketShut">
    /// Tells whether the market of the instrument is shut at a time, or <see langword="null"/> when that is not known. A
    /// gap in which the market was shut is not a lack of prices (see <see cref="IsGapAt"/>).
    /// </param>
    /// <param name="shutGapMemo">
    /// Remembers the stretches found shut for the last prices, so that a snapshot built for the same prices does not look at
    /// the hours of the market again; <see langword="null"/> to look every time.
    /// </param>
    public SparklineTimeline(
        IReadOnlyList<DateTimeOffset> times,
        DateTimeOffset asOf,
        TimeSpan window,
        TimeSpan? bucket = null,
        HistoryRange? range = null,
        Func<DateTimeOffset, bool>? isMarketShut = null,
        ShutGapMemo? shutGapMemo = null)
    {
        // The rows work out the change and the labels of the chart from the range alone, so a timeline that names a range
        // must have the window of that range.
        if (range is { } named && named != HistoryRange.Hour && window != HistoryRangeSpec.For(named).Window)
        {
            throw new ArgumentException($"The window {window} is not the window of the range {named}.", nameof(window));
        }

        Times = times;
        AsOf = asOf;
        Window = window;
        Range = range;
        GapAfter = GapAfterOf(times, bucket is { } b && b > TimeSpan.FromMinutes(1) ? MinGapOf(b, window) : MinGap);
        if (isMarketShut is null)
        {
            return;
        }

        var priceBucket = bucket ?? TimeSpan.FromMinutes(1);
        if (shutGapMemo is not null && shutGapMemo.TryGet(times, window, priceBucket, out var remembered))
        {
            _shutGaps = remembered;
        }
        else
        {
            _shutGaps = ShutGapsOf(times, GapAfter, priceBucket, isMarketShut);
            shutGapMemo?.Store(times, window, priceBucket, _shutGaps);
        }

        // The stretch after the last price grows with the clock, so it is looked at every time, and only when it is long
        // enough to be a gap at all.
        _trailingShut = CoversRange && times.Count > 0 && IsGap(times[^1], asOf) && IsMostlyShut(times[^1] + priceBucket, asOf, isMarketShut);
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
    /// Gets the range whose history the prices are, or <see langword="null"/> when they are the last hour (or the last hour
    /// of a source that has no longer range). The first price is the oldest one the source holds for the window, which may
    /// lie after its start; nothing is known before it.
    /// </summary>
    public HistoryRange? Range { get; }

    /// <summary>
    /// Gets a value indicating whether the prices are the history of a range longer than the last hour.
    /// </summary>
    public bool CoversRange => Range is { } range && range != HistoryRange.Hour;

    /// <summary>
    /// Gets how far two neighboring prices, or the last price and <see cref="AsOf"/>, have to be apart for the stretch
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
    /// Determines whether there is a stretch without prices before a price that the chart shows as one. A stretch in
    /// which the market was shut, such as a night, a weekend or a midday break, is not one: nothing was traded, and the
    /// prices around it are joined by the line.
    /// </summary>
    /// <param name="index">
    /// The index of the price after the stretch.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the price before it is too far back and the market was not shut in between.
    /// </returns>
    public bool IsGapAt(int index) =>
        index > 0 && index < Times.Count && IsGap(Times[index - 1], Times[index]) && _shutGaps?.Contains(index) != true;

    /// <summary>
    /// Gets a value indicating whether the last price is too far back from the end of the window to be the current one,
    /// unless the market was shut since (the close of a stock, a weekend), which is not a lack of prices.
    /// </summary>
    public bool HasTrailingGap => Times.Count > 0 && IsGap(Times[^1], AsOf) && !_trailingShut;

    /// <summary>
    /// Finds the stretches without prices that the shut market explains: those in which the market was shut at most of the
    /// times looked at between the end of one price and the next.
    /// </summary>
    /// <param name="times">
    /// When the prices were traded, oldest first.
    /// </param>
    /// <param name="gapAfter">
    /// The distance beyond which there is a stretch without prices.
    /// </param>
    /// <param name="bucket">
    /// The time one price stands for.
    /// </param>
    /// <param name="isMarketShut">
    /// Tells whether the market is shut at a time.
    /// </param>
    /// <returns>
    /// The index of the price after each such stretch; <see langword="null"/> when there is none.
    /// </returns>
    private static HashSet<int>? ShutGapsOf(IReadOnlyList<DateTimeOffset> times, TimeSpan gapAfter, TimeSpan bucket, Func<DateTimeOffset, bool> isMarketShut)
    {
        HashSet<int>? shut = null;
        for (var i = 1; i < times.Count; i++)
        {
            var from = times[i - 1] + bucket;
            var to = times[i];
            if (to - times[i - 1] <= gapAfter || from >= to)
            {
                continue;
            }

            if (IsMostlyShut(from, to, isMarketShut))
            {
                (shut ??= []).Add(i);
            }
        }

        return shut;
    }

    /// <summary>
    /// Tells whether the market was shut at most of the times looked at in a stretch.
    /// </summary>
    /// <param name="from">
    /// The start of the stretch.
    /// </param>
    /// <param name="to">
    /// The end of the stretch.
    /// </param>
    /// <param name="isMarketShut">
    /// Tells whether the market is shut at a time.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the stretch is not empty and the market was shut for at least three quarters of the
    /// times looked at.
    /// </returns>
    private static bool IsMostlyShut(DateTimeOffset from, DateTimeOffset to, Func<DateTimeOffset, bool> isMarketShut)
    {
        if (from >= to)
        {
            return false;
        }

        // A fixed number of samples, so that a stretch shorter than the count in ticks cannot make the step zero.
        var span = to - from;
        var closed = 0;
        for (var n = 0; n < ShutSamples; n++)
        {
            closed += isMarketShut(from + (span * n / ShutSamples)) ? 1 : 0;
        }

        // Most of the times, not all: the last price of a session is dated a little before its end by some sources.
        return closed * 4 >= ShutSamples * 3;
    }

    /// <summary>
    /// Computes the least distance that counts as a gap for prices of a bucket longer than a minute: a few buckets, and a
    /// share of the window so that the nights and weekends of a market do not break the line of a long range.
    /// </summary>
    /// <param name="bucket">
    /// The time one price stands for.
    /// </param>
    /// <param name="window">
    /// The length of the window.
    /// </param>
    /// <returns>
    /// The least distance.
    /// </returns>
    private static TimeSpan MinGapOf(TimeSpan bucket, TimeSpan window)
    {
        var share = window <= TimeSpan.FromDays(1) ? window / ShortWindowGapShare : window / LongWindowGapShare;
        var buckets = bucket * BucketsWithoutGap;
        return share > buckets ? share : buckets;
    }

    /// <summary>
    /// Computes <see cref="GapAfter"/> from the times of the prices.
    /// </summary>
    /// <param name="times">
    /// When the prices were traded, oldest first.
    /// </param>
    /// <param name="minGap">
    /// The least distance that counts as a gap.
    /// </param>
    /// <returns>
    /// The distance beyond which there is a gap.
    /// </returns>
    private static TimeSpan GapAfterOf(IReadOnlyList<DateTimeOffset> times, TimeSpan minGap)
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

        return TimeSpan.FromTicks(Math.Max(minGap.Ticks, (long)(usual.Ticks * GapFactor)));
    }
}

/// <summary>
/// Remembers which stretches without prices a shut market explains, for the last prices a timeline was built from. The
/// answer depends only on the times of the prices, the window and the bucket, and the series hands out the same array of
/// times until a price moves it, so most snapshots of a busy source can reuse it. Not thread-safe: the owner guards it.
/// </summary>
public sealed class ShutGapMemo
{
    /// <summary>
    /// The times the stored answer is for; compared by reference.
    /// </summary>
    private IReadOnlyList<DateTimeOffset>? _times;

    /// <summary>
    /// The window the stored answer is for.
    /// </summary>
    private TimeSpan _window;

    /// <summary>
    /// The bucket the stored answer is for.
    /// </summary>
    private TimeSpan _bucket;

    /// <summary>
    /// The stored answer.
    /// </summary>
    private HashSet<int>? _gaps;

    /// <summary>
    /// Looks up the answer for prices.
    /// </summary>
    /// <param name="times">
    /// The times of the prices.
    /// </param>
    /// <param name="window">
    /// The length of the window.
    /// </param>
    /// <param name="bucket">
    /// The time one price stands for.
    /// </param>
    /// <param name="gaps">
    /// The stored answer, which may be <see langword="null"/> for none.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the answer is stored for exactly these prices.
    /// </returns>
    internal bool TryGet(IReadOnlyList<DateTimeOffset> times, TimeSpan window, TimeSpan bucket, out HashSet<int>? gaps)
    {
        gaps = _gaps;
        return ReferenceEquals(_times, times) && _window == window && _bucket == bucket;
    }

    /// <summary>
    /// Stores the answer for prices.
    /// </summary>
    /// <param name="times">
    /// The times of the prices.
    /// </param>
    /// <param name="window">
    /// The length of the window.
    /// </param>
    /// <param name="bucket">
    /// The time one price stands for.
    /// </param>
    /// <param name="gaps">
    /// The answer, or <see langword="null"/> for none.
    /// </param>
    internal void Store(IReadOnlyList<DateTimeOffset> times, TimeSpan window, TimeSpan bucket, HashSet<int>? gaps)
    {
        _times = times;
        _window = window;
        _bucket = bucket;
        _gaps = gaps;
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
