namespace FluxTick.App.Services;

/// <summary>
/// Maps the font of the settings to the font family the interface text uses.
/// </summary>
public static class FontResolver
{
    /// <summary>
    /// Gets the font family of a setting, written as a list of families for <c>FontFamily.Parse</c>: the chosen family, or
    /// the usual font of the system when none is chosen, and a Chinese font of the system for the characters it does not
    /// have, so that no character is left to a font the system picks.
    /// </summary>
    /// <param name="family">
    /// The chosen family name, or an empty text for the font of the system.
    /// </param>
    /// <param name="isWindows">
    /// Whether the app runs on Windows.
    /// </param>
    /// <param name="isMacOs">
    /// Whether the app runs on macOS. When neither this nor <paramref name="isWindows"/> is set, Linux is assumed.
    /// </param>
    /// <returns>
    /// The font families, the first one that has a character wins.
    /// </returns>
    public static string Resolve(string? family, bool isWindows, bool isMacOs)
    {
        // A comma would split the name into two families.
        var chosen = family?.Replace(",", string.Empty, StringComparison.Ordinal).Trim();
        return string.IsNullOrEmpty(chosen)
            ? $"{Latin(isWindows, isMacOs)}, {Chinese(isWindows, isMacOs)}"
            : $"{chosen}, {Chinese(isWindows, isMacOs)}";
    }

    /// <summary>
    /// Gets the font family of a setting on the system the app runs on.
    /// </summary>
    /// <param name="family">
    /// The chosen family name, or an empty text for the font of the system.
    /// </param>
    /// <returns>
    /// The font families.
    /// </returns>
    public static string Resolve(string? family) => Resolve(family, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());

    /// <summary>
    /// Gets the Chinese font of a system, for the characters the other font does not have.
    /// </summary>
    /// <param name="isWindows">
    /// Whether the app runs on Windows.
    /// </param>
    /// <param name="isMacOs">
    /// Whether the app runs on macOS.
    /// </param>
    /// <returns>
    /// The font family.
    /// </returns>
    private static string Chinese(bool isWindows, bool isMacOs) =>
        isWindows ? "Microsoft YaHei UI" : isMacOs ? "PingFang SC" : "Noto Sans CJK SC";

    /// <summary>
    /// Gets the default Latin font of a system.
    /// </summary>
    /// <param name="isWindows">
    /// Whether the app runs on Windows.
    /// </param>
    /// <param name="isMacOs">
    /// Whether the app runs on macOS.
    /// </param>
    /// <returns>
    /// The font family.
    /// </returns>
    private static string Latin(bool isWindows, bool isMacOs) =>
        isWindows ? "Segoe UI" : isMacOs ? "Helvetica Neue" : "Noto Sans";
}
