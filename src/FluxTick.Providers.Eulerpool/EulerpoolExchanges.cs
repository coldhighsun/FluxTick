using FluxTick.Abstractions;
using FluxTick.Providers.Common;

namespace FluxTick.Providers.Eulerpool;

/// <summary>
/// The exchanges Eulerpool lists, by the suffix of the ticker (<c>SAP.DE</c>). The stream does not say whether a market
/// is open, so the provider works it out from these hours. An exchange that is not listed here has no state.
/// </summary>
internal static class EulerpoolExchanges
{
    /// <summary>
    /// The exchanges by ticker suffix, without the dot.
    /// </summary>
    private static readonly Dictionary<string, ExchangeHours> Suffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DE"] = Continuous(["Europe/Berlin", "W. Europe Standard Time"], Hm(9, 0), Hm(17, 30)),
        ["L"] = Continuous(["Europe/London", "GMT Standard Time"], Hm(8, 0), Hm(16, 30)),
        ["PA"] = Continuous(["Europe/Paris", "Romance Standard Time"], Hm(9, 0), Hm(17, 30)),
        ["TO"] = Continuous(["America/Toronto", "Eastern Standard Time"], Hm(9, 30), Hm(16, 0)),
        ["HK"] = ExchangeHours.HongKong,
    };

    /// <summary>
    /// Makes an exchange with one trading session a day and no break.
    /// </summary>
    /// <param name="zoneIds">
    /// The identifiers of the time zone, tried in turn as systems name zones differently.
    /// </param>
    /// <param name="open">
    /// The opening time, local time.
    /// </param>
    /// <param name="close">
    /// The closing time, local time.
    /// </param>
    /// <returns>
    /// The exchange.
    /// </returns>
    private static ExchangeHours Continuous(string[] zoneIds, TimeSpan open, TimeSpan close) =>
        new(zoneIds, [new TradingSession(open, close, MarketState.Open)]);

    /// <summary>
    /// Makes a time of day.
    /// </summary>
    /// <param name="hours">
    /// The hour.
    /// </param>
    /// <param name="minutes">
    /// The minute.
    /// </param>
    /// <returns>
    /// The time of day.
    /// </returns>
    private static TimeSpan Hm(int hours, int minutes) => ExchangeHours.Hm(hours, minutes);

    /// <summary>
    /// Finds the exchange of a ticker.
    /// </summary>
    /// <param name="symbol">
    /// The ticker.
    /// </param>
    /// <returns>
    /// The exchange, or <see langword="null"/> for a suffix that is not known. A ticker without a suffix is a US listing.
    /// A ticker with a dot for a share class (<c>BRK.B</c>) is not told apart from a suffix and stays unknown.
    /// </returns>
    public static ExchangeHours? Of(string symbol)
    {
        var dot = symbol.LastIndexOf('.');
        if (dot < 0)
        {
            return ExchangeHours.Us;
        }

        return Suffixes.TryGetValue(symbol[(dot + 1)..], out var exchange) ? exchange : null;
    }
}
