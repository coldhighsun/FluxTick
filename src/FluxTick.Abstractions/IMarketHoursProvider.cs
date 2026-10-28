namespace FluxTick.Abstractions;

/// <summary>
/// Implemented by a provider that knows the trading hours of the markets its instruments belong to. Which exchange an
/// instrument trades on follows from the symbols of the source, so the hours belong to the source and not to the
/// application. The application asks for the state as it changes with the clock, so a market that opens or closes shows
/// without a new quote. A source that reports the state in its quotes (see <see cref="Quote.Market"/>) can implement it
/// as well; the hours then check the report.
/// </summary>
public interface IMarketHoursProvider
{
    /// <summary>
    /// Gets the trading state of the market of an instrument from the regular hours of the market. Public holidays are
    /// not listed; they show up as a regular session in which nothing has traded yet that day.
    /// </summary>
    /// <param name="asset">
    /// The instrument.
    /// </param>
    /// <param name="now">
    /// The current time.
    /// </param>
    /// <param name="lastTradeTime">
    /// When the instrument last traded, or <see langword="null"/> when unknown.
    /// </param>
    /// <param name="holidayGrace">
    /// How long a session has to be under way before a last trade from an earlier day means that the market is shut, or
    /// <see langword="null"/> for the default.
    /// </param>
    /// <returns>
    /// The state, or <see cref="MarketState.Unknown"/> when the market of the instrument is not known.
    /// </returns>
    MarketState GetMarketState(Asset asset, DateTimeOffset now, DateTimeOffset? lastTradeTime = null, TimeSpan? holidayGrace = null);
}
