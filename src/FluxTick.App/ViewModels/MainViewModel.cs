using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxTick.Abstractions;
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
/// The root view model: owns the watchlist rows, navigation and the connection between settings and market data.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
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
    /// The UI thread dispatcher.
    /// </summary>
    private readonly IUiDispatcher _ui;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<MainViewModel> _logger;

    /// <summary>
    /// Serializes applying settings to the market data service.
    /// </summary>
    private readonly SemaphoreSlim _applyLock = new(1, 1);

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
    {
        _settings = settings;
        _market = market;
        _registry = registry;
        _ui = ui;
        _logger = logger;
        AddAsset = addAsset;
        Settings = settingsPage;

        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.Compact))
            {
                ApplyCompact();
            }
        };
    }

    /// <summary>
    /// Gets the watchlist rows in display order.
    /// </summary>
    public ObservableCollection<AssetRowViewModel> Rows { get; } = [];

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
        _market.SnapshotChanged += (_, snapshot) => _ui.Post(() => OnSnapshot(snapshot));
        _market.ProviderStatusChanged += (_, _) => _ui.Post(RefreshChips);
        _settings.Changed += (_, _) => _ = ApplySettingsAsync();
        _logger.LogInformation("FluxTick started with {Count} instruments", _settings.Current.Watchlist.Count);
        await ApplySettingsAsync();
    }

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
            _ui.Post(() =>
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
        var wanted = _settings.Current.Watchlist.Select(e => e.ToAsset()).ToList();

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
                row = new AssetRowViewModel(asset, ProviderName(asset.ProviderId)) { ShowSparkline = !Settings.Compact };
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

            if (snapshots.TryGetValue(asset.Id, out var snapshot))
            {
                row.Update(snapshot);
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
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
                null => $"{chip.Name}: off",
                { Message: { Length: > 0 } message } => $"{chip.Name}: {status.State} — {message}",
                _ => $"{chip.Name}: {status.State}",
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
    private string ProviderName(string providerId) => _registry.GetDescriptor(providerId)?.DisplayName ?? providerId;
}
