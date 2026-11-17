using FluxTick.App.Services;
using FluxTick.Core.Application;
using FluxTick.Core.Infrastructure;
using FluxTick.Providers.Eulerpool;
using Microsoft.Extensions.DependencyInjection;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests that the settings store is wired the way the app builds it: with the protector of the platform and the data sources
/// that tell which options are secret.
/// </summary>
public sealed class SettingsStoreWiringTests : IDisposable
{
    /// <summary>
    /// A key that must never appear in the file on Windows.
    /// </summary>
    private const string Key = "eu_wiring_test_key";

    /// <summary>
    /// The folder of the settings file of one test.
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"fluxtick-tests-{Guid.NewGuid():N}");

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies the store is the protecting one, with the real protector of the platform: DPAPI on Windows, none elsewhere.
    /// </summary>
    [Fact]
    public void AddSettingsStore_RegistersTheProtectingStoreAndThePlatformProtector()
    {
        using var provider = BuildServices();

        provider.GetRequiredService<ISettingsStore>().Should().BeOfType<ProtectedSettingsStore>();
        provider.GetRequiredService<ISecretProtector>().Should().BeOfType(
            OperatingSystem.IsWindows() ? typeof(DpapiSecretProtector) : typeof(NoSecretProtector));
    }

    /// <summary>
    /// Verifies a key saved through the store as the app builds it is protected in the file, comes back as typed, and that
    /// the options that are not secret stay readable.
    /// </summary>
    [Fact]
    public async Task SettingsStore_AsTheAppBuildsIt_ProtectsTheKeyInTheFile()
    {
        using var provider = BuildServices();
        var store = provider.GetRequiredService<ISettingsStore>();
        var settings = new AppSettings();
        settings.Providers[EulerpoolProviderFactory.ProviderId] = new ProviderSettings
        {
            Options = { [EulerpoolProviderFactory.ApiKeyKey] = Key, [EulerpoolProviderFactory.BaseUrlKey] = "https://api.example.test" },
        };

        await store.SaveAsync(settings);

        var file = await File.ReadAllTextAsync(Path.Combine(_directory, "settings.json"));
        file.Should().Contain("https://api.example.test");
        if (OperatingSystem.IsWindows())
        {
            file.Should().NotContain(Key).And.Contain(ProtectedSettingsStore.Prefix);
        }

        var loaded = await store.LoadAsync();
        loaded!.Providers[EulerpoolProviderFactory.ProviderId].Options[EulerpoolProviderFactory.ApiKeyKey].Should().Be(Key);
    }

    /// <summary>
    /// Builds the services the way the app does for the settings store, with a data source that has a secret option.
    /// </summary>
    /// <returns>
    /// The service provider.
    /// </returns>
    private ServiceProvider BuildServices() => new ServiceCollection()
        .AddLogging()
        .AddMarketDataProvider<EulerpoolProviderFactory>()
        .AddSettingsStore(Path.Combine(_directory, "settings.json"))
        .BuildServiceProvider();
}
