using FluxTick.Abstractions;

namespace FluxTick.Providers.Common;

/// <summary>
/// Builds the quotes of crypto exchange sources. A market that never closes has no previous close, so every such source
/// measures the change over a sliding 24 hours; building the quote here keeps that in one place.
/// </summary>
public static class CryptoQuote
{
    /// <summary>
    /// Creates a quote whose reference is the price 24 hours ago.
    /// </summary>
    /// <param name="assetId">
    /// The instrument the price belongs to.
    /// </param>
    /// <param name="price">
    /// The latest traded price.
    /// </param>
    /// <param name="reference">
    /// The price 24 hours ago, or <see langword="null"/> when the source gave none.
    /// </param>
    /// <param name="timestamp">
    /// The trade time, or the time the message was received when the source gave none.
    /// </param>
    /// <param name="timeIsEstimated">
    /// Whether <paramref name="timestamp"/> is the time of receipt rather than of the trade.
    /// </param>
    /// <returns>
    /// The quote, with <see cref="Quote.Basis"/> set to <see cref="ReferenceBasis.Rolling24Hours"/>.
    /// </returns>
    public static Quote Create(AssetId assetId, decimal price, decimal? reference, DateTimeOffset timestamp, bool timeIsEstimated = false) =>
        new(assetId, price, reference, timestamp)
        {
            TimeIsEstimated = timeIsEstimated,
            Basis = ReferenceBasis.Rolling24Hours,
        };
}
