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
    /// An answer with the newest two one-minute bars, as captured from the service: 20:59 and 20:58 UTC on 19 November.
    /// </summary>
    private const string TwoMinuteBars =
        """{"ticker":"AAPL","queryCount":2,"resultsCount":2,"adjusted":true,"results":[{"v":2734,"vw":351.6042,"o":351.55,"c":351.56,"h":351.84,"l":351.46,"t":1795136340000,"n":105},{"v":4788.10555,"vw":351.7198,"o":351.78,"c":351.6,"h":351.89,"l":351.32,"t":1795136280000,"n":98}],"status":"DELAYED"}""";

    /// <summary>
    /// The start of the clock of the tests: Wednesday 10:00 in New York with the market open.
    /// </summary>
    private static readonly DateTimeOffset TradingHoursStart = new(2026, 10, 28, 14, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies the minute bars captured from the service are read with the bar start as time.
    /// </summary>
    [Fact]
    public void ParseMinuteBars_CapturedAnswer_ReadsCloses()
    {
        var points = MassiveParser.ParseMinuteBars(TwoMinuteBars);

        points.Select(p => p.Price).Should().Equal(351.56m, 351.6m);
        points[0].Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1795136340000));
    }

    /// <summary>
    /// Verifies the previous close is the close of the last session before the day of the price, whatever the bar of today
    /// holds.
    /// </summary>
    [Fact]
    public void CloseBefore_DailyBars_TakesTheSessionBeforeTheDay()
    {
        var bars = MassiveParser.ParseDailyBars(TwoBars);

        // The minute bar is of 19 November, 15:59 in New York: the close before it is that of 18 November.
        MassiveParser.CloseBefore(bars, DateTimeOffset.FromUnixTimeMilliseconds(1795136340000)).Should().Be(352.45m);

        // Before the first session on record there is none.
        MassiveParser.CloseBefore(bars, new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero)).Should().BeNull();
    }

    /// <summary>
    /// Verifies the daily bars, dated at the close, and an error answer, which is raised.
    /// </summary>
    [Fact]
    public void ParseDailyBars_BarsAndError()
    {
        var bars = MassiveParser.ParseDailyBars(TwoBars);

        // 16:00 Eastern Standard Time on 19 November is 21:00 UTC.
        bars[0].Timestamp.UtcDateTime.Should().Be(new DateTime(2026, 11, 19, 21, 0, 0, DateTimeKind.Utc));
        MassiveParser.ParseDailyBars("""{"ticker":"ZZZZ","status":"OK","resultsCount":0}""").Should().BeEmpty();

        var act = () => MassiveParser.ParseDailyBars("""{"status":"ERROR","request_id":"x","error":"Unknown API Key"}""");
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
    /// Verifies the price is the newest minute bar, measured against the close before its day, with the key in a header
    /// only, and that the previous close is asked for once a day.
    /// </summary>
    [Fact]
    public async Task Provider_Price_IsTheNewestMinuteBarAndKeepsThePreviousClose()
    {
        var handler = new StubHttpHandler(request => StubHttpHandler.Json(
            request.RequestUri!.AbsoluteUri.Contains("/range/1/minute/", StringComparison.Ordinal) ? TwoMinuteBars : TwoBars));
        var time = new FakeTimeProvider(TradingHoursStart);
        await using var provider = CreateProvider(handler, TimeSpan.FromSeconds(13), time);
        var apple = new Asset("massive", "AAPL", "Apple", AssetClass.Equity);

        await provider.SetSubscriptionsAsync([apple]);

        // The wait between the two requests of the first price runs on the fake clock.
        using var pump = new CancellationTokenSource();
        var pumping = Task.Run(async () =>
        {
            while (!pump.IsCancellationRequested)
            {
                time.Advance(TimeSpan.FromSeconds(1));
                await Task.Delay(10);
            }
        });
        var quote = (await TestWait.ReadQuotesAsync(provider, 1)).Single();
        await pump.CancelAsync();
        await pumping;

        quote.AssetId.Should().Be(apple.Id);
        quote.Price.Should().Be(351.56m);
        quote.Reference.Should().Be(352.45m);
        var uris = handler.Requests.Select(r => r.RequestUri!.AbsoluteUri).ToList();
        uris.Should().Contain(u => u.Contains("/range/1/minute/") && u.Contains("sort=desc&limit=1"));
        uris.Should().Contain(u => u.Contains("/range/1/day/") && u.Contains("limit=3"));
        uris.Should().NotContain(u => u.Contains("secret-key"));
        handler.Requests.First().Headers.Authorization!.ToString().Should().Be("Bearer secret-key");
    }

    /// <summary>
    /// Verifies the last hour is asked for in minute bars, a day is not offered, and a year is told by daily bars.
    /// </summary>
    [Fact]
    public async Task GetHistoryAsync_Ranges_FollowWhatThePlanHas()
    {
        var handler = new StubHttpHandler(request => StubHttpHandler.Json(
            request.RequestUri!.AbsoluteUri.Contains("/range/1/minute/", StringComparison.Ordinal) ? TwoMinuteBars : TwoBars));
        await using var provider = CreateProvider(handler);
        var asset = new Asset("massive", "AAPL", "Apple", AssetClass.Equity);

        provider.SupportsRange(HistoryRange.Day).Should().BeFalse();
        provider.SupportsRange(HistoryRange.Hour).Should().BeTrue();
        (await provider.GetHistoryAsync(asset, HistoryRange.Day)).Should().BeEmpty();
        handler.Requests.Should().BeEmpty();

        var hour = await provider.GetHistoryAsync(asset);
        await provider.GetHistoryAsync(asset, HistoryRange.Year);

        hour.Should().BeInAscendingOrder(p => p.Timestamp);
        var uris = handler.Requests.Select(r => r.RequestUri!.AbsoluteUri).ToList();
        uris[0].Should().Contain("/range/1/minute/").And.Contain("sort=desc&limit=65");
        uris[1].Should().Contain("/range/1/day/").And.Contain("sort=asc").And.Contain($"limit={HistoryRangeSpec.For(HistoryRange.Year).Capacity}");
    }

    /// <summary>
    /// Creates a provider with a key.
    /// </summary>
    /// <param name="handler">
    /// The stub that answers the requests.
    /// </param>
    /// <param name="spacing">
    /// The wait between two requests, or <see langword="null"/> for the default.
    /// </param>
    /// <param name="time">
    /// The clock, or <see langword="null"/> for one at the start of the trading hours.
    /// </param>
    /// <returns>
    /// The provider.
    /// </returns>
    private static MassiveProvider CreateProvider(StubHttpHandler handler, TimeSpan? spacing = null, FakeTimeProvider? time = null) =>
        new(
            new MassiveProviderFactory().Descriptor,
            new Uri("https://api.example.test"),
            "secret-key",
            TimeSpan.FromMinutes(5),
            spacing ?? TimeSpan.FromSeconds(13),
            new HttpClient(handler),
            time ?? new FakeTimeProvider(TradingHoursStart),
            NullLogger.Instance);
}
