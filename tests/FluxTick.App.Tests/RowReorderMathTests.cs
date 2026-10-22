using FluxTick.App.Views;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the drag-to-reorder drop target calculation.
/// </summary>
public class RowReorderMathTests
{
    /// <summary>
    /// Three equally tall rows of 40 pixels each.
    /// </summary>
    private static readonly (double Top, double Height)[] Even = [(0, 40), (40, 40), (80, 40)];

    /// <summary>
    /// Verifies a row dragged less than halfway into its neighbour stays where it is.
    /// </summary>
    [Fact]
    public void ComputeTargetIndex_CentreInsideOwnSlot_ReturnsOriginalIndex()
    {
        var target = RowReorderMath.ComputeTargetIndex(Even, 1, 55);

        target.Should().Be(1);
    }

    /// <summary>
    /// Verifies dragging down past the next row's midpoint moves the target down.
    /// </summary>
    [Fact]
    public void ComputeTargetIndex_DraggedPastNextMidpoint_ReturnsNextIndex()
    {
        var target = RowReorderMath.ComputeTargetIndex(Even, 0, 61);

        target.Should().Be(1);
    }

    /// <summary>
    /// Verifies dragging up past the previous row's midpoint moves the target up.
    /// </summary>
    [Fact]
    public void ComputeTargetIndex_DraggedPastPreviousMidpoint_ReturnsPreviousIndex()
    {
        var target = RowReorderMath.ComputeTargetIndex(Even, 2, 59);

        target.Should().Be(1);
    }

    /// <summary>
    /// Verifies a long drag can skip over several rows at once.
    /// </summary>
    [Fact]
    public void ComputeTargetIndex_DraggedFarDown_SkipsRows()
    {
        var target = RowReorderMath.ComputeTargetIndex(Even, 0, 500);

        target.Should().Be(2);
    }

    /// <summary>
    /// Verifies the target is clamped at the top of the list.
    /// </summary>
    [Fact]
    public void ComputeTargetIndex_DraggedAboveList_ReturnsFirstIndex()
    {
        var target = RowReorderMath.ComputeTargetIndex(Even, 2, -500);

        target.Should().Be(0);
    }

    /// <summary>
    /// Verifies rows of different heights use their own midpoints.
    /// </summary>
    [Fact]
    public void ComputeTargetIndex_UnevenHeights_UsesRowMidpoints()
    {
        (double Top, double Height)[] rows = [(0, 20), (20, 60), (80, 20)];

        RowReorderMath.ComputeTargetIndex(rows, 0, 39).Should().Be(0);
        RowReorderMath.ComputeTargetIndex(rows, 0, 41).Should().Be(1);
    }

    /// <summary>
    /// Verifies the first slot is reachable when the drag is clamped so the dragged row sits flush with the list top.
    /// </summary>
    [Fact]
    public void ComputeTargetIndex_ClampedAtListTop_ReturnsFirstIndex()
    {
        var target = RowReorderMath.ComputeTargetIndex(Even, 2, 20);

        target.Should().Be(0);
    }

    /// <summary>
    /// Verifies the last slot is reachable when the drag is clamped so the dragged row sits flush with the list bottom.
    /// </summary>
    [Fact]
    public void ComputeTargetIndex_ClampedAtListBottom_ReturnsLastIndex()
    {
        var target = RowReorderMath.ComputeTargetIndex(Even, 0, 100);

        target.Should().Be(2);
    }

    /// <summary>
    /// Verifies the first slot is reachable even when the dragged row is taller than the first row.
    /// </summary>
    [Fact]
    public void ComputeTargetIndex_TallRowClampedAtTop_ReturnsFirstIndex()
    {
        (double Top, double Height)[] rows = [(0, 20), (20, 60)];

        var target = RowReorderMath.ComputeTargetIndex(rows, 1, 30);

        target.Should().Be(0);
    }

    /// <summary>
    /// Verifies an empty list yields index zero.
    /// </summary>
    [Fact]
    public void ComputeTargetIndex_NoRows_ReturnsZero()
    {
        var target = RowReorderMath.ComputeTargetIndex([], 0, 10);

        target.Should().Be(0);
    }
}
