namespace FluxTick.App.Services;

/// <summary>
/// Decides which text may be typed into a box for a real number.
/// </summary>
public static class NumberInput
{
    /// <summary>
    /// Determines whether a text is a non-negative real number, or the beginning of one: digits and at most one decimal
    /// point. The empty text counts, because every number starts with it.
    /// </summary>
    /// <param name="text">
    /// The text the box would show.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the box may show the text.
    /// </returns>
    public static bool IsPartialNumber(string text)
    {
        var points = 0;
        foreach (var c in text)
        {
            if (c == '.')
            {
                points++;
            }
            else if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return points <= 1;
    }
}
