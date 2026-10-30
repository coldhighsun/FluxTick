using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxTick.Abstractions;
using FluxTick.App.Localization;
using FluxTick.App.Services;
using FluxTick.Core.Application;
using Microsoft.Extensions.Logging;

namespace FluxTick.App.ViewModels;

/// <summary>
/// The editor of one provider option, generated from its <see cref="ProviderOptionDefinition"/>.
/// </summary>
public sealed partial class OptionViewModel : ObservableObject
{
    /// <summary>
    /// Called when the user edited the value.
    /// </summary>
    private readonly Action _edited;

    /// <summary>
    /// Suppresses edit handling while values are set programmatically.
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
    /// The saved value in its stored text form: what the settings hold, whatever the editor shows. A blank value is the
    /// saved value of a required option that was never given.
    /// </summary>
    private string _saved = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="OptionViewModel"/> class.
    /// </summary>
    /// <param name="definition">
    /// The option definition.
    /// </param>
    /// <param name="stored">
    /// The stored value, or <see langword="null"/> to show the default.
    /// </param>
    /// <param name="edited">
    /// The callback invoked when the user edits the value.
    /// </param>
    /// <param name="owner">
    /// The data source that owns the option, used to translate its hint.
    /// </param>
    public OptionViewModel(ProviderOptionDefinition definition, string? stored, Action edited, ProviderDescriptor? owner = null)
    {
        Definition = definition;
        _owner = owner;
        _edited = edited;
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
    public string Label => Definition.Required ? $"{ProviderText.OptionLabel(Definition, _owner)} *" : ProviderText.OptionLabel(Definition, _owner);

    /// <summary>
    /// Gets the help text, or an empty string.
    /// </summary>
    public string Description => (_owner is null ? Definition.Description : ProviderText.OptionDescription(_owner, Definition)) ?? string.Empty;

    /// <summary>
    /// Gets the unit suffix shown next to the label.
    /// </summary>
    public string Unit => Definition.Type == ProviderOptionType.Duration ? Loc.Get("Option_SecondsUnit") : string.Empty;

    /// <summary>
    /// Gets a value indicating whether the option is edited as free text.
    /// </summary>
    public bool IsPlainText => Definition.Type is ProviderOptionType.String or ProviderOptionType.Secret;

    /// <summary>
    /// Gets a value indicating whether the option is edited as a number, in a box that takes digits only.
    /// </summary>
    public bool IsNumber => Definition.Type is ProviderOptionType.Int or ProviderOptionType.Duration;

    /// <summary>
    /// Gets the kind of number a number option takes: seconds with a point for a duration, a whole number for an integer,
    /// which may be negative unless its lower bound rules that out.
    /// </summary>
    public NumericKind NumberKind => Definition.Type == ProviderOptionType.Duration
        ? NumericKind.Seconds
        : Definition.Min is >= 0 ? NumericKind.WholeNumber : NumericKind.SignedWholeNumber;

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
    /// Gets a value indicating whether the option is required and the saved settings hold no value for it.
    /// </summary>
    public bool IsMissingRequired => Definition.IsMissing(_saved);

    /// <summary>
    /// Gets a value indicating whether the editor differs from the saved value. An invalid input counts as a change.
    /// </summary>
    public bool IsDirty => DraftValue != _saved;

    /// <summary>
    /// Gets the value the editor holds, in its stored text form: blank for a required option that is left blank, and
    /// <see langword="null"/> when the input is invalid.
    /// </summary>
    private string? DraftValue => StoredValue ?? (Definition.IsMissing(Text) ? string.Empty : null);

    /// <summary>
    /// Shows that a required option has no saved value, when it has none.
    /// </summary>
    public void ShowErrorIfMissing()
    {
        if (IsMissingRequired)
        {
            Error = RequiredText();
        }
    }

    /// <summary>
    /// Checks the value the editor holds before it is saved and shows what is wrong with it.
    /// </summary>
    /// <param name="enforceRequired">
    /// Whether a required option must have a value, as it must for a source that is on.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value can be saved.
    /// </returns>
    public bool Validate(bool enforceRequired)
    {
        if (StoredValue is not null || (DraftValue is not null && !enforceRequired))
        {
            Error = string.Empty;
            return true;
        }

        Error = ErrorText();
        return false;
    }

    /// <summary>
    /// Takes the value the editor holds as the saved value. Call it once the value was checked.
    /// </summary>
    public void MarkSaved() => _saved = DraftValue ?? _saved;

    /// <summary>
    /// Puts the editor back to the saved value.
    /// </summary>
    public void Revert() => Load(_saved);

    /// <summary>
    /// Puts the editor to the built-in default without saving it.
    /// </summary>
    public void ResetToDefault()
    {
        Apply(Definition.DefaultValue);
        Error = string.Empty;
        _edited();
    }

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
    /// Gets the saved value to persist, or <see langword="null"/> when the option should not be stored because it is at
    /// the built-in default. The editor is not consulted, so a draft is never written by accident.
    /// </summary>
    public string? ValueToPersist => _saved == _normalizedDefault ? null : _saved;

    /// <summary>
    /// Sets the editor and the saved value from a stored value, without raising an edit.
    /// </summary>
    /// <param name="stored">
    /// The stored value, or <see langword="null"/> to show the default.
    /// </param>
    public void Load(string? stored)
    {
        var value = Resolve(stored);
        Apply(value);
        _saved = Definition.Canonicalize(value);
        Error = string.Empty;
    }

    /// <summary>
    /// Sets the saved value from a stored value and leaves the editor as it is, for an edit that is not saved yet.
    /// </summary>
    /// <param name="stored">
    /// The stored value, or <see langword="null"/> for the default.
    /// </param>
    public void SetSaved(string? stored) => _saved = Definition.Canonicalize(Resolve(stored));

    /// <summary>
    /// Gets the value an option shows for a stored value. Like ProviderOptions, which is what the provider runs with, an
    /// invalid stored value counts as the default.
    /// </summary>
    /// <param name="stored">
    /// The stored value, or <see langword="null"/> for the default.
    /// </param>
    /// <returns>
    /// The stored value when it is valid, the default otherwise.
    /// </returns>
    private string Resolve(string? stored) =>
        stored is not null && Definition.Validate(stored) is null ? stored : Definition.DefaultValue;

    /// <summary>
    /// Sets the editor from a stored value without raising an edit.
    /// </summary>
    /// <param name="value">
    /// The stored value.
    /// </param>
    private void Apply(string value)
    {
        _loading = true;
        try
        {
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
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Handles a text edit.
    /// </summary>
    /// <param name="value">
    /// The new text.
    /// </param>
    partial void OnTextChanged(string value) => Edit();

    /// <summary>
    /// Handles a switch change.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnBoolValueChanged(bool value) => Edit();

    /// <summary>
    /// Handles a pick list change.
    /// </summary>
    /// <param name="value">
    /// The new selection.
    /// </param>
    partial void OnSelectedChoiceChanged(string? value) => Edit();

    /// <summary>
    /// Builds the texts that depend on the language again after the language changed.
    /// </summary>
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Unit));
        if (Error.Length > 0)
        {
            Error = StoredValue is null ? ErrorText() : RequiredText();
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
    /// Gets the message for a required value that is missing.
    /// </summary>
    /// <returns>
    /// The message in the active language.
    /// </returns>
    private static string RequiredText() =>
        ProviderText.Validation(new OptionValidation(OptionValidationError.Required, "A value is required."));

    /// <summary>
    /// Handles an edit and tells the owner. An error is cleared as soon as the input is fine, but a new one is not shown
    /// while the user is still typing, because a value that is on its way to a valid one is not wrong yet; saving checks it
    /// and shows what is wrong. Nothing is saved here: that takes the owner's save.
    /// </summary>
    private void Edit()
    {
        if (_loading)
        {
            return;
        }

        if (Error.Length > 0 && DraftValue is not null)
        {
            Error = string.Empty;
        }

        _edited();
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
    /// Runs an action after the input that is being handled, for what must not happen inside a binding update.
    /// </summary>
    private readonly Action<Action> _later;

    /// <summary>
    /// Opens a web page in the browser; reports whether it could.
    /// </summary>
    private readonly Func<string, bool> _openWebsite;

    /// <summary>
    /// Whether an option was edited and not saved, as last announced.
    /// </summary>
    private bool _dirty;

    /// <summary>
    /// Whether a required option had no saved value, as last announced.
    /// </summary>
    private bool _missingRequired;

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
    /// <param name="later">
    /// Runs an action after the input that is being handled, or <see langword="null"/> to run it at once.
    /// </param>
    /// <param name="openWebsite">
    /// Opens a web page in the browser, or <see langword="null"/> to use the default browser.
    /// </param>
    public ProviderSettingsViewModel(
        ProviderDescriptor descriptor,
        Action<ProviderSettingsViewModel> changed,
        Action<Action>? later = null,
        Func<string, bool>? openWebsite = null)
    {
        Descriptor = descriptor;
        _changed = changed;
        _later = later ?? (action => action());
        _openWebsite = openWebsite ?? (url => ExternalLinks.TryOpenWebsite(url));
        Options = new ObservableCollection<OptionViewModel>(
            descriptor.OptionDefinitions.Select(d => new OptionViewModel(d, null, OnOptionEdited, descriptor)));
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
    /// Gets the address of the site of the data source, or <see langword="null"/> when it has none or the address is not
    /// one the app opens (see <see cref="ExternalLinks.IsWebUrl"/>).
    /// </summary>
    public string? HomepageUrl => Descriptor.Homepage is { IsAbsoluteUri: true } homepage && ExternalLinks.IsWebUrl(homepage.AbsoluteUri)
        ? homepage.AbsoluteUri
        : null;

    /// <summary>
    /// Gets a value indicating whether the card shows a link to the site of the data source.
    /// </summary>
    public bool HasHomepage => HomepageUrl is not null;

    /// <summary>
    /// Gets the host of the site, without a leading <c>www.</c>, which is what the link shows.
    /// </summary>
    public string HomepageHost => HasHomepage && Descriptor.Homepage is { } homepage
        ? (homepage.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? homepage.Host[4..] : homepage.Host)
        : string.Empty;

    /// <summary>
    /// Opens the site of the data source in the browser.
    /// </summary>
    [RelayCommand]
    private void OpenHomepage()
    {
        if (HomepageUrl is { } url)
        {
            _openWebsite(url);
        }
    }

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
    /// Gets a value indicating whether an option of a source that is on currently holds an invalid value or lacks a
    /// required one. A source that is off is not flagged, as it does not run.
    /// </summary>
    public bool HasOptionError => Enabled && Options.Any(o => o.Error.Length > 0);

    /// <summary>
    /// Gets a value indicating whether the switch was turned on without the values the source needs and is about to go
    /// back, so that what shows the switch, such as the order of the list, does not follow it.
    /// </summary>
    public bool IsEnableRefused { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a required option has no saved value, so that the source can not be switched on yet.
    /// </summary>
    public bool HasMissingRequired => Options.Any(o => o.IsMissingRequired);

    /// <summary>
    /// Gets a value indicating whether an option was edited and not saved yet.
    /// </summary>
    public bool IsDirty => Options.Any(o => o.IsDirty);

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
    /// Puts every option of this source to its built-in default, as an edit that is saved with the others. The enabled
    /// state is kept, and so are secret options such as keys, which are not settings that can be "restored" but
    /// credentials.
    /// </summary>
    [RelayCommand]
    public void RestoreDefaults()
    {
        foreach (var option in Options.Where(o => o.Definition.Type != ProviderOptionType.Secret))
        {
            option.ResetToDefault();
        }
    }

    /// <summary>
    /// Checks the edited options and saves them, or opens the card and shows what is wrong. A required value is only
    /// needed while the source is on.
    /// </summary>
    [RelayCommand]
    public void Save()
    {
        if (!IsDirty)
        {
            return;
        }

        var valid = true;
        foreach (var option in Options)
        {
            valid &= option.Validate(Enabled);
        }

        if (!valid)
        {
            IsExpanded = true;
            return;
        }

        foreach (var option in Options)
        {
            option.MarkSaved();
        }

        OnOptionEdited();
        _changed(this);
    }

    /// <summary>
    /// Puts the options back to what is saved.
    /// </summary>
    [RelayCommand]
    public void Revert()
    {
        foreach (var option in Options)
        {
            option.Revert();
        }

        ShowMissingRequired();
        OnOptionEdited();
    }

    /// <summary>
    /// Shows which required options of a source that is on have no saved value.
    /// </summary>
    private void ShowMissingRequired()
    {
        if (!Enabled)
        {
            return;
        }

        foreach (var option in Options)
        {
            option.ShowErrorIfMissing();
        }
    }

    /// <summary>
    /// Shows the buttons for unsaved edits and the status when an option was edited or saved.
    /// </summary>
    private void OnOptionEdited()
    {
        var dirty = IsDirty;
        if (dirty != _dirty)
        {
            _dirty = dirty;
            OnPropertyChanged(nameof(IsDirty));
        }

        var missing = HasMissingRequired;
        if (missing != _missingRequired)
        {
            _missingRequired = missing;
            OnPropertyChanged(nameof(HasMissingRequired));
            UpdateStatus(_status);
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
            Enabled = settings.IsProviderSwitchedOn(Descriptor);
            settings.Providers.TryGetValue(Descriptor.Id, out var stored);
            foreach (var option in Options)
            {
                string? value = null;
                stored?.Options.TryGetValue(option.Definition.Key, out value);

                // An edit that was not saved stays, so closing the window never loses it; what it is measured against
                // follows the settings.
                if (option.IsDirty)
                {
                    option.SetSaved(value);
                }
                else
                {
                    option.Load(value);
                }
            }

            // A source that is on but lacks a required value, as after an update that adds one, is flagged and opened.
            ShowMissingRequired();
        }
        finally
        {
            _loading = false;
        }

        OnOptionEdited();
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
            _ when Enabled && HasMissingRequired => (Loc.Get("Provider_NeedsSetup"), false, true),
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
        UpdateStatus(_status);
        if (_loading)
        {
            return;
        }

        // A source that needs a value, such as a key, can not be switched on before the value is saved: the switch goes
        // back, and the card opens on the field that is missing.
        if (value && HasMissingRequired)
        {
            IsExpanded = true;
            foreach (var option in Options)
            {
                option.ShowErrorIfMissing();
            }

            // Not inside this binding update, which the switch is still part of; or it could keep showing the old state.
            IsEnableRefused = true;
            _later(() =>
            {
                _loading = true;
                try
                {
                    Enabled = false;
                }
                finally
                {
                    _loading = false;
                    IsEnableRefused = false;
                }
            });

            return;
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
        UpdateStatus(_status);
        if (HasOptionError)
        {
            IsExpanded = true;
        }
    }
}

/// <summary>
/// The settings page: appearance and data sources. Every change is applied and persisted
/// immediately.
/// </summary>
public sealed partial class SettingsViewModel : LocalizedViewModel
{
    /// <summary>
    /// The settings store.
    /// </summary>
    private readonly SettingsManager _settings;

    /// <summary>
    /// The names of the installed font families, read when first needed.
    /// </summary>
    private readonly Lazy<IReadOnlyList<string>> _fontFamilies;

    /// <summary>
    /// The family the font box goes back to when it is left: the font that is saved, or being saved, or an empty text for
    /// the font of the system.
    /// </summary>
    private string _committedFont = string.Empty;

    /// <summary>
    /// Whether the text of the font box is being set by the code, which is not typing in the box.
    /// </summary>
    private bool _settingFontText;

    /// <summary>
    /// Saves the opacity once the slider stops moving.
    /// </summary>
    private readonly Debouncer _opacitySave;

    /// <summary>
    /// Suppresses saves while values are loaded programmatically.
    /// </summary>
    private bool _loading;

    /// <summary>
    /// Whether the choices of the asset class filter are being set by the page itself.
    /// </summary>
    private bool _selectingProviderClass;

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
    /// Opens a folder in the file manager and tells whether that worked.
    /// </summary>
    private readonly Func<string, bool> _openFolder;

    /// <summary>
    /// Opens the project home page in the browser and tells whether that worked.
    /// </summary>
    private readonly Func<string, bool> _openHomepage;

    /// <summary>
    /// The log, or <see langword="null"/> for none.
    /// </summary>
    private readonly ILogger<SettingsViewModel>? _logger;

    /// <summary>
    /// Counts the clicks on the buttons that open a folder, so that only the outcome of the latest one is shown. It is
    /// only touched on the UI thread: by the click, and by the outcome that is posted back.
    /// </summary>
    private int _openFolderTicket;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
    /// </summary>
    /// <param name="settings">
    /// The settings store.
    /// </param>
    /// <param name="registry">
    /// The available providers.
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
    /// <param name="openFolder">
    /// Opens a folder in the file manager and tells whether that worked; by default the real file manager is used.
    /// </param>
    /// <param name="openHomepage">
    /// Opens the project home page and tells whether that worked; by default the real browser is used.
    /// </param>
    /// <param name="logger">
    /// Receives the reason when a folder could not be opened, or <see langword="null"/> for none.
    /// </param>
    /// <param name="fontFamilies">
    /// Lists the names of the installed font families; by default the system is asked.
    /// </param>
    public SettingsViewModel(
        SettingsManager settings,
        ProviderRegistry registry,
        MarketDataService market,
        IUiDispatcher ui,
        TimeProvider timeProvider,
        UpdateViewModel? updates = null,
        IAutoStartService? autoStart = null,
        Func<bool>? trayProbe = null,
        Func<string, bool>? openFolder = null,
        Func<string, bool>? openHomepage = null,
        ILogger<SettingsViewModel>? logger = null,
        Func<IReadOnlyList<string>>? fontFamilies = null)
        : base(ui)
    {
        _settings = settings;
        _fontFamilies = new Lazy<IReadOnlyList<string>>(fontFamilies ?? FontCatalog.GetFamilies);
        _autoStart = autoStart;
        _logger = logger;
        _openFolder = openFolder ?? (directory => FileManager.TryOpen(directory, logger: logger));
        _openHomepage = openHomepage ?? (url => ExternalLinks.TryOpenGitHub(url));
        Updates = updates;
        _opacitySave = new Debouncer(TimeSpan.FromMilliseconds(400), timeProvider);
        Market = market;
        Groups = new GroupsSettingsViewModel(settings);
        TrayAssets = new TrayAssetsSettingsViewModel(settings, registry);
        Alerts = new AlertsViewModel(settings, registry, ui);
        Providers = new ObservableCollection<ProviderSettingsViewModel>(
            registry.Descriptors.Select(d => new ProviderSettingsViewModel(d, p => _ = SaveProviderAsync(p), ui.PostBackground)));
        foreach (var provider in Providers)
        {
            provider.PropertyChanged += OnProviderPropertyChanged;
        }

        ProviderClassFilters =
        [
            new ProviderClassFilterViewModel(null, OnProviderClassFilterChanged),
            .. Enum.GetValues<AssetClass>()
                .Where(c => registry.Descriptors.Any(d => d.Supports(c)))
                .Select(c => new ProviderClassFilterViewModel(c, OnProviderClassFilterChanged)),
        ];
        ProviderClassFilters[0].IsSelected = true;
        market.ProviderStatusChanged += OnProviderStatusChanged;
        Reload();
        StartTrayProbe(trayProbe ?? TrayAvailability.Detect);
    }

    /// <inheritdoc />
    protected override void Detach()
    {
        Market.ProviderStatusChanged -= OnProviderStatusChanged;
        foreach (var provider in Providers)
        {
            provider.PropertyChanged -= OnProviderPropertyChanged;
        }

        Alerts.Dispose();
    }

    /// <summary>
    /// Puts a data source that was switched on or off in its place in the list.
    /// </summary>
    /// <param name="sender">
    /// The data source entry.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnProviderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_loading && e.PropertyName == nameof(ProviderSettingsViewModel.Enabled) && sender is not ProviderSettingsViewModel { IsEnableRefused: true })
        {
            RebuildFilteredProviders();
        }
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
    /// Gets the names of the font families installed on this computer. Read when first asked, as that takes a moment.
    /// </summary>
    public IReadOnlyList<string> FontFamilies => _fontFamilies.Value;

    /// <summary>
    /// Gets the fonts the list below the font box shows: all of them while the box is empty or shows the font in use, and
    /// else the ones whose name contains what was typed.
    /// </summary>
    public IReadOnlyList<string> FontSuggestions
    {
        get
        {
            var text = FontText.Trim();
            return text.Length == 0 || string.Equals(text, _committedFont, StringComparison.OrdinalIgnoreCase)
                ? FontFamilies
                : [.. FontFamilies.Where(f => f.Contains(text, StringComparison.OrdinalIgnoreCase))];
        }
    }

    /// <summary>
    /// Gets the languages to choose from.
    /// </summary>
    public IReadOnlyList<LanguageMode> Languages { get; } = Enum.GetValues<LanguageMode>();

    /// <summary>
    /// Gets the update section, or <see langword="null"/> when the page has none.
    /// </summary>
    public UpdateViewModel? Updates { get; }

    /// <summary>
    /// Gets the product information shown on the about tab.
    /// </summary>
    public AboutInfo About { get; } = new();

    /// <summary>
    /// Gets or sets the index of the selected tab, which is the value of a <see cref="SettingsTab"/>.
    /// </summary>
    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    /// <summary>
    /// Selects a tab of the page.
    /// </summary>
    /// <param name="tab">
    /// The tab to show.
    /// </param>
    public void SelectTab(SettingsTab tab)
    {
        HomepageOpenFailed = false;
        SelectedTabIndex = (int)tab;
    }

    /// <summary>
    /// Opens the project home page in the browser.
    /// </summary>
    [RelayCommand]
    private void OpenProjectHomepage() => HomepageOpenFailed = !_openHomepage(About.HomepageUrl);

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
    /// Gets the data sources the page lists: those that match the asset class filter, the enabled
    /// ones first, each group in ascending order of name.
    /// </summary>
    public ObservableCollection<ProviderSettingsViewModel> FilteredProviders { get; } = [];

    /// <summary>
    /// Gets the choices of the asset class filter: all, then each asset class that some data source supports.
    /// </summary>
    public IReadOnlyList<ProviderClassFilterViewModel> ProviderClassFilters { get; }

    /// <summary>
    /// Gets a value indicating whether the platform can start the app at sign-in.
    /// </summary>
    public bool IsAutoStartSupported => _autoStart?.IsSupported == true;

    /// <summary>
    /// Gets a value indicating whether the platform can list the instruments when the tray icon is clicked.
    /// </summary>
    public bool IsTrayListSupported => TrayPopupAvailability.IsSupported;

    /// <summary>
    /// Gets or sets a value indicating whether the last attempt to open the settings or the log folder failed.
    /// </summary>
    [ObservableProperty]
    public partial bool FolderOpenFailed { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the last attempt to open the project home page failed.
    /// </summary>
    [ObservableProperty]
    public partial bool HomepageOpenFailed { get; set; }

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
    public partial double Opacity { get; set; } = 0.9;

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
    /// Gets or sets a value indicating whether rows tell whether the market of the instrument is trading.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowMarketStatus { get; set; } = true;

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
    /// Gets or sets the text of the font box: the name of the font family, or nothing for the font of the system. It is
    /// only a name being typed until a font is clicked in the list or Enter is pressed, which saves it.
    /// </summary>
    [ObservableProperty]
    public partial string FontText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the list of fonts below the font box is open.
    /// </summary>
    [ObservableProperty]
    public partial bool IsFontListOpen { get; set; }

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
    /// Opens the folder that holds the settings file.
    /// </summary>
    /// <returns>
    /// A task that completes when the file manager was asked and the outcome is shown.
    /// </returns>
    [RelayCommand]
    private Task OpenSettingsFolderAsync() => OpenFolderAsync(AppPaths.DataDirectory);

    /// <summary>
    /// Opens the folder that holds the log files.
    /// </summary>
    /// <returns>
    /// A task that completes when the file manager was asked and the outcome is shown.
    /// </returns>
    [RelayCommand]
    private Task OpenLogFolderAsync() => OpenFolderAsync(AppPaths.LogDirectory);

    /// <summary>
    /// Opens a folder off the UI thread, as making it and starting the file manager can take a moment on a redirected
    /// profile, and shows whether that worked.
    /// </summary>
    /// <param name="directory">
    /// The folder.
    /// </param>
    /// <returns>
    /// A task that completes when the outcome is shown.
    /// </returns>
    private async Task OpenFolderAsync(string directory)
    {
        // Only the latest click is shown: a slower, earlier one must not overwrite its outcome.
        var ticket = ++_openFolderTicket;
        bool opened;
        try
        {
            opened = await Task.Run(() => _openFolder(directory));
        }
        catch (Exception ex)
        {
            // A button must not end the app, whatever went wrong; the log keeps the defect.
            _logger?.LogError(ex, "Could not open the folder {Folder}", directory);
            opened = false;
        }

        Ui.Post(() =>
        {
            if (ticket == _openFolderTicket)
            {
                FolderOpenFailed = !opened;
            }
        });
    }

    /// <summary>
    /// Reloads every control from the persisted settings. Call when the page is opened.
    /// </summary>
    public void Reload()
    {
        FolderOpenFailed = false;
        HomepageOpenFailed = false;
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
            ShowMarketStatus = current.Appearance.ShowMarketStatus;
            AutoHideAtEdge = current.Appearance.AutoHideAtEdge;
            SelectedColorConvention = current.Appearance.ColorConvention;
            SelectedThemeMode = current.Appearance.ThemeMode;

            // The saved name is shown as it is; whether it is still installed is only checked when the box is used, as
            // that lists the system's fonts.
            _committedFont = current.Appearance.Font;
            if (!IsFontListOpen)
            {
                FontText = _committedFont;
            }

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

        RebuildFilteredProviders();

        Groups.Reload();
        TrayAssets.Reload();
        Alerts.Reload(discardEdits: true);
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
    /// Saves the setting when it changes.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnShowMarketStatusChanged(bool value) => SaveSetting(s => s.Appearance.ShowMarketStatus = value);

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
    /// Opens the list of fonts while the text is being typed. Typing does not save a font: that takes a click on a font in
    /// the list or Enter.
    /// </summary>
    /// <param name="value">
    /// The new text of the font box.
    /// </param>
    partial void OnFontTextChanged(string value)
    {
        OnPropertyChanged(nameof(FontSuggestions));
        if (_loading || _settingFontText)
        {
            return;
        }

        IsFontListOpen = true;
    }

    /// <summary>
    /// Gets the family a saved font name stands for. A name that no installed family matches is the font of the system,
    /// which is what the app uses then, so the box shows it as empty. The families are only listed when a name is saved.
    /// </summary>
    /// <param name="saved">
    /// The saved font name, or an empty text.
    /// </param>
    /// <returns>
    /// The installed family with that name, or an empty text.
    /// </returns>
    private string InstalledFont(string saved) =>
        saved.Length == 0 ? string.Empty : FindFamily(saved) ?? string.Empty;

    /// <summary>
    /// Finds the installed family a name stands for, whatever the case.
    /// </summary>
    /// <param name="name">
    /// The name to look for.
    /// </param>
    /// <returns>
    /// The family, spelled as the system has it, or <see langword="null"/> when none has that name.
    /// </returns>
    private string? FindFamily(string name) =>
        FontFamilies.FirstOrDefault(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Opens the list of fonts below the font box.
    /// </summary>
    public void OpenFontList()
    {
        if (IsFontListOpen)
        {
            return;
        }

        // A saved font that was removed from the computer is the font of the system, which the box shows as empty.
        var installed = InstalledFont(_committedFont);
        if (!string.Equals(installed, _committedFont, StringComparison.Ordinal))
        {
            _committedFont = installed;
            SetFontText(installed);
        }

        IsFontListOpen = true;
    }

    /// <summary>
    /// Uses the font the text of the box stands for, for when Enter is pressed: the installed family with that name, or the
    /// only family the typed text leaves in the list, or the font of the system when the box is empty. Any other text
    /// changes nothing.
    /// </summary>
    public void CommitFont()
    {
        var text = FontText.Trim();
        if (text.Length == 0)
        {
            ChooseFont(string.Empty);
            return;
        }

        var family = FindFamily(text);
        if (family is null && FontSuggestions is [var only])
        {
            family = only;
        }

        if (family is not null)
        {
            ChooseFont(family);
        }
    }

    /// <summary>
    /// Uses a font from the list: shows it in the box, saves it and closes the list.
    /// </summary>
    /// <param name="family">
    /// The family name.
    /// </param>
    public void ChooseFont(string family)
    {
        SetFontText(family);
        SaveFont(family);
        IsFontListOpen = false;
    }

    /// <summary>
    /// Saves the font of the interface text, unless it is the one already saved.
    /// </summary>
    /// <param name="family">
    /// The family name, or an empty text for the font of the system.
    /// </param>
    private void SaveFont(string family)
    {
        _committedFont = family;
        if (string.Equals(family, _settings.Current.Appearance.Font, StringComparison.Ordinal))
        {
            return;
        }

        SaveSetting(s => s.Appearance.Font = family);
    }

    /// <summary>
    /// Closes the list and shows the font in use in the box again, for when the box is left or Escape is pressed: typing
    /// saves nothing, so whatever was typed gives way to the font in use. A font is only used through a click in the list
    /// or Enter.
    /// </summary>
    public void LeaveFontBox()
    {
        IsFontListOpen = false;
        SetFontText(_committedFont);
    }

    /// <summary>
    /// Sets the text of the font box without that being taken for typing.
    /// </summary>
    /// <param name="text">
    /// The new text.
    /// </param>
    private void SetFontText(string text)
    {
        _settingFontText = true;
        try
        {
            FontText = text;
        }
        finally
        {
            _settingFontText = false;
        }
    }

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

        foreach (var filter in ProviderClassFilters)
        {
            filter.RefreshLanguage();
        }

        RebuildFilteredProviders();
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
    /// Keeps exactly one choice of the asset class filter checked and shows the data sources it matches. Unchecking the
    /// checked choice, which the buttons allow, keeps it checked.
    /// </summary>
    /// <param name="changed">
    /// The choice that was checked or unchecked.
    /// </param>
    private void OnProviderClassFilterChanged(ProviderClassFilterViewModel changed)
    {
        if (_selectingProviderClass)
        {
            return;
        }

        _selectingProviderClass = true;
        try
        {
            var chosen = changed.IsSelected ? changed : ProviderClassFilters.FirstOrDefault(f => f.IsSelected) ?? changed;
            foreach (var filter in ProviderClassFilters)
            {
                filter.IsSelected = filter == chosen;
            }
        }
        finally
        {
            _selectingProviderClass = false;
        }

        RebuildFilteredProviders();
    }

    /// <summary>
    /// Fills <see cref="FilteredProviders"/> with the data sources that match the asset class filter: the enabled ones
    /// first, each group in ascending order of the displayed name.
    /// </summary>
    private void RebuildFilteredProviders()
    {
        var assetClass = ProviderClassFilters.FirstOrDefault(f => f.IsSelected)?.AssetClass;
        var matching = Providers
            .Where(p => assetClass is not { } c || p.Descriptor.Supports(c))
            .Select(p => (Provider: p, p.Enabled, p.Name))
            .OrderByDescending(p => p.Enabled)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => p.Provider)
            .ToList();
        var shown = matching.ToHashSet();

        // Only what changed is removed, inserted or moved, so that the cards that stay keep their controls, and with them
        // the focus and the scroll position.
        for (var index = FilteredProviders.Count - 1; index >= 0; index--)
        {
            if (!shown.Contains(FilteredProviders[index]))
            {
                FilteredProviders.RemoveAt(index);
            }
        }

        for (var index = 0; index < matching.Count; index++)
        {
            if (index < FilteredProviders.Count && FilteredProviders[index] == matching[index])
            {
                continue;
            }

            var current = FilteredProviders.IndexOf(matching[index]);
            if (current < 0)
            {
                FilteredProviders.Insert(index, matching[index]);
            }
            else
            {
                FilteredProviders.Move(current, index);
            }
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
