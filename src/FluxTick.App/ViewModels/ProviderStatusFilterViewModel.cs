using CommunityToolkit.Mvvm.ComponentModel;
using FluxTick.App.Localization;

namespace FluxTick.App.ViewModels;

/// <summary>
/// Which data sources the list shows by their state.
/// </summary>
public enum ProviderStatusFilter
{
    /// <summary>
    /// Every data source.
    /// </summary>
    All,

    /// <summary>
    /// The data sources that are switched on.
    /// </summary>
    Enabled,

    /// <summary>
    /// The data sources that are on but need a value, or report an error.
    /// </summary>
    NeedsAttention,
}

/// <summary>
/// One choice of the state filter above the data source list.
/// </summary>
public sealed partial class ProviderStatusFilterViewModel : ObservableObject
{
    /// <summary>
    /// Called when the choice is checked or unchecked.
    /// </summary>
    private readonly Action<ProviderStatusFilterViewModel> _changed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderStatusFilterViewModel"/> class.
    /// </summary>
    /// <param name="filter">
    /// The state to show.
    /// </param>
    /// <param name="changed">
    /// The callback invoked when the choice is checked or unchecked.
    /// </param>
    public ProviderStatusFilterViewModel(ProviderStatusFilter filter, Action<ProviderStatusFilterViewModel> changed)
    {
        Filter = filter;
        _changed = changed;
    }

    /// <summary>
    /// Gets the state to show.
    /// </summary>
    public ProviderStatusFilter Filter { get; }

    /// <summary>
    /// Gets the label of the choice.
    /// </summary>
    public string Label => Loc.Get(Filter switch
    {
        ProviderStatusFilter.Enabled => "Settings_FilterEnabled",
        ProviderStatusFilter.NeedsAttention => "Settings_FilterAttention",
        _ => "Settings_FilterAll",
    });

    /// <summary>
    /// Gets or sets a value indicating whether this choice is the one in effect.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>
    /// Builds the label again after the language changed.
    /// </summary>
    public void RefreshLanguage() => OnPropertyChanged(nameof(Label));

    /// <summary>
    /// Tells the owner that the choice was checked or unchecked.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnIsSelectedChanged(bool value) => _changed(this);
}
