using System.Reflection;
using System.Text.Json;
using GitHubReleaseUpdater;
using GitHubReleaseUpdater.Assets;
using GitHubReleaseUpdater.Download;
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
    /// The last check or download failed.
    /// </summary>
    Failed,
}

/// <summary>
/// Looks for a newer release of FluxTick on GitHub and downloads its package. It does not replace the running
/// program: the package is verified and left in the updates directory for the user to unpack. Members must be
/// used from the UI thread.
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
    /// The name of a release package; <c>{rid}</c> is the runtime of the running program, such as <c>win-x64</c>.
    /// </summary>
    internal const string AssetPattern = "FluxTick-{tag}-{rid}.zip";

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
    /// The result of the last check that found a newer release.
    /// </summary>
    private UpdateCheckResult? _check;

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
    /// <param name="http">
    /// The client used for GitHub, or <see langword="null"/> for the shared default one.
    /// </param>
    public UpdateService(
        string stateFile,
        ILogger<UpdateService> logger,
        SemanticVersion? currentVersion,
        bool officialBuild,
        string downloadDirectory,
        HttpClient? http = null)
    {
        _store = new FileLastCheckStore(stateFile, logger);
        _logger = logger;
        CurrentVersion = currentVersion;
        _officialBuild = officialBuild;
        _downloadDirectory = downloadDirectory;
        _http = http;
    }

    /// <summary>
    /// Raised whenever <see cref="Status"/> or the download progress changes.
    /// </summary>
    public event EventHandler? Changed;

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
    /// Gets the download progress between 0 and 100, or <see langword="null"/> when it is not known.
    /// </summary>
    public double? DownloadPercent { get; private set; }

    /// <summary>
    /// Gets the full path of the downloaded package once <see cref="Status"/> is <see cref="UpdateStatus.Downloaded"/>.
    /// </summary>
    public string? DownloadedFile { get; private set; }

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
        if (CurrentVersion is null || Status is UpdateStatus.Checking or UpdateStatus.Downloading)
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
                    DownloadedFile = null;
                }

                _logger.LogInformation("Update {Latest} is available, running {Current}", result.LatestVersion, CurrentVersion);
                SetStatus(keepDownload ? UpdateStatus.Downloaded : UpdateStatus.Available);
                await _store.SetUpdateFoundAsync(true);
            }
            else
            {
                _check = null;
                DownloadedFile = null;
                SetStatus(UpdateStatus.UpToDate);
                await _store.SetUpdateFoundAsync(false);
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
        DownloadedFile = null;
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
            DownloadedFile = file.FilePath;
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
    /// Deletes what earlier downloads left in the updates directory, except the files of the package about to be
    /// downloaded, whose partial file lets an interrupted download go on. A file that cannot be deleted stays.
    /// </summary>
    /// <param name="keepName">
    /// The file name of the package about to be downloaded.
    /// </param>
    private void CleanDownloadDirectory(string keepName)
    {
        if (!Directory.Exists(_downloadDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_downloadDirectory))
        {
            if (Path.GetFileName(file).StartsWith(keepName, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not delete the old update file {Path}", file);
            }
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
        IncludePrerelease = false,
        AssetSelector = new PatternAssetSelector(AssetPattern),
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
