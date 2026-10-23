using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxTick.Abstractions;
using FluxTick.App.Localization;
using FluxTick.App.Services;
using FluxTick.Core.Application;
using Microsoft.Extensions.Logging;

namespace FluxTick.App.ViewModels;

/// <summary>
/// The modal window opened from the main window.
/// </summary>
public enum ToolWindowKind
{
    /// <summary>
    /// No tool window is open.
    /// </summary>
    None,

    /// <summary>
    /// The settings window.
    /// </summary>
    Settings,

    /// <summary>
    /// The add instrument / change source window.
    /// </summary>
    AddAsset,
}

/// <summary>
/// A data source chip in the footer showing its live status.
/// </summary>
/// <param name="id">
/// The provider id.
/// </param>
/// <param name="name">
/// The display name.
/// </param>
public sealed partial class ProviderChipViewModel(string id, string name) : ObservableObject
{
    /// <summary>
    /// Gets the provider id.
    /// </summary>
    public string Id { get; } = id;

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets or sets the tooltip describing the status.
    /// </summary>
    [ObservableProperty]
    public partial string Tooltip { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the provider delivers data.
    /// </summary>
    [ObservableProperty]
    public partial bool IsGood { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the provider has a problem.
    /// </summary>
    [ObservableProperty]
    public partial bool IsBad { get; set; }
}

/// <summary>
/// A target of the "Move to group" menu.
/// </summary>
/// <param name="GroupId">
/// The group id.
/// </param>
/// <param name="Title">
/// The text shown in the menu.
/// </param>
/// <param name="IsCurrent">
/// Whether the instrument is in this group already.
/// </param>
public sealed record GroupChoice(string GroupId, string Title, bool IsCurrent);

/// <summary>
/// The root view model: owns the watchlist rows, navigation and the connection between settings and market data.
/// </summary>
public sealed partial class MainViewModel : LocalizedViewModel
{
    /// <summary>
    /// The settings store.
    /// </summary>
    private readonly SettingsManager _settings;

    /// <summary>
    /// The market data service.
    /// </summary>
    private readonly MarketDataService _market;

    /// <summary>
    /// The available providers.
    /// </summary>
    private readonly ProviderRegistry _registry;

    /// <summary>
    /// Hands the prices to the UI thread, keeping only the latest one of each instrument while it is busy.
    /// </summary>
    private readonly SnapshotCoalescer _snapshots;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<MainViewModel> _logger;

    /// <summary>
    /// Serializes applying settings to the market data service.
    /// </summary>
    private readonly SemaphoreSlim _applyLock = new(1, 1);

    /// <summary>
    /// Whether the sections are being rebuilt, so that collapse changes made by the rebuild itself are not saved again.
    /// </summary>
    private bool _rebuildingSections;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
    /// <param name="settings">
    /// The settings store.
    /// </param>
    /// <param name="market">
    /// The market data service.
    /// </param>
    /// <param name="registry">
    /// The available providers.
    /// </param>
    /// <param name="ui">
    /// The UI thread dispatcher.
    /// </param>
    /// <param name="addAsset">
    /// The add instrument page.
    /// </param>
    /// <param name="settingsPage">
    /// The settings page.
    /// </param>
    /// <param name="logger">
    /// The logger.
    /// </param>
    public MainViewModel(
        SettingsManager settings,
        MarketDataService market,
        ProviderRegistry registry,
        IUiDispatcher ui,
        AddAssetViewModel addAsset,
        SettingsViewModel settingsPage,
        ILogger<MainViewModel> logger)
        : base(ui)
    {
        _settings = settings;
        _market = market;
        _registry = registry;
        _snapshots = new SnapshotCoalescer(ui, OnSnapshot, ex => logger.LogError(ex, "Could not hand a price update to the UI"));
        _logger = logger;
        AddAsset = addAsset;
        Settings = settingsPage;

        // The window sizes itself after the layout, so the layout must be right before the first rows exist.
        IsCardLayout = settings.Current.Appearance.Layout == WatchlistLayout.Cards;
        Grouping = settings.Current.Appearance.Grouping;

        Settings.PropertyChanged += OnSettingsPagePropertyChanged;
    }

    /// <summary>
    /// Gets the watchlist rows in display order.
    /// </summary>
    public ObservableCollection<AssetRowViewModel> Rows { get; } = [];

    /// <summary>
    /// Gets the sections of the card layout. They hold the same row view models as <see cref="Rows"/>, grouped
    /// according to the grouping setting.
    /// </summary>
    public ObservableCollection<WatchSectionViewModel> Sections { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the watchlist is shown as cards instead of a list.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LayoutToggleTip))]
    [NotifyPropertyChangedFor(nameof(ShowNewGroupSlot))]
    [NotifyPropertyChangedFor(nameof(ListItems))]
    public partial bool IsCardLayout { get; private set; }

    /// <summary>
    /// Gets the rows the list shows: all of them in the list layout, none in the card layout. A list without items
    /// builds no views, so the list that is hidden behind the cards does not take part in every price update.
    /// </summary>
    public IReadOnlyList<AssetRowViewModel> ListItems => IsCardLayout ? [] : Rows;

    /// <summary>
    /// Gets a value indicating whether the card layout shows the placeholder that creates a new group: only with
    /// the custom grouping, where the groups are visible.
    /// </summary>
    public bool ShowNewGroupSlot => IsCardLayout && Grouping == CardGrouping.Custom;

    /// <summary>
    /// Gets the tooltip of the layout button, which says what the next click switches to.
    /// </summary>
    public string LayoutToggleTip => Loc.Get(IsCardLayout ? "Main_SwitchToList" : "Main_SwitchToCards");

    /// <summary>
    /// Gets how the cards are grouped.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNewGroupSlot))]
    public partial CardGrouping Grouping { get; private set; }

    /// <summary>
    /// Gets the grouping that was active before creating a group switched to the custom grouping automatically, or
    /// <see langword="null"/> when no such switch is pending. The view offers to undo the switch while it is set.
    /// </summary>
    [ObservableProperty]
    public partial CardGrouping? GroupingSwitchedFrom { get; private set; }

    /// <summary>
    /// Gets the footer chips of the data sources in use.
    /// </summary>
    public ObservableCollection<ProviderChipViewModel> Chips { get; } = [];

    /// <summary>
    /// Gets the add instrument page.
    /// </summary>
    public AddAssetViewModel AddAsset { get; }

    /// <summary>
    /// Gets the settings page.
    /// </summary>
    public SettingsViewModel Settings { get; }

    /// <summary>
    /// Gets the modal tool window that is currently open. The view reports it through
    /// <see cref="BeginToolWindow"/> and <see cref="EndToolWindow"/>.
    /// </summary>
    [ObservableProperty]
    public partial ToolWindowKind ActiveTool { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a modal tool window (settings or add instrument) is open.
    /// </summary>
    public bool IsToolWindowOpen => ActiveTool != ToolWindowKind.None;

    /// <summary>
    /// Gets a value indicating whether the settings window is open.
    /// </summary>
    public bool IsSettingsOpen => ActiveTool == ToolWindowKind.Settings;

    /// <summary>
    /// Raised when the settings window should be shown.
    /// </summary>
    public event EventHandler? SettingsRequested;

    /// <summary>
    /// Raised when the window for adding an instrument or changing its source should be shown.
    /// </summary>
    public event EventHandler? AddAssetRequested;

    /// <summary>
    /// Gets a value indicating whether the watchlist is empty and a hint should be shown.
    /// </summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>
    /// Applies the persisted settings, shows the watchlist and starts reacting to changes.
    /// </summary>
    /// <returns>
    /// A task that completes once the first configuration is active.
    /// </returns>
    public async Task InitializeAsync()
    {
        _market.SnapshotChanged += OnMarketSnapshotChanged;
        _market.ProviderStatusChanged += OnProviderStatusChanged;
        _settings.Changed += OnSettingsChanged;
        _logger.LogInformation("FluxTick started with {Count} instruments", _settings.Current.Watchlist.Count);
        await ApplySettingsAsync();
    }

    /// <inheritdoc />
    protected override void Detach()
    {
        Settings.PropertyChanged -= OnSettingsPagePropertyChanged;
        _market.SnapshotChanged -= OnMarketSnapshotChanged;
        _market.ProviderStatusChanged -= OnProviderStatusChanged;
        _settings.Changed -= OnSettingsChanged;
    }

    /// <summary>
    /// Applies the settings that change what the watchlist shows when the settings page changes them.
    /// </summary>
    /// <param name="sender">
    /// The settings page.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnSettingsPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.Compact))
        {
            ApplyCompact();
        }
        else if (e.PropertyName == nameof(SettingsViewModel.FlashOnUpdate))
        {
            ApplyFlash();
        }
        else if (e.PropertyName is nameof(SettingsViewModel.SelectedLayout) or nameof(SettingsViewModel.SelectedGrouping))
        {
            RebuildSections();
        }
    }

    /// <summary>
    /// Hands a new snapshot to the coalescer, which applies it on the UI thread.
    /// </summary>
    /// <param name="sender">
    /// The market data service.
    /// </param>
    /// <param name="snapshot">
    /// The new snapshot.
    /// </param>
    private void OnMarketSnapshotChanged(object? sender, AssetSnapshot snapshot) => _snapshots.Submit(snapshot);

    /// <summary>
    /// Refreshes the footer chips when a data source changes its state.
    /// </summary>
    /// <param name="sender">
    /// The market data service.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnProviderStatusChanged(object? sender, ProviderStatusChangedEventArgs e) => Ui.Post(RefreshChips);

    /// <summary>
    /// Applies the settings again after they changed.
    /// </summary>
    /// <param name="sender">
    /// The settings store.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnSettingsChanged(object? sender, EventArgs e) => _ = ApplySettingsAsync();

    /// <summary>
    /// Notifies bindings of the derived flags when the open tool window changes.
    /// </summary>
    /// <param name="value">
    /// The new tool window.
    /// </param>
    partial void OnActiveToolChanged(ToolWindowKind value)
    {
        OnPropertyChanged(nameof(IsToolWindowOpen));
        OnPropertyChanged(nameof(IsSettingsOpen));
    }

    /// <summary>
    /// Requests the add instrument window, which is shown by the view.
    /// </summary>
    [RelayCommand]
    public void ShowAdd()
    {
        if (IsToolWindowOpen)
        {
            return;
        }

        AddAsset.BeginAdd();
        AddAssetRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Requests the settings window, which is shown by the view.
    /// </summary>
    [RelayCommand]
    public void ShowSettings()
    {
        if (IsToolWindowOpen)
        {
            return;
        }

        Settings.Reload();
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Toggles the always-on-top flag.
    /// </summary>
    [RelayCommand]
    public void TogglePin() => Settings.AlwaysOnTop = !Settings.AlwaysOnTop;

    /// <summary>
    /// Toggles whether the window position is locked against dragging.
    /// </summary>
    [RelayCommand]
    public void ToggleLock() => Settings.Locked = !Settings.Locked;

    /// <summary>
    /// Toggles compact mode, which hides the sparklines and makes the cards smaller.
    /// </summary>
    [RelayCommand]
    public void ToggleCompact() => Settings.Compact = !Settings.Compact;

    /// <summary>
    /// Toggles the flash of a row when its price changes.
    /// </summary>
    [RelayCommand]
    public void ToggleFlash() => Settings.FlashOnUpdate = !Settings.FlashOnUpdate;

    /// <summary>
    /// Requests the window that moves an instrument to another data source.
    /// </summary>
    /// <param name="row">
    /// The row to re-target.
    /// </param>
    [RelayCommand]
    public void ChangeSource(AssetRowViewModel? row)
    {
        if (row is null || IsToolWindowOpen)
        {
            return;
        }

        AddAsset.BeginReplace(row.Asset);
        AddAssetRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Records that a modal tool window is about to be shown, so further requests are ignored until it is closed.
    /// </summary>
    /// <param name="kind">
    /// The window that is opened.
    /// </param>
    public void BeginToolWindow(ToolWindowKind kind) => ActiveTool = kind;

    /// <summary>
    /// Records that the modal tool window is gone.
    /// </summary>
    public void EndToolWindow() => ActiveTool = ToolWindowKind.None;

    /// <summary>
    /// Removes an instrument from the watchlist.
    /// </summary>
    /// <param name="row">
    /// The row to remove.
    /// </param>
    /// <returns>
    /// A task that completes once the watchlist is saved.
    /// </returns>
    [RelayCommand]
    public Task RemoveAsync(AssetRowViewModel? row) =>
        row is null
            ? Task.CompletedTask
            : _settings.UpdateAsync(s => s.Watchlist.RemoveAll(e => e.ProviderId == row.Asset.ProviderId && e.Symbol == row.Asset.Symbol));

    /// <summary>
    /// Moves an instrument to an absolute position, for example after a drag and drop.
    /// </summary>
    /// <param name="row">
    /// The row to move.
    /// </param>
    /// <param name="newIndex">
    /// The zero-based target position; values outside the list are clamped to its ends.
    /// </param>
    /// <returns>
    /// A task that completes once the watchlist is saved.
    /// </returns>
    public Task MoveToAsync(AssetRowViewModel? row, int newIndex)
    {
        if (row is null)
        {
            return Task.CompletedTask;
        }

        // Move the row right away so the UI does not snap back while the settings round-trip completes;
        // SyncRows finds the rows already in order afterwards.
        var current = Rows.IndexOf(row);
        if (current >= 0)
        {
            Rows.Move(current, Math.Clamp(newIndex, 0, Rows.Count - 1));
        }

        return _settings.UpdateAsync(s =>
        {
            var index = s.Watchlist.FindIndex(e => e.ProviderId == row.Asset.ProviderId && e.Symbol == row.Asset.Symbol);
            if (index < 0)
            {
                return;
            }

            // The entry exists, so the list is not empty and the clamp range is valid.
            var target = Math.Clamp(newIndex, 0, s.Watchlist.Count - 1);
            if (target == index)
            {
                return;
            }

            var entry = s.Watchlist[index];
            s.Watchlist.RemoveAt(index);
            s.Watchlist.Insert(target, entry);
        });
    }

    /// <summary>
    /// Moves a card to a position of a section, for example after a drag and drop in the card layout. Dropping into
    /// a group section assigns the instrument to that group.
    /// </summary>
    /// <param name="row">
    /// The row to move.
    /// </param>
    /// <param name="target">
    /// The section to drop into.
    /// </param>
    /// <param name="indexInSection">
    /// The zero-based position among the other cards of the target section; values past the end append the card.
    /// </param>
    /// <returns>
    /// A task that completes once the watchlist is saved.
    /// </returns>
    public Task MoveCardAsync(AssetRowViewModel? row, WatchSectionViewModel? target, int indexInSection) =>
        row is null || target is null
            ? Task.CompletedTask
            : MoveAsync(row, target.Kind, target.AssetClass, target.GroupId, target.ProviderId, indexInSection);

    /// <summary>
    /// Moves a section to a position among the sections of its kind, for example after its header was dragged.
    /// Only user-defined groups and asset class sections can be moved.
    /// </summary>
    /// <param name="section">
    /// The section to move.
    /// </param>
    /// <param name="newIndex">
    /// The zero-based target position among the sections of the same kind; values outside the list are clamped to
    /// its ends.
    /// </param>
    /// <returns>
    /// A task that completes once the new order is saved.
    /// </returns>
    public Task MoveSectionAsync(WatchSectionViewModel? section, int newIndex)
    {
        Task task;
        switch (section)
        {
            case { Kind: WatchSectionKind.Group, GroupId: { } id }:
                task = _settings.UpdateAsync(s => WatchlistReorder.MoveGroup(s.Groups, id, newIndex));
                break;
            case { Kind: WatchSectionKind.Type, AssetClass: { } type }:
                var visible = Sections.Where(s => s.Kind == WatchSectionKind.Type).Select(s => s.AssetClass!.Value).ToList();
                task = _settings.UpdateAsync(s =>
                {
                    var order = s.Appearance.EffectiveTypeOrder();
                    if (WatchlistReorder.MoveType(order, type, visible, newIndex))
                    {
                        s.Appearance.TypeOrder = order;
                    }
                });
                break;
            default:
                return Task.CompletedTask;
        }

        // Show the new order right away; see MoveAsync.
        SyncRows();
        return task;
    }

    /// <summary>
    /// Lists the groups an instrument can be moved to, for the "Move to group" menu.
    /// </summary>
    /// <param name="row">
    /// The row that is about to be moved.
    /// </param>
    /// <returns>
    /// One choice per user-defined group.
    /// </returns>
    public IReadOnlyList<GroupChoice> GetGroupChoices(AssetRowViewModel row) =>
        [.. _settings.Current.Groups.Select(g => new GroupChoice(g.Id, g.Name, g.Id == row.GroupId))];

    /// <summary>
    /// Assigns an instrument to a group, placing it after the instruments already in that group.
    /// </summary>
    /// <param name="row">
    /// The row to assign.
    /// </param>
    /// <param name="groupId">
    /// The id of the group.
    /// </param>
    /// <returns>
    /// A task that completes once the watchlist is saved.
    /// </returns>
    public Task MoveToGroupAsync(AssetRowViewModel? row, string groupId) =>
        row is null
            ? Task.CompletedTask
            : MoveAsync(row, WatchSectionKind.Group, null, groupId, null, int.MaxValue);

    /// <summary>
    /// Switches between the list and the card layout.
    /// </summary>
    /// <returns>
    /// A task that completes once the setting is saved.
    /// </returns>
    [RelayCommand]
    public Task ToggleLayoutAsync()
    {
        // The next layout is decided on the saved settings, not on what is shown: a click that arrives while the
        // previous change is still being saved must flip that change, not repeat it.
        var task = _settings.UpdateAsync(s => s.Appearance.Layout = s.Appearance.Layout == WatchlistLayout.Cards ? WatchlistLayout.List : WatchlistLayout.Cards);
        RebuildSections();
        return task;
    }

    /// <summary>
    /// Sets how the watchlist is laid out and shows the result right away.
    /// </summary>
    /// <param name="layout">
    /// The new layout.
    /// </param>
    /// <returns>
    /// A task that completes once the setting is saved.
    /// </returns>
    [RelayCommand]
    public Task SetLayoutAsync(WatchlistLayout layout)
    {
        var task = _settings.UpdateAsync(s => s.Appearance.Layout = layout);

        // The update publishes the new settings before it first yields, so the view can switch immediately.
        RebuildSections();
        return task;
    }

    /// <summary>
    /// Sets how the cards are grouped and shows the result right away.
    /// </summary>
    /// <param name="grouping">
    /// The new grouping.
    /// </param>
    /// <returns>
    /// A task that completes once the setting is saved.
    /// </returns>
    [RelayCommand]
    public Task SetGroupingAsync(CardGrouping grouping)
    {
        // A grouping the user picked is final, so an earlier "switched automatically" hint no longer applies.
        GroupingSwitchedFrom = null;
        var task = _settings.UpdateAsync(s => s.Appearance.Grouping = grouping);
        RebuildSections();
        return task;
    }

    /// <summary>
    /// Creates a user-defined group, optionally moves an instrument into it, and shows the groups by switching to the
    /// custom grouping when another one is active, because the new group would not be visible otherwise.
    /// </summary>
    /// <param name="name">
    /// The typed name of the group.
    /// </param>
    /// <param name="rowToMove">
    /// The instrument to put into the new group, or <see langword="null"/> to create an empty group.
    /// </param>
    /// <returns>
    /// The id of the new group, or <see langword="null"/> when the name is blank and nothing was created.
    /// </returns>
    public async Task<string?> CreateGroupAsync(string? name, AssetRowViewModel? rowToMove = null)
    {
        if (!WatchGroup.TryNormalizeName(name, out var clean))
        {
            return null;
        }

        var group = new WatchGroup { Name = clean };
        var previous = _settings.Current.Appearance.Grouping;
        var switched = previous != CardGrouping.Custom;
        var task = _settings.UpdateAsync(s =>
        {
            s.Groups.Add(group);
            s.Appearance.Grouping = CardGrouping.Custom;
            if (rowToMove is not null)
            {
                s.Watchlist.Find(e => e.ProviderId == rowToMove.Asset.ProviderId && e.Symbol == rowToMove.Asset.Symbol)?.GroupId = group.Id;
            }
        });

        GroupingSwitchedFrom = switched ? previous : null;

        // Rows pick up their new group and the sections follow, before the settings round-trip completes.
        SyncRows();
        await task;
        return group.Id;
    }

    /// <summary>
    /// Goes back to the grouping that was active before creating a group switched to the custom grouping. The new
    /// group stays.
    /// </summary>
    /// <returns>
    /// A task that completes once the setting is saved.
    /// </returns>
    [RelayCommand]
    public Task UndoGroupingSwitchAsync() =>
        GroupingSwitchedFrom is { } previous ? SetGroupingAsync(previous) : Task.CompletedTask;

    /// <summary>
    /// Hides the hint about the automatic switch to the custom grouping without changing anything.
    /// </summary>
    [RelayCommand]
    public void DismissGroupingHint() => GroupingSwitchedFrom = null;

    /// <summary>
    /// Deletes a user-defined group. Only a group without instruments is deleted, and the last group stays; nothing
    /// happens otherwise.
    /// </summary>
    /// <param name="section">
    /// The section of the group.
    /// </param>
    /// <returns>
    /// A task that completes once the change is saved.
    /// </returns>
    [RelayCommand]
    public Task DeleteGroupAsync(WatchSectionViewModel? section)
    {
        if (section is not { Kind: WatchSectionKind.Group, GroupId: { } id })
        {
            return Task.CompletedTask;
        }

        // The check is repeated on the settings themselves, so a view that is out of date cannot delete a group that
        // received an instrument in the meantime.
        var task = _settings.UpdateAsync(s =>
        {
            if (s.Groups.Count > 1 && s.Watchlist.TrueForAll(e => e.GroupId != id))
            {
                s.Groups.RemoveAll(g => g.Id == id);
            }
        });
        RebuildSections();
        return task;
    }

    /// <summary>
    /// Renames a user-defined group. A blank name is rejected and the group keeps its name.
    /// </summary>
    /// <param name="groupId">
    /// The id of the group.
    /// </param>
    /// <param name="name">
    /// The typed new name.
    /// </param>
    /// <returns>
    /// A task that completes once the change is saved.
    /// </returns>
    public Task RenameGroupAsync(string groupId, string? name)
    {
        if (!WatchGroup.TryNormalizeName(name, out var clean))
        {
            // Shows the old name again where the view displayed the rejected text.
            RebuildSections();
            return Task.CompletedTask;
        }

        var task = _settings.UpdateAsync(s => s.Groups.Find(g => g.Id == groupId)?.Name = clean);
        RebuildSections();
        return task;
    }

    /// <summary>
    /// Moves an instrument into a section of the watchlist and shows the result right away.
    /// </summary>
    /// <param name="row">
    /// The row to move.
    /// </param>
    /// <param name="kind">
    /// The kind of the target section.
    /// </param>
    /// <param name="assetClass">
    /// The asset class of a type section.
    /// </param>
    /// <param name="groupId">
    /// The group id of a group section.
    /// </param>
    /// <param name="providerId">
    /// The provider id of a provider section.
    /// </param>
    /// <param name="index">
    /// The position among the other members of the section.
    /// </param>
    /// <returns>
    /// A task that completes once the watchlist is saved.
    /// </returns>
    private Task MoveAsync(AssetRowViewModel row, WatchSectionKind kind, AssetClass? assetClass, string? groupId, string? providerId, int index)
    {
        var task = _settings.UpdateAsync(s =>
        {
            var entry = s.Watchlist.Find(e => e.ProviderId == row.Asset.ProviderId && e.Symbol == row.Asset.Symbol);
            if (entry is null)
            {
                return;
            }

            Func<WatchlistEntry, bool> inSection;
            switch (kind)
            {
                case WatchSectionKind.Type:
                    if (entry.AssetClass != assetClass)
                    {
                        return;
                    }

                    inSection = e => e.AssetClass == assetClass;
                    break;
                case WatchSectionKind.Provider:
                    // A data source is not a choice made here, so a card can only be reordered inside its own section.
                    if (entry.ProviderId != providerId)
                    {
                        return;
                    }

                    inSection = e => e.ProviderId == providerId;
                    break;
                case WatchSectionKind.Group:
                    if (s.Groups.TrueForAll(g => g.Id != groupId))
                    {
                        return;
                    }

                    entry.GroupId = groupId;
                    inSection = e => e.GroupId == groupId;
                    break;
                default:
                    inSection = _ => true;
                    break;
            }

            WatchlistReorder.Move(s.Watchlist, entry, inSection, index);
        });

        // The update publishes the new settings before it first yields, so the cards can follow immediately
        // instead of snapping back until the settings round-trip completes.
        SyncRows();
        return task;
    }

    /// <summary>
    /// Pushes the current settings into the market data service and then refreshes the rows.
    /// </summary>
    /// <returns>
    /// A task that completes once the configuration is applied.
    /// </returns>
    private async Task ApplySettingsAsync()
    {
        await _applyLock.WaitAsync();
        try
        {
            await _market.ApplyAsync(_settings.Current);
            Ui.Post(() =>
            {
                SyncRows();
                RefreshChips();
                Settings.RefreshStatuses();
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Applying settings failed");
        }
        finally
        {
            _applyLock.Release();
        }
    }

    /// <summary>
    /// Aligns the rows with the persisted watchlist: adds, removes and reorders rows and refreshes their values.
    /// </summary>
    private void SyncRows()
    {
        var snapshots = _market.GetSnapshots().ToDictionary(s => s.Asset.Id);
        var entries = _settings.Current.Watchlist;
        var wanted = entries.Select(e => e.ToAsset()).ToList();

        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (!wanted.Any(a => a.Id == Rows[i].Asset.Id))
            {
                Rows.RemoveAt(i);
            }
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            var asset = wanted[i];
            var row = Rows.FirstOrDefault(r => r.Asset.Id == asset.Id);
            if (row is null)
            {
                row = new AssetRowViewModel(asset, ProviderName(asset.ProviderId))
                {
                    ShowSparkline = !Settings.Compact,
                    FlashEnabled = Settings.FlashOnUpdate,
                };
                Rows.Insert(i, row);
            }
            else
            {
                var current = Rows.IndexOf(row);
                if (current != i)
                {
                    Rows.Move(current, i);
                }
            }

            row.GroupId = entries[i].GroupId;

            if (snapshots.TryGetValue(asset.Id, out var snapshot))
            {
                row.Update(snapshot);
            }
        }

        RebuildSections();
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// Rebuilds the sections of the card layout from the rows and the layout settings. Sections and cards that
    /// are unchanged are kept, so a rebuild caused by an unrelated setting does not recreate them.
    /// </summary>
    private void RebuildSections()
    {
        var settings = _settings.Current;
        var appearance = settings.Appearance;
        IsCardLayout = appearance.Layout == WatchlistLayout.Cards;
        Grouping = appearance.Grouping;

        _rebuildingSections = true;
        try
        {
            var desired = new List<WatchSectionViewModel>();

            // The list layout does not use sections, so no card views are created for them.
            switch (IsCardLayout ? appearance.Grouping : (CardGrouping?)null)
            {
                case null:
                    break;
                case CardGrouping.ByType:
                    foreach (var type in appearance.EffectiveTypeOrder())
                    {
                        var items = Rows.Where(r => r.Asset.AssetClass == type).ToList();
                        if (items.Count > 0)
                        {
                            desired.Add(PrepareSection(WatchSectionKind.Type, type, null, EnumLabel.Of(type), appearance.CollapsedTypes.Contains(type), items));
                        }
                    }

                    break;
                case CardGrouping.ByProvider:
                    // Sections follow the order in which the data sources first appear on the watchlist.
                    foreach (var providerId in Rows.Select(r => r.Asset.ProviderId).Distinct())
                    {
                        var items = Rows.Where(r => r.Asset.ProviderId == providerId).ToList();
                        desired.Add(PrepareSection(WatchSectionKind.Provider, null, null, ProviderName(providerId), appearance.CollapsedProviders.Contains(providerId), items, providerId));
                    }

                    break;
                case CardGrouping.Custom:
                    // The settings keep every instrument in a group, so the groups cover all the rows.
                    foreach (var group in settings.Groups)
                    {
                        var items = Rows.Where(r => r.GroupId == group.Id).ToList();
                        desired.Add(PrepareSection(WatchSectionKind.Group, null, group.Id, group.Name, group.Collapsed, items));
                    }

                    break;
                default:
                    desired.Add(PrepareSection(WatchSectionKind.All, null, null, string.Empty, false, [.. Rows]));
                    break;
            }

            SyncCollection(Sections, desired);
        }
        finally
        {
            _rebuildingSections = false;
        }
    }

    /// <summary>
    /// Finds or creates the section with the given identity and fills it.
    /// </summary>
    /// <param name="kind">
    /// What the section collects.
    /// </param>
    /// <param name="assetClass">
    /// The asset class of a type section.
    /// </param>
    /// <param name="groupId">
    /// The group id of a group section.
    /// </param>
    /// <param name="title">
    /// The header text.
    /// </param>
    /// <param name="collapsed">
    /// Whether the section is collapsed.
    /// </param>
    /// <param name="items">
    /// The cards of the section in display order.
    /// </param>
    /// <param name="providerId">
    /// The provider id of a provider section.
    /// </param>
    /// <returns>
    /// The section.
    /// </returns>
    private WatchSectionViewModel PrepareSection(
        WatchSectionKind kind,
        AssetClass? assetClass,
        string? groupId,
        string title,
        bool collapsed,
        List<AssetRowViewModel> items,
        string? providerId = null)
    {
        var key = WatchSectionViewModel.KeyOf(kind, assetClass, groupId, providerId);
        var section = Sections.FirstOrDefault(s => s.Key == key);
        if (section is null)
        {
            section = new WatchSectionViewModel(kind, assetClass, groupId, providerId);
            section.PropertyChanged += OnSectionPropertyChanged;
        }

        section.Title = title;
        section.IsCollapsed = collapsed;
        SyncCollection(section.Items, items);
        section.Count = items.Count;
        if (kind == WatchSectionKind.Group)
        {
            section.DeleteBlockedReason = items.Count > 0 ? Loc.Get("Section_MoveInstrumentsFirst")
                : _settings.Current.Groups.Count <= 1 ? Loc.Get("Section_KeepOneGroup")
                : string.Empty;
        }

        return section;
    }

    /// <summary>
    /// Makes a collection hold the desired items in the desired order using as few changes as possible.
    /// </summary>
    /// <typeparam name="T">
    /// The item type.
    /// </typeparam>
    /// <param name="target">
    /// The collection to update.
    /// </param>
    /// <param name="desired">
    /// The items it should hold.
    /// </param>
    private static void SyncCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
        where T : class
    {
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var current = target.IndexOf(desired[i]);
            if (current < 0)
            {
                target.Insert(i, desired[i]);
            }
            else if (current != i)
            {
                target.Move(current, i);
            }
        }
    }

    /// <summary>
    /// Saves the collapsed state when the user collapses or expands a section.
    /// </summary>
    /// <param name="sender">
    /// The section.
    /// </param>
    /// <param name="e">
    /// The property change data.
    /// </param>
    private void OnSectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_rebuildingSections && e.PropertyName == nameof(WatchSectionViewModel.IsCollapsed) && sender is WatchSectionViewModel section)
        {
            _ = SaveCollapsedAsync(section);
        }
    }

    /// <summary>
    /// Persists whether a section is collapsed.
    /// </summary>
    /// <param name="section">
    /// The section whose state changed.
    /// </param>
    /// <returns>
    /// A task that completes once the setting is saved.
    /// </returns>
    private Task SaveCollapsedAsync(WatchSectionViewModel section)
    {
        var collapsed = section.IsCollapsed;
        return _settings.UpdateAsync(s =>
        {
            switch (section.Kind)
            {
                case WatchSectionKind.Type when section.AssetClass is { } type:
                    s.Appearance.CollapsedTypes.RemoveAll(t => t == type);
                    if (collapsed)
                    {
                        s.Appearance.CollapsedTypes.Add(type);
                    }

                    break;
                case WatchSectionKind.Provider when section.ProviderId is { } providerId:
                    s.Appearance.CollapsedProviders.RemoveAll(p => p == providerId);
                    if (collapsed)
                    {
                        s.Appearance.CollapsedProviders.Add(providerId);
                    }

                    break;
                case WatchSectionKind.Group:
                    s.Groups.Find(g => g.Id == section.GroupId)?.Collapsed = collapsed;
                    break;
            }
        });
    }

    /// <summary>
    /// Applies a snapshot to the matching row.
    /// </summary>
    /// <param name="snapshot">
    /// The snapshot.
    /// </param>
    private void OnSnapshot(AssetSnapshot snapshot) =>
        Rows.FirstOrDefault(r => r.Asset.Id == snapshot.Asset.Id)?.Update(snapshot);

    /// <summary>
    /// Shows or hides the sparkline of every row according to the compact setting.
    /// </summary>
    private void ApplyCompact()
    {
        foreach (var row in Rows)
        {
            row.ShowSparkline = !Settings.Compact;
        }
    }

    /// <summary>
    /// Enables or disables the price-change flash of every row according to the flash setting.
    /// </summary>
    private void ApplyFlash()
    {
        foreach (var row in Rows)
        {
            row.FlashEnabled = Settings.FlashOnUpdate;
        }
    }

    /// <inheritdoc />
    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(LayoutToggleTip));
        foreach (var row in Rows)
        {
            row.RefreshLanguage(ProviderName(row.Asset.ProviderId));
        }

        Chips.Clear();
        RefreshChips();
        RebuildSections();
    }

    /// <summary>
    /// Rebuilds the footer chips from the providers that serve at least one watchlist instrument.
    /// </summary>
    private void RefreshChips()
    {
        var used = _settings.Current.Watchlist.Select(e => e.ProviderId).Distinct().ToList();
        for (var i = Chips.Count - 1; i >= 0; i--)
        {
            if (!used.Contains(Chips[i].Id))
            {
                Chips.RemoveAt(i);
            }
        }

        foreach (var id in used)
        {
            var chip = Chips.FirstOrDefault(c => c.Id == id);
            if (chip is null)
            {
                chip = new ProviderChipViewModel(id, ProviderName(id));
                Chips.Add(chip);
            }

            var status = _market.GetProviderStatus(id);
            chip.IsGood = status?.State == ProviderConnectionState.Connected;
            chip.IsBad = status is null || status.State == ProviderConnectionState.Failed;
            chip.Tooltip = status switch
            {
                null => Loc.Get("Chip_Off", chip.Name),
                { Message: { Length: > 0 } message } => Loc.Get("Chip_StateMessage", chip.Name, Loc.Get($"Provider_{status.State}"), message),
                _ => Loc.Get("Common_LabelValue", chip.Name, Loc.Get($"Provider_{status.State}")),
            };
        }
    }

    /// <summary>
    /// Gets the display name of a provider.
    /// </summary>
    /// <param name="providerId">
    /// The provider id.
    /// </param>
    /// <returns>
    /// The display name, or the id when the provider is unknown.
    /// </returns>
    private string ProviderName(string providerId) =>
        _registry.GetDescriptor(providerId) is { } descriptor ? ProviderText.Name(descriptor) : providerId;
}
