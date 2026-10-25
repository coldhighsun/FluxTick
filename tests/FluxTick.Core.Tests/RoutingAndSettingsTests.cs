using FluxTick.Abstractions;
using FluxTick.Core.Application;
using FluxTick.Core.Infrastructure;
using FluxTick.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for the provider registry, the router and settings persistence.
/// </summary>
public sealed class RoutingAndSettingsTests : IDisposable
{
    /// <summary>
    /// A scratch directory unique to this test instance.
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"fluxtick-tests-{Guid.NewGuid():N}");

    /// <summary>
    /// Removes the scratch directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies two factories with the same id are rejected.
    /// </summary>
    [Fact]
    public void Constructor_WithDuplicateProviderIds_Throws()
    {
        var act = () => new ProviderRegistry(
        [
            new FakeProviderFactory("a", [AssetClass.Crypto]),
            new FakeProviderFactory("a", [AssetClass.Equity]),
        ]);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Verifies lookups find known providers and report unknown ones.
    /// </summary>
    [Fact]
    public void TryGetFactory_KnownAndUnknown_ReportsCorrectly()
    {
        var registry = new ProviderRegistry([new FakeProviderFactory("a", [AssetClass.Crypto])]);

        registry.TryGetFactory("a", out _).Should().BeTrue();
        registry.TryGetFactory("zzz", out _).Should().BeFalse();
        registry.GetDescriptor("zzz").Should().BeNull();
    }

    /// <summary>
    /// Verifies disabled providers and providers lacking the asset class are not offered.
    /// </summary>
    [Fact]
    public void GetProviders_FiltersByClassAndEnabledState()
    {
        var registry = new ProviderRegistry(
        [
            new FakeProviderFactory("crypto", [AssetClass.Crypto]),
            new FakeProviderFactory("stocks", [AssetClass.Equity]),
            new FakeProviderFactory("off", [AssetClass.Crypto], enabledByDefault: false),
        ]);
        var router = new ProviderRouter(registry);

        var providers = router.GetProviders(AssetClass.Crypto, new AppSettings());

        providers.Select(p => p.Id).Should().Equal("crypto");
    }

    /// <summary>
    /// Verifies the user's ranking wins and unranked providers follow in registration order.
    /// </summary>
    [Fact]
    public void GetProviders_WithRouting_PutsPreferredFirst()
    {
        var registry = new ProviderRegistry(
        [
            new FakeProviderFactory("a", [AssetClass.Crypto]),
            new FakeProviderFactory("b", [AssetClass.Crypto]),
            new FakeProviderFactory("c", [AssetClass.Crypto]),
        ]);
        var router = new ProviderRouter(registry);
        var settings = new AppSettings { Routing = { [AssetClass.Crypto] = ["c", "missing", "a"] } };

        var providers = router.GetProviders(AssetClass.Crypto, settings);

        providers.Select(p => p.Id).Should().Equal("c", "a", "b");
        router.GetPreferred(AssetClass.Crypto, settings)!.Id.Should().Be("c");
    }

    /// <summary>
    /// Verifies no preferred provider exists when none is enabled for the class.
    /// </summary>
    [Fact]
    public void GetPreferred_WithoutUsableProvider_ReturnsNull()
    {
        var registry = new ProviderRegistry([new FakeProviderFactory("a", [AssetClass.Crypto])]);
        var router = new ProviderRouter(registry);

        router.GetPreferred(AssetClass.Forex, new AppSettings()).Should().BeNull();
    }

    /// <summary>
    /// Verifies the explicit setting overrides the descriptor default.
    /// </summary>
    [Fact]
    public void IsProviderEnabled_ExplicitSetting_OverridesDefault()
    {
        var factory = new FakeProviderFactory("a", [AssetClass.Crypto], enabledByDefault: false);
        var settings = new AppSettings();

        settings.IsProviderEnabled(factory.Descriptor).Should().BeFalse();
        settings.Providers["a"] = new ProviderSettings { Enabled = true };
        settings.IsProviderEnabled(factory.Descriptor).Should().BeTrue();
    }

    /// <summary>
    /// Verifies settings survive a save/load round trip, including enum-keyed routing and option keys.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsSettings()
    {
        var store = new JsonSettingsStore(Path.Combine(_directory, "settings.json"), NullLogger<JsonSettingsStore>.Instance);
        var settings = new AppSettings();
        settings.Watchlist.Add(new WatchlistEntry { ProviderId = "a", Symbol = "BTC", DisplayName = "Bitcoin", AssetClass = AssetClass.Crypto });
        settings.Providers["a"] = new ProviderSettings { Enabled = false, Options = { ["PollInterval"] = "00:00:09" } };
        settings.Routing[AssetClass.Equity] = ["a", "b"];
        settings.Appearance.ColorConvention = ColorConvention.RedUp;
        settings.Appearance.ThemeMode = ThemeMode.Dark;
        settings.Appearance.Language = LanguageMode.ZhCN;
        settings.Appearance.RollPriceOnUpdate = true;
        settings.Appearance.AutoHideAtEdge = false;
        settings.Appearance.Layout = WatchlistLayout.Cards;
        settings.Appearance.Grouping = CardGrouping.Custom;
        settings.Appearance.CollapsedTypes.Add(AssetClass.Forex);
        settings.Groups.Add(new WatchGroup { Id = "g1", Name = "Core", Collapsed = true });
        settings.Watchlist[0].GroupId = "g1";

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        loaded.Should().NotBeNull();
        loaded!.Watchlist.Should().ContainSingle().Which.Symbol.Should().Be("BTC");
        loaded.Providers["a"].Enabled.Should().BeFalse();
        loaded.Providers["a"].Options["PollInterval"].Should().Be("00:00:09");
        loaded.Routing[AssetClass.Equity].Should().Equal("a", "b");
        loaded.Appearance.ColorConvention.Should().Be(ColorConvention.RedUp);
        loaded.Appearance.ThemeMode.Should().Be(ThemeMode.Dark);
        loaded.Appearance.Language.Should().Be(LanguageMode.ZhCN);
        loaded.Appearance.RollPriceOnUpdate.Should().BeTrue();
        loaded.Appearance.AutoHideAtEdge.Should().BeFalse();
        loaded.Appearance.Layout.Should().Be(WatchlistLayout.Cards);
        loaded.Appearance.Grouping.Should().Be(CardGrouping.Custom);
        loaded.Appearance.CollapsedTypes.Should().Equal(AssetClass.Forex);
        loaded.Groups.Should().ContainSingle().Which.Should().BeEquivalentTo(new WatchGroup { Id = "g1", Name = "Core", Collapsed = true });
        loaded.Watchlist[0].GroupId.Should().Be("g1");
    }

    /// <summary>
    /// Verifies the list layout without grouping is used, including for a settings file written before cards existed.
    /// </summary>
    [Fact]
    public async Task LoadAsync_WithoutLayoutKeys_DefaultsToListWithoutGroups()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, """{ "watchlist": [ { "providerId": "a", "symbol": "BTC" } ] }""");
        var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        var loaded = await store.LoadAsync();

        loaded!.Appearance.Layout.Should().Be(WatchlistLayout.List);
        loaded.Appearance.Grouping.Should().Be(CardGrouping.None);
        loaded.Appearance.CollapsedTypes.Should().BeEmpty();
        loaded.Groups.Should().BeEmpty();
        loaded.Watchlist.Should().ContainSingle().Which.GroupId.Should().BeNull();
    }

    /// <summary>
    /// Verifies a settings file written before connecting on demand existed connects on demand.
    /// </summary>
    [Fact]
    public async Task LoadAsync_WithoutConnectOnDemandKey_DefaultsToOn()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, """{ "general": { "startMinimized": true } }""");
        var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        var loaded = await store.LoadAsync();

        loaded!.General.ConnectOnDemand.Should().BeTrue();
        loaded.General.StartMinimized.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the rolling of the price defaults to off, including for a settings file written before the option existed.
    /// </summary>
    [Fact]
    public async Task LoadAsync_WithoutRollKey_DefaultsToRollOff()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, """{ "appearance": { "opacity": 0.8 } }""");
        var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        var loaded = await store.LoadAsync();

        new AppSettings().Appearance.RollPriceOnUpdate.Should().BeFalse();
        loaded!.Appearance.Opacity.Should().Be(0.8);
        loaded.Appearance.RollPriceOnUpdate.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the edge auto-hide defaults to on, including for a settings file written before the option existed.
    /// </summary>
    [Fact]
    public async Task LoadAsync_WithoutAutoHideKey_DefaultsToAutoHideOn()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, """{ "appearance": { "opacity": 0.8 } }""");
        var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        var loaded = await store.LoadAsync();

        new AppSettings().Appearance.AutoHideAtEdge.Should().BeTrue();
        loaded!.Appearance.AutoHideAtEdge.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a settings file written before the theme option existed reads as automatic.
    /// </summary>
    [Fact]
    public async Task LoadAsync_WithoutThemeModeKey_DefaultsToAuto()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, """{ "appearance": { "opacity": 0.8 } }""");
        var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        var loaded = await store.LoadAsync();

        loaded!.Appearance.Opacity.Should().Be(0.8);
        loaded.Appearance.ThemeMode.Should().Be(ThemeMode.Auto);
    }

    /// <summary>
    /// Verifies a missing file loads as <see langword="null"/>.
    /// </summary>
    [Fact]
    public async Task LoadAsync_WithoutFile_ReturnsNull()
    {
        var store = new JsonSettingsStore(Path.Combine(_directory, "settings.json"), NullLogger<JsonSettingsStore>.Instance);

        (await store.LoadAsync()).Should().BeNull();
    }

    /// <summary>
    /// Verifies a corrupt file is set aside and defaults are used.
    /// </summary>
    [Fact]
    public async Task LoadAsync_WithCorruptFile_MovesItAsideAndReturnsNull()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, "{ not json");
        var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        var loaded = await store.LoadAsync();

        loaded.Should().BeNull();
        File.Exists(path).Should().BeFalse();
        File.Exists($"{path}.bad").Should().BeTrue();
    }

    /// <summary>
    /// Verifies updates are applied to a copy, published and persisted.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_AppliesChangeRaisesEventAndPersists()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);
        var manager = new SettingsManager(store, NullLogger<SettingsManager>.Instance);
        await manager.LoadAsync(new AppSettings());
        var before = manager.Current;
        var raised = 0;
        manager.Changed += (_, _) => raised++;

        await manager.UpdateAsync(s => s.Appearance.Opacity = 0.5);

        manager.Current.Should().NotBeSameAs(before);
        before.Appearance.Opacity.Should().NotBe(0.5);
        manager.Current.Appearance.Opacity.Should().Be(0.5);
        raised.Should().Be(1);
        (await store.LoadAsync())!.Appearance.Opacity.Should().Be(0.5);
    }

    /// <summary>
    /// Verifies settings without any group get a default group that holds every instrument.
    /// </summary>
    [Fact]
    public void Normalize_WithoutGroups_CreatesDefaultGroupForAllInstruments()
    {
        var settings = new AppSettings();
        settings.Watchlist.Add(new WatchlistEntry { ProviderId = "a", Symbol = "BTC" });
        settings.Watchlist.Add(new WatchlistEntry { ProviderId = "a", Symbol = "ETH" });

        var changed = settings.Normalize();

        changed.Should().BeTrue();
        var group = settings.Groups.Should().ContainSingle().Subject;
        group.Name.Should().Be(AppSettings.DefaultGroupName);
        settings.Watchlist.Should().OnlyContain(e => e.GroupId == group.Id);
    }

    /// <summary>
    /// Verifies instruments without a group, or with a group that is gone, join the first group and others stay.
    /// </summary>
    [Fact]
    public void Normalize_LooseInstruments_JoinFirstGroup()
    {
        var settings = new AppSettings();
        settings.Groups.AddRange([new WatchGroup { Id = "a", Name = "A" }, new WatchGroup { Id = "b", Name = "B" }]);
        settings.Watchlist.AddRange(
        [
            new WatchlistEntry { Symbol = "X", GroupId = null },
            new WatchlistEntry { Symbol = "Y", GroupId = "gone" },
            new WatchlistEntry { Symbol = "Z", GroupId = "b" },
        ]);

        var changed = settings.Normalize();

        changed.Should().BeTrue();
        settings.Watchlist.Select(e => e.GroupId).Should().Equal("a", "a", "b");
        settings.Groups.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies an instrument keeps at most one alert per condition, the first one, and the other conditions and
    /// instruments are not touched.
    /// </summary>
    [Fact]
    public void Normalize_RepeatedAlertCondition_KeepsTheFirst()
    {
        var settings = new AppSettings();
        settings.Watchlist.AddRange(
        [
            new WatchlistEntry { ProviderId = "p", Symbol = "X" },
            new WatchlistEntry { ProviderId = "q", Symbol = "X" },
        ]);
        settings.Alerts.AddRange(
        [
            new AlertRule { Id = "1", ProviderId = "p", Symbol = "X", Condition = AlertCondition.PriceAbove, Threshold = 10 },
            new AlertRule { Id = "2", ProviderId = "p", Symbol = "X", Condition = AlertCondition.PriceAbove, Threshold = 20 },
            new AlertRule { Id = "3", ProviderId = "p", Symbol = "X", Condition = AlertCondition.PriceBelow, Threshold = 5 },
            new AlertRule { Id = "4", ProviderId = "q", Symbol = "X", Condition = AlertCondition.PriceAbove, Threshold = 30 },
        ]);

        var changed = settings.Normalize();

        changed.Should().BeTrue();
        settings.Alerts.Select(a => a.Id).Should().Equal("1", "3", "4");
    }

    /// <summary>
    /// Verifies the cards are of the usual size by default, also for a settings file from before there was a size, and a
    /// chosen size survives a copy of the settings.
    /// </summary>
    [Fact]
    public void CardSize_DefaultsToMediumAndSurvivesCloning()
    {
        new AppSettings().Appearance.CardSize.Should().Be(CardSize.Medium);

        var legacy = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"appearance\":{\"opacity\":0.5}}", AppSettings.JsonOptions)!;
        legacy.Appearance.CardSize.Should().Be(CardSize.Medium);

        var settings = new AppSettings();
        settings.Appearance.CardSize = CardSize.Large;
        settings.Clone().Appearance.CardSize.Should().Be(CardSize.Large);
    }

    /// <summary>
    /// Verifies settings that are already consistent are left alone.
    /// </summary>
    [Fact]
    public void Normalize_ConsistentSettings_ReportsNoChange()
    {
        var settings = new AppSettings();
        settings.Groups.Add(new WatchGroup { Id = "a", Name = "A" });
        settings.Watchlist.Add(new WatchlistEntry { Symbol = "X", GroupId = "a" });

        settings.Normalize().Should().BeFalse();
    }

    /// <summary>
    /// Verifies loading settings from an older file fixes the groups and saves the result.
    /// </summary>
    [Fact]
    public async Task LoadAsync_OldFileWithoutGroups_CreatesDefaultGroupAndSaves()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);
        var old = new AppSettings();
        old.Watchlist.Add(new WatchlistEntry { ProviderId = "a", Symbol = "BTC" });
        await store.SaveAsync(old);
        var manager = new SettingsManager(store, NullLogger<SettingsManager>.Instance);

        await manager.LoadAsync(new AppSettings());

        var group = manager.Current.Groups.Should().ContainSingle().Subject;
        manager.Current.Watchlist.Single().GroupId.Should().Be(group.Id);
        var saved = await store.LoadAsync();
        saved!.Groups.Should().ContainSingle().Which.Id.Should().Be(group.Id);
    }

    /// <summary>
    /// Verifies an instrument added without a group joins the first group when the update is published.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_AddedInstrumentWithoutGroup_JoinsFirstGroup()
    {
        var store = new JsonSettingsStore(Path.Combine(_directory, "settings.json"), NullLogger<JsonSettingsStore>.Instance);
        var manager = new SettingsManager(store, NullLogger<SettingsManager>.Instance);
        await manager.LoadAsync(new AppSettings());
        var first = manager.Current.Groups[0].Id;

        await manager.UpdateAsync(s => s.Watchlist.Add(new WatchlistEntry { ProviderId = "a", Symbol = "BTC" }));

        manager.Current.Watchlist.Single().GroupId.Should().Be(first);
    }

    /// <summary>
    /// Verifies an update that changes nothing is neither published nor saved, and one that changes something is.
    /// </summary>
    [Fact]
    public async Task TryUpdateAsync_NothingChanged_IsNeitherPublishedNorSaved()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new CountingStore(new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance));
        var manager = new SettingsManager(store, NullLogger<SettingsManager>.Instance);
        await manager.LoadAsync(new AppSettings());
        var savedBefore = store.Saves;
        var published = 0;
        manager.Changed += (_, _) => published++;

        var unchanged = await manager.TryUpdateAsync(_ => false);

        unchanged.Should().BeFalse();
        published.Should().Be(0);
        store.Saves.Should().Be(savedBefore, "an update that finds nothing to do must not write the file");

        var changed = await manager.TryUpdateAsync(s =>
        {
            s.General.StartMinimized = true;
            return true;
        });

        changed.Should().BeTrue();
        published.Should().Be(1);
        store.Saves.Should().Be(savedBefore + 1);
        manager.Current.General.StartMinimized.Should().BeTrue();
        (await store.LoadAsync())!.General.StartMinimized.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the collapsed state of a data source or asset class that no instrument uses any more is dropped, and
    /// that the state of the ones still in use is kept.
    /// </summary>
    [Fact]
    public void Normalize_CollapsedStateOfUnusedProviderAndType_IsDropped()
    {
        var settings = new AppSettings();
        settings.Groups.Add(new WatchGroup { Id = "g", Name = "G" });
        settings.Watchlist.Add(new WatchlistEntry { ProviderId = "a", Symbol = "BTC", AssetClass = AssetClass.Crypto, GroupId = "g" });
        settings.Appearance.CollapsedProviders.AddRange(["a", "gone"]);
        settings.Appearance.CollapsedTypes.AddRange([AssetClass.Crypto, AssetClass.Equity]);

        var changed = settings.Normalize();

        changed.Should().BeTrue();
        settings.Appearance.CollapsedProviders.Should().Equal("a");
        settings.Appearance.CollapsedTypes.Should().Equal(AssetClass.Crypto);
        settings.Normalize().Should().BeFalse("nothing is left to clean up");
    }

    /// <summary>
    /// Verifies removing the last instrument of a data source forgets that its section was collapsed.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_RemovingLastInstrumentOfProvider_ForgetsCollapsedProvider()
    {
        var store = new JsonSettingsStore(Path.Combine(_directory, "settings.json"), NullLogger<JsonSettingsStore>.Instance);
        var manager = new SettingsManager(store, NullLogger<SettingsManager>.Instance);
        await manager.LoadAsync(new AppSettings());
        await manager.UpdateAsync(s =>
        {
            s.Watchlist.Add(new WatchlistEntry { ProviderId = "a", Symbol = "BTC" });
            s.Appearance.CollapsedProviders.Add("a");
        });
        manager.Current.Appearance.CollapsedProviders.Should().Equal("a");

        await manager.UpdateAsync(s => s.Watchlist.Clear());

        manager.Current.Appearance.CollapsedProviders.Should().BeEmpty();
    }

    /// <summary>
    /// A settings store that counts how often it is asked to save.
    /// </summary>
    /// <param name="inner">
    /// The store that does the work.
    /// </param>
    private sealed class CountingStore(ISettingsStore inner) : ISettingsStore
    {
        /// <summary>
        /// The number of saves so far.
        /// </summary>
        private int _saves;

        /// <summary>
        /// Gets the number of saves so far.
        /// </summary>
        public int Saves => Volatile.Read(ref _saves);

        /// <inheritdoc />
        public Task<AppSettings?> LoadAsync(CancellationToken cancellationToken = default) => inner.LoadAsync(cancellationToken);

        /// <inheritdoc />
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _saves);
            return inner.SaveAsync(settings, cancellationToken);
        }
    }
}
