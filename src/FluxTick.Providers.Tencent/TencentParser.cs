using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluxTick.Abstractions;

namespace FluxTick.Providers.Tencent;

/// <summary>
/// A price line of the Tencent quote service.
/// </summary>
/// <param name="Key">
/// The code the line answers, in the casing it was requested with.
/// </param>
/// <param name="Price">
/// The last price.
/// </param>
/// <param name="PreviousClose">
/// The previous close, or <see langword="null"/> when unknown.
/// </param>
/// <param name="Time">
/// The quote time, or <see langword="null"/> when the line carries none that can be read.
/// </param>
internal sealed record TencentQuote(string Key, decimal Price, decimal? PreviousClose, DateTimeOffset? Time);

/// <summary>
/// Pure functions that build and parse payloads of the Tencent quote, search and history services.
/// </summary>
internal static partial class TencentParser
{
    /// <summary>
    /// The markets whose instruments this provider supports.
    /// </summary>
    private static readonly HashSet<string> Markets = new(["sh", "sz", "bj", "hk", "us"], StringComparer.Ordinal);

    /// <summary>
    /// The offset of Beijing and Hong Kong time, neither of which observes daylight saving.
    /// </summary>
    private static readonly TimeSpan AsiaOffset = TimeSpan.FromHours(8);

    /// <summary>
    /// The time formats the services use, depending on the market.
    /// </summary>
    private static readonly string[] TimeFormats = ["yyyyMMddHHmmss", "yyyy/MM/dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss"];

    /// <summary>
    /// The market id Tencent gives US instruments.
    /// </summary>
    private const string UsMarketId = "200";

    /// <summary>
    /// The number of one-minute samples kept for a sparkline.
    /// </summary>
    private const int HistoryLength = 60;

    /// <summary>
    /// The US Eastern time zone, looked up once; <see langword="null"/> when the system knows neither id.
    /// </summary>
    private static readonly Lazy<TimeZoneInfo?> EasternTime = new(FindEasternTime);

    /// <summary>
    /// The quote services answer in GBK.
    /// </summary>
    private static readonly Lazy<Encoding> Gbk = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936);
    });

    /// <summary>
    /// Decodes a quote service response.
    /// </summary>
    /// <param name="bytes">
    /// The response body.
    /// </param>
    /// <returns>
    /// The text.
    /// </returns>
    public static string DecodeGbk(byte[] bytes) => Gbk.Value.GetString(bytes);

    /// <summary>
    /// Builds the canonical symbol of an instrument from the market and code a search result carries. The symbol is
    /// also the code the quote service accepts: a lower-case market followed by the code, which for Hong Kong and US
    /// instruments is upper-case and for US instruments has no exchange suffix.
    /// </summary>
    /// <param name="market">
    /// The market, for example <c>sh</c> or <c>us</c>.
    /// </param>
    /// <param name="code">
    /// The code, for example <c>600000</c> or <c>aapl.oq</c>.
    /// </param>
    /// <returns>
    /// The symbol, for example <c>sh600000</c> or <c>usAAPL</c>.
    /// </returns>
    public static string ToSymbol(string market, string code)
    {
        var prefix = market.ToLowerInvariant();
        return prefix switch
        {
            "us" => prefix + StripExchange(code).ToUpperInvariant(),
            "hk" => prefix + code.ToUpperInvariant(),
            _ => prefix + code.ToLowerInvariant(),
        };
    }

    /// <summary>
    /// Brings a symbol into the form the quote service expects, so a user may type <c>SH600000</c> or <c>usaapl</c>.
    /// </summary>
    /// <param name="symbol">
    /// The symbol as stored or typed.
    /// </param>
    /// <returns>
    /// The code to request.
    /// </returns>
    public static string ToRequestCode(string symbol)
    {
        var text = symbol.Trim();
        return text.Length > 2 ? ToSymbol(text[..2], text[2..]) : text;
    }

    /// <summary>
    /// Parses the price lines of a quote service response. Lines for unknown codes are skipped.
    /// </summary>
    /// <param name="text">
    /// The decoded response, one <c>v_code="field~field~…";</c> line per instrument.
    /// </param>
    /// <returns>
    /// The quotes found.
    /// </returns>
    public static IReadOnlyList<TencentQuote> ParseQuotes(string text)
    {
        var quotes = new List<TencentQuote>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var equals = line.IndexOf("=\"", StringComparison.Ordinal);
            var end = line.LastIndexOf('"');
            if (!line.StartsWith("v_", StringComparison.Ordinal) || equals < 0 || end <= equals + 1)
            {
                continue;
            }

            var key = line[2..equals];
            var fields = line[(equals + 2)..end].Split('~');

            // Field 3 is the last price and field 4 the previous close. Unknown codes answer with a one-field body.
            if (fields.Length < 5 || !TryParsePrice(fields[3], out var price) || price <= 0m)
            {
                continue;
            }

            decimal? previous = TryParsePrice(fields[4], out var close) && close > 0m ? close : null;
            var time = fields.Length > 30 ? ParseTime(fields[0], fields[30]) : null;
            quotes.Add(new TencentQuote(key, price, previous, time));
        }

        return quotes;
    }

    /// <summary>
    /// Parses a symbol search response.
    /// </summary>
    /// <param name="text">
    /// The response, <c>v_hint="market~code~name~pinyin~type^…"</c> with names as <c>\uXXXX</c> escapes.
    /// </param>
    /// <param name="providerId">
    /// The provider id to stamp on the results.
    /// </param>
    /// <returns>
    /// The matching stocks, ETFs and indices.
    /// </returns>
    public static IReadOnlyList<Asset> ParseSearch(string text, string providerId)
    {
        var start = text.IndexOf("=\"", StringComparison.Ordinal);
        var end = text.LastIndexOf('"');
        if (start < 0 || end <= start + 1)
        {
            return [];
        }

        var assets = new List<Asset>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in text[(start + 2)..end].Split('^'))
        {
            var parts = entry.Split('~');
            if (parts.Length < 5 || !Markets.Contains(parts[0]) || parts[1].Length == 0)
            {
                continue;
            }

            AssetClass? assetClass = parts[4] switch
            {
                "ZS" => AssetClass.Index,
                "GP" or "GP-A" or "GP-B" or "ETF" or "LOF" => AssetClass.Equity,
                _ => null,
            };
            var symbol = ToSymbol(parts[0], parts[1]);
            if (assetClass is null || !seen.Add(symbol))
            {
                continue;
            }

            var name = Unescape(parts[2]);
            assets.Add(new Asset(providerId, symbol, name.Length > 0 ? name : symbol, assetClass.Value));
        }

        return assets;
    }

    /// <summary>
    /// Tells whether the minute-history service has a full-day series for a code. For US codes it answers with a single
    /// latest point and no date, so a request would only be wasted.
    /// </summary>
    /// <param name="code">
    /// The code as requested, for example <c>sh600000</c> or <c>usAAPL</c>.
    /// </param>
    /// <returns>
    /// <see langword="false"/> for US codes.
    /// </returns>
    public static bool HasMinuteHistory(string code) => !code.StartsWith("us", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses a minute-history response of a Chinese or Hong Kong code; the rows carry UTC+8 clock times.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <param name="code">
    /// The code that was requested.
    /// </param>
    /// <returns>
    /// The most recent samples, oldest first; empty when the service has no full-day series for the code.
    /// </returns>
    public static IReadOnlyList<PricePoint> ParseMinuteHistory(string json, string code)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty(code, out var entry)
            || !entry.TryGetProperty("data", out var series)
            || !series.TryGetProperty("date", out var dateElement)
            || !series.TryGetProperty("data", out var rows)
            || rows.ValueKind != JsonValueKind.Array
            || !DateTimeOffset.TryParseExact(dateElement.GetString(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        {
            return [];
        }

        // Each row reads "HHmm price volume turnover" in Beijing or Hong Kong time.
        var points = new List<PricePoint>();
        foreach (var row in rows.EnumerateArray())
        {
            var fields = row.GetString()?.Split(' ');
            if (fields is { Length: > 1 }
                && DateTimeOffset.TryParseExact(fields[0], "HHmm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var clock)
                && TryParsePrice(fields[1], out var price))
            {
                points.Add(new PricePoint(new DateTimeOffset(date.Ticks + clock.TimeOfDay.Ticks, AsiaOffset), price));
            }
        }

        return points.Count > HistoryLength ? points.GetRange(points.Count - HistoryLength, HistoryLength) : points;
    }

    /// <summary>
    /// Drops the exchange suffix of a US code: <c>aapl.oq</c> becomes <c>aapl</c> and <c>brk.b.n</c> becomes
    /// <c>brk.b</c>. Index codes such as <c>.inx</c> are kept.
    /// </summary>
    /// <param name="code">
    /// The code a search result carries.
    /// </param>
    /// <returns>
    /// The code without exchange suffix.
    /// </returns>
    private static string StripExchange(string code)
    {
        if (code.StartsWith('.'))
        {
            return code;
        }

        var dot = code.LastIndexOf('.');
        return dot > 0 ? code[..dot] : code;
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
    private static bool TryParsePrice(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// Parses the quote time of a line. Chinese and Hong Kong markets quote in UTC+8, US markets in Eastern time.
    /// </summary>
    /// <param name="marketId">
    /// The numeric market id in the first field.
    /// </param>
    /// <param name="text">
    /// The time field.
    /// </param>
    /// <returns>
    /// The time, or <see langword="null"/> when it cannot be read.
    /// </returns>
    private static DateTimeOffset? ParseTime(string marketId, string text)
    {
        // Parsed as UTC only to get the clock reading; its own offset is replaced by the market's below.
        if (!DateTimeOffset.TryParseExact(text, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var wallClock))
        {
            return null;
        }

        if (marketId != UsMarketId)
        {
            return new DateTimeOffset(wallClock.Ticks, AsiaOffset);
        }

        if (EasternTime.Value is not { } zone)
        {
            return null;
        }

        // The offset depends on the instant, which depends on the offset: take the offset at the wall-clock time read as
        // UTC, then the offset at the instant that gives. They differ only within hours of a clock change.
        var estimate = zone.GetUtcOffset(wallClock);
        return new DateTimeOffset(wallClock.Ticks, zone.GetUtcOffset(new DateTimeOffset(wallClock.Ticks, estimate)));
    }

    /// <summary>
    /// Finds the US Eastern time zone under either its IANA or its Windows id.
    /// </summary>
    /// <returns>
    /// The zone, or <see langword="null"/> when the system knows neither id.
    /// </returns>
    private static TimeZoneInfo? FindEasternTime()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the next id.
            }
            catch (InvalidTimeZoneException)
            {
                // Try the next id.
            }
        }

        return null;
    }

    /// <summary>
    /// Decodes <c>\uXXXX</c> escapes.
    /// </summary>
    /// <param name="text">
    /// The escaped text.
    /// </param>
    /// <returns>
    /// The plain text.
    /// </returns>
    private static string Unescape(string text) =>
        UnicodeEscape().Replace(text, m => ((char)int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());

    /// <summary>
    /// Matches one <c>\uXXXX</c> escape.
    /// </summary>
    /// <returns>
    /// The regular expression.
    /// </returns>
    [GeneratedRegex(@"\\u([0-9a-fA-F]{4})")]
    private static partial Regex UnicodeEscape();
}
