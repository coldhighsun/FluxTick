using System.Collections.ObjectModel;
using FluxTick.Abstractions;
using FluxTick.App.ViewModels;

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
    /// Verifies an empty watchlist is reported as empty.
    /// </summary>
    [Fact]
    public void Empty_IsEmpty()
    {
        var model = new TrayPopupViewModel(Rows(0));

        model.IsEmpty.Should().BeTrue();
        model.Items.Should().BeEmpty();
        model.HasMore.Should().BeFalse();
    }

    /// <summary>
    /// Verifies exactly the most that is listed leaves nothing out.
    /// </summary>
    [Fact]
    public void ExactlyMaxItems_HasNoMore()
    {
        var model = new TrayPopupViewModel(Rows(TrayPopupViewModel.MaxItems));

        model.Items.Should().HaveCount(TrayPopupViewModel.MaxItems);
        model.HasMore.Should().BeFalse();
        model.MoreText.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies more than the most is cut and the rest is counted.
    /// </summary>
    [Fact]
    public void MoreThanMaxItems_ListsTheFirstAndCountsTheRest()
    {
        var rows = Rows(TrayPopupViewModel.MaxItems + 3);
        var model = new TrayPopupViewModel(rows);

        model.Items.Should().Equal(rows.Take(TrayPopupViewModel.MaxItems));
        model.HasMore.Should().BeTrue();
        model.MoreText.Should().Contain("3");
    }

    /// <summary>
    /// Verifies the list follows the watchlist.
    /// </summary>
    [Fact]
    public void RowsChanged_UpdatesTheList()
    {
        var rows = Rows(2);
        var model = new TrayPopupViewModel(rows);

        rows.RemoveAt(0);

        model.Items.Should().Equal(rows);
    }
}
