using FluxTick.App.Views;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for <see cref="AssetFocusRules"/>.
/// </summary>
public class AssetFocusRulesTests
{
    /// <summary>
    /// Verifies a taller window gets a larger price, up to the ceiling.
    /// </summary>
    [Fact]
    public void PriceFontSize_TallerWindow_IsLarger()
    {
        AssetFocusRules.PriceFontSize(1080).Should().BeGreaterThan(AssetFocusRules.PriceFontSize(720));
        AssetFocusRules.PriceFontSize(100000).Should().Be(200);
    }

    /// <summary>
    /// Verifies a window shorter than the floor is sized as the floor, and an unknown height does not break the sizes.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(double.NaN)]
    public void FontSizes_TooShortOrUnknownHeight_UseTheFloor(double height)
    {
        AssetFocusRules.PriceFontSize(height).Should().Be(AssetFocusRules.PriceFontSize(AssetFocusRules.MinHeight));
        AssetFocusRules.DetailFontSize(height).Should().Be(AssetFocusRules.DetailFontSize(AssetFocusRules.MinHeight));
    }

    /// <summary>
    /// Verifies the price is the largest text and the figures the smallest.
    /// </summary>
    [Theory]
    [InlineData(600)]
    [InlineData(1080)]
    [InlineData(2160)]
    public void FontSizes_AnyHeight_AreOrderedByImportance(double height)
    {
        AssetFocusRules.PriceFontSize(height).Should().BeGreaterThan(AssetFocusRules.ChangeFontSize(height));
        AssetFocusRules.ChangeFontSize(height).Should().BeGreaterThan(AssetFocusRules.DetailFontSize(height));
        AssetFocusRules.NameFontSize(height).Should().BeGreaterThan(AssetFocusRules.DetailFontSize(height));
    }
}
