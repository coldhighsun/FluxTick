using Avalonia;

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
    public static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Configures Avalonia. Also used by the visual designer.
    /// </summary>
    /// <returns>
    /// The configured builder.
    /// </returns>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
