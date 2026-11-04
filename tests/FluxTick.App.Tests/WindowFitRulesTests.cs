using FluxTick.App.Views;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for <see cref="WindowFitRules"/>.
/// </summary>
public class WindowFitRulesTests
{
    /// <summary>
    /// The height of the window around the list of cards used by the tests.
    /// </summary>
    private const double Chrome = 80;

    /// <summary>
    /// Verifies a single card gets one column, the smallest width and a height that holds the card.
    /// </summary>
    [Fact]
    public void CardsSize_SingleCard_UsesOneColumn()
    {
        var size = WindowFitRules.CardsSize([new(1, false)], false, CardSize.Medium, Chrome, 1920, 1080);

        size.Width.Should().Be(WindowWidthRules.CardsMinWidth);
        size.Height.Should().BeApproximately(Chrome + 10 + 130, 0.001);
    }

    /// <summary>
    /// Verifies the number of columns is the one whose window shape is closest to 4:3 or 16:9.
    /// </summary>
    [Fact]
    public void CardsSize_ManyCards_ChoosesColumnsClosestToTargetRatio()
    {
        var size = WindowFitRules.CardsSize([new(12, false)], false, CardSize.Medium, Chrome, 1920, 1080);

        // Five columns of 182 plus 22 wide and three rows of 130 plus 90 high is a ratio of about 1.94.
        size.Width.Should().BeApproximately(932, 0.001);
        size.Height.Should().BeApproximately(480, 0.001);
        WindowFitRules.Score(size.Width, size.Height).Should().BeLessThan(0.1);
    }

    /// <summary>
    /// Verifies a shape that shows every card beats a better shape that needs a scroll bar.
    /// </summary>
    [Fact]
    public void CardsSize_ShapeNeedingScrollBar_LosesToOneThatFits()
    {
        var size = WindowFitRules.CardsSize([new(12, false)], false, CardSize.Medium, Chrome, 1920, 500);

        size.Height.Should().BeLessThan(500);
    }

    /// <summary>
    /// Verifies the height is capped at the work area and the closest shape is chosen when no shape shows every card.
    /// </summary>
    [Fact]
    public void CardsSize_TooManyCards_CapsHeightAndStaysWithinWidth()
    {
        var size = WindowFitRules.CardsSize([new(100, false)], false, CardSize.Medium, Chrome, 1000, 600);

        size.Height.Should().Be(600);
        size.Width.Should().BeApproximately(750, 0.001);
        size.Width.Should().BeLessThanOrEqualTo(1000);
    }

    /// <summary>
    /// Verifies a collapsed section takes no rows while its header stays.
    /// </summary>
    [Fact]
    public void CardsSize_CollapsedSection_AddsOnlyItsHeader()
    {
        var alone = WindowFitRules.CardsSize([new(6, false)], true, CardSize.Medium, Chrome, 1920, 1080);
        var withCollapsed = WindowFitRules.CardsSize([new(6, false), new(6, true)], true, CardSize.Medium, Chrome, 1920, 1080);

        withCollapsed.Width.Should().Be(alone.Width);
        withCollapsed.Height.Should().BeApproximately(alone.Height + (CardLayout.SectionHeaderHeight + CardLayout.SectionHeaderMargin.Top + CardLayout.SectionHeaderMargin.Bottom), 0.001);
    }

    /// <summary>
    /// Verifies a collapsed section does not raise the number of columns.
    /// </summary>
    [Fact]
    public void CardsSize_CollapsedLargeSection_DoesNotAddColumns()
    {
        var size = WindowFitRules.CardsSize([new(2, false), new(50, true)], true, CardSize.Medium, Chrome, 1920, 1080);

        // Two cards fill at most two columns of 182 plus 22.
        size.Width.Should().BeLessThanOrEqualTo(386.001);
    }

    /// <summary>
    /// Verifies the rows follow the columns that fit the smallest width, which is wider than one small card.
    /// </summary>
    [Fact]
    public void CardsSize_SmallCardsAtSmallestWidth_CountsTheColumnsThatFit()
    {
        var size = WindowFitRules.CardsSize([new(4, false)], false, CardSize.Small, Chrome, 328, 1080);

        // 328 fits two small cards per row, so four cards take two rows of 60 and not four.
        size.Height.Should().BeApproximately(Chrome + 10 + (2 * 60), 0.001);
    }

    /// <summary>
    /// Verifies a window without sections still has the smallest width and at least the smallest height.
    /// </summary>
    [Fact]
    public void CardsSize_NoSections_GivesSmallestWindow()
    {
        var size = WindowFitRules.CardsSize([], false, CardSize.Small, Chrome, 1920, 1080);

        size.Width.Should().Be(WindowWidthRules.CardsMinWidth);
        size.Height.Should().BeGreaterThanOrEqualTo(WindowHeightRules.MinHeight(WatchlistLayout.Cards, CardSize.Small, false, Chrome));
    }

    /// <summary>
    /// Verifies the width never exceeds the work area, whatever the card size.
    /// </summary>
    [Theory]
    [InlineData(CardSize.Small)]
    [InlineData(CardSize.Medium)]
    [InlineData(CardSize.Large)]
    public void CardsSize_AnySize_StaysWithinWorkArea(CardSize cardSize)
    {
        var size = WindowFitRules.CardsSize([new(40, false)], false, cardSize, Chrome, 1280, 720);

        size.Width.Should().BeLessThanOrEqualTo(1280);
        size.Height.Should().BeLessThanOrEqualTo(720);
    }

    /// <summary>
    /// Verifies that at a fractional scale each row gets the pixel the layout adds by rounding up its margins.
    /// </summary>
    [Fact]
    public void CardsSize_FractionalScale_AddsRoundingOfEachRow()
    {
        var plain = WindowFitRules.CardsSize([new(1, false), new(1, false), new(1, false)], false, CardSize.Medium, Chrome, 1920, 1080);
        var scaled = WindowFitRules.CardsSize([new(1, false), new(1, false), new(1, false)], false, CardSize.Medium, Chrome, 1920, 1080, 1.5);

        // One column, three rows: the margins of 3 are 4.5 screen pixels and are rounded up to 5 on each side.
        scaled.Height.Should().BeApproximately(plain.Height + (3 * 1 / 1.5), 0.001);
    }

    /// <summary>
    /// Verifies the score is zero for a window of a target ratio.
    /// </summary>
    [Fact]
    public void Score_TargetRatio_IsZero()
    {
        WindowFitRules.Score(1600, 900).Should().BeApproximately(0, 1e-9);
        WindowFitRules.Score(800, 600).Should().BeApproximately(0, 1e-9);
    }


    /// <summary>
    /// Verifies a window that reaches below the work area is moved up and one inside it stays.
    /// </summary>
    [Theory]
    [InlineData(100, 500, 0, 1000, 100)]
    [InlineData(700, 500, 0, 1000, 500)]
    [InlineData(300, 1200, 40, 1000, 40)]
    public void ClampTop_Window_StaysInsideWorkArea(int top, double height, int areaTop, int areaBottom, int expected)
    {
        WindowFitRules.ClampTop(top, height, areaTop, areaBottom).Should().Be(expected);
    }
}
