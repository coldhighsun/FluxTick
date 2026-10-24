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
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

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
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the provider-native symbol.
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the asset class.
    /// </summary>
    public AssetClass AssetClass { get; set; }

    /// <summary>
    /// Gets or sets the id of the <see cref="WatchGroup"/> the item belongs to. It is <see langword="null"/> only
    /// for a moment, until <see cref="AppSettings.Normalize"/> assigns the item to a group.
    /// </summary>
    public string? GroupId { get; set; }

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
    public double Opacity { get; set; } = 0.92;

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
    /// Gets or sets a value indicating whether a row flashes when its price changes.
    /// </summary>
    public bool FlashOnUpdate { get; set; }

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
    /// Gets or sets the interface language: Simplified Chinese, English, or following the system.
    /// </summary>
    public LanguageMode Language { get; set; } = LanguageMode.Auto;

    /// <summary>
    /// Gets or sets how the watchlist is laid out.
    /// </summary>
    public WatchlistLayout Layout { get; set; } = WatchlistLayout.List;

    /// <summary>
    /// Gets or sets how the cards are grouped when <see cref="Layout"/> is <see cref="WatchlistLayout.Cards"/>.
    /// </summary>
    public CardGrouping Grouping { get; set; } = CardGrouping.None;

    /// <summary>
    /// Gets or sets the asset classes whose card section is collapsed when grouping by type.
    /// </summary>
    public List<AssetClass> CollapsedTypes { get; set; } = [];

    /// <summary>
    /// Gets or sets the ids of the providers whose card section is collapsed when grouping by provider.
    /// </summary>
    public List<string> CollapsedProviders { get; set; } = [];

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
    /// Gets or sets a value indicating whether hovering the tray icon lists the watched instruments.
    /// </summary>
    public bool TrayHoverList { get; set; } = true;
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
}

/// <summary>
/// The root of everything persisted to <c>settings.json</c>.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// The serializer options used to persist and clone settings.
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
    public List<WatchlistEntry> Watchlist { get; set; } = [];

    /// <summary>
    /// Gets or sets the user-defined groups in display order.
    /// </summary>
    public List<WatchGroup> Groups { get; set; } = [];

    /// <summary>
    /// Gets or sets the settings of each provider keyed by provider id.
    /// </summary>
    public Dictionary<string, ProviderSettings> Providers { get; set; } = [];

    /// <summary>
    /// Gets or sets, per asset class, the preferred provider ids in priority order.
    /// </summary>
    public Dictionary<AssetClass, List<string>> Routing { get; set; } = [];

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
    /// The name of the group that is created when there is none.
    /// </summary>
    public const string DefaultGroupName = "Default";

    /// <summary>
    /// Makes the groups consistent so that every instrument always belongs to a group: when there is no group, a
    /// default one is created, and an instrument without a group, or with a group that no longer exists, joins the
    /// first group. This keeps "no group" from being a state the rest of the application has to deal with. It also
    /// drops the collapsed states of asset classes and data sources that no instrument on the watchlist uses any more,
    /// so that an old state does not come back months later when an instrument of that kind is added again.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when something had to be changed.
    /// </returns>
    public bool Normalize()
    {
        var changed = false;
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
    /// Determines whether a provider is enabled, falling back to its descriptor default when no setting exists.
    /// </summary>
    /// <param name="descriptor">
    /// The provider descriptor.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the provider should run.
    /// </returns>
    public bool IsProviderEnabled(ProviderDescriptor descriptor) =>
        Providers.TryGetValue(descriptor.Id, out var settings) ? settings.Enabled : descriptor.EnabledByDefault;
}
