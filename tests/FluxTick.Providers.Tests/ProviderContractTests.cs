using FluxTick.Abstractions;
using FluxTick.Providers.Binance;
using FluxTick.Providers.Bybit;
using FluxTick.Providers.Coinbase;
using FluxTick.Providers.Kraken;
using FluxTick.Providers.Okx;
using FluxTick.Providers.Simulated;
using FluxTick.Providers.Tencent;
using FluxTick.Providers.Yahoo;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Reusable checks every provider factory must pass. Derive one concrete class per data source;
/// a new data source gets basic compliance coverage by adding a two-line subclass.
/// </summary>
/// <typeparam name="TFactory">
/// The factory under test.
/// </typeparam>
public abstract class ProviderContractTests<TFactory>
    where TFactory : IMarketDataProviderFactory, new()
{
    /// <summary>
    /// Creates a provider with default options.
    /// </summary>
    /// <returns>
    /// The provider.
    /// </returns>
    private static IMarketDataProvider CreateProvider()
    {
        var factory = new TFactory();
        return factory.Create(new ProviderCreationContext(
            new ProviderOptions(factory.Descriptor),
            NullLoggerFactory.Instance,
            TimeProvider.System));
    }

    /// <summary>
    /// Verifies the descriptor carries everything the UI and router rely on.
    /// </summary>
    [Fact]
    public void Descriptor_IsWellFormed()
    {
        var descriptor = new TFactory().Descriptor;

        descriptor.Id.Should().MatchRegex("^[a-z0-9-]+$");
        descriptor.DisplayName.Should().NotBeNullOrWhiteSpace();
        descriptor.Description.Should().NotBeNullOrWhiteSpace();
        descriptor.SupportedAssetClasses.Should().NotBeEmpty();
        descriptor.OptionDefinitions.Select(o => o.Key).Should().OnlyHaveUniqueItems();
        foreach (var option in descriptor.OptionDefinitions)
        {
            option.Validate(option.DefaultValue).Should().BeNull($"the default of '{option.Key}' must be valid");
        }
    }

    /// <summary>
    /// Verifies garbage option values never prevent a provider from being created.
    /// </summary>
    [Fact]
    public async Task Create_WithGarbageOptions_FallsBackToDefaults()
    {
        var factory = new TFactory();
        var garbage = factory.Descriptor.OptionDefinitions.ToDictionary(o => o.Key, _ => "@@garbage@@");

        await using var provider = factory.Create(new ProviderCreationContext(
            new ProviderOptions(factory.Descriptor, garbage),
            NullLoggerFactory.Instance,
            TimeProvider.System));

        provider.Descriptor.Id.Should().Be(factory.Descriptor.Id);
    }

    /// <summary>
    /// Verifies a new provider is idle and stays idle without subscriptions, so no network I/O happens.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_Empty_StaysIdle()
    {
        await using var provider = CreateProvider();

        await provider.SetSubscriptionsAsync([]);

        provider.Status.State.Should().Be(ProviderConnectionState.Idle);
    }

    /// <summary>
    /// Verifies disposal promptly ends the quote stream for the reader.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_EndsQuoteStream()
    {
        var provider = CreateProvider();
        var reader = Task.Run(async () =>
        {
            await foreach (var _ in provider.ReadQuotesAsync())
            {
            }
        });

        await provider.DisposeAsync();

        await reader.WaitAsync(TimeSpan.FromSeconds(5));
    }
}

/// <summary>
/// Contract tests for the Simulated provider.
/// </summary>
public sealed class SimulatedProviderContractTests : ProviderContractTests<SimulatedProviderFactory>;

/// <summary>
/// Contract tests for the Binance provider.
/// </summary>
public sealed class BinanceProviderContractTests : ProviderContractTests<BinanceProviderFactory>;

/// <summary>
/// Contract tests for the Yahoo provider.
/// </summary>
public sealed class YahooProviderContractTests : ProviderContractTests<YahooProviderFactory>;

/// <summary>
/// Contract tests for the Tencent provider.
/// </summary>
public sealed class TencentProviderContractTests : ProviderContractTests<TencentProviderFactory>;

/// <summary>
/// Contract tests for the OKX provider.
/// </summary>
public sealed class OkxProviderContractTests : ProviderContractTests<OkxProviderFactory>;

/// <summary>
/// Contract tests for the Coinbase provider.
/// </summary>
public sealed class CoinbaseProviderContractTests : ProviderContractTests<CoinbaseProviderFactory>;

/// <summary>
/// Contract tests for the Kraken provider.
/// </summary>
public sealed class KrakenProviderContractTests : ProviderContractTests<KrakenProviderFactory>;

/// <summary>
/// Contract tests for the Bybit provider.
/// </summary>
public sealed class BybitProviderContractTests : ProviderContractTests<BybitProviderFactory>;
