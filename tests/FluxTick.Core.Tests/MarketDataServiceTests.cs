using FluxTick.Abstractions;
using FluxTick.Core.Application;
using FluxTick.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for <see cref="MarketDataService"/>.
/// </summary>
public class MarketDataServiceTests
{
    /// <summary>
    /// The Bitcoin test asset.
    /// </summary>
    private static readonly Asset Btc = new("crypto", "BTC", "Bitcoin", AssetClass.Crypto);

    /// <summary>
    /// The Apple test asset.
    /// </summary>
    private static readonly Asset Aapl = new("stocks", "AAPL", "Apple", AssetClass.Equity);

    /// <summary>
    /// The crypto test provider factory.
    /// </summary>
    private readonly FakeProviderFactory _crypto = new("crypto", [AssetClass.Crypto]);

    /// <summary>
    /// The stocks test provider factory.
    /// </summary>
    private readonly FakeProviderFactory _stocks = new("stocks", [AssetClass.Equity]);

    /// <summary>
    /// The controllable clock.
    /// </summary>
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Creates a service over both test factories.
    /// </summary>
    /// <returns>
    /// The service.
    /// </returns>
    private MarketDataService NewService() => new(
        new ProviderRegistry([_crypto, _stocks]),
        NullLoggerFactory.Instance,
        _time);

    /// <summary>
    /// Builds settings with the given assets on the watchlist.
    /// </summary>
    /// <param name="assets">
    /// The assets to track.
    /// </param>
    /// <returns>
    /// The settings.
    /// </returns>
    private static AppSettings SettingsFor(params Asset[] assets)
    {
        var settings = new AppSettings();
        settings.Watchlist.AddRange(assets.Select(WatchlistEntry.From));
        return settings;
    }

    /// <summary>
    /// Waits until a condition holds, failing the test on timeout.
    /// </summary>
    /// <param name="condition">
    /// The condition to wait for.
    /// </param>
    /// <returns>
    /// A task that completes when the condition holds.
    /// </returns>
    private static async Task WaitUntilAsync(Func<bool> condition)
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
    /// Verifies each provider only receives the instruments it serves.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_RoutesWatchlistToOwningProviders()
    {
        await using var service = NewService();

        await service.ApplyAsync(SettingsFor(Btc, Aapl));

        _crypto.Created.Single().Subscribed.Should().ContainSingle().Which.Should().Be(Btc);
        _stocks.Created.Single().Subscribed.Should().ContainSingle().Which.Should().Be(Aapl);
    }

    /// <summary>
    /// Verifies a quote updates the snapshot and raises an event with live health.
    /// </summary>
    [Fact]
    public async Task Quote_FromProvider_UpdatesSnapshotAndRaisesEvent()
    {
        await using var service = NewService();
        AssetSnapshot? raised = null;
        service.SnapshotChanged += (_, s) => raised = s.Quote is null ? raised : s;
        await service.ApplyAsync(SettingsFor(Btc));

        _crypto.Created.Single().Emit(new Quote(Btc.Id, 105m, 100m, _time.GetUtcNow()));
        await WaitUntilAsync(() => raised is not null);

        raised!.Health.Should().Be(AssetHealth.Live);
        raised.Quote!.ChangePercent.Should().Be(5m);
        service.GetSnapshots().Single().Sparkline.Should().Equal(105m);
    }

    /// <summary>
    /// Verifies an instrument whose provider is switched off is kept and marked, not dropped.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WithDisabledProvider_MarksAssetsAsProviderDisabled()
    {
        await using var service = NewService();
        var settings = SettingsFor(Btc, Aapl);
        settings.Providers["stocks"] = new ProviderSettings { Enabled = false };

        await service.ApplyAsync(settings);

        var snapshots = service.GetSnapshots().ToDictionary(s => s.Asset.ProviderId);
        snapshots["stocks"].Health.Should().Be(AssetHealth.ProviderDisabled);
        snapshots["crypto"].Health.Should().Be(AssetHealth.Waiting);
        service.GetProviderStatus("stocks").Should().BeNull();
    }

    /// <summary>
    /// Verifies an instrument of a provider that is not installed is marked as missing.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WithUnknownProvider_MarksAssetAsProviderMissing()
    {
        await using var service = NewService();

        await service.ApplyAsync(SettingsFor(new Asset("gone", "X", "X", AssetClass.Crypto)));

        service.GetSnapshots().Single().Health.Should().Be(AssetHealth.ProviderMissing);
    }

    /// <summary>
    /// Verifies a provider is rebuilt with the new options when its options change.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WithChangedOptions_RecreatesProvider()
    {
        await using var service = NewService();
        var settings = SettingsFor(Btc);
        await service.ApplyAsync(settings);
        var first = _crypto.Created.Single();

        settings.Providers["crypto"] = new ProviderSettings { Options = { ["Endpoint"] = "https://other" } };
        await service.ApplyAsync(settings);

        _crypto.Created.Should().HaveCount(2);
        first.IsDisposed.Should().BeTrue();
        _crypto.Created[1].Options.GetString("Endpoint").Should().Be("https://other");
        _crypto.Created[1].Subscribed.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies applying unchanged settings keeps the running provider.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WithUnchangedOptions_KeepsProvider()
    {
        await using var service = NewService();
        var settings = SettingsFor(Btc);

        await service.ApplyAsync(settings);
        await service.ApplyAsync(settings);

        _crypto.Created.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies removing an instrument from the watchlist unsubscribes and forgets it.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_AfterRemovingAsset_UnsubscribesIt()
    {
        await using var service = NewService();
        await service.ApplyAsync(SettingsFor(Btc));

        await service.ApplyAsync(SettingsFor());

        _crypto.Created.Single().Subscribed.Should().BeEmpty();
        service.GetSnapshots().Should().BeEmpty();
    }

    /// <summary>
    /// Verifies an instrument turns stale when no quote arrives for longer than the threshold.
    /// </summary>
    [Fact]
    public async Task Health_WithoutNewQuotes_BecomesStale()
    {
        await using var service = NewService();
        await service.ApplyAsync(SettingsFor(Btc));
        _crypto.Created.Single().Emit(new Quote(Btc.Id, 1m, null, _time.GetUtcNow()));
        await WaitUntilAsync(() => service.GetSnapshots().Single().Health == AssetHealth.Live);

        _time.Advance(TimeSpan.FromMinutes(2));

        await WaitUntilAsync(() => service.GetSnapshots().Single().Health == AssetHealth.Stale);
    }

    /// <summary>
    /// Verifies a provider failure is reflected in the health of its instruments.
    /// </summary>
    [Fact]
    public async Task ProviderStatus_Reconnecting_MarksAssetsAsReconnecting()
    {
        await using var service = NewService();
        await service.ApplyAsync(SettingsFor(Btc));

        _crypto.Created.Single().ChangeStatus(ProviderConnectionState.Reconnecting, "offline");

        var snapshot = service.GetSnapshots().Single();
        snapshot.Health.Should().Be(AssetHealth.Reconnecting);
        snapshot.HealthDetail.Should().Be("offline");
    }

    /// <summary>
    /// Verifies history from the provider seeds the sparkline.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WithHistoryProvider_SeedsSparkline()
    {
        var factory = new HistoryFactory(_time.GetUtcNow());
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        var seeded = new TaskCompletionSource();
        service.SnapshotChanged += (_, s) =>
        {
            if (s.Sparkline.Count == 2)
            {
                seeded.TrySetResult();
            }
        };

        await service.ApplyAsync(SettingsFor(Btc));
        await seeded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        service.GetSnapshots().Single().Sparkline.Should().Equal(1m, 2m);
    }

    /// <summary>
    /// Verifies search is routed to the named provider and unavailable providers return nothing.
    /// </summary>
    [Fact]
    public async Task SearchAsync_RoutesToProviderAndHandlesUnavailable()
    {
        await using var service = NewService();
        await service.ApplyAsync(SettingsFor());
        _crypto.Created.Single().SearchResults = [Btc];

        var found = await service.SearchAsync("crypto", "btc", null);
        var missing = await service.SearchAsync("nope", "btc", null);

        found.Should().ContainSingle();
        missing.Should().BeEmpty();
        service.SupportsSearch("crypto").Should().BeTrue();
        service.SupportsSearch("nope").Should().BeFalse();
    }

    /// <summary>
    /// A factory whose provider already has history, used to test sparkline seeding on the first apply.
    /// </summary>
    /// <param name="now">
    /// The current time used to build the history.
    /// </param>
    private sealed class HistoryFactory(DateTimeOffset now) : IMarketDataProviderFactory
    {
        /// <inheritdoc />
        public ProviderDescriptor Descriptor { get; } = new("crypto", "Crypto", "Test", [AssetClass.Crypto], ProviderUpdateMode.Streaming);

        /// <inheritdoc />
        public IMarketDataProvider Create(ProviderCreationContext context) =>
            new FakeProvider(Descriptor, context.Options)
            {
                History = [new PricePoint(now.AddMinutes(-2), 1m), new PricePoint(now.AddMinutes(-1), 2m)],
            };
    }
}
