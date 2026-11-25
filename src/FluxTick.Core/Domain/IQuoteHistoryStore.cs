using FluxTick.Abstractions;

namespace FluxTick.Core.Domain;

/// <summary>
/// Keeps the prices that were shown for an instrument, so that a chart can start from what was seen before the
/// application was last closed and does not have to ask the source for what it already has.
/// </summary>
public interface IQuoteHistoryStore
{
    /// <summary>
    /// Notes a price. It returns at once and never fails; the price reaches the disk a little later.
    /// </summary>
    /// <param name="assetId">
    /// The instrument the price belongs to.
    /// </param>
    /// <param name="price">
    /// The price.
    /// </param>
    /// <param name="time">
    /// When the price was traded, as the chart places it.
    /// </param>
    void Record(AssetId assetId, decimal price, DateTimeOffset time);

    /// <summary>
    /// Reads the stored prices of an instrument that lie in a time range.
    /// </summary>
    /// <param name="assetId">
    /// The instrument to read.
    /// </param>
    /// <param name="from">
    /// The start of the range, inclusive.
    /// </param>
    /// <param name="to">
    /// The end of the range, inclusive.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the read.
    /// </param>
    /// <returns>
    /// The prices, oldest first; empty when nothing is stored or the store is off.
    /// </returns>
    Task<IReadOnlyList<PricePoint>> ReadAsync(AssetId assetId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds prices that a source answered to what is stored, so that the next read of the same time range does not need
    /// to ask the source again.
    /// </summary>
    /// <param name="assetId">
    /// The instrument the prices belong to.
    /// </param>
    /// <param name="points">
    /// The prices, in any order; they may be older than what is stored.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the merge.
    /// </param>
    /// <returns>
    /// A task that completes when the prices are stored.
    /// </returns>
    Task MergeAsync(AssetId assetId, IReadOnlyList<PricePoint> points, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the user's choice of whether to keep prices and for how long.
    /// </summary>
    /// <param name="enabled">
    /// Whether prices are stored and read.
    /// </param>
    /// <param name="retention">
    /// How long a price is kept.
    /// </param>
    void Configure(bool enabled, TimeSpan retention);
}
