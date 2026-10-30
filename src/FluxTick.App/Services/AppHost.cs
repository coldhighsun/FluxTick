using Avalonia.Threading;
using FluxTick.Abstractions;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;
using FluxTick.Core.Infrastructure;
using FluxTick.Providers.Binance;
using FluxTick.Providers.Bybit;
using FluxTick.Providers.Coinbase;
using FluxTick.Providers.Common;
using FluxTick.Providers.Eulerpool;
using FluxTick.Providers.Kraken;
using FluxTick.Providers.Okx;
using FluxTick.Providers.Simulated;
using FluxTick.Providers.Sina;
using FluxTick.Providers.Tencent;
using FluxTick.Providers.Yahoo;
using GitHubReleaseUpdater.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using ThrottledLogging;

namespace FluxTick.App.Services;

/// <summary>
/// An <see cref="IUiDispatcher"/> backed by the Avalonia UI thread.
/// </summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    /// <inheritdoc />
    public void PostBackground(Action action) => Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
}

/// <summary>
/// The composition root. Registering a data source is a single line below.
/// </summary>
public static class AppHost
{
    /// <summary>
    /// The language of the log files, which is English whatever the interface language is, so that one file never mixes
    /// languages and can be read by anyone who gets it for support.
    /// </summary>
    internal static readonly System.Globalization.CultureInfo LogCulture = System.Globalization.CultureInfo.GetCultureInfo("en");

    /// <summary>
    /// Registers the settings store. Secret options such as API keys are protected where the settings are written;
    /// everything above works with the plain values. Only Windows has protection built in (DPAPI, for the current user).
    /// </summary>
    /// <param name="services">
    /// The services.
    /// </param>
    /// <param name="settingsPath">
    /// The settings file.
    /// </param>
    /// <returns>
    /// The services.
    /// </returns>
    internal static IServiceCollection AddSettingsStore(this IServiceCollection services, string settingsPath)
    {
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        }
        else
        {
            services.AddSingleton<ISecretProtector, NoSecretProtector>();
        }

        return services.AddSingleton<ISettingsStore>(sp => new ProtectedSettingsStore(
            new JsonSettingsStore(settingsPath, sp.GetRequiredService<ILogger<JsonSettingsStore>>()),
            sp.GetRequiredService<ISecretProtector>(),
            () => sp.GetServices<IMarketDataProviderFactory>().Select(f => f.Descriptor),
            sp.GetRequiredService<ILogger<ProtectedSettingsStore>>()));
    }

    /// <summary>
    /// Pins the language of the note ThrottledLogging adds about held back messages to <see cref="LogCulture"/>. Without
    /// it the note follows the culture of the thread that logs, and threads of the app differ in that.
    /// </summary>
    internal static void ConfigureThrottledLogging() => ThrottledLogger.Language = LogCulture;

    /// <summary>
    /// The environment variable that sets the minimum level of the log file, for example <c>Debug</c> to also record every
    /// request.
    /// </summary>
    internal const string LogLevelVariable = "FLUXTICK_LOG_LEVEL";

    /// <summary>
    /// Reads the minimum level of the log file.
    /// </summary>
    /// <param name="value">
    /// The text of the setting, which may be null or empty.
    /// </param>
    /// <returns>
    /// The level the text names, or <see cref="Serilog.Events.LogEventLevel.Information"/> when it names none.
    /// </returns>
    internal static Serilog.Events.LogEventLevel ParseLogLevel(string? value) =>
        Enum.TryParse<Serilog.Events.LogEventLevel>(value?.Trim(), ignoreCase: true, out var level) && Enum.IsDefined(level)
            ? level
            : Serilog.Events.LogEventLevel.Information;

    /// <summary>
    /// The logger that writes the log file, kept so that the entries still waiting for the background writer can be flushed.
    /// </summary>
    private static Logger? s_logger;

    /// <summary>
    /// Writes the entries that are still waiting for the background writer and stops logging. The log file is written on a
    /// thread of its own, so a process that is about to die has to call this to keep its last entries.
    /// </summary>
    public static void FlushLogs() => s_logger?.Dispose();

    /// <summary>
    /// Builds the service provider.
    /// </summary>
    /// <param name="demo">
    /// Whether the app runs in offline demo mode.
    /// </param>
    /// <returns>
    /// The service provider.
    /// </returns>
    public static ServiceProvider Build(bool demo)
    {
        var services = new ServiceCollection();

        var levelSwitch = new LoggingLevelSwitch(ParseLogLevel(Environment.GetEnvironmentVariable(LogLevelVariable)));
        var serilog = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .WriteTo.Async(file => file.File(
                AppPaths.LogFile,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 5))
            .CreateLogger();
        s_logger = serilog;
        serilog.Information(
            "FluxTick {Version} starting on {OS} (demo: {Demo}, log level: {LogLevel})",
            SemanticVersion.TryFromAssembly(typeof(AppHost).Assembly, out var appVersion) ? appVersion.ToString() : "unknown",
            System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            demo,
            levelSwitch.MinimumLevel);
        services.AddLogging(builder => builder.AddSerilog(serilog, dispose: true));
        ConfigureThrottledLogging();

        services.AddFluxTickCore();
        services.AddSettingsStore(AppPaths.SettingsFile(demo));

        // Data sources: add one line per provider. Nothing else in the app needs to change.
        services.AddMarketDataProvider<BinanceProviderFactory>();
        services.AddMarketDataProvider<YahooProviderFactory>();
        services.AddMarketDataProvider<TencentProviderFactory>();
        services.AddMarketDataProvider<SinaProviderFactory>();
        services.AddMarketDataProvider<OkxProviderFactory>();
        services.AddMarketDataProvider<CoinbaseProviderFactory>();
        services.AddMarketDataProvider<KrakenProviderFactory>();
        services.AddMarketDataProvider<BybitProviderFactory>();
        services.AddMarketDataProvider<EulerpoolProviderFactory>();
        // The simulated source only exists for the offline demo mode; a normal install does not list it.
        if (demo)
        {
            services.AddMarketDataProvider<SimulatedProviderFactory>();
        }

        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();

        // The entry lives in the machine-wide registry, which the isolated demo mode must not touch.
        if (OperatingSystem.IsWindows() && !demo)
        {
            services.AddSingleton<IAutoStartService, WindowsAutoStartService>();
        }
        else
        {
            services.AddSingleton<IAutoStartService, NullAutoStartService>();
        }

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<SettingsManager>();
            return new UpdateService(
                AppPaths.UpdateStateFile,
                sp.GetRequiredService<ILogger<UpdateService>>(),
                SemanticVersion.TryFromAssembly(typeof(AppHost).Assembly, out var version) ? version : null,
                UpdateService.ReadOfficialBuild(typeof(AppHost).Assembly),
                AppPaths.UpdatesDirectory,
                useSetup: OperatingSystem.IsWindows(),
                isAutoCheckEnabled: () => settings.Current.Updates.AutoCheck,
                archivePattern: OperatingSystem.IsMacOS() ? UpdateService.DiskImagePattern : null);
        });
        services.AddSingleton<UpdateViewModel>();
        services.AddSingleton<AddAssetViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AlertsViewModel>();
        services.AddSingleton<HoldingViewModel>();

        // The demo mode is offline: it converts with fixed rates and keeps nothing on disk. The provider owns its client, which
        // lives as long as the app, so its connections are renewed now and then to follow a change of the address of the
        // server.
        services.AddSingleton<IExchangeRateProvider>(sp => demo
            ? new DemoRateProvider()
            : new FrankfurterRateProvider(AppHttpClient.Create(
                "FluxTick",
                decompress: true,
                connectionLifetime: TimeSpan.FromMinutes(5),
                logger: sp.GetRequiredService<ILogger<FrankfurterRateProvider>>())));
        services.AddSingleton(sp => new ExchangeRateService(
            sp.GetRequiredService<IExchangeRateProvider>(),
            sp.GetRequiredService<ILogger<ExchangeRateService>>(),
            sp.GetRequiredService<TimeProvider>(),
            new ExchangeRateOptions { CachePath = demo ? null : AppPaths.ExchangeRatesFile }));
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider();
    }
}
