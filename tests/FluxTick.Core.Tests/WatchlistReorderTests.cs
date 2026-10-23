using FluxTick.Abstractions;
using FluxTick.Core.Application;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for <see cref="WatchlistReorder"/>.
/// </summary>
public sealed class WatchlistReorderTests
{
    /// <summary>
    /// Creates a watchlist whose symbols are the given names and whose group ids are given as "symbol:group".
    /// </summary>
    /// <param name="items">
    /// The items, for example <c>"A:g1"</c> or <c>"B"</c> for an item without a group.
    /// </param>
    /// <returns>
    /// The watchlist.
    /// </returns>
    private static List<WatchlistEntry> Build(params string[] items) =>
        [.. items.Select(item =>
        {
            var parts = item.Split(':');
            return new WatchlistEntry { ProviderId = "p", Symbol = parts[0], GroupId = parts.Length > 1 ? parts[1] : null };
        })];

    /// <summary>
    /// Gets the symbols of a watchlist in order.
    /// </summary>
    /// <param name="list">
    /// The watchlist.
    /// </param>
    /// <returns>
    /// The symbols.
    /// </returns>
    private static string[] Symbols(List<WatchlistEntry> list) => [.. list.Select(e => e.Symbol)];

    /// <summary>
    /// Verifies moving within the whole list forward places the entry at the requested position.
    /// </summary>
    [Fact]
    public void Move_ForwardWithinAll_PlacesEntryAtIndex()
    {
        var list = Build("A", "B", "C", "D");

        var changed = WatchlistReorder.Move(list, list[0], _ => true, 2);

        changed.Should().BeTrue();
        Symbols(list).Should().Equal("B", "C", "A", "D");
    }

    /// <summary>
    /// Verifies moving backwards places the entry before the entry that held the position.
    /// </summary>
    [Fact]
    public void Move_Backward_PlacesEntryBeforeTarget()
    {
        var list = Build("A", "B", "C", "D");

        WatchlistReorder.Move(list, list[3], _ => true, 1);

        Symbols(list).Should().Equal("A", "D", "B", "C");
    }

    /// <summary>
    /// Verifies dropping an entry where it already is reports no change.
    /// </summary>
    [Fact]
    public void Move_ToSamePosition_ReportsNoChange()
    {
        var list = Build("A", "B", "C");

        var changed = WatchlistReorder.Move(list, list[1], _ => true, 1);

        changed.Should().BeFalse();
        Symbols(list).Should().Equal("A", "B", "C");
    }

    /// <summary>
    /// Verifies an index past the end of the section appends after the section's last entry, not after the list's.
    /// </summary>
    [Fact]
    public void Move_PastEndOfSection_AppendsAfterLastMember()
    {
        var list = Build("A:g1", "B:g2", "C:g1", "D:g2");

        WatchlistReorder.Move(list, list[0], e => e.GroupId == "g2", 99);

        Symbols(list).Should().Equal("B", "C", "D", "A");
    }

    /// <summary>
    /// Verifies the position is relative to the section's members, not to the whole list.
    /// </summary>
    [Fact]
    public void Move_IntoMiddleOfOtherSection_InsertsBeforeThatMember()
    {
        var list = Build("A:g1", "B:g2", "C:g1", "D:g2", "E:g2");

        WatchlistReorder.Move(list, list[0], e => e.GroupId == "g2", 1);

        Symbols(list).Should().Equal("B", "C", "A", "D", "E");
    }

    /// <summary>
    /// Verifies moving into an empty section appends to the end of the list.
    /// </summary>
    [Fact]
    public void Move_IntoEmptySection_AppendsToList()
    {
        var list = Build("A:g1", "B:g1", "C:g1");

        WatchlistReorder.Move(list, list[0], e => e.GroupId == "empty", 0);

        Symbols(list).Should().Equal("B", "C", "A");
    }

    /// <summary>
    /// Verifies a negative index is treated as the start of the section.
    /// </summary>
    [Fact]
    public void Move_NegativeIndex_PlacesEntryFirstInSection()
    {
        var list = Build("A:g1", "B:g2", "C:g2");

        WatchlistReorder.Move(list, list[0], e => e.GroupId == "g2", -5);

        Symbols(list).Should().Equal("A", "B", "C");
    }

    /// <summary>
    /// Creates groups whose ids and names are the given ids.
    /// </summary>
    /// <param name="ids">
    /// The group ids.
    /// </param>
    /// <returns>
    /// The groups.
    /// </returns>
    private static List<WatchGroup> Groups(params string[] ids) => [.. ids.Select(id => new WatchGroup { Id = id, Name = id })];

    /// <summary>
    /// Verifies a group moves to the requested absolute position.
    /// </summary>
    [Theory]
    [InlineData("a", 2, "b,c,a")]
    [InlineData("c", 0, "c,a,b")]
    [InlineData("b", 1, "a,b,c")]
    [InlineData("a", 99, "b,c,a")]
    [InlineData("c", -4, "c,a,b")]
    public void MoveGroup_ToIndex_PlacesGroup(string id, int index, string expected)
    {
        var groups = Groups("a", "b", "c");

        var changed = WatchlistReorder.MoveGroup(groups, id, index);

        string.Join(",", groups.Select(g => g.Id)).Should().Be(expected);
        changed.Should().Be(expected != "a,b,c");
    }

    /// <summary>
    /// Verifies an asset class takes the place of the visible class at the target position, counting only visible
    /// classes.
    /// </summary>
    [Theory]
    [InlineData(AssetClass.Crypto, 2, "Equity,Index,Crypto,Forex,Commodity")]
    [InlineData(AssetClass.Index, 0, "Index,Crypto,Equity,Forex,Commodity")]
    [InlineData(AssetClass.Crypto, 1, "Equity,Crypto,Index,Forex,Commodity")]
    [InlineData(AssetClass.Equity, 99, "Crypto,Index,Equity,Forex,Commodity")]
    public void MoveType_AmongVisibleClasses_PlacesClass(AssetClass type, int index, string expected)
    {
        List<AssetClass> order = [AssetClass.Crypto, AssetClass.Equity, AssetClass.Index, AssetClass.Forex, AssetClass.Commodity];
        AssetClass[] visible = [AssetClass.Crypto, AssetClass.Equity, AssetClass.Index];

        WatchlistReorder.MoveType(order, type, visible, index);

        string.Join(",", order).Should().Be(expected);
    }

    /// <summary>
    /// Verifies the order is not changed when the class would stay where it is or is not visible.
    /// </summary>
    [Fact]
    public void MoveType_SamePositionOrHiddenClass_ReportsNoChange()
    {
        List<AssetClass> order = [AssetClass.Crypto, AssetClass.Equity, AssetClass.Index];
        AssetClass[] visible = [AssetClass.Crypto, AssetClass.Index];

        WatchlistReorder.MoveType(order, AssetClass.Crypto, visible, 0).Should().BeFalse();
        WatchlistReorder.MoveType(order, AssetClass.Equity, visible, 0).Should().BeFalse();
        order.Should().Equal(AssetClass.Crypto, AssetClass.Equity, AssetClass.Index);
    }

    /// <summary>
    /// Verifies classes missing from the stored order follow the listed ones in their default order.
    /// </summary>
    [Fact]
    public void EffectiveTypeOrder_PartialOrder_AppendsRemainingClasses()
    {
        var appearance = new AppearanceSettings { TypeOrder = [AssetClass.Forex, AssetClass.Crypto, AssetClass.Forex] };

        appearance.EffectiveTypeOrder().Should().Equal(
            AssetClass.Forex,
            AssetClass.Crypto,
            AssetClass.Equity,
            AssetClass.Index,
            AssetClass.Commodity);
    }

    /// <summary>
    /// Verifies an unknown group leaves the list untouched.
    /// </summary>
    [Fact]
    public void MoveGroup_UnknownGroup_DoesNothing()
    {
        var groups = Groups("a", "b");

        WatchlistReorder.MoveGroup(groups, "zzz", 0).Should().BeFalse();

        groups.Select(g => g.Id).Should().Equal("a", "b");
    }

    /// <summary>
    /// Verifies an entry that is not part of the list leaves it untouched.
    /// </summary>
    [Fact]
    public void Move_UnknownEntry_DoesNothing()
    {
        var list = Build("A", "B");

        var changed = WatchlistReorder.Move(list, new WatchlistEntry { Symbol = "Z" }, _ => true, 0);

        changed.Should().BeFalse();
        Symbols(list).Should().Equal("A", "B");
    }
}
