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
    /// The tray menu entry that opens the about window.
    /// </summary>
    private NativeMenuItem? _trayAbout;

    /// <summary>
    /// The tray menu entry that announces a newer release. It is part of the menu only while there is one.
    /// </summary>
    private NativeMenuItem? _trayUpdate;

    /// <summary>
    /// The update check.
    /// </summary>
    private UpdateService? _updates;

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

            RefreshAutoStartEntry();

            // The theme must be requested before the window exists, and before the ActualThemeVariantChanged
            // subscription below: the first ApplyPalette() then builds the brushes for the final variant once.
            ApplyLanguage();
            ApplyTheme();

            var viewModel = _services.GetRequiredService<MainViewModel>();
            _window = new MainWindow { DataContext = viewModel, Logger = _services.GetRequiredService<ILogger<MainWindow>>() };
            _window.RestoreGeometry(_settings.Current.Window);
            _window.GeometryChanged += OnGeometryChanged;
            if (TrayPolicy.StartHidden(_settings.Current.General, desktop.Args ?? [], () => HasTrayIcon))
            {
                ShowFromTrayLater(desktop, _window);
            }
            else
            {
                desktop.MainWindow = _window;
            }

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

                // A move or resize in the last moments is still waiting for its quiet period; save it now.
                try
                {
                    _geometrySave?.FlushAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    // Nothing can be done about a failed save now, and the services below must still be shut down.
                    _services.GetService<ILogger<App>>()?.LogWarning(ex, "Could not save the window position on exit");
                }

                _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            };

            if (Program.Instance is { } instance)
            {
                instance.Activated += (_, _) => Dispatcher.UIThread.Post(ActivateWindow);
                instance.StartListening();
            }

            _updates = _services.GetRequiredService<UpdateService>();
            _updates.Changed += (_, _) => Dispatcher.UIThread.Post(ApplyUpdateTray);
            StartUpdateCheck();

            _ = StartAsync(viewModel, desktop.Args ?? []);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Gets a value indicating whether closing the main window hides it to the tray. It does unless the user turned that
    /// off, and always when there is no tray icon to bring the window back from.
    /// </summary>
    public bool CloseToTray => _settings is null || TrayPolicy.CloseHidesWindow(_settings.Current.General, () => HasTrayIcon);

    /// <summary>
    /// Whether the desktop was found to show tray icons. Only a positive answer is kept: at sign-in the tray host may
    /// start after this app, so a negative answer is asked again the next time it matters.
    /// </summary>
    private bool _trayShown;

    /// <summary>
    /// Gets a value indicating whether the tray icon exists and the desktop shows it.
    /// </summary>
    private bool HasTrayIcon => TrayIcon.GetIcons(this)?.Count > 0 && (_trayShown || (_trayShown = TrayAvailability.Detect()));

    /// <summary>
    /// Starts without showing the window: the app runs without a main window until the window is first shown from the
    /// tray, and only then becomes the main window that ends the app when it closes.
    /// </summary>
    /// <param name="desktop">
    /// The desktop lifetime.
    /// </param>
    /// <param name="window">
    /// The main window.
    /// </param>
    private static void ShowFromTrayLater(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window)
    {
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        void OnOpened(object? sender, EventArgs e)
        {
            window.Opened -= OnOpened;
            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
        }

        window.Opened += OnOpened;
    }

    /// <summary>
    /// Points the start at sign-in entry at this executable when the one it names is gone, which happens when the app was
    /// moved. An entry that names another copy that still exists is left as the user set it.
    /// </summary>
    private void RefreshAutoStartEntry()
    {
        try
        {
            _services!.GetRequiredService<IAutoStartService>().RepairIfMoved();
        }
        catch (Exception)
        {
            // The service has logged the reason; starting the app does not depend on it.
        }
    }

    /// <summary>
    /// Starts the view model and honors the developer option <c>--page=add|settings|about</c>, which opens that page on startup.
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
                case "about":
                    viewModel.ShowAbout();
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
            if (_trayToggle is null || _trayAbout is null || _trayExit is null)
            {
                _trayToggle = new NativeMenuItem();
                _trayAbout = new NativeMenuItem();
                _trayExit = new NativeMenuItem();
                _trayToggle.Click += OnToggleWindow;
                _trayAbout.Click += OnTrayAboutClicked;
                _trayExit.Click += OnExit;
                tray.Menu = [_trayToggle, _trayAbout, new NativeMenuItemSeparator(), _trayExit];
            }

            _trayToggle.Header = Loc.Get("Tray_ShowHide");
            _trayAbout.Header = Loc.Get("Tray_About");
            _trayExit.Header = Loc.Get("Tray_Exit");
            ApplyUpdateTray();
        }
    }

    /// <summary>
    /// Looks for a newer release once the app is up, unless the user turned that off or this is a development build.
    /// </summary>
    private void StartUpdateCheck()
    {
        if (_settings!.Current.Updates.AutoCheck && _updates!.CanAutoCheck)
        {
            Dispatcher.UIThread.Post(async () => await _updates.CheckAsync(manual: false), DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Adds the "new version" entry to the top of the tray menu while a newer release is known, and removes it otherwise.
    /// </summary>
    private void ApplyUpdateTray()
    {
        if (_updates is null || TrayIcon.GetIcons(this)?.FirstOrDefault()?.Menu is not { } menu)
        {
            return;
        }

        var announce = _updates.Status is UpdateStatus.Available or UpdateStatus.Downloading or UpdateStatus.Downloaded or UpdateStatus.Failed
            && _updates.LatestVersion is not null;
        if (!announce)
        {
            if (_trayUpdate is not null)
            {
                menu.Items.Remove(_trayUpdate);
            }

            return;
        }

        if (_trayUpdate is null)
        {
            _trayUpdate = new NativeMenuItem();
            _trayUpdate.Click += OnTrayUpdateClicked;
        }

        _trayUpdate.Header = Loc.Get("Tray_UpdateAvailable", UpdateService.FormatVersion(_updates.LatestVersion!));
        if (!menu.Items.Contains(_trayUpdate))
        {
            menu.Items.Insert(0, _trayUpdate);
        }
    }

    /// <summary>
    /// Opens the settings page, where the new release can be downloaded.
    /// </summary>
    /// <param name="sender">
    /// The menu item.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnTrayUpdateClicked(object? sender, EventArgs e) => ShowFromTray(viewModel => viewModel.ShowSettings());

    /// <summary>
    /// Opens the about window.
    /// </summary>
    /// <param name="sender">
    /// The menu item.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnTrayAboutClicked(object? sender, EventArgs e) => ShowFromTray(viewModel => viewModel.ShowAbout());

    /// <summary>
    /// Brings the main window forward when another copy of the app was started, showing it first when it is hidden.
    /// </summary>
    private void ActivateWindow()
    {
        ShowFromTray(_ => { });
        _window?.Activate();
    }

    /// <summary>
    /// Shows the main window when it is hidden, and then opens a tool window on it.
    /// </summary>
    /// <param name="open">
    /// Opens the tool window.
    /// </param>
    private void ShowFromTray(Action<MainViewModel> open)
    {
        if (_window is null || _services is null)
        {
            return;
        }

        if (!_window.IsVisible)
        {
            _window.ToggleVisibility();
        }

        open(_services.GetRequiredService<MainViewModel>());
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
