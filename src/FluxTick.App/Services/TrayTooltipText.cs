namespace FluxTick.App.Services;

/// <summary>
/// Builds the tooltip of the tray icon.
/// </summary>
public static class TrayTooltipText
{
    /// <summary>
    /// The most characters a tray icon tooltip can show on Windows; a longer text is cut off by the system.
    /// </summary>
    public const int MaxLength = 127;

    /// <summary>
    /// Composes the tooltip: the name of the app, then the total of the holdings if there is one, otherwise the slogan.
    /// </summary>
    /// <param name="portfolio">
    /// The total of the holdings, or an empty text for none.
    /// </param>
    /// <param name="tagline">
    /// The slogan of the app.
    /// </param>
    /// <returns>
    /// The tooltip, ending in an ellipsis when it had to be shortened.
    /// </returns>
    public static string Compose(string? portfolio, string tagline)
    {
        var text = $"FluxTick — {(string.IsNullOrEmpty(portfolio) ? tagline : portfolio)}";
        return text.Length <= MaxLength ? text : $"{text[..(MaxLength - 1)]}…";
    }
}
