using System.Globalization;
using System.Text.Json;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;

namespace FluxTick.Providers.Eastmoney;

/// <summary>
/// Pure functions that build and parse payloads of the Eastmoney quote, search and history services.
/// </summary>
internal static class EastmoneyParser
{
    /// <summary>
    /// The fixed token the public web pages of Eastmoney send to the quote and history services. It is not an account
    /// credential: every visitor of the site sends the same value.
    /// </summary>
    public const string PublicToken = "fa5fd1943c7b386f172d6893dbfba10b";

    /// <summary>
    /// The fixed token the public web pages of Eastmoney send to the search service; like <see cref="PublicToken"/> it is
    /// shared by all visitors.
    /// </summary>
    public const string SearchToken = "D43BF722C8E33BDC906FB84D85E326E8";

    /// <summary>
    /// The markets whose instruments this provider supports, by the market id Eastmoney gives them: Shenzhen, Shanghai,
    /// Hong Kong and the three US exchange groups.
    /// </summary>
    private static readonly HashSet<string> Markets = new(["0", "1", "116", "105", "106", "107"], StringComparer.Ordinal);

    /// <summary>
    /// The offset of Beijing and Hong Kong time, which the candles are dated in whatever the market; neither observes
    /// daylight saving.
    /// </summary>
    private static readonly TimeSpan AsiaOffset = TimeSpan.FromHours(8);

    /// <summary>
    /// Gets the code the quote service takes for a symbol, which is the symbol itself.
    /// </summary>
    /// <param name="symbol">
    /// The symbol, for example <c>1.600519</c>.
    /// </param>
    /// <returns>
    /// The code.
    /// </returns>
    public static string ToRequestCode(string symbol) => symbol.Trim();

    /// <summary>
    /// Finds the exchange of a symbol from its market id (<c>1.600519</c>, <c>116.00700</c>, <c>105.AAPL</c>).
    /// </summary>
    /// <param name="symbol">
    /// The symbol.
    /// </param>
    /// <returns>
    /// The exchange, or <see langword="null"/> when the market is not known.
    /// </returns>
    public static ExchangeHours? ExchangeOf(string symbol)
    {
        var dot = symbol.IndexOf('.');
        return dot <= 0 ? null : ExchangeOfMarket(symbol[..dot]);
    }

    /// <summary>
    /// Gives the currency an instrument is quoted in from its market id: Hong Kong dollars in Hong Kong, US dollars in the
    /// US and yuan on the mainland.
    /// </summary>
    /// <param name="symbol">
    /// The symbol.
    /// </param>
    /// <returns>
    /// The currency code, or <see langword="null"/> when the market is not known.
    /// </returns>
    public static string? CurrencyOf(string symbol)
    {
        var exchange = ExchangeOf(symbol);
        return exchange is null ? null
            : ReferenceEquals(exchange, ExchangeHours.HongKong) ? "HKD"
            : ReferenceEquals(exchange, ExchangeHours.Us) ? "USD"
            : "CNY";
    }

    /// <summary>
    /// Finds the exchange of a market id.
    /// </summary>
    /// <param name="market">
    /// The market id, for example <c>1</c>.
    /// </param>
    /// <returns>
    /// The exchange, or <see langword="null"/> when the market is not known.
    /// </returns>
    private static ExchangeHours? ExchangeOfMarket(string market) => market switch
    {
        "0" or "1" => ExchangeHours.ChinaA,
        "116" => ExchangeHours.HongKong,
        "105" or "106" or "107" => ExchangeHours.Us,
        _ => null,
    };

    /// <summary>
    /// Parses the answer of the batch quote service. Each entry is keyed by <c>market.code</c>, because a code alone is
    /// not unique: <c>000001</c> is both a Shenzhen stock and the Shanghai index.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The quotes that carry a price. A suspended instrument, whose price is a dash, is left out.
    /// </returns>
    public static IReadOnlyList<ParsedQuote> ParseQuotes(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("diff", out var diff) || diff.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var quotes = new List<ParsedQuote>();
        foreach (var item in diff.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("f12", out var codeElement) || codeElement.GetString() is not { Length: > 0 } code
                || !item.TryGetProperty("f13", out var marketElement) || !TryGetMarket(marketElement, out var market)
                || !JsonValues.TryGetDecimal(item, "f2", out var price) || price <= 0)
            {
                continue;
            }

            decimal? previous = JsonValues.TryGetPositiveDecimal(item, "f18", out var close) ? close : null;
            DateTimeOffset? time = item.TryGetProperty("f124", out var stamp) && stamp.ValueKind == JsonValueKind.Number && stamp.TryGetInt64(out var seconds) && seconds > 0
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;

            // Once a session is over the service stamps the closing price with its own clock on some markets, which would
            // keep it looking current; the end of the session is when it traded.
            if (time is { } stamped && ExchangeOfMarket(market) is { } exchange)
            {
                time = exchange.ClampToSessionEnd(stamped);
            }

            quotes.Add(new ParsedQuote($"{market}.{code}", price, previous, time));
        }

        return quotes;
    }

    /// <summary>
    /// Parses the data of a message of the instrument stream. The first message holds every field, the later ones only
    /// those that changed.
    /// </summary>
    /// <param name="json">
    /// The JSON text after the <c>data:</c> prefix.
    /// </param>
    /// <param name="symbol">
    /// The symbol of the instrument, which tells the exchange a trade time is clamped to.
    /// </param>
    /// <returns>
    /// The fields the message carries, or <see langword="null"/> when it is not a message of the instrument.
    /// </returns>
    public static StreamUpdate? ParseStreamUpdate(string json, string symbol)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        decimal? price = JsonValues.TryGetDecimal(data, "f43", out var last) && last > 0 ? last : null;
        decimal? previous = JsonValues.TryGetPositiveDecimal(data, "f60", out var close) ? close : null;
        DateTimeOffset? time = data.TryGetProperty("f86", out var stamp) && stamp.ValueKind == JsonValueKind.Number && stamp.TryGetInt64(out var seconds) && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
        if (time is { } traded && ExchangeOf(symbol) is { } exchange)
        {
            time = exchange.ClampToSessionEnd(traded);
        }

        return new StreamUpdate(price, previous, time);
    }

    /// <summary>
    /// Reads a market id that the service sends as a number.
    /// </summary>
    /// <param name="element">
    /// The JSON value.
    /// </param>
    /// <param name="market">
    /// The market id as text.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value is a number.
    /// </returns>
    private static bool TryGetMarket(JsonElement element, out string market)
    {
        market = string.Empty;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
        {
            market = number.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 } text)
        {
            market = text;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parses a candle response into close-price samples. A row is <c>time,close</c>: minute candles carry
    /// <c>yyyy-MM-dd HH:mm</c> in Beijing time, daily candles a date, which is placed at the close of that day.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <param name="symbol">
    /// The symbol that was requested, which tells the exchange a daily candle is dated at.
    /// </param>
    /// <returns>
    /// The samples, oldest first; empty when the service has no candles.
    /// </returns>
    public static IReadOnlyList<PricePoint> ParseKlines(string json, string symbol)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("klines", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var exchange = ExchangeOf(symbol);
        var points = new List<PricePoint>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.GetString() is not { } text)
            {
                continue;
            }

            var comma = text.IndexOf(',');
            if (comma <= 0
                || !decimal.TryParse(text.AsSpan(comma + 1), NumberStyles.Number, CultureInfo.InvariantCulture, out var close)
                || close <= 0
                || !TryParseTime(text[..comma], exchange, out var time))
            {
                continue;
            }

            points.Add(new PricePoint(time, close));
        }

        points.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return points;
    }

    /// <summary>
    /// Reads the time of a candle row.
    /// </summary>
    /// <param name="text">
    /// The time text, with or without a clock time.
    /// </param>
    /// <param name="exchange">
    /// The exchange of the instrument, which dates a daily candle at its close.
    /// </param>
    /// <param name="time">
    /// The time.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the text was a time.
    /// </returns>
    private static bool TryParseTime(string text, ExchangeHours? exchange, out DateTimeOffset time)
    {
        // Candles are dated in Beijing time whatever the market, except a daily candle, which is dated at the close of its
        // day in the time zone of the exchange: mainland markets close at 15:00 local time, the others at 16:00.
        if (text.Contains(':', StringComparison.Ordinal))
        {
            return ZonedTime.TryParse(text, ["yyyy-MM-dd HH:mm"], null, AsiaOffset, 0, out time);
        }

        return ZonedTime.TryParse(text, ["yyyy-MM-dd"], exchange?.Zone, AsiaOffset, ReferenceEquals(exchange, ExchangeHours.ChinaA) ? 15 : 16, out time);
    }

    /// <summary>
    /// Parses a symbol search response into the instruments of the markets this provider knows.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <param name="providerId">
    /// The provider id to stamp on the instruments.
    /// </param>
    /// <returns>
    /// The stocks, funds and indices; boards, warrants of unknown markets and the like are left out.
    /// </returns>
    public static IReadOnlyList<Asset> ParseSearch(string json, string providerId)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("QuotationCodeTable", out var table) || table.ValueKind != JsonValueKind.Object
            || !table.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var assets = new List<Asset>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("QuoteID", out var idElement) || idElement.GetString() is not { Length: > 0 } symbol
                || !item.TryGetProperty("MktNum", out var marketElement) || marketElement.GetString() is not { } market
                || !Markets.Contains(market)
                || !item.TryGetProperty("Classify", out var classify))
            {
                continue;
            }

            AssetClass? assetClass = classify.GetString() switch
            {
                "Index" => AssetClass.Index,
                "AStock" or "Fund" or "HK" or "UsStock" => AssetClass.Equity,
                _ => null,
            };
            if (assetClass is null || !seen.Add(symbol))
            {
                continue;
            }

            var name = item.TryGetProperty("Name", out var nameElement) ? nameElement.GetString() : null;
            assets.Add(new Asset(providerId, symbol, string.IsNullOrWhiteSpace(name) ? symbol : name, assetClass.Value));
        }

        return assets;
    }
}

/// <summary>
/// The fields of one message of the instrument stream.
/// </summary>
/// <param name="Price">
/// The latest price, or <see langword="null"/> when the message does not carry it.
/// </param>
/// <param name="PreviousClose">
/// The previous close, or <see langword="null"/> when the message does not carry it.
/// </param>
/// <param name="Time">
/// When the instrument last traded, or <see langword="null"/> when the message does not carry it.
/// </param>
internal sealed record StreamUpdate(decimal? Price, decimal? PreviousClose, DateTimeOffset? Time);
