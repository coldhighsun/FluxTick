namespace FluxTick.App.Services;

/// <summary>
/// The place the per-application tray icon records are kept, which on Windows is the registry.
/// </summary>
public interface INotifyIconRecords
{
    /// <summary>
    /// Reads all records that name an executable.
    /// </summary>
    /// <returns>
    /// The records.
    /// </returns>
    IReadOnlyList<NotifyIconSettings.Entry> ReadAll();

    /// <summary>
    /// Reads whether the icon of one record is kept on the taskbar.
    /// </summary>
    /// <param name="key">
    /// The key of the record.
    /// </param>
    /// <returns>
    /// The choice, or <see langword="null"/> when the record is gone.
    /// </returns>
    bool? ReadPromoted(string key);

    /// <summary>
    /// Deletes one record.
    /// </summary>
    /// <param name="key">
    /// The key of the record.
    /// </param>
    void Delete(string key);
}

/// <summary>
/// Follows whether the user keeps the tray icon of an executable on the taskbar instead of folded into the overflow, and
/// removes the records of copies that are gone. It reads the records; call it off the UI thread, as the files it checks may
/// be on a drive that is slow or not there.
/// </summary>
public sealed class NotifyIconPromotion
{
    /// <summary>
    /// The place the records are kept.
    /// </summary>
    private readonly INotifyIconRecords _records;

    /// <summary>
    /// The path of the executable.
    /// </summary>
    private readonly string _executable;

    /// <summary>
    /// Finds the folder of a known folder id.
    /// </summary>
    private readonly Func<Guid, string?> _knownFolder;

    /// <summary>
    /// Tells whether a file is known to be gone.
    /// </summary>
    private readonly Func<string, bool> _isGone;

    /// <summary>
    /// The key of the record of the executable, once found.
    /// </summary>
    private volatile string? _ownKey;

    /// <summary>
    /// Whether the icon is kept on the taskbar.
    /// </summary>
    private volatile bool _promoted = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotifyIconPromotion"/> class.
    /// </summary>
    /// <param name="records">
    /// The place the records are kept.
    /// </param>
    /// <param name="executable">
    /// The path of the executable.
    /// </param>
    /// <param name="knownFolder">
    /// Finds the folder of a known folder id.
    /// </param>
    /// <param name="isGone">
    /// Tells whether a file is known to be gone, which is not the same as not being found; see <see cref="IsFileGone"/>.
    /// </param>
    public NotifyIconPromotion(INotifyIconRecords records, string executable, Func<Guid, string?> knownFolder, Func<string, bool> isGone)
    {
        _records = records;
        _executable = executable;
        _knownFolder = knownFolder;
        _isGone = isGone;
    }

    /// <summary>
    /// Gets a value indicating whether the icon is kept on the taskbar, as last read. It is assumed while there is no record
    /// of the executable or it could not be read.
    /// </summary>
    public bool Promoted => _promoted;

    /// <summary>
    /// Gets a value indicating whether a record of the executable was found.
    /// </summary>
    public bool HasRecord => _ownKey is not null;

    /// <summary>
    /// Tells whether a file is known to be gone. A file that is not found on a drive that is not there, or on a network
    /// share, or whose path could not be resolved, may well exist, so it is not reported as gone.
    /// </summary>
    /// <param name="path">
    /// The path of the file.
    /// </param>
    /// <param name="fileExists">
    /// Tells whether a file exists.
    /// </param>
    /// <param name="directoryExists">
    /// Tells whether a directory exists.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the drive of the path is there and the file is not.
    /// </returns>
    public static bool IsFileGone(string path, Func<string, bool> fileExists, Func<string, bool> directoryExists)
    {
        var root = WindowsPath.DriveRoot(path);
        return root.Length > 0 && directoryExists(root) && !fileExists(path);
    }

    /// <summary>
    /// Reads the choice again. Once the record of the executable is known only that one key is read; all records are scanned
    /// to find it, and again if it disappears. When the records cannot be read, the last answer stays.
    /// </summary>
    public void Refresh()
    {
        try
        {
            if (_ownKey is { } key && _records.ReadPromoted(key) is { } promoted)
            {
                _promoted = promoted;
                return;
            }

            var found = NotifyIconSettings.Resolve(_records.ReadAll(), _executable, _knownFolder, _isGone);
            _ownKey = found.OwnKey;
            _promoted = found.Promoted;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // The last answer stays.
        }
    }

    /// <summary>
    /// Deletes the records of copies of the executable whose file is gone. Windows keeps one record per path and never removes
    /// them, so a build that was deleted would otherwise leave its choice behind. Records of copies that may still exist are
    /// left alone, as they hold the choice of those copies.
    /// </summary>
    public void DropStale()
    {
        try
        {
            foreach (var key in NotifyIconSettings.Resolve(_records.ReadAll(), _executable, _knownFolder, _isGone).Stale)
            {
                _records.Delete(key);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            // The records stay; they are ignored when they do not name this executable.
        }
    }
}
