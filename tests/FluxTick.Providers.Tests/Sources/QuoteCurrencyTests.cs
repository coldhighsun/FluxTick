using AwesomeAssertions;
using FluxTick.Abstractions;
using FluxTick.Providers.Bitget;
using FluxTick.Providers.Common;
using FluxTick.Providers.Eastmoney;
using FluxTick.Providers.GateIo;
using FluxTick.Providers.KuCoin;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the quote currency the providers report, which spares the application a guess per source.
/// </summary>
public class QuoteCurrencyTests
{
    /// <summary>
    /// Verifies Eastmoney reports the currency of the market of the symbol.
    /// </summary>
    /// <param name="symbol">
    /// The symbol.
    /// </param>
    /// <param name="expected">
    /// The expected currency, or <see langword="null"/> for an unknown market.
    /// </param>
    [Theory]
    [InlineData("1.600519", "CNY")]
    [InlineData("0.000001", "CNY")]
    [InlineData("116.00700", "HKD")]
    [InlineData("105.AAPL", "USD")]
    [InlineData("90.BK1189", null)]
    public void Eastmoney_Symbol_ReportsTheCurrencyOfItsMarket(string symbol, string? expected)
    {
        EastmoneyParser.CurrencyOf(symbol).Should().Be(expected);
    }

    /// <summary>
    /// Verifies a crypto exchange reads the quote currency from the pair, whatever the separator, and Bitget, whose pairs have
    /// none, from the end of the symbol.
    /// </summary>
    /// <param name="exchange">
    /// The exchange.
    /// </param>
    /// <param name="symbol">
    /// The symbol.
    /// </param>
    /// <param name="expected">
    /// The expected currency.
    /// </param>
    [Theory]
    [InlineData("gateio", "BTC_USDT", "USDT")]
    [InlineData("kucoin", "ETH-EUR", "EUR")]
    [InlineData("bitget", "BTCUSDT", "USDT")]
    [InlineData("bitget", "ETHBTC", "BTC")]
    public void CryptoExchange_Pair_ReportsItsQuoteCurrency(string exchange, string symbol, string expected)
    {
        var endpoints = new ExchangeEndpoints(new Uri("wss://ws.example.test"), new Uri("https://rest.example.test"));
        using var http = new HttpClient();
        IQuoteCurrencyProvider provider = exchange switch
        {
            "gateio" => new GateIoProvider(new GateIoProviderFactory().Descriptor, endpoints, http, TimeProvider.System, NullLogger.Instance),
            "kucoin" => new KuCoinProvider(new KuCoinProviderFactory().Descriptor, endpoints, http, TimeProvider.System, NullLogger.Instance),
            _ => new BitgetProvider(new BitgetProviderFactory().Descriptor, endpoints, http, TimeProvider.System, NullLogger.Instance),
        };

        provider.QuoteCurrencyOf(new Asset(exchange, symbol, symbol, AssetClass.Crypto)).Should().Be(expected);
    }
}
