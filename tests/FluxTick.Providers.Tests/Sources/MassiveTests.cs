using AwesomeAssertions;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using FluxTick.Providers.Massive;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the Massive parser and provider.
/// </summary>
public class MassiveTests
{
    /// <summary>
    /// An answer with the newest two daily bars, newest first. 1795064400000 is the start of Wednesday 19 November 2026 in
    /// New York (05:00 UTC) and 1794978000000 that of the day before.
    /// </summary>
    private const string TwoBars =
        """{"ticker":"AAPL","status":"OK","resultsCount":2,"results":[{"v":1,"o":352.9,"c":349.21,"h":355.3,"l":346.4,"t":1795064400000,"n":1},{"v":1,"o":350.0,"c":352.45,"h":353.0,"l":349.0,"t":1794978000000,"n":1}]}""";

    /// <summary>
    /// The start of the clock of the tests: Wednesday 10:00 in New York with the market open.
    /// </summary>
    private static readonly DateTimeOffset TradingHoursStart = new(2026, 10, 28, 14, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies the newest bar is the price and the one before it the previous close, dated at the close of the day.
    /// </summary>
    [Fact]
    public void ParseQuote_TwoBars_PriceAndPreviousClose()
    {
        var quote = MassiveParser.ParseQuote("AAPL", TwoBars)!;

        quote.Key.Should().Be("AAPL");
        quote.Price.Should().Be(349.21m);
        quote.PreviousClose.Should().Be(352.45m);

        // 16:00 Eastern Standard Time on 19 November is 21:00 UTC.
        quote.Time!.Value.UtcDateTime.Should().Be(new DateTime(2026, 11, 19, 21, 0, 0, DateTimeKind.Utc));
    }

    /// <summary>
    /// Verifies a ticker without bars has no quote, and an error answer is raised.
    /// </summary>
    [Fact]
    public void ParseQuote_NoBarsOrError_ReturnsNullOrThrows()
    {
        MassiveParser.ParseQuote("ZZZZ", """{"ticker":"ZZZZ","status":"OK","resultsCount":0}""").Should().BeNull();

        var act = () => MassiveParser.ParseQuote("AAPL", """{"status":"ERROR","request_id":"x","error":"Unknown API Key"}""");
        act.Should().Throw<HttpRequestException>().WithMessage("*Unknown API Key*");
    }

    /// <summary>
    /// Verifies search lists each ticker once.
    /// </summary>
    [Fact]
    public void ParseSearch_Results_AreListedOnce()
    {
        const string json = """{"status":"OK","results":[{"ticker":"AAPL","name":"Apple Inc.","market":"stocks"},{"ticker":"AAPL","name":"dup"},{"ticker":"APLE","name":"Apple Hospitality REIT"}]}""";

        var assets = MassiveParser.ParseSearch(json, "massive");

        assets.Select(a => a.Symbol).Should().Equal("AAPL", "APLE");
        assets[0].DisplayName.Should().Be("Apple Inc.");
    }

    /// <summary>
    /// Verifies the price comes over REST with the key in a header, never in the address.
    /// </summary>
    [Fact]
    public async Task Provider_Price_UsesTheKeyInAHeaderOnly()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(TwoBars));
        await using var provider = CreateProvider(handler);
        var apple = new Asset("massive", "AAPL", "Apple", AssetClass.Equity);

        await provider.SetSubscriptionsAsync([apple]);
        var quote = (await TestWait.ReadQuotesAsync(provider, 1)).Single();

        quote.AssetId.Should().Be(apple.Id);
        quote.Price.Should().Be(349.21m);
        var request = handler.Requests.First();
        request.RequestUri!.AbsoluteUri.Should().Contain("/v2/aggs/ticker/AAPL/range/1/day/2026-10-18/2026-10-28").And.Contain("sort=desc&limit=2").And.NotContain("secret-key");
        request.Headers.Authorization!.ToString().Should().Be("Bearer secret-key");
    }

    /// <summary>
    /// Verifies only the ranges of daily bars are offered, and the history asks for no more bars than the range shows.
    /// </summary>
    [Fact]
    public async Task GetHistoryAsync_Ranges_AreDailyOnly()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(TwoBars));
        await using var provider = CreateProvider(handler);
        var asset = new Asset("massive", "AAPL", "Apple", AssetClass.Equity);

        provider.SupportsRange(HistoryRange.Day).Should().BeFalse();
        provider.SupportsRange(HistoryRange.Year).Should().BeTrue();
        (await provider.GetHistoryAsync(asset, HistoryRange.Day)).Should().BeEmpty();
        handler.Requests.Should().BeEmpty();

        await provider.GetHistoryAsync(asset, HistoryRange.Year);

        handler.Requests.Single().RequestUri!.Query.Should().Contain("sort=asc").And.Contain($"limit={HistoryRangeSpec.For(HistoryRange.Year).Capacity}");
    }

    /// <summary>
    /// Creates a provider with a key.
    /// </summary>
    /// <param name="handler">
    /// The stub that answers the requests.
    /// </param>
    /// <returns>
    /// The provider.
    /// </returns>
    private static MassiveProvider CreateProvider(StubHttpHandler handler) =>
        new(
            new MassiveProviderFactory().Descriptor,
            new Uri("https://api.example.test"),
            "secret-key",
            TimeSpan.FromMinutes(5),
            TimeSpan.FromSeconds(13),
            new HttpClient(handler),
            new FakeTimeProvider(TradingHoursStart),
            NullLogger.Instance);
}
