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
/// check for and download a newer release.
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
        _service.CurrentVersion is { } version ? UpdateService.FormatVersion(version) : Loc.Get("Update_UnknownVersion");

    /// <summary>
    /// Gets the newer version as text, or an empty text when there is none.
    /// </summary>
    private string LatestVersionText => _service.LatestVersion is { } version ? UpdateService.FormatVersion(version) : string.Empty;

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
        UpdateStatus.Downloaded => Loc.Get("Update_Downloaded"),
        UpdateStatus.Failed => Loc.Get("Update_Failed"),
        _ => string.Empty,
    };

    /// <summary>
    /// Gets a value indicating whether a check can be started now.
    /// </summary>
    public bool CanCheck => _service.CurrentVersion is not null && _service.Status is not (UpdateStatus.Checking or UpdateStatus.Downloading);

    /// <summary>
    /// Gets a value indicating whether the newer package can be downloaded now.
    /// </summary>
    public bool CanDownload => _service.CanDownload;

    /// <summary>
    /// Gets a value indicating whether the package was downloaded.
    /// </summary>
    public bool IsDownloaded => _service.Status == UpdateStatus.Downloaded;

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
    private void OnServiceChanged(object? sender, EventArgs e) => Ui.Post(RaiseStateChanged);

    /// <summary>
    /// Tells the view that everything derived from the state of the check changed.
    /// </summary>
    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(CurrentVersionText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanCheck));
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(IsDownloaded));
    }
}
