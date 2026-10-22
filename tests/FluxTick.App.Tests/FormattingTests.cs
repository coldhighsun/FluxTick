using System.Globalization;
using FluxTick.Abstractions;
using FluxTick.App.Services;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the pure display helpers.
/// </summary>
public class FormattingTests
{
    /// <summary>
    /// Verifies the price precision adapts to magnitude and asset class.
    /// </summary>
    [Theory]
    [InlineData(82243.664, AssetClass.Crypto, "82,243.66")]
    [InlineData(343.99, AssetClass.Equity, "343.99")]
    [InlineData(150.123, AssetClass.Forex, "150.123")]
    [InlineData(1.1238, AssetClass.Forex, "1.1238")]
    [InlineData(1.5, AssetClass.Equity, "1.50")]
    [InlineData(0.25, AssetClass.Crypto, "0.25000")]
    [InlineData(0.00001234, AssetClass.Crypto, "0.00001234")]
    public void FormatPrice_ByMagnitudeAndClass_UsesSensiblePrecision(double price, AssetClass assetClass, string expected)
    {
        PriceFormatter.FormatPrice((decimal)price, assetClass).Should().Be(expected);
    }

    /// <summary>
    /// Verifies percentages carry an explicit sign and unknown values show a placeholder.
    /// </summary>
    [Theory]
    [InlineData(1.234, "+1.23%")]
    [InlineData(-0.456, "-0.46%")]
    [InlineData(0.0, "0.00%")]
    [InlineData(null, "—")]
    public void FormatPercent_FormatsSignAndPlaceholder(double? percent, string expected)
    {
        PriceFormatter.FormatPercent(percent is null ? null : (decimal)percent.Value).Should().Be(expected);
    }

    /// <summary>
    /// Verifies the color convention follows the UI language only when set to automatic.
    /// </summary>
    [Theory]
    [InlineData(ColorConvention.Auto, "zh-CN", true)]
    [InlineData(ColorConvention.Auto, "zh-TW", true)]
    [InlineData(ColorConvention.Auto, "en-US", false)]
    [InlineData(ColorConvention.GreenUp, "zh-CN", false)]
    [InlineData(ColorConvention.RedUp, "en-US", true)]
    public void IsRedUp_ByConventionAndCulture(ColorConvention convention, string culture, bool expected)
    {
        ColorConventionResolver.IsRedUp(convention, new CultureInfo(culture)).Should().Be(expected);
    }
}
