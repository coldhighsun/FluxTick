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
    /// Verifies the card layout needs the card, its margin, the padding of the list and what is around the list.
    /// </summary>
    [Fact]
    public void MinHeight_CardLayout_FitsOneCard()
    {
        // 80 around the list + 4 + 6 padding + 2 * 3 margin + 176 for the card.
        WindowHeightRules.MinHeight(WatchlistLayout.Cards, CardSize.Large, grouped: false, chrome: 80).Should().Be(272);
        WindowHeightRules.MinHeight(WatchlistLayout.Cards, CardSize.Medium, grouped: false, chrome: 80).Should().Be(192);
    }

    /// <summary>
    /// Verifies a small card never takes the window below the smallest height of the list layout.
    /// </summary>
    [Fact]
    public void MinHeight_CardLayout_NeverBelowListMinimum()
    {
        WindowHeightRules.MinHeight(WatchlistLayout.Cards, CardSize.Small, grouped: false, chrome: 40).Should().Be(WindowHeightRules.ListMinHeight);
    }

    /// <summary>
    /// Verifies the header of a group adds its height and margin to the card layout.
    /// </summary>
    [Fact]
    public void MinHeight_CardLayout_Grouped_AddsHeader()
    {
        // The 272 of an ungrouped large card + 20 for the header + 8 above it and 2 below.
        WindowHeightRules.MinHeight(WatchlistLayout.Cards, CardSize.Large, grouped: true, chrome: 80).Should().Be(302);
    }
}
