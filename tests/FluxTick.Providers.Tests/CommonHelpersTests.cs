using System.Text;
using System.Text.Json;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the small helpers in Providers.Common.
/// </summary>
public class CommonHelpersTests
{
    /// <summary>
    /// A descriptor declaring one WebSocket and one HTTP endpoint option.
    /// </summary>
    private static readonly ProviderDescriptor Descriptor = new(
        "test",
        "Test",
        "A test source.",
        [AssetClass.Crypto],
        ProviderUpdateMode.Streaming,
        Options: ExchangeEndpoints.Options("wss://stream.example.test/ws", "ws hint", "https://api.example.test"));

    /// <summary>
    /// Verifies GBK bytes are decoded to the Chinese text they carry.
    /// </summary>
    [Fact]
    public void GbkText_Decode_DecodesChineseNames()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(936).GetBytes("v_sh600000=\"1~浦发银行~\";");

        GbkText.Decode(bytes).Should().Be("v_sh600000=\"1~浦发银行~\";");
    }

    /// <summary>
    /// Verifies valid endpoints are used as configured.
    /// </summary>
    [Fact]
    public void ExchangeEndpoints_ValidValues_AreUsed()
    {
        var options = Options(("WebSocketUrl", "ws://localhost:9000/feed"), ("RestBaseUrl", "http://localhost:9001"));

        var endpoints = ExchangeEndpoints.Read(options, Descriptor);

        endpoints.WebSocket.Should().Be(new Uri("ws://localhost:9000/feed"));
        endpoints.Rest.Should().Be(new Uri("http://localhost:9001"));
    }

    /// <summary>
    /// Verifies a value with the wrong scheme, or one that only looks like an address, falls back to the default
    /// instead of reaching a socket or HTTP client that would throw inside a background loop.
    /// </summary>
    [Theory]
    [InlineData("https://stream.example.test/ws", "ftp://api.example.test")]
    [InlineData("stream.example.test:8443/ws", "api.example.test:8443")]
    [InlineData("not a url", "")]
    [InlineData("file:///etc/hosts", "ws://api.example.test")]
    public void ExchangeEndpoints_WrongScheme_FallsBackToDefaults(string webSocket, string rest)
    {
        var options = Options(("WebSocketUrl", webSocket), ("RestBaseUrl", rest));

        var endpoints = ExchangeEndpoints.Read(options, Descriptor);

        endpoints.WebSocket.Should().Be(new Uri("wss://stream.example.test/ws"));
        endpoints.Rest.Should().Be(new Uri("https://api.example.test"));
    }

    /// <summary>
    /// Verifies concurrent callers share one load.
    /// </summary>
    [Fact]
    public async Task ExpiringCache_ConcurrentCallers_ShareOneLoad()
    {
        var cache = new ExpiringCache<string[]>(TimeSpan.FromMinutes(1), TimeProvider.System);
        var release = new TaskCompletionSource();
        var loads = 0;

        async Task<string[]> Load(CancellationToken token)
        {
            Interlocked.Increment(ref loads);
            await release.Task;
            return ["BTC"];
        }

        var first = cache.GetAsync(Load, CancellationToken.None);
        var second = cache.GetAsync(Load, CancellationToken.None);
        await TestWait.EventuallyAsync(() => Volatile.Read(ref loads) == 1);
        release.SetResult();

        (await first).Should().Equal("BTC");
        (await second).Should().BeSameAs(await first);
        loads.Should().Be(1);
    }

    /// <summary>
    /// Verifies a value is reused until it expires and then loaded again.
    /// </summary>
    [Fact]
    public async Task ExpiringCache_ReusesUntilExpiry()
    {
        var time = new FakeTimeProvider();
        var cache = new ExpiringCache<string[]>(TimeSpan.FromMinutes(10), time);
        var loads = 0;
        Task<string[]> Load(CancellationToken token) => Task.FromResult(new[] { $"v{Interlocked.Increment(ref loads)}" });

        var first = await cache.GetAsync(Load, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(9));
        var second = await cache.GetAsync(Load, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(2));
        var third = await cache.GetAsync(Load, CancellationToken.None);

        first.Should().Equal("v1");
        second.Should().BeSameAs(first);
        third.Should().Equal("v2");
    }

    /// <summary>
    /// Verifies an expired value is still served when the reload fails, and the reload is retried later.
    /// </summary>
    [Fact]
    public async Task ExpiringCache_ReloadFails_ServesStaleValue()
    {
        var time = new FakeTimeProvider();
        var cache = new ExpiringCache<string[]>(TimeSpan.FromMinutes(10), time);
        var loads = 0;
        Task<string[]> Load(CancellationToken token) =>
            Interlocked.Increment(ref loads) == 1 ? Task.FromResult(new[] { "v1" }) : throw new HttpRequestException("down");

        var first = await cache.GetAsync(Load, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(11));
        var stale = await cache.GetAsync(Load, CancellationToken.None);
        var again = await cache.GetAsync(Load, CancellationToken.None);

        stale.Should().BeSameAs(first);
        again.Should().BeSameAs(first);
        loads.Should().Be(2, "the failed reload is not repeated at once");
    }

    /// <summary>
    /// Verifies serving an expired value after a failed reload is reported, not silent.
    /// </summary>
    [Fact]
    public async Task ExpiringCache_ReloadFails_ReportsTheFailure()
    {
        var time = new FakeTimeProvider();
        var reported = new List<(Exception Error, int Failures)>();
        var cache = new ExpiringCache<string[]>(TimeSpan.FromMinutes(10), time, (ex, failures) => reported.Add((ex, failures)));
        var loads = 0;
        Task<string[]> Load(CancellationToken token) =>
            Interlocked.Increment(ref loads) == 1 ? Task.FromResult(new[] { "v1" }) : throw new HttpRequestException("down");

        await cache.GetAsync(Load, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(11));
        await cache.GetAsync(Load, CancellationToken.None);

        var only = reported.Should().ContainSingle().Subject;
        only.Error.Should().BeOfType<HttpRequestException>();
        only.Failures.Should().Be(1);
    }

    /// <summary>
    /// Verifies the number of failures in a row counts up while the outage lasts and starts over after a reload worked.
    /// </summary>
    [Fact]
    public async Task ExpiringCache_RepeatedFailures_AreCountedUntilAReloadSucceeds()
    {
        var time = new FakeTimeProvider();
        var counts = new List<int>();
        var cache = new ExpiringCache<string[]>(TimeSpan.FromMinutes(10), time, (_, failures) => counts.Add(failures));
        var loads = 0;
        Task<string[]> Load(CancellationToken token) => Interlocked.Increment(ref loads) switch
        {
            1 => Task.FromResult(new[] { "v1" }),
            2 or 3 or 5 => throw new HttpRequestException("down"),
            _ => Task.FromResult(new[] { "v" + loads }),
        };

        await cache.GetAsync(Load, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(11));
        await cache.GetAsync(Load, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(2));
        await cache.GetAsync(Load, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(2));
        await cache.GetAsync(Load, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(11));
        await cache.GetAsync(Load, CancellationToken.None);

        counts.Should().Equal(1, 2, 1);
    }

    /// <summary>
    /// Verifies a load that completes synchronously does not leave its finished task behind, so the value is reloaded
    /// after it expires.
    /// </summary>
    [Fact]
    public async Task ExpiringCache_SynchronousLoad_IsReloadedAfterExpiry()
    {
        for (var i = 0; i < 200; i++)
        {
            var time = new FakeTimeProvider();
            var cache = new ExpiringCache<string[]>(TimeSpan.FromMinutes(10), time);

            var first = await cache.GetAsync(_ => Task.FromResult(new[] { "old" }), CancellationToken.None);
            time.Advance(TimeSpan.FromMinutes(11));
            var second = await cache.GetAsync(_ => Task.FromResult(new[] { "new" }), CancellationToken.None);

            first.Should().Equal("old");
            second.Should().Equal("new");
        }
    }

    /// <summary>
    /// Verifies a failed load does not stay registered as the load in flight.
    /// </summary>
    [Fact]
    public async Task ExpiringCache_SynchronousFailure_DoesNotStick()
    {
        var cache = new ExpiringCache<string[]>(TimeSpan.FromMinutes(10), TimeProvider.System);

        for (var i = 0; i < 50; i++)
        {
            var failing = () => cache.GetAsync(_ => Task.FromException<string[]>(new HttpRequestException("down")), CancellationToken.None);
            await failing.Should().ThrowAsync<HttpRequestException>();
        }

        (await cache.GetAsync(_ => Task.FromResult(new[] { "BTC" }), CancellationToken.None)).Should().Equal("BTC");
    }

    /// <summary>
    /// Verifies one prepared index serves several different queries.
    /// </summary>
    [Fact]
    public void SymbolSearch_PreparedIndex_ServesRepeatedQueries()
    {
        ListedSymbol[] listed =
        [
            new("DOGE-EUR", "DOGE/EUR"),
            new("DOG-USD", "DOG/USD"),
            new("DOGE-USD", "DOGE/USD"),
            new("ETH-USD", "ETH/USD"),
        ];
        var index = new SymbolIndex(listed);

        SymbolSearch.Search(index, "doge", "okx").Select(a => a.Symbol).Should().Equal("DOGE-USD", "DOGE-EUR");
        SymbolSearch.Search(index, "eth usd", "okx").Select(a => a.Symbol).Should().Equal("ETH-USD");
        SymbolSearch.Search(index, "doge", "okx").Select(a => a.Symbol).Should().Equal("DOGE-USD", "DOGE-EUR");
        SymbolSearch.Search(index, "  ", "okx").Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a request is appended to the endpoint's own path, and a query or fragment of the endpoint is dropped.
    /// </summary>
    [Theory]
    [InlineData("https://api.example.test", "/v1/x?a=1", "https://api.example.test/v1/x?a=1")]
    [InlineData("https://api.example.test/", "/v1/x?a=1", "https://api.example.test/v1/x?a=1")]
    [InlineData("https://proxy.example.test/binance", "/api/v3/klines?symbol=BTCUSDT", "https://proxy.example.test/binance/api/v3/klines?symbol=BTCUSDT")]
    [InlineData("https://proxy.example.test/binance/", "/q=sh600000,sz000001", "https://proxy.example.test/binance/q=sh600000,sz000001")]
    [InlineData("https://gw.example.test/x?key=abc#top", "/v1/y?b=2", "https://gw.example.test/x/v1/y?b=2")]
    [InlineData("https://api.example.test:8443/base", "/p", "https://api.example.test:8443/base/p")]
    public void Combine_Request_KeepsTheEndpointPath(string endpoint, string request, string expected)
    {
        EndpointOptions.Combine(new Uri(endpoint), request).AbsoluteUri.Should().Be(expected);
    }

    /// <summary>
    /// Verifies only a decimal above zero is accepted as a positive decimal.
    /// </summary>
    [Theory]
    [InlineData("""{"v":"1.5"}""", true, 1.5)]
    [InlineData("""{"v":2}""", true, 2)]
    [InlineData("""{"v":"0"}""", false, 0)]
    [InlineData("""{"v":-3}""", false, -3)]
    [InlineData("""{"v":"abc"}""", false, 0)]
    [InlineData("""{}""", false, 0)]
    public void TryGetPositiveDecimal_Property_AcceptsOnlyPositiveValues(string json, bool expected, double expectedValue)
    {
        using var document = JsonDocument.Parse(json);

        var ok = JsonValues.TryGetPositiveDecimal(document.RootElement, "v", out var value);

        ok.Should().Be(expected);
        if (expected)
        {
            value.Should().Be((decimal)expectedValue);
        }
    }

    /// <summary>
    /// Verifies a dash-separated exchange id becomes a readable pair name.
    /// </summary>
    [Fact]
    public void PairNameOfDashedId_DashedId_ReturnsSlashedName()
    {
        SymbolSearch.PairNameOfDashedId("BTC-USDT").Should().Be("BTC/USDT");
    }

    /// <summary>
    /// Verifies a failed load is not cached and the next caller tries again.
    /// </summary>
    [Fact]
    public async Task ExpiringCache_FailedLoad_IsRetried()
    {
        var cache = new ExpiringCache<string[]>(TimeSpan.FromMinutes(10), TimeProvider.System);
        var attempts = 0;
        Task<string[]> Load(CancellationToken token) =>
            Interlocked.Increment(ref attempts) == 1
                ? Task.FromException<string[]>(new HttpRequestException("rate limited"))
                : Task.FromResult(new[] { "BTC" });

        var failing = () => cache.GetAsync(Load, CancellationToken.None);

        await failing.Should().ThrowAsync<HttpRequestException>();
        (await cache.GetAsync(Load, CancellationToken.None)).Should().Equal("BTC");
    }

    /// <summary>
    /// Verifies a caller that gives up does not cancel the load other callers wait for.
    /// </summary>
    [Fact]
    public async Task ExpiringCache_CancelledCaller_DoesNotCancelTheLoad()
    {
        var cache = new ExpiringCache<string[]>(TimeSpan.FromMinutes(10), TimeProvider.System);
        var release = new TaskCompletionSource();
        var loads = 0;

        async Task<string[]> Load(CancellationToken token)
        {
            Interlocked.Increment(ref loads);
            await release.Task;
            return ["BTC"];
        }

        using var impatient = new CancellationTokenSource();
        var cancelled = cache.GetAsync(Load, impatient.Token);
        var patient = cache.GetAsync(Load, CancellationToken.None);
        await impatient.CancelAsync();
        release.SetResult();

        await cancelled.Invoking(t => t).Should().ThrowAsync<OperationCanceledException>();
        (await patient).Should().Equal("BTC");
        loads.Should().Be(1);
    }

    /// <summary>
    /// Verifies ISO 8601 timestamps are read, including nanosecond precision, and times without an offset count as UTC.
    /// </summary>
    [Theory]
    [InlineData("""{"t":"2026-10-22T15:34:42.413298Z"}""")]
    [InlineData("""{"t":"2026-10-22T15:34:42.413298123Z"}""")]
    [InlineData("""{"t":"2026-10-22T15:34:42.413298"}""")]
    [InlineData("""{"t":"2026-10-22T23:34:42.413298+08:00"}""")]
    public void TryGetIsoTime_ReadsTimestamps(string json)
    {
        using var document = JsonDocument.Parse(json);

        JsonValues.TryGetIsoTime(document.RootElement, "t", out var time).Should().BeTrue();

        time.UtcDateTime.Should().BeCloseTo(new DateTime(2026, 10, 22, 15, 34, 42, DateTimeKind.Utc), TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Verifies missing, non-string and unreadable timestamps are reported as not found.
    /// </summary>
    [Theory]
    [InlineData("""{"other":1}""")]
    [InlineData("""{"t":12345}""")]
    [InlineData("""{"t":"yesterday"}""")]
    [InlineData("""[1]""")]
    public void TryGetIsoTime_Unreadable_ReturnsFalse(string json)
    {
        using var document = JsonDocument.Parse(json);

        JsonValues.TryGetIsoTime(document.RootElement, "t", out _).Should().BeFalse();
    }

    /// <summary>
    /// Creates provider options holding the given stored values.
    /// </summary>
    /// <param name="values">
    /// The option keys and values.
    /// </param>
    /// <returns>
    /// The options.
    /// </returns>
    private static ProviderOptions Options(params (string Key, string Value)[] values) =>
        new(Descriptor, values.ToDictionary(v => v.Key, v => v.Value));
}
