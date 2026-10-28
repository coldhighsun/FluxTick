namespace FluxTick.Core.Application;

/// <summary>
/// The currencies the app converts between and helpers for their ISO 4217 codes.
/// </summary>
public static class CurrencyCodes
{
    /// <summary>
    /// The currency that every exchange rate is expressed against.
    /// </summary>
    public const string Pivot = "USD";

    /// <summary>
    /// The currencies a holding or the portfolio total can be in, which the rate source publishes rates for.
    /// </summary>
    public static IReadOnlyList<string> Supported { get; } =
    [
        "USD", "CNY", "HKD", "EUR", "JPY", "GBP", "AUD", "CAD", "CHF", "SGD", "KRW", "INR", "THB", "MYR", "NZD",
    ];

    /// <summary>
    /// Normalizes a currency code to upper case.
    /// </summary>
    /// <param name="code">
    /// The code to normalize.
    /// </param>
    /// <returns>
    /// The upper case code, or <see langword="null"/> when the text is not a three-letter code.
    /// </returns>
    public static string? Normalize(string? code)
    {
        var trimmed = code?.Trim();
        if (trimmed is not { Length: 3 } || !trimmed.All(char.IsAsciiLetter))
        {
            return null;
        }

        return trimmed.ToUpperInvariant();
    }

    /// <summary>
    /// Normalizes a currency code and accepts it only when the app offers the currency.
    /// </summary>
    /// <param name="code">
    /// The code to normalize.
    /// </param>
    /// <returns>
    /// The upper case code, or <see langword="null"/> when it is not one of <see cref="Supported"/>.
    /// </returns>
    public static string? NormalizeSupported(string? code) => Normalize(code) is { } normalized && Supported.Contains(normalized) ? normalized : null;

    /// <summary>
    /// Gets the currency the portfolio total is shown in when the user has not chosen one.
    /// </summary>
    /// <param name="isChinese">
    /// Whether the interface language is Chinese.
    /// </param>
    /// <returns>
    /// <c>CNY</c> for Chinese, otherwise <c>USD</c>.
    /// </returns>
    public static string DefaultBase(bool isChinese) => isChinese ? "CNY" : "USD";

    /// <summary>
    /// Gets the currency the portfolio total is shown in.
    /// </summary>
    /// <param name="configured">
    /// The currency the user chose, or <see langword="null"/> for none.
    /// </param>
    /// <param name="isChinese">
    /// Whether the interface language is Chinese, which decides when nothing was chosen.
    /// </param>
    /// <returns>
    /// The chosen currency, otherwise <see cref="DefaultBase"/>.
    /// </returns>
    public static string EffectiveBase(string? configured, bool isChinese) => configured ?? DefaultBase(isChinese);
}
