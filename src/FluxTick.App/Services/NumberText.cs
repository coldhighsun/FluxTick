using System.Globalization;

namespace FluxTick.App.Services;

/// <summary>
/// Writes, reads and checks the numbers the user types in forms, such as prices and quantities. What may be typed
/// (<see cref="IsPartialNumber"/>), what is read (<see cref="TryParse"/>) and what is written (<see cref="Format"/>) take
/// their separators from the same culture, so that they agree.
/// </summary>
public static class NumberText
{
    /// <summary>
    /// Writes a number the way the current culture types it: up to twelve decimals, no trailing zeros, no thousands
    /// separator. <see cref="TryParse"/> reads the text back to the same number.
    /// </summary>
    /// <param name="value">
    /// The number.
    /// </param>
    /// <returns>
    /// The text.
    /// </returns>
    public static string Format(decimal value) => value.ToString("0.############", CultureInfo.CurrentCulture);

    /// <summary>
    /// Determines whether a text may be shown by a box for a real number: a non-negative number, or the beginning of one,
    /// made of digits and at most one decimal separator, which is the one of the current culture or the point. The empty
    /// text counts, because every number starts with it. Thousands separators and signs cannot be typed.
    /// </summary>
    /// <param name="text">
    /// The text the box would show.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the box may show the text.
    /// </returns>
    public static bool IsPartialNumber(string text)
    {
        var separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        var normalized = separator is "." or "" ? text : text.Replace(separator, ".", StringComparison.Ordinal);
        var separators = 0;
        foreach (var c in normalized)
        {
            if (c == '.')
            {
                separators++;
            }
            else if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return separators <= 1;
    }

    /// <summary>
    /// Determines whether a text may be shown by a box for a whole number: digits, and a leading minus sign when negative
    /// numbers are allowed. The empty text counts.
    /// </summary>
    /// <param name="text">
    /// The text the box would show.
    /// </param>
    /// <param name="allowNegative">
    /// Whether a leading minus sign may be typed.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the box may show the text.
    /// </returns>
    public static bool IsPartialWholeNumber(string text, bool allowNegative)
    {
        var digits = allowNegative && text.StartsWith('-') ? text.AsSpan(1) : text.AsSpan();
        foreach (var c in digits)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Determines whether a text may be shown by a box for a number of seconds as the settings store it: digits and at most
    /// one point, whatever the culture, because the settings are read with the point. The empty text counts.
    /// </summary>
    /// <param name="text">
    /// The text the box would show.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the box may show the text.
    /// </returns>
    public static bool IsPartialSeconds(string text)
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

    /// <summary>
    /// Parses a typed number. In this order: a number as the current culture writes it (<c>12,5</c> is 12.5 where the
    /// comma is the decimal separator); thousands as the current culture groups them, in threes (<c>1.000</c> is a
    /// thousand where the point is the group separator, and <c>1,5</c> is no number where the comma is); a number with
    /// the point as the decimal separator (<c>12.5</c> is 12.5 everywhere).
    /// </summary>
    /// <param name="text">
    /// The typed text.
    /// </param>
    /// <param name="value">
    /// The number.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the text is a number.
    /// </returns>
    public static bool TryParse(string? text, out decimal value)
    {
        var trimmed = text?.Trim();
        const NumberStyles plain = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        return decimal.TryParse(trimmed, plain, CultureInfo.CurrentCulture, out value)
            || (IsGrouped(trimmed, CultureInfo.CurrentCulture.NumberFormat) && decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.CurrentCulture, out value))
            || decimal.TryParse(trimmed, plain, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Determines whether a text writes its thousands the way a culture does: groups of three digits after the first
    /// (which is one to three digits and does not start with 0, so <c>0.500</c> is half and not five hundred), so that
    /// <c>1,000</c> is a thousand and <c>1,5</c> is not a number. The sign, the group separator and the decimal
    /// separator are those of the culture.
    /// </summary>
    /// <param name="text">
    /// The typed text.
    /// </param>
    /// <param name="format">
    /// The number format of the culture.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the text is grouped in threes.
    /// </returns>
    private static bool IsGrouped(string? text, NumberFormatInfo format)
    {
        var group = format.NumberGroupSeparator;
        if (text is null || group.Length == 0)
        {
            return false;
        }

        var rest = text.AsSpan();
        if (rest.StartsWith(format.NegativeSign, StringComparison.Ordinal))
        {
            rest = rest[format.NegativeSign.Length..];
        }
        else if (rest.StartsWith(format.PositiveSign, StringComparison.Ordinal))
        {
            rest = rest[format.PositiveSign.Length..];
        }

        // What follows the decimal separator must be digits only.
        var decimalAt = rest.IndexOf(format.NumberDecimalSeparator, StringComparison.Ordinal);
        if (decimalAt >= 0)
        {
            var fraction = rest[(decimalAt + format.NumberDecimalSeparator.Length)..];
            if (fraction.IsEmpty || !IsDigits(fraction))
            {
                return false;
            }

            rest = rest[..decimalAt];
        }

        // One to three digits, then any number of the separator followed by exactly three digits.
        var firstGroup = rest.IndexOf(group, StringComparison.Ordinal);
        if (firstGroup is < 1 or > 3 || !IsDigits(rest[..firstGroup]) || rest[0] == '0')
        {
            return false;
        }

        rest = rest[firstGroup..];
        while (!rest.IsEmpty)
        {
            if (!rest.StartsWith(group, StringComparison.Ordinal))
            {
                return false;
            }

            rest = rest[group.Length..];
            if (rest.Length < 3 || !IsDigits(rest[..3]))
            {
                return false;
            }

            rest = rest[3..];
        }

        return true;
    }

    /// <summary>
    /// Determines whether a text is made of the digits 0 to 9 only.
    /// </summary>
    /// <param name="text">
    /// The text.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when every character is a digit.
    /// </returns>
    private static bool IsDigits(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
