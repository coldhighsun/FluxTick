using System.Reflection;
using GitHubReleaseUpdater;
using GitHubReleaseUpdater.Assets;
using GitHubReleaseUpdater.AutoUpdate;
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
/// the updates directory for the user to unpack. The work is done by the update controller of the GitHubReleaseUpdater
/// package; this class turns its state into what the interface shows. Members must be used from the UI thread.
/// </summary>
public sealed class UpdateService : IDisposable
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
    /// The name of a release disk image, which is the package on macOS; <c>{rid}</c> is the runtime of the running
    /// program, such as <c>osx-arm64</c>.
    /// </summary>
    internal const string DiskImagePattern = "FluxTick-{tag}-{rid}.dmg";

    /// <summary>
    /// The name of a release setup program; <c>{rid}</c> is the runtime of the running program, such as <c>win-x64</c>.
    /// </summary>
    internal const string SetupPattern = "FluxTick-{tag}-{rid}-setup.exe";

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
    /// How long after start the first automatic check is made, so that it does not compete with the app starting up.
    /// </summary>
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How often the app considers looking for a newer release while it runs. Shorter than
    /// <see cref="MinimumCheckInterval"/>, because the updater skips a check that is too soon after the last one and a
    /// round of exactly that interval would always be too soon.
    /// </summary>
    private static readonly TimeSpan CheckRound = TimeSpan.FromHours(1);

    /// <summary>
    /// The log.
    /// </summary>
    private readonly ILogger<UpdateService> _logger;

    /// <summary>
    /// Whether the program was built by the release workflow.
    /// </summary>
    private readonly bool _officialBuild;

    /// <summary>
    /// The client library's updater, or <see langword="null"/> when the running version is unknown.
    /// </summary>
    private readonly ReleaseUpdater? _updater;

    /// <summary>
    /// Runs the update flow, or <see langword="null"/> when the running version is unknown.
    /// </summary>
    private readonly UpdateController? _controller;

    /// <summary>
    /// Stops the background checks.
    /// </summary>
    private readonly CancellationTokenSource _stop = new();

    /// <summary>
    /// Whether the service was disposed, after which it does nothing more.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// The error of the last check, download or installation that the interface is to show as a failure: the user asked
    /// for it, or it concerns a package they started. The controller forgets its last error at the next change of state,
    /// and this stops matching with it.
    /// </summary>
    private Exception? _shownError;

    /// <summary>
    /// Whether an installation the user started has not ended yet, which is the only time the controller may be allowed
    /// to go ahead with one, and which keeps a second one from starting beside it.
    /// </summary>
    private bool _installing;

    /// <summary>
    /// How often the loop of automatic checks considers looking for a newer release, which is also how long it waits
    /// before starting again after a failure.
    /// </summary>
    private readonly TimeSpan _checkRound;

    /// <summary>
    /// Whether <see cref="Start"/> already began the automatic checks.
    /// </summary>
    private bool _started;

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
    /// Where packages are downloaded to. Nothing else may be kept there: older files are deleted.
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
    /// <param name="isAutoCheckEnabled">
    /// Asked before every automatic check, so that the setting can change while the app runs; null always checks.
    /// </param>
    /// <param name="prepareTimeout">
    /// How long <see cref="PrepareForInstallAsync"/> is given, or <see langword="null"/> for the default.
    /// </param>
    /// <param name="installerGracePeriod">
    /// How long to wait after the setup program ended before offering the package again, or <see langword="null"/> for
    /// the default.
    /// </param>
    /// <param name="firstCheckDelay">
    /// How long after <see cref="Start"/> the first automatic check is made, or <see langword="null"/> for the default.
    /// </param>
    /// <param name="checkRound">
    /// How often the loop of automatic checks considers looking for a newer release, or <see langword="null"/> for the
    /// default.
    /// </param>
    /// <param name="archivePattern">
    /// The name of the release package where it is not a setup program, or <see langword="null"/> for
    /// <see cref="ArchivePattern"/>.
    /// </param>
    public UpdateService(
        string stateFile,
        ILogger<UpdateService> logger,
        SemanticVersion? currentVersion,
        bool officialBuild,
        string downloadDirectory,
        bool useSetup,
        HttpClient? http = null,
        IInstallerLauncher? launcher = null,
        Func<bool>? isAutoCheckEnabled = null,
        TimeSpan? prepareTimeout = null,
        TimeSpan? installerGracePeriod = null,
        TimeSpan? firstCheckDelay = null,
        TimeSpan? checkRound = null,
        string? archivePattern = null)
    {
        _logger = logger;
        _checkRound = checkRound ?? CheckRound;
        CurrentVersion = currentVersion;
        _officialBuild = officialBuild;
        CanInstallPackages = useSetup;
        if (currentVersion is null)
        {
            return;
        }

        _updater = new ReleaseUpdater(new UpdaterOptions
        {
            Owner = Owner,
            Repo = Repo,
            CurrentVersion = currentVersion,
            TagPrefix = "v",

            // Only stable releases are offered, whatever version is running.
            IncludePrerelease = false,
            AssetSelector = new PatternAssetSelector(useSetup ? SetupPattern : archivePattern ?? ArchivePattern),
            InstallerLauncher = launcher,
            RequireChecksum = true,

            // Not part of the settings: saving settings makes the whole app apply them again, which a bookkeeping write
            // of the update check must not do.
            LastCheckStore = new JsonFileLastCheckStore(stateFile),
            MinimumCheckInterval = MinimumCheckInterval,
            HttpClient = http,
        });
        _controller = new UpdateController(_updater, new Presenter(this), new UpdateControllerOptions
        {
            InitialDelay = firstCheckDelay ?? FirstCheckDelay,
            CheckInterval = _checkRound,

            IsAutoCheckEnabled = isAutoCheckEnabled,

            // A known newer release is not asked for again: nothing changes until it is installed.
            SkipChecksWhileUpdateKnown = true,

            // A check the user asks for only finds out; installing is up to them.
            AskToInstallAfterManualCheck = false,
            DownloadDirectory = downloadDirectory,
            InstallerLaunchOptions = InstallerLaunchOptions.InnoSetupSilent.WithArguments($"{CloseApplicationsArgument} {RelaunchArgument}"),
            LaunchInstaller = useSetup,
            WatchInstaller = useSetup,
            CleanDownloadDirectory = true,
            BeforeInstallAsync = _ => PrepareForInstallAsync?.Invoke() ?? Task.CompletedTask,
            BeforeInstallTimeout = prepareTimeout ?? TimeSpan.FromSeconds(10),
            InstallerGracePeriod = installerGracePeriod ?? TimeSpan.FromSeconds(30),
            OnError = error => _logger.LogWarning(error, "Update step failed"),
        });
        _controller.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raised whenever <see cref="Status"/> or the download progress changes.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised when an automatic check finds a newer release that was not announced yet and that can be installed.
    /// </summary>
    public event EventHandler? UpdateFound;

    /// <summary>
    /// Gets or sets what the program does just before the setup program is started, such as saving what is still
    /// waiting to be saved. The setup program ends the program without its own shutdown. A failure of it does not stop
    /// the install.
    /// </summary>
    public Func<Task>? PrepareForInstallAsync { get; set; }

    /// <summary>
    /// Gets the running version, or <see langword="null"/> when it cannot be told.
    /// </summary>
    public SemanticVersion? CurrentVersion { get; }

    /// <summary>
    /// Gets where the check stands.
    /// </summary>
    public UpdateStatus Status => _controller?.State switch
    {
        UpdateState.Checking => UpdateStatus.Checking,
        UpdateState.UpToDate => UpdateStatus.UpToDate,
        UpdateState.Available => ShowsFailure ? UpdateStatus.Failed : UpdateStatus.Available,
        UpdateState.Downloading => UpdateStatus.Downloading,
        UpdateState.Downloaded => ShowsFailure ? UpdateStatus.Failed : UpdateStatus.Downloaded,
        UpdateState.Installing => UpdateStatus.Installing,
        UpdateState.Failed when ShowsFailure => UpdateStatus.Failed,
        _ => UpdateStatus.Idle,
    };

    /// <summary>
    /// Gets a value indicating whether the last error of the controller is one the interface shows as a failure.
    /// </summary>
    private bool ShowsFailure => _controller?.LastError is { } error && ReferenceEquals(error, _shownError);

    /// <summary>
    /// Gets the newer version, once <see cref="Status"/> says one exists.
    /// </summary>
    public SemanticVersion? LatestVersion => _controller?.Available?.Version;

    /// <summary>
    /// Gets the running version as text without build metadata, or <see langword="null"/> when it cannot be told.
    /// </summary>
    public string? CurrentVersionText => CurrentVersion?.ToString(includeMetadata: false);

    /// <summary>
    /// Gets the newer version as text without build metadata, or <see langword="null"/> when there is none.
    /// </summary>
    public string? LatestVersionText => LatestVersion?.ToString(includeMetadata: false);

    /// <summary>
    /// Gets the release notes of the newer version as GitHub shows them, or <see langword="null"/> when there are none.
    /// </summary>
    public string? ReleaseNotes => _controller?.Available?.Release.Body is { } notes && !string.IsNullOrWhiteSpace(notes)
        ? notes.Trim()
        : null;

    /// <summary>
    /// Gets the address of the page of the newer release on GitHub, or <see langword="null"/> when there is none.
    /// </summary>
    public string? ReleaseUrl => _controller?.Available?.Release.HtmlUrl;

    /// <summary>
    /// Gets the download progress between 0 and 100, or <see langword="null"/> when it is not known.
    /// </summary>
    public double? DownloadPercent { get; private set; }

    /// <summary>
    /// Gets the full path of the downloaded package once <see cref="Status"/> is <see cref="UpdateStatus.Downloaded"/>.
    /// </summary>
    public string? DownloadedFile => _controller?.DownloadedFile;

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
    public bool HasPackage => _controller?.HasInstaller == true;

    /// <summary>
    /// Gets a value indicating whether the package can be downloaded now, also to try again after a failure.
    /// </summary>
    public bool CanDownload => HasPackage && _controller!.State == UpdateState.Available;

    /// <summary>
    /// Gets a value indicating whether the release package is a setup program that this app can start (Windows),
    /// instead of an archive to unpack.
    /// </summary>
    public bool CanInstallPackages { get; }

    /// <summary>
    /// Gets a value indicating whether the downloaded setup program can be started now, also to try again after a
    /// failure to start it.
    /// </summary>
    public bool CanInstall => CanInstallPackages && _controller?.State == UpdateState.Downloaded;

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
    /// Starts the automatic checks: the first soon after the call, then now and then while the app runs. Does nothing when
    /// <see cref="CanAutoCheck"/> is false.
    /// </summary>
    public void Start()
    {
        if (!_disposed && !_started && _controller is not null && CanAutoCheck)
        {
            _started = true;
            _ = RunChecksAsync(_controller);
        }
    }

    /// <summary>
    /// Runs the controller's loop of automatic checks until the service is disposed. A failure of the loop is logged and
    /// the loop is started again after a round.
    /// </summary>
    /// <param name="controller">
    /// The controller to run.
    /// </param>
    /// <returns>
    /// A task that completes when the loop ends.
    /// </returns>
    private async Task RunChecksAsync(UpdateController controller)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await controller.RunAsync(_stop.Token);
                return;
            }
            catch (OperationCanceledException)
            {
                // The service was disposed.
                return;
            }
            catch (Exception ex)
            {
                // Nothing awaits the loop; without this its end would go unnoticed and no more checks would be made.
                _logger.LogError(ex, "The automatic update check stopped, trying again later");
            }

            try
            {
                await Task.Delay(_checkRound, _stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

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
    public Task CheckAsync(bool manual)
    {
        _logger.LogInformation("Checking for updates (manual: {Manual}, available: {Available})", manual, _controller is not null);
        return _controller?.CheckAsync(manual) ?? Task.CompletedTask;
    }

    /// <summary>
    /// Downloads the package of the newer release into the updates directory and verifies its checksum.
    /// </summary>
    /// <returns>
    /// A task that completes once the download finished or failed.
    /// </returns>
    public Task DownloadAsync() => _controller?.DownloadAsync() ?? Task.CompletedTask;

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
        if (_controller is null || !CanInstallPackages || _installing || _controller.State != UpdateState.Downloaded)
        {
            return;
        }

        _installing = true;
        try
        {
            await _controller.InstallAsync();
        }
        finally
        {
            _installing = false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // The token source is not disposed: the loop may still be reading its token while it winds down, and a source
        // without a timer holds nothing that needs releasing.
        _stop.Cancel();
        _updater?.Dispose();
    }

    /// <summary>
    /// Shows a failure the user is to be told about.
    /// </summary>
    /// <param name="error">
    /// The error, which the controller keeps as its last error until the state changes.
    /// </param>
    private void ShowFailure(Exception error)
    {
        _shownError = error;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// What the controller asks of the app: it tells the user little, answers the question to install and records
    /// failures for the interface to show.
    /// </summary>
    /// <param name="owner">
    /// The service whose state is changed.
    /// </param>
    internal sealed class Presenter(UpdateService owner) : IUpdatePresenter
    {
        /// <summary>
        /// Tells whether an error only says that the release has no package for this platform, which is not a failure.
        /// </summary>
        /// <param name="error">
        /// The error, if any.
        /// </param>
        /// <returns>
        /// <see langword="true"/> for the missing package.
        /// </returns>
        private static bool IsNoPackage(Exception? error) => error is AssetNotFoundException;

        /// <inheritdoc />
        public void Announce(AvailableUpdate update)
        {
            owner._logger.LogInformation("Update {Latest} is available, running {Current}", update.Version, owner.CurrentVersion);
            if (owner.CanInstallPackages)
            {
                owner.UpdateFound?.Invoke(owner, EventArgs.Empty);
            }
        }

        /// <inheritdoc />
        public void ReportCheckFailed(Exception? error)
        {
            owner._logger.LogWarning(error, "Update check failed");

            // A release without a package for this platform is shown as an available version without a package, not as
            // a failure; and a failed check says nothing against a package that was downloaded before.
            if (error is not null && !IsNoPackage(error) && owner._controller!.State != UpdateState.Downloaded)
            {
                owner.ShowFailure(error);
            }
        }

        /// <inheritdoc />
        public ValueTask<bool> ConfirmInstallAsync(AvailableUpdate update, SemanticVersion currentVersion) =>
            // Yes only for an installation the user started with InstallAsync; any other ask is refused.
            ValueTask.FromResult(owner._installing);

        /// <inheritdoc />
        public void ReportDownloading(AvailableUpdate update)
        {
            // No event is raised here: the controller calls this before it reports the change to the Downloading state,
            // so that the first thing the interface sees of a download is its zero. Keep that order in mind on upgrades.
            owner.DownloadPercent = 0;
        }

        /// <inheritdoc />
        public void ReportProgress(AvailableUpdate update, DownloadProgress progress)
        {
            // The library reports every received chunk; the display only changes with the whole percent.
            var whole = progress.Percentage is { } percent ? (int?)percent : null;
            if (whole == (int?)owner.DownloadPercent)
            {
                return;
            }

            owner.DownloadPercent = progress.Percentage;
            owner.Changed?.Invoke(owner, EventArgs.Empty);
        }

        /// <inheritdoc />
        public void ReportInstallFailed(AvailableUpdate update, Exception error)
        {
            owner._logger.LogWarning(error, "Update download or installation failed");
            if (!IsNoPackage(error))
            {
                owner.ShowFailure(error);
            }
        }

        /// <inheritdoc />
        public void InstallerStarted(AvailableUpdate update) =>
            owner._logger.LogInformation("Started the installer for {Version}", update.Version);

        /// <inheritdoc />
        public void InstallerEnded(AvailableUpdate update) =>
            owner._logger.LogWarning("The installer ended without replacing the running program");
    }
}
