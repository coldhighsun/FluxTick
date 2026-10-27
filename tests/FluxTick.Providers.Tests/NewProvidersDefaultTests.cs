using FluxTick.Abstractions;
using FluxTick.Providers.Bybit;
using FluxTick.Providers.Coinbase;
using FluxTick.Providers.Kraken;
using FluxTick.Providers.Okx;
using FluxTick.Providers.Sina;
using FluxTick.Providers.Tencent;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Verifies the optional data sources stay off until the user enables them, so a fresh install behaves as before.
/// </summary>
public class NewProvidersDefaultTests
{
    /// <summary>
    /// Verifies the descriptors of the optional sources are off by default.
    /// </summary>
    [Theory]
    [MemberData(nameof(Factories))]
    public void Descriptor_IsDisabledByDefault(IMarketDataProviderFactory factory)
    {
        factory.Descriptor.EnabledByDefault.Should().BeFalse();
    }

    /// <summary>
    /// Gets the factories of the optional sources.
    /// </summary>
    public static TheoryData<IMarketDataProviderFactory> Factories =>
    [
        new TencentProviderFactory(),
        new SinaProviderFactory(),
        new OkxProviderFactory(),
        new CoinbaseProviderFactory(),
        new KrakenProviderFactory(),
        new BybitProviderFactory(),
    ];
}
