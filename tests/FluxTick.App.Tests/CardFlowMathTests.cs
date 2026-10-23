using Avalonia;
using FluxTick.App.Views;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for <see cref="CardFlowMath"/>.
/// </summary>
public class CardFlowMathTests
{
    /// <summary>
    /// The size of a card including its margin.
    /// </summary>
    private static readonly Size Card = new(100, 50);

    /// <summary>
    /// Creates a section that is 300 wide, so three cards fit on a row, with a 20 high header.
    /// </summary>
    /// <param name="key">
    /// The section key.
    /// </param>
    /// <param name="top">
    /// The top of the section.
    /// </param>
    /// <param name="originalRows">
    /// The rows the section occupies now.
    /// </param>
    /// <param name="others">
    /// The cards in the section other than the dragged one.
    /// </param>
    /// <param name="collapsed">
    /// Whether the section is collapsed.
    /// </param>
    /// <returns>
    /// The section.
    /// </returns>
    private static FlowSection Section(string key, double top, int originalRows, int others, bool collapsed = false) => new(
        key,
        new Rect(0, top, 300, 20 + (collapsed ? 0 : originalRows * 50)),
        new Rect(0, top, 300, 20),
        top + 20,
        originalRows,
        collapsed,
        others);

    /// <summary>
    /// Verifies the position of a card is computed row by row, wrapping after as many cards as fit.
    /// </summary>
    [Fact]
    public void Slot_FourthPosition_WrapsToSecondRow()
    {
        var layout = CardFlowMath.Layout([Section("a", 0, 2, 4)], Card, null);

        layout[0].PerRow.Should().Be(3);
        layout[0].Slot(0).Should().Be(new Rect(0, 20, 100, 50));
        layout[0].Slot(2).Should().Be(new Rect(200, 20, 100, 50));
        layout[0].Slot(3).Should().Be(new Rect(0, 70, 100, 50));
    }

    /// <summary>
    /// Verifies a section that loses a card shrinks by a row once it no longer needs it, and moves the sections
    /// below it up.
    /// </summary>
    [Fact]
    public void Layout_SectionLosesLastCardOfRow_MovesFollowingSectionsUp()
    {
        // Section a: 4 cards on 2 rows, one of them dragged out, leaves 3 cards on 1 row.
        var layout = CardFlowMath.Layout([Section("a", 0, 2, 3), Section("b", 120, 1, 2)], Card, null);

        layout[0].Bounds.Height.Should().Be(70);
        layout[0].ShiftY.Should().Be(0);
        layout[1].ShiftY.Should().Be(-50);
        layout[1].Bounds.Top.Should().Be(70);
        layout[1].Header.Top.Should().Be(70);
        layout[1].Slot(0).Top.Should().Be(90);
    }

    /// <summary>
    /// Verifies a gap for the dragged card can add a row and moves the following sections down.
    /// </summary>
    [Fact]
    public void Layout_GapAddsRow_MovesFollowingSectionsDown()
    {
        // Section b already has a full row; the gap makes it 4 cards on 2 rows.
        var layout = CardFlowMath.Layout([Section("a", 0, 1, 2), Section("b", 70, 1, 3), Section("c", 140, 1, 1)], Card, "b");

        layout[1].ShiftY.Should().Be(0);
        layout[1].Bounds.Height.Should().Be(120);
        layout[2].ShiftY.Should().Be(50);
    }

    /// <summary>
    /// Verifies a section never shrinks below one row, so a group that loses its last card keeps its place.
    /// </summary>
    [Fact]
    public void Layout_SectionLosesOnlyCard_KeepsOneRow()
    {
        var layout = CardFlowMath.Layout([Section("a", 0, 1, 0), Section("b", 70, 1, 1)], Card, null);

        layout[0].Bounds.Height.Should().Be(70);
        layout[1].ShiftY.Should().Be(0);
    }

    /// <summary>
    /// Verifies an empty section holds a single row for the card dropped into it, so nothing below moves.
    /// </summary>
    [Fact]
    public void Layout_GapInEmptySection_DoesNotMoveFollowingSections()
    {
        var layout = CardFlowMath.Layout([Section("empty", 0, 1, 0), Section("b", 70, 1, 1)], Card, "empty");

        layout[1].ShiftY.Should().Be(0);
        layout[0].Slot(0).Should().Be(new Rect(0, 20, 100, 50));
    }

    /// <summary>
    /// Verifies a collapsed section takes no room for cards before or after the drag.
    /// </summary>
    [Fact]
    public void Layout_CollapsedSection_KeepsItsHeight()
    {
        var layout = CardFlowMath.Layout([Section("folded", 0, 0, 3, collapsed: true), Section("b", 20, 1, 1)], Card, "folded");

        layout[0].Bounds.Height.Should().Be(20);
        layout[1].ShiftY.Should().Be(0);
    }

    /// <summary>
    /// Verifies a section narrower than a card still gets one card per row.
    /// </summary>
    [Fact]
    public void Layout_NarrowSection_PutsOneCardPerRow()
    {
        var narrow = new FlowSection("a", new Rect(0, 0, 60, 120), new Rect(0, 0, 60, 20), 20, 2, false, 2);

        var layout = CardFlowMath.Layout([narrow], Card, null);

        layout[0].PerRow.Should().Be(1);
        layout[0].Slot(1).Should().Be(new Rect(0, 70, 100, 50));
    }

    /// <summary>
    /// Verifies the cards at and behind the gap move one position further.
    /// </summary>
    [Theory]
    [InlineData(0, 2, 0)]
    [InlineData(1, 2, 1)]
    [InlineData(2, 2, 3)]
    [InlineData(3, 2, 4)]
    public void PositionOf_WithGap_SkipsTheGap(int index, int gap, int expected)
    {
        CardFlowMath.PositionOf(index, gap).Should().Be(expected);
    }

    /// <summary>
    /// Verifies the cards keep their positions when there is no gap.
    /// </summary>
    [Fact]
    public void PositionOf_WithoutGap_KeepsIndex()
    {
        CardFlowMath.PositionOf(5, null).Should().Be(5);
    }
}
