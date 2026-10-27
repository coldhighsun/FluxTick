using FluxTick.Abstractions;
using FluxTick.Core.Application;

namespace FluxTick.Core.Infrastructure;

/// <summary>
/// The shape of <c>settings.json</c> as it is read. Everything in it may be missing or an explicit <see langword="null"/>,
/// because the file is edited by hand, comes from older versions and may be damaged. <see cref="ToSettings"/> is the one
/// place where that is dealt with: a missing or null value takes the default of <see cref="AppSettings"/>, and a null
/// inside a collection is dropped. The settings that come out hold no null, so the rest of the application does not
/// have to deal with it. The names match the properties of <see cref="AppSettings"/>, which is what gets written.
/// </summary>
internal sealed class SettingsFile
{
    /// <summary>
    /// Gets or sets the tracked instruments.
    /// </summary>
    public List<WatchlistEntryFile?>? Watchlist { get; set; }

    /// <summary>
    /// Gets or sets the user-defined groups.
    /// </summary>
    public List<WatchGroupFile?>? Groups { get; set; }

    /// <summary>
    /// Gets or sets the price alert rules.
    /// </summary>
    public List<AlertRuleFile?>? Alerts { get; set; }

    /// <summary>
    /// Gets or sets the settings of each provider.
    /// </summary>
    public Dictionary<string, ProviderSettingsFile?>? Providers { get; set; }

    /// <summary>
    /// Gets or sets the preferred provider ids per asset class.
    /// </summary>
    public Dictionary<AssetClass, List<string?>?>? Routing { get; set; }

    /// <summary>
    /// Gets or sets the appearance and behavior settings.
    /// </summary>
    public AppearanceFile? Appearance { get; set; }

    /// <summary>
    /// Gets or sets the general settings.
    /// </summary>
    public GeneralFile? General { get; set; }

    /// <summary>
    /// Gets or sets the update check settings.
    /// </summary>
    public UpdatesFile? Updates { get; set; }

    /// <summary>
    /// Gets or sets the window geometry.
    /// </summary>
    public WindowFile? Window { get; set; }

    /// <summary>
    /// Converts what was read to settings.
    /// </summary>
    /// <returns>
    /// The settings, with a default for everything that was missing. When something had to be dropped or made up, such
    /// as a null inside a collection or an identifier that was missing, <see cref="AppSettings.Repaired"/> is set, so
    /// that the file is written again. A value that is missing or null, as in a file from an older version, is not a
    /// repair: the two can not be told apart, and the default is what the next save writes anyway.
    /// </returns>
    public AppSettings ToSettings()
    {
        var repaired = false;
        var settings = new AppSettings();
        settings.Watchlist.AddRange(NotNull(Watchlist, ref repaired).Select(e => e.ToEntry()));
        var groups = NotNull(Groups, ref repaired);
        repaired |= groups.Exists(g => g.Id is null);
        settings.Groups.AddRange(groups.Select(g => g.ToGroup()));
        var alerts = NotNull(Alerts, ref repaired);
        repaired |= alerts.Exists(a => a.Id is null);
        settings.Alerts.AddRange(alerts.Select(a => a.ToRule()));
        foreach (var (id, provider) in Providers ?? [])
        {
            if (provider is null)
            {
                repaired = true;
            }
            else
            {
                settings.Providers[id] = provider.ToSettings(ref repaired);
            }
        }

        foreach (var (assetClass, order) in Routing ?? [])
        {
            if (order is null)
            {
                repaired = true;
            }
            else
            {
                settings.Routing[assetClass] = NotNull(order, ref repaired);
            }
        }

        settings.Appearance = (Appearance ?? new()).ToSettings(ref repaired);
        settings.General = (General ?? new()).ToSettings(ref repaired);
        settings.Updates = (Updates ?? new()).ToSettings();
        settings.Window = (Window ?? new()).ToSettings();
        settings.Repaired = repaired;
        return settings;
    }

    /// <summary>
    /// Gets the elements of a list that are not <see langword="null"/>.
    /// </summary>
    /// <typeparam name="T">
    /// The element type.
    /// </typeparam>
    /// <param name="items">
    /// The list, or <see langword="null"/>.
    /// </param>
    /// <param name="repaired">
    /// Set to <see langword="true"/> when an element was dropped.
    /// </param>
    /// <returns>
    /// A new list of the elements that are not <see langword="null"/>; empty when the list itself is.
    /// </returns>
    internal static List<T> NotNull<T>(List<T?>? items, ref bool repaired)
        where T : class
    {
        if (items is null)
        {
            return [];
        }

        var result = items.OfType<T>().ToList();
        repaired |= result.Count != items.Count;
        return result;
    }
}

/// <summary>
/// A persisted watchlist item as it is read.
/// </summary>
internal sealed class WatchlistEntryFile
{
    /// <summary>
    /// The values that stand in for what is missing. It is never changed.
    /// </summary>
    private static readonly WatchlistEntry Defaults = new();

    /// <summary>
    /// Gets or sets the provider that serves the instrument.
    /// </summary>
    public string? ProviderId { get; set; }

    /// <summary>
    /// Gets or sets the provider-native symbol.
    /// </summary>
    public string? Symbol { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the asset class.
    /// </summary>
    public AssetClass? AssetClass { get; set; }

    /// <summary>
    /// Gets or sets the id of the group the item belongs to.
    /// </summary>
    public string? GroupId { get; set; }

    /// <summary>
    /// Gets or sets the price paid per unit.
    /// </summary>
    public decimal? CostPrice { get; set; }

    /// <summary>
    /// Gets or sets the number of units held.
    /// </summary>
    public decimal? Quantity { get; set; }

    /// <summary>
    /// Gets or sets the currency of the holding.
    /// </summary>
    public string? HoldingCurrency { get; set; }

    /// <summary>
    /// Converts what was read to an entry.
    /// </summary>
    /// <returns>
    /// The entry.
    /// </returns>
    public WatchlistEntry ToEntry() =>
        new()
        {
            ProviderId = ProviderId ?? Defaults.ProviderId,
            Symbol = Symbol ?? Defaults.Symbol,
            DisplayName = DisplayName ?? Defaults.DisplayName,
            AssetClass = AssetClass ?? Defaults.AssetClass,
            GroupId = GroupId,
            CostPrice = CostPrice,
            Quantity = Quantity,
            HoldingCurrency = HoldingCurrency,
        };
}

/// <summary>
/// A user-defined group as it is read.
/// </summary>
internal sealed class WatchGroupFile
{
    /// <summary>
    /// The values that stand in for what is missing. It is never changed.
    /// </summary>
    private static readonly WatchGroup Defaults = new();

    /// <summary>
    /// Gets or sets the identifier.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the group is collapsed.
    /// </summary>
    public bool? Collapsed { get; set; }

    /// <summary>
    /// Converts what was read to a group.
    /// </summary>
    /// <returns>
    /// The group. Without an id it gets a new one.
    /// </returns>
    public WatchGroup ToGroup() =>
        new()
        {
            Id = Id ?? Identifier.New(),
            Name = Name ?? Defaults.Name,
            Collapsed = Collapsed ?? Defaults.Collapsed,
        };
}

/// <summary>
/// A price alert rule as it is read.
/// </summary>
internal sealed class AlertRuleFile
{
    /// <summary>
    /// The values that stand in for what is missing. It is never changed.
    /// </summary>
    private static readonly AlertRule Defaults = new();

    /// <summary>
    /// Gets or sets the identifier.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the provider that serves the instrument.
    /// </summary>
    public string? ProviderId { get; set; }

    /// <summary>
    /// Gets or sets the provider-native symbol.
    /// </summary>
    public string? Symbol { get; set; }

    /// <summary>
    /// Gets or sets what is compared with the threshold.
    /// </summary>
    public AlertCondition? Condition { get; set; }

    /// <summary>
    /// Gets or sets the threshold.
    /// </summary>
    public decimal? Threshold { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the rule is watched.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the rule fires again.
    /// </summary>
    public bool? Repeat { get; set; }

    /// <summary>
    /// Converts what was read to a rule.
    /// </summary>
    /// <returns>
    /// The rule.
    /// </returns>
    public AlertRule ToRule() =>
        new()
        {
            Id = Id ?? Identifier.New(),
            ProviderId = ProviderId ?? Defaults.ProviderId,
            Symbol = Symbol ?? Defaults.Symbol,
            Condition = Condition ?? Defaults.Condition,
            Threshold = Threshold ?? Defaults.Threshold,
            Enabled = Enabled ?? Defaults.Enabled,
            Repeat = Repeat ?? Defaults.Repeat,
        };
}

/// <summary>
/// The settings of one provider as they are read.
/// </summary>
internal sealed class ProviderSettingsFile
{
    /// <summary>
    /// Gets or sets a value indicating whether the provider is in use.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Gets or sets the option values keyed by option key.
    /// </summary>
    public Dictionary<string, string?>? Options { get; set; }

    /// <summary>
    /// Converts what was read to provider settings.
    /// </summary>
    /// <param name="repaired">
    /// Set to <see langword="true"/> when something had to be dropped.
    /// </param>
    /// <returns>
    /// The provider settings.
    /// </returns>
    public ProviderSettings ToSettings(ref bool repaired)
    {
        var settings = new ProviderSettings { Enabled = Enabled ?? new ProviderSettings().Enabled };
        foreach (var (key, value) in Options ?? [])
        {
            if (value is null)
            {
                repaired = true;
            }
            else
            {
                settings.Options[key] = value;
            }
        }

        return settings;
    }
}

/// <summary>
/// The appearance and behavior settings as they are read.
/// </summary>
internal sealed class AppearanceFile
{
    /// <summary>
    /// Gets or sets the window opacity.
    /// </summary>
    public double? Opacity { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the window stays above other windows.
    /// </summary>
    public bool? AlwaysOnTop { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether rows are shown without the sparkline.
    /// </summary>
    public bool? Compact { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the window position is locked.
    /// </summary>
    public bool? Locked { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the price rolls when it changes.
    /// </summary>
    public bool? RollPriceOnUpdate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the window hides at the screen edge.
    /// </summary>
    public bool? AutoHideAtEdge { get; set; }

    /// <summary>
    /// Gets or sets the up/down color convention.
    /// </summary>
    public ColorConvention? ColorConvention { get; set; }

    /// <summary>
    /// Gets or sets the color scheme.
    /// </summary>
    public ThemeMode? ThemeMode { get; set; }

    /// <summary>
    /// Gets or sets the interface language.
    /// </summary>
    public LanguageMode? Language { get; set; }

    /// <summary>
    /// Gets or sets how the watchlist is laid out.
    /// </summary>
    public WatchlistLayout? Layout { get; set; }

    /// <summary>
    /// Gets or sets how large the cards are.
    /// </summary>
    public CardSize? CardSize { get; set; }

    /// <summary>
    /// Gets or sets how the cards are grouped.
    /// </summary>
    public CardGrouping? Grouping { get; set; }

    /// <summary>
    /// Gets or sets the asset classes whose card section is collapsed.
    /// </summary>
    public List<AssetClass>? CollapsedTypes { get; set; }

    /// <summary>
    /// Gets or sets the ids of the providers whose card section is collapsed.
    /// </summary>
    public List<string?>? CollapsedProviders { get; set; }

    /// <summary>
    /// Gets or sets the order of the asset class sections.
    /// </summary>
    public List<AssetClass>? TypeOrder { get; set; }

    /// <summary>
    /// Converts what was read to appearance settings.
    /// </summary>
    /// <param name="repaired">
    /// Set to <see langword="true"/> when something had to be dropped.
    /// </param>
    /// <returns>
    /// The appearance settings.
    /// </returns>
    public AppearanceSettings ToSettings(ref bool repaired)
    {
        var defaults = new AppearanceSettings();
        return new AppearanceSettings
        {
            Opacity = Opacity ?? defaults.Opacity,
            AlwaysOnTop = AlwaysOnTop ?? defaults.AlwaysOnTop,
            Compact = Compact ?? defaults.Compact,
            Locked = Locked ?? defaults.Locked,
            RollPriceOnUpdate = RollPriceOnUpdate ?? defaults.RollPriceOnUpdate,
            AutoHideAtEdge = AutoHideAtEdge ?? defaults.AutoHideAtEdge,
            ColorConvention = ColorConvention ?? defaults.ColorConvention,
            ThemeMode = ThemeMode ?? defaults.ThemeMode,
            Language = Language ?? defaults.Language,
            Layout = Layout ?? defaults.Layout,
            CardSize = CardSize ?? defaults.CardSize,
            Grouping = Grouping ?? defaults.Grouping,
            CollapsedTypes = [.. CollapsedTypes ?? []],
            CollapsedProviders = SettingsFile.NotNull(CollapsedProviders, ref repaired),
            TypeOrder = [.. TypeOrder ?? []],
        };
    }
}

/// <summary>
/// The general settings as they are read.
/// </summary>
internal sealed class GeneralFile
{
    /// <summary>
    /// Gets or sets a value indicating whether the app starts hidden in the tray.
    /// </summary>
    public bool? StartMinimized { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether closing the window hides it to the tray.
    /// </summary>
    public bool? CloseToTray { get; set; }

    /// <summary>
    /// Gets or sets the instruments the tray list shows.
    /// </summary>
    public List<string?>? TrayAssets { get; set; }

    /// <summary>
    /// Gets or sets the currency the portfolio total is shown in.
    /// </summary>
    public string? BaseCurrency { get; set; }

    /// <summary>
    /// Gets or sets the global hotkey that hides every window and the tray icon.
    /// </summary>
    public string? BossKey { get; set; }

    /// <summary>
    /// Converts what was read to general settings.
    /// </summary>
    /// <param name="repaired">
    /// Set to <see langword="true"/> when something had to be dropped.
    /// </param>
    /// <returns>
    /// The general settings.
    /// </returns>
    public GeneralSettings ToSettings(ref bool repaired)
    {
        var defaults = new GeneralSettings();
        return new GeneralSettings
        {
            StartMinimized = StartMinimized ?? defaults.StartMinimized,
            CloseToTray = CloseToTray ?? defaults.CloseToTray,
            TrayAssets = SettingsFile.NotNull(TrayAssets, ref repaired),
            BaseCurrency = BaseCurrency,
            BossKey = BossKey ?? defaults.BossKey,
        };
    }
}

/// <summary>
/// The update check settings as they are read.
/// </summary>
internal sealed class UpdatesFile
{
    /// <summary>
    /// Gets or sets a value indicating whether the app looks for a newer release by itself.
    /// </summary>
    public bool? AutoCheck { get; set; }

    /// <summary>
    /// Converts what was read to update settings.
    /// </summary>
    /// <returns>
    /// The update settings.
    /// </returns>
    public UpdateSettings ToSettings() => new() { AutoCheck = AutoCheck ?? new UpdateSettings().AutoCheck };
}

/// <summary>
/// The window geometry as it is read.
/// </summary>
internal sealed class WindowFile
{
    /// <summary>
    /// Gets or sets the left edge in screen pixels.
    /// </summary>
    public int? X { get; set; }

    /// <summary>
    /// Gets or sets the top edge in screen pixels.
    /// </summary>
    public int? Y { get; set; }

    /// <summary>
    /// Gets or sets the width.
    /// </summary>
    public double? Width { get; set; }

    /// <summary>
    /// Gets or sets the height.
    /// </summary>
    public double? Height { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the window was full screen.
    /// </summary>
    public bool? FullScreen { get; set; }

    /// <summary>
    /// Converts what was read to window settings.
    /// </summary>
    /// <returns>
    /// The window settings.
    /// </returns>
    public WindowSettings ToSettings()
    {
        var defaults = new WindowSettings();
        return new WindowSettings
        {
            X = X,
            Y = Y,
            Width = Width ?? defaults.Width,
            Height = Height ?? defaults.Height,
            FullScreen = FullScreen ?? defaults.FullScreen,
        };
    }
}
