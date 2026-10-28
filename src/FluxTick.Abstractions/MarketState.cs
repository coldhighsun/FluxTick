namespace FluxTick.Abstractions;

/// <summary>
/// Whether the market of an instrument is trading.
/// </summary>
public enum MarketState
{
    /// <summary>
    /// The market of the instrument is not known, so nothing is said about it.
    /// </summary>
    Unknown,

    /// <summary>
    /// The regular session is under way.
    /// </summary>
    Open,

    /// <summary>
    /// Before the regular session, when the market takes orders or trades in a limited way.
    /// </summary>
    PreMarket,

    /// <summary>
    /// After the regular session, when trading goes on in a limited way.
    /// </summary>
    AfterHours,

    /// <summary>
    /// The pause between two sessions of the same day.
    /// </summary>
    Break,

    /// <summary>
    /// The market is shut.
    /// </summary>
    Closed,
}
