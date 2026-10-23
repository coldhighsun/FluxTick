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
        settings.Appearance.FlashOnUpdate = true;

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        loaded.Should().NotBeNull();
        loaded!.Watchlist.Should().ContainSingle().Which.Symbol.Should().Be("BTC");
        loaded.Providers["a"].Enabled.Should().BeFalse();
        loaded.Providers["a"].Options["PollInterval"].Should().Be("00:00:09");
        loaded.Routing[AssetClass.Equity].Should().Equal("a", "b");
        loaded.Appearance.ColorConvention.Should().Be(ColorConvention.RedUp);
        loaded.Appearance.FlashOnUpdate.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the flash defaults to off, including for a settings file written before the option existed.
    /// </summary>
    [Fact]
    public async Task LoadAsync_WithoutFlashKey_DefaultsToFlashOff()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, """{ "appearance": { "opacity": 0.8 } }""");
        var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        var loaded = await store.LoadAsync();

        new AppSettings().Appearance.FlashOnUpdate.Should().BeFalse();
        loaded!.Appearance.Opacity.Should().Be(0.8);
        loaded.Appearance.FlashOnUpdate.Should().BeFalse();
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
}
