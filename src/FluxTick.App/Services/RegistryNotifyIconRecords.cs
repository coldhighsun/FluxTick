using System.Runtime.Versioning;
using Microsoft.Win32;

namespace FluxTick.App.Services;

/// <summary>
/// The tray icon records of Windows, kept under <c>HKEY_CURRENT_USER\Control Panel\NotifyIconSettings</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryNotifyIconRecords : INotifyIconRecords
{
    /// <summary>
    /// The registry key that holds the records.
    /// </summary>
    private const string SettingsKey = @"Control Panel\NotifyIconSettings";

    /// <inheritdoc />
    public IReadOnlyList<NotifyIconSettings.Entry> ReadAll()
    {
        using var root = Registry.CurrentUser.OpenSubKey(SettingsKey);
        var records = new List<NotifyIconSettings.Entry>();
        if (root is null)
        {
            return records;
        }

        foreach (var name in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(name);
            if (key?.GetValue("ExecutablePath") is string path)
            {
                records.Add(new NotifyIconSettings.Entry(name, path, IsPromoted(key)));
            }
        }

        return records;
    }

    /// <inheritdoc />
    public bool? ReadPromoted(string key)
    {
        using var root = Registry.CurrentUser.OpenSubKey(SettingsKey);
        using var record = root?.OpenSubKey(key);
        return record is null ? null : IsPromoted(record);
    }

    /// <inheritdoc />
    public void Delete(string key)
    {
        using var root = Registry.CurrentUser.OpenSubKey(SettingsKey, writable: true);
        root?.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
    }

    /// <summary>
    /// Reads the choice of a record; a record without the value counts as keeping the icon visible.
    /// </summary>
    /// <param name="record">
    /// The registry key of the record.
    /// </param>
    /// <returns>
    /// <see langword="true"/> unless the value is present and zero.
    /// </returns>
    private static bool IsPromoted(RegistryKey record) => record.GetValue("IsPromoted") is not int value || value != 0;
}
