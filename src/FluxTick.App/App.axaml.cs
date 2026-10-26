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
    /// The tray menu entry that opens the settings.
    /// </summary>
    private NativeMenuItem? _traySettings;

    /// <summary>
    /// The tray menu entry that opens the about window.
    /// </summary>
    private NativeMenuItem? _trayAbout;

    /// <summary>
    /// The tray menu entry that announces a newer release. It is part of the menu only while there is one.
    /// </summary>
    private NativeMenuItem? _trayUpdate;

    /// <summary>
    /// The about window, or <see langword="null"/> while it is not open.
    /// </summary>
    private AboutWindow? _aboutWindow;

    /// <summary>
    /// The update check.
    /// </summary>
    private UpdateService? _updates;

    /// <summary>
    /// The popup that lists the watched instruments after the tray icon was clicked, or <see langword="null"/> until the
    /// first time it is needed.
    /// </summary>
    private TrayPopupWindow? _trayPopup;

    /// <summary>
    /// Tells a click on the tray icon from the first half of a double-click.
    /// </summary>
    private TrayClickInterpreter? _trayClicks;

    /// <summary>
    /// How many times the desktop was asked for a tray while there was none; the tray host may start after the app.
    /// </summary>
    private int _taskbarAttempts;

    /// <summary>
    /// The language setting that was applied last, so that settings changes that leave it alone cost nothing.
    /// </summary>
    private LanguageMode? _appliedLanguage;

    /// <summary>
    /// How long a window that is about to open counts as showing prices before its real state counts.
    /// </summary>
    private static readonly TimeSpan OpeningTimeLimit = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long after the popup closed by itself a click on the tray icon is taken as the one that closed it.
    /// </summary>
    private static readonly TimeSpan TrayReopenGuard = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// How many times the desktop is asked for a tray, a while apart, before the taskbar button stays for good.
    /// </summary>
    private const int TaskbarMaxAttempts = 10;

    /// <summary>
    /// How long to wait before asking the desktop for a tray again.
    /// </summary>
    private static readonly TimeSpan TaskbarRetryInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets a value indicating whether the application is shutting down, so closing the window really closes it.
    /// </summary>
    public bool IsExiting { get; private set; }

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Writes exceptions that nothing handled to the log, so a crash or a lost background failure leaves a trace.
    /// </summary>
    /// <param name="services">
    /// The service provider that holds the logger.
    /// </param>
    private static void LogUnhandledExceptions(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<App>>();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})", e.IsTerminating);

            // The log file is written in the background, and the process dies right after this.
            if (e.IsTerminating)
            {
                AppHost.FlushLogs();
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();

            // A background task that ended because its owner was disposed is expected, not an error.
            if (!e.Exception.Flatten().InnerExceptions.All(inner => inner is OperationCanceledException))
            {
                logger.LogError(e.Exception, "A task failed and nobody observed it");
            }
        };
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var demo = desktop.Args?.Contains("--demo", StringComparer.OrdinalIgnoreCase) == true;
            _services = AppHost.Build(demo);
            LogUnhandledExceptions(_services);
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
            ApplyTaskbarButton();
            _window.GeometryChanged += OnGeometryChanged;
            _window.FullScreenChanged += OnFullScreenChanged;
            _window.Closed += OnWindowClosed;

            // A window that is about to open counts as shown from the start, so the data sources are not told to stay
            // disconnected by the first settings only to connect a moment later when the window appears.
            var startHidden = TrayPolicy.StartHidden(_settings.Current.General, desktop.Args ?? [], () => HasTrayIcon);
            BindUsage(_window, _services.GetRequiredService<MarketDataService>(), () => _window!.IsDockedAway, handler => _window!.DockedAwayChanged += handler, expectOpen: !startHidden);
            if (startHidden)
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
                ApplyTrayAssets();
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

            // The alerts watch the prices whether or not a window shows them, so the notifications are not tied to one.
            var toasts = new AlertToastPresenter(ActivateWindow);
            var alerts = _services.GetRequiredService<AlertService>();
            alerts.AlertTriggered += (_, e) => Dispatcher.UIThread.Post(() =>
            {
                viewModel.FlashAlert(e);
                toasts.Show(e);
            });
            alerts.Start();

            // The defaults are in use and nothing is saved, which the user would otherwise only find out by losing changes.
            if (_settings.StoredSettingsUnreadable)
            {
                Dispatcher.UIThread.Post(() =>
                    toasts.Show(Loc.Get("Settings_ReadFailedTitle"), Loc.Get("Settings_ReadFailedBody")));
            }

            // The alerts of an instrument go along when its data source is changed, with the price levels they had.
            viewModel.AddAsset.AlertsMoved += (_, e) => Dispatcher.UIThread.Post(() =>
                toasts.Show(Loc.Get("Alert_MovedTitle", e.ProviderName), Loc.Get("Alert_MovedBody", e.Count, e.Symbol)));

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
    /// Gets a value indicating whether the tray icon exists and the desktop shows it.
    /// </summary>
    private bool HasTrayIcon => IsTrayIconDefined && TrayAvailability.Detect();

    /// <summary>
    /// Gets a value indicating whether the application defines a tray icon, whether or not the desktop shows it.
    /// </summary>
    private bool IsTrayIconDefined => TrayIcon.GetIcons(this) is { Count: > 0 };

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
    private async Task StartAsync(MainViewModel viewModel, string[] args)
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
                    ShowAboutWindow();
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
        Task.Run(() => _settings!.TryUpdateAsync(s => ProviderOptionCleanup.DropOptionsAtDefault(s, descriptors))).GetAwaiter().GetResult();
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
            tray.ToolTipText = TrayTooltip();
            if (_trayToggle is null || _traySettings is null || _trayAbout is null || _trayExit is null)
            {
                _trayToggle = new NativeMenuItem();
                _traySettings = new NativeMenuItem();
                _trayAbout = new NativeMenuItem();
                _trayExit = new NativeMenuItem();
                _trayToggle.Click += OnToggleWindow;
                _traySettings.Click += OnTraySettingsClicked;
                _trayAbout.Click += OnTrayAboutClicked;
                _trayExit.Click += OnExit;
                tray.Menu = [_trayToggle, _traySettings, _trayAbout, new NativeMenuItemSeparator(), _trayExit];
            }

            _trayToggle.Header = Loc.Get("Tray_ShowHide");
            _traySettings.Header = Loc.Get("Tray_Settings");
            _trayAbout.Header = Loc.Get("Tray_About");
            _trayExit.Header = Loc.Get("Tray_Exit");
            ApplyUpdateTray();
        }
    }

    /// <summary>
    /// Ends the tray popup and the about window with the main window: they are windows too, and the app only ends when the
    /// last window is closed.
    /// </summary>
    /// <param name="sender">
    /// The main window.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _trayPopup?.Close();
        _aboutWindow?.Close();
    }

    /// <summary>
    /// Holds a usage of the market data for as long as the window is shown and not minimized, so that the data sources
    /// only stay connected while somebody can see the prices.
    /// </summary>
    /// <param name="window">
    /// The window that shows prices.
    /// </param>
    /// <param name="market">
    /// The market data service.
    /// </param>
    /// <param name="isAway">
    /// Tells whether the window is shown but parked out of sight, such as at a screen edge; <see langword="null"/> for never.
    /// </param>
    /// <param name="subscribeAway">
    /// Subscribes a handler to the changes of <paramref name="isAway"/>.
    /// </param>
    /// <param name="expectOpen">
    /// Whether the window is about to open, and counts as shown until it does.
    /// </param>
    private static void BindUsage(Window window, MarketDataService market, Func<bool>? isAway = null, Action<EventHandler>? subscribeAway = null, bool expectOpen = false)
    {
        var source = new OpeningShownSource(new WindowShownSource(window, isAway, subscribeAway), expectOpen);
        if (expectOpen)
        {
            window.Opened += (_, _) => source.EndOpening();

            // A window that never opens must not keep the sources connected.
            DispatcherTimer.RunOnce(source.EndOpening, OpeningTimeLimit);
        }

        _ = new UsageBinding(market, source);
    }

    /// <summary>
    /// Creates the tray popup on first use. It counts as a window that shows prices while it is open, so the data sources
    /// connect for it.
    /// </summary>
    /// <returns>
    /// The popup.
    /// </returns>
    private TrayPopupWindow EnsureTrayPopup()
    {
        if (_trayPopup is null)
        {
            _trayPopup = new TrayPopupWindow { DataContext = new TrayPopupViewModel(_services!.GetRequiredService<MainViewModel>().Rows) };
            _trayPopup.Dismissed += (_, _) => _trayClicks?.NotifyDismissed();
            BindUsage(_trayPopup, _services!.GetRequiredService<MarketDataService>());
            ApplyTrayAssets();
        }

        return _trayPopup;
    }

    /// <summary>
    /// Opens the tray popup next to the pointer, which is on the tray icon right after the click.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the pointer position is not known, so the popup has nowhere to go.
    /// </returns>
    private bool ShowTrayList()
    {
        if (!OperatingSystem.IsWindows() || !WindowsTrayCursor.TryGetPosition(out var cursor))
        {
            return false;
        }

        EnsureTrayPopup().ShowAt(new PixelRect(cursor, new PixelSize(1, 1)));
        return true;
    }

    /// <summary>
    /// Gives the tray popup the instruments the user chose for it, in the order they chose.
    /// </summary>
    private void ApplyTrayAssets()
    {
        if (_settings is not null && _trayPopup?.DataContext is TrayPopupViewModel model)
        {
            model.SetSelection(_settings.Current.General.TrayAssets);
        }
    }

    /// <summary>
    /// Gets the tooltip of the tray icon: the name and the slogan of the app.
    /// </summary>
    /// <returns>
    /// The tooltip text.
    /// </returns>
    private static string TrayTooltip() => $"FluxTick — {Loc.Get("Tagline")}";

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
    private void OnTrayUpdateClicked(object? sender, EventArgs e) => ShowFromTray(viewModel => viewModel.ShowSettings(), replaceToolWindow: true);

    /// <summary>
    /// Opens the settings window.
    /// </summary>
    /// <param name="sender">
    /// The menu item.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnTraySettingsClicked(object? sender, EventArgs e) => ShowFromTray(viewModel => viewModel.ShowSettings(), replaceToolWindow: true);

    /// <summary>
    /// Opens the about window on its own: it does not bring up the main window and does not block it.
    /// </summary>
    /// <param name="sender">
    /// The menu item.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnTrayAboutClicked(object? sender, EventArgs e) => ShowAboutWindow();

    /// <summary>
    /// Opens the about window on its own, or brings it forward when it is open.
    /// </summary>
    private void ShowAboutWindow()
    {
        if (_aboutWindow is not null)
        {
            _aboutWindow.Activate();
            return;
        }

        if (_services is null || IsExiting)
        {
            return;
        }

        var viewModel = _services.GetRequiredService<MainViewModel>();
        var window = new AboutWindow
        {
            DataContext = new AboutViewModel(viewModel.Settings.Updates),

            // A window that is always on top would cover one that is not, whichever of them was used last.
            Topmost = _window?.Topmost == true,
        };
        window.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow = window;
        window.ShowStandalone();
    }

    /// <summary>
    /// Gives the main window a taskbar button only where there is a tray icon to bring it back with. The window keeps
    /// its button until the desktop has answered, and the desktop is asked off the UI thread because one without a tray
    /// takes a moment to say so. While there is none the question is repeated a few times, since the tray host may
    /// start after the app.
    /// </summary>
    private void ApplyTaskbarButton()
    {
        if (_window is null || !IsTrayIconDefined)
        {
            return;
        }

        if (!OperatingSystem.IsLinux())
        {
            // Windows and macOS always have a tray and say so at once, so the button is right before the window opens.
            OnTrayProbed(TrayAvailability.Detect());
            return;
        }

        Task.Run(TrayAvailability.Detect).ContinueWith(
            probe =>
            {
                if (probe.IsFaulted)
                {
                    _services?.GetService<ILogger<App>>()?.LogWarning(probe.Exception, "Could not find out whether the desktop shows a tray");
                }

                Dispatcher.UIThread.Post(() => OnTrayProbed(probe.IsCompletedSuccessfully && probe.Result));
            },
            TaskScheduler.Default);
    }

    /// <summary>
    /// Matches the taskbar button of the main window to the answer of the desktop, and asks again later when there is
    /// no tray yet.
    /// </summary>
    /// <param name="hasTray">
    /// Whether the desktop shows tray icons.
    /// </param>
    private void OnTrayProbed(bool hasTray)
    {
        if (_window is null || IsExiting)
        {
            return;
        }

        _window.ShowInTaskbar = !hasTray;
        if (!hasTray && ++_taskbarAttempts < TaskbarMaxAttempts)
        {
            DispatcherTimer.RunOnce(ApplyTaskbarButton, TaskbarRetryInterval);
        }
    }

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
    /// <param name="replaceToolWindow">
    /// Whether a tool window that is already open is closed first. The view model ignores a request while one is open, so
    /// without this a choice from the tray menu would do nothing; a plain activation must leave the open window alone.
    /// </param>
    private void ShowFromTray(Action<MainViewModel> open, bool replaceToolWindow = false)
    {
        if (_window is null || _services is null)
        {
            return;
        }

        if (!_window.IsVisible)
        {
            _window.ToggleVisibility();
        }
        else
        {
            _window.RestoreIfMinimized();
        }

        var viewModel = _services.GetRequiredService<MainViewModel>();
        if (replaceToolWindow && viewModel.IsToolWindowOpen)
        {
            // The old window is gone once its close has been handled, so the new one is opened after that.
            _window.CloseToolWindow();
            Dispatcher.UIThread.Post(() => open(viewModel), DispatcherPriority.Background);
            return;
        }

        open(viewModel);
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
    /// Remembers whether the window is full screen, so that it opens the same way next time.
    /// </summary>
    /// <param name="sender">
    /// The window.
    /// </param>
    /// <param name="fullScreen">
    /// Whether the window is full screen now.
    /// </param>
    private async void OnFullScreenChanged(object? sender, bool fullScreen)
    {
        try
        {
            await _settings!.TryUpdateAsync(s =>
            {
                if (s.Window.FullScreen == fullScreen)
                {
                    return false;
                }

                s.Window.FullScreen = fullScreen;
                return true;
            });
        }
        catch (Exception ex)
        {
            // Called from an event handler, where an escaping exception would end the application.
            _services?.GetService<ILogger<App>>()?.LogWarning(ex, "Could not save the full screen state");
        }
    }

    /// <summary>
    /// Handles a click on the tray icon: one click lists the instruments, a double-click opens the main window. Where the
    /// list is not available, or turned off, a click shows or hides the window as before.
    /// </summary>
    /// <param name="sender">
    /// The tray icon.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnTrayClicked(object? sender, EventArgs e)
    {
        if (!TrayPopupAvailability.IsSupported)
        {
            _window?.ToggleVisibility();
            return;
        }

        _trayClicks ??= new TrayClickInterpreter(TimeProvider.System, OperatingSystem.IsWindows() ? WindowsTrayCursor.DoubleClickTime : TimeSpan.FromMilliseconds(500), TrayReopenGuard);
        switch (_trayClicks.Click(_trayPopup?.IsVisible == true))
        {
            case TrayClickInterpreter.TrayClickAction.ShowList:
                // A click that cannot list the instruments still does what a click did before.
                if (!ShowTrayList())
                {
                    _window?.ToggleVisibility();
                }

                break;
            case TrayClickInterpreter.TrayClickAction.HideList:
                _trayPopup?.Hide();
                break;
            case TrayClickInterpreter.TrayClickAction.OpenWindow:
                _trayPopup?.Hide();
                _window?.ToggleVisibility(showOnly: true);
                break;
        }
    }

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
