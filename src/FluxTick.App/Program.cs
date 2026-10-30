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
    /// The command line arguments; <c>--demo</c> runs offline with simulated data.
    /// </param>
    /// <returns>
    /// The process exit code.
    /// </returns>
    [STAThread]
    public static int Main(string[] args)
    {
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
            .LogToTrace();
}
