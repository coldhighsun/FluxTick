using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace FluxTick.App.Services;

/// <summary>
/// Builds and reads the launchd property list that starts the app when the user logs in on macOS.
/// </summary>
public static class LaunchAgentPlist
{
    /// <summary>
    /// The label of the launch agent, which is also the bundle identifier of the app.
    /// </summary>
    public const string Label = "io.github.coldhighsun.FluxTick";

    /// <summary>
    /// The public identifier of the DOCTYPE of an XML property list.
    /// </summary>
    private const string PublicId = "-//Apple//DTD PLIST 1.0//EN";

    /// <summary>
    /// The system identifier (the DTD location) of the DOCTYPE of an XML property list.
    /// </summary>
    private const string SystemId = "http://www.apple.com/DTDs/PropertyList-1.0.dtd";

    /// <summary>
    /// Builds the property list that starts an executable at login.
    /// </summary>
    /// <param name="label">
    /// The label of the launch agent.
    /// </param>
    /// <param name="executable">
    /// The path of the executable.
    /// </param>
    /// <returns>
    /// The XML text of the property list.
    /// </returns>
    public static string Build(string label, string executable)
    {
        var dict = new XElement(
            "dict",
            new XElement("key", "Label"),
            new XElement("string", label),
            new XElement("key", "ProgramArguments"),
            new XElement("array", new XElement("string", executable)),
            new XElement("key", "RunAtLoad"),
            new XElement("true"),
            new XElement("key", "ProcessType"),
            new XElement("string", "Interactive"));

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("plist", PublicId, SystemId, null),
            new XElement("plist", new XAttribute("version", "1.0"), dict));

        return $"{document.Declaration}{Environment.NewLine}{document}{Environment.NewLine}";
    }

    /// <summary>
    /// Reads the executable that a property list starts.
    /// </summary>
    /// <param name="xml">
    /// The XML text of the property list.
    /// </param>
    /// <returns>
    /// The first program argument, or <see langword="null"/> when the text is not a property list of this shape.
    /// </returns>
    public static string? ExecutableOf(string xml)
    {
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            var dict = XDocument.Load(reader).Root?.Element("dict");
            if (dict is null)
            {
                return null;
            }

            // Keys and values alternate; the array follows its key.
            var key = dict.Elements("key").FirstOrDefault(e => e.Value == "ProgramArguments");
            var value = key?.ElementsAfterSelf().FirstOrDefault();
            var first = value?.Element("string")?.Value;
            return string.IsNullOrWhiteSpace(first) ? null : first;
        }
        catch (XmlException)
        {
            return null;
        }
    }
}

/// <summary>
/// An <see cref="IAutoStartService"/> that keeps a launch agent in the per-user <c>LaunchAgents</c> folder of macOS.
/// </summary>
/// <param name="logger">
/// The logger.
/// </param>
/// <param name="launchAgentsDirectory">
/// The folder that holds the property list, or <see langword="null"/> for <c>~/Library/LaunchAgents</c>.
/// </param>
/// <param name="processPath">
/// The path of the running executable, or <see langword="null"/> for <see cref="Environment.ProcessPath"/>.
/// </param>
public sealed class MacAutoStartService(
    ILogger<MacAutoStartService> logger,
    string? launchAgentsDirectory = null,
    string? processPath = null) : IAutoStartService
{
    /// <summary>
    /// The folder that holds the property list.
    /// </summary>
    private readonly string _directory = launchAgentsDirectory
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");

    /// <summary>
    /// The path of the running executable.
    /// </summary>
    private readonly string? _processPath = processPath ?? Environment.ProcessPath;

    /// <summary>
    /// Gets the path of the property list.
    /// </summary>
    private string PlistPath => Path.Combine(_directory, $"{LaunchAgentPlist.Label}.plist");

    /// <inheritdoc />
    public bool IsSupported => AutoStartCommand.IsApplicationPath(_processPath) && IsPersistentPath(_processPath!);

    /// <summary>
    /// Determines whether a path will still exist at the next login. An app started from a mounted disk image, or from a
    /// quarantined download that Gatekeeper copied to a randomized read-only location, runs from a path that is gone later.
    /// </summary>
    /// <param name="path">
    /// The path of the running executable.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the path can be registered.
    /// </returns>
    public static bool IsPersistentPath(string path) =>
        !path.StartsWith("/Volumes/", StringComparison.Ordinal) && !path.Contains("/AppTranslocation/", StringComparison.Ordinal);

    /// <inheritdoc />
    public bool IsEnabled() => IsSupported && ReadExecutable() is { } executable && NamesCurrentExecutable(executable);

    /// <summary>
    /// Determines whether the executable in the property list is the running one. The path is not a command line, so it is
    /// quoted first; otherwise a folder name with a space would cut it short.
    /// </summary>
    /// <param name="executable">
    /// The executable read from the property list.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both name the same file.
    /// </returns>
    private bool NamesCurrentExecutable(string executable) =>
        AutoStartCommand.IsCurrentExecutable(AutoStartCommand.Build(executable), _processPath);

    /// <inheritdoc />
    public void RepairIfMoved()
    {
        if (!IsSupported || ReadExecutable() is not { } executable)
        {
            return;
        }

        if (!NamesCurrentExecutable(executable))
        {
            try
            {
                Write();
                logger.LogInformation("Launch agent pointed at {Executable}, as it named another file", _processPath);
            }
            catch (Exception ex)
            {
                // Starting the app does not depend on the repair.
                logger.LogWarning(ex, "Could not repair the launch agent");
            }
        }
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        if (!IsSupported)
        {
            return;
        }

        try
        {
            if (enabled)
            {
                Write();
            }
            else
            {
                // launchd may still have the job loaded in this session, but unloading it could end the running app when
                // launchd started it. Removing the file is enough: the job is not started at the next login.
                File.Delete(PlistPath);
            }

            logger.LogInformation("Launch at login turned {State}", enabled ? "on" : "off");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not {Action} the launch agent", enabled ? "create" : "remove");
            throw;
        }
    }

    /// <summary>
    /// Writes the property list for the running executable, through a temporary file so that a reader never sees half of it.
    /// </summary>
    private void Write()
    {
        Directory.CreateDirectory(_directory);
        var temp = $"{PlistPath}.tmp";
        try
        {
            File.WriteAllText(temp, LaunchAgentPlist.Build(LaunchAgentPlist.Label, _processPath!));
            File.Move(temp, PlistPath, overwrite: true);
        }
        catch
        {
            // A failed move must not leave the temporary file in the user's LaunchAgents folder.
            try
            {
                File.Delete(temp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Could not remove the temporary launch agent file");
            }

            throw;
        }
    }

    /// <summary>
    /// Reads the executable the property list starts.
    /// </summary>
    /// <returns>
    /// The path, or <see langword="null"/> when there is no usable property list or it cannot be read.
    /// </returns>
    private string? ReadExecutable()
    {
        try
        {
            return File.Exists(PlistPath) ? LaunchAgentPlist.ExecutableOf(File.ReadAllText(PlistPath)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read the launch agent");
            return null;
        }
    }
}
