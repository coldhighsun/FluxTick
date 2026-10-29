using FluxTick.Abstractions;
using FluxTick.Core.Application;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for the choice of instruments the tray list shows.
/// </summary>
public class TrayAssetSelectionTests
{
    /// <summary>
    /// Creates watchlist entries of one provider.
    /// </summary>
    /// <param name="symbols">
    /// The symbols, in watchlist order.
    /// </param>
    /// <returns>
    /// The entries.
    /// </returns>
    private static List<WatchlistEntry> Entries(params string[] symbols) =>
        [.. symbols.Select(s => new WatchlistEntry { ProviderId = "p", Symbol = s, DisplayName = s, AssetClass = AssetClass.Crypto })];

    /// <summary>
    /// Gets the key of an entry.
    /// </summary>
    /// <param name="entry">
    /// The entry.
    /// </param>
    /// <returns>
    /// The key.
    /// </returns>
    private static string KeyOf(WatchlistEntry entry) => TrayAssetSelection.Key(entry.ProviderId, entry.Symbol);

    /// <summary>
    /// Verifies only the chosen instruments are listed, in the chosen order.
    /// </summary>
    [Fact]
    public void Choice_ListsTheChosenInTheirOrder()
    {
        var entries = Entries("A", "B", "C");

        var picked = TrayAssetSelection.Resolve(entries, KeyOf, [TrayAssetSelection.Key("p", "C"), TrayAssetSelection.Key("p", "A")]);

        picked.Select(e => e.Symbol).Should().Equal("C", "A");
    }

    /// <summary>
    /// Verifies a key without an instrument, and a repeated key, are skipped.
    /// </summary>
    [Fact]
    public void UnknownOrRepeatedKey_IsSkipped()
    {
        var entries = Entries("A", "B");

        var picked = TrayAssetSelection.Resolve(entries, KeyOf, [TrayAssetSelection.Key("p", "X"), TrayAssetSelection.Key("p", "B"), TrayAssetSelection.Key("p", "B")]);

        picked.Select(e => e.Symbol).Should().Equal("B");
    }

    /// <summary>
    /// Verifies an empty choice lists nothing.
    /// </summary>
    [Fact]
    public void EmptyChoice_ListsNothing()
    {
        TrayAssetSelection.Resolve(Entries("A"), KeyOf, []).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies normalizing drops instruments that left the watchlist and repeated keys, and keeps the order.
    /// </summary>
    [Fact]
    public void Normalize_DropsGoneAndRepeatedKeys()
    {
        var settings = new AppSettings { Watchlist = Entries("A", "B") };
        settings.General.TrayAssets =
        [
            TrayAssetSelection.Key("p", "B"),
            TrayAssetSelection.Key("p", "GONE"),
            TrayAssetSelection.Key("p", "B"),
            TrayAssetSelection.Key("p", "A"),
        ];

        settings.Normalize().Should().BeTrue();

        settings.General.TrayAssets.Should().Equal(TrayAssetSelection.Key("p", "B"), TrayAssetSelection.Key("p", "A"));
    }

    /// <summary>
    /// Verifies normalizing cuts a list that is longer than the tray list can hold.
    /// </summary>
    [Fact]
    public void Normalize_CutsTheListToTheMost()
    {
        var symbols = Enumerable.Range(1, TrayAssetSelection.MaxAssets + 2).Select(i => $"S{i}").ToArray();
        var settings = new AppSettings { Watchlist = Entries(symbols) };
        settings.General.TrayAssets = [.. symbols.Select(s => TrayAssetSelection.Key("p", s))];

        settings.Normalize().Should().BeTrue();

        settings.General.TrayAssets.Should().Equal(symbols.Take(TrayAssetSelection.MaxAssets).Select(s => TrayAssetSelection.Key("p", s)));
    }

    /// <summary>
    /// Verifies the tray list is empty by default.
    /// </summary>
    [Fact]
    public void Normalize_DefaultsToEmpty()
    {
        var settings = new AppSettings { Watchlist = Entries("A") };

        settings.Normalize();
        settings.General.TrayAssets.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies adding puts an instrument at the end, once, and stops when the list is full.
    /// </summary>
    [Fact]
    public void Add_AppendsOnceAndStopsWhenFull()
    {
        var list = new List<string>();

        TrayAssetSelection.Add(list, "p|A").Should().BeTrue();
        TrayAssetSelection.Add(list, "p|A").Should().BeFalse("it is on the list already");
        for (var i = 1; i < TrayAssetSelection.MaxAssets; i++)
        {
            TrayAssetSelection.Add(list, $"p|S{i}").Should().BeTrue();
        }

        TrayAssetSelection.Add(list, "p|extra").Should().BeFalse("the list is full");
        list.Should().HaveCount(TrayAssetSelection.MaxAssets).And.StartWith("p|A");
    }

    /// <summary>
    /// Verifies toggling takes an instrument off, puts it on when there is room, and does nothing when the list is full.
    /// </summary>
    [Fact]
    public void Toggle_RemovesOrAddsAndRespectsTheMost()
    {
        var list = new List<string> { "p|A" };

        TrayAssetSelection.Toggle(list, "p|A").Should().BeTrue();
        list.Should().BeEmpty();
        TrayAssetSelection.Toggle(list, "p|B").Should().BeTrue();
        list.Should().Equal("p|B");

        var full = Enumerable.Range(1, TrayAssetSelection.MaxAssets).Select(i => $"p|S{i}").ToList();
        TrayAssetSelection.Toggle(full, "p|extra").Should().BeFalse();
        full.Should().NotContain("p|extra");
    }

    /// <summary>
    /// Verifies moving puts an instrument at a position, clamps the position, and ignores unknown keys.
    /// </summary>
    [Fact]
    public void Move_PutsTheKeyAtThePosition()
    {
        var list = new List<string> { "p|A", "p|B", "p|C" };

        TrayAssetSelection.Move(list, "p|C", 0).Should().BeTrue();
        list.Should().Equal("p|C", "p|A", "p|B");
        TrayAssetSelection.Move(list, "p|C", 99).Should().BeTrue();
        list.Should().Equal("p|A", "p|B", "p|C");
        TrayAssetSelection.Move(list, "p|C", 2).Should().BeFalse("it is there already");
        TrayAssetSelection.Move(list, "p|X", 0).Should().BeFalse();
    }

    /// <summary>
    /// Verifies moving on an empty list does nothing instead of failing.
    /// </summary>
    [Fact]
    public void Move_OnAnEmptyList_DoesNothing()
    {
        var list = new List<string>();

        var move = () => TrayAssetSelection.Move(list, "p|A", 0);

        move.Should().NotThrow().Which.Should().BeFalse();
        list.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies an instrument that changed its data source keeps its position on the list.
    /// </summary>
    [Fact]
    public void Replace_KeyOnTheList_TakesTheNewKeyAtTheSamePosition()
    {
        var list = new List<string> { "p|A", "p|B", "p|C" };

        var replaced = TrayAssetSelection.Replace(list, "p|B", "q|B");

        replaced.Should().BeTrue();
        list.Should().Equal("p|A", "q|B", "p|C");
    }

    /// <summary>
    /// Verifies an instrument that was not on the list does not get onto it by changing its data source.
    /// </summary>
    [Fact]
    public void Replace_KeyNotOnTheList_DoesNothing()
    {
        var list = new List<string> { "p|A" };

        TrayAssetSelection.Replace(list, "p|B", "q|B").Should().BeFalse();
        list.Should().Equal("p|A");
    }

    /// <summary>
    /// Verifies the key is not repeated when the new one was on the list already.
    /// </summary>
    [Fact]
    public void Replace_NewKeyAlreadyOnTheList_OnlyDropsTheOldKey()
    {
        var list = new List<string> { "p|B", "p|A", "q|B" };

        TrayAssetSelection.Replace(list, "p|B", "q|B").Should().BeTrue();
        list.Should().Equal("p|A", "q|B");
    }

    /// <summary>
    /// Verifies the choice survives a clone, which is how settings are saved.
    /// </summary>
    [Fact]
    public void Clone_KeepsTheChoice()
    {
        var settings = new AppSettings();
        settings.General.TrayAssets = [TrayAssetSelection.Key("p", "A")];

        settings.Clone().General.TrayAssets.Should().Equal(TrayAssetSelection.Key("p", "A"));
        new AppSettings().Clone().General.TrayAssets.Should().BeEmpty();
    }
}
