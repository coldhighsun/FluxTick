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

    /// <summary>
    /// Verifies each theme mode maps to its variant, with automatic following the system.
    /// </summary>
    [Fact]
    public void ThemeMode_MapsToVariant()
    {
        ThemeModeResolver.ToVariant(ThemeMode.Auto).Should().Be(Avalonia.Styling.ThemeVariant.Default);
        ThemeModeResolver.ToVariant(ThemeMode.Light).Should().Be(Avalonia.Styling.ThemeVariant.Light);
        ThemeModeResolver.ToVariant(ThemeMode.Dark).Should().Be(Avalonia.Styling.ThemeVariant.Dark);
    }

    /// <summary>
    /// Verifies a chosen family comes first and a Chinese font of the system follows it, so no character is left to a
    /// font the system picks.
    /// </summary>
    [Theory]
    [InlineData("Arial", true, false, "Arial, Microsoft YaHei UI")]
    [InlineData("Arial", false, true, "Arial, PingFang SC")]
    [InlineData("Arial", false, false, "Arial, Noto Sans CJK SC")]
    public void FontResolver_ChosenFamily_IsFollowedByAChineseFont(string family, bool isWindows, bool isMacOs, string expected)
    {
        FontResolver.Resolve(family, isWindows, isMacOs).Should().Be(expected);
    }

    /// <summary>
    /// Verifies no family, or only blanks, gives the usual font of the system with its Chinese font.
    /// </summary>
    [Theory]
    [InlineData("", true, false, "Segoe UI, Microsoft YaHei UI")]
    [InlineData(null, false, true, "Helvetica Neue, PingFang SC")]
    [InlineData("  ", false, false, "Noto Sans, Noto Sans CJK SC")]
    public void FontResolver_NoFamily_IsTheFontOfTheSystem(string? family, bool isWindows, bool isMacOs, string expected)
    {
        FontResolver.Resolve(family, isWindows, isMacOs).Should().Be(expected);
    }

    /// <summary>
    /// Verifies a comma in a name does not split it into two families.
    /// </summary>
    [Fact]
    public void FontResolver_NameWithAComma_StaysOneFamily()
    {
        FontResolver.Resolve("Foo, Bar", true, false).Should().Be("Foo Bar, Microsoft YaHei UI");
    }

    /// <summary>
    /// Verifies the formatting helpers of money and quantities.
    /// </summary>
    [Fact]
    public void Format_MoneyAndQuantity_UsesSignsAndSeparators()
    {
        PriceFormatter.FormatSignedMoney(1234.5m).Should().Be("+1,234.50");
        PriceFormatter.FormatSignedMoney(-5m).Should().Be("-5.00");
        PriceFormatter.FormatSignedMoney(0m).Should().Be("0.00");
        PriceFormatter.FormatMoney(1234.567m).Should().Be("1,234.57");
        PriceFormatter.FormatQuantity(1000m).Should().Be("1,000");
        PriceFormatter.FormatQuantity(0.5m).Should().Be("0.5");
    }
}
