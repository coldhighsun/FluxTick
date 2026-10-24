namespace FluxTick.App.Services;

/// <summary>
/// Reads the per-application tray icon records Windows keeps under <c>Control Panel\NotifyIconSettings</c>, where each
/// entry names an executable and whether the user keeps its icon on the taskbar instead of folded into the overflow.
/// </summary>
public static class NotifyIconSettings
{
    /// <summary>
    /// One record of the tray icon settings.
    /// </summary>
    /// <param name="Key">
    /// The name of the registry key of the record.
    /// </param>
    /// <param name="Path">
    /// The executable the record belongs to, as Windows wrote it: a plain path, or one that starts with the id of a known
    /// folder in braces, such as the one of Program Files.
    /// </param>
    /// <param name="Promoted">
    /// Whether the icon is kept on the taskbar.
    /// </param>
    public readonly record struct Entry(string Key, string Path, bool Promoted);

    /// <summary>
    /// The outcome of looking for the record of an executable.
    /// </summary>
    /// <param name="Promoted">
    /// Whether the icon is kept on the taskbar; assumed when there is no record of this executable.
    /// </param>
    /// <param name="OwnKey">
    /// The key of the record of this executable, or <see langword="null"/> when there is none.
    /// </param>
    /// <param name="Stale">
    /// The keys of the records of other copies of the same file name whose file is gone.
    /// </param>
    public readonly record struct Resolution(bool Promoted, string? OwnKey, IReadOnlyList<string> Stale);

    /// <summary>
    /// Turns the path of a record into a plain path.
    /// </summary>
    /// <param name="path">
    /// The path as Windows wrote it.
    /// </param>
    /// <param name="knownFolder">
    /// Finds the folder of a known folder id, or returns <see langword="null"/> when there is none.
    /// </param>
    /// <returns>
    /// The path with a leading known folder id replaced by that folder.
    /// </returns>
    public static string Normalize(string path, Func<Guid, string?> knownFolder)
    {
        var end = path.IndexOf('}');
        if (path.StartsWith('{') && end > 0 && Guid.TryParse(path.AsSpan(0, end + 1), out var id) && knownFolder(id) is { } folder)
        {
            return WindowsPath.Combine(folder, path[(end + 1)..]);
        }

        return path;
    }

    /// <summary>
    /// Decides whether the icon of an executable is kept on the taskbar, and which records are left over from copies of the
    /// same file that are gone.
    /// </summary>
    /// <param name="entries">
    /// The records found.
    /// </param>
    /// <param name="executable">
    /// The path of the running executable.
    /// </param>
    /// <param name="knownFolder">
    /// Finds the folder of a known folder id.
    /// </param>
    /// <param name="isGone">
    /// Tells whether a file is known to be gone; a file that cannot be found for lack of access to its drive is not.
    /// </param>
    /// <returns>
    /// The outcome. A record of another path is only reported as left over when its file is known to be gone, so the choices
    /// for copies that are still there are never touched.
    /// </returns>
    public static Resolution Resolve(IEnumerable<Entry> entries, string executable, Func<Guid, string?> knownFolder, Func<string, bool> isGone)
    {
        var name = WindowsPath.FileName(executable);
        var promoted = true;
        string? ownKey = null;
        var stale = new List<string>();
        foreach (var entry in entries)
        {
            var path = Normalize(entry.Path, knownFolder);
            if (string.Equals(path, executable, StringComparison.OrdinalIgnoreCase))
            {
                promoted = entry.Promoted;
                ownKey = entry.Key;
            }
            else if (string.Equals(WindowsPath.FileName(path), name, StringComparison.OrdinalIgnoreCase) && isGone(path))
            {
                stale.Add(entry.Key);
            }
        }

        return new Resolution(promoted, ownKey, stale);
    }
}
