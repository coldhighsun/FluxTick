namespace FluxTick.Core.Application;

/// <summary>
/// Guesses the currency an instrument is quoted in, for the holding editor to preselect.
/// </summary>
public static class CurrencyGuesser
{
    /// <summary>
    /// Guesses the quote currency of an instrument from its provider and symbol.
    /// </summary>
    /// <param name="providerId">
    /// The provider that serves the instrument.
    /// </param>
    /// <param name="symbol">
    /// The provider-native symbol.
    /// </param>
    /// <returns>
    /// An ISO 4217 code. It is <c>USD</c> when nothing says otherwise, which is right for crypto and US stocks.
    /// </returns>
    public static string Guess(string providerId, string symbol)
    {
        var text = symbol.Trim();
        return providerId switch
        {
            "sina" => "CNY",
            "tencent" when text.StartsWith("hk", StringComparison.OrdinalIgnoreCase) => "HKD",
            "tencent" when text.StartsWith("us", StringComparison.OrdinalIgnoreCase) => "USD",
            "tencent" => "CNY",
            "yahoo" => GuessYahoo(text),
            "binance" or "bybit" or "okx" or "coinbase" or "kraken" => GuessFromPair(text),
            _ => CurrencyCodes.Pivot,
        };
    }

    /// <summary>
    /// Guesses the quote currency of a trading pair such as <c>BTCEUR</c>, <c>BTC-EUR</c> or <c>BTC/EUR</c>. Stablecoins
    /// count as US dollars.
    /// </summary>
    /// <param name="symbol">
    /// The pair.
    /// </param>
    /// <returns>
    /// An ISO 4217 code, <c>USD</c> when the quote currency is not one the app converts.
    /// </returns>
    private static string GuessFromPair(string symbol)
    {
        var text = symbol.ToUpperInvariant();
        var separator = text.LastIndexOfAny(['-', '/', '_']);
        var quote = separator >= 0
            ? text[(separator + 1)..]
            : CurrencyCodes.Supported.FirstOrDefault(code => text.EndsWith(code, StringComparison.Ordinal) && text.Length > code.Length);
        return quote is not null && CurrencyCodes.Supported.Contains(quote) ? quote : CurrencyCodes.Pivot;
    }

    /// <summary>
    /// Guesses the quote currency of a Yahoo symbol from its exchange suffix.
    /// </summary>
    /// <param name="symbol">
    /// The Yahoo symbol, for example <c>0700.HK</c>.
    /// </param>
    /// <returns>
    /// An ISO 4217 code.
    /// </returns>
    private static string GuessYahoo(string symbol)
    {
        var dot = symbol.LastIndexOf('.');
        if (dot < 0)
        {
            // A crypto pair such as BTC-EUR names its quote currency; a plain ticker is a US stock.
            return symbol.Contains('-') ? GuessFromPair(symbol) : CurrencyCodes.Pivot;
        }

        return symbol[(dot + 1)..].ToUpperInvariant() switch
        {
            "HK" => "HKD",
            "SS" or "SZ" => "CNY",
            "T" => "JPY",
            "L" => "GBP",
            "KS" or "KQ" => "KRW",
            "SI" => "SGD",
            "AX" => "AUD",
            "TO" => "CAD",
            "NS" or "BO" => "INR",
            _ => CurrencyCodes.Pivot,
        };
    }
}
