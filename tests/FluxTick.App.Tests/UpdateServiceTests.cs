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
    /// Initializes a new instance of the <see cref="UpdateServiceTests"/> class.
    /// </summary>
    public UpdateServiceTests() => _downloads = Path.Combine(_directory, "updates");

    /// <inheritdoc />
    public void Dispose()
    {
        _github.Dispose();
        DeleteDirectory();
    }

    /// <summary>
    /// Deletes the directory of the test. A service that was disposed may still be winding down its background loop, which
    /// can hold the state file for a moment on Windows, so a file that is in use is tried again until it is let go.
    /// </summary>
    private void DeleteDirectory()
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (Directory.Exists(_directory))
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(20));
            }
        }
    }


    /// <summary>
    /// A release with a higher version is reported as available, with a package for this platform.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task CheckAsync_NewerRelease_IsAvailable()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);

        var service = Create("1.0.0");
        await service.CheckAsync(manual: false);

        service.Status.Should().Be(UpdateStatus.Available);
        service.LatestVersion.Should().Be(SemanticVersion.Parse("9.9.9"));
        service.HasPackage.Should().BeTrue();
        service.CanDownload.Should().BeTrue();
        service.CurrentVersionText.Should().Be("1.0.0");
        service.LatestVersionText.Should().Be("9.9.9");
    }

    /// <summary>
    /// A release with the running version is not an update.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task CheckAsync_SameVersion_IsUpToDate()
    {
        _github.Latest = FakeGitHub.Release("v1.0.0", withPackage: true);

        var service = Create("1.0.0");
        await service.CheckAsync(manual: false);

        service.Status.Should().Be(UpdateStatus.UpToDate);
        service.CanDownload.Should().BeFalse();
        service.LatestVersion.Should().BeNull();
        service.LatestVersionText.Should().BeNull();
    }

    /// <summary>
    /// A newer release without a package for this platform is still known, but cannot be downloaded.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task CheckAsync_ReleaseWithoutPackage_CannotBeDownloaded()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: false);

        var service = Create("1.0.0");
        await service.CheckAsync(manual: false);

        service.Status.Should().Be(UpdateStatus.Available);
        service.HasPackage.Should().BeFalse();
        service.CanDownload.Should().BeFalse();
        service.ReleaseUrl.Should().NotBeNull();
    }

    /// <summary>
    /// A check that fails is shown as a failure only when the user asked for it.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task CheckAsync_Failure_IsReportedOnlyWhenManual()
    {
        _github.Latest = null;
        var service = Create("1.0.0");

        await service.CheckAsync(manual: false);
        service.Status.Should().Be(UpdateStatus.Idle);

        await service.CheckAsync(manual: true);
        service.Status.Should().Be(UpdateStatus.Failed);
    }

    /// <summary>
    /// A failure is forgotten by the next check.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task CheckAsync_AfterEarlierFailure_ClearsTheFailure()
    {
        _github.Latest = null;
        var service = Create("1.0.0");
        await service.CheckAsync(manual: true);
        service.Status.Should().Be(UpdateStatus.Failed);

        _github.Latest = FakeGitHub.Release("v1.0.0", withPackage: true);
        await service.CheckAsync(manual: true);

        service.Status.Should().Be(UpdateStatus.UpToDate);
    }

    /// <summary>
    /// A second automatic check right after the first asks GitHub nothing; a manual one does.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task CheckAsync_AutomaticAfterRecentCheck_IsThrottledButManualIsNot()
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
    /// Without a known running version nothing is asked of GitHub.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task CheckAsync_UnknownRunningVersion_TurnsTheCheckOff()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);
        var service = Create(null);

        await service.CheckAsync(manual: true);

        service.CanAutoCheck.Should().BeFalse();
        service.Status.Should().Be(UpdateStatus.Idle);
        _github.ApiRequests.Should().Be(0);
    }

    /// <summary>
    /// Only a build from the release workflow with a known version checks by itself.
    /// </summary>
    /// <param name="version">
    /// The running version.
    /// </param>
    /// <param name="official">
    /// Whether the release workflow built the program.
    /// </param>
    /// <param name="expected">
    /// Whether the automatic check is allowed.
    /// </param>
    [Theory]
    [InlineData("1.0.0", true, true)]
    [InlineData("1.0.0", false, false)]
    [InlineData(null, true, false)]
    public void CanAutoCheck_Build_OnlyOfficialBuildsMayCheck(string? version, bool official, bool expected) =>
        Create(version, official).CanAutoCheck.Should().Be(expected);

    /// <summary>
    /// The package of the newer release is downloaded and its checksum checked.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task DownloadAsync_Package_IsDownloadedAndVerified()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);
        var service = Create("1.0.0");
        await service.CheckAsync(manual: false);

        await service.DownloadAsync();

        service.Status.Should().Be(UpdateStatus.Downloaded);
        File.Exists(service.DownloadedFile).Should().BeTrue();
        service.CanInstall.Should().BeFalse("an archive is unpacked by the user");
    }

    /// <summary>
    /// A check that finds the same version again keeps what was downloaded.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task CheckAsync_AfterDownload_KeepsTheDownloadedPackage()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);
        var service = Create("1.0.0");
        await service.CheckAsync(manual: false);
        await service.DownloadAsync();

        await service.CheckAsync(manual: true);

        service.Status.Should().Be(UpdateStatus.Downloaded);
        File.Exists(service.DownloadedFile).Should().BeTrue();
    }

    /// <summary>
    /// A failed check says nothing against a package that was downloaded before.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task CheckAsync_FailureAfterDownload_KeepsTheDownloadedPackage()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);
        var service = Create("1.0.0");
        await service.CheckAsync(manual: false);
        await service.DownloadAsync();

        _github.Latest = null;
        await service.CheckAsync(manual: true);

        service.Status.Should().Be(UpdateStatus.Downloaded);
    }

    /// <summary>
    /// While a newer release is known, the next start asks again so that the announcement comes back.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task CheckAsync_PendingUpdateAfterRestart_IsNotThrottled()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);
        await Create("1.0.0").CheckAsync(manual: false);

        await Create("1.0.0").CheckAsync(manual: false);

        _github.ApiRequests.Should().Be(2);
    }

    /// <summary>
    /// Without a newer release, the time of the last check is remembered across starts.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task CheckAsync_AfterRestart_KeepsTheCheckInterval()
    {
        _github.Latest = FakeGitHub.Release("v1.0.0", withPackage: true);
        await Create("1.0.0").CheckAsync(manual: false);

        await Create("1.0.0").CheckAsync(manual: false);

        _github.ApiRequests.Should().Be(1);
    }

    /// <summary>
    /// A download that does not match its published checksum is refused.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task DownloadAsync_WrongChecksum_IsRefused()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);
        _github.CorruptPackage = true;
        var service = Create("1.0.0");
        await service.CheckAsync(manual: false);

        await service.DownloadAsync();

        service.Status.Should().Be(UpdateStatus.Failed);
        service.DownloadedFile.Should().BeNull();
        service.CanDownload.Should().BeTrue("the download can be tried again");
    }

    /// <summary>
    /// A pre-release build asks for the latest stable release only.
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
    /// A new download clears what earlier ones left.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task DownloadAsync_OlderUpdateFiles_AreRemoved()
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
    /// Once the app is up to date, the setup program of the update that was just installed is deleted.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task CheckAsync_UpToDate_DeletesLeftoverUpdateFiles()
    {
        Directory.CreateDirectory(_downloads);
        var old = Path.Combine(_downloads, "FluxTick-v9.9.9-setup.exe");
        await File.WriteAllTextAsync(old, "installed already");
        _github.Latest = FakeGitHub.Release("v1.0.0", withPackage: true);
        var service = Create("1.0.0");

        await service.CheckAsync(manual: false);

        File.Exists(old).Should().BeFalse();
    }

    /// <summary>
    /// On Windows the package is the setup program and can be started.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task DownloadAsync_SetupProgram_IsDownloadedAndVerified()
    {
        var service = await CreateDownloadedAsync();

        service.Status.Should().Be(UpdateStatus.Downloaded);
        service.CanInstall.Should().BeTrue();
        Path.GetFileName(service.DownloadedFile).Should().EndWith("-setup.exe");
    }

    /// <summary>
    /// The downloaded setup program is started silently, closes the app and starts it again.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task InstallAsync_DownloadedSetup_StartsItSilentlyWithRelaunch()
    {
        var service = await CreateDownloadedAsync();

        await service.InstallAsync();

        var started = _launcher.Started.Should().ContainSingle().Subject;
        started.Path.Should().Be(service.DownloadedFile);
        started.Options.Arguments.Should().Contain("/SILENT")
            .And.Contain(UpdateService.CloseApplicationsArgument)
            .And.Contain(UpdateService.RelaunchArgument);
        started.Options.RequireVerified.Should().BeTrue();
    }

    /// <summary>
    /// Installing before the download started does nothing.
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
    /// A check the user asked for shows the update and waits for them to install it.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task CheckAsync_Manual_DoesNotInstallWhatItFinds()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true, setup: true);
        var service = Create("1.0.0", useSetup: true);

        await service.CheckAsync(manual: true);

        service.Status.Should().Be(UpdateStatus.Available);
        _launcher.Started.Should().BeEmpty();
    }

    /// <summary>
    /// A refused elevation prompt is not a failure; the package stays ready.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task InstallAsync_ElevationRefused_KeepsPackageReady()
    {
        var service = await CreateDownloadedAsync();
        _launcher.Failure = new InstallerLaunchException("refused", "x", isUserCancelled: true);

        await service.InstallAsync();

        service.Status.Should().Be(UpdateStatus.Downloaded);
        service.CanInstall.Should().BeTrue();
    }

    /// <summary>
    /// A setup program that cannot be started is a failure; the download is kept for another try.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task InstallAsync_StartFails_FailsAndCanBeRetried()
    {
        var service = await CreateDownloadedAsync();
        _launcher.Failure = new IOException("held by a scanner");

        await service.InstallAsync();

        service.Status.Should().Be(UpdateStatus.Failed);
        service.CanInstall.Should().BeTrue();

        _launcher.Failure = null;
        await service.InstallAsync();

        _launcher.Started.Should().ContainSingle();
    }

    /// <summary>
    /// A setup program that ends while the app still runs did not replace it, so the package is offered again.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task InstallAsync_SetupEndsEarly_OffersPackageAgain()
    {
        var service = await CreateDownloadedAsync();

        await service.InstallAsync();

        service.Status.Should().Be(UpdateStatus.Downloaded);
        service.CanInstall.Should().BeTrue();
    }

    /// <summary>
    /// A file that changed after the download is not started; it is downloaded again.
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
    /// What the app saves before the setup program starts runs first, and a failure of it does not stop the install.
    /// </summary>
    /// <param name="prepareFails">
    /// Whether the preparation fails.
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
    /// The controller's question to install is answered yes only while an installation the user started is running.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task ConfirmInstallAsync_OnlyDuringInstallAsync_IsAnsweredYes()
    {
        var service = await CreateDownloadedAsync();
        var presenter = new UpdateService.Presenter(service);
        var version = SemanticVersion.Parse("1.0.0");
        var during = false;
        service.PrepareForInstallAsync = async () => during = await presenter.ConfirmInstallAsync(null!, version);

        var before = await presenter.ConfirmInstallAsync(null!, version);
        await service.InstallAsync();
        var after = await presenter.ConfirmInstallAsync(null!, version);

        before.Should().BeFalse();
        during.Should().BeTrue();
        after.Should().BeFalse();
    }

    /// <summary>
    /// Calling Start again does not begin a second round of checks.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task Start_CalledTwice_RunsOneLoop()
    {
        var loops = 0;
        using var service = Create(
            "1.0.0",
            isAutoCheckEnabled: () =>
            {
                loops++;
                return false;
            },
            firstCheckDelay: TimeSpan.FromMilliseconds(20));

        service.Start();
        service.Start();
        await WaitUntilAsync(() => loops > 0);
        await Task.Delay(200);

        loops.Should().Be(1);
    }

    /// <summary>
    /// A failure of the loop of automatic checks is survived: the loop starts again after a round.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task Start_LoopFails_StartsAgainAfterARound()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);
        var calls = 0;
        using var service = Create(
            "1.0.0",
            isAutoCheckEnabled: () => ++calls > 1 ? true : throw new InvalidOperationException("setting unavailable"),
            firstCheckDelay: TimeSpan.FromMilliseconds(20),
            checkRound: TimeSpan.FromMilliseconds(50));

        service.Start();

        await WaitUntilAsync(() => service.Status == UpdateStatus.Available);
    }

    /// <summary>
    /// A second installation is not started while one is running.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task InstallAsync_CalledTwiceAtOnce_StartsTheSetupProgramOnce()
    {
        var service = await CreateDownloadedAsync();

        await Task.WhenAll(service.InstallAsync(), service.InstallAsync());

        _launcher.Started.Should().ContainSingle();
    }

    /// <summary>
    /// A preparation that never ends delays the setup program only until its timeout.
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
    /// A newer version found by an automatic check is announced once.
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
    /// A check the user asked for, and an archive that cannot be installed, are not announced.
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
    /// The release notes of the newer version are kept until the app is up to date.
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
    /// A newer release without a package for this platform is shown as available, also when the user asked.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task CheckAsync_ManualReleaseWithoutPackage_IsNotAFailure()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: false);
        var service = Create("1.0.0", useSetup: true);

        await service.CheckAsync(manual: true);

        service.Status.Should().Be(UpdateStatus.Available);
        service.HasPackage.Should().BeFalse();
        service.LatestVersion.Should().Be(SemanticVersion.Parse("9.9.9"));
        service.ReleaseUrl.Should().NotBeNull();
    }

    /// <summary>
    /// The percentage shown when a download starts is zero, not what an earlier download reached.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task DownloadAsync_SecondDownload_StartsFromZero()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);
        _github.CorruptPackage = true;
        var service = Create("1.0.0");
        await service.CheckAsync(manual: false);
        var shown = new List<double?>();
        service.Changed += (_, _) =>
        {
            if (service.Status == UpdateStatus.Downloading)
            {
                shown.Add(service.DownloadPercent);
            }
        };
        await service.DownloadAsync();
        _github.CorruptPackage = false;
        shown.Clear();

        await service.DownloadAsync();

        shown.Should().NotBeEmpty();
        shown[0].Should().Be(0);
    }

    /// <summary>
    /// The automatic check runs once the delay has passed.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task Start_AfterTheDelay_ChecksByItself()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);
        using var service = Create("1.0.0", firstCheckDelay: TimeSpan.FromMilliseconds(20));

        service.Start();

        await WaitUntilAsync(() => service.Status == UpdateStatus.Available);
    }

    /// <summary>
    /// A build that is not from the release workflow does not look for updates by itself.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task Start_UnofficialBuild_DoesNothing()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);
        using var service = Create("1.0.0", officialBuild: false, firstCheckDelay: TimeSpan.FromMilliseconds(20));

        service.Start();
        await Task.Delay(300);

        _github.ApiRequests.Should().Be(0);
    }

    /// <summary>
    /// With the setting off the automatic check asks nothing.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task Start_AutoCheckSettingChanges_IsFollowed()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);
        using var service = Create("1.0.0", isAutoCheckEnabled: () => false, firstCheckDelay: TimeSpan.FromMilliseconds(20));

        service.Start();
        await Task.Delay(300);

        _github.ApiRequests.Should().Be(0);
    }

    /// <summary>
    /// A release that is already known is not asked for again by the automatic check.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task Start_NewerReleaseKnown_DoesNotAskAgain()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);
        using var service = Create("1.0.0", firstCheckDelay: TimeSpan.FromMilliseconds(20));
        await service.CheckAsync(manual: true);

        service.Start();
        await Task.Delay(300);

        _github.ApiRequests.Should().Be(1);
    }

    /// <summary>
    /// Starting a service that was disposed does nothing, and disposing twice is harmless.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task Start_AfterDispose_DoesNothing()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);
        var service = Create("1.0.0", firstCheckDelay: TimeSpan.FromMilliseconds(20));
        service.Dispose();

        service.Dispose();
        service.Start();
        await Task.Delay(200);

        _github.ApiRequests.Should().Be(0);
    }

    /// <summary>
    /// Disposing the service before the first check keeps it from running.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task Dispose_AutomaticCheckPending_StopsIt()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: true);
        var service = Create("1.0.0", firstCheckDelay: TimeSpan.FromMilliseconds(300));
        service.Start();

        service.Dispose();
        await Task.Delay(600);

        _github.ApiRequests.Should().Be(0);
    }

    /// <summary>
    /// The rounds of the automatic check also delete what earlier downloads left once the app is up to date, also with
    /// the automatic check switched off.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task Start_UpToDate_DeletesLeftoverUpdateFiles()
    {
        _github.Latest = FakeGitHub.Release("v1.0.0", withPackage: true);
        using var service = Create("1.0.0", isAutoCheckEnabled: () => false, firstCheckDelay: TimeSpan.FromMilliseconds(20));
        await service.CheckAsync(manual: true);
        Directory.CreateDirectory(_downloads);
        var old = Path.Combine(_downloads, "FluxTick-v0.9.0-setup.exe");
        await File.WriteAllTextAsync(old, "old");

        service.Start();

        await WaitUntilAsync(() => !File.Exists(old));
    }

    /// <summary>
    /// Downloading or installing a release without a package is not shown as a failure.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is done.
    /// </returns>
    [Fact]
    public async Task DownloadAndInstall_ReleaseWithoutPackage_AreNotShownAsFailures()
    {
        _github.Latest = FakeGitHub.Release("v9.9.9", withPackage: false);
        var service = Create("1.0.0", useSetup: true);
        await service.CheckAsync(manual: false);

        await service.DownloadAsync();
        await service.InstallAsync();

        service.Status.Should().Be(UpdateStatus.Available);
        service.HasPackage.Should().BeFalse();
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
    /// Waits until a condition holds, for at most five seconds, and fails the test if it does not.
    /// </summary>
    /// <param name="condition">
    /// The condition.
    /// </param>
    /// <returns>
    /// A task that completes when the condition holds.
    /// </returns>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition did not hold in time.");
            }

            await Task.Delay(10);
        }
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
    /// <param name="isAutoCheckEnabled">
    /// Whether automatic checks are on; null means always.
    /// </param>
    /// <param name="firstCheckDelay">
    /// How long after the start the first automatic check is made; null for the default.
    /// </param>
    /// <returns>
    /// The service.
    /// </returns>
    private UpdateService Create(
        string? current,
        bool officialBuild = true,
        bool useSetup = false,
        Func<bool>? isAutoCheckEnabled = null,
        TimeSpan? firstCheckDelay = null,
        TimeSpan? checkRound = null) =>
        new(
            Path.Combine(_directory, "update-state.json"),
            NullLogger<UpdateService>.Instance,
            current is null ? null : SemanticVersion.Parse(current),
            officialBuild,
            _downloads,
            useSetup,
            new HttpClient(_github, disposeHandler: false),
            _launcher,
            prepareTimeout: TimeSpan.FromMilliseconds(200),
            installerGracePeriod: TimeSpan.Zero,
            isAutoCheckEnabled: isAutoCheckEnabled,
            firstCheckDelay: firstCheckDelay,
            checkRound: checkRound);

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
        public int? ProcessId { get; set; }

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
