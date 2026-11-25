using FluxTick.Abstractions;

namespace FluxTick.Core.Domain;

/// <summary>
/// Thins out stored prices by their age. The chart of a range shows one point per bucket of that range, so a price only
/// needs the resolution of the finest range that can still show it: the last hour keeps the 10 seconds of the live
/// chart, and older prices are cut down to the bucket of the day, 5 days, month and year ranges in turn. Buckets are
/// aligned like those of <see cref="SparklineSeries"/>, and each bucket keeps its last price.
/// </summary>
public static class QuoteHistoryCompactor
{
    /// <summary>
    /// The age tiers, youngest first: prices up to <c>MaxAge</c> old are kept at <c>Bucket</c> resolution. Each bound
    /// is a little beyond the window of the range it serves, so that the moment of compaction never leaves a hole at the
    /// edge of a window; each bucket divides the next one, so that compacting again is a no-op.
    /// </summary>
    private static readonly (TimeSpan MaxAge, TimeSpan Bucket)[] Tiers =
    [
        (TimeSpan.FromMinutes(90), TimeSpan.FromSeconds(10)),
        (TimeSpan.FromHours(30), TimeSpan.FromMinutes(5)),
        (TimeSpan.FromDays(6), TimeSpan.FromMinutes(30)),
        (TimeSpan.FromDays(32), TimeSpan.FromHours(4)),
    ];

    /// <summary>
    /// The bucket of prices older than every tier.
    /// </summary>
    private static readonly TimeSpan OldestBucket = TimeSpan.FromDays(1);

    /// <summary>
    /// Gets the width of the finest bucket, which is also how often the store takes a price down.
    /// </summary>
    public static TimeSpan FinestBucket => Tiers[0].Bucket;

    /// <summary>
    /// Gets the bucket a price of the given age is kept at.
    /// </summary>
    /// <param name="age">
    /// How long ago the price was traded; zero or negative for a price that is not in the past.
    /// </param>
    /// <returns>
    /// The width of the bucket.
    /// </returns>
    public static TimeSpan BucketFor(TimeSpan age)
    {
        foreach (var (maxAge, bucket) in Tiers)
        {
            if (age <= maxAge)
            {
                return bucket;
            }
        }

        return OldestBucket;
    }

    /// <summary>
    /// Sorts prices, drops those older than the retention and keeps only the last price of each bucket of its age.
    /// </summary>
    /// <param name="points">
    /// The prices, in any order. Of two prices at the same time, the later one in the sequence wins.
    /// </param>
    /// <param name="now">
    /// The time the age of a price is measured against.
    /// </param>
    /// <param name="retention">
    /// How long a price is kept.
    /// </param>
    /// <returns>
    /// The remaining prices, oldest first.
    /// </returns>
    public static List<PricePoint> Compact(IEnumerable<PricePoint> points, DateTimeOffset now, TimeSpan retention)
    {
        var result = new List<PricePoint>();
        var lastBucket = TimeSpan.Zero;
        var lastKey = long.MinValue;
        foreach (var point in points.OrderBy(p => p.Timestamp))
        {
            var age = now - point.Timestamp;
            if (age > retention)
            {
                continue;
            }

            var bucket = BucketFor(age);
            var key = point.Timestamp.UtcTicks / bucket.Ticks;
            if (result.Count > 0 && bucket == lastBucket && key == lastKey)
            {
                result[^1] = point;
                continue;
            }

            result.Add(point);
            lastBucket = bucket;
            lastKey = key;
        }

        return result;
    }
}
