using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Eastmoney;

/// <summary>
/// The server-sent event stream of one instrument. Eastmoney pushes the price of a single instrument per connection: the
/// first message is a full snapshot and the later ones carry only the fields that changed, so the stream keeps the state
/// and publishes a quote whenever the price is known.
/// </summary>
internal sealed class EastmoneyStream
{
    /// <summary>
    /// The prefix of a data line of an event stream.
    /// </summary>
    private const string DataPrefix = "data:";

    /// <summary>
    /// How long a connection may stay silent while the market is open before it is dropped and opened again.
    /// </summary>
    private static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(3);

    /// <summary>
    /// How long a connection has to stay up before the next failure starts the backoff over.
    /// </summary>
    private static readonly TimeSpan HealthyUptime = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The longest wait between two attempts to connect.
    /// </summary>
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The instrument.
    /// </summary>
    private readonly Asset _asset;

    /// <summary>
    /// The address of the stream.
    /// </summary>
    private readonly Uri _uri;

    /// <summary>
    /// The HTTP client; owned by the provider.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// The clock.
    /// </summary>
    private readonly TimeProvider _time;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Receives each quote.
    /// </summary>
    private readonly Action<Quote> _publish;

    /// <summary>
    /// Tells whether the market of the instrument is known to be shut, in which case silence is expected.
    /// </summary>
    private readonly Func<bool> _isShut;

    /// <summary>
    /// Called when the connection delivers its first message, so that the provider can report itself connected.
    /// </summary>
    private readonly Action _onLive;

    /// <summary>
    /// The previous close of the instrument.
    /// </summary>
    private decimal? _previousClose;

    /// <summary>
    /// When the instrument last traded.
    /// </summary>
    private DateTimeOffset? _tradeTime;

    /// <summary>
    /// Whether the connection is open and delivering.
    /// </summary>
    private volatile bool _live;

    /// <summary>
    /// Initializes a new instance of the <see cref="EastmoneyStream"/> class.
    /// </summary>
    /// <param name="asset">
    /// The instrument.
    /// </param>
    /// <param name="uri">
    /// The address of the stream.
    /// </param>
    /// <param name="http">
    /// The HTTP client.
    /// </param>
    /// <param name="time">
    /// The clock.
    /// </param>
    /// <param name="logger">
    /// The logger.
    /// </param>
    /// <param name="publish">
    /// Receives each quote.
    /// </param>
    /// <param name="isShut">
    /// Tells whether the market of the instrument is known to be shut.
    /// </param>
    /// <param name="onLive">
    /// Called when the connection delivers its first message.
    /// </param>
    public EastmoneyStream(
        Asset asset,
        Uri uri,
        HttpClient http,
        TimeProvider time,
        ILogger logger,
        Action<Quote> publish,
        Func<bool> isShut,
        Action onLive)
    {
        _asset = asset;
        _uri = uri;
        _http = http;
        _time = time;
        _logger = logger;
        _publish = publish;
        _isShut = isShut;
        _onLive = onLive;
    }

    /// <summary>
    /// Gets a value indicating whether the connection is open and has delivered.
    /// </summary>
    public bool IsLive => _live;

    /// <summary>
    /// Connects, reads and reconnects with exponential backoff until cancelled.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token that ends the stream.
    /// </param>
    /// <returns>
    /// A task that completes when the token is cancelled.
    /// </returns>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromSeconds(1);
        while (!cancellationToken.IsCancellationRequested)
        {
            var started = _time.GetTimestamp();
            try
            {
                await ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or InvalidDataException)
            {
                _logger.LogDebug("Eastmoney stream of {Symbol} ended: {Reason}", _asset.Symbol, ex.Message);
            }
            finally
            {
                _live = false;
            }

            if (_time.GetElapsedTime(started) >= HealthyUptime)
            {
                backoff = TimeSpan.FromSeconds(1);
            }

            try
            {
                await Task.Delay(backoff + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)), _time, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }
    }

    /// <summary>
    /// Opens one connection and handles its messages until it ends.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token that ends the stream.
    /// </param>
    /// <returns>
    /// A task that completes when the server closes the connection.
    /// </returns>
    private async Task ReadAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _uri);
        request.Headers.Accept.ParseAdd("text/event-stream");

        // Only the wait for the headers counts against the timeout of the client; the body is read for as long as it lasts.
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(body);
        while (true)
        {
            // A market that is open and silent for minutes means a connection that died without telling; a shut one is quiet.
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (!_isShut())
            {
                idle.CancelAfter(IdleLimit);
            }

            string? line;
            try
            {
                line = await reader.ReadLineAsync(idle.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IOException("The stream was silent for too long.");
            }

            if (line is null)
            {
                return;
            }

            if (TryApply(line, out var quote))
            {
                if (!_live)
                {
                    _live = true;
                    _onLive();
                }

                _publish(quote!);
            }
        }
    }

    /// <summary>
    /// Merges a line of the stream into the state of the instrument.
    /// </summary>
    /// <param name="line">
    /// The line.
    /// </param>
    /// <param name="quote">
    /// The quote to publish when the line changed the price.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the line was a data line that gave a price.
    /// </returns>
    public bool TryApply(string line, out Quote? quote)
    {
        quote = null;
        if (!line.StartsWith(DataPrefix, StringComparison.Ordinal)
            || EastmoneyParser.ParseStreamUpdate(line[DataPrefix.Length..].Trim(), _asset.Symbol) is not { } update)
        {
            return false;
        }

        _previousClose = update.PreviousClose ?? _previousClose;
        _tradeTime = update.Time ?? _tradeTime;
        if (update.Price is not { } price)
        {
            return false;
        }

        quote = new Quote(_asset.Id, price, _previousClose, _tradeTime ?? _time.GetUtcNow());
        return true;
    }
}
