using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Yahoo;

/// <summary>
/// Streams prices from the Yahoo Finance streamer. The streamer only pushes an instrument while it trades, so the chart
/// API gives every new instrument its first price (and the market state, which the stream does not carry for a closed
/// market) and takes over while the stream is down.
/// </summary>
public sealed class YahooProvider : RestBackedWebSocketProviderBase, IHistoryProvider, ISymbolSearchProvider, IMarketHoursProvider
{
    /// <summary>
    /// The US indices, which trade in New York hours.
    /// </summary>
    private static readonly HashSet<string> UsIndices = new(StringComparer.OrdinalIgnoreCase)
    {
        "^GSPC", "^DJI", "^IXIC", "^RUT", "^VIX", "^NYA", "^NDX",
    };

    /// <summary>
    /// The Hong Kong indices.
    /// </summary>
    private static readonly HashSet<string> HongKongIndices = new(StringComparer.OrdinalIgnoreCase) { "^HSI", "^HSCE" };

    /// <summary>
    /// The maximum number of requests in flight.
    /// </summary>
    private const int MaxConcurrency = 4;

    /// <summary>
    /// The number the streamer gives the overnight session.
    /// </summary>
    private const int OvernightSession = 4;

    /// <summary>
    /// The most symbols one subscribe message carries.
    /// </summary>
    private const int SubscribeBatchSize = 100;

    /// <summary>
    /// The API endpoint.
    /// </summary>
    private readonly Uri _baseUri;

    /// <summary>
    /// The last update of an instrument that trades in a session with hours, by symbol without regard to case, so that the
    /// end of an extended session, which the stream does not announce, can be told from the chart API.
    /// </summary>
    private readonly ConcurrentDictionary<string, PushedQuote> _lastPushed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The HTTP client; owned by the provider.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// Initializes a new instance of the <see cref="YahooProvider"/> class.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    /// <param name="baseUri">
    /// The API endpoint.
    /// </param>
    /// <param name="streamUri">
    /// The streamer endpoint.
    /// </param>
    /// <param name="fallbackInterval">
    /// How often the chart API is polled while the stream is down.
    /// </param>
    /// <param name="http">
    /// The HTTP client; the provider takes ownership.
    /// </param>
    /// <param name="timeProvider">
    /// The clock.
    /// </param>
    /// <param name="logger">
    /// The logger.
    /// </param>
    /// <param name="quietInterval">
    /// How long a connected stream may stay quiet during trading hours before REST is polled as well, or
    /// <see langword="null"/> for the default.
    /// </param>
    internal YahooProvider(
        ProviderDescriptor descriptor,
        Uri baseUri,
        Uri streamUri,
        TimeSpan fallbackInterval,
        HttpClient http,
        TimeProvider timeProvider,
        ILogger logger,
        TimeSpan? quietInterval = null)
        : base(descriptor, streamUri, fallbackInterval, timeProvider, logger, quietInterval)
    {
        _baseUri = baseUri;
        _http = http;
    }

    /// <inheritdoc />
    public MarketState GetMarketState(Asset asset, DateTimeOffset now, DateTimeOffset? lastTradeTime = null, TimeSpan? holidayGrace = null) =>
        ExchangeHours.StateOf(ExchangeOf(asset.Symbol), now, lastTradeTime, holidayGrace);

    /// <summary>
    /// Finds the exchange of a symbol. Only the US, Chinese A-share and Hong Kong markets are known.
    /// </summary>
    /// <param name="symbol">
    /// The symbol.
    /// </param>
    /// <returns>
    /// The exchange, or <see langword="null"/> when it is not known.
    /// </returns>
    internal static ExchangeHours? ExchangeOf(string symbol)
    {
        if (symbol.StartsWith('^'))
        {
            return UsIndices.Contains(symbol) ? ExchangeHours.Us : HongKongIndices.Contains(symbol) ? ExchangeHours.HongKong : null;
        }

        var dot = symbol.LastIndexOf('.');
        if (dot < 0)
        {
            // A symbol with an equals sign is a forex pair or a future; a five letter one that ends in X is a mutual fund,
            // which is priced once a day.
            return symbol.Contains('=') || (symbol.Length == 5 && symbol.EndsWith('X')) ? null : ExchangeHours.Us;
        }

        var suffix = symbol[(dot + 1)..];

        // The letter A, B or C after the dot is the share class of a US company, such as BRK.B. Other single letters name
        // exchanges, such as L for London.
        return suffix is ['A' or 'B' or 'C' or 'a' or 'b' or 'c']
            ? ExchangeHours.Us
            : suffix.ToUpperInvariant() switch
            {
                "SS" or "SZ" or "BJ" => ExchangeHours.ChinaA,
                "HK" => ExchangeHours.HongKong,
                _ => null,
            };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PricePoint>> GetHistoryAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        var data = await FetchChartAsync(asset.Symbol, intraday: true, cancellationToken).ConfigureAwait(false);
        return data.History;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Asset>> SearchAsync(string query, AssetClass? assetClass, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var uri = EndpointOptions.Combine(_baseUri, $"/v1/finance/search?q={Uri.EscapeDataString(query.Trim())}&quotesCount=15&newsCount=0");
        var json = await _http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        return YahooParser.ParseSearch(json, Descriptor.Id)
            .Where(a => assetClass is null || a.AssetClass == assetClass)
            .ToArray();
    }

    /// <inheritdoc />
    protected override IEnumerable<string> BuildSubscribeMessages(IReadOnlyCollection<Asset> assets) =>
        BuildMessages("subscribe", assets);

    /// <inheritdoc />
    protected override IEnumerable<string> BuildUnsubscribeMessages(IReadOnlyCollection<Asset> assets)
    {
        // The stream knows symbols without regard to case, but the subscriptions are told apart by case; an instrument
        // that goes must not take the stream of one that stays under the same symbol.
        var kept = CurrentSubscriptions.Select(a => a.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return BuildMessages("unsubscribe", [.. assets.Where(a => !kept.Contains(a.Symbol))]);
    }

    /// <inheritdoc />
    protected override void HandleMessage(string text)
    {
        var payload = text;
        if (text.StartsWith('{'))
        {
            using var document = JsonDocument.Parse(text);
            if (!document.RootElement.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.String)
            {
                return;
            }

            payload = message.GetString() ?? string.Empty;
        }

        if (!YahooStreamParser.TryParse(payload, out var update))
        {
            return;
        }

        // The streamer sends 32-bit floats, so the previous close derived from the price and the change carries rounding
        // noise that would make it jitter from update to update; the digits the price itself shows are what is reliable.
        var reference = update.Change is { } change
            ? Math.Round(update.Price - change, Math.Max(update.Price.Scale, update.Price < 10m ? 4 : 2))
            : (decimal?)null;
        var market = ToMarketState(update);
        if (PublishToSubscribers(update.Symbol, update.Price, reference, update.Time, market) && market is { } state)
        {
            var template = new Quote(new AssetId(Descriptor.Id, update.Symbol), update.Price, reference, update.Time ?? TimeProvider.GetUtcNow())
            {
                Market = state,
                TimeIsEstimated = update.Time is null,
            };
            _lastPushed[update.Symbol] = new PushedQuote(template, update.MarketHours == OvernightSession);
        }
    }

    /// <inheritdoc />
    protected override async Task OnSubscriptionsChangedAsync(
        IReadOnlyCollection<Asset> current,
        IReadOnlyCollection<Asset> added,
        IReadOnlyCollection<Asset> removed,
        CancellationToken cancellationToken)
    {
        await base.OnSubscriptionsChangedAsync(current, added, removed, cancellationToken).ConfigureAwait(false);
        foreach (var asset in removed)
        {
            _lastPushed.TryRemove(asset.Symbol, out _);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeResourcesAsync()
    {
        await base.DisposeResourcesAsync().ConfigureAwait(false);
        _http.Dispose();
    }

    /// <summary>
    /// Builds the messages that subscribe or unsubscribe instruments.
    /// </summary>
    /// <param name="action">
    /// The action, <c>subscribe</c> or <c>unsubscribe</c>.
    /// </param>
    /// <param name="assets">
    /// The instruments.
    /// </param>
    /// <returns>
    /// One message per batch of instruments.
    /// </returns>
    private static IEnumerable<string> BuildMessages(string action, IReadOnlyCollection<Asset> assets) =>
        assets
            .Select(a => a.Symbol.ToUpperInvariant())
            .Distinct()
            .Chunk(SubscribeBatchSize)
            .Select(batch => JsonSerializer.Serialize(new Dictionary<string, string[]> { [action] = batch }));

    /// <summary>
    /// Maps the session of a pushed update to a market state, for the instrument types that have trading hours.
    /// </summary>
    /// <param name="update">
    /// The update.
    /// </param>
    /// <returns>
    /// The state, or <see langword="null"/> when the update does not tell.
    /// </returns>
    private static MarketState? ToMarketState(YahooStreamQuote update)
    {
        if (update.QuoteType is not (8 or 9 or 20))
        {
            return null;
        }

        return update.MarketHours switch
        {
            0 => MarketState.PreMarket,
            1 => MarketState.Open,
            // Extended hours and the overnight session (4) trade after the regular close, so they show as after hours.
            // Without a state the hours of the calendar would call a market closed that is still trading.
            2 or 3 or 4 => MarketState.AfterHours,
            _ => null,
        };
    }

    /// <inheritdoc />
    protected override async Task<PollOutcome> FetchQuotesAsync(IReadOnlyCollection<Asset> assets, CancellationToken cancellationToken)
    {
        var succeeded = 0;
        var failed = 0;
        var limited = 0;
        var refused = 0;
        string? lastError = null;
        var problems = new ConcurrentDictionary<AssetId, string>();

        try
        {
            await Parallel.ForEachAsync(
                assets,
                new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency, CancellationToken = cancellationToken },
                async (asset, token) =>
                {
                    try
                    {
                        var data = await FetchChartAsync(asset.Symbol, intraday: false, token).ConfigureAwait(false);

                        // The chart is parsed under the provider id and the symbol that was asked for, which is the one
                        // that was subscribed. The chart reports the regular session only, so when it comes after a pushed
                        // extended-hours trade it is older, and the service ignores it.
                        Publish(data.Quote with { AssetId = asset.Id });
                        if (ClosedAfterExtendedSession(asset, data) is { } closed)
                        {
                            Publish(closed);
                        }

                        Interlocked.Increment(ref succeeded);
                    }
                    catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        Interlocked.Increment(ref limited);
                        problems[asset.Id] = "Rate limited by Yahoo";
                        Logger.LogWarning("Yahoo rate limited the request for {Symbol}", asset.Symbol);
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested)
                    {
                        // Any other failure, including an odd response of one symbol, must not fail the other symbols.
                        // A refused request and a timeout both mean the service does not answer; ask less often.
                        if (ex is HttpRequestException or TaskCanceledException)
                        {
                            Interlocked.Increment(ref refused);
                        }

                        Interlocked.Increment(ref failed);
                        Volatile.Write(ref lastError, $"{asset.Symbol}: {ex.Message}");
                        problems[asset.Id] = $"{asset.Symbol}: {ex.Message}";

                        if (ex is HttpRequestException or YahooApiException or JsonException or TaskCanceledException)
                        {
                            Logger.LogWarning(ex, "Yahoo request for {Symbol} failed", asset.Symbol);
                        }
                        else
                        {
                            // Not something the service can do: a bug in the parser or the provider, which must not pass
                            // for a service that does not answer.
                            Logger.LogError(ex, "Yahoo request for {Symbol} failed unexpectedly", asset.Symbol);
                        }
                    }
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The subscriptions ended or the provider is shutting down.
        }

        // A push back says more than which instrument failed last, and is the same whichever task finished first.
        return new PollOutcome(succeeded, failed + limited, limited > 0 ? "Rate limited by Yahoo" : lastError, BackOff: limited > 0 || (succeeded == 0 && refused > 0), problems);
    }

    /// <summary>
    /// Works out the quote that ends an extended session. The stream says nothing when pre-market or after-hours trading
    /// ends, and the chart reports the regular session, so its quote is older than the last pushed trade and the service
    /// ignores it, market state included. When the chart says the market is shut after the stream pushed a trade of an
    /// extended session, and the chart says that session is over, that trade is published again, with its own time, and
    /// the shut state. A chart that gives no extended periods is read the way the parser does for the US exchanges, whose
    /// sessions have a fixed length; for any other it says nothing about the end. Its time is not older than
    /// the trade shown, so it is applied, and the older chart quotes that follow still cannot move the price back. The
    /// overnight session is not among the periods of the chart and is not ended this way: the next session's first
    /// update takes it over.
    /// </summary>
    /// <param name="asset">
    /// The instrument.
    /// </param>
    /// <param name="data">
    /// The parsed chart.
    /// </param>
    /// <returns>
    /// The quote to publish, or <see langword="null"/> when the extended session did not end.
    /// </returns>
    private Quote? ClosedAfterExtendedSession(Asset asset, YahooChartData data)
    {
        if (data.Quote.Market is not MarketState.Closed
            || !_lastPushed.TryGetValue(asset.Symbol, out var pushed)
            || pushed.Overnight
            || pushed.Template.TimeIsEstimated
            || pushed.Template.Timestamp <= data.Quote.Timestamp
            || !(pushed.Template.Market switch
            {
                MarketState.PreMarket => data.Sessions?.InPreMarket is false,
                MarketState.AfterHours => data.Sessions?.InPostMarket is false,
                _ => false,
            }))
        {
            return null;
        }

        // Only the entry that was read goes: a newer update that arrived meanwhile has its own end to wait for.
        _lastPushed.TryRemove(new KeyValuePair<string, PushedQuote>(asset.Symbol, pushed));
        return pushed.Template with { AssetId = asset.Id, Market = MarketState.Closed };
    }

    /// <summary>
    /// Downloads and parses the chart of a symbol.
    /// </summary>
    /// <param name="symbol">
    /// The Yahoo symbol.
    /// </param>
    /// <param name="intraday">
    /// Whether to request the 1-minute series of the day; a poll only needs the quote in the metadata, which a single
    /// daily candle carries as well.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the request.
    /// </param>
    /// <returns>
    /// The parsed chart.
    /// </returns>
    private async Task<YahooChartData> FetchChartAsync(string symbol, bool intraday, CancellationToken cancellationToken)
    {
        // The minutes of the sparkline are asked for by time, not as a whole day: that is about 390 candles, and a market that
        // is shut answers with the day before although nothing of it would show. A window without trades has no candles.
        var now = TimeProvider.GetUtcNow();
        var query = intraday
            ? $"interval=1m&period1={(now - HistoryWindow.Span).ToUnixTimeSeconds()}&period2={now.ToUnixTimeSeconds()}"
            : "interval=1d&range=1d";
        var uri = EndpointOptions.Combine(_baseUri, $"/v8/finance/chart/{Uri.EscapeDataString(symbol)}?{query}");
        using var response = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new YahooApiException($"Unknown symbol '{symbol}'.");
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return YahooParser.ParseChart(json, Descriptor.Id, symbol, TimeProvider.GetUtcNow());
    }

    /// <summary>
    /// The last update the streamer pushed for an instrument that trades in a session with hours.
    /// </summary>
    /// <param name="Template">
    /// The update as a quote, under the symbol the streamer sent; it is published again for the subscribed instrument when
    /// the session ends.
    /// </param>
    /// <param name="Overnight">
    /// Whether the update was traded in the overnight session.
    /// </param>
    private sealed record PushedQuote(Quote Template, bool Overnight);
}
