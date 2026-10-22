using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FluxTick.Abstractions;
using FluxTick.App.Services;
using FluxTick.Core.Application;

namespace FluxTick.App.ViewModels;

/// <summary>
/// The editor of one provider option, generated from its <see cref="ProviderOptionDefinition"/>.
/// </summary>
public sealed partial class OptionViewModel : ObservableObject
{
    /// <summary>
    /// Called when the user committed a valid value.
    /// </summary>
    private readonly Action _committed;

    /// <summary>
    /// Suppresses commits while values are set programmatically.
    /// </summary>
    private bool _loading;

    /// <summary>
    /// Initializes a new instance of the <see cref="OptionViewModel"/> class.
    /// </summary>
    /// <param name="definition">
    /// The option definition.
    /// </param>
    /// <param name="stored">
    /// The stored value, or <see langword="null"/> to show the default.
    /// </param>
    /// <param name="committed">
    /// The callback invoked when the user commits a valid value.
    /// </param>
    public OptionViewModel(ProviderOptionDefinition definition, string? stored, Action committed)
    {
        Definition = definition;
        _committed = committed;
        Load(stored);
    }

    /// <summary>
    /// Gets the option definition.
    /// </summary>
    public ProviderOptionDefinition Definition { get; }

    /// <summary>
    /// Gets the label.
    /// </summary>
    public string Label => Definition.Label;

    /// <summary>
    /// Gets the help text, or an empty string.
    /// </summary>
    public string Description => Definition.Description ?? string.Empty;

    /// <summary>
    /// Gets the unit suffix shown next to the label.
    /// </summary>
    public string Unit => Definition.Type == ProviderOptionType.Duration ? " (seconds)" : string.Empty;

    /// <summary>
    /// Gets a value indicating whether the option is edited as free text or a number.
    /// </summary>
    public bool IsText => Definition.Type is ProviderOptionType.String or ProviderOptionType.Secret or ProviderOptionType.Int or ProviderOptionType.Duration;

    /// <summary>
    /// Gets the password character for secret options, or <see langword="null"/>.
    /// </summary>
    public char PasswordChar => Definition.Type == ProviderOptionType.Secret ? '•' : '\0';

    /// <summary>
    /// Gets a value indicating whether the option is a switch.
    /// </summary>
    public bool IsBool => Definition.Type == ProviderOptionType.Bool;

    /// <summary>
    /// Gets a value indicating whether the option is a pick list.
    /// </summary>
    public bool IsChoice => Definition.Type == ProviderOptionType.Choice;

    /// <summary>
    /// Gets the selectable values of a choice option.
    /// </summary>
    public IReadOnlyList<string> Choices => Definition.Choices ?? [];

    /// <summary>
    /// Gets a value indicating whether there is help text to show.
    /// </summary>
    public bool HasDescription => Description.Length > 0;

    /// <summary>
    /// Gets or sets the text of a text, number or duration option (durations are shown in seconds).
    /// </summary>
    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the value of a switch option.
    /// </summary>
    [ObservableProperty]
    public partial bool BoolValue { get; set; }

    /// <summary>
    /// Gets or sets the selected value of a choice option.
    /// </summary>
    [ObservableProperty]
    public partial string? SelectedChoice { get; set; }

    /// <summary>
    /// Gets or sets the validation error, or an empty string.
    /// </summary>
    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    /// <summary>
    /// Gets the current value in its stored text form, or <see langword="null"/> when the input is invalid.
    /// </summary>
    public string? StoredValue
    {
        get
        {
            string stored;
            switch (Definition.Type)
            {
                case ProviderOptionType.Bool:
                    stored = BoolValue ? "true" : "false";
                    break;
                case ProviderOptionType.Choice:
                    stored = SelectedChoice ?? string.Empty;
                    break;
                case ProviderOptionType.Duration:
                    if (!double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                        || seconds is <= 0 or > 86400)
                    {
                        return null;
                    }

                    stored = TimeSpan.FromSeconds(seconds).ToString("c", CultureInfo.InvariantCulture);
                    break;
                default:
                    stored = Text;
                    break;
            }

            return Definition.Validate(stored) is null ? stored : null;
        }
    }

    /// <summary>
    /// Sets the editor from a stored value without committing.
    /// </summary>
    /// <param name="stored">
    /// The stored value, or <see langword="null"/> to show the default.
    /// </param>
    public void Load(string? stored)
    {
        _loading = true;
        try
        {
            var value = stored ?? Definition.DefaultValue;
            switch (Definition.Type)
            {
                case ProviderOptionType.Bool:
                    BoolValue = bool.TryParse(value, out var flag) && flag;
                    break;
                case ProviderOptionType.Choice:
                    SelectedChoice = value;
                    break;
                case ProviderOptionType.Duration:
                    Text = TimeSpan.TryParseExact(value, "c", CultureInfo.InvariantCulture, out var span)
                        ? span.TotalSeconds.ToString(CultureInfo.InvariantCulture)
                        : value;
                    break;
                default:
                    Text = value;
                    break;
            }

            Error = string.Empty;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Validates a text edit and commits it when valid.
    /// </summary>
    /// <param name="value">
    /// The new text.
    /// </param>
    partial void OnTextChanged(string value) => Commit();

    /// <summary>
    /// Commits a switch change.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnBoolValueChanged(bool value) => Commit();

    /// <summary>
    /// Commits a pick list change.
    /// </summary>
    /// <param name="value">
    /// The new selection.
    /// </param>
    partial void OnSelectedChoiceChanged(string? value) => Commit();

    /// <summary>
    /// Validates the current input and notifies the owner when valid.
    /// </summary>
    private void Commit()
    {
        if (_loading)
        {
            return;
        }

        if (StoredValue is null)
        {
            Error = Definition.Type == ProviderOptionType.Duration
                ? "Enter a number of seconds."
                : Definition.Validate(Text) ?? "Invalid value.";
            return;
        }

        Error = string.Empty;
        _committed();
    }
}

/// <summary>
/// The settings entry of one data source: enable switch, options and live status.
/// </summary>
public sealed partial class ProviderSettingsViewModel : ObservableObject
{
    /// <summary>
    /// Called when the enabled state or an option changed.
    /// </summary>
    private readonly Action<ProviderSettingsViewModel> _changed;

    /// <summary>
    /// Suppresses change notifications while loading.
    /// </summary>
    private bool _loading;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderSettingsViewModel"/> class.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    /// <param name="changed">
    /// The callback invoked when the user changes something.
    /// </param>
    public ProviderSettingsViewModel(ProviderDescriptor descriptor, Action<ProviderSettingsViewModel> changed)
    {
        Descriptor = descriptor;
        _changed = changed;
        Options = new ObservableCollection<OptionViewModel>(
            descriptor.OptionDefinitions.Select(d => new OptionViewModel(d, null, () => _changed(this))));
    }

    /// <summary>
    /// Gets the provider descriptor.
    /// </summary>
    public ProviderDescriptor Descriptor { get; }

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string Name => Descriptor.DisplayName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public string Description => Descriptor.Description;

    /// <summary>
    /// Gets a summary line of the mode and supported asset classes.
    /// </summary>
    public string Summary =>
        $"{(Descriptor.UpdateMode == ProviderUpdateMode.Streaming ? "Streaming" : "Polling")} · {string.Join(", ", Descriptor.SupportedAssetClasses)}";

    /// <summary>
    /// Gets the option editors.
    /// </summary>
    public ObservableCollection<OptionViewModel> Options { get; }

    /// <summary>
    /// Gets a value indicating whether the provider has options.
    /// </summary>
    public bool HasOptions => Options.Count > 0;

    /// <summary>
    /// Gets or sets a value indicating whether the provider is in use.
    /// </summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the status text.
    /// </summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = "Off";

    /// <summary>
    /// Gets or sets a value indicating whether the status is good.
    /// </summary>
    [ObservableProperty]
    public partial bool StatusGood { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the status needs attention.
    /// </summary>
    [ObservableProperty]
    public partial bool StatusBad { get; set; }

    /// <summary>
    /// Loads enabled state and option values from settings without raising a change.
    /// </summary>
    /// <param name="settings">
    /// The settings to read.
    /// </param>
    public void Load(AppSettings settings)
    {
        _loading = true;
        try
        {
            Enabled = settings.IsProviderEnabled(Descriptor);
            settings.Providers.TryGetValue(Descriptor.Id, out var stored);
            foreach (var option in Options)
            {
                string? value = null;
                stored?.Options.TryGetValue(option.Definition.Key, out value);
                option.Load(value);
            }
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Collects the valid option values for persistence.
    /// </summary>
    /// <returns>
    /// The option values keyed by option key.
    /// </returns>
    public Dictionary<string, string> CollectOptions()
    {
        var values = new Dictionary<string, string>();
        foreach (var option in Options)
        {
            if (option.StoredValue is { } value)
            {
                values[option.Definition.Key] = value;
            }
        }

        return values;
    }

    /// <summary>
    /// Updates the live status shown to the user.
    /// </summary>
    /// <param name="status">
    /// The provider status, or <see langword="null"/> when the provider is not running.
    /// </param>
    public void UpdateStatus(ProviderStatus? status)
    {
        (StatusText, StatusGood, StatusBad) = status switch
        {
            null => ("Off", false, false),
            { State: ProviderConnectionState.Connected } => ("Connected", true, false),
            { State: ProviderConnectionState.Idle } => ("Idle", false, false),
            { State: ProviderConnectionState.Reconnecting } s => (s.Message is { Length: > 0 } m ? $"Reconnecting: {m}" : "Reconnecting", false, false),
            { State: ProviderConnectionState.Failed } s => (s.Message is { Length: > 0 } m ? $"Error: {m}" : "Error", false, true),
            _ => ("Unknown", false, false),
        };
    }

    /// <summary>
    /// Notifies the owner when the user toggles the provider.
    /// </summary>
    /// <param name="value">
    /// The new enabled state.
    /// </param>
    partial void OnEnabledChanged(bool value)
    {
        if (!_loading)
        {
            _changed(this);
        }
    }
}

/// <summary>
/// The routing preference of one asset class: which data source new instruments of that class use by default.
/// </summary>
public sealed partial class RoutingRowViewModel : ObservableObject
{
    /// <summary>
    /// Called when the user picked another data source.
    /// </summary>
    private readonly Action<RoutingRowViewModel> _changed;

    /// <summary>
    /// Suppresses change notifications while refreshing.
    /// </summary>
    private bool _loading;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoutingRowViewModel"/> class.
    /// </summary>
    /// <param name="assetClass">
    /// The asset class the row configures.
    /// </param>
    /// <param name="changed">
    /// The callback invoked when the user changes the preference.
    /// </param>
    public RoutingRowViewModel(AssetClass assetClass, Action<RoutingRowViewModel> changed)
    {
        AssetClass = assetClass;
        _changed = changed;
    }

    /// <summary>
    /// Gets the asset class.
    /// </summary>
    public AssetClass AssetClass { get; }

    /// <summary>
    /// Gets the label of the asset class.
    /// </summary>
    public string Label => AssetClass switch
    {
        AssetClass.Equity => "Equities & ETFs",
        AssetClass.Index => "Indices",
        AssetClass.Commodity => "Commodities",
        _ => AssetClass.ToString(),
    };

    /// <summary>
    /// Gets the enabled providers that support the class, best first.
    /// </summary>
    public ObservableCollection<ProviderDescriptor> Providers { get; } = [];

    /// <summary>
    /// Gets a value indicating whether at least one provider is available.
    /// </summary>
    public bool HasProviders => Providers.Count > 0;

    /// <summary>
    /// Gets or sets the preferred provider.
    /// </summary>
    [ObservableProperty]
    public partial ProviderDescriptor? Selected { get; set; }

    /// <summary>
    /// Rebuilds the list of available providers.
    /// </summary>
    /// <param name="available">
    /// The usable providers, best first.
    /// </param>
    public void Refresh(IReadOnlyList<ProviderDescriptor> available)
    {
        _loading = true;
        try
        {
            Providers.Clear();
            foreach (var descriptor in available)
            {
                Providers.Add(descriptor);
            }

            Selected = available.FirstOrDefault();
            OnPropertyChanged(nameof(HasProviders));
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Notifies the owner when the user picks another provider.
    /// </summary>
    /// <param name="value">
    /// The new selection.
    /// </param>
    partial void OnSelectedChanged(ProviderDescriptor? value)
    {
        if (!_loading && value is not null)
        {
            _changed(this);
        }
    }
}

/// <summary>
/// The settings page: appearance, data sources and per-asset-class routing. Every change is applied and persisted
/// immediately.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>
    /// The settings store.
    /// </summary>
    private readonly SettingsManager _settings;

    /// <summary>
    /// The router that lists usable providers per class.
    /// </summary>
    private readonly ProviderRouter _router;

    /// <summary>
    /// Saves the opacity once the slider stops moving.
    /// </summary>
    private readonly Debouncer _opacitySave;

    /// <summary>
    /// Suppresses saves while values are loaded programmatically.
    /// </summary>
    private bool _loading;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
    /// </summary>
    /// <param name="settings">
    /// The settings store.
    /// </param>
    /// <param name="registry">
    /// The available providers.
    /// </param>
    /// <param name="router">
    /// The provider router.
    /// </param>
    /// <param name="market">
    /// The market data service, used for live provider status.
    /// </param>
    /// <param name="ui">
    /// The UI thread dispatcher.
    /// </param>
    /// <param name="timeProvider">
    /// The clock used for debouncing.
    /// </param>
    public SettingsViewModel(
        SettingsManager settings,
        ProviderRegistry registry,
        ProviderRouter router,
        MarketDataService market,
        IUiDispatcher ui,
        TimeProvider timeProvider)
    {
        _settings = settings;
        _router = router;
        _opacitySave = new Debouncer(TimeSpan.FromMilliseconds(400), timeProvider);
        Market = market;
        Providers = new ObservableCollection<ProviderSettingsViewModel>(
            registry.Descriptors.Select(d => new ProviderSettingsViewModel(d, p => _ = SaveProviderAsync(p))));
        Routing = new ObservableCollection<RoutingRowViewModel>(
            Enum.GetValues<AssetClass>().Select(c => new RoutingRowViewModel(c, r => _ = SaveRoutingAsync(r))));
        market.ProviderStatusChanged += (_, _) => ui.Post(RefreshStatuses);
        Reload();
    }

    /// <summary>
    /// Gets the market data service, used to look up provider status.
    /// </summary>
    private MarketDataService Market { get; }

    /// <summary>
    /// Gets the selectable color conventions.
    /// </summary>
    public IReadOnlyList<ColorConvention> ColorConventions { get; } = Enum.GetValues<ColorConvention>();

    /// <summary>
    /// Gets the data source entries.
    /// </summary>
    public ObservableCollection<ProviderSettingsViewModel> Providers { get; }

    /// <summary>
    /// Gets the routing rows, one per asset class.
    /// </summary>
    public ObservableCollection<RoutingRowViewModel> Routing { get; }

    /// <summary>
    /// Gets or sets the background opacity between 0.3 and 1.
    /// </summary>
    [ObservableProperty]
    public partial double Opacity { get; set; } = 0.92;

    /// <summary>
    /// Gets or sets a value indicating whether the window stays on top.
    /// </summary>
    [ObservableProperty]
    public partial bool AlwaysOnTop { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether rows hide their sparkline.
    /// </summary>
    [ObservableProperty]
    public partial bool Compact { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the window cannot be dragged.
    /// </summary>
    [ObservableProperty]
    public partial bool Locked { get; set; }

    /// <summary>
    /// Gets or sets the color convention.
    /// </summary>
    [ObservableProperty]
    public partial ColorConvention SelectedColorConvention { get; set; }

    /// <summary>
    /// Reloads every control from the persisted settings. Call when the page is opened.
    /// </summary>
    public void Reload()
    {
        _loading = true;
        try
        {
            var current = _settings.Current;
            Opacity = current.Appearance.Opacity;
            AlwaysOnTop = current.Appearance.AlwaysOnTop;
            Compact = current.Appearance.Compact;
            Locked = current.Appearance.Locked;
            SelectedColorConvention = current.Appearance.ColorConvention;
            foreach (var provider in Providers)
            {
                provider.Load(current);
            }
        }
        finally
        {
            _loading = false;
        }

        RefreshRouting();
        RefreshStatuses();
    }

    /// <summary>
    /// Saves the opacity after the slider settled.
    /// </summary>
    /// <param name="value">
    /// The new opacity.
    /// </param>
    partial void OnOpacityChanged(double value)
    {
        if (!_loading)
        {
            _opacitySave.Run(() => _settings.UpdateAsync(s => s.Appearance.Opacity = value));
        }
    }

    /// <summary>
    /// Saves the always-on-top flag.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnAlwaysOnTopChanged(bool value) => SaveAppearance(s => s.Appearance.AlwaysOnTop = value);

    /// <summary>
    /// Saves the compact flag.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnCompactChanged(bool value) => SaveAppearance(s => s.Appearance.Compact = value);

    /// <summary>
    /// Saves the locked flag.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnLockedChanged(bool value) => SaveAppearance(s => s.Appearance.Locked = value);

    /// <summary>
    /// Saves the color convention.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnSelectedColorConventionChanged(ColorConvention value) => SaveAppearance(s => s.Appearance.ColorConvention = value);

    /// <summary>
    /// Persists an appearance change unless values are being loaded.
    /// </summary>
    /// <param name="change">
    /// The change to apply.
    /// </param>
    private void SaveAppearance(Action<AppSettings> change)
    {
        if (!_loading)
        {
            _ = _settings.UpdateAsync(change);
        }
    }

    /// <summary>
    /// Persists a provider's enabled state and options, then refreshes dependent views.
    /// </summary>
    /// <param name="provider">
    /// The provider entry that changed.
    /// </param>
    /// <returns>
    /// A task that completes once the change is saved.
    /// </returns>
    private async Task SaveProviderAsync(ProviderSettingsViewModel provider)
    {
        var id = provider.Descriptor.Id;
        var enabled = provider.Enabled;
        var options = provider.CollectOptions();
        await _settings.UpdateAsync(s =>
        {
            if (!s.Providers.TryGetValue(id, out var entry))
            {
                entry = new ProviderSettings();
                s.Providers[id] = entry;
            }

            entry.Enabled = enabled;
            entry.Options = options;
        });
        RefreshRouting();
    }

    /// <summary>
    /// Persists the preferred provider of an asset class.
    /// </summary>
    /// <param name="row">
    /// The routing row that changed.
    /// </param>
    /// <returns>
    /// A task that completes once the change is saved.
    /// </returns>
    private Task SaveRoutingAsync(RoutingRowViewModel row)
    {
        var selected = row.Selected!.Id;
        var order = new List<string> { selected };
        order.AddRange(row.Providers.Select(p => p.Id).Where(id => id != selected));
        return _settings.UpdateAsync(s => s.Routing[row.AssetClass] = order);
    }

    /// <summary>
    /// Rebuilds each routing row from the enabled providers.
    /// </summary>
    private void RefreshRouting()
    {
        var current = _settings.Current;
        foreach (var row in Routing)
        {
            row.Refresh(_router.GetProviders(row.AssetClass, current));
        }
    }

    /// <summary>
    /// Refreshes the live status of every provider entry. Call after the market data configuration was applied.
    /// </summary>
    public void RefreshStatuses()
    {
        foreach (var provider in Providers)
        {
            provider.UpdateStatus(Market.GetProviderStatus(provider.Descriptor.Id));
        }
    }
}
