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
    /// Publishes a quote to the reader. Quotes for instruments that are no longer subscribed are dropped.
    /// </summary>
    /// <param name="quote">
    /// The quote to publish.
    /// </param>
    protected void Publish(Quote quote)
    {
        if (Volatile.Read(ref _subscriptions).ContainsKey(quote.AssetId))
        {
            _channel.Writer.TryWrite(quote);
        }
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
            StatusChanged?.Invoke(this, next);
        }
    }
}
