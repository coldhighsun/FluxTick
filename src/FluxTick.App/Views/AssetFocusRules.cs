namespace FluxTick.App.Views;

/// <summary>
/// Sizes the text of the screen-filling view of one instrument from the height of the window, so that it reads the same on
/// a laptop and on a large monitor.
/// </summary>
internal static class AssetFocusRules
{
    /// <summary>
    /// Gets the smallest height the text is sized for; a shorter window is treated as this tall.
    /// </summary>
    public const double MinHeight = 480;

    /// <summary>
    /// Gets the font size of the price.
    /// </summary>
    /// <param name="height">The height of the window.</param>
    /// <returns>The font size.</returns>
    public static double PriceFontSize(double height) => Scaled(height, 0.14, 48, 200);

    /// <summary>
    /// Gets the font size of the change beside the price.
    /// </summary>
    /// <param name="height">The height of the window.</param>
    /// <returns>The font size.</returns>
    public static double ChangeFontSize(double height) => Scaled(height, 0.05, 20, 72);

    /// <summary>
    /// Gets the font size of the name of the instrument.
    /// </summary>
    /// <param name="height">The height of the window.</param>
    /// <returns>The font size.</returns>
    public static double NameFontSize(double height) => Scaled(height, 0.04, 18, 48);

    /// <summary>
    /// Gets the font size of the small figures (range, cost, profit or loss).
    /// </summary>
    /// <param name="height">The height of the window.</param>
    /// <returns>The font size.</returns>
    public static double DetailFontSize(double height) => Scaled(height, 0.024, 13, 30);

    /// <summary>
    /// Takes a share of the window height, kept between a floor and a ceiling.
    /// </summary>
    /// <param name="height">The height of the window.</param>
    /// <param name="share">The share of the height.</param>
    /// <param name="min">The smallest result.</param>
    /// <param name="max">The largest result.</param>
    /// <returns>The scaled value, rounded to whole units.</returns>
    private static double Scaled(double height, double share, double min, double max)
    {
        var basis = double.IsNaN(height) ? MinHeight : Math.Max(height, MinHeight);
        return Math.Round(Math.Clamp(basis * share, min, max));
    }
}
