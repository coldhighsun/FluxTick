using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxTick.App.Localization;
using FluxTick.App.Services;
using FluxTick.Core.Application;

namespace FluxTick.App.ViewModels;

/// <summary>
/// The tray list as the main window's menus see it: which rows are on it, whether it is full, and the command that puts
/// a row on it or takes it off. The list itself is a setting; this follows it and the platform's support for it.
/// </summary>
public sealed partial class TrayListViewModel : LocalizedViewModel
{
    /// <summary>
    /// The settings store.
    /// </summary>
    private readonly SettingsManager _settings;

    /// <summary>
    /// The rows of the watchlist, which show whether they are on the tray list.
    /// </summary>
    private readonly ObservableCollection<AssetRowViewModel> _rows;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayListViewModel"/> class.
    /// </summary>
    /// <param name="settings">
    /// The settings store.
    /// </param>
    /// <param name="rows">
    /// The rows of the watchlist.
    /// </param>
    /// <param name="ui">
    /// The dispatcher of the UI thread.
    /// </param>
    public TrayListViewModel(SettingsManager settings, ObservableCollection<AssetRowViewModel> rows, IUiDispatcher ui)
        : base(ui)
    {
        _settings = settings;
        _rows = rows;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the tray list holds as many instruments as it can, so no more can be added.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    [NotifyPropertyChangedFor(nameof(AddTip))]
    public partial bool IsFull { get; set; }

    /// <summary>
    /// Gets the explanation shown on the disabled "Add to tray list" menu item, or <see langword="null"/> while it can be
    /// used.
    /// </summary>
    public string? AddTip => IsFull ? Loc.Get("Watch_TrayFull", TrayAssetSelection.MaxAssets) : null;

    /// <summary>
    /// Marks the rows that are on the tray list and tells whether the list is full. Call after the settings or the rows
    /// changed.
    /// </summary>
    public void Refresh()
    {
        var trayAssets = _settings.Current.General.TrayAssets;
        foreach (var row in _rows)
        {
            row.InTray = trayAssets.Count > 0 && trayAssets.Contains(TrayAssetSelection.Key(row.Asset));
        }

        IsFull = trayAssets.Count >= TrayAssetSelection.MaxAssets;
    }

    /// <summary>
    /// Adds an instrument to the end of the tray list, or takes it off when it is already there.
    /// </summary>
    /// <param name="row">
    /// The row to add or take off.
    /// </param>
    /// <returns>
    /// A task that completes once the tray list is saved.
    /// </returns>
    [RelayCommand(CanExecute = nameof(CanToggle))]
    public Task ToggleAsync(AssetRowViewModel? row)
    {
        if (row is null)
        {
            return Task.CompletedTask;
        }

        var key = TrayAssetSelection.Key(row.Asset);

        // A full list takes nothing more; the menu offers no adding then, so this only guards a stale menu.
        return _settings.TryUpdateAsync(s => TrayAssetSelection.Toggle(s.General.TrayAssets, key));
    }

    /// <summary>
    /// Tells whether the tray list takes the command: an instrument can always be taken off, but only added while there
    /// is room, so the menu item is disabled when the list is full.
    /// </summary>
    /// <param name="row">
    /// The row the command is for.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the command can run.
    /// </returns>
    private bool CanToggle(AssetRowViewModel? row) => row is null || row.InTray || !IsFull;

    /// <inheritdoc />
    protected override void OnLanguageChanged() => OnPropertyChanged(nameof(AddTip));
}
