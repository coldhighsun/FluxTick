using System.Globalization;

namespace FluxTick.Providers.Common;

/// <summary>
/// Helpers for reading the <c>name="body"</c> assignments the Chinese quote services answer with.
/// </summary>
public static class QuoteText
{
    /// <summary>
    /// Tests whether a symbol starts with a prefix and has more after it, compared without regard to case, as the symbols
    /// of Tencent and Sina start with a market code (<c>sh600000</c>).
    /// </summary>
    /// <param name="symbol">
    /// The symbol.
    /// </param>
    /// <param name="prefix">
    /// The prefix.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when it does.
    /// </returns>
    public static bool HasPrefix(string symbol, string prefix) =>
        symbol.Length > prefix.Length && symbol.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Cuts the quoted body out of a <c>name="body";</c> assignment.
    /// </summary>
    /// <param name="text">
    /// One assignment, or a whole response that holds a single one.
    /// </param>
    /// <param name="nameEnd">
    /// The index of the <c>=</c>, so the text before it is the name; meaningful only when the method returns
    /// <see langword="true"/>.
    /// </param>
    /// <param name="body">
    /// The text between the first <c>="</c> and the last quote.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the text holds a non-empty quoted body.
    /// </returns>
    public static bool TryGetQuotedBody(string text, out int nameEnd, out string body)
    {
        nameEnd = text.IndexOf("=\"", StringComparison.Ordinal);
        var end = text.LastIndexOf('"');
        if (nameEnd < 0 || end <= nameEnd + 1)
        {
            body = string.Empty;
            return false;
        }

        body = text[(nameEnd + 2)..end];
        return true;
    }

    /// <summary>
    /// Parses a price field.
    /// </summary>
    /// <param name="text">
    /// The field text.
    /// </param>
    /// <param name="value">
    /// The parsed value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the text is a number.
    /// </returns>
    public static bool TryParsePrice(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
