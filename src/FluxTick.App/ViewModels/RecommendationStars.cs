namespace FluxTick.App.ViewModels;

/// <summary>
/// Formats the recommendation score of a data source as stars.
/// </summary>
public static class RecommendationStars
{
    /// <summary>
    /// The number of stars of a full score.
    /// </summary>
    public const int MaxStars = 5;

    /// <summary>
    /// A filled star.
    /// </summary>
    private const char Filled = '★';

    /// <summary>
    /// Builds the stars of a score: one filled star per point and nothing for the missing ones, because empty stars are
    /// hard to tell from filled ones at the small size they are shown in.
    /// </summary>
    /// <param name="recommendation">
    /// The score, or <see langword="null"/> when the source is not rated.
    /// </param>
    /// <returns>
    /// The stars, for example <c>★★★★</c> for 4, or an empty string when there is no score.
    /// </returns>
    public static string Format(int? recommendation)
    {
        if (recommendation is not { } score)
        {
            return string.Empty;
        }

        return new string(Filled, Math.Clamp(score, 0, MaxStars));
    }
}
