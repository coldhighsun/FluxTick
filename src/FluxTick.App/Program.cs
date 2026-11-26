using System.Runtime.InteropServices;
using Avalonia;
using FluxTick.App.Services;

namespace FluxTick.App;

/// <summary>
/// Application entry point.
/// </summary>
public static class Program
{
    /// <summary>
    /// Starts the desktop application.
    /// </summary>
    /// <param name="args">
    /// The command line arguments; <c>--demo</c> runs offline with simulated data, and <c>--dump-history [file]</c> prints a stored price history as CSV and exits.
    /// </param>
    /// <returns>
    /// The process exit code.
    /// </returns>
    [STAThread]
    public static int Main(string[] args)
    {
        if (HistoryDumpCommand.IsRequested(args))
        {
            return RunHistoryDump(args);
        }

        var name = SingleInstance.NameFor(args.Contains("--demo", StringComparer.OrdinalIgnoreCase));
        SingleInstance? instance;
        try
        {
            instance = SingleInstance.TryAcquire(name);
            if (instance is null)
            {
                // Another copy runs, possibly hidden in the tray: bring its window forward instead of starting a second one.
                SingleInstance.NotifyRunning(name);
                return 0;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            // The guard is a convenience: when the system refuses it, the app still starts, just without it.
            instance = null;
        }

        using (instance)
        {
            Instance = instance;
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
    }

    /// <summary>
    /// Runs <c>--dump-history</c>: prints a price history file as CSV to the console that started the process.
    /// </summary>
    /// <param name="args">
    /// The command line arguments.
    /// </param>
    /// <returns>
    /// The process exit code.
    /// </returns>
    private static int RunHistoryDump(string[] args)
    {
        // The app is a Windows GUI program, which has no console of its own: join the one of the shell that started it.
        if (OperatingSystem.IsWindows())
        {
            AttachConsole(AttachParentProcess);
        }

        using var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        using var error = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
        return HistoryDumpCommand.Run(args, AppPaths.HistoryDirectory, output, error);
    }

    /// <summary>
    /// Attaches the process to the console of another process.
    /// </summary>
    /// <param name="processId">
    /// The process, or <see cref="AttachParentProcess"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> on success.
    /// </returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    /// <summary>
    /// The value of <c>ATTACH_PARENT_PROCESS</c>.
    /// </summary>
    private const int AttachParentProcess = -1;

    /// <summary>
    /// Gets the guard that keeps this copy the only one running, or <see langword="null"/> when the visual designer
    /// builds the app.
    /// </summary>
    internal static SingleInstance? Instance { get; private set; }

    /// <summary>
    /// Configures Avalonia. Also used by the visual designer.
    /// </summary>
    /// <returns>
    /// The configured builder.
    /// </returns>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // The GPU paths (ANGLE/WGL) keep 100+ MB of driver memory resident for a window of text and sparklines, so
            // draw on the CPU instead.
            .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
            .LogToTrace();
}
