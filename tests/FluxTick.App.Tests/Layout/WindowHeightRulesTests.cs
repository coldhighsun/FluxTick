using FluxTick.App.Views;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for <see cref="WindowHeightRules"/>.
/// </summary>
public class WindowHeightRulesTests
{
    /// <summary>
    /// Verifies the list layout keeps its fixed smallest height whatever the card size.
    /// </summary>
    [Theory]
    [InlineData(CardSize.Small)]
    [InlineData(CardSize.Large)]
    public void MinHeight_ListLayout_IsFixed(CardSize size)
    {
        WindowHeightRules.MinHeight(WatchlistLayout.List, size, grouped: true, chrome: 100).Should().Be(WindowHeightRules.ListMinHeight);
    }

    /// <summary>
    /// Verifies the width of a card follows its size.
    /// </summary>
    [Theory]
    [InlineData(CardSize.Small, 128)]
    [InlineData(CardSize.Medium, 176)]
    [InlineData(CardSize.Large, CardLayout.LargeCardWidth)]
    public void CardWidth_Size_ReturnsCardLayoutWidth(CardSize size, double expected)
    {
        WindowHeightRules.CardWidth(size).Should().Be(expected);
    }

    /// <summary>
    /// Verifies the list layout needs the height of all rows and what is around them.
    /// </summary>
    [Fact]
    public void ListContentMinHeight_FitsAllRows()
    {
        WindowHeightRules.ListContentMinHeight(content: 400, chrome: 80, limit: 1000).Should().Be(480);
    }

    /// <summary>
    /// Verifies a few rows never take the window below the smallest height of the list layout.
    /// </summary>
    [Fact]
    public void ListContentMinHeight_NeverBelowListMinimum()
    {
        WindowHeightRules.ListContentMinHeight(content: 40, chrome: 80, limit: 1000).Should().Be(WindowHeightRules.ListMinHeight);
    }

    /// <summary>
    /// Verifies many rows stop at the height of the screen, where they scroll.
    /// </summary>
    [Fact]
    public void ListContentMinHeight_CappedByScreen()
    {
        WindowHeightRules.ListContentMinHeight(content: 5000, chrome: 80, limit: 900).Should().Be(900);
    }

    /// <summary>
    /// Verifies the card layout needs the card, its margin, the padding of the list and what is around the list.
    /// </summary>
    [Fact]
    public void MinHeight_CardLayout_FitsOneCard()
    {
        // 80 around the list + 4 + 6 padding + 2 * 3 margin + 226 for the card.
        WindowHeightRules.MinHeight(WatchlistLayout.Cards, CardSize.Large, grouped: false, chrome: 80).Should().Be(322);
        WindowHeightRules.MinHeight(WatchlistLayout.Cards, CardSize.Medium, grouped: false, chrome: 80).Should().Be(220);
    }

    /// <summary>
    /// Verifies a small card only needs its own height, which can be below the smallest height of the list layout.
    /// </summary>
    [Fact]
    public void MinHeight_CardLayout_SmallCard_CanBeBelowListMinimum()
    {
        // 80 around the list + 4 + 6 padding + 2 * 3 margin + 54 for the card.
        WindowHeightRules.MinHeight(WatchlistLayout.Cards, CardSize.Small, grouped: false, chrome: 80).Should().Be(150);
    }

    /// <summary>
    /// Verifies the header of a group adds its height and margin to the card layout.
    /// </summary>
    [Fact]
    public void MinHeight_CardLayout_Grouped_AddsHeader()
    {
        // The 322 of an ungrouped large card + 20 for the header + 8 above it and 2 below.
        WindowHeightRules.MinHeight(WatchlistLayout.Cards, CardSize.Large, grouped: true, chrome: 80).Should().Be(352);
    }

    /// <summary>
    /// Verifies the height is a whole number of screen pixels and at least one pixel above the input.
    /// </summary>
    [Theory]
    [InlineData(194.33, 1.5)]
    [InlineData(200, 1.5)]
    [InlineData(200, 1)]
    [InlineData(200.2, 1.25)]
    public void RoundUpToPixels_FractionalScale_GivesWholePixelsAboveInput(double height, double scale)
    {
        var result = WindowHeightRules.RoundUpToPixels(height, scale);

        (result * scale).Should().BeApproximately(Math.Round(result * scale), 1e-6);
        result.Should().BeGreaterThanOrEqualTo(height + (1 / scale) - 1e-6);
        result.Should().BeLessThan(height + (2 / scale));
    }
}
