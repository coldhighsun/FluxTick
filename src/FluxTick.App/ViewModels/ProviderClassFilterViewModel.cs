using CommunityToolkit.Mvvm.ComponentModel;
using FluxTick.Abstractions;
using FluxTick.App.Localization;

namespace FluxTick.App.ViewModels;

/// <summary>
/// One choice of the asset class filter above the data source list: an asset class, or all of them.
/// </summary>
public sealed partial class ProviderClassFilterViewModel : ObservableObject
{
    /// <summary>
    /// Called when the choice is checked or unchecked.
    /// </summary>
    private readonly Action<ProviderClassFilterViewModel> _changed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderClassFilterViewModel"/> class.
    /// </summary>
    /// <param name="assetClass">
    /// The asset class to show, or <see langword="null"/> for all of them.
    /// </param>
    /// <param name="changed">
    /// The callback invoked when the choice is checked or unchecked.
    /// </param>
    public ProviderClassFilterViewModel(AssetClass? assetClass, Action<ProviderClassFilterViewModel> changed)
    {
        AssetClass = assetClass;
        _changed = changed;
    }

    /// <summary>
    /// Gets the asset class to show, or <see langword="null"/> for all of them.
    /// </summary>
    public AssetClass? AssetClass { get; }

    /// <summary>
    /// Gets the label of the choice.
    /// </summary>
    public string Label => AssetClass is { } assetClass ? EnumLabel.Of(assetClass) : Loc.Get("Settings_FilterAll");

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
