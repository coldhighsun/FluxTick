using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Eulerpool;

/// <summary>
/// Keeps the previous close of each ticker, which the stream and the latest quotes endpoint do not carry but the app needs
/// to show the change and its color. It comes from the daily candles and holds for the whole trading day of the exchange
/// of the ticker; a new day fetches it again.
/// </summary>
internal sealed class EulerpoolReferenceCache : IDisposable
{
    /// <summary>
    /// How many tickers are fetched at the same time.
    /// </summary>
    private const int MaxConcurrency = 4;

    /// <summary>
    /// How long to wait before asking again for a ticker whose previous close could not be fetched. Every fetch counts
    /// against the request limit of the key, so a failure is not retried quickly.
    /// </summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long to wait before asking again for a ticker that has no earlier candle, which a new request will not change soon.
    /// </summary>
    private static readonly TimeSpan RetryAfterEmpty = TimeSpan.FromHours(6);

    /// <summary>
    /// Fetches the previous close of a ticker, or <see langword="null"/> when there is none.
    /// </summary>
    private readonly Func<string, CancellationToken, Task<decimal?>> _fetch;

    /// <summary>
    /// The clock.
    /// </summary>
    private readonly TimeProvider _time;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// The previous close of each ticker known so far.
    /// </summary>
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The fetches under way, so that a ticker is never fetched twice at once.
    /// </summary>
    private readonly ConcurrentDictionary<string, Task> _loading = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ends the fetches that are under way when the cache is disposed.
    /// </summary>
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>
    /// Whether the cache was disposed; a late call then starts no fetch.
    /// </summary>
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="EulerpoolReferenceCache"/> class.
    /// </summary>
    /// <param name="fetch">
    /// Fetches the previous close of a ticker, or <see langword="null"/> when there is none.
    /// </param>
    /// <param name="time">
    /// The clock.
    /// </param>
    /// <param name="logger">
    /// The logger.
    /// </param>
    public EulerpoolReferenceCache(Func<string, CancellationToken, Task<decimal?>> fetch, TimeProvider time, ILogger logger)
    {
        _fetch = fetch;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Gets the previous close known for a ticker, without waiting. A ticker that is not known yet, or whose value is from
    /// an earlier day, is fetched in the background, so the next call has the new value.
    /// </summary>
    /// <param name="symbol">
    /// The ticker.
    /// </param>
    /// <returns>
    /// The previous close, or <see langword="null"/> when it is not known.
    /// </returns>
    public decimal? Get(string symbol)
    {
        _entries.TryGetValue(symbol, out var entry);
        if (!_disposed && NeedsRefresh(symbol, entry))
        {
            _ = Refresh(symbol);
        }

        return entry?.Value;
    }

    /// <summary>
    /// Fetches the previous close of the tickers that are not known for the current day yet, and waits for it.
    /// </summary>
    /// <param name="symbols">
    /// The tickers.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that stops the wait.
    /// </param>
    /// <returns>
    /// A task that completes when the tickers were fetched or failed; a failure never escapes.
    /// </returns>
    public Task EnsureAsync(IEnumerable<string> symbols, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        var missing = symbols
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(s => NeedsRefresh(s, _entries.TryGetValue(s, out var entry) ? entry : null))
            .ToArray();
        return Parallel.ForEachAsync(
            missing,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency, CancellationToken = cancellationToken },
            async (symbol, token) => await Refresh(symbol).WaitAsync(token).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    /// <summary>
    /// Tells whether a ticker has to be fetched: it is not known, its value is from an earlier day of its exchange, or a
    /// failed fetch is old enough to try again.
    /// </summary>
    /// <param name="symbol">
    /// The ticker.
    /// </param>
    /// <param name="entry">
    /// What is known about it, or <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when it has to be fetched.
    /// </returns>
    private bool NeedsRefresh(string symbol, Entry? entry) => entry switch
    {
        null => true,
        { RetryAt: { } retryAt } => _time.GetUtcNow() >= retryAt,
        _ => entry.Day != DayOf(symbol),
    };

    /// <summary>
    /// Gets the trading day of the exchange of a ticker, which is the date there.
    /// </summary>
    /// <param name="symbol">
    /// The ticker.
    /// </param>
    /// <returns>
    /// The date at the exchange; the UTC date for an exchange that is not known.
    /// </returns>
    private DateOnly DayOf(string symbol) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), EulerpoolExchanges.Of(symbol)?.Zone ?? TimeZoneInfo.Utc).DateTime);

    /// <summary>
    /// Starts fetching a ticker unless it is being fetched already.
    /// </summary>
    /// <param name="symbol">
    /// The ticker.
    /// </param>
    /// <returns>
    /// The task of the fetch.
    /// </returns>
    private Task Refresh(string symbol)
    {
        var task = _loading.GetOrAdd(symbol, s => Task.Run(() => LoadAsync(s)));

        // A fetch that finished before it was added to the map has removed nothing; it must not stay and block the next one.
        if (task.IsCompleted)
        {
            _loading.TryRemove(new KeyValuePair<string, Task>(symbol, task));
        }

        return task;
    }

    /// <summary>
    /// Fetches a ticker and stores the result. A failure is logged and tried again later; the value from an earlier day
    /// stays in use meanwhile.
    /// </summary>
    /// <param name="symbol">
    /// The ticker.
    /// </param>
    /// <returns>
    /// A task that completes when the result is stored.
    /// </returns>
    private async Task LoadAsync(string symbol)
    {
        _entries.TryGetValue(symbol, out var previous);
        try
        {
            if (_disposed)
            {
                return;
            }

            var value = await _fetch(symbol, _shutdown.Token).ConfigureAwait(false);
            _entries[symbol] = value is null
                ? new Entry(previous?.Value, previous?.Day ?? default, _time.GetUtcNow() + RetryAfterEmpty)
                : new Entry(value, DayOf(symbol), null);
        }
        catch (Exception) when (_disposed)
        {
            // The provider is shutting down.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Eulerpool previous close of {Symbol} could not be fetched", symbol);
            _entries[symbol] = new Entry(previous?.Value, previous?.Day ?? default, _time.GetUtcNow() + RetryAfter);
        }
        finally
        {
            _loading.TryRemove(symbol, out _);
        }
    }

    /// <summary>
    /// The previous close of a ticker.
    /// </summary>
    /// <param name="Value">
    /// The previous close, or <see langword="null"/> when there is none.
    /// </param>
    /// <param name="Day">
    /// The trading day at the exchange it was fetched for.
    /// </param>
    /// <param name="RetryAt">
    /// When to ask again after a failure, or <see langword="null"/> when the value is good for the day.
    /// </param>
    private sealed record Entry(decimal? Value, DateOnly Day, DateTimeOffset? RetryAt);
}
