using AwesomeAssertions;
using FluxTick.App.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the launch agent that starts the app at login on macOS. They only touch a temporary folder.
/// </summary>
public sealed class MacAutoStartTests : IDisposable
{
    /// <summary>
    /// The folder standing in for <c>~/Library/LaunchAgents</c>.
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"fluxtick-agents-{Guid.NewGuid():N}");

    /// <summary>
    /// The path of the property list the service writes.
    /// </summary>
    private string PlistPath => Path.Combine(_directory, $"{LaunchAgentPlist.Label}.plist");

    /// <summary>
    /// Creates a service for an executable path.
    /// </summary>
    /// <param name="processPath">
    /// The path of the running executable.
    /// </param>
    /// <returns>
    /// The service.
    /// </returns>
    private MacAutoStartService CreateService(string? processPath = "/Applications/FluxTick.app/Contents/MacOS/FluxTick") =>
        new(NullLogger<MacAutoStartService>.Instance, _directory, processPath);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// Checks that a built property list gives back the same executable.
    /// </summary>
    /// <param name="executable">
    /// The path to round-trip.
    /// </param>
    [Theory]
    [InlineData("/Applications/FluxTick.app/Contents/MacOS/FluxTick")]
    [InlineData("/Users/a b/Apps/Flux & Tick <1>.app/Contents/MacOS/FluxTick")]
    public void Build_ThenExecutableOf_RoundTripsThePath(string executable)
    {
        var xml = LaunchAgentPlist.Build(LaunchAgentPlist.Label, executable);

        LaunchAgentPlist.ExecutableOf(xml).Should().Be(executable);
        xml.Should().Contain("<key>RunAtLoad</key>").And.Contain("<true />");
    }

    /// <summary>
    /// Checks that text which is not a property list names no executable.
    /// </summary>
    /// <param name="xml">
    /// The text to read.
    /// </param>
    [Theory]
    [InlineData("")]
    [InlineData("not xml")]
    [InlineData("<plist version=\"1.0\"><dict></dict></plist>")]
    [InlineData("<plist version=\"1.0\"><dict><key>ProgramArguments</key><array></array></dict></plist>")]
    public void ExecutableOf_NotAUsablePlist_ReturnsNull(string xml)
    {
        LaunchAgentPlist.ExecutableOf(xml).Should().BeNull();
    }

    /// <summary>
    /// Checks that turning the setting on writes the agent and the service reads it back.
    /// </summary>
    [Fact]
    public void SetEnabled_True_WritesPlistAndIsEnabled()
    {
        var service = CreateService();

        service.SetEnabled(true);

        service.IsEnabled().Should().BeTrue();
        File.ReadAllText(PlistPath).Should().Contain("/Applications/FluxTick.app/Contents/MacOS/FluxTick");
        File.Exists($"{PlistPath}.tmp").Should().BeFalse();
    }

    /// <summary>
    /// Checks that turning the setting off removes the agent, also when there is none.
    /// </summary>
    [Fact]
    public void SetEnabled_False_RemovesPlistAndToleratesMissingFile()
    {
        var service = CreateService();
        service.SetEnabled(true);

        service.SetEnabled(false);
        var again = () => service.SetEnabled(false);

        service.IsEnabled().Should().BeFalse();
        File.Exists(PlistPath).Should().BeFalse();
        again.Should().NotThrow();
    }

    /// <summary>
    /// Checks that a process that is not the app itself, such as the dotnet host, is not supported.
    /// </summary>
    [Fact]
    public void SetEnabled_DotnetHost_DoesNothing()
    {
        var service = CreateService("/usr/local/share/dotnet/dotnet");

        service.SetEnabled(true);

        service.IsSupported.Should().BeFalse();
        File.Exists(PlistPath).Should().BeFalse();
    }

    /// <summary>
    /// Checks that an agent naming another executable is pointed at the running one.
    /// </summary>
    [Fact]
    public void RepairIfMoved_OtherExecutable_RewritesPlist()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PlistPath, LaunchAgentPlist.Build(LaunchAgentPlist.Label, "/Old/FluxTick.app/Contents/MacOS/FluxTick"));

        CreateService().RepairIfMoved();

        LaunchAgentPlist.ExecutableOf(File.ReadAllText(PlistPath)).Should().Be("/Applications/FluxTick.app/Contents/MacOS/FluxTick");
    }

    /// <summary>
    /// Checks that an agent which already names the running executable is left as it is.
    /// </summary>
    [Fact]
    public void RepairIfMoved_SameExecutable_LeavesPlistUntouched()
    {
        var service = CreateService();
        service.SetEnabled(true);
        var written = File.GetLastWriteTimeUtc(PlistPath);
        File.SetLastWriteTimeUtc(PlistPath, written.AddHours(-1));
        var before = File.GetLastWriteTimeUtc(PlistPath);

        service.RepairIfMoved();

        File.GetLastWriteTimeUtc(PlistPath).Should().Be(before);
    }

    /// <summary>
    /// Checks that a path with spaces that already matches is not rewritten on every launch.
    /// </summary>
    [Fact]
    public void RepairIfMoved_SameExecutableWithSpaces_LeavesPlistUntouched()
    {
        var service = CreateService("/Applications/My Apps/FluxTick.app/Contents/MacOS/FluxTick");
        service.SetEnabled(true);
        File.SetLastWriteTimeUtc(PlistPath, DateTime.UtcNow.AddHours(-1));
        var before = File.GetLastWriteTimeUtc(PlistPath);

        service.RepairIfMoved();

        File.GetLastWriteTimeUtc(PlistPath).Should().Be(before);
        service.IsEnabled().Should().BeTrue();
    }

    /// <summary>
    /// Checks that an agent naming another executable does not count as enabled.
    /// </summary>
    [Fact]
    public void IsEnabled_PlistNamesOtherExecutable_ReturnsFalse()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PlistPath, LaunchAgentPlist.Build(LaunchAgentPlist.Label, "/Old/FluxTick.app/Contents/MacOS/FluxTick"));

        CreateService().IsEnabled().Should().BeFalse();
    }

    /// <summary>
    /// Checks that a path that will not exist at the next login is not supported.
    /// </summary>
    /// <param name="path">
    /// The path of the running executable.
    /// </param>
    [Theory]
    [InlineData("/Volumes/FluxTick/FluxTick.app/Contents/MacOS/FluxTick")]
    [InlineData("/private/var/folders/ab/T/AppTranslocation/1F2E/d/FluxTick.app/Contents/MacOS/FluxTick")]
    public void IsSupported_TransientLocation_ReturnsFalse(string path)
    {
        var service = CreateService(path);

        service.IsSupported.Should().BeFalse();
        service.SetEnabled(true);
        File.Exists(PlistPath).Should().BeFalse();
    }

    /// <summary>
    /// Checks that a failed write leaves no temporary file behind.
    /// </summary>
    [Fact]
    public void SetEnabled_True_MoveFails_RemovesTemporaryFile()
    {
        var service = CreateService();
        Directory.CreateDirectory(PlistPath);

        var act = () => service.SetEnabled(true);

        act.Should().Throw<Exception>();
        File.Exists($"{PlistPath}.tmp").Should().BeFalse();
    }

    /// <summary>
    /// Checks that no agent is created when the user never turned the setting on.
    /// </summary>
    [Fact]
    public void RepairIfMoved_NoPlist_CreatesNothing()
    {
        CreateService().RepairIfMoved();

        File.Exists(PlistPath).Should().BeFalse();
    }

    /// <summary>
    /// Checks that a corrupt property list counts as not enabled.
    /// </summary>
    [Fact]
    public void IsEnabled_CorruptPlist_ReturnsFalse()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PlistPath, "<<<garbage");

        CreateService().IsEnabled().Should().BeFalse();
    }
}
