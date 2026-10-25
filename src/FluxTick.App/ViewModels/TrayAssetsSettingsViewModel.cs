using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxTick.Abstractions;
using FluxTick.App.Localization;
using FluxTick.App.Services;
using FluxTick.Core.Application;

namespace FluxTick.App.ViewModels;

/// <summary>
/// One instrument in the tray list editor of the settings page.
/// </summary>
public sealed partial class TrayAssetItemViewModel : ObservableObject
{
    /// <summary>
    /// The editor that owns the item.
    /// </summary>
    private readonly TrayAssetsSettingsViewModel _owner;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayAssetItemViewModel"/> class.
    /// </summary>
    /// <param name="owner">
    /// The editor that owns the item.
    /// </param>
    /// <param name="key">
    /// The key that identifies the instrument in the settings.
    /// </param>
    /// <param name="name">
    /// The display name.
    /// </param>
    /// <param name="symbol">
    /// The provider-native symbol.
    /// </param>
    /// <param name="providerId">
    /// The id of the data source that serves the instrument.
    /// </param>
    /// <param name="providerName">
    /// The name of the data source that serves the instrument.
    /// </param>
    public TrayAssetItemViewModel(TrayAssetsSettingsViewModel owner, string key, string name, string symbol, string providerId, string providerName)
    {
        _owner = owner;
        Key = key;
        Name = name;
        Symbol = symbol;
        ProviderId = providerId;
        ProviderName = providerName;
    }

    /// <summary>
    /// Gets the id of the data source that serves the instrument.
    /// </summary>
    public string ProviderId { get; }

    /// <summary>
    /// Gets the key that identifies the instrument in the settings.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the provider-native symbol.
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// Gets or sets the name of the data source that serves the instrument, in the language of the interface.
    /// </summary>
    [ObservableProperty]
    public partial string ProviderName { get; set; }

    /// <summary>
    /// Takes the instrument off the tray list.
    /// </summary>
    /// <returns>
    /// A task that completes once the change is saved.
    /// </returns>
    [RelayCommand]
    private Task RemoveAsync() => _owner.RemoveAsync(this);
}

/// <summary>
/// The tray list editor of the settings page: shows the instruments the tray popup lists, lets the user drag them
/// into order and take them off. Instruments are added from the menu of a row or card in the main window. Every
/// change is persisted immediately.
/// </summary>
public sealed partial class TrayAssetsSettingsViewModel : ObservableObject
{
    /// <summary>
    /// The settings store.
    /// </summary>
    private readonly SettingsManager _settings;

    /// <summary>
    /// The available providers, used to name the data source of an instrument.
    /// </summary>
    private readonly ProviderRegistry _registry;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayAssetsSettingsViewModel"/> class.
    /// </summary>
    /// <param name="settings">
    /// The settings store.
    /// </param>
    /// <param name="registry">
    /// The available providers.
    /// </param>
    public TrayAssetsSettingsViewModel(SettingsManager settings, ProviderRegistry registry)
    {
        _settings = settings;
        _registry = registry;
        Reload();
    }

    /// <summary>
    /// Gets the instruments the tray list shows, in order.
    /// </summary>
    public ObservableCollection<TrayAssetItemViewModel> Items { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the tray list is empty.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    /// <summary>
    /// Reloads the list from the persisted settings.
    /// </summary>
    public void Reload()
    {
        var current = _settings.Current;
        var chosen = TrayAssetSelection.Resolve(current.Watchlist, TrayAssetSelection.Key, current.General.TrayAssets);

        Items.Clear();
        foreach (var entry in chosen)
        {
            Items.Add(new TrayAssetItemViewModel(this, TrayAssetSelection.Key(entry), entry.DisplayName, entry.Symbol, entry.ProviderId, ProviderText.Name(_registry, entry.ProviderId)));
        }

        IsEmpty = Items.Count == 0;
    }

    /// <summary>
    /// Shows the names of the data sources in the language the interface now uses.
    /// </summary>
    public void RefreshLanguage()
    {
        foreach (var item in Items)
        {
            item.ProviderName = ProviderText.Name(_registry, item.ProviderId);
        }
    }

    /// <summary>
    /// Takes an instrument off the tray list.
    /// </summary>
    /// <param name="item">
    /// The instrument to remove.
    /// </param>
    /// <returns>
    /// A task that completes once the change is saved.
    /// </returns>
    public Task RemoveAsync(TrayAssetItemViewModel item)
    {
        var key = item.Key;
        Items.Remove(item);
        IsEmpty = Items.Count == 0;
        return _settings.TryUpdateAsync(s => TrayAssetSelection.Remove(s.General.TrayAssets, key));
    }

    /// <summary>
    /// Moves an instrument to a position in the list, for example after it was dragged there.
    /// </summary>
    /// <param name="item">
    /// The instrument to move.
    /// </param>
    /// <param name="newIndex">
    /// The zero-based target position; values outside the list are clamped to its ends.
    /// </param>
    /// <returns>
    /// A task that completes once the new order is saved.
    /// </returns>
    public Task MoveToAsync(TrayAssetItemViewModel item, int newIndex)
    {
        var current = Items.IndexOf(item);
        if (current < 0)
        {
            return Task.CompletedTask;
        }

        var target = Math.Clamp(newIndex, 0, Items.Count - 1);
        if (current == target)
        {
            return Task.CompletedTask;
        }

        var key = item.Key;
        Items.Move(current, target);
        return _settings.TryUpdateAsync(s => TrayAssetSelection.Move(s.General.TrayAssets, key, target));
    }
}
