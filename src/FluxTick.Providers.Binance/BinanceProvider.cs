using System.Net.WebSockets;
using System.Text;
using FluxTick.Abstractions;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Binance;

/// <summary>
/// Streams 24h mini ticker updates (roughly once per second per symbol) from Binance and reconnects with
/// exponential backoff when the connection drops.
/// </summary>
public sealed class BinanceProvider : MarketDataProviderBase, IHistoryProvider, ISymbolSearchProvider
{
    /// <summary>
    /// Quote currencies, longest first, used to split a symbol into a readable pair.
    /// </summary>
    private static readonly string[] QuoteAssets = ["FDUSD", "USDT", "USDC", "BUSD", "TUSD", "BTC", "ETH", "BNB", "EUR", "TRY", "BRL", "GBP"];

    /// <summary>
    /// How long a downloaded symbol list stays valid.
    /// </summary>
    private static readonly TimeSpan SymbolCacheLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The WebSocket endpoint.
    /// </summary>
    private readonly Uri _webSocketUri;

    /// <summary>
    /// The REST endpoint.
    /// </summary>
    private readonly Uri _restBase;

    /// <summary>
    /// The HTTP client used for REST calls; owned by the provider.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Serializes writes to the socket.
    /// </summary>
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    /// <summary>
    /// The connection loop, or <see langword="null"/> while idle.
    /// </summary>
    private Task? _loop;

    /// <summary>
    /// Stops the connection loop without disposing the provider.
    /// </summary>
    private CancellationTokenSource? _loopCts;

    /// <summary>
    /// The currently open socket, or <see langword="null"/>.
    /// </summary>
    private ClientWebSocket? _socket;

    /// <summary>
    /// The next control message id.
    /// </summary>
    private int _requestId;

    /// <summary>
    /// The cached symbol list.
    /// </summary>
    private IReadOnlyList<string>? _symbols;

    /// <summary>
    /// When the symbol list was cached.
    /// </summary>
    private DateTimeOffset _symbolsFetchedAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="BinanceProvider"/> class.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    /// <param name="webSocketUri">
    /// The WebSocket endpoint.
    /// </param>
    /// <param name="restBase">
    /// The REST endpoint.
    /// </param>
    /// <param name="http">
    /// The HTTP client for REST calls; the provider takes ownership.
    /// </param>
    /// <param name="timeProvider">
    /// The clock.
    /// </param>
    /// <param name="logger">
    /// The logger.
    /// </param>
    internal BinanceProvider(
        ProviderDescriptor descriptor,
        Uri webSocketUri,
        Uri restBase,
        HttpClient http,
        TimeProvider timeProvider,
        ILogger logger)
        : base(descriptor, timeProvider)
    {
        _webSocketUri = webSocketUri;
        _restBase = restBase;
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PricePoint>> GetHistoryAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        var uri = new Uri(_restBase, $"/api/v3/klines?symbol={Uri.EscapeDataString(asset.Symbol)}&interval=1m&limit=60");
        var json = await _http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        return BinanceMessages.ParseKlines(json);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Asset>> SearchAsync(string query, AssetClass? assetClass, CancellationToken cancellationToken = default)
    {
        if (assetClass is not (null or AssetClass.Crypto) || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var symbols = await GetSymbolsAsync(cancellationToken).ConfigureAwait(false);
        var needle = query.Trim().ToUpperInvariant();
        return symbols
            .Where(s => s.Contains(needle, StringComparison.Ordinal))
            .OrderBy(s => s.StartsWith(needle, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(s => s.EndsWith("USDT", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(s => s.Length)
            .Take(20)
            .Select(s => new Asset(Descriptor.Id, s, DisplayNameOf(s), AssetClass.Crypto))
            .ToArray();
    }

    /// <inheritdoc />
    protected override async Task OnSubscriptionsChangedAsync(
        IReadOnlyCollection<Asset> current,
        IReadOnlyCollection<Asset> added,
        IReadOnlyCollection<Asset> removed,
        CancellationToken cancellationToken)
    {
        if (current.Count == 0)
        {
            await StopLoopAsync().ConfigureAwait(false);
            SetStatus(ProviderConnectionState.Idle);
            return;
        }

        if (_loop is null)
        {
            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ShutdownToken);
            var token = _loopCts.Token;
            _loop = Task.Run(() => RunAsync(token), CancellationToken.None);
            return;
        }

        if (added.Count > 0)
        {
            await TrySendAsync(BinanceMessages.ControlMessage("SUBSCRIBE", added.Select(a => a.Symbol), NextId()), cancellationToken)
                .ConfigureAwait(false);
        }

        if (removed.Count > 0)
        {
            await TrySendAsync(BinanceMessages.ControlMessage("UNSUBSCRIBE", removed.Select(a => a.Symbol), NextId()), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeCoreAsync()
    {
        await StopLoopAsync().ConfigureAwait(false);
        _http.Dispose();
    }

    /// <summary>
    /// Builds a readable pair name such as <c>BTC/USDT</c> from a Binance symbol.
    /// </summary>
    /// <param name="symbol">
    /// The Binance symbol.
    /// </param>
    /// <returns>
    /// The readable name, or the symbol itself when the quote currency is unknown.
    /// </returns>
    internal static string DisplayNameOf(string symbol)
    {
        foreach (var quote in QuoteAssets)
        {
            if (symbol.Length > quote.Length && symbol.EndsWith(quote, StringComparison.Ordinal))
            {
                return $"{symbol[..^quote.Length]}/{quote}";
            }
        }

        return symbol;
    }

    /// <summary>
    /// Stops the connection loop and waits for it to finish.
    /// </summary>
    /// <returns>
    /// A task that completes when the loop has ended.
    /// </returns>
    private async Task StopLoopAsync()
    {
        var loop = _loop;
        var cts = _loopCts;
        _loop = null;
        _loopCts = null;
        if (loop is null || cts is null)
        {
            return;
        }

        await cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        cts.Dispose();
    }

    /// <summary>
    /// Connects, subscribes and reads until the connection drops, then retries with exponential backoff.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token that stops the loop.
    /// </param>
    /// <returns>
    /// A task that completes when the loop is cancelled.
    /// </returns>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromSeconds(1);
        SetStatus(ProviderConnectionState.Reconnecting, "Connecting");
        while (!cancellationToken.IsCancellationRequested)
        {
            string? failure = null;
            try
            {
                using var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(30);
                await socket.ConnectAsync(_webSocketUri, cancellationToken).ConfigureAwait(false);
                _socket = socket;
                SetStatus(ProviderConnectionState.Connected);
                backoff = TimeSpan.FromSeconds(1);

                var symbols = CurrentSubscriptions.Select(a => a.Symbol).ToArray();
                if (symbols.Length > 0)
                {
                    await SendAsync(socket, BinanceMessages.ControlMessage("SUBSCRIBE", symbols, NextId()), cancellationToken)
                        .ConfigureAwait(false);
                }

                await ReceiveAsync(socket, cancellationToken).ConfigureAwait(false);
                failure = "Connection closed by server";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is WebSocketException or HttpRequestException or IOException)
            {
                failure = ex.Message;
                _logger.LogWarning(ex, "Binance connection failed");
            }
            finally
            {
                _socket = null;
            }

            SetStatus(ProviderConnectionState.Reconnecting, failure);
            try
            {
                await Task.Delay(backoff + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)), TimeProvider, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 30));
        }
    }

    /// <summary>
    /// Reads messages until the socket closes and publishes the quotes they contain.
    /// </summary>
    /// <param name="socket">
    /// The open socket.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that stops reading.
    /// </param>
    /// <returns>
    /// A task that completes when the server closes the connection.
    /// </returns>
    private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);
            try
            {
                if (BinanceMessages.TryParseMiniTicker(text, Descriptor.Id, out var quote))
                {
                    Publish(quote);
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogDebug(ex, "Ignoring malformed Binance message");
            }
        }
    }

    /// <summary>
    /// Sends a control message on the open socket if there is one; otherwise the next connect sends the full set.
    /// </summary>
    /// <param name="text">
    /// The message text.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the send.
    /// </param>
    /// <returns>
    /// A task that completes when the message was sent or skipped.
    /// </returns>
    private async Task TrySendAsync(string text, CancellationToken cancellationToken)
    {
        var socket = _socket;
        if (socket is null || socket.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            await SendAsync(socket, text, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException)
        {
            _logger.LogDebug(ex, "Could not send control message; the reconnect will resubscribe");
        }
    }

    /// <summary>
    /// Sends a text frame, serialized with other senders.
    /// </summary>
    /// <param name="socket">
    /// The socket to write to.
    /// </param>
    /// <param name="text">
    /// The text to send.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the send.
    /// </param>
    /// <returns>
    /// A task that completes when the frame was sent.
    /// </returns>
    private async Task SendAsync(ClientWebSocket socket, string text, CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Gets the next control message id.
    /// </summary>
    /// <returns>
    /// A unique id.
    /// </returns>
    private int NextId() => Interlocked.Increment(ref _requestId);

    /// <summary>
    /// Gets the list of tradable symbols, cached for a few minutes.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token to cancel the request.
    /// </param>
    /// <returns>
    /// The symbols.
    /// </returns>
    private async Task<IReadOnlyList<string>> GetSymbolsAsync(CancellationToken cancellationToken)
    {
        var cached = _symbols;
        if (cached is not null && TimeProvider.GetUtcNow() - _symbolsFetchedAt < SymbolCacheLifetime)
        {
            return cached;
        }

        var json = await _http.GetStringAsync(new Uri(_restBase, "/api/v3/ticker/price"), cancellationToken).ConfigureAwait(false);
        var symbols = BinanceMessages.ParseSymbols(json);
        _symbols = symbols;
        _symbolsFetchedAt = TimeProvider.GetUtcNow();
        return symbols;
    }
}
