using FluxTick.Abstractions;

namespace FluxTick.Core.Domain;

/// <summary>
/// A store that keeps nothing, for the demo mode and for tests that do not care about history.
/// </summary>
public sealed class NullQuoteHistoryStore : IQuoteHistoryStore
{
    /// <summary>
    /// Gets the only instance.
    /// </summary>
    public static NullQuoteHistoryStore Instance { get; } = new();

    /// <inheritdoc />
    public void Record(AssetId assetId, decimal price, DateTimeOffset time)
    {
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PricePoint>> ReadAsync(AssetId assetId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PricePoint>>([]);

    /// <inheritdoc />
    public Task MergeAsync(AssetId assetId, IReadOnlyList<PricePoint> points, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    public void Configure(bool enabled, TimeSpan retention)
    {
    }
}
