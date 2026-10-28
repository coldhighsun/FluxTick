using System.Threading.Channels;

namespace FluxTick.Abstractions;

/// <summary>
/// Base class that implements the plumbing every provider needs: the quote channel, status reporting,
/// subscription diffing and disposal. Derive from it and implement <see cref="OnSubscriptionsChangedAsync"/>.
/// </summary>
/// <param name="descriptor">
/// The self-description of the provider.
/// </param>
/// <param name="timeProvider">
/// The clock to use.
/// </param>
public abstract class MarketDataProviderBase(ProviderDescriptor descriptor, TimeProvider timeProvider) : IMarketDataProvider
{
    /// <summary>
    /// The channel that carries published quotes to the single reader.
    /// </summary>
    private readonly Channel<Quote> _channel = Channel.CreateUnbounded<Quote>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>
    /// Serializes subscription changes.
    /// </summary>
    private readonly SemaphoreSlim _subscriptionLock = new(1, 1);

    /// <summary>
    /// Cancelled when the provider is disposed.
    /// </summary>
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>
    /// The current subscriptions, replaced atomically.
    /// </summary>
    private IReadOnlyDictionary<AssetId, Asset> _subscriptions = new Dictionary<AssetId, Asset>();

    /// <summary>
    /// The current subscriptions by <see cref="KeyOf"/>, without regard to case, replaced atomically.
    /// </summary>
    private IReadOnlyDictionary<string, Asset[]> _byKey = new Dictionary<string, Asset[]>();

    /// <summary>
    /// How many quotes the current thread has published, so that a derived class can tell whether handling one message
    /// published a quote, whatever other threads publish meanwhile.
    /// </summary>
    [ThreadStatic]
    private static long _publishedByThisThread;

    /// <summary>
    /// The current status, replaced atomically.
    /// </summary>
    private ProviderStatus _status = new(ProviderConnectionState.Idle);

    /// <summary>
    /// Set to 1 once disposal started.
    /// </summary>
    private int _disposed;

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = descriptor;

    /// <inheritdoc />
    public ProviderStatus Status => Volatile.Read(ref _status);

    /// <inheritdoc />
    public event EventHandler<ProviderStatus>? StatusChanged;

    /// <summary>
    /// Gets the clock to use for timestamps and timers.
    /// </summary>
    protected TimeProvider TimeProvider { get; } = timeProvider;

    /// <summary>
    /// Gets a token that is cancelled when the provider is disposed. Background loops must observe it.
    /// </summary>
    protected CancellationToken ShutdownToken => _shutdown.Token;

    /// <summary>
    /// Gets a snapshot of the current subscriptions.
    /// </summary>
    protected IReadOnlyCollection<Asset> CurrentSubscriptions => Volatile.Read(ref _subscriptions).Values.ToArray();

    /// <inheritdoc />
    public async Task SetSubscriptionsAsync(IReadOnlyCollection<Asset> assets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        await _subscriptionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var next = assets
                .Where(a => a.ProviderId == Descriptor.Id)
                .GroupBy(a => a.Id)
                .ToDictionary(g => g.Key, g => g.First());
            var previous = _subscriptions;

            var added = next.Where(p => !previous.ContainsKey(p.Key)).Select(p => p.Value).ToArray();
            var removed = previous.Where(p => !next.ContainsKey(p.Key)).Select(p => p.Value).ToArray();
            if (added.Length == 0 && removed.Length == 0)
            {
                return;
            }

            Volatile.Write(ref _subscriptions, next);

            // Built before the derived class hears of the change, so a message that arrives as soon as it subscribes
            // already finds its instruments.
            Volatile.Write(
                ref _byKey,
                next.Values.GroupBy(KeyOf, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase));
            await OnSubscriptionsChangedAsync(next.Values.ToArray(), added, removed, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _subscriptionLock.Release();
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<Quote> ReadQuotesAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _shutdown.CancelAsync().ConfigureAwait(false);
        _channel.Writer.TryComplete();
        try
        {
            await DisposeCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _shutdown.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Called, serialized, whenever the set of subscriptions changed. Start, stop or adjust network activity here.
    /// </summary>
    /// <param name="current">
    /// The full subscription set after the change.
    /// </param>
    /// <param name="added">
    /// The instruments that were added.
    /// </param>
    /// <param name="removed">
    /// The instruments that were removed.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the operation.
    /// </param>
    /// <returns>
    /// A task that completes once the change is applied.
    /// </returns>
    protected abstract Task OnSubscriptionsChangedAsync(
        IReadOnlyCollection<Asset> current,
        IReadOnlyCollection<Asset> added,
        IReadOnlyCollection<Asset> removed,
        CancellationToken cancellationToken);

    /// <summary>
    /// Called once on disposal after <see cref="ShutdownToken"/> was cancelled. Await background loops here.
    /// </summary>
    /// <returns>
    /// A task that completes when resources are released.
    /// </returns>
    protected virtual ValueTask DisposeCoreAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Gets the key a pushed or fetched message names an instrument by, which <see cref="PublishToSubscribers"/> looks
    /// instruments up with. The default is the symbol; override it for a source that names instruments differently from
    /// how they are stored, for example in another form of the symbol.
    /// </summary>
    /// <param name="asset">
    /// The subscribed instrument.
    /// </param>
    /// <returns>
    /// The key, compared without regard to case.
    /// </returns>
    protected virtual string KeyOf(Asset asset) => asset.Symbol;

    /// <summary>
    /// Publishes a price for the instrument that was subscribed, under the symbol it was subscribed with. A quote
    /// without a trade time is stamped with the current time and marked as estimated.
    /// </summary>
    /// <param name="asset">
    /// The subscribed instrument.
    /// </param>
    /// <param name="price">
    /// The last price.
    /// </param>
    /// <param name="reference">
    /// The previous close, or <see langword="null"/> when unknown.
    /// </param>
    /// <param name="tradedAt">
    /// When the price was traded, or <see langword="null"/> when the message carries no usable time.
    /// </param>
    /// <param name="market">
    /// The trading state the source reports, or <see langword="null"/> when it reports none.
    /// </param>
    protected void Publish(Asset asset, decimal price, decimal? reference, DateTimeOffset? tradedAt, MarketState? market = null) =>
        Publish(new Quote(asset.Id, price, reference, tradedAt ?? TimeProvider.GetUtcNow())
        {
            Market = market,
            TimeIsEstimated = tradedAt is null,
        });

    /// <summary>
    /// Publishes a price for every subscribed instrument whose <see cref="KeyOf"/> is the given key.
    /// </summary>
    /// <param name="key">
    /// The key the message names the instrument by, compared without regard to case.
    /// </param>
    /// <param name="price">
    /// The last price.
    /// </param>
    /// <param name="reference">
    /// The previous close, or <see langword="null"/> when unknown.
    /// </param>
    /// <param name="tradedAt">
    /// When the price was traded, or <see langword="null"/> when the message carries no usable time.
    /// </param>
    /// <param name="market">
    /// The trading state the source reports, or <see langword="null"/> when it reports none.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an instrument with that key is subscribed.
    /// </returns>
    protected bool PublishToSubscribers(string key, decimal price, decimal? reference, DateTimeOffset? tradedAt, MarketState? market = null)
    {
        if (!Volatile.Read(ref _byKey).TryGetValue(key, out var assets))
        {
            return false;
        }

        foreach (var asset in assets)
        {
            Publish(asset, price, reference, tradedAt, market);
        }

        return true;
    }

    /// <summary>
    /// Publishes a quote to the reader. Quotes for instruments that are no longer subscribed are dropped.
    /// </summary>
    /// <param name="quote">
    /// The quote to publish.
    /// </param>
    protected void Publish(Quote quote)
    {
        if (Volatile.Read(ref _subscriptions).ContainsKey(quote.AssetId) && _channel.Writer.TryWrite(quote))
        {
            _publishedByThisThread++;
            OnQuotePublished(quote);
        }
    }

    /// <summary>
    /// Gets how many quotes the calling thread has published so far. The difference across a call tells whether that call
    /// published a quote, which quotes published by other threads do not disturb. It only sees quotes published
    /// synchronously on the calling thread, so a caller that needs it states that as a requirement of its callee.
    /// </summary>
    protected static long PublishedByThisThread => _publishedByThisThread;

    /// <summary>
    /// Tells whether an instrument is subscribed at this moment.
    /// </summary>
    /// <param name="id">
    /// The instrument.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when it is in the current subscriptions.
    /// </returns>
    protected bool IsSubscribed(AssetId id) => Volatile.Read(ref _subscriptions).ContainsKey(id);

    /// <summary>
    /// Called after a quote was accepted by <see cref="Publish(Quote)"/>, on the thread that published it. A derived class
    /// that wants to follow what is published overrides it; it must be quick and must not throw.
    /// </summary>
    /// <param name="quote">
    /// The quote that was published.
    /// </param>
    protected virtual void OnQuotePublished(Quote quote)
    {
    }

    /// <summary>
    /// Called when a listener of <see cref="StatusChanged"/> threw; the exception is not propagated. Override to log it.
    /// </summary>
    /// <param name="exception">
    /// The exception of the listener.
    /// </param>
    protected virtual void OnStatusListenerFailed(Exception exception)
    {
    }

    /// <summary>
    /// Updates the status and raises <see cref="StatusChanged"/> when it changed.
    /// </summary>
    /// <param name="state">
    /// The new state.
    /// </param>
    /// <param name="message">
    /// Optional detail.
    /// </param>
    protected void SetStatus(ProviderConnectionState state, string? message = null)
    {
        var next = new ProviderStatus(state, message);
        var previous = Interlocked.Exchange(ref _status, next);
        if (previous != next)
        {
            try
            {
                StatusChanged?.Invoke(this, next);
            }
            catch (Exception ex)
            {
                // A failing listener must not end the polling or reconnect loop that reports the status.
                OnStatusListenerFailed(ex);
            }
        }
    }
}
