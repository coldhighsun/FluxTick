namespace FluxTick.Abstractions;

/// <summary>
/// What the <see cref="Quote.Reference"/> price of a quote is the price of. It tells how the change of a quote is to be read.
/// </summary>
public enum ReferenceBasis
{
    /// <summary>
    /// The close of the previous trading session. It stays the same through the session and moves on with the next one.
    /// </summary>
    PreviousClose,

    /// <summary>
    /// The price 24 hours ago, a window that slides with the clock. It is what a market that never closes uses, because it
    /// has no session to take a close from.
    /// </summary>
    Rolling24Hours,
}
