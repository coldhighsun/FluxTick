using FluxTick.Core.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for <see cref="ExchangeRateService"/>.
/// </summary>
public class ExchangeRateServiceTests
{
    /// <summary>
    /// Rates against the US dollar used by the tests.
    /// </summary>
    private static readonly Dictionary<string, decimal> Rates = new() { ["CNY"] = 7m, ["HKD"] = 8m, ["EUR"] = 0.5m };

    /// <summary>
    /// Creates a service with a fake source.
    /// </summary>
    /// <param name="provider">
    /// The rate source.
    /// </param>
    /// <param name="time">
    /// The clock, or <see langword="null"/> for a fresh fake.
    /// </param>
    /// <param name="cachePath">
    /// The cache file, or <see langword="null"/> for none.
    /// </param>
    /// <param name="retries">
    /// How many times a failed fetch is tried again; by default none. The retries wait no time, so that the tests need no
    /// clock.
    /// </param>
    /// <returns>
    /// The service.
    /// </returns>
    private static ExchangeRateService NewService(IExchangeRateProvider provider, TimeProvider? time = null, string? cachePath = null, int retries = 0) =>
        new(provider, NullLogger<ExchangeRateService>.Instance, time ?? new FakeTimeProvider(), new ExchangeRateOptions { CachePath = cachePath, RetryAttempts = retries, RetryDelay = TimeSpan.Zero });

    /// <summary>
    /// Verifies the crossing of two currencies through the pivot.
    /// </summary>
    [Fact]
    public async Task Convert_CrossRate_GoesThroughPivot()
    {
        var service = NewService(new FakeRateSource(Rates));
        await service.RefreshAsync();

        service.Convert(80m, "HKD", "CNY").Should().Be(70m);
        service.Convert(5m, "usd", "eur").Should().Be(2.5m);
        service.Convert(5m, "CNY", "CNY").Should().Be(5m);
        service.Convert(5m, "CNY", "XXX").Should().BeNull();
    }

    /// <summary>
    /// Verifies that the rates are fetched at start and then every interval.
    /// </summary>
    [Fact]
    public void Start_FetchesNowAndEveryInterval()
    {
        var rates = new FakeRateSource(Rates);
        var time = new FakeTimeProvider();
        using var service = NewService(rates, time);

        service.Start();
        rates.Calls.Should().Be(1);

        time.Advance(TimeSpan.FromMinutes(59));
        rates.Calls.Should().Be(1);

        time.Advance(TimeSpan.FromMinutes(1));

        rates.Calls.Should().Be(2);
        service.UpdatedAt.Should().Be(time.GetUtcNow());
    }

    /// <summary>
    /// Verifies that a failed fetch keeps the previous rates.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_Failure_KeepsPreviousRates()
    {
        var rates = new FakeRateSource(Rates);
        var service = NewService(rates);
        await service.RefreshAsync();

        rates.Fail = true;
        await service.RefreshAsync();

        service.Convert(7m, "CNY", "USD").Should().Be(1m);
    }

    /// <summary>
    /// Verifies that a reply that is not JSON, such as the page of a captive portal, is handled like any failed fetch.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_NonJsonReply_KeepsPreviousRates()
    {
        var rates = new FakeRateSource(Rates);
        var service = NewService(rates);
        await service.RefreshAsync();

        rates.Fail = true;
        rates.Failure = new NotSupportedException("The content type 'text/html' is not supported.");
        await service.RefreshAsync();

        service.Convert(7m, "CNY", "USD").Should().Be(1m);
    }

    /// <summary>
    /// Verifies that an answer without rates is handled like any failed fetch, and that other errors are not hidden.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_EmptyAnswerOrBug_IsHandledOnlyForTheFirst()
    {
        var rates = new FakeRateSource(Rates) { Fail = true, Failure = new ExchangeRateException("no rates") };
        var service = NewService(rates);

        await service.RefreshAsync();

        service.UpdatedAt.Should().BeNull();

        rates.Failure = new InvalidOperationException("a bug");
        var act = () => service.RefreshAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// Verifies that a failure nobody waits for, as with the timer, is logged and does not escape.
    /// </summary>
    [Fact]
    public void Start_UnexpectedFailure_IsLoggedAndDoesNotEscape()
    {
        var logger = new ErrorCountingLogger();
        var rates = new FakeRateSource(Rates) { Fail = true, Failure = new InvalidOperationException("a bug") };
        using var service = new ExchangeRateService(rates, logger, new FakeTimeProvider(), new ExchangeRateOptions());

        var act = service.Start;

        act.Should().NotThrow();
        logger.Errors.Should().Be(1);
    }

    /// <summary>
    /// Verifies that a failed fetch is tried again after a wait, and that the rates of the try that works are used.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_FailsTwice_TriesAgainAndUsesTheRates()
    {
        var rates = new FakeRateSource(Rates) { FailuresLeft = 2 };
        var service = NewService(rates, retries: 3);

        await service.RefreshAsync();

        rates.Calls.Should().Be(3);
        service.Convert(7m, "CNY", "USD").Should().Be(1m);
        service.UpdatedAt.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that the retries stop after the configured number and the previous rates stay.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_FailsAlways_StopsAfterTheRetries()
    {
        var rates = new FakeRateSource(Rates) { Fail = true };
        var service = NewService(rates, retries: 2);

        await service.RefreshAsync();

        rates.Calls.Should().Be(3, "the first try and two retries");
        service.UpdatedAt.Should().BeNull();
    }

    /// <summary>
    /// Verifies that retry settings the retry strategy would refuse are made usable and do not fail the start.
    /// </summary>
    [Fact]
    public async Task Constructor_UnusableRetrySettings_AreMadeUsable()
    {
        var rates = new FakeRateSource(Rates) { FailuresLeft = 1 };
        var options = new ExchangeRateOptions { RetryAttempts = 2, RetryDelay = TimeSpan.FromSeconds(-5), RetryMaxDelay = TimeSpan.FromSeconds(-1) };
        using var service = new ExchangeRateService(rates, NullLogger<ExchangeRateService>.Instance, new FakeTimeProvider(), options);

        await service.RefreshAsync();

        rates.Calls.Should().Be(2);
        service.UpdatedAt.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that ending the service while a refresh waits for its next try ends that refresh quietly, without a
    /// failure and without another request.
    /// </summary>
    [Fact]
    public async Task Dispose_WhileWaitingForARetry_EndsTheRefreshQuietly()
    {
        var rates = new FakeRateSource(Rates) { Fail = true };
        var options = new ExchangeRateOptions { RetryAttempts = 3, RetryDelay = TimeSpan.FromSeconds(30) };
        var service = new ExchangeRateService(rates, NullLogger<ExchangeRateService>.Instance, new FakeTimeProvider(), options);
        var refresh = service.RefreshAsync();
        rates.Calls.Should().Be(1);
        refresh.IsCompleted.Should().BeFalse("it waits for the first retry");

        service.Dispose();

        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        rates.Calls.Should().Be(1);
    }

    /// <summary>
    /// Verifies that a bug is not retried.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_Bug_IsNotRetried()
    {
        var rates = new FakeRateSource(Rates) { Fail = true, Failure = new InvalidOperationException("a bug") };
        var service = NewService(rates, retries: 3);

        var act = () => service.RefreshAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        rates.Calls.Should().Be(1);
    }

    /// <summary>
    /// Verifies that rates that are zero, negative or not for a currency code are dropped, and that an answer with nothing
    /// usable is a failed fetch.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_UnusableRates_AreDroppedOrFailTheFetch()
    {
        var mixed = new Dictionary<string, decimal> { ["CNY"] = 0m, ["HKD"] = 8m, ["eur"] = 0.5m, ["JPY"] = -1m, ["NOT-A-CODE"] = 3m };
        var service = NewService(new FakeRateSource(mixed));

        await service.RefreshAsync();

        service.Convert(1m, "USD", "CNY").Should().BeNull("a rate of zero is no rate");
        service.Convert(1m, "USD", "JPY").Should().BeNull();
        service.Convert(8m, "HKD", "USD").Should().Be(1m);
        service.Convert(1m, "USD", "EUR").Should().Be(0.5m);

        var none = NewService(new FakeRateSource(new Dictionary<string, decimal> { ["CNY"] = 0m }));
        await none.RefreshAsync();

        none.UpdatedAt.Should().BeNull();
    }

    /// <summary>
    /// Verifies that a refresh after the service ended does nothing.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_AfterDispose_DoesNothing()
    {
        var rates = new FakeRateSource(Rates);
        var service = NewService(rates);
        service.Dispose();

        await service.RefreshAsync();

        rates.Calls.Should().Be(0);
    }

    /// <summary>
    /// Verifies that rates saved by one run are read by the next.
    /// </summary>
    [Fact]
    public async Task Constructor_WithCache_LoadsRates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fluxtick-rates-{Guid.NewGuid():N}.json");
        try
        {
            await NewService(new FakeRateSource(Rates), cachePath: path).RefreshAsync();

            var second = NewService(new FakeRateSource(Rates) { Fail = true }, cachePath: path);

            second.Convert(7m, "CNY", "USD").Should().Be(1m);
            second.UpdatedAt.Should().NotBeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Verifies that a damaged cache file is ignored, and that cached rates are found whatever the case of the code.
    /// </summary>
    [Fact]
    public async Task Constructor_DamagedOrMixedCaseCache_IsIgnoredOrMatchedCaseInsensitively()
    {
        var damaged = Path.Combine(Path.GetTempPath(), $"fluxtick-rates-{Guid.NewGuid():N}.json");
        var cached = Path.Combine(Path.GetTempPath(), $"fluxtick-rates-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(damaged, "{ not json");
            await NewService(new FakeRateSource(Rates), cachePath: cached).RefreshAsync();

            var fromDamaged = NewService(new FakeRateSource(Rates) { Fail = true }, cachePath: damaged);
            var fromCache = NewService(new FakeRateSource(Rates) { Fail = true }, cachePath: cached);

            fromDamaged.UpdatedAt.Should().BeNull();
            fromDamaged.Convert(7m, "CNY", "USD").Should().BeNull();
            fromCache.Convert(7m, "cny", "usd").Should().Be(1m);
        }
        finally
        {
            File.Delete(damaged);
            File.Delete(cached);
        }
    }
}

/// <summary>
/// A rate source that serves a fixed table.
/// </summary>
/// <param name="table">
/// The rates to serve.
/// </param>
internal sealed class FakeRateSource(Dictionary<string, decimal> table) : IExchangeRateProvider
{
    /// <summary>
    /// Gets the number of requests made.
    /// </summary>
    public int Calls { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether requests fail.
    /// </summary>
    public bool Fail { get; set; }

    /// <summary>
    /// Gets or sets the exception requests fail with, instead of the one of a failed connection.
    /// </summary>
    public Exception? Failure { get; set; }

    /// <summary>
    /// Gets or sets how many of the next requests fail before the source works again.
    /// </summary>
    public int FailuresLeft { get; set; }

    /// <inheritdoc/>
    public Task<IReadOnlyDictionary<string, decimal>> GetRatesAsync(CancellationToken cancellationToken)
    {
        Calls++;
        if (FailuresLeft > 0)
        {
            FailuresLeft--;
            throw new HttpRequestException("offline");
        }

        return Fail
            ? throw (Failure ?? new HttpRequestException("offline"))
            : Task.FromResult<IReadOnlyDictionary<string, decimal>>(table);
    }
}

/// <summary>
/// A logger that counts the errors written to it.
/// </summary>
internal sealed class ErrorCountingLogger : ILogger<ExchangeRateService>
{
    /// <summary>
    /// Gets the number of errors logged.
    /// </summary>
    public int Errors { get; private set; }

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc/>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Error)
        {
            Errors++;
        }
    }
}
