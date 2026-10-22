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
    /// Gets or sets the up/down color convention.
    /// </summary>
    public ColorConvention ColorConvention { get; set; } = ColorConvention.Auto;
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
    /// Gets or sets the window geometry.
    /// </summary>
    public WindowSettings Window { get; set; } = new();

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
