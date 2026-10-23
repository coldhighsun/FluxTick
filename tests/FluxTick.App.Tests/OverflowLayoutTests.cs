using FluxTick.App.Controls;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the arithmetic that decides how many footer chips fit.
/// </summary>
public class OverflowLayoutTests
{
    /// <summary>
    /// Three chips of 50 pixels with a 10 pixel gap take 170 pixels.
    /// </summary>
    private static readonly double[] Three = [50, 50, 50];

    /// <summary>
    /// Verifies every item is shown when they fit without the reserved width.
    /// </summary>
    [Fact]
    public void ComputeVisibleCount_AllFit_ReturnsAllWithoutReserving()
    {
        var count = OverflowLayout.ComputeVisibleCount(Three, 10, 170, 40);

        count.Should().Be(3);
    }

    /// <summary>
    /// Verifies the overflow indicator's width is reserved once an item has to be dropped.
    /// </summary>
    [Fact]
    public void ComputeVisibleCount_OneTooWide_ReservesIndicatorWidth()
    {
        // 169 < 170 so not all fit; 169 - 40 = 129 holds two chips (110) but not three.
        var count = OverflowLayout.ComputeVisibleCount(Three, 10, 169, 40);

        count.Should().Be(2);
    }

    /// <summary>
    /// Verifies the gaps between items are part of the budget.
    /// </summary>
    [Fact]
    public void ComputeVisibleCount_SpacingCounts_DropsItemThatOnlyFitsWithoutGap()
    {
        // Budget 100: 50 + 10 + 50 = 110 does not fit, so only the first chip stays.
        var count = OverflowLayout.ComputeVisibleCount(Three, 10, 100 + 40, 40);

        count.Should().Be(1);
    }

    /// <summary>
    /// Verifies nothing is shown when not even the first item fits next to the indicator.
    /// </summary>
    [Fact]
    public void ComputeVisibleCount_TinyWidth_ReturnsZero()
    {
        var count = OverflowLayout.ComputeVisibleCount(Three, 10, 30, 40);

        count.Should().Be(0);
    }

    /// <summary>
    /// Verifies an item wider than everything available is hidden instead of throwing.
    /// </summary>
    [Fact]
    public void ComputeVisibleCount_SingleItemWiderThanAvailable_ReturnsZero()
    {
        var count = OverflowLayout.ComputeVisibleCount([200], 10, 100, 40);

        count.Should().Be(0);
    }

    /// <summary>
    /// Verifies an empty list yields zero.
    /// </summary>
    [Fact]
    public void ComputeVisibleCount_NoItems_ReturnsZero()
    {
        var count = OverflowLayout.ComputeVisibleCount([], 10, 100, 40);

        count.Should().Be(0);
    }

    /// <summary>
    /// Verifies an unconstrained width shows everything.
    /// </summary>
    [Fact]
    public void ComputeVisibleCount_InfiniteWidth_ReturnsAll()
    {
        var count = OverflowLayout.ComputeVisibleCount(Three, 10, double.PositiveInfinity, 40);

        count.Should().Be(3);
    }

    /// <summary>
    /// Verifies the total includes the gaps between items only.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 50)]
    [InlineData(3, 170)]
    public void Total_LeadingItems_IncludesGapsBetweenThem(int count, double expected)
    {
        var total = OverflowLayout.Total(Three, count, 10);

        total.Should().Be(expected);
    }
}
