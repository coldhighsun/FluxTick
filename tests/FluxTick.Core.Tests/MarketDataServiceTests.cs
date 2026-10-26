using FluxTick.Abstractions;
using FluxTick.Core.Application;
using FluxTick.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging;
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
    /// Verifies disposing does not wait for a provider that is stuck while subscribing.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_WhileProviderIsStuck_Completes()
    {
        var factory = new StuckFactory();
        var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        var apply = service.ApplyAsync(SettingsFor(Btc));
        await WaitUntilAsync(() => factory.Entered);

        var dispose = service.DisposeAsync().AsTask();

        (await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(3)))).Should().BeSameAs(dispose);
        try
        {
            await apply;
        }
        catch (OperationCanceledException)
        {
            // The stuck call is cancelled by the disposal.
        }
    }

    /// <summary>
    /// Verifies a provider created while the service is being disposed is disposed too, not left running.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_WhileProviderIsBeingCreated_DisposesIt()
    {
        var factory = new SlowCreateFactory();
        var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        var apply = Task.Run(() => service.ApplyAsync(SettingsFor(Btc)));
        await WaitUntilAsync(() => factory.Entered);

        var dispose = service.DisposeAsync().AsTask();
        factory.Release.Set();
        await dispose;
        await apply;

        factory.Created.Should().ContainSingle().Which.IsDisposed.Should().BeTrue();
        service.GetProviderStatus("crypto").Should().BeNull();
    }

    /// <summary>
    /// Verifies a snapshot handler may wait for another apply, because snapshots are raised after the apply lock is
    /// released.
    /// </summary>
    [Fact]
    public async Task Apply_SnapshotHandlerWaitingForAnotherApply_DoesNotDeadlock()
    {
        await using var service = NewService();
        var nested = 0;
        service.SnapshotChanged += (_, _) =>
        {
            if (Interlocked.Exchange(ref nested, 1) == 0)
            {
                service.ApplyAsync(SettingsFor(Btc)).GetAwaiter().GetResult();
            }
        };

        var apply = Task.Run(() => service.ApplyAsync(SettingsFor(Btc)));

        (await Task.WhenAny(apply, Task.Delay(TimeSpan.FromSeconds(3)))).Should().BeSameAs(apply);
    }

    /// <summary>
    /// Verifies status events of different providers are raised in the order they were queued, once the events left over
    /// from applying the settings have been raised.
    /// </summary>
    [Fact]
    public async Task StatusEvents_FromDifferentProviders_KeepTheirOrder()
    {
        await using var service = NewService();
        var seen = new List<string>();
        service.ProviderStatusChanged += (_, e) =>
        {
            lock (seen)
            {
                seen.Add($"{e.ProviderId}:{e.Status.State}");
            }
        };
        await service.ApplyAsync(SettingsFor(Btc, Aapl));

        // Applying announces both providers; wait for that, so only the changes below are left to be raised.
        await WaitUntilAsync(() =>
        {
            lock (seen)
            {
                return seen.Count >= 2;
            }
        });
        lock (seen)
        {
            seen.Clear();
        }

        _crypto.Created.Single().ChangeStatus(ProviderConnectionState.Connected);
        await WaitUntilAsync(() =>
        {
            lock (seen)
            {
                return seen.Count >= 1;
            }
        });
        _stocks.Created.Single().ChangeStatus(ProviderConnectionState.Failed, "down");

        await WaitUntilAsync(() =>
        {
            lock (seen)
            {
                return seen.Count >= 2;
            }
        });
        lock (seen)
        {
            seen.Should().Equal("crypto:Connected", "stocks:Failed");
        }
    }

    /// <summary>
    /// Verifies a handler that throws does not stop later status events.
    /// </summary>
    [Fact]
    public async Task StatusEvents_HandlerThrows_LaterEventsStillArrive()
    {
        var logs = new CollectingLoggerFactory();
        await using var service = new MarketDataService(new ProviderRegistry([_crypto]), logs, _time);
        await service.ApplyAsync(SettingsFor(Btc));
        var provider = _crypto.Created.Single();
        var states = new List<ProviderConnectionState>();
        service.ProviderStatusChanged += (_, e) =>
        {
            lock (states)
            {
                states.Add(e.Status.State);
                if (states.Count == 1)
                {
                    throw new InvalidOperationException("handler failed");
                }
            }
        };

        provider.ChangeStatus(ProviderConnectionState.Connected);
        await WaitUntilAsync(() => logs.Messages.Any(m => m.Contains("A handler of ProviderStatusChanged failed", StringComparison.Ordinal)));
        provider.ChangeStatus(ProviderConnectionState.Reconnecting, "retry");

        await WaitUntilAsync(() =>
        {
            lock (states)
            {
                return states.Contains(ProviderConnectionState.Reconnecting);
            }
        });
    }

    /// <summary>
    /// Verifies events still queued when the service is disposed are dropped, not raised against the cleared providers.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_WithQueuedStatusEvents_DropsThem()
    {
        var service = NewService();
        await service.ApplyAsync(SettingsFor(Btc));
        var provider = _crypto.Created.Single();
        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var raised = 0;
        var snapshots = 0;
        service.ProviderStatusChanged += (_, _) =>
        {
            Interlocked.Increment(ref raised);
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(5));
        };
        provider.ChangeStatus(ProviderConnectionState.Connected);
        entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        service.SnapshotChanged += (_, _) => Interlocked.Increment(ref snapshots);
        provider.ChangeStatus(ProviderConnectionState.Reconnecting, "retry");
        provider.ChangeStatus(ProviderConnectionState.Failed, "down");
        var disposing = service.DisposeAsync().AsTask();

        release.Set();
        var finished = await Task.WhenAny(disposing, Task.Delay(TimeSpan.FromSeconds(4)));

        // Disposal waits for the pump for 5 seconds at most, so finishing earlier means the pump drained and ended.
        finished.Should().BeSameAs(disposing);
        raised.Should().Be(1);
        snapshots.Should().Be(0);
    }

    /// <summary>
    /// Verifies a handler that throws does not keep the other handlers from getting a status event.
    /// </summary>
    [Fact]
    public async Task StatusEvents_FirstHandlerThrows_OtherHandlersStillGetIt()
    {
        await using var service = NewService();
        await service.ApplyAsync(SettingsFor(Btc));
        var received = new List<ProviderConnectionState>();
        service.ProviderStatusChanged += (_, _) => throw new InvalidOperationException("first handler");
        service.ProviderStatusChanged += (_, e) =>
        {
            lock (received)
            {
                received.Add(e.Status.State);
            }
        };

        _crypto.Created.Single().ChangeStatus(ProviderConnectionState.Connected);

        await WaitUntilAsync(() =>
        {
            lock (received)
            {
                return received.Contains(ProviderConnectionState.Connected);
            }
        });
    }

    /// <summary>
    /// Verifies a snapshot handler that throws neither fails the apply nor keeps the other handlers from the snapshot,
    /// and is reported under its own name.
    /// </summary>
    [Fact]
    public async Task SnapshotEvents_FirstHandlerThrows_ApplyAndOtherHandlersAreUnaffected()
    {
        var logs = new CollectingLoggerFactory();
        await using var service = new MarketDataService(new ProviderRegistry([_crypto]), logs, _time);
        var received = 0;
        service.SnapshotChanged += (_, _) => throw new InvalidOperationException("first handler");
        service.SnapshotChanged += (_, _) => Interlocked.Increment(ref received);

        await service.ApplyAsync(SettingsFor(Btc));

        received.Should().BeGreaterThan(0);
        logs.Messages.Should().Contain(m => m.Contains("A handler of SnapshotChanged failed", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies a snapshot handler that throws on a quote neither ends the quote stream of the provider nor keeps the
    /// other handlers from the quote.
    /// </summary>
    [Fact]
    public async Task Quotes_HandlerThrows_QuoteStreamAndOtherHandlersContinue()
    {
        await using var service = NewService();
        await service.ApplyAsync(SettingsFor(Btc));
        var provider = _crypto.Created.Single();
        var prices = new List<decimal>();
        service.SnapshotChanged += (_, s) =>
        {
            if (s.Quote is not null)
            {
                throw new InvalidOperationException("first handler");
            }
        };
        service.SnapshotChanged += (_, s) =>
        {
            if (s.Quote is { } quote)
            {
                lock (prices)
                {
                    prices.Add(quote.Price);
                }
            }
        };

        provider.Emit(new Quote(Btc.Id, 101m, 100m, _time.GetUtcNow()));
        provider.Emit(new Quote(Btc.Id, 102m, 100m, _time.GetUtcNow()));
        provider.Emit(new Quote(Btc.Id, 103m, 100m, _time.GetUtcNow()));

        await WaitUntilAsync(() =>
        {
            lock (prices)
            {
                return prices.Count == 3;
            }
        });
        lock (prices)
        {
            prices.Should().Equal(101m, 102m, 103m);
        }
    }

    /// <summary>
    /// Verifies a slow status handler is reported in the log, because every status event waits behind it.
    /// </summary>
    [Fact]
    public async Task StatusEvents_SlowHandler_IsLogged()
    {
        var logs = new CollectingLoggerFactory();
        await using var service = new MarketDataService(
            new ProviderRegistry([_crypto]),
            logs,
            _time,
            new MarketDataServiceOptions { SlowHandlerWarning = TimeSpan.FromMilliseconds(20) });
        await service.ApplyAsync(SettingsFor(Btc));
        service.ProviderStatusChanged += (_, _) => Thread.Sleep(100);

        _crypto.Created.Single().ChangeStatus(ProviderConnectionState.Connected);

        await WaitUntilAsync(() => logs.Messages.Any(m => m.Contains("the events behind it wait", StringComparison.Ordinal)));
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
    /// Verifies a status change a provider reports while the subscriptions are being routed, such as going idle on an
    /// empty set, is raised after the apply lock is released, so a handler may wait for another apply.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_ProviderStatusChangedWhileRouting_IsRaisedOutsideTheLock()
    {
        await using var service = NewService();
        await service.ApplyAsync(SettingsFor(Btc));
        var provider = _crypto.Created.Single();
        provider.ChangeStatus(ProviderConnectionState.Connected);
        var handled = new TaskCompletionSource();
        var nested = 0;
        service.ProviderStatusChanged += (_, e) =>
        {
            if (e.ProviderId == "crypto" && e.Status.State == ProviderConnectionState.Idle && Interlocked.Exchange(ref nested, 1) == 0)
            {
                service.ApplyAsync(SettingsFor()).GetAwaiter().GetResult();
                handled.TrySetResult();
            }
        };

        await service.ApplyAsync(SettingsFor());

        (await Task.WhenAny(handled.Task, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(handled.Task);
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
    /// Verifies writing a value out explicitly and leaving it to the default are the same configuration,
    /// so the running provider is neither restarted when the explicit default is dropped nor when it is added.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_ExplicitDefaultVersusAbsent_KeepsProvider()
    {
        await using var service = NewService();
        var settings = SettingsFor(Btc);
        settings.Providers["crypto"] = new ProviderSettings { Options = { ["Endpoint"] = "default" } };
        await service.ApplyAsync(settings);

        settings.Providers["crypto"] = new ProviderSettings();
        await service.ApplyAsync(settings);
        settings.Providers["crypto"] = new ProviderSettings { Options = { ["Endpoint"] = "default" } };
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
    /// Verifies a provider no instrument uses stays idle without a subscription, gets one when an instrument that it serves
    /// is added, and is left with none again when the last one is removed.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_ProviderUsedByNoInstrument_IsSubscribedOnlyWhileAnInstrumentUsesIt()
    {
        await using var service = NewService();

        await service.ApplyAsync(SettingsFor(Btc));

        var stocks = _stocks.Created.Single();
        stocks.Subscribed.Should().BeEmpty();
        service.GetProviderStatus("stocks")!.State.Should().Be(ProviderConnectionState.Idle);

        await service.ApplyAsync(SettingsFor(Btc, Aapl));

        stocks.Subscribed.Should().ContainSingle().Which.Should().Be(Aapl);

        await service.ApplyAsync(SettingsFor(Btc));

        stocks.Subscribed.Should().BeEmpty();
        _crypto.Created.Single().Subscribed.Should().ContainSingle().Which.Should().Be(Btc);
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
    /// Verifies an instrument is stale when quotes keep arriving but carry a time older than the threshold, and live
    /// again once a quote with a fresh time arrives.
    /// </summary>
    [Fact]
    public async Task Health_WithFreshlyReceivedButOldQuote_IsStaleUntilTimeAdvances()
    {
        await using var service = NewService();
        await service.ApplyAsync(SettingsFor(Btc));
        var provider = _crypto.Created.Single();

        provider.Emit(new Quote(Btc.Id, 1m, null, _time.GetUtcNow().AddMinutes(-5)));
        await WaitUntilAsync(() => service.GetSnapshots().Single().Health == AssetHealth.Stale);

        provider.Emit(new Quote(Btc.Id, 2m, null, _time.GetUtcNow()));
        await WaitUntilAsync(() => service.GetSnapshots().Single().Health == AssetHealth.Live);
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
    /// Verifies a source that keeps answering with an empty history is asked a few times and then left alone, instead of
    /// again with every settings change.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_HistoryStaysEmpty_StopsAskingAfterAFewAnswers()
    {
        var factory = new EmptyHistoryFactory();
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);

        // An apply while a load is still running does not start another, so apply until the third answer arrived.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (factory.Provider?.HistoryRequests is null or < 3)
        {
            Assert.True(DateTime.UtcNow < deadline, "The history was not asked for three times.");
            await service.ApplyAsync(SettingsFor(Btc));
            await Task.Delay(20);
        }

        // From now on it is left alone, however often the settings are applied.
        for (var i = 0; i < 5; i++)
        {
            await service.ApplyAsync(SettingsFor(Btc));
            await Task.Delay(50);
        }

        factory.Provider.HistoryRequests.Should().Be(3);
    }

    /// <summary>
    /// Verifies a source that fails with a cancellation, as an HTTP timeout does, does not keep the other sources from
    /// getting their subscriptions.
    /// </summary>
    /// <param name="cryptoFails">
    /// Whether the crypto source fails; otherwise the stocks source does. Both, because the order the sources are
    /// routed in is not something the test should depend on.
    /// </param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApplyAsync_SourceFailsWithCancellation_OtherSourcesStillSubscribe(bool cryptoFails)
    {
        await using var service = NewService();
        await service.ApplyAsync(SettingsFor(Btc, Aapl));
        var crypto = _crypto.Created.Single();
        var stocks = _stocks.Created.Single();
        (cryptoFails ? crypto : stocks).SubscribeFailure = new TaskCanceledException("timed out");

        var eth = new Asset("crypto", "ETH", "Ether", AssetClass.Crypto);
        var msft = new Asset("stocks", "MSFT", "Microsoft", AssetClass.Equity);

        // Both sources get a new instrument; the one that fails must not keep the other from getting its own.
        var act = () => service.ApplyAsync(SettingsFor(Btc, eth, Aapl, msft));

        await act.Should().ThrowAsync<TaskCanceledException>();
        var healthy = cryptoFails ? stocks : crypto;
        healthy.Subscribed.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies replacing a provider, for example after its options changed, ends the wait that followed the failed
    /// history load of the old one.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_ProviderReplaced_HistoryIsAskedForAtOnce()
    {
        _crypto.HistoryFailure = new HttpRequestException("down");
        await using var service = NewService();
        await service.ApplyAsync(SettingsFor(Btc));
        await WaitUntilAsync(() => _crypto.Created.Single().HistoryRequests == 1);
        await Task.Delay(100);

        var changed = SettingsFor(Btc);
        changed.Providers["crypto"] = new ProviderSettings { Options = { ["Endpoint"] = "other" } };
        await service.ApplyAsync(changed);

        await WaitUntilAsync(() => _crypto.Created.Count == 2 && _crypto.Created[1].HistoryRequests == 1);
    }

    /// <summary>
    /// Verifies a source whose history fails is not asked again at once, but after a wait that grows.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_HistoryFails_IsRetriedOnlyAfterAWait()
    {
        var factory = new EmptyHistoryFactory(new HttpRequestException("down"));
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));
        await WaitUntilAsync(() => factory.Provider?.HistoryRequests == 1);
        await Task.Delay(100);

        // The failure is recorded now; applying again, as showing the window does, must not ask again.
        for (var i = 0; i < 3; i++)
        {
            await service.ApplyAsync(SettingsFor(Btc));
            await Task.Delay(30);
        }

        factory.Provider!.HistoryRequests.Should().Be(1, "the source is left alone for a while after a failure");

        _time.Advance(TimeSpan.FromSeconds(31));
        await service.ApplyAsync(SettingsFor(Btc));
        await WaitUntilAsync(() => factory.Provider.HistoryRequests == 2);
    }

    /// <summary>
    /// Verifies the sparkline only shows the last hour: once the seeded history is older than that it is gone, and the
    /// change is published without a new quote.
    /// </summary>
    [Fact]
    public async Task Sparkline_WhenHistoryAgesOutOfTheLastHour_BecomesEmptyAndIsPublished()
    {
        var factory = new HistoryFactory(_time.GetUtcNow());
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 2);
        var emptied = new TaskCompletionSource();
        service.SnapshotChanged += (_, s) =>
        {
            if (s.Sparkline.Count == 0)
            {
                emptied.TrySetResult();
            }
        };

        _time.Advance(TimeSpan.FromMinutes(30));
        service.GetSnapshots().Single().Sparkline.Should().Equal(1m, 2m);
        _time.Advance(TimeSpan.FromMinutes(32));

        await emptied.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.GetSnapshots().Single().Sparkline.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a snapshot says when the prices were traded and where the window ends, and that it is raised again when
    /// the time moves into a new minute although nothing else changed.
    /// </summary>
    [Fact]
    public async Task Snapshot_HasTimelineAndIsRaisedAgainInANewBucket()
    {
        var factory = new HistoryFactory(_time.GetUtcNow());
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        await service.ApplyAsync(SettingsFor(Btc));
        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 2);
        var first = service.GetSnapshots().Single().Timeline;
        first.Should().NotBeNull();
        first!.Times.Should().HaveCount(2);
        first.AsOf.Should().Be(_time.GetUtcNow());
        first.Window.Should().Be(TimeSpan.FromMinutes(60));
        var raised = new TaskCompletionSource<AssetSnapshot>();
        service.SnapshotChanged += (_, s) =>
        {
            if (s.Timeline!.AsOf > first.AsOf)
            {
                raised.TrySetResult(s);
            }
        };

        _time.Advance(TimeSpan.FromMinutes(2));

        var later = await raised.Task.WaitAsync(TimeSpan.FromSeconds(5));
        later.Timeline!.Times.Should().Equal(first.Times,"the points did not move, only the window did");
    }

    /// <summary>
    /// Verifies the time moving into a new minute raises nothing for an instrument whose sparkline is empty and so cannot
    /// change, and the move that empties a sparkline is still raised.
    /// </summary>
    [Fact]
    public async Task Snapshot_NewBucketWithEmptySparkline_RaisesNothing()
    {
        await using var service = NewService();
        await service.ApplyAsync(SettingsFor(Btc));
        var start = _time.GetUtcNow();
        var healths = new System.Collections.Concurrent.ConcurrentQueue<AssetHealth>();
        service.SnapshotChanged += (_, s) =>
        {
            // What was raised before the time moved on is of no interest.
            if (s.Timeline!.AsOf > start)
            {
                healths.Enqueue(s.Health);
            }
        };

        _time.Advance(TimeSpan.FromMinutes(3));

        // A status change raises a snapshot that is known to come: when it has, whatever the new minute raised has too.
        _crypto.Created.Single().ChangeStatus(ProviderConnectionState.Reconnecting, "offline");
        await WaitUntilAsync(() => healths.Contains(AssetHealth.Reconnecting));
        healths.Should().OnlyContain(h => h == AssetHealth.Reconnecting, "nothing was received, so there is no chart that could slide");
    }

    /// <summary>
    /// Verifies history still seeds the sparkline when a source sends its first price the moment it is subscribed, before
    /// the service has looked at the sparkline.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_FirstQuoteArrivesImmediately_StillSeedsSparkline()
    {
        var factory = new HistoryFactory(_time.GetUtcNow(), quoteOnSubscribe: 5m);
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);

        await service.ApplyAsync(SettingsFor(Btc));

        await WaitUntilAsync(() => service.GetSnapshots().Single().Sparkline.Count == 3);
        service.GetSnapshots().Single().Sparkline.Should().Equal(1m, 2m, 5m);
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
    /// Verifies a value that means the same but is written differently from the default (a duration with extra
    /// zeros) does not restart the provider when it is rewritten in canonical form or dropped.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_EquivalentlyWrittenValue_KeepsProvider()
    {
        var factory = new OptionsFactory([new ProviderOptionDefinition("Wait", "Wait", ProviderOptionType.Duration, "00:00:05")]);
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);
        var settings = SettingsFor(Btc);
        settings.Providers["crypto"] = new ProviderSettings { Options = { ["Wait"] = "00:00:05.0000000" } };
        await service.ApplyAsync(settings);

        settings.Providers["crypto"] = new ProviderSettings { Options = { ["Wait"] = "00:00:05" } };
        await service.ApplyAsync(settings);
        settings.Providers["crypto"] = new ProviderSettings();
        await service.ApplyAsync(settings);

        factory.Created.Should().ContainSingle();

        settings.Providers["crypto"] = new ProviderSettings { Options = { ["Wait"] = "00:00:07" } };
        await service.ApplyAsync(settings);

        factory.Created.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies a descriptor that declares an option key twice does not break applying the settings.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_DescriptorWithDuplicateOptionKeys_StillStartsTheProvider()
    {
        var factory = new OptionsFactory(
        [
            new ProviderOptionDefinition("Url", "Url", ProviderOptionType.String, "a"),
            new ProviderOptionDefinition("Url", "Url", ProviderOptionType.String, "b"),
        ]);
        await using var service = new MarketDataService(new ProviderRegistry([factory]), NullLoggerFactory.Instance, _time);

        var act = () => service.ApplyAsync(SettingsFor(Btc));

        await act.Should().NotThrowAsync();
        factory.Created.Should().ContainSingle();
    }

    /// <summary>
    /// A factory with configurable option declarations that counts the providers it created.
    /// </summary>
    /// <param name="options">
    /// The declared options.
    /// </param>
    private sealed class OptionsFactory(IReadOnlyList<ProviderOptionDefinition> options) : IMarketDataProviderFactory
    {
        /// <summary>
        /// Gets the providers created so far.
        /// </summary>
        public List<FakeProvider> Created { get; } = [];

        /// <inheritdoc />
        public ProviderDescriptor Descriptor { get; } =
            new("crypto", "Crypto", "Test", [AssetClass.Crypto], ProviderUpdateMode.Streaming, Options: options);

        /// <inheritdoc />
        public IMarketDataProvider Create(ProviderCreationContext context)
        {
            var provider = new FakeProvider(Descriptor, context.Options);
            Created.Add(provider);
            return provider;
        }
    }

    /// <summary>
    /// A logger factory that remembers the formatted messages of all its loggers.
    /// </summary>
    private sealed class CollectingLoggerFactory : ILoggerFactory
    {
        /// <summary>
        /// The messages logged so far.
        /// </summary>
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();

        /// <summary>
        /// Gets a snapshot of the messages logged so far.
        /// </summary>
        public IReadOnlyCollection<string> Messages => _messages.ToArray();

        /// <inheritdoc />
        public void AddProvider(ILoggerProvider provider)
        {
        }

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName) => new CollectingLogger(_messages);

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <summary>
        /// A logger that stores the formatted messages.
        /// </summary>
        /// <param name="messages">
        /// The queue to store them in.
        /// </param>
        private sealed class CollectingLogger(System.Collections.Concurrent.ConcurrentQueue<string> messages) : ILogger
        {
            /// <inheritdoc />
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            /// <inheritdoc />
            public bool IsEnabled(LogLevel logLevel) => true;

            /// <inheritdoc />
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
    }

    /// <summary>
    /// A factory whose creating waits until the test lets it go.
    /// </summary>
    private sealed class SlowCreateFactory : IMarketDataProviderFactory
    {
        /// <summary>
        /// Whether a provider is being created.
        /// </summary>
        public volatile bool Entered;

        /// <summary>
        /// Gets the signal that lets creating finish.
        /// </summary>
        public ManualResetEventSlim Release { get; } = new();

        /// <summary>
        /// Gets the providers created so far.
        /// </summary>
        public List<FakeProvider> Created { get; } = [];

        /// <inheritdoc />
        public ProviderDescriptor Descriptor { get; } = new("crypto", "Crypto", "Test", [AssetClass.Crypto], ProviderUpdateMode.Streaming);

        /// <inheritdoc />
        public IMarketDataProvider Create(ProviderCreationContext context)
        {
            Entered = true;
            Release.Wait(TimeSpan.FromSeconds(10));
            var provider = new FakeProvider(Descriptor, context.Options);
            Created.Add(provider);
            return provider;
        }
    }

    /// <summary>
    /// A factory of providers that never finish subscribing until they are disposed.
    /// </summary>
    private sealed class StuckFactory : IMarketDataProviderFactory
    {
        /// <summary>
        /// Whether a provider started to subscribe.
        /// </summary>
        public volatile bool Entered;

        /// <inheritdoc />
        public ProviderDescriptor Descriptor { get; } = new("crypto", "Crypto", "Test", [AssetClass.Crypto], ProviderUpdateMode.Streaming);

        /// <inheritdoc />
        public IMarketDataProvider Create(ProviderCreationContext context) => new StuckProvider(this, Descriptor);
    }

    /// <summary>
    /// A provider whose subscribing only ends when it is disposed.
    /// </summary>
    /// <param name="owner">
    /// The factory to tell when subscribing started.
    /// </param>
    /// <param name="descriptor">
    /// The descriptor.
    /// </param>
    private sealed class StuckProvider(StuckFactory owner, ProviderDescriptor descriptor) : MarketDataProviderBase(descriptor, TimeProvider.System)
    {
        /// <inheritdoc />
        protected override async Task OnSubscriptionsChangedAsync(
            IReadOnlyCollection<Asset> current,
            IReadOnlyCollection<Asset> added,
            IReadOnlyCollection<Asset> removed,
            CancellationToken cancellationToken)
        {
            owner.Entered = true;
            await Task.Delay(Timeout.Infinite, ShutdownToken);
        }

        /// <inheritdoc />
        protected override ValueTask DisposeCoreAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// A factory whose provider has no history or fails to give it, and which keeps the provider it created.
    /// </summary>
    /// <param name="failure">
    /// The exception the history fails with, or <see langword="null"/> for an empty answer.
    /// </param>
    private sealed class EmptyHistoryFactory(Exception? failure = null) : IMarketDataProviderFactory
    {
        /// <summary>
        /// Gets the provider that was created, or <see langword="null"/> before there is one.
        /// </summary>
        public FakeProvider? Provider { get; private set; }

        /// <inheritdoc />
        public ProviderDescriptor Descriptor { get; } = new("crypto", "Crypto", "Test", [AssetClass.Crypto], ProviderUpdateMode.Streaming);

        /// <inheritdoc />
        public IMarketDataProvider Create(ProviderCreationContext context) =>
            Provider = new FakeProvider(Descriptor, context.Options) { HistoryFailure = failure };
    }

    /// <summary>
    /// A factory whose provider already has history, used to test sparkline seeding on the first apply.
    /// </summary>
    /// <param name="now">
    /// The current time used to build the history.
    /// </param>
    /// <param name="quoteOnSubscribe">
    /// The price the provider publishes right when an instrument is subscribed, or <see langword="null"/> for none.
    /// </param>
    private sealed class HistoryFactory(DateTimeOffset now, decimal? quoteOnSubscribe = null) : IMarketDataProviderFactory
    {
        /// <inheritdoc />
        public ProviderDescriptor Descriptor { get; } = new("crypto", "Crypto", "Test", [AssetClass.Crypto], ProviderUpdateMode.Streaming);

        /// <inheritdoc />
        public IMarketDataProvider Create(ProviderCreationContext context) =>
            new FakeProvider(Descriptor, context.Options)
            {
                History = [new PricePoint(now.AddMinutes(-2), 1m), new PricePoint(now.AddMinutes(-1), 2m)],
                QuoteOnSubscribe = quoteOnSubscribe,
            };
    }
}
