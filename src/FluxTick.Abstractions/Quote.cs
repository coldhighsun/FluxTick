namespace FluxTick.Abstractions;

/// <summary>
/// A single price observation for an instrument.
/// </summary>
/// <param name="AssetId">
/// The instrument the price belongs to.
/// </param>
/// <param name="Price">
/// The latest traded price.
/// </param>
/// <param name="Reference">
/// The price changes are measured against (previous close or 24h open), or <see langword="null"/> when unknown.
/// </param>
/// <param name="Timestamp">
/// When the price was observed.
/// </param>
public sealed record Quote(AssetId AssetId, decimal Price, decimal? Reference, DateTimeOffset Timestamp)
{
    /// <summary>
    /// Gets the absolute change versus <see cref="Reference"/>, or <see langword="null"/> when no reference is known.
    /// </summary>
    public decimal? Change => Reference is null ? null : Price - Reference.Value;

    /// <summary>
    /// Gets the relative change versus <see cref="Reference"/> in percent, or <see langword="null"/> when it cannot be computed.
    /// </summary>
    public decimal? ChangePercent => Reference is null or 0m ? null : (Price - Reference.Value) / Reference.Value * 100m;
}

/// <summary>
/// A historical price sample used to seed sparklines.
/// </summary>
/// <param name="Timestamp">
/// When the sample was taken.
/// </param>
/// <param name="Price">
/// The price at that time.
/// </param>
public readonly record struct PricePoint(DateTimeOffset Timestamp, decimal Price);
