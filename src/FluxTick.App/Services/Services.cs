using System.Globalization;
using Avalonia.Styling;
using FluxTick.Abstractions;
using FluxTick.Core.Application;

namespace FluxTick.App.Services;

/// <summary>
/// Marshals work onto the UI thread so view models never touch the UI framework directly.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Queues an action to run on the UI thread. Runs inline when already on it.
    /// </summary>
    /// <param name="action">
    /// The action to run.
    /// </param>
    void Post(Action action);

    /// <summary>
    /// Queues an action to run on the UI thread after the input that is waiting there has been handled. It never
    /// runs inline. For frequent, replaceable updates such as prices, so that clicks stay responsive under load.
    /// </summary>
    /// <param name="action">
    /// The action to run.
    /// </param>
    void PostBackground(Action action);
}

/// <summary>
/// Well-known locations of the application's files.
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// Gets the per-user directory for settings and logs.
    /// </summary>
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluxTick");

    /// <summary>
    /// Gets the path of the settings file.
    /// </summary>
    /// <param name="demo">
    /// Whether the app runs in demo mode, which uses a separate file so real settings are never touched.
    /// </param>
    /// <returns>
    /// The full path of the settings file.
    /// </returns>
    public static string SettingsFile(bool demo) => Path.Combine(DataDirectory, demo ? "settings.demo.json" : "settings.json");

    /// <summary>
    /// Gets the directory that downloaded update packages are kept in.
    /// </summary>
    public static string UpdatesDirectory => Path.Combine(DataDirectory, "updates");

    /// <summary>
    /// Gets the path of the file that remembers when the update check last ran.
    /// </summary>
    public static string UpdateStateFile => Path.Combine(DataDirectory, "update-state.json");

    /// <summary>
    /// Gets the path of the file that keeps the latest exchange rates for offline use.
    /// </summary>
    public static string ExchangeRatesFile => Path.Combine(DataDirectory, "rates.json");

    /// <summary>
    /// Gets the directory that the log files are kept in.
    /// </summary>
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>
    /// Gets the rolling log file path pattern.
    /// </summary>
    public static string LogFile => Path.Combine(LogDirectory, "fluxtick-.log");
}

/// <summary>
/// Resolves which colors mean up and down.
/// </summary>
public static class ColorConventionResolver
{
    /// <summary>
    /// Determines whether gains are shown in red.
    /// </summary>
    /// <param name="convention">
    /// The configured convention.
    /// </param>
    /// <param name="culture">
    /// The UI culture used when the convention is <see cref="ColorConvention.Auto"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when gains are red and losses are green.
    /// </returns>
    public static bool IsRedUp(ColorConvention convention, CultureInfo culture) => convention switch
    {
        ColorConvention.RedUp => true,
        ColorConvention.GreenUp => false,
        _ => culture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase),
    };
}

/// <summary>
/// Maps the configured color scheme to the UI framework's theme variant.
/// </summary>
public static class ThemeModeResolver
{
    /// <summary>
    /// Converts a theme mode to the variant to request. <see cref="ThemeMode.Auto"/> yields the default variant,
    /// which follows the system.
    /// </summary>
    /// <param name="mode">
    /// The configured mode.
    /// </param>
    /// <returns>
    /// The theme variant to request.
    /// </returns>
    public static ThemeVariant ToVariant(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => ThemeVariant.Light,
        ThemeMode.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };
}

/// <summary>
/// Formats prices and changes for display.
/// </summary>
public static class PriceFormatter
{
    /// <summary>
    /// The text shown when a value is unavailable.
    /// </summary>
    public const string Unavailable = "—";

    /// <summary>
    /// Formats a price with a precision suited to its magnitude and asset class.
    /// </summary>
    /// <param name="price">
    /// The price.
    /// </param>
    /// <param name="assetClass">
    /// The asset class, which decides how many decimals small prices need.
    /// </param>
    /// <returns>
    /// The formatted price.
    /// </returns>
    public static string FormatPrice(decimal price, AssetClass assetClass)
    {
        var abs = Math.Abs(price);
        var format = abs switch
        {
            >= 1000m => "#,##0.00",
            >= 10m => assetClass == AssetClass.Forex ? "0.000" : "0.00",
            >= 1m => assetClass is AssetClass.Forex or AssetClass.Crypto ? "0.0000" : "0.00",
            >= 0.01m => "0.00000",
            _ => "0.00000000",
        };
        return price.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats a relative change with an explicit sign.
    /// </summary>
    /// <param name="percent">
    /// The change in percent, or <see langword="null"/> when unknown.
    /// </param>
    /// <returns>
    /// Text such as <c>+1.23%</c>, or <see cref="Unavailable"/>.
    /// </returns>
    public static string FormatPercent(decimal? percent) =>
        percent is null ? Unavailable : percent.Value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// Formats an amount of money with two decimals and a thousands separator.
    /// </summary>
    /// <param name="amount">
    /// The amount.
    /// </param>
    /// <returns>
    /// Text such as <c>12,345.60</c>.
    /// </returns>
    public static string FormatMoney(decimal amount) => amount.ToString("#,##0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats an amount of money with an explicit sign.
    /// </summary>
    /// <param name="amount">
    /// The amount.
    /// </param>
    /// <returns>
    /// Text such as <c>+12,345.60</c> or <c>-5.00</c>.
    /// </returns>
    public static string FormatSignedMoney(decimal amount) => amount.ToString("+#,##0.00;-#,##0.00;0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a quantity without trailing zeros.
    /// </summary>
    /// <param name="quantity">
    /// The quantity.
    /// </param>
    /// <returns>
    /// Text such as <c>1,000</c> or <c>0.5</c>.
    /// </returns>
    public static string FormatQuantity(decimal quantity) => quantity.ToString("#,##0.########", CultureInfo.InvariantCulture);
}
