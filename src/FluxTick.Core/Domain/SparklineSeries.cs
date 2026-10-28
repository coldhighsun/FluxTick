using System.Collections.ObjectModel;
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
    /// The start of each bucket in UTC ticks, parallel to <see cref="_prices"/>.
    /// </summary>
    private readonly List<long> _starts = [];

    /// <summary>
    /// The start of the newest bucket, in UTC ticks.
    /// </summary>
    private long _lastBucketStart = long.MinValue;

    /// <summary>
    /// The times returned by <see cref="Times"/> last, kept while the bucket starts and the window start did not change.
    /// </summary>
    private ReadOnlyCollection<DateTimeOffset>? _times;

    /// <summary>
    /// The index of the first point inside the window that <see cref="_times"/> was made for.
    /// </summary>
    private int _timesFirst;

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
    /// Gets the length of the window the series shows: the capacity times the bucket width.
    /// </summary>
    public TimeSpan Window => _bucket * _capacity;

    /// <summary>
    /// Gets the width of one bucket.
    /// </summary>
    public TimeSpan Bucket => _bucket;

    /// <summary>
    /// Replaces the content with historical samples, oldest first. Later live ticks continue from the newest sample.
    /// </summary>
    /// <param name="history">
    /// The samples to seed from; unordered input is sorted.
    /// </param>
    public void Seed(IEnumerable<PricePoint> history)
    {
        Clear();
        foreach (var point in history.OrderBy(p => p.Timestamp))
        {
            Add(point.Price, point.Timestamp);
        }
    }

    /// <summary>
    /// Drops every point and the state that goes with them.
    /// </summary>
    private void Clear()
    {
        _prices.Clear();
        _starts.Clear();
        _times = null;
        _lastBucketStart = long.MinValue;
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
        _starts.Add(start);
        _times = null;
        _lastBucketStart = start;
        if (_prices.Count > _capacity)
        {
            _prices.RemoveAt(0);
            _starts.RemoveAt(0);
        }
    }

    /// <summary>
    /// Creates a copy of the points inside the window that ends at <paramref name="now"/>; the window is as long as
    /// the capacity times the bucket width, so older points that were kept are left out.
    /// </summary>
    /// <param name="now">
    /// The end of the window.
    /// </param>
    /// <returns>
    /// The prices in the window, oldest first; empty when none is recent enough.
    /// </returns>
    public IReadOnlyList<decimal> Snapshot(DateTimeOffset now)
    {
        var first = FirstInWindow(now);
        return first == 0 ? _prices.ToArray() : _prices.GetRange(first, _prices.Count - first).ToArray();
    }

    /// <summary>
    /// Determines whether any point is inside the window that ends at <paramref name="now"/>.
    /// </summary>
    /// <param name="now">
    /// The end of the window.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <see cref="Snapshot"/> would return a point.
    /// </returns>
    public bool AnyWithin(DateTimeOffset now) => _starts.Count > 0 && _starts[^1] > OldestIncluded(now);

    /// <summary>
    /// Creates a copy of the start times of the points <see cref="Snapshot"/> returns for the same time.
    /// </summary>
    /// <param name="now">
    /// The end of the window.
    /// </param>
    /// <returns>
    /// The start of the bucket of each point, oldest first. The list is shared until the points or the window move on.
    /// </returns>
    public IReadOnlyList<DateTimeOffset> Times(DateTimeOffset now)
    {
        // A price that replaces the last one of its bucket leaves the starts as they are, so most quotes of a busy source
        // get the array made before.
        var first = FirstInWindow(now);
        if (_times is not null && _timesFirst == first)
        {
            return _times;
        }

        var times = new DateTimeOffset[_starts.Count - first];
        for (var i = 0; i < times.Length; i++)
        {
            times[i] = new DateTimeOffset(_starts[first + i], TimeSpan.Zero);
        }

        // Shared by every snapshot until the points move on, so it is wrapped: a cast cannot change it.
        _times = Array.AsReadOnly(times);
        _timesFirst = first;
        return _times;
    }

    /// <summary>
    /// Finds the index of the oldest point inside the window that ends at <paramref name="now"/>.
    /// </summary>
    /// <param name="now">
    /// The end of the window.
    /// </param>
    /// <returns>
    /// The index, or the point count when every point is older than the window.
    /// </returns>
    private int FirstInWindow(DateTimeOffset now)
    {
        var oldest = OldestIncluded(now);
        var first = 0;
        while (first < _starts.Count && _starts[first] <= oldest)
        {
            first++;
        }

        return first;
    }

    /// <summary>
    /// Computes the start of the window that ends at <paramref name="now"/>. A point in a bucket that starts at, or before
    /// it is outside the window.
    /// </summary>
    /// <param name="now">
    /// The end of the window.
    /// </param>
    /// <returns>
    /// The start of the window, in UTC ticks.
    /// </returns>
    private long OldestIncluded(DateTimeOffset now) => now.UtcTicks - Window.Ticks;
}
