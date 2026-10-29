using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace FluxTick.Core.Application;

/// <summary>
/// Supplies foreign exchange rates.
/// </summary>
public interface IExchangeRateProvider
{
    /// <summary>
    /// Fetches the latest rates.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancels the request.
    /// </param>
    /// <returns>
    /// The units of each currency that one unit of <see cref="CurrencyCodes.Pivot"/> buys.
    /// </returns>
    Task<IReadOnlyDictionary<string, decimal>> GetRatesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Options of the <see cref="ExchangeRateService"/>.
/// </summary>
public sealed record ExchangeRateOptions
{
    /// <summary>
    /// Gets or sets how often the rates are fetched again.
    /// </summary>
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the file the latest rates are kept in so that they are available offline, or <see langword="null"/>
    /// to keep them in memory only.
    /// </summary>
    public string? CachePath { get; init; }

    /// <summary>
    /// Gets or sets how many times a failed fetch is tried again before the service waits for the next refresh interval.
    /// Zero turns retrying off.
    /// </summary>
    public int RetryAttempts { get; init; } = 6;

    /// <summary>
    /// Gets or sets how long the service waits before the first retry; every further retry waits twice as long as the
    /// one before, up to <see cref="RetryMaxDelay"/>.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the longest wait between two retries.
    /// </summary>
    public TimeSpan RetryMaxDelay { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Keeps exchange rates up to date by fetching them at a fixed interval, and converts amounts with them.
/// </summary>
public sealed class ExchangeRateService : IDisposable
{
    /// <summary>
    /// The serializer options of the cache file.
    /// </summary>
    private static readonly JsonSerializerOptions CacheJson = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Where the rates come from.
    /// </summary>
    private readonly IExchangeRateProvider _provider;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<ExchangeRateService> _logger;

    /// <summary>
    /// The clock.
    /// </summary>
    private readonly TimeProvider _time;

    /// <summary>
    /// The options.
    /// </summary>
    private readonly ExchangeRateOptions _options;

    /// <summary>
    /// Runs a fetch and tries it again, after a growing wait, when the connection or the answer failed.
    /// </summary>
    private readonly ResiliencePipeline<Dictionary<string, decimal>> _pipeline;

    /// <summary>
    /// Fires the periodic refresh.
    /// </summary>
    private ITimer? _timer;

    /// <summary>
    /// Cancels a refresh in flight when the service is disposed.
    /// </summary>
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// The latest rates, replaced as a whole.
    /// </summary>
    private volatile IReadOnlyDictionary<string, decimal> _rates = new Dictionary<string, decimal>();

    /// <summary>
    /// Whether the service was disposed, after which a refresh that is still due does nothing.
    /// </summary>
    private volatile bool _disposed;

    /// <summary>
    /// Whether a refresh is running (1) or not (0); a second one is not started on top of it.
    /// </summary>
    private int _refreshing;

    /// <summary>
    /// Guards <see cref="_updatedAt"/>, which is written on the thread that fetched and read on the UI thread; a date with
    /// an offset is too wide to be read in one piece.
    /// </summary>
    private readonly Lock _updatedAtLock = new();

    /// <summary>
    /// When the rates were fetched, or <see langword="null"/> for never.
    /// </summary>
    private DateTimeOffset? _updatedAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExchangeRateService"/> class.
    /// </summary>
    /// <param name="provider">
    /// Where the rates come from.
    /// </param>
    /// <param name="logger">
    /// The logger.
    /// </param>
    /// <param name="time">
    /// The clock.
    /// </param>
    /// <param name="options">
    /// The options.
    /// </param>
    public ExchangeRateService(IExchangeRateProvider provider, ILogger<ExchangeRateService> logger, TimeProvider time, ExchangeRateOptions options)
    {
        _provider = provider;
        _logger = logger;
        _time = time;
        _options = options;
        _pipeline = BuildPipeline(options, time, logger);
        LoadCache();
    }

    /// <summary>
    /// Occurs when new rates have been fetched.
    /// </summary>
    public event EventHandler? RatesChanged;

    /// <summary>
    /// Gets when the rates were fetched, or <see langword="null"/> when there are none.
    /// </summary>
    public DateTimeOffset? UpdatedAt
    {
        get
        {
            lock (_updatedAtLock)
            {
                return _updatedAt;
            }
        }

        private set
        {
            lock (_updatedAtLock)
            {
                _updatedAt = value;
            }
        }
    }

    /// <summary>
    /// Gets how long ago the rates were fetched, or <see langword="null"/> when there are none.
    /// </summary>
    public TimeSpan? Age => UpdatedAt is { } updated ? _time.GetUtcNow() - updated : null;

    /// <summary>
    /// Starts fetching rates now and then at every refresh interval.
    /// </summary>
    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        _timer = _time.CreateTimer(_ => _ = RefreshAndLogAsync(), null, _options.RefreshInterval, _options.RefreshInterval);
        _ = RefreshAndLogAsync();
    }

    /// <summary>
    /// Fetches the rates for a caller that does not wait for the result: a failure that <see cref="RefreshAsync"/> does
    /// not handle is logged here, as nothing else would ever see it.
    /// </summary>
    /// <returns>
    /// A task that completes when the request is done.
    /// </returns>
    private async Task RefreshAndLogAsync()
    {
        try
        {
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Refreshing the exchange rates failed unexpectedly; the previous rates stay in use.");
        }
    }

    /// <summary>
    /// Fetches the rates. A failed fetch is tried again after a growing wait (see <see cref="ExchangeRateOptions"/>), so
    /// the task can stay pending for many minutes. When the last try fails, that is logged and the previous rates stay in
    /// use.
    /// </summary>
    /// <returns>
    /// A task that completes when the request is done.
    /// </returns>
    public async Task RefreshAsync()
    {
        if (_disposed || Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var rates = await _pipeline.ExecuteAsync(
                async token => Clean(await _provider.GetRatesAsync(token).ConfigureAwait(false))
                    ?? throw new ExchangeRateException("The rate service returned no usable rates."),
                _cts.Token).ConfigureAwait(false);

            _rates = rates;
            UpdatedAt = _time.GetUtcNow();
            SaveCache(rates);
            _logger.LogInformation("Fetched {Count} exchange rates", rates.Count);
            RatesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException or ExchangeRateException)
        {
            _logger.LogWarning(ex, "Fetching exchange rates failed; the previous rates stay in use.");
        }
        catch (OperationCanceledException) when (_disposed)
        {
            // The service ended while a request or a wait for a retry was on its way.
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            // The service ended while the request was due or on its way; there is nobody to give the rates to.
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }

    /// <summary>
    /// Builds the pipeline that retries a failed fetch with a growing wait.
    /// </summary>
    /// <param name="options">
    /// The options with the retry settings.
    /// </param>
    /// <param name="time">
    /// The clock the waits are measured with.
    /// </param>
    /// <param name="logger">
    /// The logger.
    /// </param>
    /// <returns>
    /// The pipeline.
    /// </returns>
    private static ResiliencePipeline<Dictionary<string, decimal>> BuildPipeline(ExchangeRateOptions options, TimeProvider time, ILogger logger)
    {
        // The options come from the outside; the values the retry strategy would refuse (a negative wait, a longest wait
        // below the first one) are made usable instead of failing the start.
        var delay = options.RetryDelay < TimeSpan.Zero ? TimeSpan.Zero : options.RetryDelay;
        var maxDelay = options.RetryMaxDelay < delay ? delay : options.RetryMaxDelay;
        var builder = new ResiliencePipelineBuilder<Dictionary<string, decimal>> { TimeProvider = time };
        if (options.RetryAttempts > 0)
        {
            builder.AddRetry(new RetryStrategyOptions<Dictionary<string, decimal>>
            {
                ShouldHandle = new PredicateBuilder<Dictionary<string, decimal>>()
                    .Handle<HttpRequestException>()
                    .Handle<JsonException>()
                    .Handle<NotSupportedException>()
                    .Handle<TaskCanceledException>()
                    .Handle<ExchangeRateException>(),
                MaxRetryAttempts = options.RetryAttempts,
                Delay = delay,
                MaxDelay = maxDelay > TimeSpan.Zero ? maxDelay : null,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = false,
                OnRetry = args =>
                {
                    // The last failure is logged with its exception by the caller; a retry only needs a line.
                    logger.LogInformation("Fetching exchange rates failed ({Reason}); trying again in {Delay}.", args.Outcome.Exception?.Message, args.RetryDelay);
                    return default;
                },
            });
        }

        return builder.Build();
    }

    /// <summary>
    /// Keeps the usable rates of an answer: those above zero for a three letter currency code, by upper case code, and
    /// the pivot currency at 1.
    /// </summary>
    /// <param name="fetched">
    /// The rates as the source gave them.
    /// </param>
    /// <returns>
    /// The usable rates, or <see langword="null"/> when there are none.
    /// </returns>
    private static Dictionary<string, decimal>? Clean(IEnumerable<KeyValuePair<string, decimal>> fetched)
    {
        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, rate) in fetched)
        {
            if (rate > 0m && CurrencyCodes.Normalize(code) is { } normalized)
            {
                rates[normalized] = rate;
            }
        }

        if (rates.Count == 0)
        {
            return null;
        }

        rates[CurrencyCodes.Pivot] = 1m;
        return rates;
    }

    /// <summary>
    /// Converts an amount between two currencies through the pivot currency.
    /// </summary>
    /// <param name="amount">
    /// The amount to convert.
    /// </param>
    /// <param name="from">
    /// The currency of the amount.
    /// </param>
    /// <param name="to">
    /// The currency to convert to.
    /// </param>
    /// <returns>
    /// The converted amount, or <see langword="null"/> when a rate is unknown.
    /// </returns>
    public decimal? Convert(decimal amount, string from, string to)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return amount;
        }

        var rates = _rates;
        if (!rates.TryGetValue(from, out var fromRate) || !rates.TryGetValue(to, out var toRate) || fromRate == 0)
        {
            return null;
        }

        return amount / fromRate * toRate;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _cts.Cancel();
        _cts.Dispose();
    }

    /// <summary>
    /// Reads the rates saved by an earlier run, if there are any.
    /// </summary>
    private void LoadCache()
    {
        if (_options.CachePath is not { } path || !File.Exists(path))
        {
            return;
        }

        try
        {
            var cache = JsonSerializer.Deserialize<RateCache>(File.ReadAllText(path), CacheJson);
            if (cache?.Rates is { } saved && Clean(saved) is { } rates)
            {
                _rates = rates;
                UpdatedAt = cache.UpdatedAt;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The exchange rate cache {Path} could not be read.", path);
        }
    }

    /// <summary>
    /// Saves the rates for the next run.
    /// </summary>
    /// <param name="rates">
    /// The rates to save.
    /// </param>
    private void SaveCache(Dictionary<string, decimal> rates)
    {
        if (_options.CachePath is not { } path)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new RateCache { UpdatedAt = UpdatedAt, Rates = rates }, CacheJson));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The exchange rate cache {Path} could not be written.", path);
        }
    }

    /// <summary>
    /// The content of the cache file.
    /// </summary>
    private sealed record RateCache
    {
        /// <summary>
        /// Gets or sets when the rates were fetched.
        /// </summary>
        public DateTimeOffset? UpdatedAt { get; init; }

        /// <summary>
        /// Gets or sets the rates.
        /// </summary>
        public Dictionary<string, decimal>? Rates { get; init; }
    }
}
