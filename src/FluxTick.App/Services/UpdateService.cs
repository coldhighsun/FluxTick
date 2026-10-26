using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using GitHubReleaseUpdater;
using GitHubReleaseUpdater.Assets;
using GitHubReleaseUpdater.Download;
using GitHubReleaseUpdater.Exceptions;
using GitHubReleaseUpdater.Installation;
using GitHubReleaseUpdater.LastCheck;
using GitHubReleaseUpdater.Versioning;
using Microsoft.Extensions.Logging;

namespace FluxTick.App.Services;

/// <summary>
/// Where the update check currently stands.
/// </summary>
public enum UpdateStatus
{
    /// <summary>
    /// Nothing is known yet: no check ran, or the automatic check was skipped.
    /// </summary>
    Idle,

    /// <summary>
    /// Asking GitHub for the latest release.
    /// </summary>
    Checking,

    /// <summary>
    /// The running version is the newest one.
    /// </summary>
    UpToDate,

    /// <summary>
    /// A newer release exists.
    /// </summary>
    Available,

    /// <summary>
    /// The package of the newer release is being downloaded.
    /// </summary>
    Downloading,

    /// <summary>
    /// The package was downloaded and its checksum verified.
    /// </summary>
    Downloaded,

    /// <summary>
    /// The installer of the downloaded package was started. It closes the program when it replaces it; when it ends
    /// without doing so, the status goes back to <see cref="Downloaded"/>.
    /// </summary>
    Installing,

    /// <summary>
    /// The last check or download failed.
    /// </summary>
    Failed,
}

/// <summary>
/// Looks for a newer release of FluxTick on GitHub and downloads its package. Where the package is a setup program
/// (Windows) it can also start it, which replaces the running program; elsewhere the package is verified and left in
/// the updates directory for the user to unpack. Members must be used from the UI thread.
/// </summary>
public sealed class UpdateService
{
    /// <summary>
    /// The GitHub account that owns the repository.
    /// </summary>
    public const string Owner = "coldhighsun";

    /// <summary>
    /// The GitHub repository the releases come from.
    /// </summary>
    public const string Repo = "FluxTick";

    /// <summary>
    /// The shortest time between two automatic checks.
    /// </summary>
    public static readonly TimeSpan MinimumCheckInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// The name of a release archive; <c>{rid}</c> is the runtime of the running program, such as <c>linux-x64</c>.
    /// </summary>
    internal const string ArchivePattern = "FluxTick-{tag}-{rid}.zip";

    /// <summary>
    /// The name of a release setup program; <c>{rid}</c> is the runtime of the running program, such as <c>win-x64</c>.
    /// </summary>
    internal const string SetupPattern = "FluxTick-{tag}-{rid}-setup.exe";

    /// <summary>
    /// How many times <see cref="InstallerGracePeriod"/> a setup program that cannot be watched is given.
    /// </summary>
    private const int UnwatchedGraceFactor = 10;

    /// <summary>
    /// The argument that makes the setup program close FluxTick when it replaces it, whatever its silent-mode default is.
    /// </summary>
    internal const string CloseApplicationsArgument = "/CLOSEAPPLICATIONS";

    /// <summary>
    /// The argument that makes the setup program start FluxTick again once it is done, which it does not after a
    /// silent install otherwise.
    /// </summary>
    internal const string RelaunchArgument = "/RELAUNCH=1";

    /// <summary>
    /// Remembers when the last check happened.
    /// </summary>
    private readonly FileLastCheckStore _store;

    /// <summary>
    /// The log.
    /// </summary>
    private readonly ILogger<UpdateService> _logger;

    /// <summary>
    /// The client used for GitHub, or <see langword="null"/> for the shared default one.
    /// </summary>
    private readonly HttpClient? _http;

    /// <summary>
    /// Where the packages are downloaded to.
    /// </summary>
    private readonly string _downloadDirectory;

    /// <summary>
    /// Whether the program was built by the release workflow.
    /// </summary>
    private readonly bool _officialBuild;

    /// <summary>
    /// Starts the setup program, or <see langword="null"/> for the default one.
    /// </summary>
    private readonly IInstallerLauncher? _launcher;

    /// <summary>
    /// The result of the last check that found a newer release.
    /// </summary>
    private UpdateCheckResult? _check;

    /// <summary>
    /// The package that was downloaded and verified for <see cref="_check"/>.
    /// </summary>
    private DownloadResult? _download;

    /// <summary>
    /// The version the user was told about, so that it is announced once.
    /// </summary>
    private SemanticVersion? _announced;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateService"/> class.
    /// </summary>
    /// <param name="stateFile">
    /// The file that remembers when the last check happened.
    /// </param>
    /// <param name="logger">
    /// The log.
    /// </param>
    /// <param name="currentVersion">
    /// The running version, or <see langword="null"/> when it cannot be told, which turns the check off.
    /// </param>
    /// <param name="officialBuild">
    /// Whether the program was built by the release workflow, which is what allows the automatic check.
    /// </param>
    /// <param name="downloadDirectory">
    /// Where packages are downloaded to.
    /// </param>
    /// <param name="useSetup">
    /// Whether the release package is a setup program that can be started (Windows), instead of an archive.
    /// </param>
    /// <param name="http">
    /// The client used for GitHub, or <see langword="null"/> for the shared default one.
    /// </param>
    /// <param name="launcher">
    /// Starts the setup program, or <see langword="null"/> for the default one.
    /// </param>
    public UpdateService(
        string stateFile,
        ILogger<UpdateService> logger,
        SemanticVersion? currentVersion,
        bool officialBuild,
        string downloadDirectory,
        bool useSetup,
        HttpClient? http = null,
        IInstallerLauncher? launcher = null)
    {
        _store = new FileLastCheckStore(stateFile, logger);
        _logger = logger;
        CurrentVersion = currentVersion;
        _officialBuild = officialBuild;
        _downloadDirectory = downloadDirectory;
        CanInstallPackages = useSetup;
        _http = http;
        _launcher = launcher;
    }

    /// <summary>
    /// Raised whenever <see cref="Status"/> or the download progress changes.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets or sets what the program does just before the setup program is started, such as saving what is still
    /// waiting to be saved. The setup program ends the program without its own shutdown. A failure of it does not stop
    /// the install.
    /// </summary>
    public Func<Task>? PrepareForInstallAsync { get; set; }

    /// <summary>
    /// Gets how long <see cref="PrepareForInstallAsync"/> is given before the setup program is started anyway.
    /// </summary>
    internal TimeSpan PrepareTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets how long the program waits after the setup program ended before it offers the package again.
    /// </summary>
    internal TimeSpan InstallerGracePeriod { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the wait for a process to end, replaceable so that tests need no real process.
    /// </summary>
    internal Func<int, Task> WaitForProcessExitAsync { get; init; } = WatchProcessAsync;

    /// <summary>
    /// Raised when an automatic check finds a newer release that was not announced yet and that can be installed.
    /// </summary>
    public event EventHandler? UpdateFound;

    /// <summary>
    /// Gets the running version, or <see langword="null"/> when it cannot be told.
    /// </summary>
    public SemanticVersion? CurrentVersion { get; }

    /// <summary>
    /// Gets where the check stands.
    /// </summary>
    public UpdateStatus Status { get; private set; }

    /// <summary>
    /// Gets the newer version, once <see cref="Status"/> says one exists.
    /// </summary>
    public SemanticVersion? LatestVersion => _check?.Update?.Version;

    /// <summary>
    /// Gets the release notes of the newer version as GitHub shows them, or <see langword="null"/> when there are none.
    /// </summary>
    public string? ReleaseNotes => _check?.Update is null || string.IsNullOrWhiteSpace(_check.ReleaseNotes)
        ? null
        : _check.ReleaseNotes.Trim();

    /// <summary>
    /// Gets the address of the page of the newer release on GitHub, or <see langword="null"/> when there is none.
    /// </summary>
    public string? ReleaseUrl => _check?.Update is null ? null : _check.Release?.HtmlUrl;

    /// <summary>
    /// Gets the download progress between 0 and 100, or <see langword="null"/> when it is not known.
    /// </summary>
    public double? DownloadPercent { get; private set; }

    /// <summary>
    /// Gets the full path of the downloaded package once <see cref="Status"/> is <see cref="UpdateStatus.Downloaded"/>.
    /// </summary>
    public string? DownloadedFile => _download?.FilePath;

    /// <summary>
    /// Gets a value indicating whether the app may look for an update by itself. It may not when the running version
    /// is unknown, or when the program was not built by the release workflow: such a build, for example one from a
    /// commit after the last tag, is below every release and would announce an update on every start. The user can
    /// still check by hand.
    /// </summary>
    public bool CanAutoCheck => CurrentVersion is not null && _officialBuild;

    /// <summary>
    /// Gets a value indicating whether the newer release has a package for the running platform.
    /// </summary>
    public bool HasPackage => _check?.Update?.SelectedAsset is not null;

    /// <summary>
    /// Gets a value indicating whether the package can be downloaded now, also to try again after a failure.
    /// </summary>
    public bool CanDownload => HasPackage && Status is UpdateStatus.Available or UpdateStatus.Failed;

    /// <summary>
    /// Gets a value indicating whether the release package is a setup program that this app can start (Windows),
    /// instead of an archive to unpack.
    /// </summary>
    public bool CanInstallPackages { get; }

    /// <summary>
    /// Gets a value indicating whether the downloaded setup program can be started now, also to try again after a
    /// failure to start it.
    /// </summary>
    public bool CanInstall => CanInstallPackages && _download is not null && Status is UpdateStatus.Downloaded or UpdateStatus.Failed;

    /// <summary>
    /// Reads the version of the running program from its assembly, which MinVer sets from the git tag.
    /// </summary>
    /// <param name="assembly">
    /// The assembly to read.
    /// </param>
    /// <returns>
    /// The version, or <see langword="null"/> when the assembly has none that can be understood.
    /// </returns>
    public static SemanticVersion? ReadVersion(Assembly assembly)
    {
        var text = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return text is not null && SemanticVersion.TryParse(text, out var version) ? version : null;
    }

    /// <summary>
    /// Reads whether the program was built by the release workflow, which the project file records in the assembly.
    /// </summary>
    /// <param name="assembly">
    /// The assembly to read.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for an official build.
    /// </returns>
    public static bool ReadOfficialBuild(Assembly assembly) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "OfficialBuild")?.Value is { } value
        && bool.TryParse(value, out var official)
        && official;

    /// <summary>
    /// Writes a version the way the interface shows it: without the build metadata, such as the commit hash, that
    /// MinVer appends.
    /// </summary>
    /// <param name="version">
    /// The version to write.
    /// </param>
    /// <returns>
    /// The version with its pre-release part, if any.
    /// </returns>
    public static string FormatVersion(SemanticVersion version) =>
        $"{version.Major}.{version.Minor}.{version.Patch}{(version.IsPrerelease ? "-" + version.Prerelease : string.Empty)}";

    /// <summary>
    /// Asks GitHub whether a newer release exists.
    /// </summary>
    /// <param name="manual">
    /// <see langword="true"/> when the user asked for the check: it ignores the check interval and reports failures.
    /// An automatic check stays quiet when it fails or when it was done recently.
    /// </param>
    /// <returns>
    /// A task that completes once the check finished.
    /// </returns>
    public async Task CheckAsync(bool manual)
    {
        if (CurrentVersion is null || Status is UpdateStatus.Checking or UpdateStatus.Downloading or UpdateStatus.Installing)
        {
            return;
        }

        // An earlier failure is not shown again by a check that stays quiet; a newer release that is known stays.
        var before = Status == UpdateStatus.Failed
            ? _check is null ? UpdateStatus.Idle : UpdateStatus.Available
            : Status;
        SetStatus(UpdateStatus.Checking);
        try
        {
            using var updater = CreateUpdater();
            var result = await updater.CheckForUpdateAsync(bypassSkippedVersion: true, bypassThrottle: manual);
            if (!result.Success)
            {
                _logger.LogWarning("Update check failed: {Error}", result.Error);
                SetStatus(StatusAfterFailedCheck(manual, before));
            }
            else if (result.Throttled)
            {
                SetStatus(before);
            }
            else if (result.IsUpdateAvailable)
            {
                // A package that was downloaded for this very version stays downloaded.
                var keepDownload = DownloadedFile is not null
                    && File.Exists(DownloadedFile)
                    && Equals(_check?.Update?.Version, result.Update!.Version);
                _check = result;
                if (!keepDownload)
                {
                    _download = null;
                }

                _logger.LogInformation("Update {Latest} is available, running {Current}", result.LatestVersion, CurrentVersion);
                SetStatus(keepDownload ? UpdateStatus.Downloaded : UpdateStatus.Available);
                await _store.SetUpdateFoundAsync(true);
                Announce(manual, result.Update!.Version);
            }
            else
            {
                _check = null;
                _download = null;
                SetStatus(UpdateStatus.UpToDate);
                await _store.SetUpdateFoundAsync(false);

                await DeleteLeftoversAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed");
            SetStatus(StatusAfterFailedCheck(manual, before));
        }
    }

    /// <summary>
    /// Decides the status after a check that did not work. A failed check says nothing against a package that was
    /// downloaded before, so that stays shown.
    /// </summary>
    /// <param name="manual">
    /// Whether the user asked for the check, which is the only case that reports the failure.
    /// </param>
    /// <param name="before">
    /// The status before the check.
    /// </param>
    /// <returns>
    /// The status to go to.
    /// </returns>
    private static UpdateStatus StatusAfterFailedCheck(bool manual, UpdateStatus before) =>
        before == UpdateStatus.Downloaded || !manual ? before : UpdateStatus.Failed;

    /// <summary>
    /// Downloads the package of the newer release into the updates directory and verifies its checksum.
    /// </summary>
    /// <returns>
    /// A task that completes once the download finished or failed.
    /// </returns>
    public async Task DownloadAsync()
    {
        if (!CanDownload || _check is null)
        {
            return;
        }

        DownloadPercent = 0;
        _download = null;
        SetStatus(UpdateStatus.Downloading);
        try
        {
            var keepName = _check.Update!.SelectedAsset!.Name;
            await Task.Run(() => CleanDownloadDirectory(keepName));
            using var updater = CreateUpdater();
            var reported = (int?)0;
            var progress = new Progress<DownloadProgress>(p =>
            {
                // The library reports every received chunk; the display only changes with the whole percent.
                var whole = p.Percentage is { } percent ? (int?)percent : null;
                if (whole == reported)
                {
                    return;
                }

                reported = whole;
                DownloadPercent = p.Percentage;
                Changed?.Invoke(this, EventArgs.Empty);
            });
            var file = await updater.DownloadAsync(_check, _downloadDirectory, progress);
            _download = file;
            _logger.LogInformation("Downloaded update to {Path}, verified: {Verified}", file.FilePath, file.Verified);
            SetStatus(UpdateStatus.Downloaded);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update download failed");
            SetStatus(UpdateStatus.Failed);
        }
    }

    /// <summary>
    /// Deletes what earlier downloads left in the updates directory once the app is up to date: the setup program of an
    /// update that was just installed is not needed again. It is a chore on the side, so a failure only means the files
    /// stay until the next try; a file still in use, such as the setup program that is just finishing, is the usual one.
    /// The disk work is done off the UI thread, and the status is looked at again before each file, so that a download
    /// that starts meanwhile keeps its files.
    /// </summary>
    /// <returns>
    /// A task that completes when the files that could be deleted are deleted.
    /// </returns>
    public async Task DeleteLeftoversAsync()
    {
        if (Status != UpdateStatus.UpToDate)
        {
            return;
        }

        try
        {
            var files = await Task.Run(() => Directory.Exists(_downloadDirectory)
                ? Directory.GetFiles(_downloadDirectory)
                : []);
            foreach (var file in files)
            {
                if (Status != UpdateStatus.UpToDate)
                {
                    return;
                }

                await Task.Run(() => TryDelete(file));
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Could not delete the leftover update files");
        }
    }

    /// <summary>
    /// Starts the downloaded setup program, which closes the running program itself once it replaces it. The program
    /// is not closed here, so that a refused elevation prompt or a setup program that fails or is cancelled leaves it
    /// running; when the setup program ends while the program still runs, the package is offered again.
    /// </summary>
    /// <returns>
    /// A task that completes once the setup program was started and has ended, or failed to start.
    /// </returns>
    public async Task InstallAsync()
    {
        if (!CanInstall || _download is not { } download)
        {
            return;
        }

        SetStatus(UpdateStatus.Installing);
        await PrepareAsync();
        InstallerLaunchResult launched;
        try
        {
            using var updater = CreateUpdater();
            launched = await updater.LaunchInstallerAsync(download, new InstallerLaunchOptions
            {
                Arguments = $"{InstallerLaunchOptions.InnoSetupSilent.Arguments} {CloseApplicationsArgument} {RelaunchArgument}",
                RequireVerified = true,
            });
        }
        catch (InstallerLaunchException ex) when (ex.IsUserCancelled)
        {
            _logger.LogInformation("The installer was not allowed to start");
            SetStatus(UpdateStatus.Downloaded);
            return;
        }
        catch (InstallerLaunchException ex)
        {
            _logger.LogWarning(ex, "Could not start the installer");
            SetStatus(UpdateStatus.Failed);
            return;
        }
        catch (UpdaterException ex)
        {
            // The file is not what was verified: trying it again would fail the same way, so it is downloaded again.
            _logger.LogWarning(ex, "The downloaded installer cannot be used");
            _download = null;
            SetStatus(UpdateStatus.Failed);
            return;
        }
        catch (Exception ex)
        {
            // Typically a virus scanner holding the fresh file; the file itself is fine, so it is kept for another try.
            _logger.LogWarning(ex, "Could not start the downloaded installer");
            SetStatus(UpdateStatus.Failed);
            return;
        }

        _logger.LogInformation("Started the installer {Path}", download.FilePath);
        await WaitForInstallerAsync(launched.ProcessId);
    }

    /// <summary>
    /// Lets the program get ready for being ended by the setup program.
    /// </summary>
    /// <returns>
    /// A task that completes when that is done or failed.
    /// </returns>
    private async Task PrepareAsync()
    {
        if (PrepareForInstallAsync is not { } prepare)
        {
            return;
        }

        try
        {
            await prepare().WaitAsync(PrepareTimeout);
        }
        catch (Exception ex)
        {
            // Not being able to save, or a save that takes too long, must not keep the update from being installed.
            _logger.LogWarning(ex, "Could not get ready for the installer");
        }
    }

    /// <summary>
    /// Waits for the setup program to end. When it does while this program still runs, it did not replace it: the user
    /// cancelled it or it failed, so the package is offered again. A short wait follows the end of the process, since a
    /// setup program may hand over to an elevated copy of itself and end first. A setup program that cannot be watched,
    /// or that has no process to watch, is given a long time instead.
    /// </summary>
    /// <param name="processId">
    /// The process of the setup program, or <see langword="null"/> when the launcher started none.
    /// </param>
    /// <returns>
    /// A task that completes when the package is offered again.
    /// </returns>
    private async Task WaitForInstallerAsync(int? processId)
    {
        var wait = InstallerGracePeriod;
        if (processId is null)
        {
            wait *= UnwatchedGraceFactor;
        }
        else
        {
            try
            {
                await WaitForProcessExitAsync(processId.Value);
            }
            catch (ArgumentException)
            {
                // The setup program is already gone.
            }
            catch (Exception ex)
            {
                // An elevated setup program, for example, may not be watched from here.
                wait *= UnwatchedGraceFactor;
                _logger.LogInformation(ex, "Could not watch the installer, waiting {Wait} for it", wait);
            }
        }

        await Task.Delay(wait);
        _logger.LogWarning("The installer ended without replacing the running program");
        SetStatus(UpdateStatus.Downloaded);
    }

    /// <summary>
    /// Waits for a process to end.
    /// </summary>
    /// <param name="processId">
    /// The process.
    /// </param>
    /// <returns>
    /// A task that completes when the process ended.
    /// </returns>
    private static async Task WatchProcessAsync(int processId)
    {
        using var process = Process.GetProcessById(processId);
        await process.WaitForExitAsync();
    }

    /// <summary>
    /// Tells the listeners about a newer release found by an automatic check, once per version.
    /// </summary>
    /// <param name="manual">
    /// Whether the user asked for the check, who sees the result anyway.
    /// </param>
    /// <param name="version">
    /// The newer version.
    /// </param>
    private void Announce(bool manual, SemanticVersion version)
    {
        if (manual || !CanInstallPackages || !HasPackage || Equals(_announced, version))
        {
            return;
        }

        _announced = version;
        UpdateFound?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Deletes what earlier downloads left in the updates directory, except the files of the package about to be
    /// downloaded, whose partial file lets an interrupted download go on. A file that cannot be deleted stays.
    /// </summary>
    /// <param name="keepName">
    /// The file name of the package about to be downloaded, or <see langword="null"/> to delete everything.
    /// </param>
    private void CleanDownloadDirectory(string? keepName)
    {
        if (!Directory.Exists(_downloadDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_downloadDirectory))
        {
            if (keepName is not null && Path.GetFileName(file).StartsWith(keepName, StringComparison.Ordinal))
            {
                continue;
            }

            TryDelete(file);
        }
    }

    /// <summary>
    /// Deletes a file of an earlier download. A file that cannot be deleted stays.
    /// </summary>
    /// <param name="file">
    /// The file.
    /// </param>
    private void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Typically still in use; it is tried again later.
            _logger.LogInformation(ex, "Could not delete the old update file {Path}", file);
        }
    }

    /// <summary>
    /// Creates the client library, which holds no state besides its options and may be created per use.
    /// </summary>
    /// <returns>
    /// The updater.
    /// </returns>
    private ReleaseUpdater CreateUpdater() => new(new UpdaterOptions
    {
        Owner = Owner,
        Repo = Repo,
        CurrentVersion = CurrentVersion!,
        TagPrefix = "v",
        // Only stable releases are offered, whatever version is running.
        IncludePrerelease = false,
        AssetSelector = new PatternAssetSelector(CanInstallPackages ? SetupPattern : ArchivePattern),
        InstallerLauncher = _launcher,
        RequireChecksum = true,
        LastCheckStore = _store,
        MinimumCheckInterval = MinimumCheckInterval,
        HttpClient = _http,
    });

    /// <summary>
    /// Changes the status and tells the listeners.
    /// </summary>
    /// <param name="status">
    /// The new status.
    /// </param>
    private void SetStatus(UpdateStatus status)
    {
        Status = status;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Keeps when the last check happened and whether it found an update in a small file of its own, so that the
    /// interval survives a restart. It is not part of the settings: saving settings makes the whole app apply them
    /// again, which a bookkeeping write of the update check must not do.
    /// </summary>
    /// <param name="path">
    /// The file to keep the state in.
    /// </param>
    /// <param name="logger">
    /// The log.
    /// </param>
    private sealed class FileLastCheckStore(string path, ILogger logger) : ILastCheckStore
    {
        /// <summary>
        /// The serializer options of the state file.
        /// </summary>
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        /// <summary>
        /// The state, read from the file on first use.
        /// </summary>
        private State? _state;

        /// <summary>
        /// Remembers whether the last successful check found a newer release. While one was found, the check is not
        /// throttled, so that the announcement comes back after a restart.
        /// </summary>
        /// <param name="found">
        /// Whether a newer release was found.
        /// </param>
        /// <returns>
        /// A task that completes once the state is saved. It does not fail when saving does.
        /// </returns>
        public async Task SetUpdateFoundAsync(bool found)
        {
            (await LoadAsync()).UpdateFound = found;
            await SaveAsync();
        }

        /// <inheritdoc />
        public async Task<DateTimeOffset?> GetLastCheckedAtAsync(CancellationToken cancellationToken = default)
        {
            var state = await LoadAsync();
            return state.UpdateFound ? null : state.LastCheckedAt;
        }

        /// <inheritdoc />
        public async Task SetLastCheckedAtAsync(DateTimeOffset checkedAt, CancellationToken cancellationToken = default)
        {
            (await LoadAsync()).LastCheckedAt = checkedAt;
            await SaveAsync();
        }

        /// <inheritdoc />
        public Task<SemanticVersion?> GetSkippedVersionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<SemanticVersion?>(null);

        /// <inheritdoc />
        public Task SetSkippedVersionAsync(SemanticVersion version, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        /// <inheritdoc />
        public Task ClearSkippedVersionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <summary>
        /// Gets the state, reading the file the first time. A missing or damaged file means an empty state.
        /// </summary>
        /// <returns>
        /// The state.
        /// </returns>
        private async Task<State> LoadAsync()
        {
            if (_state is not null)
            {
                return _state;
            }

            try
            {
                await using var stream = File.OpenRead(path);
                _state = await JsonSerializer.DeserializeAsync<State>(stream, Json);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // First start, or a file that cannot be read: checking again is harmless.
            }

            return _state ??= new State();
        }

        /// <summary>
        /// Writes the state to the file, through a temporary file so that an interrupted write cannot leave half a file
        /// behind. Failing to do so only means that the interval is not remembered.
        /// </summary>
        /// <returns>
        /// A task that completes once the file is written or the attempt failed.
        /// </returns>
        private async Task SaveAsync()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporary = path + ".tmp";
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(_state, Json));
                File.Move(temporary, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not save the update state to {Path}", path);
            }
        }

        /// <summary>
        /// What is remembered between runs.
        /// </summary>
        private sealed class State
        {
            /// <summary>
            /// Gets or sets when the last check finished.
            /// </summary>
            public DateTimeOffset? LastCheckedAt { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether the last successful check found a newer release.
            /// </summary>
            public bool UpdateFound { get; set; }
        }
    }
}
