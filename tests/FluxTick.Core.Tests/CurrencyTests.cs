using FluxTick.Core.Application;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for the currency codes and the guess of the currency of an instrument.
/// </summary>
public class CurrencyTests
{
    /// <summary>
    /// Verifies the currency guessed for each data source.
    /// </summary>
    /// <param name="provider">
    /// The provider id.
    /// </param>
    /// <param name="symbol">
    /// The symbol.
    /// </param>
    /// <param name="expected">
    /// The expected currency.
    /// </param>
    [Theory]
    [InlineData("tencent", "sh600000", "CNY")]
    [InlineData("tencent", "hk00700", "HKD")]
    [InlineData("tencent", "usAAPL", "USD")]
    [InlineData("sina", "sz000001", "CNY")]
    [InlineData("yahoo", "0700.HK", "HKD")]
    [InlineData("yahoo", "600519.SS", "CNY")]
    [InlineData("yahoo", "AAPL", "USD")]
    [InlineData("binance", "BTCUSDT", "USD")]
    [InlineData("binance", "BTCEUR", "EUR")]
    [InlineData("coinbase", "BTC-GBP", "GBP")]
    [InlineData("kraken", "BTC/JPY", "JPY")]
    [InlineData("okx", "BTC-USDT", "USD")]
    [InlineData("yahoo", "BTC-EUR", "EUR")]
    public void Guess_KnownProviders_ReturnsQuoteCurrency(string provider, string symbol, string expected)
    {
        CurrencyGuesser.Guess(provider, symbol).Should().Be(expected);
    }

    /// <summary>
    /// Verifies that no currency is offered or guessed that the rate source has no rate for.
    /// </summary>
    [Fact]
    public void Supported_DoesNotOfferCurrenciesTheRateSourceLacks()
    {
        CurrencyCodes.Supported.Should().NotContain("TWD");
        CurrencyGuesser.Guess("yahoo", "2330.TW").Should().BeOneOf(CurrencyCodes.Supported);
    }

    /// <summary>
    /// Verifies that the chosen currency wins over the default of the language.
    /// </summary>
    [Fact]
    public void EffectiveBase_ChosenOrDefault_PrefersTheChosen()
    {
        CurrencyCodes.EffectiveBase("HKD", isChinese: true).Should().Be("HKD");
        CurrencyCodes.EffectiveBase(null, isChinese: true).Should().Be("CNY");
        CurrencyCodes.EffectiveBase(null, isChinese: false).Should().Be("USD");
    }

    /// <summary>
    /// Verifies that only three letter codes are accepted, in upper case.
    /// </summary>
    /// <param name="input">
    /// The raw code.
    /// </param>
    /// <param name="expected">
    /// The normalized code.
    /// </param>
    [Theory]
    [InlineData("cny", "CNY")]
    [InlineData(" usd ", "USD")]
    [InlineData("US", null)]
    [InlineData("US1", null)]
    [InlineData(null, null)]
    public void Normalize_Code_UpperCasesOrRejects(string? input, string? expected)
    {
        CurrencyCodes.Normalize(input).Should().Be(expected);
    }

    /// <summary>
    /// Verifies that a code is accepted only when the app offers the currency.
    /// </summary>
    /// <param name="input">
    /// The raw code.
    /// </param>
    /// <param name="expected">
    /// The normalized code.
    /// </param>
    [Theory]
    [InlineData("hkd", "HKD")]
    [InlineData("TWD", null)]
    [InlineData("XXX", null)]
    [InlineData(null, null)]
    public void NormalizeSupported_Code_AcceptsOnlyOfferedCurrencies(string? input, string? expected)
    {
        CurrencyCodes.NormalizeSupported(input).Should().Be(expected);
    }
}
