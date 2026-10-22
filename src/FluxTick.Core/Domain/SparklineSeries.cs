using FluxTick.Abstractions;

namespace FluxTick.Core.Domain;

/// <summary>
/// A fixed-capacity, time-bucketed price series for a sparkline. Each bucket keeps the last price seen in it,
/// so a flood of ticks never grows the series beyond <c>capacity</c> points. Not thread-safe.
/// </summary>
public sealed class SparklineSeries
{
    /// <summary>
    /// The maximum number of buckets kept.
    /// </summary>
    private readonly int _capacity;

    /// <summary>
    /// The width of one bucket.
    /// </summary>
    private readonly TimeSpan _bucket;

    /// <summary>
    /// The bucket prices, oldest first.
    /// </summary>
    private readonly List<decimal> _prices = [];

    /// <summary>
    /// The start of the newest bucket, in UTC ticks.
    /// </summary>
    private long _lastBucketStart = long.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="SparklineSeries"/> class.
    /// </summary>
    /// <param name="capacity">
    /// The maximum number of points kept; must be at least 2.
    /// </param>
    /// <param name="bucket">
    /// The width of one bucket; must be positive.
    /// </param>
    public SparklineSeries(int capacity, TimeSpan bucket)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bucket, TimeSpan.Zero);
        _capacity = capacity;
        _bucket = bucket;
    }

    /// <summary>
    /// Gets the number of points currently held.
    /// </summary>
    public int Count => _prices.Count;

    /// <summary>
    /// Replaces the content with historical samples, oldest first. Later live ticks continue from the newest sample.
    /// </summary>
    /// <param name="history">
    /// The samples to seed from; unordered input is sorted.
    /// </param>
    public void Seed(IEnumerable<PricePoint> history)
    {
        _prices.Clear();
        _lastBucketStart = long.MinValue;
        foreach (var point in history.OrderBy(p => p.Timestamp))
        {
            Add(point.Price, point.Timestamp);
        }
    }

    /// <summary>
    /// Records a price. A price in the newest bucket replaces its value, a later bucket appends,
    /// and a price older than the newest bucket is ignored.
    /// </summary>
    /// <param name="price">
    /// The price to record.
    /// </param>
    /// <param name="timestamp">
    /// When the price was observed.
    /// </param>
    public void Add(decimal price, DateTimeOffset timestamp)
    {
        var start = timestamp.UtcTicks / _bucket.Ticks * _bucket.Ticks;
        if (start < _lastBucketStart)
        {
            return;
        }

        if (start == _lastBucketStart && _prices.Count > 0)
        {
            _prices[^1] = price;
            return;
        }

        _prices.Add(price);
        _lastBucketStart = start;
        if (_prices.Count > _capacity)
        {
            _prices.RemoveAt(0);
        }
    }

    /// <summary>
    /// Creates a copy of the current points.
    /// </summary>
    /// <returns>
    /// The prices, oldest first.
    /// </returns>
    public IReadOnlyList<decimal> Snapshot() => _prices.ToArray();
}
