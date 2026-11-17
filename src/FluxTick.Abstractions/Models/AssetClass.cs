namespace FluxTick.Abstractions;

/// <summary>
/// Broad category of a tradable instrument. Used for routing and display.
/// </summary>
public enum AssetClass
{
    /// <summary>
    /// Digital assets such as BTC or ETH.
    /// </summary>
    Crypto,

    /// <summary>
    /// Stocks and ETFs.
    /// </summary>
    Equity,

    /// <summary>
    /// Market indices such as the S&amp;P 500.
    /// </summary>
    Index,

    /// <summary>
    /// Currency pairs.
    /// </summary>
    Forex,

    /// <summary>
    /// Commodities and futures such as gold or crude oil.
    /// </summary>
    Commodity,
}
