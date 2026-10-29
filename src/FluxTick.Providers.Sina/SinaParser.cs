using System.Globalization;
using System.Text.Json;
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
    /// The prefix of the codes of currency pairs, such as <c>fx_seurusd</c>.
    /// </summary>
    public const string ForexPrefix = "fx_";

    /// <summary>
    /// The prefix of the codes of international commodity futures, such as <c>hf_GC</c>.
    /// </summary>
    public const string FuturesPrefix = "hf_";

    /// <summary>
    /// Where the lines of the stocks and indices keep what the app shows.
    /// </summary>
    private static readonly FieldLayout StockLayout = new(PreviousClose: 2, Price: 3, Date: 30, Time: 31);

    /// <summary>
    /// Where the lines of the currency pairs keep what the app shows; the price is the last one, not the bid or the ask.
    /// </summary>
    private static readonly FieldLayout ForexLayout = new(PreviousClose: 3, Price: 8, Date: 17, Time: 0);

    /// <summary>
    /// Where the lines of the commodity futures keep what the app shows; the previous close is the previous settlement.
    /// </summary>
    private static readonly FieldLayout FuturesLayout = new(PreviousClose: 7, Price: 0, Date: 12, Time: 6);

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
    /// The code to request: a lower-case market followed by the code, a lower-case currency pair (<c>fx_seurusd</c>) or
    /// the upper-case code of a future (<c>hf_GC</c>).
    /// </returns>
    public static string ToRequestCode(string symbol)
    {
        var code = symbol.Trim();
        return code.StartsWith(FuturesPrefix, StringComparison.OrdinalIgnoreCase)
            ? FuturesPrefix + code[FuturesPrefix.Length..].ToUpperInvariant()
            : code.ToLowerInvariant();
    }

    /// <summary>
    /// Tells which kind of instrument a code is by its prefix.
    /// </summary>
    /// <param name="code">
    /// The code.
    /// </param>
    /// <returns>
    /// <see cref="AssetClass.Forex"/> or <see cref="AssetClass.Commodity"/> for the codes of currency pairs and futures;
    /// <see langword="null"/> for everything else.
    /// </returns>
    public static AssetClass? KindOf(string code) =>
        code.StartsWith(ForexPrefix, StringComparison.OrdinalIgnoreCase) ? AssetClass.Forex
        : code.StartsWith(FuturesPrefix, StringComparison.OrdinalIgnoreCase) ? AssetClass.Commodity
        : null;

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
            if (TryParseBody(key, body) is { } quote)
            {
                quotes.Add(quote);
            }
        }

        return quotes;
    }

    /// <summary>
    /// Parses the lines the stream pushes. They carry the same fields as the lines of the quote service, but without the
    /// <c>var hq_str_</c> prefix and the quotes: <c>code=field,field,…</c>. Lines for unknown codes are skipped.
    /// </summary>
    /// <param name="text">
    /// One pushed message, one line per instrument.
    /// </param>
    /// <returns>
    /// The quotes found.
    /// </returns>
    public static IReadOnlyList<ParsedQuote> ParseStream(string text)
    {
        var quotes = new List<ParsedQuote>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd(';');
            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var key = line[..equals].ToLowerInvariant();
            if (TryParseBody(key, line[(equals + 1)..].Trim('"')) is { } quote)
            {
                quotes.Add(quote);
            }
        }

        return quotes;
    }

    /// <summary>
    /// Parses the fields of one instrument, which are the same in the quote service and in the stream.
    /// </summary>
    /// <param name="key">
    /// The code of the instrument.
    /// </param>
    /// <param name="body">
    /// The comma-separated fields.
    /// </param>
    /// <returns>
    /// The quote, or <see langword="null"/> when the fields carry no price that can be shown.
    /// </returns>
    private static ParsedQuote? TryParseBody(string key, string body)
    {
        var layout = KindOf(key) switch
        {
            AssetClass.Forex => ForexLayout,
            AssetClass.Commodity => FuturesLayout,
            _ => StockLayout,
        };
        var fields = body.Split(',');
        if (fields.Length <= Math.Max(layout.Price, layout.PreviousClose))
        {
            return null;
        }

        decimal? previous = QuoteText.TryParsePrice(fields[layout.PreviousClose], out var prior) && prior > 0m ? prior : null;

        var time = fields.Length > Math.Max(layout.Date, layout.Time) ? ParseTime(fields[layout.Date], fields[layout.Time]) : null;

        // Before the opening auction, and for a suspended stock, the price is zero; the previous close is then the
        // price still in force. It is only published with the time the line carries, which tells the app how old
        // it is; without one it would look current, so the line is skipped.
        if (QuoteText.TryParsePrice(fields[layout.Price], out var last) && last > 0m)
        {
            return new ParsedQuote(key, last, previous, time);
        }

        return previous is { } close && time is not null ? new ParsedQuote(key, close, previous, time) : null;
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
    /// Tells whether the minute history service has candles for a code: the A-share stocks and funds, whose candles come
    /// from the stock service, and the currency pairs, whose candles come from the currency service. The service has none
    /// for the exchange indices and the code of a future is not known to either.
    /// </summary>
    /// <param name="code">
    /// The code as requested, in lower case.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the code has minute history.
    /// </returns>
    public static bool HasMinuteHistory(string code) =>
        IsForex(code) || (Markets.Any(m => QuoteText.HasPrefix(code, m)) && !IsIndex(code));

    /// <summary>
    /// Tells whether a code is a currency pair.
    /// </summary>
    /// <param name="code">
    /// The code.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for the codes that start with <c>fx_</c>.
    /// </returns>
    public static bool IsForex(string code) => KindOf(code) == AssetClass.Forex;

    /// <summary>
    /// Parses the minute candles of an A-share stock or fund, <c>[{"day":"2026-10-29 10:30:00","close":"228.030",…},…]</c>.
    /// </summary>
    /// <param name="text">
    /// The response body.
    /// </param>
    /// <returns>
    /// The close of each candle at its time, oldest first; empty when the service has no candles.
    /// </returns>
    public static IReadOnlyList<PricePoint> ParseStockMinutes(string text) => ParseMinutes(text, "day", "close");

    /// <summary>
    /// Parses the minute candles of a currency pair, <c>var([{"d":"2026-10-29 10:32:00","c":5.2029,…},…]);</c> after a
    /// line of script that is of no use.
    /// </summary>
    /// <param name="text">
    /// The response body.
    /// </param>
    /// <returns>
    /// The close of each candle at its time, oldest first; empty when the service has no candles.
    /// </returns>
    public static IReadOnlyList<PricePoint> ParseForexMinutes(string text) => ParseMinutes(text, "d", "c");

    /// <summary>
    /// Reads the array of candles in a body, whatever surrounds it. Candles without a readable time or a positive close are
    /// skipped.
    /// </summary>
    /// <param name="text">
    /// The response body.
    /// </param>
    /// <param name="timeName">
    /// The name of the property with the time of a candle, Beijing time.
    /// </param>
    /// <param name="closeName">
    /// The name of the property with the close of a candle.
    /// </param>
    /// <returns>
    /// The points, in the order of the candles.
    /// </returns>
    private static IReadOnlyList<PricePoint> ParseMinutes(string text, string timeName, string closeName)
    {
        var start = text.IndexOf("[{", StringComparison.Ordinal);
        var end = text.LastIndexOf(']');
        if (start < 0 || end < start)
        {
            return [];
        }

        using var document = JsonDocument.Parse(text[start..(end + 1)]);
        var points = new List<PricePoint>();
        foreach (var candle in document.RootElement.EnumerateArray())
        {
            if (candle.ValueKind == JsonValueKind.Object
                && candle.TryGetProperty(timeName, out var day)
                && day.ValueKind == JsonValueKind.String
                && ParseCandleTime(day.GetString()) is { } time
                && JsonValues.TryGetDecimal(candle, closeName, out var close)
                && close > 0m)
            {
                points.Add(new PricePoint(time, close));
            }
        }

        return points;
    }

    /// <summary>
    /// Parses the time of a candle, <c>yyyy-MM-dd HH:mm:ss</c> in Beijing time. The service labels a candle with the end of
    /// its minute, so the candle that is still under way carries a time that has not come yet.
    /// </summary>
    /// <param name="text">
    /// The text.
    /// </param>
    /// <returns>
    /// The time, or <see langword="null"/> when it cannot be read.
    /// </returns>
    private static DateTimeOffset? ParseCandleTime(string? text) =>
        text?.Split(' ') is [var date, var time] ? ParseTime(date, time) : null;

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

    /// <summary>
    /// The positions of the fields the app shows in the line of a kind of instrument.
    /// </summary>
    /// <param name="PreviousClose">
    /// The index of the previous close.
    /// </param>
    /// <param name="Price">
    /// The index of the last price.
    /// </param>
    /// <param name="Date">
    /// The index of the quote date.
    /// </param>
    /// <param name="Time">
    /// The index of the quote time.
    /// </param>
    private readonly record struct FieldLayout(int PreviousClose, int Price, int Date, int Time);
}
