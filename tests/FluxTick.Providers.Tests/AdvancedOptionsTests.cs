using FluxTick.Abstractions;
using FluxTick.Providers.Binance;
using FluxTick.Providers.Bybit;
using FluxTick.Providers.Coinbase;
using FluxTick.Providers.Eulerpool;
using FluxTick.Providers.Kraken;
using FluxTick.Providers.Okx;
using FluxTick.Providers.Sina;
using FluxTick.Providers.Tencent;
using FluxTick.Providers.Yahoo;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Verifies which options the sources declare as advanced, which the settings page keeps in a collapsed section.
/// </summary>
public class AdvancedOptionsTests
{
    /// <summary>
    /// Verifies endpoint addresses and the quiet-stream interval are advanced.
    /// </summary>
    /// <param name="factory">
    /// The factory of the source.
    /// </param>
    [Theory]
    [MemberData(nameof(Factories))]
    public void Descriptor_EndpointAndQuietIntervalOptions_AreAdvanced(IMarketDataProviderFactory factory)
    {
        var tuning = factory.Descriptor.OptionDefinitions
            .Where(o => o.Key.EndsWith("Url", StringComparison.Ordinal) || o.Key == ProviderOptionDefinition.QuietPollIntervalKey);

        tuning.Should().OnlyContain(o => o.Advanced);
    }

    /// <summary>
    /// Verifies the options a user must fill in, such as a key, stay in view.
    /// </summary>
    /// <param name="factory">
    /// The factory of the source.
    /// </param>
    [Theory]
    [MemberData(nameof(Factories))]
    public void Descriptor_RequiredOptions_AreNotAdvanced(IMarketDataProviderFactory factory)
    {
        factory.Descriptor.OptionDefinitions.Where(o => o.Required).Should().OnlyContain(o => !o.Advanced);
    }

    /// <summary>
    /// Verifies an option is not advanced unless its declaration says so.
    /// </summary>
    [Fact]
    public void ProviderOptionDefinition_ByDefault_IsNotAdvanced()
    {
        var option = new ProviderOptionDefinition("Key", "Label", ProviderOptionType.String, string.Empty);

        option.Advanced.Should().BeFalse();
    }

    /// <summary>
    /// Gets the factories of the sources with options.
    /// </summary>
    public static TheoryData<IMarketDataProviderFactory> Factories =>
    [
        new BinanceProviderFactory(),
        new YahooProviderFactory(),
        new TencentProviderFactory(),
        new SinaProviderFactory(),
        new OkxProviderFactory(),
        new CoinbaseProviderFactory(),
        new KrakenProviderFactory(),
        new BybitProviderFactory(),
        new EulerpoolProviderFactory(),
    ];
}
