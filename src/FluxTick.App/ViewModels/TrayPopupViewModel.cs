using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using FluxTick.App.Localization;

namespace FluxTick.App.ViewModels;

/// <summary>
/// The list the tray popup shows: the first watched instruments, with a note for the ones that did not fit.
/// </summary>
public sealed partial class TrayPopupViewModel : ObservableObject
{
    /// <summary>
    /// The most instruments the popup lists.
    /// </summary>
    public const int MaxItems = 10;

    /// <summary>
    /// The rows of the watchlist.
    /// </summary>
    private readonly ObservableCollection<AssetRowViewModel> _rows;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayPopupViewModel"/> class.
    /// </summary>
    /// <param name="rows">
    /// The rows of the watchlist, in the order the user arranged them.
    /// </param>
    public TrayPopupViewModel(ObservableCollection<AssetRowViewModel> rows)
    {
        _rows = rows;
        _rows.CollectionChanged += OnRowsChanged;
        Refresh();
    }

    /// <summary>
    /// Gets the instruments listed. The rows update themselves, so prices stay current while the popup is open.
    /// </summary>
    public ObservableCollection<AssetRowViewModel> Items { get; } = [];

    /// <summary>
    /// Gets the note about the instruments that are not listed, or an empty text when all are.
    /// </summary>
    [ObservableProperty]
    public partial string MoreText { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the watchlist is empty.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    /// <summary>
    /// Gets a value indicating whether there are instruments that are not listed.
    /// </summary>
    [ObservableProperty]
    public partial bool HasMore { get; set; }

    /// <summary>
    /// Rebuilds the list and the texts, which also picks up a changed language.
    /// </summary>
    public void Refresh()
    {
        var wanted = _rows.Take(MaxItems).ToList();
        if (!Items.SequenceEqual(wanted))
        {
            Items.Clear();
            foreach (var row in wanted)
            {
                Items.Add(row);
            }
        }

        var more = _rows.Count - wanted.Count;
        HasMore = more > 0;
        MoreText = more > 0 ? Loc.Get("TrayPopup_More", more) : string.Empty;
        IsEmpty = _rows.Count == 0;
    }

    /// <summary>
    /// Follows the watchlist.
    /// </summary>
    /// <param name="sender">
    /// The rows.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();
}
