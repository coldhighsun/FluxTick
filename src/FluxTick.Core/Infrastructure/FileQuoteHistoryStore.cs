using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FluxTick.Abstractions;
using FluxTick.Core.Domain;
using Microsoft.Extensions.Logging;
using ThrottledLogging;

namespace FluxTick.Core.Infrastructure;

/// <summary>
/// Keeps the prices of each instrument in a small binary file of its own: <c>{directory}/{provider}/q-{symbol}.bin</c>.
/// A file is a sequence of 24-byte records (UTC time in Unix milliseconds, then the price as the four integers of a
/// decimal), so a write cut short is cut off by length alone. Prices are appended once their 10-second bucket is over;
/// once a day the files are thinned out by age and cleared of what is past the retention
/// (see <see cref="QuoteHistoryCompactor"/>), which keeps a year of a 24-hour market below 40 KB.
/// </summary>
/// <param name="directory">
/// The folder the files are kept in; it is created with the first write.
/// </param>
/// <param name="timeProvider">
/// The clock.
/// </param>
/// <param name="logger">
/// The logger.
/// </param>
public sealed class FileQuoteHistoryStore(string directory, TimeProvider timeProvider, ILogger<FileQuoteHistoryStore> logger)
    : IQuoteHistoryStore, IAsyncDisposable
{
    /// <summary>
    /// The size of one record in a file.
    /// </summary>
    private const int RecordSize = 24;

    /// <summary>
    /// The most prices waiting to be written; when the disk is unusable for long, the oldest are dropped.
    /// </summary>
    private const int MaxClosedBuffer = 100_000;

    /// <summary>
    /// The longest name of a file made from a symbol; a longer symbol is named by its hash.
    /// </summary>
    private const int MaxFileNameLength = 100;

    /// <summary>
    /// The shortest time between two log messages about a file that could not be written; a disk that is full or locked
    /// fails with every flush, for every instrument.
    /// </summary>
    private static readonly TimeSpan WriteFailureLogInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How often waiting prices are written.
    /// </summary>
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long after the first price the files are thinned out for the first time, so that it does not compete with the
    /// start of the application.
    /// </summary>
    private static readonly TimeSpan FirstCompactionDelay = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How often the files are thinned out.
    /// </summary>
    private static readonly TimeSpan CompactionInterval = TimeSpan.FromDays(1);

    /// <summary>
    /// The earliest time a record can have; it keeps a damaged record from becoming a date out of range.
    /// </summary>
    private static readonly DateTimeOffset EarliestTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Guards the price buffers.
    /// </summary>
    private readonly Lock _bufferLock = new();

    /// <summary>
    /// Lets only one operation on the files run at a time, so that a read never meets a file that is being replaced.
    /// </summary>
    private readonly SemaphoreSlim _fileGate = new(1, 1);

    /// <summary>
    /// The newest price of each instrument whose 10-second bucket is still open.
    /// </summary>
    private readonly Dictionary<AssetId, PricePoint> _open = [];

    /// <summary>
    /// The prices of buckets that are over and not written yet.
    /// </summary>
    private readonly List<(AssetId Id, PricePoint Point)> _closed = [];

    /// <summary>
    /// Stops the background loop.
    /// </summary>
    private readonly CancellationTokenSource _stop = new();

    /// <summary>
    /// Whether prices are stored and read.
    /// </summary>
    private volatile bool _enabled = true;

    /// <summary>
    /// How long a price is kept, in ticks.
    /// </summary>
    private long _retentionTicks = TimeSpan.FromDays(365).Ticks;

    /// <summary>
    /// The background loop; <see langword="null"/> until the first price comes in.
    /// </summary>
    private Task? _loop;

    /// <summary>
    /// Whether the store was disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Gets how long a price is kept.
    /// </summary>
    private TimeSpan Retention => TimeSpan.FromTicks(Interlocked.Read(ref _retentionTicks));

    /// <inheritdoc />
    public void Configure(bool enabled, TimeSpan retention)
    {
        Interlocked.Exchange(ref _retentionTicks, retention.Ticks);
        _enabled = enabled;
        if (!enabled)
        {
            lock (_bufferLock)
            {
                _open.Clear();
                _closed.Clear();
            }
        }
    }

    /// <inheritdoc />
    public void Record(AssetId assetId, decimal price, DateTimeOffset time)
    {
        if (!_enabled || _disposed)
        {
            return;
        }

        var bucket = QuoteHistoryCompactor.FinestBucket.Ticks;
        lock (_bufferLock)
        {
            if (_open.TryGetValue(assetId, out var current))
            {
                var currentStart = current.Timestamp.UtcTicks / bucket;
                var start = time.UtcTicks / bucket;
                if (start < currentStart)
                {
                    return;
                }

                if (start > currentStart)
                {
                    Close(assetId, current);
                }
            }

            _open[assetId] = new PricePoint(time, price);
        }

        EnsureLoop();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PricePoint>> ReadAsync(AssetId assetId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            return [];
        }

        await FlushAsync(includeOpen: false, cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        var points = new List<PricePoint>();
        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            points.AddRange(await ReadFileAsync(PathOf(assetId), cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _fileGate.Release();
        }

        lock (_bufferLock)
        {
            if (_open.TryGetValue(assetId, out var open))
            {
                points.Add(open);
            }
        }

        return QuoteHistoryCompactor.Compact(points, now, Retention)
            .Where(p => p.Timestamp >= from && p.Timestamp <= to)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task MergeAsync(AssetId assetId, IReadOnlyList<PricePoint> points, CancellationToken cancellationToken = default)
    {
        if (!_enabled || points.Count == 0)
        {
            return;
        }

        await FlushAsync(includeOpen: false, cancellationToken).ConfigureAwait(false);
        var path = PathOf(assetId);
        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stored = await ReadFileAsync(path, cancellationToken).ConfigureAwait(false);
            var merged = QuoteHistoryCompactor.Compact(points.Concat(stored), timeProvider.GetUtcNow(), Retention);
            await RewriteAsync(path, merged, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarningThrottled("history:write", WriteFailureLogInterval, ex, "Could not store history for {Asset}.", assetId);
        }
        finally
        {
            _fileGate.Release();
        }
    }

    /// <summary>
    /// Writes the prices that wait to be written.
    /// </summary>
    /// <param name="includeOpen">
    /// Whether the prices of buckets that are still open are written too, as when the store closes.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the write.
    /// </param>
    /// <returns>
    /// A task that completes when the prices are written; a file that cannot be written is logged and skipped.
    /// </returns>
    public async Task FlushAsync(bool includeOpen, CancellationToken cancellationToken = default)
    {
        (AssetId Id, PricePoint Point)[] batch;
        lock (_bufferLock)
        {
            if (includeOpen)
            {
                foreach (var (id, point) in _open)
                {
                    _closed.Add((id, point));
                }

                _open.Clear();
            }

            if (_closed.Count == 0)
            {
                return;
            }

            batch = [.. _closed];
            _closed.Clear();
        }

        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var group in batch.GroupBy(b => b.Id))
            {
                try
                {
                    await AppendAsync(PathOf(group.Key), group.Select(g => g.Point), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarningThrottled("history:write", WriteFailureLogInterval, ex, "Could not write history for {Asset}.", group.Key);
                }
            }
        }
        finally
        {
            _fileGate.Release();
        }
    }

    /// <summary>
    /// Thins out every file by the age of its prices and drops what is past the retention.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancels the work.
    /// </param>
    /// <returns>
    /// A task that completes when all files are done; a file that fails is logged and skipped.
    /// </returns>
    public async Task CompactAllAsync(CancellationToken cancellationToken = default)
    {
        if (!_enabled || !Directory.Exists(directory))
        {
            return;
        }

        await FlushAsync(includeOpen: false, cancellationToken).ConfigureAwait(false);
        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*.bin", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not list the history files.");
            return;
        }

        foreach (var file in files)
        {
            await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var stored = await ReadFileAsync(file, cancellationToken).ConfigureAwait(false);
                var compact = QuoteHistoryCompactor.Compact(stored, timeProvider.GetUtcNow(), Retention);
                if (compact.Count != stored.Count)
                {
                    await RewriteAsync(file, compact, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not compact {File}.", file);
            }
            finally
            {
                _fileGate.Release();
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            await _loop.ConfigureAwait(false);
        }

        if (_enabled)
        {
            await FlushAsync(includeOpen: true).ConfigureAwait(false);
        }

        _stop.Dispose();
        _fileGate.Dispose();
    }

    /// <summary>
    /// Moves the price of a bucket that is over to the ones waiting to be written. The caller holds the buffer lock.
    /// </summary>
    /// <param name="assetId">
    /// The instrument.
    /// </param>
    /// <param name="point">
    /// The last price of the bucket.
    /// </param>
    private void Close(AssetId assetId, PricePoint point)
    {
        if (_closed.Count >= MaxClosedBuffer)
        {
            _closed.RemoveAt(0);
        }

        _closed.Add((assetId, point));
    }

    /// <summary>
    /// Starts the background loop with the first price, so that creating the store does no work.
    /// </summary>
    private void EnsureLoop()
    {
        if (_loop is not null)
        {
            return;
        }

        lock (_bufferLock)
        {
            _loop ??= Task.Run(() => RunAsync(_stop.Token));
        }
    }

    /// <summary>
    /// Writes waiting prices on a timer and thins out the files once a day.
    /// </summary>
    /// <param name="cancellationToken">
    /// Stops the loop.
    /// </param>
    /// <returns>
    /// A task that completes when the loop is stopped.
    /// </returns>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(FlushInterval, timeProvider);
        var nextCompaction = timeProvider.GetUtcNow() + FirstCompactionDelay;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await FlushAsync(includeOpen: false, cancellationToken).ConfigureAwait(false);
                if (timeProvider.GetUtcNow() >= nextCompaction)
                {
                    await CompactAllAsync(cancellationToken).ConfigureAwait(false);
                    nextCompaction = timeProvider.GetUtcNow() + CompactionInterval;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The store is closing.
        }
    }

    /// <summary>
    /// Finds the file of an instrument.
    /// </summary>
    /// <param name="assetId">
    /// The instrument.
    /// </param>
    /// <returns>
    /// The full path.
    /// </returns>
    private string PathOf(AssetId assetId)
        => Path.Combine(directory, FileNamePart(assetId.ProviderId), $"q-{FileNamePart(assetId.Symbol)}.bin");

    /// <summary>
    /// Turns an identifier into text that is safe in a file name on every platform, including those whose file system
    /// ignores the case of letters: lower-case letters, digits, <c>-</c> and <c>_</c> stay, an upper-case letter becomes
    /// <c>~</c> and its lower-case form, and everything else becomes <c>%XX</c> per UTF-8 byte, so that two identifiers
    /// that differ only in case never share a file. A long result is replaced by a hash.
    /// </summary>
    /// <param name="value">
    /// The identifier.
    /// </param>
    /// <returns>
    /// The text to use in a file or folder name.
    /// </returns>
    internal static string FileNamePart(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            if (b is >= (byte)'A' and <= (byte)'Z')
            {
                builder.Append('~').Append((char)(b + ('a' - 'A')));
            }
            else if (b is (>= (byte)'a' and <= (byte)'z') or (>= (byte)'0' and <= (byte)'9') or (byte)'-' or (byte)'_')
            {
                builder.Append((char)b);
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        if (builder.Length <= MaxFileNameLength)
        {
            return builder.ToString();
        }

        return $"h-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..32]}";
    }

    /// <summary>
    /// Reads all records of a file; a record that is cut off or cannot be a price is skipped.
    /// </summary>
    /// <param name="path">
    /// The file.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the read.
    /// </param>
    /// <returns>
    /// The prices in file order; empty when the file is missing or cannot be read.
    /// </returns>
    private async Task<List<PricePoint>> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        var points = new List<PricePoint>();
        byte[] bytes;
        try
        {
            if (!File.Exists(path))
            {
                return points;
            }

            bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read {File}.", path);
            return points;
        }

        for (var offset = 0; offset + RecordSize <= bytes.Length; offset += RecordSize)
        {
            if (TryDecode(bytes.AsSpan(offset, RecordSize), out var point))
            {
                points.Add(point);
            }
        }

        return points;
    }

    /// <summary>
    /// Appends prices to a file, first cutting off a record that an earlier write left half done.
    /// </summary>
    /// <param name="path">
    /// The file; it and its folder are created when missing.
    /// </param>
    /// <param name="points">
    /// The prices to append.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the write.
    /// </param>
    /// <returns>
    /// A task that completes when the prices are written.
    /// </returns>
    private static async Task AppendAsync(string path, IEnumerable<PricePoint> points, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
        var whole = stream.Length / RecordSize * RecordSize;
        if (whole != stream.Length)
        {
            stream.SetLength(whole);
        }

        stream.Seek(0, SeekOrigin.End);
        await stream.WriteAsync(Encode(points), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces a file by the given prices through a temporary file, so that a crash leaves either the old or the new
    /// content. An empty list removes the file.
    /// </summary>
    /// <param name="path">
    /// The file.
    /// </param>
    /// <param name="points">
    /// The prices, oldest first.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the write.
    /// </param>
    /// <returns>
    /// A task that completes when the file is replaced.
    /// </returns>
    private static async Task RewriteAsync(string path, IReadOnlyList<PricePoint> points, CancellationToken cancellationToken)
    {
        if (points.Count == 0)
        {
            File.Delete(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await stream.WriteAsync(Encode(points), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(path))
        {
            File.Replace(temp, path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temp, path);
        }
    }

    /// <summary>
    /// Encodes prices as records.
    /// </summary>
    /// <param name="points">
    /// The prices.
    /// </param>
    /// <returns>
    /// The bytes, <see cref="RecordSize"/> per price.
    /// </returns>
    private static byte[] Encode(IEnumerable<PricePoint> points)
    {
        var list = points as IReadOnlyCollection<PricePoint> ?? [.. points];
        var bytes = new byte[list.Count * RecordSize];
        Span<int> bits = stackalloc int[4];
        var offset = 0;
        foreach (var point in list)
        {
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(offset), point.Timestamp.ToUnixTimeMilliseconds());
            decimal.GetBits(point.Price, bits);
            for (var i = 0; i < 4; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 8 + (i * 4)), bits[i]);
            }

            offset += RecordSize;
        }

        return bytes;
    }

    /// <summary>
    /// Decodes one record.
    /// </summary>
    /// <param name="record">
    /// The <see cref="RecordSize"/> bytes of the record.
    /// </param>
    /// <param name="point">
    /// The price, when the record is valid.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the record holds a time and a price that can be used.
    /// </returns>
    private static bool TryDecode(ReadOnlySpan<byte> record, out PricePoint point)
    {
        point = default;
        var millis = BinaryPrimitives.ReadInt64LittleEndian(record);
        if (millis < EarliestTime.ToUnixTimeMilliseconds() || millis > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
        {
            return false;
        }

        Span<int> bits = stackalloc int[4];
        for (var i = 0; i < 4; i++)
        {
            bits[i] = BinaryPrimitives.ReadInt32LittleEndian(record[(8 + (i * 4))..]);
        }

        try
        {
            point = new PricePoint(DateTimeOffset.FromUnixTimeMilliseconds(millis), new decimal(bits));
            return true;
        }
        catch (ArgumentException)
        {
            // The four integers do not make a decimal.
            return false;
        }
    }
}
