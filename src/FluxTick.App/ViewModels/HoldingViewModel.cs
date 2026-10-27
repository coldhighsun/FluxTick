using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxTick.Abstractions;
using FluxTick.App.Localization;
using FluxTick.App.Services;
using FluxTick.Core.Application;

namespace FluxTick.App.ViewModels;

/// <summary>
/// The form in which the user says what they hold of one instrument and what it cost.
/// </summary>
public sealed partial class HoldingViewModel : LocalizedViewModel
{
    /// <summary>
    /// The settings that hold the holdings.
    /// </summary>
    private readonly SettingsManager _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="HoldingViewModel"/> class.
    /// </summary>
    /// <param name="settings">
    /// The settings that hold the holdings.
    /// </param>
    /// <param name="ui">
    /// The dispatcher of the UI thread.
    /// </param>
    public HoldingViewModel(SettingsManager settings, IUiDispatcher ui)
        : base(ui)
    {
        _settings = settings;
    }

    /// <summary>
    /// Raised when the form is done, so that the window closes.
    /// </summary>
    public event EventHandler? Completed;

    /// <summary>
    /// Gets the instrument the form is for.
    /// </summary>
    public Asset? Subject { get; private set; }

    /// <summary>
    /// Gets the currencies to choose from.
    /// </summary>
    public IReadOnlyList<string> Currencies { get; } = CurrencyCodes.Supported;

    /// <summary>
    /// Gets the title of the window.
    /// </summary>
    public string Title => Loc.Get("Holding_EditorTitle", Subject is { DisplayName.Length: > 0 } ? Subject.DisplayName : Subject?.Symbol);

    /// <summary>
    /// Gets or sets the price paid per unit, as typed.
    /// </summary>
    [ObservableProperty]
    public partial string CostText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of units held, as typed.
    /// </summary>
    [ObservableProperty]
    public partial string QuantityText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the currency of the cost price.
    /// </summary>
    [ObservableProperty]
    public partial string SelectedCurrency { get; set; } = CurrencyCodes.Pivot;

    /// <summary>
    /// Gets or sets what is wrong with the typed values.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the instrument has a saved holding, which can be cleared.
    /// </summary>
    [ObservableProperty]
    public partial bool CanClear { get; set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Message"/> has something to say.
    /// </summary>
    public bool HasMessage => Message.Length > 0;

    /// <summary>
    /// Clears the message as soon as the cost is changed.
    /// </summary>
    /// <param name="value">
    /// The new text.
    /// </param>
    partial void OnCostTextChanged(string value) => Message = string.Empty;

    /// <summary>
    /// Clears the message as soon as the quantity is changed.
    /// </summary>
    /// <param name="value">
    /// The new text.
    /// </param>
    partial void OnQuantityTextChanged(string value) => Message = string.Empty;

    /// <summary>
    /// Loads the saved holding of an instrument into the form.
    /// </summary>
    /// <param name="asset">
    /// The instrument.
    /// </param>
    /// <param name="currentPrice">
    /// The latest price, which fills the cost price when the instrument has no holding yet, or <see langword="null"/>
    /// when there is no price.
    /// </param>
    public void Begin(Asset asset, decimal? currentPrice = null)
    {
        Subject = asset;
        var entry = _settings.Current.Watchlist.Find(e => e.ProviderId == asset.ProviderId && e.Symbol == asset.Symbol);
        CanClear = entry?.HasHolding == true;
        CostText = entry?.CostPrice is { } cost ? NumberText.Format(cost)
            : currentPrice is > 0m ? NumberText.Format(currentPrice.Value)
            : string.Empty;
        QuantityText = entry?.Quantity is { } quantity ? NumberText.Format(quantity) : string.Empty;
        SelectedCurrency = entry?.HoldingCurrency ?? CurrencyGuesser.Guess(asset.ProviderId, asset.Symbol);
        Message = string.Empty;
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>
    /// Saves the holding of the form.
    /// </summary>
    /// <returns>
    /// A task that completes once the holding is saved.
    /// </returns>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Subject is not { } asset)
        {
            return;
        }

        if (!NumberText.TryParse(CostText, out var cost) || cost <= 0m)
        {
            Message = Loc.Get("Holding_InvalidCost");
            return;
        }

        if (!NumberText.TryParse(QuantityText, out var quantity) || quantity <= 0m)
        {
            Message = Loc.Get("Holding_InvalidQuantity");
            return;
        }

        var currency = SelectedCurrency;
        await WriteAsync(asset, cost, quantity, currency);
        Completed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Removes the saved holding.
    /// </summary>
    /// <returns>
    /// A task that completes once the holding is gone.
    /// </returns>
    [RelayCommand]
    private async Task ClearAsync()
    {
        if (Subject is not { } asset)
        {
            return;
        }

        await WriteAsync(asset, null, null, null);
        Completed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Writes the holding of an instrument to the settings.
    /// </summary>
    /// <param name="asset">
    /// The instrument.
    /// </param>
    /// <param name="cost">
    /// The price paid per unit, or <see langword="null"/> for no holding.
    /// </param>
    /// <param name="quantity">
    /// The number of units, or <see langword="null"/> for no holding.
    /// </param>
    /// <param name="currency">
    /// The currency of the cost price, or <see langword="null"/> for no holding.
    /// </param>
    /// <returns>
    /// A task that completes once the settings are saved.
    /// </returns>
    private Task WriteAsync(Asset asset, decimal? cost, decimal? quantity, string? currency) =>
        _settings.UpdateAsync(s =>
        {
            if (s.Watchlist.Find(e => e.ProviderId == asset.ProviderId && e.Symbol == asset.Symbol) is { } entry)
            {
                entry.CostPrice = cost;
                entry.Quantity = quantity;
                entry.HoldingCurrency = currency;
            }
        });

    /// <inheritdoc />
    protected override void OnLanguageChanged()
    {
        Message = string.Empty;
        OnPropertyChanged(nameof(Title));
    }
}
