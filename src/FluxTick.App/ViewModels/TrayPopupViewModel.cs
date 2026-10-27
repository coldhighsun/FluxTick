using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using FluxTick.Core.Application;

namespace FluxTick.App.ViewModels;

/// <summary>
/// The list the tray popup shows: the instruments the user put on the tray list, in the order they chose.
/// </summary>
public sealed partial class TrayPopupViewModel : ObservableObject
{
    /// <summary>
    /// The rows of the watchlist.
    /// </summary>
    private readonly ObservableCollection<AssetRowViewModel> _rows;

    /// <summary>
    /// The keys of the chosen instruments in display order.
    /// </summary>
    private List<string> _selection;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayPopupViewModel"/> class.
    /// </summary>
    /// <param name="rows">
    /// The rows of the watchlist. Nothing is listed until <see cref="SetSelection"/> says which instruments.
    /// </param>
    /// <param name="main">
    /// The main view model, whose total of the holdings the popup shows, or <see langword="null"/> for none.
    /// </param>
    public TrayPopupViewModel(ObservableCollection<AssetRowViewModel> rows, MainViewModel? main = null)
    {
        _rows = rows;
        _selection = [];
        _rows.CollectionChanged += OnRowsChanged;
        if (main is not null)
        {
            _main = main;
            main.PropertyChanged += OnMainPropertyChanged;
        }

        Refresh();
    }

    /// <summary>
    /// The main view model, or <see langword="null"/> when the popup shows no total.
    /// </summary>
    private readonly MainViewModel? _main;

    /// <summary>
    /// Gets the total of the holdings in the base currency, or an empty text when there is none.
    /// </summary>
    public string PortfolioText => _main?.PortfolioText ?? string.Empty;

    /// <summary>
    /// Gets a value indicating whether the popup shows the total of the holdings.
    /// </summary>
    public bool HasPortfolio => PortfolioText.Length > 0;

    /// <summary>
    /// Gets a value indicating whether the total is a profit.
    /// </summary>
    public bool PortfolioIsProfit => _main?.PortfolioIsProfit ?? false;

    /// <summary>
    /// Gets a value indicating whether the total is a loss.
    /// </summary>
    public bool PortfolioIsLoss => _main?.PortfolioIsLoss ?? false;

    /// <summary>
    /// Passes on the change of the total of the holdings.
    /// </summary>
    /// <param name="sender">
    /// The main view model.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnMainPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.PortfolioText) or nameof(MainViewModel.PortfolioIsProfit) or nameof(MainViewModel.PortfolioIsLoss))
        {
            OnPropertyChanged(nameof(PortfolioText));
            OnPropertyChanged(nameof(HasPortfolio));
            OnPropertyChanged(nameof(PortfolioIsProfit));
            OnPropertyChanged(nameof(PortfolioIsLoss));
        }
    }

    /// <summary>
    /// Gets the instruments listed. The rows update themselves, so prices stay current while the popup is open.
    /// </summary>
    public ObservableCollection<AssetRowViewModel> Items { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the tray list is empty.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    /// <summary>
    /// Rebuilds the list. The settings keep the selection within <see cref="TrayAssetSelection.MaxAssets"/> instruments.
    /// </summary>
    public void Refresh()
    {
        var picked = TrayAssetSelection.Resolve(_rows, row => TrayAssetSelection.Key(row.Asset), _selection);
        if (!Items.SequenceEqual(picked))
        {
            Items.Clear();
            foreach (var row in picked)
            {
                Items.Add(row);
            }
        }

        IsEmpty = picked.Count == 0;
    }

    /// <summary>
    /// Sets which instruments are listed and in which order, then rebuilds the list.
    /// </summary>
    /// <param name="selection">
    /// The chosen keys in display order.
    /// </param>
    public void SetSelection(IReadOnlyList<string> selection)
    {
        // Every settings change brings the selection again, so only a real change rebuilds the list.
        if (_selection.SequenceEqual(selection))
        {
            return;
        }

        _selection = [.. selection];
        Refresh();
    }

    /// <summary>
    /// Follows the watchlist. Only rows that are, or may become, on the tray list matter: the list is in the order of
    /// the selection, so moving rows changes nothing, and the watchlist is rebuilt row by row at startup, so a change
    /// that no chosen instrument is part of must not cost a pass over all rows.
    /// </summary>
    /// <param name="sender">
    /// The rows.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var irrelevant = e.Action switch
        {
            NotifyCollectionChangedAction.Move => true,
            NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Remove =>
                !IsChosen(e.NewItems) && !IsChosen(e.OldItems),
            _ => false,
        };

        if (!irrelevant)
        {
            Refresh();
        }
    }

    /// <summary>
    /// Tells whether any of the rows is on the tray list.
    /// </summary>
    /// <param name="items">
    /// The rows of a collection change, or <see langword="null"/> for none.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when one of them is chosen.
    /// </returns>
    private bool IsChosen(System.Collections.IList? items) =>
        _selection.Count > 0 && items is not null && items.OfType<AssetRowViewModel>().Any(row => _selection.Contains(TrayAssetSelection.Key(row.Asset)));
}
