using CommunityToolkit.Mvvm.ComponentModel;
using FluxTick.Abstractions;
using FluxTick.App.Localization;
using FluxTick.App.Services;
using FluxTick.Core.Application;

namespace FluxTick.App.ViewModels;

/// <summary>
/// The direction of a price change.
/// </summary>
public enum PriceDirection
{
    /// <summary>
    /// No change or unknown.
    /// </summary>
    Flat,

    /// <summary>
    /// The price is above its reference.
    /// </summary>
    Up,

    /// <summary>
    /// The price is below its reference.
    /// </summary>
    Down,
}

/// <summary>
/// One row of the watchlist: formatted values of an instrument ready for display.
/// </summary>
public sealed partial class AssetRowViewModel : ObservableObject
{
    /// <summary>
    /// The last price shown, used to detect tick direction for the flash effect.
    /// </summary>
    private decimal? _lastPrice;

    /// <summary>
    /// The snapshot shown last, kept to build the texts again when the language changes.
    /// </summary>
    private AssetSnapshot? _lastSnapshot;

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetRowViewModel"/> class.
    /// </summary>
    /// <param name="asset">
    /// The instrument shown by the row.
    /// </param>
    /// <param name="providerName">
    /// The display name of the data source.
    /// </param>
    public AssetRowViewModel(Asset asset, string providerName)
    {
        Asset = asset;
        ProviderName = providerName;
        ClassLabel = LabelOf(asset.AssetClass);
        Sparkline = [];
    }

    /// <summary>
    /// Gets the instrument shown by the row.
    /// </summary>
    public Asset Asset { get; }

    /// <summary>
    /// Gets the symbol shown as the row title.
    /// </summary>
    public string Symbol => Asset.Symbol;

    /// <summary>
    /// Gets the secondary line, usually the instrument name.
    /// </summary>
    public string Name => Asset.DisplayName;

    /// <summary>
    /// Gets the short label of the asset class.
    /// </summary>
    public string ClassLabel { get; private set; }

    /// <summary>
    /// Gets the display name of the data source.
    /// </summary>
    public string ProviderName { get; private set; }

    /// <summary>
    /// Gets or sets the id of the user-defined group the instrument belongs to, or <see langword="null"/> for none.
    /// </summary>
    public string? GroupId { get; set; }

    /// <summary>
    /// Gets or sets the formatted price.
    /// </summary>
    [ObservableProperty]
    public partial string PriceText { get; set; } = PriceFormatter.Unavailable;

    /// <summary>
    /// Gets or sets the formatted percentage change.
    /// </summary>
    [ObservableProperty]
    public partial string ChangeText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tooltip with details.
    /// </summary>
    [ObservableProperty]
    public partial string Tooltip { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the direction of the change against the reference price.
    /// </summary>
    [ObservableProperty]
    public partial PriceDirection Direction { get; set; }

    /// <summary>
    /// Gets or sets the recent prices for the sparkline.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<decimal> Sparkline { get; set; }

    /// <summary>
    /// Gets or sets the data health.
    /// </summary>
    [ObservableProperty]
    public partial AssetHealth Health { get; set; } = AssetHealth.Waiting;

    /// <summary>
    /// Gets or sets a short note shown when the data is not live, such as <c>Stale</c>.
    /// </summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a counter incremented on every price change; the view plays a flash animation when it changes.
    /// </summary>
    [ObservableProperty]
    public partial int FlashVersion { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the last tick moved the price up.
    /// </summary>
    [ObservableProperty]
    public partial bool FlashUp { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sparkline is displayed.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowSparkline { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a price change advances <see cref="FlashVersion" />. The view stops a
    /// flash that is playing when this turns off.
    /// </summary>
    [ObservableProperty]
    public partial bool FlashEnabled { get; set; }

    /// <summary>
    /// Gets a value indicating whether the change is positive.
    /// </summary>
    public bool IsUp => Direction == PriceDirection.Up;

    /// <summary>
    /// Gets a value indicating whether the change is negative.
    /// </summary>
    public bool IsDown => Direction == PriceDirection.Down;

    /// <summary>
    /// Gets a value indicating whether a status note is shown instead of the change.
    /// </summary>
    public bool HasStatus => StatusText.Length > 0;

    /// <summary>
    /// Gets a value indicating whether the displayed data is not trustworthy and should be dimmed.
    /// </summary>
    public bool IsDimmed => Health is not (AssetHealth.Live or AssetHealth.Waiting);

    /// <summary>
    /// Applies a new snapshot of the instrument.
    /// </summary>
    /// <param name="snapshot">
    /// The snapshot to show.
    /// </param>
    public void Update(AssetSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        Health = snapshot.Health;
        StatusText = StatusOf(snapshot);
        Sparkline = snapshot.Sparkline;

        if (snapshot.Quote is { } quote)
        {
            PriceText = PriceFormatter.FormatPrice(quote.Price, Asset.AssetClass);
            ChangeText = PriceFormatter.FormatPercent(quote.ChangePercent);
            Direction = quote.Change switch
            {
                > 0m => PriceDirection.Up,
                < 0m => PriceDirection.Down,
                _ => PriceDirection.Flat,
            };
            Tooltip = BuildTooltip(snapshot, quote);

            if (FlashEnabled && _lastPrice is { } previous && previous != quote.Price)
            {
                FlashUp = quote.Price > previous;
                FlashVersion++;
            }

            _lastPrice = quote.Price;
        }
        else
        {
            Tooltip = $"{Asset.DisplayName}\n{ProviderName}";
        }
    }

    /// <summary>
    /// Builds the texts that depend on the language again after the language changed.
    /// </summary>
    /// <param name="providerName">
    /// The display name of the data source in the new language.
    /// </param>
    public void RefreshLanguage(string providerName)
    {
        ProviderName = providerName;
        OnPropertyChanged(nameof(ProviderName));
        ClassLabel = LabelOf(Asset.AssetClass);
        OnPropertyChanged(nameof(ClassLabel));
        if (_lastSnapshot is { } snapshot)
        {
            Update(snapshot);
        }
    }

    /// <summary>
    /// Notifies bindings of derived properties when their source changes.
    /// </summary>
    /// <param name="value">
    /// The new direction.
    /// </param>
    partial void OnDirectionChanged(PriceDirection value)
    {
        OnPropertyChanged(nameof(IsUp));
        OnPropertyChanged(nameof(IsDown));
    }

    /// <summary>
    /// Notifies bindings of derived properties when the status text changes.
    /// </summary>
    /// <param name="value">
    /// The new status text.
    /// </param>
    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    /// <summary>
    /// Notifies bindings of derived properties when the health changes.
    /// </summary>
    /// <param name="value">
    /// The new health.
    /// </param>
    partial void OnHealthChanged(AssetHealth value) => OnPropertyChanged(nameof(IsDimmed));

    /// <summary>
    /// Derives the short status note of a snapshot.
    /// </summary>
    /// <param name="snapshot">
    /// The snapshot.
    /// </param>
    /// <returns>
    /// The note, or an empty string when the data is live.
    /// </returns>
    private static string StatusOf(AssetSnapshot snapshot) => snapshot.Health switch
    {
        AssetHealth.Stale => Loc.Get("Status_Stale"),
        AssetHealth.Reconnecting => Loc.Get("Provider_Reconnecting"),
        AssetHealth.Failed => Loc.Get("Status_SourceError"),
        AssetHealth.ProviderDisabled => Loc.Get("Status_SourceOff"),
        AssetHealth.ProviderMissing => Loc.Get("Status_NoSource"),
        _ => string.Empty,
    };

    /// <summary>
    /// Builds the tooltip text of a quote.
    /// </summary>
    /// <param name="snapshot">
    /// The snapshot.
    /// </param>
    /// <param name="quote">
    /// The latest quote.
    /// </param>
    /// <returns>
    /// The tooltip text.
    /// </returns>
    private string BuildTooltip(AssetSnapshot snapshot, Quote quote)
    {
        var lines = new List<string>
        {
            Asset.DisplayName,
            $"{ProviderName} · {quote.Timestamp.ToLocalTime():HH:mm:ss}",
        };
        if (quote.Change is { } change)
        {
            lines.Add(Loc.Get("Row_Change", PriceFormatter.FormatPrice(change, Asset.AssetClass), PriceFormatter.FormatPercent(quote.ChangePercent)));
        }

        if (!string.IsNullOrEmpty(snapshot.HealthDetail))
        {
            lines.Add(snapshot.HealthDetail);
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Gets the short label of an asset class.
    /// </summary>
    /// <param name="assetClass">
    /// The asset class.
    /// </param>
    /// <returns>
    /// The label.
    /// </returns>
    private static string LabelOf(AssetClass assetClass) => Loc.Get($"ClassChip_{assetClass}");
}
