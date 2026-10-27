using System.Xml.Linq;

namespace FluxTick.App.Tests;

/// <summary>
/// Finds the source files of the application for the tests that read them.
/// </summary>
internal static class SourceFiles
{
    /// <summary>
    /// Loads a XAML file of the application. The test is skipped when the sources are not next to the build output, for
    /// example when the tests run from a copy of it.
    /// </summary>
    /// <param name="pathInApp">
    /// The path inside the application's project folder, for example <c>Views</c> and <c>MainWindow.axaml</c>.
    /// </param>
    /// <returns>
    /// The parsed document.
    /// </returns>
    public static XDocument LoadXaml(params string[] pathInApp)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FluxTick.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.SkipWhen(directory is null, "The sources are not next to the build output.");
        return XDocument.Load(Path.Combine([directory!.FullName, "src", "FluxTick.App", .. pathInApp]));
    }
}
