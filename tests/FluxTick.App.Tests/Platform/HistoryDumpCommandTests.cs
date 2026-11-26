using FluxTick.App.Services;

namespace FluxTick.App.Tests.Platform;

/// <summary>
/// Tests for <see cref="HistoryDumpCommand"/>.
/// </summary>
public sealed class HistoryDumpCommandTests : IDisposable
{
    /// <summary>
    /// The history folder of this test run.
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"fluxtick-dumpcmd-{Guid.NewGuid():N}");

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// Checks that the option is recognised in both spellings and only then.
    /// </summary>
    [Theory]
    [InlineData("--dump-history", true)]
    [InlineData("--DUMP-HISTORY=a.bin", true)]
    [InlineData("--demo", false)]
    public void IsRequested_Args_DetectsOption(string arg, bool expected)
    {
        HistoryDumpCommand.IsRequested([arg]).Should().Be(expected);
    }

    /// <summary>
    /// Checks that a file name that is not found is reported with exit code 1.
    /// </summary>
    [Fact]
    public void Run_MissingFile_ReturnsOneAndReports()
    {
        var error = new StringWriter();

        var code = HistoryDumpCommand.Run(["--dump-history", "nope.bin"], _directory, new StringWriter(), error);

        code.Should().Be(1);
        error.ToString().Should().Contain("not found");
    }

    /// <summary>
    /// Checks that a file relative to the history folder is dumped as CSV and that without a file the files are listed.
    /// </summary>
    [Fact]
    public void Run_RelativeFile_WritesCsvAndListsFiles()
    {
        var folder = Path.Combine(_directory, "binance");
        Directory.CreateDirectory(folder);
        var record = new byte[24];
        BitConverter.TryWriteBytes(record.AsSpan(0, 8), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds());
        decimal.GetBits(5.5m).Select((bits, i) => BitConverter.TryWriteBytes(record.AsSpan(8 + (i * 4), 4), bits)).ToList();
        File.WriteAllBytes(Path.Combine(folder, "q-X.bin"), record);

        var csv = new StringWriter();
        var list = new StringWriter();
        var code = HistoryDumpCommand.Run([$"--dump-history={Path.Combine("binance", "q-X.bin")}"], _directory, csv, new StringWriter());
        var listCode = HistoryDumpCommand.Run(["--dump-history"], _directory, list, new StringWriter());

        code.Should().Be(0);
        csv.ToString().ReplaceLineEndings("\n").Should().Be("time_utc,price\n2026-01-01T00:00:00.000Z,5.5\n");
        listCode.Should().Be(0);
        list.ToString().Should().Contain("q-X.bin").And.Contain("1 records");
    }
}
