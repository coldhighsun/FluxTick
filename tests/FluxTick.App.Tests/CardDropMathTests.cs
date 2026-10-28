using Avalonia;
using FluxTick.App.Views;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for <see cref="CardDropMath"/>.
/// </summary>
public class CardDropMathTests
{
    /// <summary>
    /// The cards of section "a": three on the first row and one on the second, each 100 by 50, below a 24 high header.
    /// </summary>
    private static readonly CardSlot[] SectionA =
    [
        new("a", 0, new Rect(0, 24, 100, 50)),
        new("a", 1, new Rect(100, 24, 100, 50)),
        new("a", 2, new Rect(200, 24, 100, 50)),
        new("a", 3, new Rect(0, 74, 100, 50)),
    ];

    /// <summary>
    /// The area of section "a": its header and its four cards.
    /// </summary>
    private static readonly SectionArea AreaA = new("a", new Rect(0, 0, 400, 124), new Rect(0, 0, 400, 24), 4, IsCollapsed: false);

    /// <summary>
    /// The area of an empty section "empty" below section "a".
    /// </summary>
    private static readonly SectionArea AreaEmpty = new("empty", new Rect(0, 124, 400, 60), new Rect(0, 124, 400, 24), 0, IsCollapsed: false);

    /// <summary>
    /// The area of a collapsed section "folded" that holds three cards below the empty section.
    /// </summary>
    private static readonly SectionArea AreaFolded = new("folded", new Rect(0, 184, 400, 24), new Rect(0, 184, 400, 24), 3, IsCollapsed: true);

    /// <summary>
    /// All sections in display order.
    /// </summary>
    private static readonly SectionArea[] All = [AreaA, AreaEmpty, AreaFolded];

    /// <summary>
    /// Verifies the left half of a card inserts before it.
    /// </summary>
    [Fact]
    public void ComputeTarget_LeftHalfOfCard_InsertsBefore()
    {
        var target = CardDropMath.ComputeTarget(SectionA, All, new Point(120, 40), null);

        target.Should().Be(new DropTarget("a", 1, IsHeaderHighlight: false));
    }

    /// <summary>
    /// Verifies the right half of a card inserts after it.
    /// </summary>
    [Fact]
    public void ComputeTarget_RightHalfOfCard_InsertsAfter()
    {
        var target = CardDropMath.ComputeTarget(SectionA, All, new Point(180, 40), null);

        target.Should().Be(new DropTarget("a", 2, IsHeaderHighlight: false));
    }

    /// <summary>
    /// Verifies the blank space to the right of a row's last card inserts after that card.
    /// </summary>
    [Fact]
    public void ComputeTarget_BlankSpaceRightOfRow_InsertsAfterLastCardOfRow()
    {
        var target = CardDropMath.ComputeTarget(SectionA, All, new Point(380, 40), null);

        target!.Value.Index.Should().Be(3);
    }

    /// <summary>
    /// Verifies the blank space to the right of a shorter row inserts after the card that ends it.
    /// </summary>
    [Fact]
    public void ComputeTarget_BlankSpaceRightOfSecondRow_InsertsAtEnd()
    {
        var target = CardDropMath.ComputeTarget(SectionA, All, new Point(300, 100), null);

        target!.Value.Index.Should().Be(4);
    }

    /// <summary>
    /// Verifies a position on the second row picks the card there, not the one above it.
    /// </summary>
    [Fact]
    public void ComputeTarget_SecondRow_PicksCardOnThatRow()
    {
        var target = CardDropMath.ComputeTarget(SectionA, All, new Point(20, 90), null);

        target!.Value.Index.Should().Be(3);
    }

    /// <summary>
    /// Verifies a position below the last row of the section appends.
    /// </summary>
    [Fact]
    public void ComputeTarget_BelowLastRowOfSection_Appends()
    {
        var target = CardDropMath.ComputeTarget(SectionA, [AreaA], new Point(10, 130), null);

        target.Should().Be(new DropTarget("a", 4, IsHeaderHighlight: false));
    }

    /// <summary>
    /// Verifies the header of an expanded section inserts before its first card.
    /// </summary>
    [Fact]
    public void ComputeTarget_HeaderOfExpandedSection_InsertsFirst()
    {
        var target = CardDropMath.ComputeTarget(SectionA, All, new Point(250, 10), null);

        target.Should().Be(new DropTarget("a", 0, IsHeaderHighlight: false));
    }

    /// <summary>
    /// Verifies an empty expanded section accepts a card at position zero.
    /// </summary>
    [Fact]
    public void ComputeTarget_EmptySection_DropsAtZero()
    {
        var target = CardDropMath.ComputeTarget(SectionA, All, new Point(50, 150), null);

        target.Should().Be(new DropTarget("empty", 0, IsHeaderHighlight: false));
    }

    /// <summary>
    /// Verifies dropping onto a collapsed section appends after the cards it holds and flags the header.
    /// </summary>
    [Fact]
    public void ComputeTarget_CollapsedSection_AppendsAndHighlightsHeader()
    {
        var target = CardDropMath.ComputeTarget(SectionA, All, new Point(50, 190), null);

        target.Should().Be(new DropTarget("folded", 3, IsHeaderHighlight: true));
    }

    /// <summary>
    /// Verifies a pointer outside every section falls back to the vertically nearest one.
    /// </summary>
    [Fact]
    public void ComputeTarget_PointerBelowEverything_UsesLastSection()
    {
        var target = CardDropMath.ComputeTarget(SectionA, All, new Point(50, 400), null);

        target!.Value.SectionKey.Should().Be("folded");
    }

    /// <summary>
    /// Verifies a locked section is used even when the pointer is over another one.
    /// </summary>
    [Fact]
    public void ComputeTarget_LockedSection_IgnoresOtherSections()
    {
        var target = CardDropMath.ComputeTarget(SectionA, All, new Point(50, 190), "a");

        target!.Value.SectionKey.Should().Be("a");
        target.Value.Index.Should().Be(4, "the pointer is below the last row of the locked section");
    }

    /// <summary>
    /// Verifies a locked section that is not laid out yields no target.
    /// </summary>
    [Fact]
    public void ComputeTarget_UnknownLockedSection_ReturnsNull()
    {
        CardDropMath.ComputeTarget(SectionA, All, new Point(50, 50), "missing").Should().BeNull();
    }

    /// <summary>
    /// Verifies there is no target when there are no sections.
    /// </summary>
    [Fact]
    public void ComputeTarget_NoSections_ReturnsNull()
    {
        CardDropMath.ComputeTarget([], [], new Point(0, 0), null).Should().BeNull();
    }
}
