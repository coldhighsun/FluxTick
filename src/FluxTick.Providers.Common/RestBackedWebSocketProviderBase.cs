using FluxTick.Abstractions;
using Microsoft.Extensions.Logging;

namespace FluxTick.Providers.Common;

/// <summary>
/// Base class for sources that stream live prices over a WebSocket but have a REST service as well. The stream is the
/// way to get prices; REST gives every new instrument its first price at once (a stream is silent for a market that is
/// closed), takes over while the stream is down and looks again, slowly, when the stream is connected but quiet. The
/// status reflects what REST delivers while the stream is down: prices that keep arriving make the source connected, not
/// reconnecting, and rounds that keep failing make it failed. Derived classes describe the wire protocol of the stream, as
/// for <see cref="WebSocketProviderBase"/>, and implement one method that fetches and publishes quotes over REST.
/// </summary>
public abstract class RestBackedWebSocketProviderBase : WebSocketProviderBase
{
    /// <summary>
    /// The number of rounds in a row without a price after which the source is reported as failed, as for a polled source.
    /// </summary>
    private const int FailureThreshold = 3;

    /// <summary>
    /// The longest wait between two fallback polls after the service pushed back.
    /// </summary>
    private static readonly TimeSpan MaxFallbackInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The value of the time of the last success while there was none.
    /// </summary>
    private const long NoSuccess = long.MinValue;

    /// <summary>
    /// The factor by which the rounds required for a reconnect may grow at most, after reconnects that did not help.
    /// </summary>
    private const int MaxEscalation = 8;

    /// <summary>
    /// The most problems the status message tells at once.
    /// </summary>
    private const int MaxShownProblems = 3;

    /// <summary>
    /// The shortest time a successful round keeps the source reported as connected, so that a poll interval below it does
    /// not make the status flicker.
    /// </summary>
    private static readonly TimeSpan MinFreshness = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How often REST is polled while the stream is down.
    /// </summary>
    private readonly TimeSpan _fallbackInterval;

    /// <summary>
    /// How long a connected stream may stay quiet before REST is polled as well.
    /// </summary>
    private readonly TimeSpan _quietStreamInterval;

    /// <summary>
    /// The fallback loop, which runs while instruments are subscribed. Its token also ends the snapshots.
    /// </summary>
    private readonly BackgroundLoop _fallback;

    /// <summary>
    /// When the last round of the fallback delivered a price, or <see cref="NoSuccess"/> if none did.
    /// </summary>
    private long _lastRestSuccess = NoSuccess;

    /// <summary>
    /// The number of rounds of the fallback in a row that delivered no price.
    /// </summary>
    private int _failedRounds;

    /// <summary>
    /// Whether there are subscriptions and every one of them is outside trading, so that the fallback has nothing to fetch.
    /// The status then does not take the lack of polling for a failure.
    /// </summary>
    private bool _outsideTrading;

    /// <summary>
    /// The problems REST reported, such as a symbol without data; replaced atomically.
    /// </summary>
    private RestProblems _restProblems = RestProblems.None;

    /// <summary>
    /// Makes merging the problems of a snapshot into the current ones one step.
    /// </summary>
    private readonly object _problemLock = new();

    /// <summary>
    /// The newest trade time published for each instrument, to tell a quote that moves a price forward from one that
    /// repeats it.
    /// </summary>
    private readonly Dictionary<AssetId, DateTimeOffset> _newestTrade = [];

    /// <summary>
    /// Guards <see cref="_newestTrade"/>.
    /// </summary>
    private readonly object _newestTradeLock = new();


    /// <summary>
    /// How many published quotes moved the price of an instrument forward with a trade that is recent, from any source.
    /// </summary>
    private long _advances;

    /// <summary>
    /// The rounds in a row of polling a quiet stream that found newer prices while the stream said nothing. Only the
    /// fallback loop touches it.
    /// </summary>
    private int _quietRounds;

    /// <summary>
    /// The number of such rounds that makes the connection be dropped. It grows after each reconnect and starts over once
    /// the stream says something, so a stream that stays silent whatever is done is not reconnected every few minutes.
    /// Only the fallback loop touches it.
    /// </summary>
    private int _requiredQuietRounds;

    /// <summary>
    /// Initializes a new instance of the <see cref="RestBackedWebSocketProviderBase"/> class.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    /// <param name="webSocketUri">
    /// The WebSocket endpoint.
    /// </param>
    /// <param name="fallbackInterval">
    /// How often REST is polled while the stream is down.
    /// </param>
    /// <param name="timeProvider">
    /// The clock.
    /// </param>
    /// <param name="logger">
    /// The logger.
    /// </param>
    /// <param name="quietStreamInterval">
    /// How long a connected stream may stay quiet during trading hours before REST is polled as well, or
    /// <see langword="null"/> for 5 minutes.
    /// </param>
    protected RestBackedWebSocketProviderBase(
        ProviderDescriptor descriptor,
        Uri webSocketUri,
        TimeSpan fallbackInterval,
        TimeProvider timeProvider,
        ILogger logger,
        TimeSpan? quietStreamInterval = null)
        : base(descriptor, webSocketUri, timeProvider, logger)
    {
        _fallbackInterval = fallbackInterval;
        _quietStreamInterval = quietStreamInterval ?? TimeSpan.FromMinutes(5);
        _fallback = new BackgroundLoop(ShutdownToken);
    }

    /// <summary>
    /// Gets how long a connected stream may stay quiet before REST is polled as well, once for every such period. Closed
    /// markets are quiet too, so this is slow on purpose: it keeps prices and market states from going unnoticed stale
    /// behind a connection that is up but no longer delivers, without polling for a market that is simply shut.
    /// </summary>
    protected virtual TimeSpan QuietStreamInterval => _quietStreamInterval;

    /// <summary>
    /// Tells whether the market of an instrument is known not to be trading, so that polling REST for it is pointless: it
    /// is shut, at midday break, or in the pre-market or after hours, when the stream delivers what trades. The hours
    /// come from <see cref="IMarketHoursProvider"/>; an instrument whose market is not known is polled as usual. The
    /// first price of a new instrument is fetched regardless.
    /// </summary>
    /// <param name="asset">
    /// The instrument.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the market is known not to be open; see <see cref="TradingHoursPolicy"/>.
    /// </returns>
    protected virtual bool IsOutsideTrading(Asset asset) =>
        TradingHoursPolicy.IsOutsideTrading(this as IMarketHoursProvider, asset, TimeProvider.GetUtcNow(), hasStream: true);

    /// <summary>
    /// Gets how many rounds in a row of polling a connected but quiet stream must find newer prices, with the stream
    /// saying nothing, before the connection is dropped and opened again. Only a price that data should be arriving for
    /// right now counts: a trade that is recent (within twice <see cref="QuietStreamInterval"/> of the clock), newer than
    /// the last one seen for the instrument, of an instrument that was already known and of a market the source does not
    /// report as closed. A closed market, a late print of the close or the first price of a new instrument never count.
    /// After a reconnect that does not make the stream deliver, twice as many rounds are needed the next time, up to
    /// eight times as many, and the count starts over once the stream says something.
    /// </summary>
    protected virtual int QuietRoundsBeforeReconnect => 3;

    /// <summary>
    /// Fetches the given instruments over REST and publishes their quotes. One failing instrument must not fail the
    /// others, and a failure must not escape: log it and report whether the service pushed back.
    /// </summary>
    /// <param name="assets">
    /// The instruments to fetch; never empty.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that ends the fetch when the subscriptions end or the provider shuts down.
    /// </param>
    /// <returns>
    /// How the round went; the fallback waits longer when it says to back off, and its last error is shown in the status.
    /// </returns>
    protected abstract Task<PollOutcome> FetchQuotesAsync(IReadOnlyCollection<Asset> assets, CancellationToken cancellationToken);

    /// <inheritdoc />
    protected override ProviderStatus AdjustStatus(ProviderStatus stream)
    {
        if (stream.State == ProviderConnectionState.Connected)
        {
            // A problem REST reported for an instrument, such as a symbol without data, still shows.
            return stream.Message is null && CurrentProblem() is { } problem ? stream with { Message = problem } : stream;
        }

        if (stream.State != ProviderConnectionState.Reconnecting)
        {
            return stream;
        }

        var reason = CurrentProblem() ?? (stream.Message is "Connecting" ? null : stream.Message);
        if (Volatile.Read(ref _failedRounds) >= FailureThreshold)
        {
            return new ProviderStatus(ProviderConnectionState.Failed, reason ?? "No prices from the stream or from polling");
        }

        if (Volatile.Read(ref _outsideTrading))
        {
            // Nothing is polled while no market is open, so the lack of a recent REST success says nothing; the source is as
            // available as it was, and the prices come back when a market opens or the stream does.
            return new ProviderStatus(
                ProviderConnectionState.Connected,
                reason is null ? "Live stream unavailable; no market is open" : $"Live stream unavailable; no market is open ({reason})");
        }

        var freshness = TimeSpan.FromTicks(Math.Max(_fallbackInterval.Ticks * 3, MinFreshness.Ticks));
        if (Volatile.Read(ref _lastRestSuccess) is var success and not NoSuccess && TimeProvider.GetElapsedTime(success) < freshness)
        {
            return new ProviderStatus(
                ProviderConnectionState.Connected,
                reason is null ? "Live stream unavailable; polling" : $"Live stream unavailable; polling ({reason})");
        }

        return stream;
    }

    /// <inheritdoc />
    protected override void OnStreamStatusChanged(ProviderStatus stream)
    {
        if (stream.State == ProviderConnectionState.Connected)
        {
            // The stream is back: the rounds that failed while it was down no longer count towards a failure.
            Volatile.Write(ref _failedRounds, 0);
        }
    }

    /// <inheritdoc />
    protected override void OnQuotePublished(Quote quote)
    {
        if (quote.TimeIsEstimated)
        {
            return;
        }

        lock (_newestTradeLock)
        {
            var known = _newestTrade.TryGetValue(quote.AssetId, out var newest);
            if (known && quote.Timestamp <= newest)
            {
                return;
            }

            // An instrument removed since the quote was accepted must not come back into the map.
            if (!IsSubscribed(quote.AssetId))
            {
                return;
            }

            // The first quote of an instrument has nothing to be newer than, so it says nothing about the stream.
            _newestTrade[quote.AssetId] = quote.Timestamp;
            if (!known)
            {
                return;
            }
        }

        if (IsLiveTrade(quote))
        {
            Interlocked.Increment(ref _advances);
        }
    }

    /// <summary>
    /// Tells whether a quote shows that trading is going on right now: its trade is recent, and the source does not say the
    /// market is shut. A trade that is old, such as a late print of the close, proves nothing about the stream. A time
    /// ahead of the clock counts, as the clocks of source and machine may differ.
    /// </summary>
    /// <param name="quote">
    /// The quote.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when data should be arriving by the stream for this quote.
    /// </returns>
    private bool IsLiveTrade(Quote quote) =>
        quote.Market is not (MarketState.Closed or MarketState.Break)
        && TimeProvider.GetUtcNow() - quote.Timestamp <= QuietStreamInterval * 2;

    /// <inheritdoc />
    protected override async Task OnSubscriptionsChangedAsync(
        IReadOnlyCollection<Asset> current,
        IReadOnlyCollection<Asset> added,
        IReadOnlyCollection<Asset> removed,
        CancellationToken cancellationToken)
    {
        await base.OnSubscriptionsChangedAsync(current, added, removed, cancellationToken).ConfigureAwait(false);

        if (removed.Count > 0)
        {
            lock (_newestTradeLock)
            {
                foreach (var asset in removed)
                {
                    _newestTrade.Remove(asset.Id);
                }
            }

            // The problem of an instrument that went is no longer shown.
            RefreshStatus();
        }

        if (current.Count == 0)
        {
            await _fallback.StopAsync().ConfigureAwait(false);
            Volatile.Write(ref _outsideTrading, false);
            Volatile.Write(ref _lastRestSuccess, NoSuccess);
            Volatile.Write(ref _failedRounds, 0);
            Volatile.Write(ref _restProblems, RestProblems.None);
            return;
        }

        _fallback.Start(RunFallbackAsync);

        if (added.Count > 0)
        {
            var snapshotToken = _fallback.Token;
            _ = Task.Run(() => SnapshotAsync(added, snapshotToken), CancellationToken.None);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeResourcesAsync() => await _fallback.StopAsync().ConfigureAwait(false);

    /// <summary>
    /// Fetches the first prices of new instruments. Nobody awaits this task, so nothing may escape it.
    /// </summary>
    /// <param name="assets">
    /// The new instruments.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that ends the fetch.
    /// </param>
    /// <returns>
    /// A task that completes when the prices were fetched or the fetch failed.
    /// </returns>
    private async Task SnapshotAsync(IReadOnlyCollection<Asset> assets, CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await FetchQuotesAsync(assets, cancellationToken).ConfigureAwait(false);

            // A snapshot covers only the new instruments, so it can report a problem but not clear one of the others.
            if (MergeProblems(assets, outcome))
            {
                RefreshStatus();
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // The subscriptions ended or the provider is shutting down. Nobody waits for this task, so the derived class
            // may already have released what the request used; that is no failure worth a warning.
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{Provider} could not fetch the first prices", Descriptor.DisplayName);
        }
    }

    /// <summary>
    /// Polls REST for every subscribed instrument while the stream is not connected, and, more slowly, while it is
    /// connected but quiet (see <see cref="QuietStreamInterval"/>). Backs off when the service pushes back. It wakes every
    /// poll interval and decides then, so that a wait grown by a push back ends with the outage that caused it.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token that ends the loop.
    /// </param>
    /// <returns>
    /// A task that completes when the loop is cancelled; it never throws.
    /// </returns>
    private async Task RunFallbackAsync(CancellationToken cancellationToken)
    {
        var wait = _fallbackInterval;
        long? lastPoll = null;
        var seenMessages = MessageCount;
        _quietRounds = 0;
        _requiredQuietRounds = QuietRoundsBeforeReconnect;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_fallbackInterval, TimeProvider, cancellationToken).ConfigureAwait(false);
                var streamUp = StreamConnected;
                if (streamUp)
                {
                    wait = _fallbackInterval;
                }

                var messages = MessageCount;
                if (messages != seenMessages)
                {
                    // The stream says something, so it works: what was counted starts over, and a reconnect is as soon again.
                    seenMessages = messages;
                    _quietRounds = 0;
                    _requiredQuietRounds = QuietRoundsBeforeReconnect;
                }
                else if (!streamUp)
                {
                    _quietRounds = 0;
                }

                // A market that is not trading has nothing new, and the stream delivers the pre-market and after hours.
                var subscribed = CurrentSubscriptions;
                var assets = subscribed.Where(a => !IsOutsideTrading(a)).ToArray();
                SetOutsideTrading(subscribed.Count > 0 && assets.Length == 0);

                var due = streamUp
                    ? IdleFor >= QuietStreamInterval && (lastPoll is not { } quiet || TimeProvider.GetElapsedTime(quiet) >= QuietStreamInterval)
                    : lastPoll is not { } polled || TimeProvider.GetElapsedTime(polled) >= wait;
                if (!due)
                {
                    continue;
                }

                if (assets.Length == 0)
                {
                    continue;
                }

                lastPoll = TimeProvider.GetTimestamp();
                var advancesBefore = Interlocked.Read(ref _advances);
                var messagesBefore = MessageCount;
                try
                {
                    var outcome = await FetchQuotesAsync(assets, cancellationToken).ConfigureAwait(false);
                    wait = outcome.BackOff ? Backoff.Double(wait, MaxFallbackInterval) : _fallbackInterval;
                    RecordRound(outcome, streamUp);
                    if (streamUp && outcome.Succeeded > 0)
                    {
                        CountQuietRound(Interlocked.Read(ref _advances) > advancesBefore, messagesBefore);
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    // A derived class that lets a failure escape must not end the loop; treat it as a push back.
                    Logger.LogWarning(ex, "{Provider} fallback poll failed", Descriptor.DisplayName);
                    wait = Backoff.Double(wait, MaxFallbackInterval);
                    RecordRound(new PollOutcome(0, 1, ex.Message, BackOff: true), streamUp);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The subscriptions ended or the provider is shutting down.
        }
    }

    /// <summary>
    /// Records whether every subscribed instrument is outside trading, and reports the status again when that changed.
    /// </summary>
    /// <param name="value">
    /// Whether there is nothing to fetch because no market is open.
    /// </param>
    private void SetOutsideTrading(bool value)
    {
        if (Volatile.Read(ref _outsideTrading) == value)
        {
            return;
        }

        Volatile.Write(ref _outsideTrading, value);
        RefreshStatus();
    }

    /// <summary>
    /// Takes note of a round that polled a connected stream that had been quiet: the round found newer prices of live
    /// trading while the stream still said nothing, or it did not. Enough rounds of the first kind in a row mean the
    /// connection no longer delivers, and it is opened again.
    /// </summary>
    /// <param name="foundNewer">
    /// Whether this round found a price of live trading that moved forward.
    /// </param>
    /// <param name="messagesBefore">
    /// The number of messages the stream had delivered when the round began.
    /// </param>
    private void CountQuietRound(bool foundNewer, long messagesBefore)
    {
        // The stream may have spoken while the round ran; only a stream that stayed quiet throughout counts.
        if (!foundNewer || MessageCount != messagesBefore)
        {
            _quietRounds = 0;
            return;
        }

        _quietRounds++;
        if (_quietRounds < _requiredQuietRounds)
        {
            return;
        }

        Logger.LogWarning(
            "{Provider} stream has said nothing for {Quiet:0} s while polling finds prices of live trading; connecting again",
            Descriptor.DisplayName,
            IdleFor.TotalSeconds);
        _quietRounds = 0;
        _requiredQuietRounds = Math.Min(_requiredQuietRounds * 2, QuietRoundsBeforeReconnect * MaxEscalation);
        ReconnectStream();
    }

    /// <summary>
    /// Takes note of how a round that covered every instrument went and reports the status that follows.
    /// </summary>
    /// <param name="outcome">
    /// How the round went.
    /// </param>
    /// <param name="streamUp">
    /// Whether the stream was connected, so that the round says nothing about whether prices arrive without it.
    /// </param>
    private void RecordRound(PollOutcome outcome, bool streamUp)
    {
        Volatile.Write(ref _restProblems, new RestProblems(outcome.Problems, outcome.Problems is null ? outcome.LastError : null));

        if (!streamUp)
        {
            if (outcome.Succeeded > 0)
            {
                Volatile.Write(ref _lastRestSuccess, TimeProvider.GetTimestamp());
                Volatile.Write(ref _failedRounds, 0);
            }
            else if (outcome.Failed > 0)
            {
                Interlocked.Increment(ref _failedRounds);
            }
        }

        RefreshStatus();
    }

    /// <summary>
    /// Takes the problems of a snapshot into the current ones, replacing those of the instruments it covered.
    /// </summary>
    /// <param name="assets">
    /// The instruments the snapshot covered.
    /// </param>
    /// <param name="outcome">
    /// How the snapshot went.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the problems changed.
    /// </returns>
    private bool MergeProblems(IReadOnlyCollection<Asset> assets, PollOutcome outcome)
    {
        lock (_problemLock)
        {
            var current = Volatile.Read(ref _restProblems);
            if (outcome.Problems is null)
            {
                if (outcome.LastError is not { } error)
                {
                    return false;
                }

                Volatile.Write(ref _restProblems, new RestProblems(current.ByAsset, error));
                return true;
            }

            var merged = current.ByAsset is null ? [] : new Dictionary<AssetId, string>(current.ByAsset);
            foreach (var asset in assets)
            {
                merged.Remove(asset.Id);
            }

            foreach (var (id, message) in outcome.Problems)
            {
                merged[id] = message;
            }

            Volatile.Write(ref _restProblems, new RestProblems(merged, current.General));
            return true;
        }
    }

    /// <summary>
    /// Works out the problem to show: those REST reported for instruments that are still subscribed.
    /// </summary>
    /// <returns>
    /// The text, or <see langword="null"/> when there is none.
    /// </returns>
    private string? CurrentProblem()
    {
        var problems = Volatile.Read(ref _restProblems);
        if (problems.ByAsset is null)
        {
            return problems.General;
        }

        var messages = new List<string>(MaxShownProblems);
        foreach (var (id, message) in problems.Ordered)
        {
            if (IsSubscribed(id) && !messages.Contains(message))
            {
                messages.Add(message);
                if (messages.Count == MaxShownProblems)
                {
                    break;
                }
            }
        }

        return messages.Count == 0 ? problems.General : string.Join("; ", messages);
    }

    /// <summary>
    /// The problems REST reported.
    /// </summary>
    /// <param name="ByAsset">
    /// The problem of each instrument that has one, or <see langword="null"/> when the source does not tell them apart.
    /// </param>
    /// <param name="General">
    /// A problem that is not about one instrument, or <see langword="null"/>.
    /// </param>
    private sealed record RestProblems(IReadOnlyDictionary<AssetId, string>? ByAsset, string? General)
    {
        /// <summary>
        /// Gets the problems of the instruments in the order they are shown, worked out once when they are reported.
        /// </summary>
        public KeyValuePair<AssetId, string>[] Ordered { get; } =
            ByAsset is null ? [] : [.. ByAsset.OrderBy(entry => entry.Key.Symbol, StringComparer.Ordinal)];

        /// <summary>
        /// Gets the state without problems.
        /// </summary>
        public static RestProblems None { get; } = new(null, null);
    }
}
