using FluxTick.Core.Infrastructure;

namespace FluxTick.App.Services;

/// <summary>
/// The <c>--dump-history</c> command line option: prints a stored price history file as CSV and exits, without starting
/// the application.
/// </summary>
public static class HistoryDumpCommand
{
    /// <summary>
    /// The name of the option.
    /// </summary>
    public const string Option = "--dump-history";

    /// <summary>
    /// Gets whether the arguments ask for the dump.
    /// </summary>
    /// <param name="args">
    /// The command line arguments.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <see cref="Option"/> is among them.
    /// </returns>
    public static bool IsRequested(string[] args)
        => args.Any(a => a.Equals(Option, StringComparison.OrdinalIgnoreCase)
            || a.StartsWith($"{Option}=", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Runs the command: with a file, writes its prices as CSV; without one, lists the history files.
    /// </summary>
    /// <param name="args">
    /// The command line arguments; the file follows <see cref="Option"/> (as the next argument or after <c>=</c>), as a
    /// path or relative to the history folder, for example <c>binance/q-BTCUSDT.bin</c>.
    /// </param>
    /// <param name="historyDirectory">
    /// The folder the history files are kept in.
    /// </param>
    /// <param name="output">
    /// Where the result goes.
    /// </param>
    /// <param name="error">
    /// Where problems are reported.
    /// </param>
    /// <returns>
    /// The process exit code: 0 on success, 1 when the file is missing or cannot be read.
    /// </returns>
    public static int Run(string[] args, string historyDirectory, TextWriter output, TextWriter error)
    {
        var target = FindTarget(args);
        if (target is null)
        {
            return List(historyDirectory, output, error);
        }

        var path = File.Exists(target) ? target : Path.Combine(historyDirectory, target);
        if (!File.Exists(path))
        {
            error.WriteLine($"History file not found: {path}");
            return 1;
        }

        try
        {
            QuoteHistoryDump.WriteCsv(QuoteHistoryDump.Read(path), output);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Could not read {path}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Finds the file named after the option.
    /// </summary>
    /// <param name="args">
    /// The command line arguments.
    /// </param>
    /// <returns>
    /// The file, or <see langword="null"/> when none is given.
    /// </returns>
    private static string? FindTarget(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith($"{Option}=", StringComparison.OrdinalIgnoreCase))
            {
                var value = args[i][(Option.Length + 1)..];
                return value.Length == 0 ? null : value;
            }

            if (args[i].Equals(Option, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Lists the history files.
    /// </summary>
    /// <param name="historyDirectory">
    /// The folder the history files are kept in.
    /// </param>
    /// <param name="output">
    /// Where the list goes.
    /// </param>
    /// <param name="error">
    /// Where problems are reported.
    /// </param>
    /// <returns>
    /// The process exit code.
    /// </returns>
    private static int List(string historyDirectory, TextWriter output, TextWriter error)
    {
        if (!Directory.Exists(historyDirectory))
        {
            error.WriteLine($"No history folder yet: {historyDirectory}");
            return 1;
        }

        output.WriteLine($"# Usage: {Option} <file>   (a path, or relative to {historyDirectory})");
        foreach (var file in Directory.EnumerateFiles(historyDirectory, "*.bin", SearchOption.AllDirectories).Order())
        {
            var info = new FileInfo(file);
            output.WriteLine($"{Path.GetRelativePath(historyDirectory, file)}\t{QuoteHistoryDump.RecordCountOf(info.Length)} records");
        }

        return 0;
    }
}
