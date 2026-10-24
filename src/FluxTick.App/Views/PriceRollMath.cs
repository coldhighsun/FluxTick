namespace FluxTick.App.Views;

/// <summary>
/// The distances of the roll of a price: the old price slides out of its box and the new one slides in from the other
/// side, so the direction of the roll tells whether the price rose or fell.
/// </summary>
internal static class PriceRollMath
{
    /// <summary>
    /// How far the numbers travel, as a share of the height of the line of the price.
    /// </summary>
    public const double Share = 0.6;

    /// <summary>
    /// Computes where the numbers start and end.
    /// </summary>
    /// <param name="up">
    /// Whether the price rose.
    /// </param>
    /// <param name="lineHeight">
    /// The height of the line of the price.
    /// </param>
    /// <returns>
    /// The vertical offset the old price ends at, and the one the new price starts from; the new price ends at 0 and the
    /// old one starts there. When the price rose the old one moves up (negative) and the new one comes from below
    /// (positive); when it fell, the other way round.
    /// </returns>
    public static (double OldTo, double NewFrom) Offsets(bool up, double lineHeight)
    {
        var distance = lineHeight * Share;
        return up ? (-distance, distance) : (distance, -distance);
    }
}
