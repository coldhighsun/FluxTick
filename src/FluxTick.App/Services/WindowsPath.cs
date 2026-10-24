namespace FluxTick.App.Services;

/// <summary>
/// Takes apart the Windows paths the tray icon records hold. The records are always Windows paths, so these work the same on
/// every system, unlike <see cref="System.IO.Path"/>, whose separators and roots depend on the system it runs on.
/// </summary>
internal static class WindowsPath
{
    /// <summary>
    /// Gets the last part of a path.
    /// </summary>
    /// <param name="path">
    /// The path, with backslashes or slashes.
    /// </param>
    /// <returns>
    /// The part after the last separator, or the whole path when it has none.
    /// </returns>
    public static string FileName(string path)
    {
        var index = path.LastIndexOfAny(['\\', '/']);
        return index < 0 ? path : path[(index + 1)..];
    }

    /// <summary>
    /// Gets the root of a path that starts with a drive letter.
    /// </summary>
    /// <param name="path">
    /// The path.
    /// </param>
    /// <returns>
    /// The root such as <c>D:\</c>, or an empty text for paths without a drive letter, such as network shares and paths that
    /// are not resolved.
    /// </returns>
    public static string DriveRoot(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/' ? path[..2] + '\\' : string.Empty;

    /// <summary>
    /// Joins a folder and a path inside it.
    /// </summary>
    /// <param name="folder">
    /// The folder.
    /// </param>
    /// <param name="relative">
    /// The path inside the folder, which may start with a separator.
    /// </param>
    /// <returns>
    /// The joined path with backslashes.
    /// </returns>
    public static string Combine(string folder, string relative)
    {
        var rest = relative.TrimStart('\\', '/').Replace('/', '\\');
        var start = folder.TrimEnd('\\', '/');
        return rest.Length == 0 ? start : start + '\\' + rest;
    }
}
