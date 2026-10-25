using FluxTick.Abstractions;
using FluxTick.Core.Application;
using FluxTick.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for <see cref="AlertService"/>.
/// </summary>
public sealed class AlertServiceTests : IAsyncDisposable
{
    /// <summary>
    /// The Bitcoin test asset.
    /// </summary>
    private static readonly Asset Btc = new("crypto", "BTC", "Bitcoin", AssetClass.Crypto);

    /// <summary>
    /// The crypto test provider factory.
    /// </summary>
    private readonly FakeProviderFactory _crypto = new("crypto", [AssetClass.Crypto]);

    /// <summary>
    /// The controllable clock.
    /// </summary>
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// The market data service under the alerts.
    /// </summary>
    private readonly MarketDataService _market;

    /// <summary>
    /// The settings that hold the rules.
    /// </summary>
    private readonly SettingsManager _settings = new(new MemoryStore(), NullLogger<SettingsManager>.Instance);

    /// <summary>
    /// The service under test.
    /// </summary>
    private readonly AlertService _alerts;

    /// <summary>
    /// The alerts that fired, in order.
    /// </summary>
    private readonly List<AlertTriggeredEventArgs> _fired = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="AlertServiceTests"/> class.
    /// </summary>
    public AlertServiceTests()
    {
        _market = new MarketDataService(new ProviderRegistry([_crypto]), NullLoggerFactory.Instance, _time);
        _alerts = new AlertService(_market, _settings, NullLogger<AlertService>.Instance);
        _alerts.AlertTriggered += (_, e) =>
        {
            lock (_fired)
            {
                _fired.Add(e);
            }
        };
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _alerts.Dispose();
        await _market.DisposeAsync();
    }

    /// <summary>
    /// Starts the services with a rule on Bitcoin and connect-on-demand switched on.
    /// </summary>
    /// <param name="condition">
    /// The condition of the rule.
    /// </param>
    /// <param name="threshold">
    /// The threshold of the rule.
    /// </param>
    /// <param name="repeat">
    /// Whether the rule repeats.
    /// </param>
    /// <returns>
    /// The provider that serves Bitcoin.
    /// </returns>
    private async Task<FakeProvider> StartAsync(AlertCondition condition, decimal threshold, bool repeat = false)
    {
        var settings = new AppSettings();
        settings.General.ConnectOnDemand = true;
        settings.Watchlist.Add(WatchlistEntry.From(Btc));
        settings.Alerts.Add(new AlertRule { Id = "r1", ProviderId = "crypto", Symbol = "BTC", Condition = condition, Threshold = threshold, Repeat = repeat });
        await _settings.LoadAsync(settings);
        await _market.ApplyAsync(_settings.Current);
        _alerts.Start();
        var provider = _crypto.Created.Single();
        await WaitUntilAsync(() => provider.Subscribed.Count == 1);
        return provider;
    }

    /// <summary>
    /// Publishes a price and waits until the market has handled it.
    /// </summary>
    /// <param name="provider">
    /// The provider to publish from.
    /// </param>
    /// <param name="price">
    /// The price.
    /// </param>
    /// <param name="reference">
    /// The reference price.
    /// </param>
    /// <returns>
    /// A task that completes once the snapshot of the price was raised.
    /// </returns>
    private async Task EmitAsync(FakeProvider provider, decimal price, decimal? reference = null)
    {
        var seen = new TaskCompletionSource();
        void Handler(object? sender, AssetSnapshot snapshot)
        {
            if (snapshot.Quote?.Price == price)
            {
                seen.TrySetResult();
            }
        }

        _market.SnapshotChanged += Handler;
        try
        {
            provider.Emit(new Quote(Btc.Id, price, reference, _time.GetUtcNow()));
            await seen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            _market.SnapshotChanged -= Handler;
        }
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
    /// Verifies a rule fires when the price crosses the threshold, and only once while it stays across.
    /// </summary>
    [Fact]
    public async Task PriceAbove_CrossingThreshold_FiresOnceWhileItStaysAbove()
    {
        var provider = await StartAsync(AlertCondition.PriceAbove, 100, repeat: true);

        await EmitAsync(provider, 99);
        await EmitAsync(provider, 101);
        await EmitAsync(provider, 102);
        await EmitAsync(provider, 103);

        _fired.Should().ContainSingle().Which.Quote.Price.Should().Be(101);
    }

    /// <summary>
    /// Verifies starting twice does not subscribe twice, which would fire every alert twice.
    /// </summary>
    [Fact]
    public async Task Start_CalledTwice_FiresOnlyOnce()
    {
        var provider = await StartAsync(AlertCondition.PriceAbove, 100, repeat: true);
        _alerts.Start();

        await EmitAsync(provider, 99);
        await EmitAsync(provider, 101);

        _fired.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies a repeating rule fires again once the condition stopped and came true again.
    /// </summary>
    [Fact]
    public async Task RepeatingRule_AfterConditionStopped_FiresAgain()
    {
        var provider = await StartAsync(AlertCondition.PriceAbove, 100, repeat: true);

        await EmitAsync(provider, 99);
        await EmitAsync(provider, 101);
        await EmitAsync(provider, 98);
        await EmitAsync(provider, 105);

        _fired.Select(e => e.Quote.Price).Should().Equal(101, 105);
    }

    /// <summary>
    /// Verifies a condition that already holds with the first price does not fire.
    /// </summary>
    [Fact]
    public async Task FirstQuote_AlreadySatisfied_DoesNotFire()
    {
        var provider = await StartAsync(AlertCondition.PriceAbove, 100, repeat: true);

        await EmitAsync(provider, 150);
        await EmitAsync(provider, 160);

        _fired.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a rule that does not repeat is switched off in the settings after it fired.
    /// </summary>
    [Fact]
    public async Task OneShotRule_AfterFiring_IsDisabledAndReleasesTheUsage()
    {
        var provider = await StartAsync(AlertCondition.PriceBelow, 50);

        await EmitAsync(provider, 60);
        await EmitAsync(provider, 49);

        _fired.Should().ContainSingle();
        await WaitUntilAsync(() => !_settings.Current.Alerts.Single().Enabled);

        // The last usage is gone, so the sources disconnect once the grace period is over.
        _time.Advance(TimeSpan.FromSeconds(31));
        await WaitUntilAsync(() => provider.Subscribed.Count == 0);
    }

    /// <summary>
    /// Verifies a price that is no longer live neither fires a rule nor counts as the condition having stopped.
    /// </summary>
    [Fact]
    public async Task StalePrice_DoesNotFireAndKeepsTheState()
    {
        var provider = await StartAsync(AlertCondition.PriceAbove, 100, repeat: true);
        await EmitAsync(provider, 99);

        // The price goes stale, then a refresh raises a snapshot without a new quote. The alert service subscribed first, so it
        // has seen the snapshot by the time this handler runs.
        var stale = new TaskCompletionSource();
        _market.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.Health == AssetHealth.Stale)
            {
                stale.TrySetResult();
            }
        };
        _time.Advance(TimeSpan.FromMinutes(2));
        await stale.Task.WaitAsync(TimeSpan.FromSeconds(5));

        _fired.Should().BeEmpty();
        await EmitAsync(provider, 101);
        _fired.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies a change that is not about the alerts leaves them as they are, while turning off Repeat of a rule takes
    /// effect at once: the rule then disables itself after it fires.
    /// </summary>
    [Fact]
    public async Task SettingsChange_UnrelatedKeepsTheState_RepeatToggleTakesEffect()
    {
        var provider = await StartAsync(AlertCondition.PriceAbove, 100, repeat: true);
        await EmitAsync(provider, 99);
        await EmitAsync(provider, 101);
        _fired.Should().ContainSingle();

        await _settings.UpdateAsync(s => s.Appearance.Opacity = 0.5);
        await EmitAsync(provider, 102);
        _fired.Should().ContainSingle("the condition has been true all along");

        await _settings.UpdateAsync(s => s.Alerts.Single().Repeat = false);
        await EmitAsync(provider, 98);
        await EmitAsync(provider, 103);

        _fired.Should().HaveCount(2);
        await WaitUntilAsync(() => !_settings.Current.Alerts.Single().Enabled);
    }

    /// <summary>
    /// Verifies a rule that is edited starts over, so a condition that already holds does not fire.
    /// </summary>
    [Fact]
    public async Task EditedThreshold_StartsOver()
    {
        var provider = await StartAsync(AlertCondition.PriceAbove, 100, repeat: true);
        await EmitAsync(provider, 90);

        await _settings.UpdateAsync(s => s.Alerts.Single().Threshold = 80);
        await EmitAsync(provider, 91);

        _fired.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the sources stay connected for a rule while the window is hidden, and disconnect when the last rule is
    /// removed.
    /// </summary>
    [Fact]
    public async Task EnabledRule_HoldsTheUsageUntilItIsRemoved()
    {
        var provider = await StartAsync(AlertCondition.PriceAbove, 100);

        _time.Advance(TimeSpan.FromMinutes(5));
        provider.Subscribed.Should().HaveCount(1);

        await _settings.UpdateAsync(s => s.Alerts.Clear());
        _time.Advance(TimeSpan.FromSeconds(31));
        await WaitUntilAsync(() => provider.Subscribed.Count == 0);
    }

    /// <summary>
    /// Verifies the rules of an instrument that leaves the watchlist are dropped.
    /// </summary>
    [Fact]
    public async Task RemovingTheInstrument_DropsItsRules()
    {
        await StartAsync(AlertCondition.PriceAbove, 100);

        await _settings.UpdateAsync(s => s.Watchlist.Clear());

        _settings.Current.Alerts.Should().BeEmpty();
    }

    /// <summary>
    /// A settings store that keeps nothing.
    /// </summary>
    private sealed class MemoryStore : ISettingsStore
    {
        /// <inheritdoc />
        public Task<AppSettings?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<AppSettings?>(null);

        /// <inheritdoc />
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
