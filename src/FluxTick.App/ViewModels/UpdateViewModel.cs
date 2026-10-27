using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxTick.App.Localization;
using FluxTick.App.Services;
using FluxTick.Core.Application;

namespace FluxTick.App.ViewModels;

/// <summary>
/// The update section of the settings page: the running version, the automatic check switch, and the buttons that
/// check for, download and install a newer release.
/// </summary>
public sealed partial class UpdateViewModel : LocalizedViewModel
{
    /// <summary>
    /// The update check.
    /// </summary>
    private readonly UpdateService _service;

    /// <summary>
    /// The settings store.
    /// </summary>
    private readonly SettingsManager _settings;

    /// <summary>
    /// The status the properties were last raised for.
    /// </summary>
    private UpdateStatus _shownStatus;

    /// <summary>
    /// Suppresses saves while the value is loaded from the settings.
    /// </summary>
    private bool _loading;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateViewModel"/> class.
    /// </summary>
    /// <param name="service">
    /// The update check.
    /// </param>
    /// <param name="settings">
    /// The settings store.
    /// </param>
    /// <param name="ui">
    /// The UI thread dispatcher.
    /// </param>
    public UpdateViewModel(UpdateService service, SettingsManager settings, IUiDispatcher ui)
        : base(ui)
    {
        _service = service;
        _settings = settings;
        _loading = true;
        AutoCheck = settings.Current.Updates.AutoCheck;
        _loading = false;
        _service.Changed += OnServiceChanged;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the app looks for a newer release when it starts.
    /// </summary>
    [ObservableProperty]
    public partial bool AutoCheck { get; set; }

    /// <summary>
    /// Gets the running version as text.
    /// </summary>
    public string CurrentVersionText =>
        _service.CurrentVersionText ?? Loc.Get("Update_UnknownVersion");

    /// <summary>
    /// Gets the newer version as text, or an empty text when there is none.
    /// </summary>
    private string LatestVersionText => _service.LatestVersionText ?? string.Empty;

    /// <summary>
    /// Gets the sentence that says where the check stands, or an empty text before the first check.
    /// </summary>
    public string StatusText => _service.Status switch
    {
        UpdateStatus.Checking => Loc.Get("Update_Checking"),
        UpdateStatus.UpToDate => Loc.Get("Update_UpToDate"),
        UpdateStatus.Available when _service.HasPackage => Loc.Get("Update_Available", LatestVersionText),
        UpdateStatus.Available => Loc.Get("Update_Available", LatestVersionText) + "\n" + Loc.Get("Update_NoPackage"),
        UpdateStatus.Downloading => Loc.Get("Update_Downloading", _service.DownloadPercent is { } p ? $"{p:0}%" : "…"),
        UpdateStatus.Downloaded when _service.CanInstallPackages => Loc.Get("Update_DownloadedInstall"),
        UpdateStatus.Downloaded => Loc.Get("Update_Downloaded"),
        UpdateStatus.Installing => Loc.Get("Update_Installing"),
        UpdateStatus.Failed => Loc.Get("Update_Failed"),
        _ => string.Empty,
    };

    /// <summary>
    /// Gets the release notes of the newer version, or an empty text when there are none.
    /// </summary>
    public string ReleaseNotes => _service.ReleaseNotes ?? string.Empty;

    /// <summary>
    /// Gets a value indicating whether the newer release has a page on GitHub to open.
    /// </summary>
    public bool HasReleasePage => ExternalLinks.IsGitHubUrl(_service.ReleaseUrl);

    /// <summary>
    /// Gets a value indicating whether the newer version has release notes to show.
    /// </summary>
    public bool HasReleaseNotes => _service.ReleaseNotes is not null;

    /// <summary>
    /// Gets a value indicating whether the button that looks for a newer version is shown: not while one is known
    /// and can be acted on.
    /// </summary>
    public bool ShowCheck => _service.LatestVersion is null || !_service.HasPackage;

    /// <summary>
    /// Gets the headline of the prompt that announces the newer release.
    /// </summary>
    public string PromptTitle => Loc.Get("Update_PromptTitle", LatestVersionText);

    /// <summary>
    /// Gets a value indicating whether a check can be started now.
    /// </summary>
    public bool CanCheck => _service.CurrentVersion is not null
        && _service.Status is not (UpdateStatus.Checking or UpdateStatus.Downloading or UpdateStatus.Installing);

    /// <summary>
    /// Gets a value indicating whether the newer package can be downloaded now, for a package that is unpacked by hand.
    /// </summary>
    public bool CanDownload => _service.CanDownload && !_service.CanInstallPackages;

    /// <summary>
    /// Gets a value indicating whether the package was downloaded and has to be unpacked by hand.
    /// </summary>
    public bool IsDownloaded => _service.Status == UpdateStatus.Downloaded && !_service.CanInstallPackages;

    /// <summary>
    /// Gets a value indicating whether the newer version can be downloaded and installed with one click.
    /// </summary>
    public bool CanUpdateNow => _service.CanInstallPackages && (_service.CanDownload || _service.CanInstall);

    /// <inheritdoc />
    protected override void OnLanguageChanged() => RaiseStateChanged();

    /// <inheritdoc />
    protected override void Detach() => _service.Changed -= OnServiceChanged;

    /// <summary>
    /// Saves the automatic check switch.
    /// </summary>
    /// <param name="value">
    /// The new value.
    /// </param>
    partial void OnAutoCheckChanged(bool value)
    {
        if (!_loading)
        {
            _ = _settings.UpdateAsync(s => s.Updates.AutoCheck = value);
        }
    }

    /// <summary>
    /// Looks for a newer release now.
    /// </summary>
    /// <returns>
    /// A task that completes once the check finished.
    /// </returns>
    [RelayCommand]
    private Task CheckAsync() => _service.CheckAsync(manual: true);

    /// <summary>
    /// Downloads the package of the newer release.
    /// </summary>
    /// <returns>
    /// A task that completes once the download finished.
    /// </returns>
    [RelayCommand]
    private Task DownloadAsync() => _service.DownloadAsync();

    /// <summary>
    /// Downloads the newer version if that has not happened yet, then installs it, which closes the program.
    /// </summary>
    /// <returns>
    /// A task that completes once the installer was started or something failed.
    /// </returns>
    [RelayCommand]
    private async Task UpdateNowAsync()
    {
        if (!_service.CanInstall && _service.CanDownload)
        {
            await _service.DownloadAsync();
        }

        await _service.InstallAsync();
    }

    /// <summary>
    /// Opens the page of the newer release on GitHub in the browser.
    /// </summary>
    [RelayCommand]
    private void OpenReleasePage()
    {
        ExternalLinks.TryOpenGitHub(_service.ReleaseUrl);
    }

    /// <summary>
    /// Shows the downloaded package in the file manager.
    /// </summary>
    [RelayCommand]
    private void OpenFolder()
    {
        if (_service.DownloadedFile is not { } file)
        {
            return;
        }

        var info = new ProcessStartInfo { UseShellExecute = false };
        if (OperatingSystem.IsWindows())
        {
            info.FileName = "explorer.exe";
            // ArgumentList would quote the switch and the path together, which explorer does not understand.
            info.Arguments = $"/select,\"{file}\"";
        }
        else if (OperatingSystem.IsMacOS())
        {
            info.FileName = "open";
            info.ArgumentList.Add("-R");
            info.ArgumentList.Add(file);
        }
        else
        {
            info.FileName = "xdg-open";
            info.ArgumentList.Add(Path.GetDirectoryName(file) ?? file);
        }

        try
        {
            using var process = Process.Start(info);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // There is no file manager to show it in; the status text still names the download as done.
        }
    }

    /// <summary>
    /// Shows the new state of the check.
    /// </summary>
    /// <param name="sender">
    /// The update service.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnServiceChanged(object? sender, EventArgs e) => Ui.Post(() =>
    {
        // The progress of a download changes only the text; everything else follows the status.
        if (_service.Status == _shownStatus)
        {
            OnPropertyChanged(nameof(StatusText));
        }
        else
        {
            RaiseStateChanged();
        }
    });

    /// <summary>
    /// Tells the view that everything derived from the state of the check changed.
    /// </summary>
    private void RaiseStateChanged()
    {
        _shownStatus = _service.Status;
        OnPropertyChanged(nameof(CurrentVersionText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PromptTitle));
        OnPropertyChanged(nameof(ReleaseNotes));
        OnPropertyChanged(nameof(HasReleaseNotes));
        OnPropertyChanged(nameof(HasReleasePage));
        OnPropertyChanged(nameof(ShowCheck));
        OnPropertyChanged(nameof(CanUpdateNow));
        OnPropertyChanged(nameof(CanCheck));
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(IsDownloaded));
    }
}
