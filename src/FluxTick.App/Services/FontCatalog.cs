using Avalonia.Media;

namespace FluxTick.App.Services;

/// <summary>
/// The font families installed on this computer.
/// </summary>
public static class FontCatalog
{
    /// <summary>
    /// Gets the names of the installed font families, sorted, without the ones the system hides: the names that start
    /// with <c>@</c> are the vertical variants of the East Asian fonts.
    /// </summary>
    /// <returns>
    /// The family names, each once.
    /// </returns>
    public static IReadOnlyList<string> GetFamilies() =>
        [.. FontManager.Current.SystemFonts
            .Select(f => f.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !name.StartsWith('@'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>
    /// Tells whether a font family is installed.
    /// </summary>
    /// <param name="name">
    /// The family name.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the system has the family.
    /// </returns>
    public static bool IsInstalled(string name) =>
        GetFamilies().Contains(name, StringComparer.OrdinalIgnoreCase);
}
