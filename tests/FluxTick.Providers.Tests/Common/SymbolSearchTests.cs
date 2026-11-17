using FluxTick.Abstractions;
using FluxTick.Providers.Common;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the shared crypto pair search.
/// </summary>
public class SymbolSearchTests
{
    /// <summary>
    /// A small listing mixing quote currencies.
    /// </summary>
    private static readonly ListedSymbol[] Listing =
    [
        new("BTC-EUR", "BTC/EUR"),
        new("BTC-USDT", "BTC/USDT"),
        new("WBTC-USD", "WBTC/USD"),
        new("ETH-BTC", "ETH/BTC"),
        new("ETH-USDT", "ETH/USDT"),
    ];

    /// <summary>
    /// Verifies prefix matches come first and dollar-quoted pairs beat other quotes.
    /// </summary>
    [Fact]
    public void Search_RanksPrefixThenDollarQuoted()
    {
        var result = SymbolSearch.Search(new SymbolIndex(Listing), "btc", "x");

        result.Select(a => a.Symbol).Should().Equal("BTC-USDT", "BTC-EUR", "WBTC-USD", "ETH-BTC");
        result.Should().OnlyContain(a => a.AssetClass == AssetClass.Crypto && a.ProviderId == "x");
    }

    /// <summary>
    /// Verifies a pair that only matches once its separator is removed ranks below real matches of the base currency.
    /// </summary>
    [Fact]
    public void Search_BaseCurrencyMatchesBeatJoinedNameMatches()
    {
        ListedSymbol[] listing = [new("DOG/EUR", "DOG/EUR"), new("DOGE/EUR", "DOGE/EUR"), new("DOGE/USDT", "DOGE/USDT")];

        var result = SymbolSearch.Search(new SymbolIndex(listing), "doge", "x");

        result.Select(a => a.Symbol).Should().Equal("DOGE/USDT", "DOGE/EUR", "DOG/EUR");
    }

    /// <summary>
    /// Verifies separators and case are ignored in the query.
    /// </summary>
    [Theory]
    [InlineData("btc usdt")]
    [InlineData("BTC/USDT")]
    [InlineData("btc-usdt")]
    public void Search_IgnoresSeparatorsAndCase(string query)
    {
        SymbolSearch.Search(new SymbolIndex(Listing), query, "x").Select(a => a.Symbol).Should().Equal("BTC-USDT");
    }

    /// <summary>
    /// Verifies blank queries and the result limit.
    /// </summary>
    [Fact]
    public void Search_BlankQueryIsEmpty_AndLimitApplies()
    {
        SymbolSearch.Search(new SymbolIndex(Listing), "  ", "x").Should().BeEmpty();
        SymbolSearch.Search(new SymbolIndex(Listing), "t", "x", limit: 2).Should().HaveCount(2);
    }
}
