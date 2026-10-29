using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Common;

/// <summary>
/// Logs every request an HTTP client sends: the method, the host and path, the status and how long it took. The query is
/// never logged because it may carry an API key.
/// </summary>
public sealed class LoggingHttpHandler : DelegatingHandler
{
    /// <summary>
    /// The logger that receives the entries.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingHttpHandler"/> class.
    /// </summary>
    /// <param name="inner">
    /// The handler that sends the requests.
    /// </param>
    /// <param name="logger">
    /// The logger that receives the entries.
    /// </param>
    public LoggingHttpHandler(HttpMessageHandler inner, ILogger logger)
        : base(inner)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(started);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "{Method} {Host}{Path} answered {StatusCode} in {Elapsed:F0} ms",
                    request.Method, request.RequestUri?.Host, request.RequestUri?.AbsolutePath, (int)response.StatusCode, elapsed.TotalMilliseconds);
            }
            else
            {
                _logger.LogWarning(
                    "{Method} {Host}{Path} answered {StatusCode} in {Elapsed:F0} ms",
                    request.Method, request.RequestUri?.Host, request.RequestUri?.AbsolutePath, (int)response.StatusCode, elapsed.TotalMilliseconds);
            }

            return response;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The caller handles the failure and logs it; this entry only records which request it was.
            _logger.LogDebug(
                "{Method} {Host}{Path} failed after {Elapsed:F0} ms: {Reason}",
                request.Method, request.RequestUri?.Host, request.RequestUri?.AbsolutePath, Stopwatch.GetElapsedTime(started).TotalMilliseconds, ex.GetType().Name);
            throw;
        }
    }
}
