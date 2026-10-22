using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxTick.Abstractions;
using FluxTick.Core.Application;
using Microsoft.Extensions.Logging;

namespace FluxTick.App.ViewModels;

/// <summary>
/// The "add instrument" page. The user picks an asset class, a data source and searches for a symbol.
/// The same page re-targets an existing instrument to another data source.
/// </summary>
public sealed partial class AddAssetViewModel : ObservableObject
{
    /// <summary>
    /// How long to wait after the last keystroke before searching.
    /// </summary>
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// The settings store.
    /// </summary>
    private readonly SettingsManager _settings;

    /// <summary>
    /// The market data service used for searching.
    /// </summary>
    private readonly MarketDataService _market;

    /// <summary>
    /// The router that lists usable providers per class.
    /// </summary>
    private readonly ProviderRouter _router;

    /// <summary>
    /// The clock used for debouncing.
    /// </summary>
    private readonly TimeProvider _time;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<AddAssetViewModel> _logger;

    /// <summary>
    /// Cancels the search in flight.
    /// </summary>
    private CancellationTokenSource? _searchCts;

    /// <summary>
    /// The instrument being re-targeted, or <see langword="null"/> when adding.
    /// </summary>
    private Asset? _replacing;

    /// <summary>
    /// Suppresses searches while several inputs are set at once.
    /// </summary>
    private bool _resetting;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddAssetViewModel"/> class.
    /// </summary>
    /// <param name="settings">
    /// The settings store.
    /// </param>
    /// <param name="market">
    /// The market data service.
    /// </param>
    /// <param name="router">
    /// The provider router.
    /// </param>
    /// <param name="time">
    /// The clock used for debouncing.
    /// </param>
    /// <param name="logger">
    /// The logger.
    /// </param>
    public AddAssetViewModel(
        SettingsManager settings,
        MarketDataService market,
        ProviderRouter router,
        TimeProvider time,
        ILogger<AddAssetViewModel> logger)
    {
        _settings = settings;
        _market = market;
        _router = router;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Raised when the page is finished (added, replaced or cancelled) and the watchlist should be shown again.
    /// </summary>
    public event EventHandler? Completed;

    /// <summary>
    /// Gets the selectable asset classes.
    /// </summary>
    public IReadOnlyList<AssetClass> AssetClasses { get; } = Enum.GetValues<AssetClass>();

    /// <summary>
    /// Gets the data sources that can serve the selected asset class, best first.
    /// </summary>
    public ObservableCollection<ProviderDescriptor> AvailableProviders { get; } = [];

    /// <summary>
    /// Gets the search results.
    /// </summary>
    public ObservableCollection<Asset> Results { get; } = [];

    /// <summary>
    /// Gets or sets the selected asset class.
    /// </summary>
    [ObservableProperty]
    public partial AssetClass SelectedAssetClass { get; set; } = AssetClass.Equity;

    /// <summary>
    /// Gets or sets the selected data source.
    /// </summary>
    [ObservableProperty]
    public partial ProviderDescriptor? SelectedProvider { get; set; }

    /// <summary>
    /// Gets or sets the search text.
    /// </summary>
    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether a search is running.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSearching { get; set; }

    /// <summary>
    /// Gets or sets the message shown below the search box, or an empty string.
    /// </summary>
    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the page title.
    /// </summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "Add instrument";

    /// <summary>
    /// Gets or sets a value indicating whether the selected data source can search its catalog.
    /// </summary>
    [ObservableProperty]
    public partial bool CanSearch { get; set; }

    /// <summary>
    /// Gets a value indicating whether the asset class can be changed (not when re-targeting an instrument).
    /// </summary>
    public bool CanChangeClass => _replacing is null;

    /// <summary>
    /// Prepares the page for adding a new instrument.
    /// </summary>
    public void BeginAdd()
    {
        _replacing = null;
        Title = "Add instrument";
        Start(preferredClass: SelectedAssetClass, query: string.Empty, avoidProviderId: null);
    }

    /// <summary>
    /// Prepares the page for moving an existing instrument to another data source.
    /// </summary>
    /// <param name="asset">
    /// The instrument to re-target.
    /// </param>
    public void BeginReplace(Asset asset)
    {
        _replacing = asset;
        Title = $"Change source · {asset.Symbol}";
        Start(asset.AssetClass, asset.DisplayName, asset.ProviderId);
    }

    /// <summary>
    /// Adds (or re-targets to) the chosen instrument and returns to the watchlist.
    /// </summary>
    /// <param name="asset">
    /// The instrument to add.
    /// </param>
    /// <returns>
    /// A task that completes once the watchlist is saved.
    /// </returns>
    [RelayCommand]
    public async Task AddAsync(Asset? asset)
    {
        if (asset is null)
        {
            return;
        }

        var replacing = _replacing;
        var duplicate = _settings.Current.Watchlist.Any(e => e.ProviderId == asset.ProviderId && e.Symbol == asset.Symbol);
        if (duplicate && replacing is null)
        {
            Message = $"{asset.Symbol} is already on your list.";
            return;
        }

        if (replacing is null || replacing.Id != asset.Id)
        {
            await _settings.UpdateAsync(s =>
            {
                var entry = WatchlistEntry.From(asset);
                s.Watchlist.RemoveAll(e => e.ProviderId == asset.ProviderId && e.Symbol == asset.Symbol);
                var index = replacing is null
                    ? -1
                    : s.Watchlist.FindIndex(e => e.ProviderId == replacing.ProviderId && e.Symbol == replacing.Symbol);
                if (index < 0)
                {
                    s.Watchlist.Add(entry);
                }
                else
                {
                    s.Watchlist[index] = entry;
                }
            });
        }

        Completed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds the typed text as a symbol for data sources that cannot search.
    /// </summary>
    /// <returns>
    /// A task that completes once the watchlist is saved.
    /// </returns>
    [RelayCommand]
    public Task AddTypedAsync()
    {
        var text = Query.Trim();
        if (text.Length == 0 || SelectedProvider is null)
        {
            return Task.CompletedTask;
        }

        return AddAsync(new Asset(SelectedProvider.Id, text.ToUpperInvariant(), text.ToUpperInvariant(), SelectedAssetClass));
    }

    /// <summary>
    /// Leaves the page without changes.
    /// </summary>
    [RelayCommand]
    public void Cancel() => Completed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Refreshes the provider list and restarts the search when the asset class changes.
    /// </summary>
    /// <param name="value">
    /// The new asset class.
    /// </param>
    partial void OnSelectedAssetClassChanged(AssetClass value)
    {
        if (_resetting)
        {
            return;
        }

        LoadProviders(value, avoidProviderId: null);
        ScheduleSearch();
    }

    /// <summary>
    /// Restarts the search when the data source changes.
    /// </summary>
    /// <param name="value">
    /// The new data source.
    /// </param>
    partial void OnSelectedProviderChanged(ProviderDescriptor? value)
    {
        if (!_resetting)
        {
            ScheduleSearch();
        }
    }

    /// <summary>
    /// Restarts the search when the text changes.
    /// </summary>
    /// <param name="value">
    /// The new text.
    /// </param>
    partial void OnQueryChanged(string value)
    {
        if (!_resetting)
        {
            ScheduleSearch();
        }
    }

    /// <summary>
    /// Resets the page and starts a search for the initial query.
    /// </summary>
    /// <param name="preferredClass">
    /// The asset class to preselect.
    /// </param>
    /// <param name="query">
    /// The initial search text.
    /// </param>
    /// <param name="avoidProviderId">
    /// A provider that should not be preselected when an alternative exists.
    /// </param>
    private void Start(AssetClass preferredClass, string query, string? avoidProviderId)
    {
        _resetting = true;
        try
        {
            Results.Clear();
            Message = string.Empty;
            SelectedAssetClass = preferredClass;
            LoadProviders(preferredClass, avoidProviderId);
            Query = query;
        }
        finally
        {
            _resetting = false;
        }

        OnPropertyChanged(nameof(CanChangeClass));
        ScheduleSearch();
    }

    /// <summary>
    /// Fills the provider list for an asset class and preselects the best one.
    /// </summary>
    /// <param name="assetClass">
    /// The asset class.
    /// </param>
    /// <param name="avoidProviderId">
    /// A provider that should not be preselected when an alternative exists.
    /// </param>
    private void LoadProviders(AssetClass assetClass, string? avoidProviderId)
    {
        var providers = _router.GetProviders(assetClass, _settings.Current);
        AvailableProviders.Clear();
        foreach (var provider in providers)
        {
            AvailableProviders.Add(provider);
        }

        SelectedProvider = providers.FirstOrDefault(p => p.Id != avoidProviderId) ?? providers.FirstOrDefault();
    }

    /// <summary>
    /// Starts a debounced search for the current inputs, cancelling the previous one.
    /// </summary>
    private void ScheduleSearch()
    {
        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _ = SearchAsync(cts.Token);
    }

    /// <summary>
    /// Runs the search after the debounce delay and shows the results.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token cancelled when newer input supersedes this search.
    /// </param>
    /// <returns>
    /// A task that completes when the search finished or was superseded.
    /// </returns>
    private async Task SearchAsync(CancellationToken cancellationToken)
    {
        var provider = SelectedProvider;
        var query = Query.Trim();
        var assetClass = SelectedAssetClass;

        Results.Clear();
        Message = string.Empty;
        CanSearch = provider is not null && _market.SupportsSearch(provider.Id);

        if (provider is null)
        {
            Message = "No data source is enabled for this asset class. Enable one in Settings.";
            return;
        }

        if (query.Length == 0)
        {
            Message = CanSearch ? "Type a name or symbol to search." : $"Type the {provider.DisplayName} symbol, then press Add.";
            return;
        }

        if (!CanSearch)
        {
            Message = $"{provider.DisplayName} cannot search. Press Add to use \"{query.ToUpperInvariant()}\" as typed.";
            return;
        }

        try
        {
            await Task.Delay(SearchDelay, _time, cancellationToken);
            IsSearching = true;
            var found = await _market.SearchAsync(provider.Id, query, assetClass, cancellationToken);
            Results.Clear();
            foreach (var asset in found)
            {
                Results.Add(asset);
            }

            Message = found.Count == 0 ? "No matches." : string.Empty;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Search on {Provider} failed", provider.Id);
            Message = $"Search failed: {ex.Message}";
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsSearching = false;
            }
        }
    }
}
