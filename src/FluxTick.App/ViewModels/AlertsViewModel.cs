using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxTick.Abstractions;
using FluxTick.App.Localization;
using FluxTick.App.Services;
using FluxTick.Core.Application;

namespace FluxTick.App.ViewModels;

/// <summary>
/// One alert rule in a list, with the switches that change it.
/// </summary>
public sealed partial class AlertRuleItemViewModel : ObservableObject
{
    /// <summary>
    /// The list the rule belongs to.
    /// </summary>
    private readonly AlertsViewModel _owner;

    /// <summary>
    /// The rule as it is saved. What is shown is compared with it, a change that is saved is written to it too, and the
    /// switches are read from and written to it.
    /// </summary>
    private readonly AlertRule _saved;

    /// <summary>
    /// Initializes a new instance of the <see cref="AlertRuleItemViewModel"/> class.
    /// </summary>
    /// <param name="owner">
    /// The list the rule belongs to.
    /// </param>
    /// <param name="rule">
    /// The rule.
    /// </param>
    /// <param name="providerName">
    /// The name of the data source that serves the instrument.
    /// </param>
    public AlertRuleItemViewModel(AlertsViewModel owner, AlertRule rule, string providerName)
    {
        _owner = owner;
        _saved = rule.Clone();
        Id = rule.Id;
        Symbol = rule.Symbol;
        ProviderName = providerName;
        SelectedCondition = rule.Condition;
        ThresholdText = FormatThreshold(rule.Threshold);
    }

    /// <summary>
    /// Gets the id of the rule.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the symbol of the instrument the rule watches.
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// Gets the name of the data source that serves the instrument, which tells apart instruments with the same symbol.
    /// </summary>
    public string ProviderName { get; }

    /// <summary>
    /// Gets the conditions to choose from.
    /// </summary>
    public IReadOnlyList<AlertCondition> Conditions { get; } = Enum.GetValues<AlertCondition>();

    /// <summary>
    /// Gets or sets the condition of the rule.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(Error))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial AlertCondition SelectedCondition { get; set; }

    /// <summary>
    /// Gets or sets the threshold of the rule, as typed. It only takes effect when it is saved.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(Error))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string ThresholdText { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the condition or the threshold was changed and not saved yet.
    /// </summary>
    public bool IsDirty => SelectedCondition != _saved.Condition || ThresholdText.Trim() != FormatThreshold(_saved.Threshold);

    /// <summary>
    /// Gets a value indicating whether the rule cannot be saved as it is typed.
    /// </summary>
    public bool HasError => Problem is not null;

    /// <summary>
    /// Gets what is wrong, in the language of the interface, or an empty text when nothing is.
    /// </summary>
    public string Error => Problem is { } key ? Loc.Get(key) : string.Empty;

    /// <summary>
    /// Gets the key of the text that says what is wrong: the threshold is not a price, or the instrument already has
    /// another alert with this condition. It is <see langword="null"/> when nothing is wrong.
    /// </summary>
    private string? Problem =>
        !TryGetThreshold(out _) ? "Alert_InvalidThreshold"
        : _owner.IsTaken(_saved.AssetId, Id, SelectedCondition) ? "Alert_DuplicateCondition"
        : null;

    /// <summary>
    /// Gets a value indicating whether there is something to save and it can be saved.
    /// </summary>
    private bool CanSave => IsDirty && !HasError;

    /// <summary>
    /// Reads the typed threshold.
    /// </summary>
    /// <param name="threshold">
    /// The threshold.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the text is a number that can be used with the condition.
    /// </returns>
    private bool TryGetThreshold(out decimal threshold) =>
        AlertsViewModel.TryParseThreshold(ThresholdText, out threshold) && AlertRule.IsValidThreshold(threshold);

    /// <summary>
    /// Determines whether the rule in the settings is the one shown, so that the list does not have to be rebuilt.
    /// </summary>
    /// <param name="rule">
    /// The rule in the settings.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the saved rule is the same.
    /// </returns>
    public bool Matches(AlertRule rule) => _saved.SameAs(rule);

    /// <summary>
    /// Gets or sets a value indicating whether the rule is watched. A change is saved at once.
    /// </summary>
    public bool Enabled
    {
        get => _saved.Enabled;
        set
        {
            if (_saved.Enabled != value)
            {
                _saved.Enabled = value;
                OnPropertyChanged();
                _owner.Change(Id, rule => rule.Enabled = value);
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the rule fires again after its condition stopped and came true again. A
    /// change is saved at once.
    /// </summary>
    public bool Repeat
    {
        get => _saved.Repeat;
        set
        {
            if (_saved.Repeat != value)
            {
                _saved.Repeat = value;
                OnPropertyChanged();
                _owner.Change(Id, rule => rule.Repeat = value);
            }
        }
    }

    /// <summary>
    /// Saves the condition and the threshold that were changed. It cannot run while the threshold cannot be used with the
    /// condition; the row says so.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var condition = SelectedCondition;
        if (!TryGetThreshold(out var threshold))
        {
            return;
        }

        _saved.Condition = condition;
        _saved.Threshold = threshold;
        ThresholdText = FormatThreshold(threshold);
        OnPropertyChanged(nameof(IsDirty));
        SaveCommand.NotifyCanExecuteChanged();
        _owner.Change(Id, rule =>
        {
            rule.Condition = condition;
            rule.Threshold = threshold;
        });
    }

    /// <summary>
    /// Checks the rule again, after the other rules changed.
    /// </summary>
    public void Revalidate()
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(Error));
        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Writes a threshold the way it is shown and typed.
    /// </summary>
    /// <param name="threshold">
    /// The threshold.
    /// </param>
    /// <returns>
    /// The text.
    /// </returns>
    private static string FormatThreshold(decimal threshold) => threshold.ToString("0.########", CultureInfo.InvariantCulture);

    /// <summary>
    /// Deletes the rule.
    /// </summary>
    /// <returns>
    /// A task that completes once the rule is gone.
    /// </returns>
    [RelayCommand]
    private Task DeleteAsync() => _owner.DeleteAsync(this);
}

/// <summary>
/// The alert rules of one instrument, or of all of them, and the form that adds a rule.
/// </summary>
public sealed partial class AlertsViewModel : LocalizedViewModel
{
    /// <summary>
    /// The settings that hold the rules.
    /// </summary>
    private readonly SettingsManager _settings;

    /// <summary>
    /// The available providers, for their names.
    /// </summary>
    private readonly ProviderRegistry _registry;

    /// <summary>
    /// Whether a rule of the list was saved since the rules were checked against each other.
    /// </summary>
    private bool _needsRevalidation;

    /// <summary>
    /// Initializes a new instance of the <see cref="AlertsViewModel"/> class.
    /// </summary>
    /// <param name="settings">
    /// The settings that hold the rules.
    /// </param>
    /// <param name="registry">
    /// The available providers, for their names.
    /// </param>
    /// <param name="ui">
    /// The dispatcher of the UI thread.
    /// </param>
    public AlertsViewModel(SettingsManager settings, ProviderRegistry registry, IUiDispatcher ui)
        : base(ui)
    {
        _settings = settings;
        _registry = registry;
        _settings.Changed += OnSettingsChanged;
        Reload();
    }

    /// <summary>
    /// Gets the rules shown, in the order they were added.
    /// </summary>
    public ObservableCollection<AlertRuleItemViewModel> Items { get; } = [];

    /// <summary>
    /// Gets the instrument the list is limited to, or <see langword="null"/> when it shows the rules of all.
    /// </summary>
    public Asset? Subject { get; private set; }

    /// <summary>
    /// Gets the conditions the form offers.
    /// </summary>
    public IReadOnlyList<AlertCondition> Conditions { get; } = Enum.GetValues<AlertCondition>();

    /// <summary>
    /// Gets the title of the window of an instrument.
    /// </summary>
    public string Title => Loc.Get("Alert_EditorTitle", Subject?.Symbol);

    /// <summary>
    /// Gets a value indicating whether there is no rule to show.
    /// </summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// Gets or sets the condition of the rule to add.
    /// </summary>
    [ObservableProperty]
    public partial AlertCondition SelectedCondition { get; set; } = AlertCondition.PriceAbove;

    /// <summary>
    /// Gets or sets the threshold of the rule to add, as typed.
    /// </summary>
    [ObservableProperty]
    public partial string ThresholdText { get; set; } = string.Empty;

    /// <summary>
    /// Warns about a threshold that cannot be used as soon as it is typed.
    /// </summary>
    /// <param name="value">
    /// The new text.
    /// </param>
    partial void OnThresholdTextChanged(string value) => CheckForm();

    /// <summary>
    /// Warns about a threshold that cannot be used with the condition that was chosen.
    /// </summary>
    /// <param name="value">
    /// The new condition.
    /// </param>
    partial void OnSelectedConditionChanged(AlertCondition value) => CheckForm();

    /// <summary>
    /// Sets the message according to what is typed in the form: nothing while it is empty, a warning while it cannot be
    /// used.
    /// </summary>
    private void CheckForm() =>
        Message = Subject is not null && IsTaken(Subject.Id, string.Empty, SelectedCondition) ? Loc.Get("Alert_DuplicateCondition")
            : ThresholdText.Length == 0 || (TryParseThreshold(ThresholdText, out var threshold) && AlertRule.IsValidThreshold(threshold)) ? string.Empty
            : Loc.Get("Alert_InvalidThreshold");

    /// <summary>
    /// Determines whether an instrument already has a saved alert with a condition, other than a given one. An instrument
    /// has at most one alert above and one below.
    /// </summary>
    /// <param name="asset">
    /// The instrument.
    /// </param>
    /// <param name="ignoreId">
    /// The id of the rule that is being edited and does not count, or an empty text.
    /// </param>
    /// <param name="condition">
    /// The condition.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when another alert has the condition.
    /// </returns>
    public bool IsTaken(AssetId asset, string ignoreId, AlertCondition condition) =>
        _settings.Current.Alerts.Any(r => r.Id != ignoreId && r.AssetId == asset && r.Condition == condition);

    /// <summary>
    /// Gets or sets a value indicating whether the rule to add repeats.
    /// </summary>
    [ObservableProperty]
    public partial bool NewRepeat { get; set; } = true;

    /// <summary>
    /// Gets or sets what is wrong with the typed threshold.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether <see cref="Message"/> has something to say.
    /// </summary>
    public bool HasMessage => Message.Length > 0;

    /// <summary>
    /// Limits the list to one instrument and clears the form.
    /// </summary>
    /// <param name="asset">
    /// The instrument.
    /// </param>
    public void Begin(Asset asset)
    {
        Subject = asset;
        ThresholdText = string.Empty;
        Message = string.Empty;
        NewRepeat = true;

        // The form starts with a condition the instrument has no alert for yet, so that it is ready to use. When it has
        // both, nothing can be added and the form says so.
        SelectedCondition = Conditions.FirstOrDefault(c => !IsTaken(asset.Id, string.Empty, c), AlertCondition.PriceAbove);

        // Setting the condition says nothing when it already was this one, so the warning is asked for.
        CheckForm();
        OnPropertyChanged(nameof(Title));
        Reload(discardEdits: true);
    }

    /// <summary>
    /// Reads the rules from the settings.
    /// </summary>
    /// <param name="discardEdits">
    /// Whether what was typed and not saved is thrown away. Use it when a window is opened, so that it shows the saved
    /// rules and not what was left in it the last time. Otherwise a list that shows these rules already stays as it is,
    /// so that what the user is typing is not lost when the settings change for another reason, for example because a
    /// rule saved or fired.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the list was built again; the rules in a list that was not are as they were.
    /// </returns>
    public bool Reload(bool discardEdits = false)
    {
        var rules = _settings.Current.Alerts.Where(r => Subject is null || r.AssetId == Subject.Id).ToList();
        if (!discardEdits && rules.Count == Items.Count && rules.Zip(Items).All(pair => pair.Second.Matches(pair.First)))
        {
            return false;
        }

        Items.Clear();
        foreach (var rule in rules)
        {
            Items.Add(new AlertRuleItemViewModel(this, rule, ProviderName(rule.ProviderId)));
        }

        OnPropertyChanged(nameof(IsEmpty));
        return true;
    }

    /// <summary>
    /// Parses a typed threshold, in the invariant format or the one of the current culture.
    /// </summary>
    /// <param name="text">
    /// The typed text.
    /// </param>
    /// <param name="value">
    /// The number.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the text is a number.
    /// </returns>
    public static bool TryParseThreshold(string? text, out decimal value)
    {
        var trimmed = text?.Trim();
        return decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
            || decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.CurrentCulture, out value);
    }

    /// <summary>
    /// Adds the rule of the form for the instrument of the list.
    /// </summary>
    /// <returns>
    /// A task that completes once the rule is saved.
    /// </returns>
    [RelayCommand]
    private async Task AddAsync()
    {
        if (Subject is null)
        {
            return;
        }

        if (!TryParseThreshold(ThresholdText, out var threshold) || !AlertRule.IsValidThreshold(threshold))
        {
            Message = Loc.Get("Alert_InvalidThreshold");
            return;
        }

        if (IsTaken(Subject.Id, string.Empty, SelectedCondition))
        {
            Message = Loc.Get("Alert_DuplicateCondition");
            return;
        }

        var rule = new AlertRule
        {
            ProviderId = Subject.ProviderId,
            Symbol = Subject.Symbol,
            Condition = SelectedCondition,
            Threshold = threshold,
            Repeat = NewRepeat,
        };
        ThresholdText = string.Empty;
        Message = string.Empty;
        await _settings.UpdateAsync(s => s.Alerts.Add(rule));
    }

    /// <summary>
    /// Gets the name of a provider in the language of the interface.
    /// </summary>
    /// <param name="providerId">
    /// The provider id.
    /// </param>
    /// <returns>
    /// The display name, or the id when the provider is unknown.
    /// </returns>
    private string ProviderName(string providerId) => ProviderText.Name(_registry, providerId);

    /// <summary>
    /// Changes a saved rule.
    /// </summary>
    /// <param name="id">
    /// The id of the rule.
    /// </param>
    /// <param name="change">
    /// The change to make.
    /// </param>
    public void Change(string id, Action<AlertRule> change)
    {
        _needsRevalidation = true;
        _ = _settings.UpdateAsync(s =>
        {
            if (s.Alerts.Find(r => r.Id == id) is { } rule)
            {
                change(rule);
            }
        });
    }

    /// <summary>
    /// Deletes a rule.
    /// </summary>
    /// <param name="item">
    /// The rule to delete.
    /// </param>
    /// <returns>
    /// A task that completes once the rule is gone.
    /// </returns>
    public Task DeleteAsync(AlertRuleItemViewModel item) => _settings.UpdateAsync(s => s.Alerts.RemoveAll(r => r.Id == item.Id));

    /// <inheritdoc />
    protected override void OnLanguageChanged()
    {
        Message = string.Empty;
        OnPropertyChanged(nameof(Title));
        Reload(discardEdits: true);
    }

    /// <inheritdoc />
    protected override void Detach() => _settings.Changed -= OnSettingsChanged;

    /// <summary>
    /// Shows the rules again after they changed, for example because one switched itself off after it fired.
    /// </summary>
    /// <param name="sender">
    /// The settings manager.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnSettingsChanged(object? sender, EventArgs e) => Ui.Post(ShowChangedRules);

    /// <summary>
    /// Shows the rules again after they changed. A list that was built again is checked as it is built; one that was not
    /// is checked again when a rule of it was saved, because whether a condition is taken depends on the other rules.
    /// </summary>
    private void ShowChangedRules()
    {
        var rebuilt = Reload();
        if (!rebuilt && _needsRevalidation)
        {
            foreach (var item in Items)
            {
                item.Revalidate();
            }
        }

        _needsRevalidation = false;
    }
}
