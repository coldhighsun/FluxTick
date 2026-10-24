using FluxTick.Abstractions;

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
    : MarketDataProviderBase(descriptor, TimeProvider.System), IHistoryProvider, ISymbolSearchProvider
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
    public Task<IReadOnlyList<PricePoint>> GetHistoryAsync(Asset asset, CancellationToken cancellationToken = default) =>
        Task.FromResult(History);

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
    public FakeProviderFactory(string id, AssetClass[] classes, bool enabledByDefault = true)
    {
        Descriptor = new ProviderDescriptor(
            id,
            id.ToUpperInvariant(),
            "Test provider",
            classes,
            ProviderUpdateMode.Streaming,
            Options: [new ProviderOptionDefinition("Endpoint", "Endpoint", ProviderOptionType.String, "default")],
            EnabledByDefault: enabledByDefault);
    }

    /// <summary>
    /// Gets the providers created so far, oldest first.
    /// </summary>
    public List<FakeProvider> Created { get; } = [];

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; }

    /// <inheritdoc />
    public IMarketDataProvider Create(ProviderCreationContext context)
    {
        var provider = new FakeProvider(Descriptor, context.Options);
        Created.Add(provider);
        return provider;
    }
}
