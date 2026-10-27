using System.Globalization;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;

namespace FluxTick.Providers.Sina;

/// <summary>
/// Pure functions that build and parse payloads of the Sina quote and search services.
/// </summary>
internal static class SinaParser
{
    /// <summary>
    /// The text every price line starts with before the code.
    /// </summary>
    private const string LinePrefix = "var hq_str_";

    /// <summary>
    /// The index of the previous close in a price line.
    /// </summary>
    private const int PreviousCloseField = 2;

    /// <summary>
    /// The index of the last price in a price line.
    /// </summary>
    private const int PriceField = 3;

    /// <summary>
    /// The index of the quote date in a price line.
    /// </summary>
    private const int DateField = 30;

    /// <summary>
    /// The index of the quote time in a price line.
    /// </summary>
    private const int TimeField = 31;

    /// <summary>
    /// The offset of Beijing time, which does not observe daylight saving.
    /// </summary>
    private static readonly TimeSpan BeijingOffset = TimeSpan.FromHours(8);

    /// <summary>
    /// The markets whose instruments this provider supports.
    /// </summary>
    private static readonly string[] Markets = ["sh", "sz", "bj"];

    /// <summary>
    /// Brings a symbol into the form the quote service expects, so a user may type <c>SH600000</c>.
    /// </summary>
    /// <param name="symbol">
    /// The symbol as stored or typed.
    /// </param>
    /// <returns>
    /// The code to request: a lower-case market followed by the code.
    /// </returns>
    public static string ToRequestCode(string symbol) => symbol.Trim().ToLowerInvariant();

    /// <summary>
    /// Parses the price lines of a quote service response. Lines for unknown codes are skipped.
    /// </summary>
    /// <param name="text">
    /// The decoded response, one <c>var hq_str_code="field,field,…";</c> line per instrument.
    /// </param>
    /// <returns>
    /// The quotes found.
    /// </returns>
    public static IReadOnlyList<ParsedQuote> ParseQuotes(string text)
    {
        var quotes = new List<ParsedQuote>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith(LinePrefix, StringComparison.Ordinal) || !QuoteText.TryGetQuotedBody(line, out var equals, out var body))
            {
                continue;
            }

            // Unknown codes answer with an empty body.
            var key = line[LinePrefix.Length..equals].ToLowerInvariant();
            var fields = body.Split(',');
            if (fields.Length <= PriceField)
            {
                continue;
            }

            decimal? previous = QuoteText.TryParsePrice(fields[PreviousCloseField], out var prior) && prior > 0m ? prior : null;

            var time = fields.Length > TimeField ? ParseTime(fields[DateField], fields[TimeField]) : null;

            // Before the opening auction, and for a suspended stock, the price is zero; the previous close is then the
            // price still in force. It is only published with the time the line carries, which tells the app how old
            // it is; without one it would look current, so the line is skipped.
            if (QuoteText.TryParsePrice(fields[PriceField], out var last) && last > 0m)
            {
                quotes.Add(new ParsedQuote(key, last, previous, time));
            }
            else if (previous is { } close && time is not null)
            {
                quotes.Add(new ParsedQuote(key, close, previous, time));
            }
        }

        return quotes;
    }

    /// <summary>
    /// Parses a symbol search response.
    /// </summary>
    /// <param name="text">
    /// The response, <c>var suggestvalue="name,type,code,symbol,…;…"</c>.
    /// </param>
    /// <param name="providerId">
    /// The provider id to stamp on the results.
    /// </param>
    /// <returns>
    /// The matching stocks and indices.
    /// </returns>
    public static IReadOnlyList<Asset> ParseSearch(string text, string providerId)
    {
        if (!QuoteText.TryGetQuotedBody(text, out _, out var body))
        {
            return [];
        }

        var assets = new List<Asset>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in body.Split(';'))
        {
            var parts = entry.Split(',');
            if (parts.Length < 5)
            {
                continue;
            }

            var symbol = ToRequestCode(parts[3]);
            if (!Markets.Any(m => symbol.StartsWith(m, StringComparison.Ordinal)) || !seen.Add(symbol))
            {
                continue;
            }

            var name = parts[4].Length > 0 ? parts[4] : parts[0];
            assets.Add(new Asset(providerId, symbol, name.Length > 0 ? name : symbol, IsIndex(symbol) ? AssetClass.Index : AssetClass.Equity));
        }

        return assets;
    }

    /// <summary>
    /// Tells whether a symbol is one of the exchange indices. Indices share the number range of stocks on the other
    /// exchange (<c>sh000001</c> is the Shanghai Composite, <c>sz000001</c> a bank), so the prefix decides: Shanghai
    /// 000, Shenzhen 399 and Beijing 899.
    /// </summary>
    /// <param name="symbol">
    /// The symbol in lower case.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for the indices of the three exchanges.
    /// </returns>
    private static bool IsIndex(string symbol) =>
        symbol.StartsWith("sh000", StringComparison.Ordinal)
        || symbol.StartsWith("sz399", StringComparison.Ordinal)
        || symbol.StartsWith("bj899", StringComparison.Ordinal);

    /// <summary>
    /// Parses the quote time of a line, which is Beijing time.
    /// </summary>
    /// <param name="date">
    /// The date field, <c>yyyy-MM-dd</c>.
    /// </param>
    /// <param name="time">
    /// The time field, <c>HH:mm:ss</c>.
    /// </param>
    /// <returns>
    /// The time, or <see langword="null"/> when it cannot be read.
    /// </returns>
    private static DateTimeOffset? ParseTime(string date, string time)
    {
        // Parsed as UTC only to get the clock reading; the offset is replaced by Beijing's below.
        return DateTimeOffset.TryParseExact($"{date} {time}", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var wallClock)
            ? new DateTimeOffset(wallClock.Ticks, BeijingOffset)
            : null;
    }
}
