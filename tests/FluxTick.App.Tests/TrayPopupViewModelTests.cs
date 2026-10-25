using System.Collections.ObjectModel;
using FluxTick.Abstractions;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the list the tray popup shows.
/// </summary>
public class TrayPopupViewModelTests
{
    /// <summary>
    /// Creates watchlist rows.
    /// </summary>
    /// <param name="count">
    /// The number of rows.
    /// </param>
    /// <returns>
    /// The rows.
    /// </returns>
    private static ObservableCollection<AssetRowViewModel> Rows(int count) =>
        [.. Enumerable.Range(1, count).Select(i => new AssetRowViewModel(new Asset("p", $"S{i}", $"Name {i}", AssetClass.Crypto), "p"))];

    /// <summary>
    /// Creates the keys of rows in the order given.
    /// </summary>
    /// <param name="rows">
    /// The rows.
    /// </param>
    /// <returns>
    /// The keys.
    /// </returns>
    private static List<string> KeysOf(IEnumerable<AssetRowViewModel> rows) => [.. rows.Select(r => TrayAssetSelection.Key(r.Asset))];

    /// <summary>
    /// Creates the popup list with the given instruments chosen.
    /// </summary>
    /// <param name="rows">
    /// The rows of the watchlist.
    /// </param>
    /// <param name="chosen">
    /// The chosen keys in display order; by default none.
    /// </param>
    /// <returns>
    /// The popup list.
    /// </returns>
    private static TrayPopupViewModel Model(ObservableCollection<AssetRowViewModel> rows, IReadOnlyList<string>? chosen = null)
    {
        var model = new TrayPopupViewModel(rows);
        model.SetSelection(chosen ?? []);
        return model;
    }

    /// <summary>
    /// Verifies nothing is listed until the user chooses, however long the watchlist is.
    /// </summary>
    [Fact]
    public void NoChoice_IsEmpty()
    {
        var model = Model(Rows(3));

        model.IsEmpty.Should().BeTrue();
        model.Items.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the tray list at its largest is listed completely.
    /// </summary>
    [Fact]
    public void ExactlyTheMost_ListsAll()
    {
        var rows = Rows(TrayAssetSelection.MaxAssets);
        var model = Model(rows, KeysOf(rows));

        model.Items.Should().Equal(rows);
    }

    /// <summary>
    /// Verifies the list follows the watchlist.
    /// </summary>
    [Fact]
    public void RowsChanged_UpdatesTheList()
    {
        var rows = Rows(2);
        var model = Model(rows, KeysOf(rows));

        rows.RemoveAt(0);

        model.Items.Should().Equal(rows);
    }

    /// <summary>
    /// Verifies a choice lists only the chosen instruments, in the chosen order.
    /// </summary>
    [Fact]
    public void Selection_ListsTheChosenInTheirOrder()
    {
        var rows = Rows(4);
        var model = Model(rows);

        model.SetSelection([TrayAssetSelection.Key("p", "S3"), TrayAssetSelection.Key("p", "S1")]);

        model.Items.Should().Equal(rows[2], rows[0]);
    }

    /// <summary>
    /// Verifies emptying the choice shows the empty state again.
    /// </summary>
    [Fact]
    public void Selection_Emptied_ShowsTheEmptyState()
    {
        var rows = Rows(2);
        var model = Model(rows, KeysOf(rows));
        model.IsEmpty.Should().BeFalse();

        model.SetSelection([]);

        model.IsEmpty.Should().BeTrue();
        model.Items.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a row that is added or removed and not chosen leaves the list alone, and one that is chosen is picked up.
    /// </summary>
    [Fact]
    public void RowsChanged_OnlyChosenRowsMatter()
    {
        var rows = Rows(3);
        var model = Model(rows, [TrayAssetSelection.Key("p", "S2")]);
        model.Items.Should().Equal(rows[1]);

        rows.Move(0, 2);
        rows.RemoveAt(rows.Count - 1);
        model.Items.Should().Equal(rows.Single(r => r.Asset.Symbol == "S2"));

        var chosen = rows.Single(r => r.Asset.Symbol == "S2");
        rows.Remove(chosen);
        model.Items.Should().BeEmpty();

        rows.Add(chosen);
        model.Items.Should().Equal(chosen);
    }

    /// <summary>
    /// Verifies the same selection again changes nothing.
    /// </summary>
    [Fact]
    public void SetSelection_Same_KeepsTheItems()
    {
        var rows = Rows(2);
        var model = Model(rows, KeysOf(rows));
        var before = model.Items.ToList();

        model.SetSelection(KeysOf(rows));

        model.Items.Should().Equal(before);
    }
}
