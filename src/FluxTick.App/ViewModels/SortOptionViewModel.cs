using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxTick.Core.Application;

namespace FluxTick.App.ViewModels;

/// <summary>
/// One entry of the sort menus. The header menu and the context menu of the list show the same entries, so the menus
/// are built from these instead of being written twice.
/// </summary>
/// <param name="key">
/// The sort key the entry picks.
/// </param>
/// <param name="choose">
/// Applies the pick.
/// </param>
public sealed partial class SortOptionViewModel(WatchlistSortKey key, Func<WatchlistSortKey, Task> choose) : ObservableObject
{
    /// <summary>
    /// Gets the sort key the entry picks.
    /// </summary>
    public WatchlistSortKey Key { get; } = key;

    /// <summary>
    /// Gets the command that picks the entry.
    /// </summary>
    public IAsyncRelayCommand ChooseCommand { get; } = new AsyncRelayCommand(() => choose(key));

    /// <summary>
    /// Gets or sets the text of the entry: the name, followed by the arrow of the direction while the list is sorted by it.
    /// </summary>
    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tooltip of the entry, or <see langword="null"/> when it has none.
    /// </summary>
    [ObservableProperty]
    public partial string? Tip { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the list is ordered by the entry, for its check mark.
    /// </summary>
    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the entry can be picked.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;
}
