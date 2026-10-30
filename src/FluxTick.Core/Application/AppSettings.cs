using System.Text.Json;
using System.Text.Json.Serialization;
using FluxTick.Abstractions;

namespace FluxTick.Core.Application;

/// <summary>
/// Which colors mean "up" and "down".
/// </summary>
public enum ColorConvention
{
    /// <summary>
    /// Follow the system language: red-up for Chinese, green-up otherwise.
    /// </summary>
    Auto,

    /// <summary>
    /// Green for gains, red for losses.
    /// </summary>
    GreenUp,

    /// <summary>
    /// Red for gains, green for losses.
    /// </summary>
    RedUp,
}

/// <summary>
/// Which color scheme the app uses.
/// </summary>
public enum ThemeMode
{
    /// <summary>
    /// Follow the system's light or dark setting.
    /// </summary>
    Auto,

    /// <summary>
    /// Always light.
    /// </summary>
    Light,

    /// <summary>
    /// Always dark.
    /// </summary>
    Dark,
}

/// <summary>
/// Which language the interface is shown in.
/// </summary>
public enum LanguageMode
{
    /// <summary>
    /// Follow the system language: Chinese systems get Simplified Chinese, everything else English.
    /// </summary>
    Auto,

    /// <summary>
    /// Always Simplified Chinese.
    /// </summary>
    ZhCN,

    /// <summary>
    /// Always English.
    /// </summary>
    En,
}

/// <summary>
/// How the watchlist is laid out in the main window.
/// </summary>
public enum WatchlistLayout
{
    /// <summary>
    /// One row per instrument in a single column.
    /// </summary>
    List,

    /// <summary>
    /// Fixed-size cards that flow into as many columns as the window width allows.
    /// </summary>
    Cards,
}

/// <summary>
/// How large the cards are in the card layout.
/// </summary>
public enum CardSize
{
    /// <summary>
    /// Small cards with the name and the price only; the rest is in the tooltip.
    /// </summary>
    Small,

    /// <summary>
    /// The usual cards with a chart and the change.
    /// </summary>
    Medium,

    /// <summary>
    /// Large cards with a taller chart, the change in amount and percent, the reference price and the range.
    /// </summary>
    Large,
}

/// <summary>
/// How the cards are grouped. Only applies to <see cref="WatchlistLayout.Cards"/>.
/// </summary>
public enum CardGrouping
{
    /// <summary>
    /// A single flow of cards without headers.
    /// </summary>
    None,

    /// <summary>
    /// One section per asset class.
    /// </summary>
    ByType,

    /// <summary>
    /// One section per data source (provider) that serves instruments on the watchlist.
    /// </summary>
    ByProvider,

    /// <summary>
    /// One section per user-defined group. Every instrument belongs to a group, see <see cref="AppSettings.Normalize"/>.
    /// </summary>
    Custom,
}

/// <summary>
/// A user-defined group of watchlist items.
/// </summary>
public sealed class WatchGroup
{
    /// <summary>
    /// Gets or sets the stable identifier that watchlist entries refer to.
    /// </summary>
    public string Id { get; init; } = Identifier.New();

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the group is collapsed.
    /// </summary>
    public bool Collapsed { get; set; }

    /// <summary>
    /// Cleans up a typed group name. This is the one naming rule shared by every place that creates or renames a
    /// group.
    /// </summary>
    /// <param name="raw">
    /// The name as typed.
    /// </param>
    /// <param name="name">
    /// The trimmed name, or an empty string when the name is not usable.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the name is not blank.
    /// </returns>
    public static bool TryNormalizeName(string? raw, out string name)
    {
        name = raw?.Trim() ?? string.Empty;
        return name.Length > 0;
    }
}

/// <summary>
/// A persisted watchlist item.
/// </summary>
public sealed class WatchlistEntry
{
    /// <summary>
    /// Gets or sets the provider that serves the instrument.
    /// </summary>
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the provider-native symbol.
    /// </summary>
    public string Symbol { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the asset class.
    /// </summary>
    public AssetClass AssetClass { get; init; }

    /// <summary>
    /// Gets or sets the id of the <see cref="WatchGroup"/> the item belongs to. It is <see langword="null"/> only
    /// for a moment, until <see cref="AppSettings.Normalize"/> assigns the item to a group.
    /// </summary>
    public string? GroupId { get; set; }

    /// <summary>
    /// Gets or sets the price the user paid per unit, in <see cref="HoldingCurrency"/>, or <see langword="null"/> when
    /// the user holds nothing of the instrument. It is set together with <see cref="Quantity"/>.
    /// </summary>
    public decimal? CostPrice { get; set; }

    /// <summary>
    /// Gets or sets the number of units the user holds, or <see langword="null"/> when the user holds nothing of the
    /// instrument. It is set together with <see cref="CostPrice"/>.
    /// </summary>
    public decimal? Quantity { get; set; }

    /// <summary>
    /// Gets or sets the ISO 4217 code of the currency of <see cref="CostPrice"/> and of the quoted price, or
    /// <see langword="null"/> when the holding does not say and <see cref="CurrencyGuesser"/> decides.
    /// </summary>
    public string? HoldingCurrency { get; set; }

    /// <summary>
    /// Gets a value indicating whether the entry has a usable holding.
    /// </summary>
    [JsonIgnore]
    public bool HasHolding => HoldingPnl.IsUsable(CostPrice, Quantity);

    /// <summary>
    /// Copies the holding of another entry, for when an instrument moves to another data source.
    /// </summary>
    /// <param name="other">
    /// The entry to copy the holding from.
    /// </param>
    public void CopyHoldingFrom(WatchlistEntry other)
    {
        CostPrice = other.CostPrice;
        Quantity = other.Quantity;
        HoldingCurrency = other.HoldingCurrency;
    }

    /// <summary>
    /// Converts an asset to a persisted entry.
    /// </summary>
    /// <param name="asset">
    /// The asset to convert.
    /// </param>
    /// <returns>
    /// The persisted entry.
    /// </returns>
    public static WatchlistEntry From(Asset asset) => new()
    {
        ProviderId = asset.ProviderId,
        Symbol = asset.Symbol,
        DisplayName = asset.DisplayName,
        AssetClass = asset.AssetClass,
    };

    /// <summary>
    /// Converts the entry to an asset.
    /// </summary>
    /// <returns>
    /// The asset.
    /// </returns>
    public Asset ToAsset() => new(ProviderId, Symbol, DisplayName, AssetClass);
}

/// <summary>
/// Persisted per-provider settings.
/// </summary>
public sealed class ProviderSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the provider is in use.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the option values keyed by option key, in stored text form.
    /// </summary>
    public Dictionary<string, string> Options { get; set; } = [];
}

/// <summary>
/// Persisted appearance and behavior settings.
/// </summary>
public sealed class AppearanceSettings
{
    /// <summary>
    /// Gets or sets the window opacity between 0.3 and 1.
    /// </summary>
    public double Opacity { get; set; } = 0.9;

    /// <summary>
    /// Gets or sets a value indicating whether the window stays above other windows.
    /// </summary>
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether rows are shown without the sparkline for a denser list.
    /// </summary>
    public bool Compact { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the window position is locked against dragging.
    /// </summary>
    public bool Locked { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the price rolls (the old number slides out and the new one slides in) when
    /// it changes.
    /// </summary>
    public bool RollPriceOnUpdate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the rows tell whether the market of an instrument is trading (pre-market,
    /// after hours, break, closed) and say "closed" instead of "stale" when the prices stopped because the market is shut.
    /// </summary>
    public bool ShowMarketStatus { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the window hides at the screen edge it is dragged to and slides out when
    /// the pointer comes near.
    /// </summary>
    public bool AutoHideAtEdge { get; set; } = true;

    /// <summary>
    /// Gets or sets the up/down color convention.
    /// </summary>
    public ColorConvention ColorConvention { get; set; } = ColorConvention.Auto;

    /// <summary>
    /// Gets or sets the color scheme: light, dark, or following the system.
    /// </summary>
    public ThemeMode ThemeMode { get; set; } = ThemeMode.Auto;

    /// <summary>
    /// Gets or sets the name of the font family of the interface text, or an empty text for the font of the system.
    /// </summary>
    public string Font { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the interface language: Simplified Chinese, English, or following the system.
    /// </summary>
    public LanguageMode Language { get; set; } = LanguageMode.Auto;

    /// <summary>
    /// Gets or sets how the watchlist is laid out.
    /// </summary>
    public WatchlistLayout Layout { get; set; } = WatchlistLayout.List;

    /// <summary>
    /// Gets or sets how large the cards are when <see cref="Layout"/> is <see cref="WatchlistLayout.Cards"/>.
    /// </summary>
    public CardSize CardSize { get; set; } = CardSize.Medium;

    /// <summary>
    /// Gets or sets how the cards are grouped when <see cref="Layout"/> is <see cref="WatchlistLayout.Cards"/>.
    /// </summary>
    public CardGrouping Grouping { get; set; } = CardGrouping.None;

    /// <summary>
    /// Gets or sets the asset classes whose card section is collapsed when grouping by type.
    /// </summary>
    public List<AssetClass> CollapsedTypes { get; init; } = [];

    /// <summary>
    /// Gets or sets the ids of the providers whose card section is collapsed when grouping by provider.
    /// </summary>
    public List<string> CollapsedProviders { get; init; } = [];

    /// <summary>
    /// Gets or sets the order of the asset class sections when grouping by type. Classes that are not listed follow
    /// in their default order, so an empty list means the default order.
    /// </summary>
    public List<AssetClass> TypeOrder { get; set; } = [];

    /// <summary>
    /// Gets every asset class in the order its section is shown when grouping by type.
    /// </summary>
    /// <returns>
    /// The listed classes in the order they are listed, followed by the remaining ones in their default order.
    /// </returns>
    public List<AssetClass> EffectiveTypeOrder() => [.. TypeOrder.Distinct(), .. Enum.GetValues<AssetClass>().Except(TypeOrder)];
}

/// <summary>
/// Persisted settings of the update check.
/// </summary>
public sealed class UpdateSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the app looks for a newer release by itself when it starts.
    /// </summary>
    public bool AutoCheck { get; set; } = true;
}

/// <summary>
/// Persisted general settings.
/// </summary>
public sealed class GeneralSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the app starts hidden in the tray instead of showing its window.
    /// </summary>
    public bool StartMinimized { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether closing the window hides it to the tray; otherwise it exits the app.
    /// </summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>
    /// Gets or sets the instruments the tray list shows, as keys made by <see cref="TrayAssetSelection.Key(string, string)"/>
    /// in the order they are listed. It is empty until the user adds instruments.
    /// </summary>
    public List<string> TrayAssets { get; set; } = [];

    /// <summary>
    /// Gets or sets the ISO 4217 code of the currency the portfolio total is shown in, or <see langword="null"/> to
    /// follow the interface language.
    /// </summary>
    public string? BaseCurrency { get; set; }

    /// <summary>
    /// Gets or sets the global hotkey that hides every window and the tray icon at once and brings them back,
    /// written like <c>Ctrl+Alt+H</c>. It is empty when the boss key is off.
    /// </summary>
    public string BossKey { get; set; } = "Ctrl+Alt+H";
}

/// <summary>
/// Persisted window geometry.
/// </summary>
public sealed class WindowSettings
{
    /// <summary>
    /// Gets or sets the left edge in screen pixels, or <see langword="null"/> for the default placement.
    /// </summary>
    public int? X { get; set; }

    /// <summary>
    /// Gets or sets the top edge in screen pixels, or <see langword="null"/> for the default placement.
    /// </summary>
    public int? Y { get; set; }

    /// <summary>
    /// Gets or sets the width in device independent pixels.
    /// </summary>
    public double Width { get; set; } = 340;

    /// <summary>
    /// Gets or sets the height in device independent pixels.
    /// </summary>
    public double Height { get; set; } = 420;

    /// <summary>
    /// Gets or sets a value indicating whether the window was full screen when it was last used, so that it opens full
    /// screen again. The position and size above stay those of the normal window.
    /// </summary>
    public bool FullScreen { get; set; }
}

/// <summary>
/// The root of everything persisted to <c>settings.json</c>.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// The serializer options used to persist and clone settings. A file is read through <c>SettingsFile</c>, which
    /// takes care of values that are missing or null.
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Gets or sets the tracked instruments in display order.
    /// </summary>
    public List<WatchlistEntry> Watchlist { get; init; } = [];

    /// <summary>
    /// Gets or sets the user-defined groups in display order.
    /// </summary>
    public List<WatchGroup> Groups { get; init; } = [];

    /// <summary>
    /// Gets or sets the price alert rules.
    /// </summary>
    public List<AlertRule> Alerts { get; init; } = [];

    /// <summary>
    /// Gets or sets the settings of each provider keyed by provider id.
    /// </summary>
    public Dictionary<string, ProviderSettings> Providers { get; init; } = [];

    /// <summary>
    /// Gets or sets the appearance and behavior settings.
    /// </summary>
    public AppearanceSettings Appearance { get; set; } = new();

    /// <summary>
    /// Gets or sets the general settings.
    /// </summary>
    public GeneralSettings General { get; set; } = new();

    /// <summary>
    /// Gets or sets the update check settings.
    /// </summary>
    public UpdateSettings Updates { get; set; } = new();

    /// <summary>
    /// Gets or sets the window geometry.
    /// </summary>
    public WindowSettings Window { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether reading the settings from a file had to drop or make up something, such
    /// as a null inside a collection or a missing identifier. <see cref="Normalize"/> reports that once, so that the
    /// settings get written again.
    /// </summary>
    internal bool Repaired { get; set; }

    /// <summary>
    /// The name of the group that is created when there is none.
    /// </summary>
    public const string DefaultGroupName = "Default";

    /// <summary>
    /// Makes the groups consistent so that every instrument always belongs to a group: when there is no group, a
    /// default one is created, and an instrument without a group, or with a group that no longer exists, joins the
    /// first group. This keeps "no group" from being a state the rest of the application has to deal with. It also
    /// drops the collapsed states of asset classes and data sources that no instrument on the watchlist uses anymore,
    /// so that an old state does not come back months later when an instrument of that kind is added again.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when something had to be changed, which includes a repair made while reading the file.
    /// </returns>
    public bool Normalize()
    {
        var changed = Repaired;
        Repaired = false;
        if (Groups.Count == 0)
        {
            Groups.Add(new WatchGroup { Name = DefaultGroupName });
            changed = true;
        }

        changed |= Appearance.CollapsedProviders.RemoveAll(id => Watchlist.TrueForAll(e => e.ProviderId != id)) > 0;
        changed |= Appearance.CollapsedTypes.RemoveAll(type => Watchlist.TrueForAll(e => e.AssetClass != type)) > 0;

        var known = Groups.Select(g => g.Id).ToHashSet();
        foreach (var entry in Watchlist.Where(e => e.GroupId is null || !known.Contains(e.GroupId)))
        {
            entry.GroupId = Groups[0].Id;
            changed = true;
        }

        // A rule is dropped when its instrument left the watchlist, when its threshold cannot be used, when its id
        // repeats an earlier one, or when an earlier rule already has the same condition on the same instrument: an
        // instrument has at most one alert above and one below.
        var ids = new HashSet<string>();
        var conditions = new HashSet<(string, string, AlertCondition)>();
        changed |= Alerts.RemoveAll(rule =>
            !AlertRule.IsValidThreshold(rule.Threshold)
            || Watchlist.TrueForAll(e => e.ProviderId != rule.ProviderId || e.Symbol != rule.Symbol)
            || !ids.Add(rule.Id)
            || !conditions.Add((rule.ProviderId, rule.Symbol, rule.Condition))) > 0;

        // The tray list forgets instruments that left the watchlist and keys that repeat.
        changed |= TrayAssetSelection.Sanitize(General.TrayAssets, Watchlist);

        // A holding needs both a cost and a quantity above zero, otherwise it is not one. A currency is one of the app has rates for.
        foreach (var entry in Watchlist)
        {
            if (!entry.HasHolding && (entry.CostPrice is not null || entry.Quantity is not null || entry.HoldingCurrency is not null))
            {
                entry.CostPrice = null;
                entry.Quantity = null;
                entry.HoldingCurrency = null;
                changed = true;
            }
            else if (entry.HoldingCurrency is not null && CurrencyCodes.NormalizeSupported(entry.HoldingCurrency) is var code && code != entry.HoldingCurrency)
            {
                entry.HoldingCurrency = code;
                changed = true;
            }
        }

        if (General.BaseCurrency is not null && CurrencyCodes.NormalizeSupported(General.BaseCurrency) is var baseCode && baseCode != General.BaseCurrency)
        {
            General.BaseCurrency = baseCode;
            changed = true;
        }

        // The boss key is a hotkey the app can register, written the usual way, or empty when the user turned it off. Any
        // other text is the default again, as there is nothing to tell what it was meant to be.
        if (General.BossKey is { Length: > 0 } bossKey)
        {
            var spelled = HotkeyGesture.TryParse(bossKey, out var gesture)
                ? gesture.ToString()
                : new GeneralSettings().BossKey;
            if (spelled != bossKey)
            {
                General.BossKey = spelled;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Creates a deep copy.
    /// </summary>
    /// <returns>
    /// The copy.
    /// </returns>
    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    /// <summary>
    /// Determines whether a provider is switched on, falling back to its descriptor default when no setting exists. A
    /// provider can be switched on and still not run, because a required option has no value; see
    /// <see cref="IsProviderEnabled"/>.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the provider is switched on.
    /// </returns>
    public bool IsProviderSwitchedOn(ProviderDescriptor descriptor) =>
        Providers.TryGetValue(descriptor.Id, out var settings) ? settings.Enabled : descriptor.EnabledByDefault;

    /// <summary>
    /// Determines whether a provider should run: it is switched on and every option it requires has a value.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the provider should run.
    /// </returns>
    public bool IsProviderEnabled(ProviderDescriptor descriptor) =>
        IsProviderSwitchedOn(descriptor) && HasRequiredOptions(descriptor);

    /// <summary>
    /// Determines whether every option a provider requires has a value in the settings.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when no required option is missing.
    /// </returns>
    private bool HasRequiredOptions(ProviderDescriptor descriptor)
    {
        Providers.TryGetValue(descriptor.Id, out var settings);
        foreach (var option in descriptor.OptionDefinitions)
        {
            if (!option.Required)
            {
                continue;
            }

            string? value = null;
            if (settings is null || !settings.Options.TryGetValue(option.Key, out value) || option.IsMissing(value) || option.Check(value) is not null)
            {
                return false;
            }
        }

        return true;
    }
}
