namespace FluxTick.Abstractions;

/// <summary>
/// Implemented by a provider that knows the currency its instruments are quoted in, so that the application does not
/// have to guess it from the provider and the symbol. The holding editor preselects it and the portfolio converts from it.
/// </summary>
public interface IQuoteCurrencyProvider
{
    /// <summary>
    /// Gets the currency an instrument is quoted in.
    /// </summary>
    /// <param name="asset">
    /// The instrument.
    /// </param>
    /// <returns>
    /// The currency code as the source writes it, for example <c>USD</c>, <c>HKD</c> or a stablecoin such as <c>USDT</c>,
    /// or <see langword="null"/> when it is not known (yet). The application maps it to a currency it converts.
    /// </returns>
    string? QuoteCurrencyOf(Asset asset);
}
