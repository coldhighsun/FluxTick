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
    /// Whether the prices of the snapshot shown last stopped because the market is shut, so that they still count in a
    /// total. It is only so while the trading state is shown; with it off the prices are stale again.
    /// </summary>
    private bool _isClosingPrice;

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
        TooltipContent = new RowTooltip(this);
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
    /// Gets or sets the daily change in percent that the list is sorted by, or <see langword="null"/> before the first
    /// quote or when the quote has no reference price.
    /// </summary>
    [ObservableProperty]
    public partial decimal? ChangePercent { get; set; }

    /// <summary>
    /// Gets the profit or loss of the holding in percent, which the list can be sorted by, or <see langword="null"/>
    /// without a holding or before the first quote.
    /// </summary>
    public decimal? PnlPercent => Pnl?.Percent;

    /// <summary>
    /// Determines whether any of the rows has a holding.
    /// </summary>
    /// <param name="rows">
    /// The rows.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when at least one row has a holding.
    /// </returns>
    public static bool AnyHolding(IEnumerable<AssetRowViewModel> rows) => rows.Any(row => row.HasHolding);

    /// <summary>
    /// Gets a value indicating whether the market of the instrument is shut according to its trading hours, so that
    /// <see cref="ChangePercent"/> is the change of the last session. Unlike the closing price shown in the row, it does
    /// not depend on the setting that shows the trading state: the order of the list must not change with a display setting.
    /// </summary>
    public bool IsMarketShut { get; private set; }

    /// <summary>
    /// Gets or sets the price that was shown before the last change of the price, which the view rolls out of the way
    /// when the new price rolls in. It is set when <see cref="RollVersion"/> moves on.
    /// </summary>
    [ObservableProperty]
    public partial string PreviousPriceText { get; set; } = string.Empty;

    /// <summary>
    /// Gets the content the views show as the tooltip. It asks for <see cref="Tooltip"/> only while the tooltip is open.
    /// </summary>
    public RowTooltip TooltipContent { get; }

    /// <summary>
    /// Gets the tooltip with details. It is worked out when it is read; every price only announces that it changed.
    /// </summary>
    public string Tooltip => _lastSnapshot switch
    {
        null => string.Empty,
        { Quote: { } quote } snapshot => BuildTooltip(snapshot, quote),
        _ => $"{Asset.DisplayName}\n{ProviderName}",
    };

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
    /// Gets or sets a value indicating whether <see cref="StatusText"/> only says that the market is shut, which is no
    /// fault, so that it is not drawn as a warning.
    /// </summary>
    [ObservableProperty]
    public partial bool StatusIsNeutral { get; set; }

    /// <summary>
    /// Gets or sets the label of the trading state of the market when it is not the regular session (pre-market,
    /// after hours, break, closed), or an empty string. The chip beside the class shows it.
    /// </summary>
    [ObservableProperty]
    public partial string MarketBadgeText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the text of the chip that the list row shows beside the name: the trading state of the market, also
    /// while it is shut, or the note that the data is not live. It is empty when there is nothing to tell. The list row
    /// has no room for a status line, so it tells the state here and keeps the change in percent in view.
    /// </summary>
    [ObservableProperty]
    public partial string RowBadgeText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="RowBadgeText"/> is a warning about the data and not the trading
    /// state of the market.
    /// </summary>
    [ObservableProperty]
    public partial bool RowBadgeIsWarn { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the trading state of the market is shown.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowMarketStatus { get; set; } = true;

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
    /// Gets or sets a value indicating whether the tray list shows the instrument.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddToTray))]
    [NotifyPropertyChangedFor(nameof(CanRemoveFromTray))]
    [NotifyPropertyChangedFor(nameof(ShowTrayMarker))]
    public partial bool InTray { get; set; }

    /// <summary>
    /// Gets a value indicating whether the menu offers to put the instrument on the tray list. The menus only offer the
    /// tray list where it works, which the platform decides for all rows alike.
    /// </summary>
    public bool CanAddToTray => TrayPopupAvailability.IsSupported && !InTray;

    /// <summary>
    /// Gets a value indicating whether the menu offers to take the instrument off the tray list.
    /// </summary>
    public bool CanRemoveFromTray => TrayPopupAvailability.IsSupported && InTray;

    /// <summary>
    /// Gets a value indicating whether the row and the card mark the instrument as shown in the tray list. It is kept apart
    /// from <see cref="CanRemoveFromTray"/>, which only says what the menu offers.
    /// </summary>
    public bool ShowTrayMarker => TrayPopupAvailability.IsSupported && InTray;

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
    /// The price the user paid per unit, or <see langword="null"/> without a holding.
    /// </summary>
    private decimal? _costPrice;

    /// <summary>
    /// The number of units the user holds, or <see langword="null"/> without a holding.
    /// </summary>
    private decimal? _quantity;

    /// <summary>
    /// Gets the currency of the holding.
    /// </summary>
    public string HoldingCurrency { get; private set; } = CurrencyCodes.Pivot;

    /// <summary>
    /// Gets the profit and loss of the holding at the latest price, or <see langword="null"/> without a holding or a
    /// price. It is in <see cref="HoldingCurrency"/>.
    /// </summary>
    public HoldingPnl? Pnl { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user holds the instrument.
    /// </summary>
    [ObservableProperty]
    public partial bool HasHolding { get; set; }

    /// <summary>
    /// Gets what the user holds and paid, such as <c>100 × 10.00 CNY</c>.
    /// </summary>
    public string HoldingText =>
        HasHolding ? $"{PriceFormatter.FormatQuantity(_quantity!.Value)} × {PriceFormatter.FormatPrice(_costPrice!.Value, Asset.AssetClass)} {HoldingCurrency}" : string.Empty;

    /// <summary>
    /// Gets the price paid per unit, such as <c>100.00</c>, for the large card. The currency is in the tooltip.
    /// </summary>
    public string CostPriceText => HasHolding ? PriceFormatter.FormatPrice(_costPrice!.Value, Asset.AssetClass) : string.Empty;

    /// <summary>
    /// Gets the profit or loss of the holding in money only, such as <c>+1,200.00</c>, for the small card.
    /// </summary>
    public string PnlAmountText => Pnl is { } pnl ? PriceFormatter.FormatSignedMoney(pnl.Amount) : PriceFormatter.Unavailable;

    /// <summary>
    /// Gets the profit or loss of the holding in percent only, such as <c>+20.00%</c>, for the tray popup.
    /// </summary>
    public string PnlPercentText => Pnl is { } pnl ? PriceFormatter.FormatPercent(pnl.Percent) : PriceFormatter.Unavailable;

    /// <summary>
    /// Gets the profit or loss of the holding in money and percent without the currency, such as
    /// <c>+1,200.00 (+20.00%)</c>, for the other cards, which have little room.
    /// </summary>
    public string PnlCompactText => Pnl is null ? PriceFormatter.Unavailable : $"{PnlAmountText} ({PnlPercentText})";

    /// <summary>
    /// Gets the profit or loss of the holding in money and percent, such as <c>+1,200.00 CNY (+20.00%)</c>.
    /// </summary>
    public string PnlText => Pnl is null ? PriceFormatter.Unavailable : $"{PnlAmountText} {HoldingCurrency} ({PnlPercentText})";

    /// <summary>
    /// Gets or sets a value indicating whether the holding is in profit.
    /// </summary>
    [ObservableProperty]
    public partial bool IsProfit { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the holding is at a loss.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoss { get; set; }

    /// <summary>
    /// Sets what the user holds of the instrument.
    /// </summary>
    /// <param name="costPrice">
    /// The price paid per unit, or <see langword="null"/> for no holding.
    /// </param>
    /// <param name="quantity">
    /// The number of units held, or <see langword="null"/> for no holding.
    /// </param>
    /// <param name="currency">
    /// The currency of the cost price.
    /// </param>
    public void SetHolding(decimal? costPrice, decimal? quantity, string currency)
    {
        if (_costPrice == costPrice && _quantity == quantity && HoldingCurrency == currency)
        {
            return;
        }

        _costPrice = costPrice;
        _quantity = quantity;
        HoldingCurrency = currency;
        ApplyHolding(CurrentPrice, force: true);
        RefreshTooltip();
    }

    /// <summary>
    /// Works out the profit and loss at a price and the texts that show them.
    /// </summary>
    /// <param name="price">
    /// The latest price, or <see langword="null"/> before the first quote.
    /// </param>
    /// <param name="force">
    /// Whether the texts are announced as changed even when the figures are the same, because the holding itself changed.
    /// </param>
    private void ApplyHolding(decimal? price, bool force = false)
    {
        // The texts are worked out where they are read, so a price that moves only costs what is on screen. Here only
        // the flags are set and the bindings are told what changed.
        var before = Pnl;
        var hadHolding = HasHolding;
        if (!force && !hadHolding && !HoldingPnl.IsUsable(_costPrice, _quantity))
        {
            // Most instruments are not held: nothing to work out for them on every price.
            return;
        }

        HasHolding = HoldingPnl.IsUsable(_costPrice, _quantity);
        Pnl = HasHolding ? HoldingPnl.Compute(price, _costPrice, _quantity) : null;
        IsProfit = Pnl is { Amount: > 0m };
        IsLoss = Pnl is { Amount: < 0m };
        if (force || before != Pnl || hadHolding != HasHolding)
        {
            OnPropertyChanged(nameof(HoldingText));
            OnPropertyChanged(nameof(CostPriceText));
            OnPropertyChanged(nameof(PnlAmountText));
            OnPropertyChanged(nameof(PnlPercentText));
            OnPropertyChanged(nameof(PnlCompactText));
            OnPropertyChanged(nameof(PnlText));
            OnPropertyChanged(nameof(PnlPercent));
        }
    }

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
    /// Announces that the tooltip changed, after something it tells changed.
    /// </summary>
    private void RefreshTooltip()
    {
        OnPropertyChanged(nameof(Tooltip));
        TooltipContent.Refresh();
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
    /// Gets a value indicating whether the chip with the trading state of the market is shown.
    /// </summary>
    public bool HasMarketBadge => MarketBadgeText.Length > 0;

    /// <summary>
    /// Gets a value indicating whether the chip of the list row is shown.
    /// </summary>
    public bool HasRowBadge => RowBadgeText.Length > 0;

    /// <summary>
    /// Gets the change in percent as the list row shows it: the change of the last quote, which for a shut market is
    /// the change of the last session, or a dash before the first quote.
    /// </summary>
    public string ChangeDisplayText => ChangeText.Length > 0 ? ChangeText : PriceFormatter.Unavailable;

    /// <summary>
    /// Gets a value indicating whether the prices are not coming in, whatever the reason (stale, reconnecting, failed,
    /// switched off), which the sparkline shows as a gray dashed line from the last price on. Delayed prices keep coming
    /// in, only with an old trade time, so they do not count.
    /// </summary>
    public bool IsStopped => Health is not (AssetHealth.Live or AssetHealth.Waiting or AssetHealth.Delayed);

    /// <summary>
    /// Gets a value indicating whether the last price is too old to count in a total: the prices went stale, the source
    /// failed or is off or missing. A source that is reconnecting is not outdated yet, so that a short break does not
    /// make the holdings of a whole source drop out of the total and come back. Prices that stopped because the market
    /// is shut are the closing prices and still count, while the trading state is shown. Delayed prices are still the
    /// latest the source delivers, so they count too.
    /// </summary>
    public bool IsPriceOutdated =>
        Health is AssetHealth.StaleReconnecting or AssetHealth.Failed or AssetHealth.ProviderDisabled or AssetHealth.ProviderMissing
        || (Health == AssetHealth.Stale && !_isClosingPrice);

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
        IsMarketShut = IsClosingPrice(snapshot);

        // It is set before the health, whose change notifies IsPriceOutdated, so that no listener sees the two out of step.
        var closing = ShowMarketStatus && IsClosingPrice(snapshot);
        var closingChanged = closing != _isClosingPrice;
        _isClosingPrice = closing;
        Health = snapshot.Health;
        if (closingChanged)
        {
            OnPropertyChanged(nameof(IsPriceOutdated));
        }

        ApplyMarketStatus(snapshot);
        Sparkline = snapshot.Sparkline;
        SparklineTimeline = snapshot.Timeline;

        if (snapshot.Quote is { } quote)
        {
            var shownBefore = PriceText;
            PriceText = PriceFormatter.FormatPrice(quote.Price, Asset.AssetClass);
            ChangeText = PriceFormatter.FormatPercent(quote.ChangePercent);
            ChangePercent = quote.ChangePercent;
            ReferenceText = quote.Reference is { } reference ? PriceFormatter.FormatPrice(reference, Asset.AssetClass) : PriceFormatter.Unavailable;
            ChangeAmountText = quote.Change is { } amount
                ? (amount > 0m ? "+" : string.Empty) + PriceFormatter.FormatPrice(amount, Asset.AssetClass)
                : string.Empty;
            var sparkline = snapshot.Sparkline;
            if (sparkline.Count > 0)
            {
                // One pass instead of Min() and Max(), which enumerate through an allocated enumerator each.
                var low = sparkline[0];
                var high = low;
                for (var i = 1; i < sparkline.Count; i++)
                {
                    low = Math.Min(low, sparkline[i]);
                    high = Math.Max(high, sparkline[i]);
                }

                LowText = PriceFormatter.FormatPrice(low, Asset.AssetClass);
                HighText = PriceFormatter.FormatPrice(high, Asset.AssetClass);
            }
            else
            {
                LowText = PriceFormatter.Unavailable;
                HighText = PriceFormatter.Unavailable;
            }

            Direction = quote.Change switch
            {
                > 0m => PriceDirection.Up,
                < 0m => PriceDirection.Down,
                _ => PriceDirection.Flat,
            };
            ApplyHolding(quote.Price);
            RefreshTooltip();

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
            RefreshTooltip();
        }
    }

    /// <summary>
    /// Works out the status text and the market chip from the health and the trading state of the market.
    /// </summary>
    /// <param name="snapshot">
    /// The snapshot.
    /// </param>
    private void ApplyMarketStatus(AssetSnapshot snapshot)
    {
        var market = ShowMarketStatus ? snapshot.MarketState : MarketState.Unknown;

        // A market that is shut is told as such instead of "stale": the prices stopped because nothing trades, not because
        // the data is bad. The snapshot only says so while prices are still being received; a source that went quiet keeps
        // the warning.
        var shut = _isClosingPrice;
        var chip = !shut && market is MarketState.PreMarket or MarketState.AfterHours or MarketState.Break or MarketState.Closed;
        StatusIsNeutral = shut;
        StatusText = shut ? MarketLabel(snapshot.MarketState) : StatusOf(snapshot);
        MarketBadgeText = chip ? MarketLabel(market) : string.Empty;

        // The list row has one chip: a shut market first, then a fault of the data, then the other trading states. Delayed
        // prices are no fault, so they only get the chip when there is no trading state to tell.
        var isFault = !shut && StatusText.Length > 0 && snapshot.Health != AssetHealth.Delayed;
        var showStatus = shut || isFault || MarketBadgeText.Length == 0;
        RowBadgeText = showStatus ? StatusText : MarketBadgeText;
        RowBadgeIsWarn = showStatus && !StatusIsNeutral && StatusText.Length > 0;
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
    /// Notifies bindings of derived properties when the market chip changes.
    /// </summary>
    /// <param name="value">
    /// The new label.
    /// </param>
    partial void OnMarketBadgeTextChanged(string value) => OnPropertyChanged(nameof(HasMarketBadge));

    /// <summary>
    /// Notifies bindings of derived properties when the chip of the list row changes.
    /// </summary>
    /// <param name="value">
    /// The new text.
    /// </param>
    partial void OnRowBadgeTextChanged(string value) => OnPropertyChanged(nameof(HasRowBadge));

    /// <summary>
    /// Notifies bindings of derived properties when the change text changes.
    /// </summary>
    /// <param name="value">
    /// The new text.
    /// </param>
    partial void OnChangeTextChanged(string value) => OnPropertyChanged(nameof(ChangeDisplayText));

    /// <summary>
    /// Shows the instrument again after the trading state was turned on or off.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnShowMarketStatusChanged(bool value)
    {
        if (_lastSnapshot is { } snapshot)
        {
            var closing = value && IsClosingPrice(snapshot);
            if (closing != _isClosingPrice)
            {
                _isClosingPrice = closing;
                OnPropertyChanged(nameof(IsPriceOutdated));
            }

            ApplyMarketStatus(snapshot);
            RefreshTooltip();
        }
    }

    /// <summary>
    /// Gets the label of a trading state.
    /// </summary>
    /// <param name="state">
    /// The state.
    /// </param>
    /// <returns>
    /// The label.
    /// </returns>
    private static string MarketLabel(MarketState state) => Loc.Get($"MarketState_{state}");

    /// <summary>
    /// Determines whether the prices of a snapshot stopped because the market is shut, so that they are the closing prices.
    /// </summary>
    /// <param name="snapshot">
    /// The snapshot.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the prices are live, stale or delayed and the market is closed or on a break.
    /// </returns>
    private static bool IsClosingPrice(AssetSnapshot snapshot) =>
        // The trading hours decide first: a source may go on sending the last price of a shut market, which looks live.
        (snapshot.Health is AssetHealth.Live or AssetHealth.Stale or AssetHealth.Delayed)
        && (snapshot.MarketState is MarketState.Closed or MarketState.Break);

    /// <summary>
    /// Notifies bindings of derived properties when the health changes.
    /// </summary>
    /// <param name="value">
    /// The new health.
    /// </param>
    partial void OnHealthChanged(AssetHealth value)
    {
        OnPropertyChanged(nameof(IsStopped));
        OnPropertyChanged(nameof(IsPriceOutdated));
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
        AssetHealth.Delayed => Loc.Get("Status_Delayed"),
        AssetHealth.StaleReconnecting => Loc.Get("Status_StaleReconnecting"),
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

        if (HasHolding)
        {
            lines.Add(Loc.Get("Row_HoldingTip", HoldingText));
            if (Pnl is { } pnl)
            {
                lines.Add(Loc.Get("Row_PnlTip", PriceFormatter.FormatMoney(pnl.MarketValue), HoldingCurrency, PnlText));
            }
        }

        if (small && snapshot.Health != AssetHealth.Live && StatusText.Length > 0)
        {
            lines.Add(StatusText);
        }

        // The small card has no chip, so its tooltip tells the state of the market.
        if (small && HasMarketBadge)
        {
            lines.Add(MarketBadgeText);
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
