using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluxTick.App.Services;
using GitHubReleaseUpdater.Assets;
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
    /// Creates a service that talks to the canned GitHub.
    /// </summary>
    /// <param name="current">
    /// The running version, or <see langword="null"/> when it is unknown.
    /// </param>
    /// <param name="officialBuild">
    /// Whether the release workflow built the program.
    /// </param>
    /// <returns>
    /// The service.
    /// </returns>
    private UpdateService Create(string? current, bool officialBuild = true) =>
        new(
            Path.Combine(_directory, "update-state.json"),
            NullLogger<UpdateService>.Instance,
            current is null ? null : SemanticVersion.Parse(current),
            officialBuild,
            _downloads,
            new HttpClient(_github, disposeHandler: false));

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
        /// <returns>
        /// The JSON.
        /// </returns>
        public static string Release(string tag, bool withPackage)
        {
            var name = $"FluxTick-{tag}-{RuntimeInfo.Current.Rid}.zip";
            var assets = withPackage
                ? $$"""
                    [
                      { "id": 1, "name": "{{name}}", "size": {{Package.Length}}, "browser_download_url": "https://example.test/dl/{{name}}", "url": "https://example.test/dl/{{name}}" },
                      { "id": 2, "name": "SHA256SUMS.txt", "size": 100, "browser_download_url": "https://example.test/dl/SHA256SUMS.txt", "url": "https://example.test/dl/SHA256SUMS.txt" }
                    ]
                    """
                : "[]";
            return $$"""
                { "id": 10, "tag_name": "{{tag}}", "name": "{{tag}}", "draft": false, "prerelease": false, "assets": {{assets}} }
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
                var name = Path.GetFileName(RuntimeInfo.Current.Rid);
                var hash = Convert.ToHexStringLower(SHA256.HashData(Package));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{hash}  FluxTick-v9.9.9-{name}.zip\n"),
                });
            }

            if (path.EndsWith(".zip", StringComparison.Ordinal))
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
