using FluxTick.Abstractions;
using FluxTick.Providers.Common;

namespace FluxTick.Core.Tests.TestDoubles;

/// <summary>
/// A controllable provider for tests: records subscriptions and lets tests emit quotes and status changes.
/// </summary>
/// <param name="descriptor">
/// The provider descriptor.
/// </param>
/// <param name="options">
/// The options the provider was created with.
/// </param>
public sealed class FakeProvider(ProviderDescriptor descriptor, ProviderOptions options)
    : MarketDataProviderBase(descriptor, TimeProvider.System), IHistoryProvider, ISymbolSearchProvider, IMarketHoursProvider
{
    /// <summary>
    /// Gets the options the provider was created with.
    /// </summary>
    public ProviderOptions Options { get; } = options;

    /// <summary>
    /// Gets the subscription set after the latest change.
    /// </summary>
    public IReadOnlyCollection<Asset> Subscribed => CurrentSubscriptions;

    /// <summary>
    /// Gets or sets the history returned by <see cref="GetHistoryAsync"/>.
    /// </summary>
    public IReadOnlyList<PricePoint> History { get; set; } = [];

    /// <summary>
    /// The number of calls to <see cref="GetHistoryAsync"/>.
    /// </summary>
    private int _historyRequests;

    /// <summary>
    /// Gets the number of times the history was asked for.
    /// </summary>
    public int HistoryRequests => Volatile.Read(ref _historyRequests);

    /// <summary>
    /// Gets or sets an exception that <see cref="GetHistoryAsync"/> fails with, or <see langword="null"/> for none.
    /// </summary>
    public Exception? HistoryFailure { get; set; }

    /// <summary>
    /// Gets or sets an exception thrown when the subscriptions change, or <see langword="null"/> for none.
    /// </summary>
    public Exception? SubscribeFailure { get; set; }

    /// <summary>
    /// Gets or sets the assets returned by <see cref="SearchAsync"/>.
    /// </summary>
    public IReadOnlyList<Asset> SearchResults { get; set; } = [];

    /// <summary>
    /// Gets or sets the price of a quote that is published for every instrument the moment it is subscribed, like a source
    /// that sends its first price right away, or <see langword="null"/> to publish nothing.
    /// </summary>
    public decimal? QuoteOnSubscribe { get; set; }

    /// <summary>
    /// Gets a value indicating whether the provider was disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Publishes a quote as if it arrived from the data source.
    /// </summary>
    /// <param name="quote">
    /// The quote to publish.
    /// </param>
    public void Emit(Quote quote) => Publish(quote);

    /// <summary>
    /// Gets or sets the exchange whose hours the provider reports for every instrument, or <see langword="null"/> for a
    /// provider that does not know the hours of its markets.
    /// </summary>
    public ExchangeHours? Hours { get; set; }

    /// <inheritdoc />
    public MarketState GetMarketState(Asset asset, DateTimeOffset now, DateTimeOffset? lastTradeTime = null, TimeSpan? holidayGrace = null) =>
        Hours?.GetState(now, lastTradeTime, holidayGrace) ?? MarketState.Unknown;

    /// <summary>
    /// Changes the reported status.
    /// </summary>
    /// <param name="state">
    /// The new state.
    /// </param>
    /// <param name="message">
    /// Optional detail.
    /// </param>
    public void ChangeStatus(ProviderConnectionState state, string? message = null) => SetStatus(state, message);

    /// <inheritdoc />
    public Task<IReadOnlyList<PricePoint>> GetHistoryAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _historyRequests);
        return HistoryFailure is { } failure ? Task.FromException<IReadOnlyList<PricePoint>>(failure) : Task.FromResult(History);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Asset>> SearchAsync(string query, AssetClass? assetClass, CancellationToken cancellationToken = default) =>
        Task.FromResult(SearchResults);

    /// <inheritdoc />
    protected override Task OnSubscriptionsChangedAsync(
        IReadOnlyCollection<Asset> current,
        IReadOnlyCollection<Asset> added,
        IReadOnlyCollection<Asset> removed,
        CancellationToken cancellationToken)
    {
        if (SubscribeFailure is { } failure)
        {
            throw failure;
        }

        // Like the real providers: nothing subscribed means nothing connected.
        if (current.Count == 0)
        {
            SetStatus(ProviderConnectionState.Idle);
        }

        if (QuoteOnSubscribe is { } price)
        {
            foreach (var asset in added)
            {
                Publish(new Quote(asset.Id, price, null, TimeProvider.GetUtcNow()));
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override ValueTask DisposeCoreAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A factory that records every provider it creates.
/// </summary>
public sealed class FakeProviderFactory : IMarketDataProviderFactory
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FakeProviderFactory"/> class.
    /// </summary>
    /// <param name="id">
    /// The provider id.
    /// </param>
    /// <param name="classes">
    /// The supported asset classes.
    /// </param>
    /// <param name="enabledByDefault">
    /// Whether the provider is enabled when no setting exists.
    /// </param>
    /// <param name="pollInterval">
    /// The poll interval the provider declares as its option, or <see langword="null"/> for none.
    /// </param>
    public FakeProviderFactory(string id, AssetClass[] classes, bool enabledByDefault = true, string? pollInterval = null)
    {
        Descriptor = new ProviderDescriptor(
            id,
            id.ToUpperInvariant(),
            "Test provider",
            classes,
            ProviderUpdateMode.Streaming,
            Options:
            [
                new ProviderOptionDefinition("Endpoint", "Endpoint", ProviderOptionType.String, "default"),
                .. pollInterval is null ? Array.Empty<ProviderOptionDefinition>() : [new ProviderOptionDefinition("PollInterval", "Poll interval", ProviderOptionType.Duration, pollInterval)],
            ],
            EnabledByDefault: enabledByDefault);
    }

    /// <summary>
    /// Gets the providers created so far, oldest first.
    /// </summary>
    public List<FakeProvider> Created { get; } = [];

    /// <summary>
    /// Gets or sets the exchange whose hours the created providers report, or <see langword="null"/> for none.
    /// </summary>
    public ExchangeHours? Hours { get; set; }

    /// <summary>
    /// Gets or sets the exception the history of the providers created from now on fails with, or <see langword="null"/>.
    /// </summary>
    public Exception? HistoryFailure { get; set; }

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; }

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context)
    {
        var provider = new FakeProvider(Descriptor, context.Options) { HistoryFailure = HistoryFailure, Hours = Hours };
        Created.Add(provider);
        return provider;
    }
}
