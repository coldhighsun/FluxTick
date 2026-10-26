using System.ComponentModel;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluxTick.App.Services;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;
using GitHubReleaseUpdater.Assets;
using GitHubReleaseUpdater.Exceptions;
using GitHubReleaseUpdater.Installation;
using GitHubReleaseUpdater.Versioning;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests of the update check against a GitHub that is replaced by canned answers.
/// </summary>
public sealed class UpdateServiceTests : IDisposable
{
    /// <summary>
    /// The package contents served for the release.
    /// </summary>
    private static readonly byte[] Package = Encoding.UTF8.GetBytes("pretend this is a zip");

    /// <summary>
    /// A directory the downloads of one test go to.
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fluxtick-update-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// The directory the packages go to, apart from the state file like in the app.
    /// </summary>
    private readonly string _downloads;

    /// <summary>
    /// The canned GitHub.
    /// </summary>
    private readonly FakeGitHub _github = new();

    /// <summary>
    /// The stand-in for the setup program that records what was started.
    /// </summary>
    private readonly FakeLauncher _launcher = new();

    /// <summary>
    /// The stand-in for waiting on the setup program; by default it ended at once.
    /// </summary>
    private Func<int, Task> _watch = _ => Task.CompletedTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateServiceTests"/> class.
    /// </summary>
    public UpdateServiceTests() => _downloads = Path.Combine(_directory, "updates");

    /// <inheritdoc />
    public void Dispose()
    {
        _github.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// A release with a higher version is reported as available, with a package for this platform.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task A_newer_release_is_available()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);

        var service = Create("1.0.0");
        await service.CheckAsync(manual: false);

        service.Status.Should().Be(UpdateStatus.Available);
        service.LatestVersion.Should().Be(SemanticVersion.Parse("9.9.9"));
        service.HasPackage.Should().BeTrue();
        service.CanDownload.Should().BeTrue();
    }

    /// <summary>
    /// A release with the running version is not an update.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task The_same_version_is_up_to_date()
    {
        _github.Latest = FakeGitHub.Release("v1.0.0", withPackage: true);

        var service = Create("1.0.0");
        await service.CheckAsync(manual: false);

        service.Status.Should().Be(UpdateStatus.UpToDate);
        service.CanDownload.Should().BeFalse();
    }

    /// <summary>
    /// A newer release without a package for this platform is still announced, but cannot be downloaded.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task A_release_without_a_package_cannot_be_downloaded()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: false);

        var service = Create("1.0.0");
        await service.CheckAsync(manual: false);

        service.Status.Should().Be(UpdateStatus.Available);
        service.HasPackage.Should().BeFalse();
        service.CanDownload.Should().BeFalse();
    }

    /// <summary>
    /// A failing automatic check stays quiet, a failing manual one is reported.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task A_failed_check_is_only_reported_when_the_user_asked()
    {
        _github.Latest = null;

        var service = Create("1.0.0");
        await service.CheckAsync(manual: false);
        service.Status.Should().Be(UpdateStatus.Idle);

        await service.CheckAsync(manual: true);
        service.Status.Should().Be(UpdateStatus.Failed);
    }

    /// <summary>
    /// An automatic check right after another one does not ask GitHub again, a manual one does.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task The_automatic_check_is_throttled_and_the_manual_one_is_not()
    {
        _github.Latest = FakeGitHub.Release("v1.0.0", withPackage: true);

        var service = Create("1.0.0");
        await service.CheckAsync(manual: false);
        await service.CheckAsync(manual: false);
        _github.ApiRequests.Should().Be(1);

        await service.CheckAsync(manual: true);
        _github.ApiRequests.Should().Be(2);
    }

    /// <summary>
    /// Without a known version nothing is checked.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task An_unknown_running_version_turns_the_check_off()
    {
        var service = Create(null);
        await service.CheckAsync(manual: true);

        service.Status.Should().Be(UpdateStatus.Idle);
        _github.ApiRequests.Should().Be(0);
    }

    /// <summary>
    /// The package is downloaded and its checksum verified against the release's checksum file.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task The_package_is_downloaded_and_verified()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);
        var service = Create("1.0.0");
        await service.CheckAsync(manual: false);

        await service.DownloadAsync();

        service.Status.Should().Be(UpdateStatus.Downloaded);
        service.DownloadedFile.Should().NotBeNull();
        (await File.ReadAllBytesAsync(service.DownloadedFile!)).Should().Equal(Package);
    }

    /// <summary>
    /// Checking again for the version that was just downloaded keeps the download.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task Checking_again_keeps_the_downloaded_package()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);
        var service = Create("1.0.0");
        await service.CheckAsync(manual: false);
        await service.DownloadAsync();

        await service.CheckAsync(manual: true);

        service.Status.Should().Be(UpdateStatus.Downloaded);
        service.DownloadedFile.Should().NotBeNull();
    }

    /// <summary>
    /// A pending update is announced again after a restart, even within the check interval.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task A_pending_update_is_not_throttled_after_a_restart()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);
        await Create("1.0.0").CheckAsync(manual: false);

        var restarted = Create("1.0.0");
        await restarted.CheckAsync(manual: false);

        restarted.Status.Should().Be(UpdateStatus.Available);
        _github.ApiRequests.Should().Be(2);
    }

    /// <summary>
    /// The interval between automatic checks is remembered across a restart.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task The_check_interval_survives_a_restart()
    {
        _github.Latest = FakeGitHub.Release("v1.0.0", withPackage: true);
        await Create("1.0.0").CheckAsync(manual: false);

        await Create("1.0.0").CheckAsync(manual: false);

        _github.ApiRequests.Should().Be(1);
    }

    /// <summary>
    /// A manual check that fails does not take the downloaded package away.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task A_failed_check_keeps_the_downloaded_package()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);
        var service = Create("1.0.0");
        await service.CheckAsync(manual: false);
        await service.DownloadAsync();

        _github.Latest = null;
        await service.CheckAsync(manual: true);

        service.Status.Should().Be(UpdateStatus.Downloaded);
        service.DownloadedFile.Should().NotBeNull();
    }

    /// <summary>
    /// Downloading removes what earlier downloads left behind.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task Downloading_removes_older_update_files()
    {
        Directory.CreateDirectory(_downloads);
        var old = Path.Combine(_downloads, "FluxTick-v0.9.0-old.zip");
        await File.WriteAllTextAsync(old, "old");
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);
        var service = Create("1.0.0");
        await service.CheckAsync(manual: false);

        await service.DownloadAsync();

        File.Exists(old).Should().BeFalse();
        File.Exists(service.DownloadedFile).Should().BeTrue();
    }

    /// <summary>
    /// Only official builds with a known version look for updates by themselves, whatever the version is called.
    /// </summary>
    /// <param name="version">
    /// The running version, or <see langword="null"/> when it is unknown.
    /// </param>
    /// <param name="official">
    /// Whether the release workflow built the program.
    /// </param>
    /// <param name="expected">
    /// Whether the automatic check is allowed.
    /// </param>
    [Theory]
    [InlineData("1.2.3", true, true)]
    [InlineData("0.2.0-alpha.3", true, true)]
    [InlineData("0.2.0-beta.2.5", false, false)]
    [InlineData("0.1.0-alpha.1.31", false, false)]
    [InlineData(null, true, false)]
    public void Only_official_builds_check_by_themselves(string? version, bool official, bool expected) =>
        Create(version, official).CanAutoCheck.Should().Be(expected);

    /// <summary>
    /// Versions are shown without the build metadata that MinVer appends, such as the commit hash.
    /// </summary>
    /// <param name="text">
    /// The version as MinVer writes it.
    /// </param>
    /// <param name="expected">
    /// The text shown.
    /// </param>
    [Theory]
    [InlineData("1.2.3+abcdef0123456789", "1.2.3")]
    [InlineData("0.1.0-alpha.1.31+55d1e591d8c7885dc47e5e10d576cf8bff8c577f", "0.1.0-alpha.1.31")]
    [InlineData("2.0.0", "2.0.0")]
    public void Versions_are_shown_without_build_metadata(string text, string expected) =>
        UpdateService.FormatVersion(SemanticVersion.Parse(text)).Should().Be(expected);

    /// <summary>
    /// An earlier failure is not shown again by an automatic check that stays quiet.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task An_earlier_failure_is_cleared_by_the_next_check()
    {
        _github.Latest = null;
        var service = Create("1.0.0");
        await service.CheckAsync(manual: true);
        service.Status.Should().Be(UpdateStatus.Failed);

        await service.CheckAsync(manual: false);

        service.Status.Should().Be(UpdateStatus.Idle);
    }

    /// <summary>
    /// A package that does not match its checksum is thrown away and reported as a failure.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task A_package_with_a_wrong_checksum_is_refused()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);
        _github.CorruptPackage = true;
        var service = Create("1.0.0");
        await service.CheckAsync(manual: false);

        await service.DownloadAsync();

        service.Status.Should().Be(UpdateStatus.Failed);
        service.DownloadedFile.Should().BeNull();
    }

    /// <summary>
    /// The version of an assembly is read from the informational version that MinVer writes.
    /// </summary>
    /// <param name="text">
    /// The informational version.
    /// </param>
    /// <param name="expected">
    /// The version it stands for.
    /// </param>
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3+abcdef0", "1.2.3")]
    [InlineData("0.1.0-alpha.0.5+abcdef0", "0.1.0-alpha.0.5")]
    public void Versions_with_build_metadata_are_understood(string text, string expected)
    {
        SemanticVersion.TryParse(text, out var version).Should().BeTrue();
        version!.CompareTo(SemanticVersion.Parse(expected)).Should().Be(0);
    }

    /// <summary>
    /// Only the latest stable release is asked for, also by someone running a pre-release; the list of releases, which
    /// holds pre-releases, is not asked for.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task CheckAsync_RunningPrerelease_AsksOnlyForLatestStableRelease()
    {
        _github.Latest = FakeGitHub.Release("v1.0.0", withPackage: true);

        var service = Create("0.1.0-alpha.1");
        await service.CheckAsync(manual: false);

        service.Status.Should().Be(UpdateStatus.Available);
        _github.ApiRequests.Should().Be(1);
    }

    /// <summary>
    /// With a setup program as the package, the setup program of the release is the one that is found and downloaded.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task DownloadAsync_SetupProgram_IsDownloadedAndVerified()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true, setup: true);
        var service = Create("1.0.0", useSetup: true);
        await service.CheckAsync(manual: false);

        await service.DownloadAsync();

        service.Status.Should().Be(UpdateStatus.Downloaded);
        Path.GetFileName(service.DownloadedFile).Should().EndWith("-setup.exe");
        service.CanInstall.Should().BeTrue();
    }

    /// <summary>
    /// A release that only has archives has no package when a setup program is wanted.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task CheckAsync_ReleaseWithoutSetupProgram_HasNoPackage()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);

        var service = Create("1.0.0", useSetup: true);
        await service.CheckAsync(manual: false);

        service.Status.Should().Be(UpdateStatus.Available);
        service.HasPackage.Should().BeFalse();
    }

    /// <summary>
    /// Installing starts the verified setup program silently and asks it to start the app again. The app is not closed
    /// by itself: the setup program does that once it really replaces it.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task InstallAsync_DownloadedSetup_StartsItSilentlyWithRelaunch()
    {
        var service = await CreateDownloadedAsync();
        var running = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        _watch = _ =>
        {
            started.SetResult();
            return running.Task;
        };

        var installing = service.InstallAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        _launcher.Started.Should().ContainSingle();
        _launcher.Started[0].Path.Should().Be(service.DownloadedFile);
        _launcher.Started[0].Options.Arguments.Should().Be("/SILENT /SP- /NORESTART /CLOSEAPPLICATIONS /RELAUNCH=1");
        _launcher.Started[0].Options.RequireVerified.Should().BeTrue();
        service.Status.Should().Be(UpdateStatus.Installing);
        service.CanInstall.Should().BeFalse();

        running.SetResult();
        await installing;
    }

    /// <summary>
    /// Nothing is started when no package was downloaded.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task InstallAsync_NothingDownloaded_StartsNothing()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true, setup: true);
        var service = Create("1.0.0", useSetup: true);
        await service.CheckAsync(manual: false);

        await service.InstallAsync();

        _launcher.Started.Should().BeEmpty();
        service.Status.Should().Be(UpdateStatus.Available);
    }

    /// <summary>
    /// Refusing the elevation prompt is not a failure: the package stays ready and the app stays open.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task InstallAsync_ElevationRefused_KeepsPackageReady()
    {
        var service = await CreateDownloadedAsync();
        _launcher.Failure = new InstallerLaunchException("refused", "setup.exe", true, new InvalidOperationException());

        await service.InstallAsync();

        service.Status.Should().Be(UpdateStatus.Downloaded);
        service.CanInstall.Should().BeTrue();
    }

    /// <summary>
    /// A setup program that cannot be started is reported as a failure, and trying again needs no new download.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task InstallAsync_StartFails_FailsAndCanBeRetried()
    {
        var service = await CreateDownloadedAsync();
        _launcher.Failure = new InstallerLaunchException("broken", "setup.exe", false, new InvalidOperationException());

        await service.InstallAsync();

        service.Status.Should().Be(UpdateStatus.Failed);
        service.CanInstall.Should().BeTrue();

        _launcher.Failure = null;
        await service.InstallAsync();

        _launcher.Started.Should().ContainSingle();
    }

    /// <summary>
    /// A setup program that ends while the app still runs, for example because the user cancelled it, leaves the app
    /// running and offers the package again.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task InstallAsync_SetupEndsEarly_OffersPackageAgain()
    {
        var service = await CreateDownloadedAsync();
        var watched = 0;
        _watch = id =>
        {
            watched = id;
            return Task.CompletedTask;
        };

        await service.InstallAsync();

        watched.Should().Be(4242);
        service.Status.Should().Be(UpdateStatus.Downloaded);
        service.CanInstall.Should().BeTrue();
    }

    /// <summary>
    /// A setup program that cannot be watched, or that has no process to watch, does not leave the app stuck: the package
    /// is offered again after a while.
    /// </summary>
    /// <param name="processId">
    /// The process the launcher reports, or <see langword="null"/> for none.
    /// </param>
    /// <param name="cannotWatch">
    /// Whether watching the process fails.
    /// </param>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Theory]
    [InlineData(null, false)]
    [InlineData(4242, true)]
    [InlineData(4242, false)]
    public async Task InstallAsync_ProcessNotWatchable_OffersPackageAgainAfterWaiting(int? processId, bool cannotWatch)
    {
        var service = await CreateDownloadedAsync();
        _launcher.ProcessId = processId;
        var watched = false;
        _watch = _ =>
        {
            watched = true;
            return cannotWatch ? Task.FromException(new Win32Exception("access denied")) : Task.CompletedTask;
        };

        await service.InstallAsync();

        watched.Should().Be(processId is not null);
        service.Status.Should().Be(UpdateStatus.Downloaded);
        service.CanInstall.Should().BeTrue();
    }

    /// <summary>
    /// A setup program that fails for a reason that is not about the file, for example a file a virus scanner holds
    /// for a moment or an unexpected error, is reported as a failure and the download is kept for another try.
    /// </summary>
    /// <param name="failure">
    /// The exception the start fails with.
    /// </param>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Theory]
    [MemberData(nameof(UnexpectedStartFailures))]
    public async Task InstallAsync_UnexpectedStartFailure_FailsAndKeepsTheDownload(Exception failure)
    {
        var service = await CreateDownloadedAsync();
        _launcher.Failure = failure;

        await service.InstallAsync();

        service.Status.Should().Be(UpdateStatus.Failed);
        service.CanInstall.Should().BeTrue();
        service.DownloadedFile.Should().NotBeNull();
    }

    /// <summary>
    /// Gets the exceptions a start can fail with that are not about the verified file.
    /// </summary>
    public static TheoryData<Exception> UnexpectedStartFailures => new()
    {
        new IOException("in use"),
        new InvalidOperationException("unexpected"),
    };

    /// <summary>
    /// What is still waiting to be saved is saved before the setup program starts, and a failure of that does not stop
    /// the install.
    /// </summary>
    /// <param name="prepareFails">
    /// Whether getting ready fails.
    /// </param>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstallAsync_PrepareHook_RunsBeforeTheSetupProgramStarts(bool prepareFails)
    {
        var service = await CreateDownloadedAsync();
        var startedBefore = -1;
        service.PrepareForInstallAsync = () =>
        {
            startedBefore = _launcher.Started.Count;
            return prepareFails ? Task.FromException(new IOException("disk full")) : Task.CompletedTask;
        };

        await service.InstallAsync();

        startedBefore.Should().Be(0);
        _launcher.Started.Should().ContainSingle();
    }

    /// <summary>
    /// Getting ready that never ends does not keep the setup program from starting.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task InstallAsync_PrepareHookNeverEnds_StartsTheSetupProgramAfterTheTimeout()
    {
        var service = await CreateDownloadedAsync();
        service.PrepareForInstallAsync = () => new TaskCompletionSource().Task;

        await service.InstallAsync().WaitAsync(TimeSpan.FromSeconds(10));

        _launcher.Started.Should().ContainSingle();
    }

    /// <summary>
    /// Any failure to watch the setup program is treated like a setup program that cannot be watched: the package is
    /// offered again instead of the status staying on Installing.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task InstallAsync_WatchFailsUnexpectedly_OffersPackageAgain()
    {
        var service = await CreateDownloadedAsync();
        _watch = _ => Task.FromException(new UnauthorizedAccessException());

        await service.InstallAsync();

        service.Status.Should().Be(UpdateStatus.Downloaded);
    }

    /// <summary>
    /// Once the app is up to date, what earlier downloads left in the updates folder is deleted.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task CheckAsync_UpToDate_DeletesLeftoverUpdateFiles()
    {
        Directory.CreateDirectory(_downloads);
        var leftover = Path.Combine(_downloads, "FluxTick-v1.0.0-win-x64-setup.exe");
        await File.WriteAllTextAsync(leftover, "installed already");
        _github.Latest = FakeGitHub.Release("v1.0.0", withPackage: true, setup: true);

        await Create("1.0.0", useSetup: true).CheckAsync(manual: false);

        File.Exists(leftover).Should().BeFalse();
    }

    /// <summary>
    /// A leftover file that cannot be deleted for now does not turn an up-to-date check into a failure; it is deleted by
    /// a later call, and nothing is deleted while a newer version is known.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task DeleteLeftoversAsync_FileInUse_KeepsUpToDateAndRetriesLater()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to delete a file that is open.");
        Directory.CreateDirectory(_downloads);
        var leftover = Path.Combine(_downloads, "FluxTick-v1.0.0-win-x64-setup.exe");
        await File.WriteAllTextAsync(leftover, "still running");
        _github.Latest = FakeGitHub.Release("v1.0.0", withPackage: true, setup: true);
        var service = Create("1.0.0", useSetup: true);
        var hold = new FileStream(leftover, FileMode.Open, FileAccess.Read, FileShare.None);

        try
        {
            await service.CheckAsync(manual: true);

            service.Status.Should().Be(UpdateStatus.UpToDate);
            File.Exists(leftover).Should().BeTrue();
        }
        finally
        {
            hold.Dispose();
        }

        await service.DeleteLeftoversAsync();

        File.Exists(leftover).Should().BeFalse();
    }

    /// <summary>
    /// Nothing is deleted from the updates folder while a newer version is known, since it may be the one to install.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task DeleteLeftoversAsync_NewerVersionKnown_DeletesNothing()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true, setup: true);
        var service = Create("1.0.0", useSetup: true);
        await service.CheckAsync(manual: false);
        await service.DownloadAsync();

        await service.DeleteLeftoversAsync();

        File.Exists(service.DownloadedFile).Should().BeTrue();
    }

    /// <summary>
    /// A downloaded setup program that no longer matches its checksum is not tried again: it is downloaded again.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task InstallAsync_FileChangedAfterDownload_DownloadsAgain()
    {
        var service = await CreateDownloadedAsync();
        await File.AppendAllTextAsync(service.DownloadedFile!, "tampered");

        await service.InstallAsync();

        service.Status.Should().Be(UpdateStatus.Failed);
        service.CanInstall.Should().BeFalse();
        service.CanDownload.Should().BeTrue();
        _launcher.Started.Should().BeEmpty();

        await service.DownloadAsync();

        service.CanInstall.Should().BeTrue();
    }

    /// <summary>
    /// The buttons and texts of the settings page follow the state of the update.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task UpdateNowCommand_UpdateFound_ShowsUpdateNowAndInstalls()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true, setup: true, notes: "Fixed things");
        var service = Create("1.0.0", useSetup: true);
        var page = CreateViewModel(service);
        page.ShowCheck.Should().BeTrue();
        page.CanUpdateNow.Should().BeFalse();
        page.HasReleaseNotes.Should().BeFalse();

        await service.CheckAsync(manual: false);

        page.ShowCheck.Should().BeFalse();
        page.CanUpdateNow.Should().BeTrue();
        page.CanDownload.Should().BeFalse();
        page.ReleaseNotes.Should().Be("Fixed things");

        await page.UpdateNowCommand.ExecuteAsync(null);

        _launcher.Started.Should().ContainSingle();
    }

    /// <summary>
    /// The page of the newer release is known while an update is, and not otherwise.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task ReleaseUrl_UpdateKnown_IsThePageOfTheRelease()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true, setup: true);
        var service = Create("1.0.0", useSetup: true);
        var page = CreateViewModel(service);
        service.ReleaseUrl.Should().BeNull();
        page.HasReleasePage.Should().BeFalse();

        await service.CheckAsync(manual: false);

        service.ReleaseUrl.Should().Be("https://github.com/coldhighsun/FluxTick/releases/tag/v9.9.9");
        page.HasReleasePage.Should().BeTrue();
    }

    /// <summary>
    /// The link to the release page is offered for a GitHub address, also when the release has no notes, and not for any
    /// other address.
    /// </summary>
    /// <param name="htmlUrl">
    /// The address the release data names.
    /// </param>
    /// <param name="expected">
    /// Whether the page link is offered.
    /// </param>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Theory]
    [InlineData("https://github.com/coldhighsun/FluxTick/releases/tag/v9.9.9", true)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    public async Task HasReleasePage_ReleaseWithoutNotes_FollowsTheAddress(string htmlUrl, bool expected)
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true, setup: true, notes: string.Empty)
            .Replace("https://github.com/coldhighsun/FluxTick/releases/tag/v9.9.9", htmlUrl, StringComparison.Ordinal);
        var service = Create("1.0.0", useSetup: true);
        var page = CreateViewModel(service);

        await service.CheckAsync(manual: false);

        page.HasReleaseNotes.Should().BeFalse();
        page.HasReleasePage.Should().Be(expected);
    }

    /// <summary>
    /// A release without a package for this platform keeps the check button, since there is nothing to update to.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task ShowCheck_ReleaseWithoutPackage_StaysTrue()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: false);
        var service = Create("1.0.0", useSetup: true);
        var page = CreateViewModel(service);

        await service.CheckAsync(manual: false);

        page.ShowCheck.Should().BeTrue();
        page.CanUpdateNow.Should().BeFalse();
    }

    /// <summary>
    /// An archive is downloaded and unpacked by hand: the page offers Download, not Update now.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task CanDownload_ArchivePackage_OffersDownloadNotUpdateNow()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);
        var service = Create("1.0.0");
        var page = CreateViewModel(service);

        await service.CheckAsync(manual: false);

        page.CanUpdateNow.Should().BeFalse();
        page.CanDownload.Should().BeTrue();
    }

    /// <summary>
    /// An automatic check announces a newer version once, however often it finds it.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task CheckAsync_NewerVersion_IsAnnouncedOnce()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true, setup: true);
        var service = Create("1.0.0", useSetup: true);
        var announced = 0;
        service.UpdateFound += (_, _) => announced++;

        await service.CheckAsync(manual: false);
        await service.CheckAsync(manual: false);

        announced.Should().Be(1);
    }

    /// <summary>
    /// A check the user asked for is not announced, and neither is a package that cannot be installed.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task CheckAsync_ManualOrArchive_IsNotAnnounced()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true, setup: true);
        var manual = Create("1.0.0", useSetup: true);
        var archive = Create("1.0.0");
        var announced = 0;
        manual.UpdateFound += (_, _) => announced++;
        archive.UpdateFound += (_, _) => announced++;

        await manual.CheckAsync(manual: true);
        await archive.CheckAsync(manual: false);

        announced.Should().Be(0);
    }

    /// <summary>
    /// The release notes of the newer version are available, and gone once the app is up to date.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task ReleaseNotes_UpdateKnown_AreKeptUntilUpToDate()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true, notes: "Fixed things");
        var service = Create("1.0.0");
        service.ReleaseNotes.Should().BeNull();

        await service.CheckAsync(manual: false);
        service.ReleaseNotes.Should().Be("Fixed things");

        _github.Latest = FakeGitHub.Release("v1.0.0", withPackage: true, notes: "Old");
        await service.CheckAsync(manual: true);
        service.ReleaseNotes.Should().BeNull();
    }

    /// <summary>
    /// Creates the view model of the settings page for a service.
    /// </summary>
    /// <param name="service">
    /// The service.
    /// </param>
    /// <returns>
    /// The view model.
    /// </returns>
    private static UpdateViewModel CreateViewModel(UpdateService service)
    {
        var settings = new SettingsManager(new InMemorySettingsStore { Stored = new AppSettings() }, NullLogger<SettingsManager>.Instance);
        settings.LoadAsync(new AppSettings()).GetAwaiter().GetResult();
        return new UpdateViewModel(service, settings, new InlineDispatcher());
    }

    /// <summary>
    /// Creates a service that has found and downloaded a setup program.
    /// </summary>
    /// <returns>
    /// The service.
    /// </returns>
    private async Task<UpdateService> CreateDownloadedAsync()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true, setup: true);
        var service = Create("1.0.0", useSetup: true);
        await service.CheckAsync(manual: false);
        await service.DownloadAsync();
        return service;
    }

    /// <summary>
    /// Creates a service that talks to the canned GitHub.
    /// </summary>
    /// <param name="current">
    /// The running version, or <see langword="null"/> when it is unknown.
    /// </param>
    /// <param name="officialBuild">
    /// Whether the release workflow built the program.
    /// </param>
    /// <param name="useSetup">
    /// Whether the package is a setup program instead of an archive.
    /// </param>
    /// <returns>
    /// The service.
    /// </returns>
    private UpdateService Create(string? current, bool officialBuild = true, bool useSetup = false) =>
        new(
            Path.Combine(_directory, "update-state.json"),
            NullLogger<UpdateService>.Instance,
            current is null ? null : SemanticVersion.Parse(current),
            officialBuild,
            _downloads,
            useSetup,
            new HttpClient(_github, disposeHandler: false),
            _launcher)
        {
            InstallerGracePeriod = TimeSpan.Zero,
            PrepareTimeout = TimeSpan.FromMilliseconds(200),
            WaitForProcessExitAsync = id => _watch(id),
        };

    /// <summary>
    /// A stand-in for starting the setup program that records what would have been started.
    /// </summary>
    private sealed class FakeLauncher : IInstallerLauncher
    {
        /// <summary>
        /// Gets the setup programs that were started.
        /// </summary>
        public List<(string Path, InstallerLaunchOptions Options)> Started { get; } = [];

        /// <summary>
        /// Gets or sets the failure to throw instead of starting, or <see langword="null"/>.
        /// </summary>
        public Exception? Failure { get; set; }

        /// <summary>
        /// Gets or sets the process id reported for a started setup program, or <see langword="null"/> for none.
        /// </summary>
        public int? ProcessId { get; set; } = 4242;

        /// <inheritdoc />
        public InstallerLaunchResult Launch(string installerPath, InstallerLaunchOptions options)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Started.Add((installerPath, options));
            return new InstallerLaunchResult(installerPath, ProcessId);
        }
    }

    /// <summary>
    /// A stand-in for the GitHub API and the download host.
    /// </summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        /// <summary>
        /// Gets or sets the JSON of the latest release, or <see langword="null"/> to make the API fail.
        /// </summary>
        public string? Latest { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the package differs from what the checksum file says.
        /// </summary>
        public bool CorruptPackage { get; set; }

        /// <summary>
        /// Gets how many times the release API was asked.
        /// </summary>
        public int ApiRequests { get; private set; }

        /// <summary>
        /// Builds the JSON of a release.
        /// </summary>
        /// <param name="tag">
        /// The tag of the release.
        /// </param>
        /// <param name="withPackage">
        /// Whether the release carries the package of this platform and the checksum file.
        /// </param>
        /// <param name="setup">
        /// Whether the package is the setup program instead of the archive.
        /// </param>
        /// <param name="notes">
        /// The release notes.
        /// </param>
        /// <returns>
        /// The JSON.
        /// </returns>
        public static string Release(string tag, bool withPackage, bool setup = false, string notes = "")
        {
            var name = setup
                ? $"FluxTick-{tag}-{RuntimeInfo.Current.Rid}-setup.exe"
                : $"FluxTick-{tag}-{RuntimeInfo.Current.Rid}.zip";
            var assets = withPackage
                ? $$"""
                    [
                      { "id": 1, "name": "{{name}}", "size": {{Package.Length}}, "browser_download_url": "https://example.test/dl/{{name}}", "url": "https://example.test/dl/{{name}}" },
                      { "id": 2, "name": "SHA256SUMS.txt", "size": 100, "browser_download_url": "https://example.test/dl/SHA256SUMS.txt", "url": "https://example.test/dl/SHA256SUMS.txt" }
                    ]
                    """
                : "[]";
            return $$"""
                { "id": 10, "tag_name": "{{tag}}", "name": "{{tag}}", "draft": false, "prerelease": false, "html_url": "https://github.com/coldhighsun/FluxTick/releases/tag/{{tag}}", "body": "{{notes}}", "assets": {{assets}} }
                """;
        }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/releases/latest", StringComparison.Ordinal))
            {
                ApiRequests++;
                return Task.FromResult(Latest is null
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : Json(Latest));
            }

            if (path.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal))
            {
                var rid = RuntimeInfo.Current.Rid;
                var hash = Convert.ToHexStringLower(SHA256.HashData(Package));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{hash}  FluxTick-v9.9.9-{rid}.zip\n{hash}  FluxTick-v9.9.9-{rid}-setup.exe\n{hash}  FluxTick-v0.1.1-alpha.1-{rid}.zip\n"),
                });
            }

            if (path.EndsWith(".zip", StringComparison.Ordinal) || path.EndsWith(".exe", StringComparison.Ordinal))
            {
                var bytes = CorruptPackage ? [.. Package, (byte)0] : Package;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        /// <summary>
        /// Wraps JSON in a response.
        /// </summary>
        /// <param name="json">
        /// The JSON.
        /// </param>
        /// <returns>
        /// The response.
        /// </returns>
        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
