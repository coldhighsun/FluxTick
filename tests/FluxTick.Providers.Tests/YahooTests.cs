using System.Net;
using FluxTick.Abstractions;
using FluxTick.Providers.Yahoo;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the Yahoo parser and provider.
/// </summary>
public class YahooTests
{
    /// <summary>
    /// A trimmed chart response.
    /// </summary>
    private const string ChartJson = """
        {"chart":{"result":[{"meta":{"regularMarketPrice":190.5,"chartPreviousClose":188.0,"previousClose":187.5,"regularMarketTime":1700000000},
        "timestamp":[1699999880,1699999940,1700000000],
        "indicators":{"quote":[{"close":[190.0,null,190.5]}]}}],"error":null}}
        """;

    /// <summary>
    /// A chart response carrying an error.
    /// </summary>
    private const string ErrorJson = """{"chart":{"result":null,"error":{"code":"Not Found","description":"No data found, symbol may be delisted"}}}""";

    /// <summary>
    /// The Apple test asset.
    /// </summary>
    private static readonly Asset Aapl = new("yahoo", "AAPL", "Apple", AssetClass.Equity);

    /// <summary>
    /// The Microsoft test asset.
    /// </summary>
    private static readonly Asset Msft = new("yahoo", "MSFT", "Microsoft", AssetClass.Equity);

    /// <summary>
    /// Verifies the chart is parsed into a quote and history, preferring <c>previousClose</c> as reference.
    /// </summary>
    [Fact]
    public void ParseChart_ValidResponse_ReturnsQuoteAndHistory()
    {
        var data = YahooParser.ParseChart(ChartJson, "yahoo", "AAPL");

        data.Quote.Price.Should().Be(190.5m);
        data.Quote.Reference.Should().Be(187.5m);
        data.Quote.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1700000000));
        data.History.Select(p => p.Price).Should().Equal(190.0m, 190.5m);
    }

    /// <summary>
    /// Verifies an error object surfaces as a typed exception with Yahoo's message.
    /// </summary>
    [Fact]
    public void ParseChart_ErrorResponse_Throws()
    {
        var act = () => YahooParser.ParseChart(ErrorJson, "yahoo", "NOPE");

        act.Should().Throw<YahooApiException>().WithMessage("*delisted*");
    }

    /// <summary>
    /// Verifies search results are mapped to asset classes and unsupported types are skipped.
    /// </summary>
    [Fact]
    public void ParseSearch_MapsQuoteTypesToAssetClasses()
    {
        const string json = """
            {"quotes":[
            {"symbol":"AAPL","quoteType":"EQUITY","shortname":"Apple Inc.","longname":"Apple Inc."},
            {"symbol":"^GSPC","quoteType":"INDEX","shortname":"S&P 500"},
            {"symbol":"EURUSD=X","quoteType":"CURRENCY","shortname":"EUR/USD"},
            {"symbol":"GC=F","quoteType":"FUTURE","shortname":"Gold"},
            {"symbol":"AAPL250101C00100000","quoteType":"OPTION","shortname":"opt"}]}
            """;

        var assets = YahooParser.ParseSearch(json, "yahoo");

        assets.Select(a => (a.Symbol, a.AssetClass)).Should().Equal(
            ("AAPL", AssetClass.Equity),
            ("^GSPC", AssetClass.Index),
            ("EURUSD=X", AssetClass.Forex),
            ("GC=F", AssetClass.Commodity));
        assets[0].DisplayName.Should().Be("Apple Inc.");
    }

    /// <summary>
    /// Verifies the provider requests the chart endpoint of each subscribed symbol and publishes the quotes.
    /// </summary>
    [Fact]
    public async Task Provider_Subscribed_PublishesQuotes()
    {
        var handler = new StubHandler(_ => Json(ChartJson));
        var time = new FakeTimeProvider();
        await using var provider = CreateProvider(handler, time);

        await provider.SetSubscriptionsAsync([Aapl, Msft]);
        var quotes = await ReadAsync(provider, 2);

        quotes.Select(q => q.AssetId.Symbol).Should().BeEquivalentTo(["AAPL", "MSFT"]);
        handler.Requests.Select(r => r.RequestUri!.AbsolutePath).Should().BeEquivalentTo(
            ["/v8/finance/chart/AAPL", "/v8/finance/chart/MSFT"]);
        // The status is reported after the cycle returns, which can be after the quotes were read.
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Connected);
    }

    /// <summary>
    /// Verifies an endpoint with a path keeps its prefix in the request.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_EndpointWithPath_KeepsItsPrefix()
    {
        var handler = new StubHandler(_ => Json(ChartJson));
        await using var provider = new YahooProvider(
            new YahooProviderFactory().Descriptor,
            new Uri("https://proxy.example.test/yahoo/"),
            TimeSpan.FromSeconds(5),
            new HttpClient(handler),
            new FakeTimeProvider(),
            NullLogger.Instance);

        await provider.SetSubscriptionsAsync([Aapl]);
        await ReadAsync(provider, 1);

        handler.Requests.Select(r => r.RequestUri!.AbsolutePath).Should().Equal("/yahoo/v8/finance/chart/AAPL");
    }

    /// <summary>
    /// Verifies one bad symbol does not stop the others from updating.
    /// </summary>
    [Fact]
    public async Task Provider_OneSymbolFails_StillPublishesOthers()
    {
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath.EndsWith("MSFT", StringComparison.Ordinal)
            ? Json(ErrorJson)
            : Json(ChartJson));
        await using var provider = CreateProvider(handler, new FakeTimeProvider());

        await provider.SetSubscriptionsAsync([Aapl, Msft]);
        var quotes = await ReadAsync(provider, 1);

        quotes.Single().AssetId.Symbol.Should().Be("AAPL");
        await EventuallyAsync(() => provider.Status.Message?.Contains("MSFT", StringComparison.Ordinal) == true);
        provider.Status.State.Should().Be(ProviderConnectionState.Connected);
    }

    /// <summary>
    /// Verifies HTTP 429 doubles the delay before the next poll.
    /// </summary>
    [Fact]
    public async Task Provider_RateLimited_BacksOff()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var time = new FakeTimeProvider();
        await using var provider = CreateProvider(handler, time);

        await provider.SetSubscriptionsAsync([Aapl]);
        await EventuallyAsync(() => handler.Requests.Count == 1);
        await EventuallyAsync(() => provider.Status.State == ProviderConnectionState.Reconnecting);

        // Stepping the clock until the next request shows how long the provider waited, whenever its timer was registered.
        var start = time.GetUtcNow();
        await EventuallyAsync(() =>
        {
            if (handler.Requests.Count == 2)
            {
                return true;
            }

            time.Advance(TimeSpan.FromSeconds(1));
            return false;
        });

        (time.GetUtcNow() - start).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10), "the 5 second delay doubled");
    }

    /// <summary>
    /// Verifies the provider reports failure after repeated cycles without any success.
    /// </summary>
    [Fact]
    public async Task Provider_RepeatedFailures_ReportsFailed()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var time = new FakeTimeProvider();
        await using var provider = CreateProvider(handler, time);

        await provider.SetSubscriptionsAsync([Aapl]);

        // Advancing in steps until the status flips does not depend on the loop having registered its timer already,
        // which a fixed sleep before a single advance does under load.
        await EventuallyAsync(() =>
        {
            if (provider.Status.State == ProviderConnectionState.Failed)
            {
                return true;
            }

            time.Advance(TimeSpan.FromSeconds(5));
            return false;
        });
    }

    /// <summary>
    /// Creates a provider over a stub HTTP handler.
    /// </summary>
    /// <param name="handler">
    /// The stub handler.
    /// </param>
    /// <param name="time">
    /// The fake clock.
    /// </param>
    /// <returns>
    /// The provider.
    /// </returns>
    private static YahooProvider CreateProvider(StubHandler handler, FakeTimeProvider time)
    {
        var descriptor = new YahooProviderFactory().Descriptor;
        return new YahooProvider(
            descriptor,
            new Uri("https://example.test"),
            TimeSpan.FromSeconds(5),
            new HttpClient(handler),
            time,
            NullLogger.Instance);
    }

    /// <summary>
    /// Creates a successful JSON response.
    /// </summary>
    /// <param name="json">
    /// The body.
    /// </param>
    /// <returns>
    /// The response.
    /// </returns>
    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    /// <summary>
    /// Reads a number of quotes with a timeout.
    /// </summary>
    /// <param name="provider">
    /// The provider to read from.
    /// </param>
    /// <param name="count">
    /// The number of quotes to read.
    /// </param>
    /// <returns>
    /// The quotes.
    /// </returns>
    private static async Task<List<Quote>> ReadAsync(IMarketDataProvider provider, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var quotes = new List<Quote>();
        await foreach (var quote in provider.ReadQuotesAsync(timeout.Token))
        {
            quotes.Add(quote);
            if (quotes.Count == count)
            {
                break;
            }
        }

        return quotes;
    }

    /// <summary>
    /// Waits until a condition holds, failing on timeout.
    /// </summary>
    /// <param name="condition">
    /// The condition to wait for.
    /// </param>
    /// <returns>
    /// A task that completes when the condition holds.
    /// </returns>
    private static async Task EventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(10);
        }
    }

    /// <summary>
    /// An HTTP handler that answers from a delegate and records every request.
    /// </summary>
    /// <param name="respond">
    /// Produces the response for a request.
    /// </param>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        /// <summary>
        /// Gets the requests received so far.
        /// </summary>
        public System.Collections.Concurrent.ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(request);
            return Task.FromResult(respond(request));
        }
    }
}
