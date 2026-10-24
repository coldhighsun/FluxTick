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
    /// Gets or sets the price that was shown before the last change of the price, which the view rolls out of the way
    /// when the new price rolls in. It is set when <see cref="RollVersion"/> moves on.
    /// </summary>
    [ObservableProperty]
    public partial string PreviousPriceText { get; set; } = string.Empty;

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
    /// Gets or sets when each of the <see cref="Sparkline"/> prices was traded, or <see langword="null"/> when that is
    /// not known.
    /// </summary>
    [ObservableProperty]
    public partial SparklineTimeline? SparklineTimeline { get; set; }

    /// <summary>
    /// Gets or sets the latest price, which the sparkline places the alert lines by when it has no prices of its own, or
    /// <see langword="null"/> before the first quote.
    /// </summary>
    [ObservableProperty]
    public partial decimal? CurrentPrice { get; set; }

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
    public partial int RollVersion { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the last tick moved the price up.
    /// </summary>
    [ObservableProperty]
    public partial bool RollUp { get; set; }

    /// <summary>
    /// Gets or sets a counter incremented every time an alert of the instrument fires; the view flashes the whole row or
    /// card for a few seconds when it changes. It does not depend on <see cref="RollEnabled"/>.
    /// </summary>
    [ObservableProperty]
    public partial int AlertFlashVersion { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the alert that fired last was the one for a price that rises.
    /// </summary>
    [ObservableProperty]
    public partial bool AlertFlashUp { get; set; }

    /// <summary>
    /// Makes the row or card flash because an alert of the instrument fired.
    /// </summary>
    /// <param name="up">
    /// Whether the alert is the one for a price that rises to its level, which flashes in the color of a gain.
    /// </param>
    public void FlashAlert(bool up)
    {
        AlertFlashUp = up;
        AlertFlashVersion++;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the sparkline is displayed.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowSparkline { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a price change advances <see cref="RollVersion" />. The view stops a
    /// flash that is playing when this turns off.
    /// </summary>
    [ObservableProperty]
    public partial bool RollEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an enabled price alert watches the instrument.
    /// </summary>
    [ObservableProperty]
    public partial bool HasAlert { get; set; }

    /// <summary>
    /// Gets or sets the price of the enabled alert that fires when the price rises to it, or <see langword="null"/> for
    /// none. The sparkline draws it.
    /// </summary>
    [ObservableProperty]
    public partial decimal? AlertAbove { get; set; }

    /// <summary>
    /// Gets or sets how large the card of the instrument is. A small card shows only the name and the price, so the rest
    /// goes into the tooltip.
    /// </summary>
    [ObservableProperty]
    public partial CardSize CardSize { get; set; } = CardSize.Medium;

    /// <summary>
    /// Gets or sets the reference price (previous close or 24h open) the change is measured against, for the large card.
    /// </summary>
    [ObservableProperty]
    public partial string ReferenceText { get; set; } = PriceFormatter.Unavailable;

    /// <summary>
    /// Gets or sets the signed change in price, for the large card.
    /// </summary>
    [ObservableProperty]
    public partial string ChangeAmountText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the lowest price of the chart, for the large card.
    /// </summary>
    [ObservableProperty]
    public partial string LowText { get; set; } = PriceFormatter.Unavailable;

    /// <summary>
    /// Gets or sets the highest price of the chart, for the large card.
    /// </summary>
    [ObservableProperty]
    public partial string HighText { get; set; } = PriceFormatter.Unavailable;

    /// <summary>
    /// Gets a value indicating whether the card is small.
    /// </summary>
    public bool IsSmallCard => CardSize == CardSize.Small;

    /// <summary>
    /// Gets a value indicating whether the card is large.
    /// </summary>
    public bool IsLargeCard => CardSize == CardSize.Large;

    /// <summary>
    /// Rebuilds the tooltip, which tells more on a small card, after the size changed.
    /// </summary>
    /// <param name="value">
    /// The new size.
    /// </param>
    partial void OnCardSizeChanged(CardSize value)
    {
        OnPropertyChanged(nameof(IsSmallCard));
        OnPropertyChanged(nameof(IsLargeCard));
        RefreshTooltip();
    }

    /// <summary>
    /// Rebuilds the tooltip, which names the alert levels of a small card.
    /// </summary>
    /// <param name="value">
    /// The new level.
    /// </param>
    partial void OnAlertAboveChanged(decimal? value) => RefreshTooltip();

    /// <summary>
    /// Rebuilds the tooltip, which names the alert levels of a small card.
    /// </summary>
    /// <param name="value">
    /// The new level.
    /// </param>
    partial void OnAlertBelowChanged(decimal? value) => RefreshTooltip();

    /// <summary>
    /// Builds the tooltip again from the last snapshot.
    /// </summary>
    private void RefreshTooltip()
    {
        if (_lastSnapshot is { Quote: { } quote } snapshot)
        {
            Tooltip = BuildTooltip(snapshot, quote);
        }
    }

    /// <summary>
    /// Gets or sets the price of the enabled alert that fires when the price falls to it, or <see langword="null"/> for
    /// none. The sparkline draws it.
    /// </summary>
    [ObservableProperty]
    public partial decimal? AlertBelow { get; set; }

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
    /// Gets a value indicating whether the prices are not coming in, whatever the reason (stale, reconnecting, failed,
    /// switched off), which the sparkline shows as a gray dashed line from the last price on.
    /// </summary>
    public bool IsStopped => Health is not (AssetHealth.Live or AssetHealth.Waiting);

    /// <summary>
    /// Gets a value indicating whether the displayed data is not trustworthy and should be dimmed, which it is while the
    /// prices are not coming in.
    /// </summary>
    public bool IsDimmed => IsStopped;

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
        SparklineTimeline = snapshot.Timeline;

        if (snapshot.Quote is { } quote)
        {
            var shownBefore = PriceText;
            PriceText = PriceFormatter.FormatPrice(quote.Price, Asset.AssetClass);
            ChangeText = PriceFormatter.FormatPercent(quote.ChangePercent);
            ReferenceText = quote.Reference is { } reference ? PriceFormatter.FormatPrice(reference, Asset.AssetClass) : PriceFormatter.Unavailable;
            ChangeAmountText = quote.Change is { } amount
                ? (amount > 0m ? "+" : string.Empty) + PriceFormatter.FormatPrice(amount, Asset.AssetClass)
                : string.Empty;
            LowText = snapshot.Sparkline.Count > 0 ? PriceFormatter.FormatPrice(snapshot.Sparkline.Min(), Asset.AssetClass) : PriceFormatter.Unavailable;
            HighText = snapshot.Sparkline.Count > 0 ? PriceFormatter.FormatPrice(snapshot.Sparkline.Max(), Asset.AssetClass) : PriceFormatter.Unavailable;
            Direction = quote.Change switch
            {
                > 0m => PriceDirection.Up,
                < 0m => PriceDirection.Down,
                _ => PriceDirection.Flat,
            };
            Tooltip = BuildTooltip(snapshot, quote);

            // A change that does not show in the text, because of the rounding, has nothing to roll.
            if (RollEnabled && CurrentPrice is { } previous && previous != quote.Price && shownBefore != PriceText)
            {
                // The old price is set before the version moves on: the view reads it when the version changes.
                PreviousPriceText = shownBefore;
                RollUp = quote.Price > previous;
                RollVersion++;
            }

            CurrentPrice = quote.Price;
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
    partial void OnHealthChanged(AssetHealth value)
    {
        OnPropertyChanged(nameof(IsStopped));
        OnPropertyChanged(nameof(IsDimmed));
    }

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
        var small = CardSize == CardSize.Small;
        var lines = new List<string>
        {
            // A small card shows only the symbol and the price, so the tooltip tells the rest.
            small ? $"{Asset.DisplayName} ({Asset.Symbol})" : Asset.DisplayName,
            small
                ? $"{ClassLabel} · {ProviderName} · {quote.Timestamp.ToLocalTime():HH:mm:ss}"
                : $"{ProviderName} · {quote.Timestamp.ToLocalTime():HH:mm:ss}",
        };
        if (quote.Change is { } change)
        {
            lines.Add(Loc.Get("Row_Change", PriceFormatter.FormatPrice(change, Asset.AssetClass), PriceFormatter.FormatPercent(quote.ChangePercent)));
        }

        if (small && snapshot.Health != AssetHealth.Live && StatusOf(snapshot) is { Length: > 0 } status)
        {
            lines.Add(status);
        }

        if (!string.IsNullOrEmpty(snapshot.HealthDetail))
        {
            lines.Add(snapshot.HealthDetail);
        }

        if (small)
        {
            if (AlertAbove is { } above)
            {
                lines.Add(Loc.Get("Row_AlertAbove", PriceFormatter.FormatPrice(above, Asset.AssetClass)));
            }

            if (AlertBelow is { } below)
            {
                lines.Add(Loc.Get("Row_AlertBelow", PriceFormatter.FormatPrice(below, Asset.AssetClass)));
            }
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
