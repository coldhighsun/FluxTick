using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FluxTick.Abstractions;
using FluxTick.App.Localization;
using FluxTick.App.Services;
using FluxTick.App.ViewModels;
using FluxTick.App.Views;
using FluxTick.Core.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluxTick.App;

/// <summary>
/// The Avalonia application: wires up services, the main window and the tray icon.
/// </summary>
public sealed partial class App : Application
{
    /// <summary>
    /// The service provider, created on startup.
    /// </summary>
    private ServiceProvider? _services;

    /// <summary>
    /// The settings store.
    /// </summary>
    private SettingsManager? _settings;

    /// <summary>
    /// The main window.
    /// </summary>
    private MainWindow? _window;

    /// <summary>
    /// Saves the window geometry once it stops changing.
    /// </summary>
    private Debouncer? _geometrySave;

    /// <summary>
    /// The theme variant the up and down brushes were last built for, so an unchanged palette is not rebuilt.
    /// </summary>
    private ThemeVariant? _paletteVariant;

    /// <summary>
    /// Whether the up and down brushes were last built with red for gains.
    /// </summary>
    private bool _paletteRedUp;

    /// <summary>
    /// The tray menu entry that shows or hides the window.
    /// </summary>
    private NativeMenuItem? _trayToggle;

    /// <summary>
    /// The tray menu entry that exits the application.
    /// </summary>
    private NativeMenuItem? _trayExit;

    /// <summary>
    /// The language setting that was applied last, so that settings changes that leave it alone cost nothing.
    /// </summary>
    private LanguageMode? _appliedLanguage;

    /// <summary>
    /// Gets a value indicating whether the application is shutting down, so closing the window really closes it.
    /// </summary>
    public bool IsExiting { get; private set; }

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var demo = desktop.Args?.Contains("--demo", StringComparer.OrdinalIgnoreCase) == true;
            _services = AppHost.Build(demo);
            _settings = _services.GetRequiredService<SettingsManager>();
            _geometrySave = new Debouncer(TimeSpan.FromMilliseconds(600), TimeProvider.System);

            Task.Run(() => _settings.LoadAsync(DefaultSettings.Create(demo))).GetAwaiter().GetResult();
            CleanUpOptionsAtDefault(_services.GetRequiredService<ProviderRegistry>().Descriptors);

            // The theme must be requested before the window exists, and before the ActualThemeVariantChanged
            // subscription below: the first ApplyPalette() then builds the brushes for the final variant once.
            ApplyLanguage();
            ApplyTheme();

            var viewModel = _services.GetRequiredService<MainViewModel>();
            _window = new MainWindow { DataContext = viewModel, Logger = _services.GetRequiredService<ILogger<MainWindow>>() };
            _window.RestoreGeometry(_settings.Current.Window);
            _window.GeometryChanged += OnGeometryChanged;
            desktop.MainWindow = _window;

            ActualThemeVariantChanged += (_, _) => ApplyPalette();
            _settings.Changed += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                ApplyLanguage();
                ApplyTheme();
                ApplyPalette();
            });
            ApplyPalette();

            desktop.Exit += (_, _) =>
            {
                IsExiting = true;
                _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            };

            _ = StartAsync(viewModel, desktop.Args ?? []);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Starts the view model and honors the developer option <c>--page=add|settings</c>, which opens that page on startup.
    /// </summary>
    /// <param name="viewModel">
    /// The main view model.
    /// </param>
    /// <param name="args">
    /// The command line arguments.
    /// </param>
    /// <returns>
    /// A task that completes once startup finished.
    /// </returns>
    private static async Task StartAsync(MainViewModel viewModel, string[] args)
    {
        await viewModel.InitializeAsync();
        var page = args.FirstOrDefault(a => a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase))?["--page=".Length..];
        Dispatcher.UIThread.Post(() =>
        {
            switch (page?.ToLowerInvariant())
            {
                case "add":
                    viewModel.ShowAdd();
                    break;
                case "settings":
                    viewModel.ShowSettings();
                    break;
            }
        });
    }

    /// <summary>
    /// Removes explicit option values that equal the default, which earlier versions wrote for every option,
    /// so the settings file only holds real customisations. Does nothing, and saves nothing, when there are none.
    /// </summary>
    /// <param name="descriptors">
    /// The descriptors of the available providers.
    /// </param>
    private void CleanUpOptionsAtDefault(IReadOnlyList<ProviderDescriptor> descriptors)
    {
        if (!ProviderOptionCleanup.DropOptionsAtDefault(_settings!.Current.Clone(), descriptors))
        {
            return;
        }

        Task.Run(() => _settings.UpdateAsync(s => ProviderOptionCleanup.DropOptionsAtDefault(s, descriptors))).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Switches the interface to the configured language, and renames the tray entries to match.
    /// </summary>
    private void ApplyLanguage()
    {
        if (_settings is null)
        {
            return;
        }

        var language = _settings.Current.Appearance.Language;
        if (language == _appliedLanguage)
        {
            return;
        }

        _appliedLanguage = language;
        Loc.Instance.Apply(language);
        if (TrayIcon.GetIcons(this)?.FirstOrDefault() is { } tray)
        {
            tray.ToolTipText = $"FluxTick — {Loc.Get("Tagline")}";
            if (_trayToggle is null || _trayExit is null)
            {
                _trayToggle = new NativeMenuItem();
                _trayExit = new NativeMenuItem();
                _trayToggle.Click += OnToggleWindow;
                _trayExit.Click += OnExit;
                tray.Menu = [_trayToggle, new NativeMenuItemSeparator(), _trayExit];
            }

            _trayToggle.Header = Loc.Get("Tray_ShowHide");
            _trayExit.Header = Loc.Get("Tray_Exit");
        }
    }

    /// <summary>
    /// Requests the theme variant of the configured color scheme. Auto follows the system.
    /// </summary>
    private void ApplyTheme()
    {
        if (_settings is null)
        {
            return;
        }

        var variant = ThemeModeResolver.ToVariant(_settings.Current.Appearance.ThemeMode);
        if (RequestedThemeVariant != variant)
        {
            RequestedThemeVariant = variant;
        }
    }

    /// <summary>
    /// Replaces the up and down brushes according to the color convention and the active theme.
    /// </summary>
    private void ApplyPalette()
    {
        if (_settings is null)
        {
            return;
        }

        var redUp = ColorConventionResolver.IsRedUp(_settings.Current.Appearance.ColorConvention, CultureInfo.CurrentUICulture);
        var variant = ActualThemeVariant ?? ThemeVariant.Dark;
        if (variant == _paletteVariant && redUp == _paletteRedUp)
        {
            return;
        }

        if (TryGetResource("GainColor", variant, out var gain) && gain is Color gainColor
            && TryGetResource("LossColor", variant, out var loss) && loss is Color lossColor)
        {
            Resources["UpBrush"] = new SolidColorBrush(redUp ? lossColor : gainColor);
            Resources["DownBrush"] = new SolidColorBrush(redUp ? gainColor : lossColor);
            _paletteVariant = variant;
            _paletteRedUp = redUp;
        }
    }

    /// <summary>
    /// Persists the window geometry after it settled.
    /// </summary>
    /// <param name="sender">
    /// The window.
    /// </param>
    /// <param name="geometry">
    /// The new geometry.
    /// </param>
    private void OnGeometryChanged(object? sender, WindowGeometry geometry) =>
        _geometrySave?.Run(() => _settings!.UpdateAsync(s =>
        {
            s.Window.X = geometry.X;
            s.Window.Y = geometry.Y;
            s.Window.Width = geometry.Width;
            s.Window.Height = geometry.Height;
        }));

    /// <summary>
    /// Toggles the window when the tray icon is clicked.
    /// </summary>
    /// <param name="sender">
    /// The tray icon.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnTrayClicked(object? sender, EventArgs e) => _window?.ToggleVisibility();

    /// <summary>
    /// Toggles the window from the tray menu.
    /// </summary>
    /// <param name="sender">
    /// The menu item.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnToggleWindow(object? sender, EventArgs e) => _window?.ToggleVisibility();

    /// <summary>
    /// Exits the application from the tray menu.
    /// </summary>
    /// <param name="sender">
    /// The menu item.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnExit(object? sender, EventArgs e)
    {
        IsExiting = true;
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
