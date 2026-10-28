using FluxTick.Abstractions;

namespace FluxTick.Providers.Common;

/// <summary>
/// The rule the provider bases share for not asking a source for prices of a market that has nothing new to give.
/// </summary>
public static class TradingHoursPolicy
{
    /// <summary>
    /// Tells whether the market of an instrument is known not to be trading, so that fetching its price is pointless. Only
    /// stocks and indices have exchange hours; crypto, forex and futures trade around the clock or by their own rules, so
    /// they are always fetched, whatever hours a source would give their symbols. An instrument whose market the source
    /// does not know is fetched as usual.
    /// </summary>
    /// <param name="hours">
    /// The source, or <see langword="null"/> when it does not know the hours of its markets.
    /// </param>
    /// <param name="asset">
    /// The instrument.
    /// </param>
    /// <param name="now">
    /// The current time.
    /// </param>
    /// <param name="hasStream">
    /// Whether a live stream delivers the prices of the pre-market and after hours. Without one, only a shut market or a
    /// break has nothing to fetch; with one, a market that is not open has nothing to fetch over REST either.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when nothing needs to be fetched.
    /// </returns>
    public static bool IsOutsideTrading(IMarketHoursProvider? hours, Asset asset, DateTimeOffset now, bool hasStream)
    {
        if (hours is null || asset.AssetClass is not (AssetClass.Equity or AssetClass.Index))
        {
            return false;
        }

        return hours.GetMarketState(asset, now) switch
        {
            MarketState.Closed or MarketState.Break => true,
            MarketState.PreMarket or MarketState.AfterHours => hasStream,
            _ => false,
        };
    }
}
