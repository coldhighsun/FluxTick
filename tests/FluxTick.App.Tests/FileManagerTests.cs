using System.ComponentModel;
using System.Diagnostics;
using FluxTick.App.Services;
using Microsoft.Extensions.Logging;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests of <see cref="FileManager"/>, which opens the settings and log folders in the file manager.
/// </summary>
public sealed class FileManagerTests : IDisposable
{
    /// <summary>
    /// The folder the tests work in.
    /// </summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FluxTickTests-" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies a missing folder is created and then handed to the file manager.
    /// </summary>
    [Fact]
    public void TryOpen_MissingFolder_CreatesItAndStartsTheFileManager()
    {
        var folder = Path.Combine(_root, "logs");
        var started = new List<ProcessStartInfo>();

        var opened = FileManager.TryOpen(folder, started.Add);

        opened.Should().BeTrue();
        Directory.Exists(folder).Should().BeTrue();
        started.Should().ContainSingle();
        started[0].ArgumentList.Should().ContainSingle().Which.Should().Be(folder);
        started[0].UseShellExecute.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a folder that cannot be made is reported as not opened, and the file manager is not started for it.
    /// </summary>
    [Fact]
    public void TryOpen_PathIsAFile_ReturnsFalseWithoutStarting()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "taken");
        File.WriteAllText(file, string.Empty);
        var started = new List<ProcessStartInfo>();

        var opened = FileManager.TryOpen(file, started.Add);

        opened.Should().BeFalse();
        started.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a path the platform rejects is reported as not opened instead of throwing. The path is inside the test
    /// folder, so a platform that accepts it leaves nothing behind.
    /// </summary>
    [Fact]
    public void TryOpen_RejectedPath_ReturnsFalse() =>
        FileManager.TryOpen(Path.Combine(_root, "a\0b"), _ => { }).Should().BeFalse();

    /// <summary>
    /// Verifies the file manager is named by a full path where the platform has a fixed one, and by its name where it
    /// has not.
    /// </summary>
    [Fact]
    public void TryOpen_Started_NamesTheFileManagerOfThePlatform()
    {
        var started = new List<ProcessStartInfo>();

        FileManager.TryOpen(_root, started.Add);

        var program = started.Single().FileName;
        if (OperatingSystem.IsWindows())
        {
            program.Should().Be(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            program.Should().Be("/usr/bin/open");
        }
        else
        {
            program.Should().Be("xdg-open");
        }
    }

    /// <summary>
    /// Verifies the reason for a failure goes to the logger, for a folder that cannot be made and for a missing file
    /// manager.
    /// </summary>
    [Fact]
    public void TryOpen_Fails_LogsTheReason()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "taken");
        File.WriteAllText(file, string.Empty);
        var logger = new CollectingLogger();

        FileManager.TryOpen(file, _ => { }, logger);
        FileManager.TryOpen(_root, _ => throw new Win32Exception("no file manager"), logger);

        logger.Warnings.Should().HaveCount(2);
        logger.Warnings.Should().OnlyContain(warning => warning.Exception != null);
    }

    /// <summary>
    /// Verifies a failure that is not a missing file manager is not hidden.
    /// </summary>
    [Fact]
    public void TryOpen_DefectInStart_Throws()
    {
        var act = () => FileManager.TryOpen(_root, _ => throw new InvalidOperationException("no file name"));

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// Verifies a file is handed to the file manager and a machine without one is reported as not shown.
    /// </summary>
    [Fact]
    public void TryReveal_File_StartsTheFileManagerOrReportsNone()
    {
        var file = Path.Combine(_root, "update.zip");
        var started = new List<ProcessStartInfo>();

        var shown = FileManager.TryReveal(file, started.Add);
        var none = FileManager.TryReveal(file, _ => throw new Win32Exception("no file manager"));

        shown.Should().BeTrue();
        none.Should().BeFalse();
        started.Should().ContainSingle();

        // Windows and macOS select the file itself; elsewhere only the folder that holds it can be opened.
        var expected = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? file : _root;
        (started[0].Arguments + string.Join(' ', started[0].ArgumentList)).Should().Contain(expected);
    }

    /// <summary>
    /// Verifies revealing a file logs the reason when it fails, and does not hide a defect.
    /// </summary>
    [Fact]
    public void TryReveal_Fails_LogsTheReasonAndLetsDefectsThrough()
    {
        var logger = new CollectingLogger();
        var file = Path.Combine(_root, "update.zip");

        var shown = FileManager.TryReveal(file, _ => throw new Win32Exception("no file manager"), logger);
        var act = () => FileManager.TryReveal(file, _ => throw new InvalidOperationException("no file name"), logger);

        shown.Should().BeFalse();
        logger.Warnings.Should().ContainSingle().Which.Exception.Should().BeOfType<Win32Exception>();
        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// Verifies the log folder is the one the log file pattern is written in.
    /// </summary>
    [Fact]
    public void LogDirectory_IsTheFolderOfTheLogFile()
    {
        Path.GetDirectoryName(AppPaths.LogFile).Should().Be(AppPaths.LogDirectory);
        AppPaths.LogDirectory.Should().StartWith(AppPaths.DataDirectory);
    }

    /// <summary>
    /// Verifies a machine without a file manager is reported as not opened instead of failing.
    /// </summary>
    [Fact]
    public void TryOpen_NoFileManager_ReturnsFalse()
    {
        var opened = FileManager.TryOpen(_root, _ => throw new Win32Exception("no file manager"));

        opened.Should().BeFalse();
    }

    /// <summary>
    /// A logger that keeps the warnings it receives.
    /// </summary>
    private sealed class CollectingLogger : ILogger
    {
        /// <summary>
        /// Gets the warnings received so far.
        /// </summary>
        public List<(string Message, Exception? Exception)> Warnings { get; } = [];

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc/>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add((formatter(state, exception), exception));
            }
        }
    }
}
