using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxTick.Abstractions;
using FluxTick.App.Localization;
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
    /// The built-in default in the same text form as <see cref="StoredValue"/>.
    /// </summary>
    private readonly string _normalizedDefault;

    /// <summary>
    /// The data source that owns the option, which supplies the translated hint.
    /// </summary>
    private readonly ProviderDescriptor? _owner;

    /// <summary>
    /// The last valid value the editor held, kept while the user types something invalid.
    /// </summary>
    private string? _lastValid;

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
    /// <param name="owner">
    /// The data source that owns the option, used to translate its hint.
    /// </param>
    public OptionViewModel(ProviderOptionDefinition definition, string? stored, Action committed, ProviderDescriptor? owner = null)
    {
        Definition = definition;
        _owner = owner;
        _committed = committed;
        _normalizedDefault = definition.Canonicalize(definition.DefaultValue);
        Load(stored);
    }

    /// <summary>
    /// Gets the option definition.
    /// </summary>
    public ProviderOptionDefinition Definition { get; }

    /// <summary>
    /// Gets the label.
    /// </summary>
    public string Label => ProviderText.OptionLabel(Definition);

    /// <summary>
    /// Gets the help text, or an empty string.
    /// </summary>
    public string Description => (_owner is null ? Definition.Description : ProviderText.OptionDescription(_owner, Definition)) ?? string.Empty;

    /// <summary>
    /// Gets the unit suffix shown next to the label.
    /// </summary>
    public string Unit => Definition.Type == ProviderOptionType.Duration ? Loc.Get("Option_SecondsUnit") : string.Empty;

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

                    stored = ProviderOptionDefinition.FormatDuration(TimeSpan.FromSeconds(seconds));
                    break;
                case ProviderOptionType.Int:
                    // Written back in one canonical form, so '05' and ' 5' are the same value as '5'.
                    stored = Definition.Canonicalize(Text);
                    break;
                default:
                    stored = Text;
                    break;
            }

            return Definition.Validate(stored) is null ? stored : null;
        }
    }

    /// <summary>
    /// Gets the value to persist, or <see langword="null"/> when the option should not be stored because it is at the
    /// built-in default. While the input is invalid the last valid value is kept, so a half-typed edit never reverts
    /// a saved setting.
    /// </summary>
    public string? ValueToPersist
    {
        get
        {
            var value = StoredValue ?? _lastValid;
            return value is null || value == _normalizedDefault ? null : value;
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
            // Like ProviderOptions, which is what the provider runs with: an invalid stored value counts as the default.
            var value = stored is not null && Definition.Validate(stored) is null ? stored : Definition.DefaultValue;
            switch (Definition.Type)
            {
                case ProviderOptionType.Bool:
                    BoolValue = bool.TryParse(value, out var flag) && flag;
                    break;
                case ProviderOptionType.Choice:
                    SelectedChoice = value;
                    break;
                case ProviderOptionType.Duration:
                    Text = ProviderOptionDefinition.TryParseDuration(value, out var span)
                        ? span.TotalSeconds.ToString(CultureInfo.InvariantCulture)
                        : value;
                    break;
                default:
                    Text = value;
                    break;
            }

            Error = string.Empty;
            _lastValid = StoredValue;
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
    /// Builds the texts that depend on the language again after the language changed.
    /// </summary>
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Unit));
        if (Error.Length > 0 && StoredValue is null)
        {
            Error = ErrorText();
        }
    }

    /// <summary>
    /// Explains why the current input is not valid.
    /// </summary>
    /// <returns>
    /// The message in the active language.
    /// </returns>
    private string ErrorText() => Definition.Type == ProviderOptionType.Duration
        ? Loc.Get("Option_EnterSeconds")
        : Definition.Check(Text) is { } problem ? ProviderText.Validation(problem) : Loc.Get("Option_Invalid");

    /// <summary>
    /// Validates the current input and notifies the owner when valid.
    /// </summary>
    private void Commit()
    {
        if (_loading)
        {
            return;
        }

        if (StoredValue is not { } valid)
        {
            Error = ErrorText();
            return;
        }

        Error = string.Empty;
        _lastValid = valid;
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
    /// The status shown last, kept to build its text again when the language changes.
    /// </summary>
    private ProviderStatus? _status;

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
            descriptor.OptionDefinitions.Select(d => new OptionViewModel(d, null, () => _changed(this), descriptor)));
        foreach (var option in Options)
        {
            option.PropertyChanged += OnOptionPropertyChanged;
        }
    }

    /// <summary>
    /// Gets the provider descriptor.
    /// </summary>
    public ProviderDescriptor Descriptor { get; }

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string Name => ProviderText.Name(Descriptor);

    /// <summary>
    /// Gets the description.
    /// </summary>
    public string Description => ProviderText.Description(Descriptor);

    /// <summary>
    /// Gets a summary line of the mode and supported asset classes.
    /// </summary>
    public string Summary =>
        $"{Loc.Get(Descriptor.UpdateMode == ProviderUpdateMode.Streaming ? "Provider_Streaming" : "Provider_Polling")} · {string.Join(", ", Descriptor.SupportedAssetClasses.Select(c => EnumLabel.Of(c)))}";

    /// <summary>
    /// Gets the option editors.
    /// </summary>
    public ObservableCollection<OptionViewModel> Options { get; }

    /// <summary>
    /// Gets a value indicating whether the provider has options.
    /// </summary>
    public bool HasOptions => Options.Count > 0;

    /// <summary>
    /// Gets a value indicating whether an option of an enabled source currently holds an invalid value.
    /// The options of a disabled source are hidden, so their errors are not surfaced.
    /// </summary>
    public bool HasOptionError => Enabled && Options.Any(o => o.Error.Length > 0);

    /// <summary>
    /// Gets or sets a value indicating whether the provider is in use.
    /// </summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the description and options are shown.
    /// </summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>
    /// Gets or sets the status text.
    /// </summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = Loc.Get("Provider_Off");

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
    /// Puts every option of this source back to its built-in default and saves it when that changes anything.
    /// The enabled state is kept, and so are secret options such as keys, which are not settings that can be
    /// "restored" but credentials.
    /// </summary>
    [RelayCommand]
    public void RestoreDefaults()
    {
        var before = CollectOptions();
        foreach (var option in Options.Where(o => o.Definition.Type != ProviderOptionType.Secret))
        {
            option.Load(null);
        }

        var after = CollectOptions();
        if (before.Count != after.Count || before.Any(p => !after.TryGetValue(p.Key, out var value) || value != p.Value))
        {
            _changed(this);
        }
    }

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
    /// Collects the option values that differ from the built-in default for persistence.
    /// Options at their default are left out, so they keep following the default of the installed version.
    /// </summary>
    /// <returns>
    /// The option values keyed by option key.
    /// </returns>
    public Dictionary<string, string> CollectOptions()
    {
        var values = new Dictionary<string, string>();
        foreach (var option in Options)
        {
            if (option.ValueToPersist is { } value)
            {
                values[option.Definition.Key] = value;
            }
        }

        return values;
    }

    /// <summary>
    /// Builds the texts that depend on the language again after the language changed.
    /// </summary>
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Summary));
        UpdateStatus(_status);
        foreach (var option in Options)
        {
            option.RefreshLanguage();
        }
    }

    /// <summary>
    /// Updates the live status shown to the user.
    /// </summary>
    /// <param name="status">
    /// The provider status, or <see langword="null"/> when the provider is not running.
    /// </param>
    public void UpdateStatus(ProviderStatus? status)
    {
        _status = status;
        (StatusText, StatusGood, StatusBad) = status switch
        {
            null => (Loc.Get("Provider_Off"), false, false),
            { State: ProviderConnectionState.Connected } => (Loc.Get("Provider_Connected"), true, false),
            { State: ProviderConnectionState.Idle } => (Loc.Get("Provider_Idle"), false, false),
            { State: ProviderConnectionState.Reconnecting } s => (s.Message is { Length: > 0 } m ? Loc.Get("Common_LabelValue", Loc.Get("Provider_Reconnecting"), m) : Loc.Get("Provider_Reconnecting"), false, false),
            { State: ProviderConnectionState.Failed } s => (s.Message is { Length: > 0 } m ? Loc.Get("Common_LabelValue", Loc.Get("Provider_Error"), m) : Loc.Get("Provider_Error"), false, true),
            _ => (Loc.Get("Provider_Unknown"), false, false),
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
        OnPropertyChanged(nameof(HasOptionError));
        if (_loading)
        {
            return;
        }

        // Show the options of a source the user just switched on, so required settings are not hidden.
        if (value && HasOptions)
        {
            IsExpanded = true;
        }

        _changed(this);
    }

    /// <summary>
    /// Surfaces an invalid option: flags the header and opens the section so the error is visible.
    /// </summary>
    /// <param name="sender">
    /// The option.
    /// </param>
    /// <param name="e">
    /// The property change data.
    /// </param>
    private void OnOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OptionViewModel.Error))
        {
            return;
        }

        OnPropertyChanged(nameof(HasOptionError));
        if (HasOptionError)
        {
            IsExpanded = true;
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
    public string Label => Loc.Instance.Find($"Routing_{AssetClass}") ?? EnumLabel.Of(AssetClass);

    /// <summary>
    /// Gets the enabled providers that support the class, best first.
    /// </summary>
    public ObservableCollection<ProviderDescriptor> Providers { get; } = [];

    /// <summary>
    /// Builds the texts that depend on the language again after the language changed.
    /// </summary>
    public void RefreshLanguage() => OnPropertyChanged(nameof(Label));

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
public sealed partial class SettingsViewModel : LocalizedViewModel
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
    /// Whether the desktop shows tray icons. It is assumed until the desktop has answered, which can take a moment on
    /// Linux and is done in the background.
    /// </summary>
    private volatile bool _hasTray = true;

    /// <summary>
    /// The start at sign-in registration, or <see langword="null"/> when the platform has none.
    /// </summary>
    private readonly IAutoStartService? _autoStart;

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
    /// <param name="updates">
    /// The update section, or <see langword="null"/> when the page has none.
    /// </param>
    /// <param name="autoStart">
    /// The start at sign-in registration, or <see langword="null"/> when the platform has none.
    /// </param>
    /// <param name="trayProbe">
    /// Tells whether the desktop shows tray icons; by default the desktop is asked, which can take a moment.
    /// </param>
    public SettingsViewModel(
        SettingsManager settings,
        ProviderRegistry registry,
        ProviderRouter router,
        MarketDataService market,
        IUiDispatcher ui,
        TimeProvider timeProvider,
        UpdateViewModel? updates = null,
        IAutoStartService? autoStart = null,
        Func<bool>? trayProbe = null)
        : base(ui)
    {
        _settings = settings;
        _autoStart = autoStart;
        Updates = updates;
        _router = router;
        _opacitySave = new Debouncer(TimeSpan.FromMilliseconds(400), timeProvider);
        Market = market;
        Groups = new GroupsSettingsViewModel(settings);
        TrayAssets = new TrayAssetsSettingsViewModel(settings, registry);
        Alerts = new AlertsViewModel(settings, registry, ui);
        Providers = new ObservableCollection<ProviderSettingsViewModel>(
            registry.Descriptors.Select(d => new ProviderSettingsViewModel(d, p => _ = SaveProviderAsync(p))));
        Routing = new ObservableCollection<RoutingRowViewModel>(
            Enum.GetValues<AssetClass>().Select(c => new RoutingRowViewModel(c, r => _ = SaveRoutingAsync(r))));
        market.ProviderStatusChanged += OnProviderStatusChanged;
        Reload();
        StartTrayProbe(trayProbe ?? TrayAvailability.Detect);
    }

    /// <inheritdoc />
    protected override void Detach()
    {
        Market.ProviderStatusChanged -= OnProviderStatusChanged;
        Alerts.Dispose();
    }

    /// <summary>
    /// Shows the new state of the data sources when one changes.
    /// </summary>
    /// <param name="sender">
    /// The market data service.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnProviderStatusChanged(object? sender, ProviderStatusChangedEventArgs e) => Ui.Post(RefreshStatuses);

    /// <summary>
    /// Gets the market data service, used to look up provider status.
    /// </summary>
    private MarketDataService Market { get; }

    /// <summary>
    /// Gets the selectable color conventions.
    /// </summary>
    public IReadOnlyList<ColorConvention> ColorConventions { get; } = Enum.GetValues<ColorConvention>();

    /// <summary>
    /// Gets the selectable color schemes.
    /// </summary>
    public IReadOnlyList<ThemeMode> ThemeModes { get; } = Enum.GetValues<ThemeMode>();

    /// <summary>
    /// Gets the languages to choose from.
    /// </summary>
    public IReadOnlyList<LanguageMode> Languages { get; } = Enum.GetValues<LanguageMode>();

    /// <summary>
    /// Gets the update section, or <see langword="null"/> when the page has none.
    /// </summary>
    public UpdateViewModel? Updates { get; }

    /// <summary>
    /// Gets the editor of the user-defined groups.
    /// </summary>
    public GroupsSettingsViewModel Groups { get; }

    /// <summary>
    /// Gets the editor of the instruments the tray list shows.
    /// </summary>
    public TrayAssetsSettingsViewModel TrayAssets { get; }

    /// <summary>
    /// Gets the explanation of the tray list editor, which names how many instruments the list holds.
    /// </summary>
    public string TrayAssetsHelp => Loc.Get("Settings_TrayAssetsHelp", TrayAssetSelection.MaxAssets);

    /// <summary>
    /// Gets the price alerts of all instruments.
    /// </summary>
    public AlertsViewModel Alerts { get; }

    /// <summary>
    /// Gets the selectable watchlist layouts.
    /// </summary>
    public IReadOnlyList<WatchlistLayout> Layouts { get; } = Enum.GetValues<WatchlistLayout>();

    /// <summary>
    /// Gets the selectable card groupings.
    /// </summary>
    public IReadOnlyList<CardGrouping> Groupings { get; } = Enum.GetValues<CardGrouping>();

    /// <summary>
    /// Gets the selectable card sizes.
    /// </summary>
    public IReadOnlyList<CardSize> CardSizes { get; } = Enum.GetValues<CardSize>();

    /// <summary>
    /// Gets the data source entries.
    /// </summary>
    public ObservableCollection<ProviderSettingsViewModel> Providers { get; }

    /// <summary>
    /// Gets the routing rows, one per asset class.
    /// </summary>
    public ObservableCollection<RoutingRowViewModel> Routing { get; }

    /// <summary>
    /// Gets a value indicating whether the platform can start the app at sign-in.
    /// </summary>
    public bool IsAutoStartSupported => _autoStart?.IsSupported == true;

    /// <summary>
    /// Gets a value indicating whether the platform can list the instruments when the tray icon is clicked.
    /// </summary>
    public bool IsTrayListSupported => TrayPopupAvailability.IsSupported;

    /// <summary>
    /// Gets or sets a value indicating whether the app starts when the user signs in.
    /// </summary>
    [ObservableProperty]
    public partial bool LaunchAtLogin { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the app starts hidden in the tray.
    /// </summary>
    [ObservableProperty]
    public partial bool StartMinimized { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether closing the window hides it to the tray instead of exiting.
    /// </summary>
    [ObservableProperty]
    public partial bool CloseToTray { get; set; } = true;

    /// <summary>
    /// Gets a value indicating whether this platform has the boss key.
    /// </summary>
    public bool IsBossKeySupported => OperatingSystem.IsWindows();

    /// <summary>
    /// Gets or sets the boss key as the user typed it; the box hands it over when it loses the focus, so a half-typed key
    /// such as <c>Ctrl+A</c> is never registered on the way. Text that is not a hotkey is shown but not saved.
    /// </summary>
    [ObservableProperty]
    public partial string BossKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the boss key box has the focus and records keys. The app lets go of the boss
    /// key meanwhile, because Windows would take the combination for it before the box could see it.
    /// </summary>
    [ObservableProperty]
    public partial bool IsRecordingBossKey { get; set; }

    /// <summary>
    /// Gets the currencies the total of the holdings can be shown in.
    /// </summary>
    public IReadOnlyList<string> BaseCurrencies { get; } = CurrencyCodes.Supported;

    /// <summary>
    /// Gets or sets the currency the total of the holdings is shown in.
    /// </summary>
    [ObservableProperty]
    public partial string BaseCurrency { get; set; } = CurrencyCodes.Pivot;

    /// <summary>
    /// Gets the tooltip of the window's close button, which says what it does under the current setting.
    /// </summary>
    public string CloseButtonTip => Loc.Get(CloseToTray && HasTray ? "Main_HideToTray" : "Common_Close");

    /// <summary>
    /// Gets a value indicating whether the desktop shows tray icons: without a tray the window is never hidden to it,
    /// whatever the setting says.
    /// </summary>
    private bool HasTray => _hasTray;

    /// <summary>
    /// Asks the desktop whether it shows tray icons, in the background, and updates the close button tip when it does not.
    /// </summary>
    /// <param name="probe">
    /// Tells whether the desktop shows tray icons.
    /// </param>
    private void StartTrayProbe(Func<bool> probe) =>
        _ = Task.Run(probe).ContinueWith(
            task =>
            {
                if (task is { IsCompletedSuccessfully: true, Result: false })
                {
                    _hasTray = false;
                    Ui.Post(() => OnPropertyChanged(nameof(CloseButtonTip)));
                }
            },
            TaskScheduler.Default);

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
    /// Gets or sets a value indicating whether rows flash when their price changes.
    /// </summary>
    [ObservableProperty]
    public partial bool RollPriceOnUpdate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether edge hiding can work here. It does until the platform turns out not to
    /// keep a window beyond the screen edge.
    /// </summary>
    [ObservableProperty]
    public partial bool AutoHideAvailable { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the window hides at a screen edge and slides out on hover.
    /// </summary>
    [ObservableProperty]
    public partial bool AutoHideAtEdge { get; set; }

    /// <summary>
    /// Gets or sets the color convention.
    /// </summary>
    [ObservableProperty]
    public partial ColorConvention SelectedColorConvention { get; set; }

    /// <summary>
    /// Gets or sets the color scheme.
    /// </summary>
    [ObservableProperty]
    public partial ThemeMode SelectedThemeMode { get; set; }

    /// <summary>
    /// Gets or sets the interface language.
    /// </summary>
    [ObservableProperty]
    public partial LanguageMode SelectedLanguage { get; set; }

    /// <summary>
    /// Gets or sets how the watchlist is laid out.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCardLayoutSelected))]
    public partial WatchlistLayout SelectedLayout { get; set; }

    /// <summary>
    /// Gets a value indicating whether the card layout is selected. The card grouping can only be set then.
    /// </summary>
    public bool IsCardLayoutSelected => SelectedLayout == WatchlistLayout.Cards;

    /// <summary>
    /// Gets or sets how the cards are grouped.
    /// </summary>
    [ObservableProperty]
    public partial CardGrouping SelectedGrouping { get; set; }

    /// <summary>
    /// Gets or sets how large the cards are.
    /// </summary>
    [ObservableProperty]
    public partial CardSize SelectedCardSize { get; set; }

    /// <summary>
    /// Reloads every control from the persisted settings. Call when the page is opened.
    /// </summary>
    public void Reload()
    {
        _loading = true;
        try
        {
            var current = _settings.Current;

            // The system's entry is the only record; it is not saved to the settings file, as the user may remove it in
            // the system settings.
            LaunchAtLogin = IsAutoStartSupported && _autoStart!.IsEnabled();
            StartMinimized = current.General.StartMinimized;
            CloseToTray = current.General.CloseToTray;
            BossKey = _settings.Current.General.BossKey;
            IsRecordingBossKey = false;
            BaseCurrency = CurrencyCodes.EffectiveBase(current.General.BaseCurrency, Loc.Instance.Language == Loc.SimplifiedChinese);
            Opacity = current.Appearance.Opacity;
            AlwaysOnTop = current.Appearance.AlwaysOnTop;
            Compact = current.Appearance.Compact;
            Locked = current.Appearance.Locked;
            RollPriceOnUpdate = current.Appearance.RollPriceOnUpdate;
            AutoHideAtEdge = current.Appearance.AutoHideAtEdge;
            SelectedColorConvention = current.Appearance.ColorConvention;
            SelectedThemeMode = current.Appearance.ThemeMode;
            SelectedLanguage = current.Appearance.Language;
            SelectedLayout = current.Appearance.Layout;
            SelectedGrouping = current.Appearance.Grouping;
            SelectedCardSize = current.Appearance.CardSize;
            foreach (var provider in Providers)
            {
                provider.Load(current);
            }
        }
        finally
        {
            _loading = false;
        }

        Groups.Reload();
        TrayAssets.Reload();
        Alerts.Reload(discardEdits: true);
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
    /// Registers or unregisters the start at sign-in. When the system refuses, the switch goes back.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnLaunchAtLoginChanged(bool value)
    {
        if (_loading || !IsAutoStartSupported)
        {
            return;
        }

        try
        {
            _autoStart!.SetEnabled(value);
        }
        catch (Exception)
        {
            // The service has logged the reason.
            _loading = true;
            try
            {
                LaunchAtLogin = !value;
            }
            finally
            {
                _loading = false;
            }
        }
    }

    /// <summary>
    /// Saves the start-minimized flag.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnStartMinimizedChanged(bool value) => SaveSetting(s => s.General.StartMinimized = value);

    /// <summary>
    /// Saves the currency of the total of the holdings.
    /// </summary>
    /// <param name="value">
    /// The new currency.
    /// </param>
    partial void OnBaseCurrencyChanged(string value) => SaveSetting(s => s.General.BaseCurrency = value);

    /// <summary>
    /// Saves the boss key when it is empty (off) or a valid hotkey, showing it in its normal spelling. Text that is not a
    /// hotkey is put back to the key in use, so the box never shows a key that does not work.
    /// </summary>
    /// <param name="value">
    /// The new text.
    /// </param>
    partial void OnBossKeyChanged(string value)
    {
        if (_loading)
        {
            return;
        }

        // The box can hand over null when it is cleared.
        if (string.IsNullOrWhiteSpace(value))
        {
            if (value is not (null or ""))
            {
                BossKey = string.Empty;
                return;
            }

            SaveSetting(s => s.General.BossKey = string.Empty);
        }
        else if (!HotkeyGesture.TryParse(value, out var gesture))
        {
            BossKey = _settings.Current.General.BossKey;
        }
        else if (gesture.ToString() != value)
        {
            BossKey = gesture.ToString();
        }
        else
        {
            SaveSetting(s => s.General.BossKey = value);
        }
    }

    /// <summary>
    /// Saves the close-to-tray flag.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnCloseToTrayChanged(bool value)
    {
        OnPropertyChanged(nameof(CloseButtonTip));
        SaveSetting(s => s.General.CloseToTray = value);
    }

    /// <summary>
    /// Saves the always-on-top flag.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnAlwaysOnTopChanged(bool value) => SaveSetting(s => s.Appearance.AlwaysOnTop = value);

    /// <summary>
    /// Saves the compact flag.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnCompactChanged(bool value) => SaveSetting(s => s.Appearance.Compact = value);

    /// <summary>
    /// Saves the flash-on-update flag.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnRollPriceOnUpdateChanged(bool value) => SaveSetting(s => s.Appearance.RollPriceOnUpdate = value);

    /// <summary>
    /// Saves the edge auto-hide setting.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnAutoHideAtEdgeChanged(bool value) => SaveSetting(s => s.Appearance.AutoHideAtEdge = value);

    /// <summary>
    /// Saves the locked flag.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnLockedChanged(bool value) => SaveSetting(s => s.Appearance.Locked = value);

    /// <summary>
    /// Saves the color convention.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnSelectedColorConventionChanged(ColorConvention value) => SaveSetting(s => s.Appearance.ColorConvention = value);

    /// <summary>
    /// Saves the color scheme.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnSelectedThemeModeChanged(ThemeMode value) => SaveSetting(s => s.Appearance.ThemeMode = value);

    /// <summary>
    /// Saves a changed interface language.
    /// </summary>
    /// <param name="value">
    /// The new language.
    /// </param>
    partial void OnSelectedLanguageChanged(LanguageMode value) => SaveSetting(s => s.Appearance.Language = value);

    /// <inheritdoc />
    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(CloseButtonTip));
        OnPropertyChanged(nameof(TrayAssetsHelp));
        TrayAssets.RefreshLanguage();
        foreach (var provider in Providers)
        {
            provider.RefreshLanguage();
        }

        foreach (var row in Routing)
        {
            row.RefreshLanguage();
        }
    }

    /// <summary>
    /// Saves the watchlist layout.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnSelectedLayoutChanged(WatchlistLayout value)
    {
        SaveSetting(s => s.Appearance.Layout = value);
        if (!_loading)
        {
            LayoutChosen?.Invoke(this, value);
        }
    }

    /// <summary>
    /// Raised when the user chose a layout on the settings page. Filling the page from the saved settings is not a choice.
    /// </summary>
    public event EventHandler<WatchlistLayout>? LayoutChosen;

    /// <summary>
    /// Saves the card grouping.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnSelectedGroupingChanged(CardGrouping value) => SaveSetting(s => s.Appearance.Grouping = value);

    /// <summary>
    /// Persists the card size.
    /// </summary>
    /// <param name="value">
    /// The new size.
    /// </param>
    partial void OnSelectedCardSizeChanged(CardSize value) => SaveSetting(s => s.Appearance.CardSize = value);

    /// <summary>
    /// Persists a change of a general or appearance setting unless values are being loaded.
    /// </summary>
    /// <param name="change">
    /// The change to apply.
    /// </param>
    private void SaveSetting(Action<AppSettings> change)
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
