namespace FluxTick.Abstractions;

/// <summary>
/// Uniquely identifies an instrument as the pair of the provider that serves it and its provider-native symbol.
/// </summary>
/// <param name="ProviderId">
/// The identifier of the provider that serves the instrument.
/// </param>
/// <param name="Symbol">
/// The provider-native symbol, for example <c>BTCUSDT</c> or <c>AAPL</c>.
/// </param>
public readonly record struct AssetId(string ProviderId, string Symbol)
{
    /// <summary>
    /// Returns the identifier in <c>provider:symbol</c> form.
    /// </summary>
    /// <returns>
    /// The textual representation of the identifier.
    /// </returns>
    public override string ToString() => $"{ProviderId}:{Symbol}";
}

/// <summary>
/// An instrument that can be tracked on the watchlist.
/// </summary>
/// <param name="ProviderId">
/// The identifier of the provider that serves the instrument.
/// </param>
/// <param name="Symbol">
/// The provider-native symbol.
/// </param>
/// <param name="DisplayName">
/// A human friendly name, for example a company name.
/// </param>
/// <param name="AssetClass">
/// The broad category of the instrument.
/// </param>
public sealed record Asset(string ProviderId, string Symbol, string DisplayName, AssetClass AssetClass)
{
    /// <summary>
    /// Gets the unique identifier of the instrument.
    /// </summary>
    public AssetId Id => new(ProviderId, Symbol);
}
