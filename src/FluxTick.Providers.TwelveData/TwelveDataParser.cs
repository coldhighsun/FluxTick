using System.Globalization;
using System.Text.Json;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;

namespace FluxTick.Providers.TwelveData;

/// <summary>
/// Pure functions that parse payloads of the Twelve Data quote, time series and symbol search services.
/// </summary>
internal static class TwelveDataParser
{
    /// <summary>
    /// The keep-alive message the stream expects every few seconds; it answers with a heartbeat event.
    /// </summary>
    public const string Heartbeat = """{"action":"heartbeat"}""";

    /// <summary>
    /// The most symbols sent in one subscribe message.
    /// </summary>
    private const int MaxSymbolsPerMessage = 50;

    /// <summary>
    /// Builds subscribe or unsubscribe messages, <c>{"action":"subscribe","params":{"symbols":"AAPL,BTC/USD"}}</c>.
    /// </summary>
    /// <param name="action">
    /// <c>subscribe</c> or <c>unsubscribe</c>.
    /// </param>
    /// <param name="symbols">
    /// The symbols.
    /// </param>
    /// <returns>
    /// The JSON texts to send.
    /// </returns>
    public static IEnumerable<string> ControlMessages(string action, IEnumerable<string> symbols) =>
        symbols
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Chunk(MaxSymbolsPerMessage)
            .Select(chunk => JsonSerializer.Serialize(new { action, @params = new { symbols = string.Join(',', chunk) } }));

    /// <summary>
    /// Parses a price event of the stream.
    /// </summary>
    /// <param name="json">
    /// The received text.
    /// </param>
    /// <param name="price">
    /// The price.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the message was a price event.
    /// </returns>
    public static bool TryParsePrice(string json, out StreamPrice price)
    {
        price = null!;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("event", out var kind) || kind.GetString() != "price"
            || !root.TryGetProperty("symbol", out var symbolElement) || symbolElement.GetString() is not { Length: > 0 } symbol
            || !JsonValues.TryGetDecimal(root, "price", out var value) || value <= 0)
        {
            return false;
        }

        price = new StreamPrice(symbol, value, TryGetUnixSeconds(root, "timestamp"));
        return true;
    }

    /// <summary>
    /// Reads the symbols a subscribe reply refused, or the error an error event reports.
    /// </summary>
    /// <param name="json">
    /// The received text.
    /// </param>
    /// <returns>
    /// A description of the problem, or <see langword="null"/> when the message reports none.
    /// </returns>
    public static string? TryParseStreamError(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("event", out var kind))
        {
            return null;
        }

        if (kind.GetString() == "subscribe-status" && root.TryGetProperty("fails", out var fails) && fails.ValueKind == JsonValueKind.Array)
        {
            var names = fails.EnumerateArray()
                .Select(f => f.ValueKind == JsonValueKind.Object && f.TryGetProperty("symbol", out var n) ? n.GetString() : f.ToString())
                .Where(n => !string.IsNullOrEmpty(n))
                .ToArray();
            return names.Length == 0 ? null : $"cannot subscribe to {string.Join(", ", names)}";
        }

        return kind.GetString() == "error" && root.TryGetProperty("message", out var message) ? message.GetString() : null;
    }
    /// <summary>
    /// Parses the answer of the quote service. A request for one symbol is answered with the quote itself, a request for
    /// several with an object that holds one quote per symbol; a symbol that failed carries an error in its place.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The quotes that carry a price.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// The whole request failed, for example because of a wrong key or the rate limit.
    /// </exception>
    public static IReadOnlyList<ParsedQuote> ParseQuotes(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        ThrowIfError(root);
        var quotes = new List<ParsedQuote>();
        if (root.TryGetProperty("symbol", out _))
        {
            AddQuote(root, quotes);
            return quotes;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                AddQuote(property.Value, quotes);
            }
        }

        return quotes;
    }

    /// <summary>
    /// Reads one quote object and adds it when it carries a price.
    /// </summary>
    /// <param name="item">
    /// The quote object.
    /// </param>
    /// <param name="quotes">
    /// The list to add to.
    /// </param>
    private static void AddQuote(JsonElement item, List<ParsedQuote> quotes)
    {
        if (!item.TryGetProperty("symbol", out var symbolElement) || symbolElement.GetString() is not { Length: > 0 } symbol
            || !JsonValues.TryGetDecimal(item, "close", out var price) || price <= 0)
        {
            return;
        }

        decimal? previous = JsonValues.TryGetPositiveDecimal(item, "previous_close", out var close) ? close : null;

        // The time of the last quote; "timestamp" is the start of the day's candle, which is hours before it.
        DateTimeOffset? time = TryGetUnixSeconds(item, "last_quote_at") ?? TryGetUnixSeconds(item, "timestamp");
        quotes.Add(new ParsedQuote(symbol, price, previous, time));
    }

    /// <summary>
    /// Reads a Unix time in seconds that is a number or a string.
    /// </summary>
    /// <param name="item">
    /// The object that holds it.
    /// </param>
    /// <param name="name">
    /// The property name.
    /// </param>
    /// <returns>
    /// The time, or <see langword="null"/> when it is missing or not positive.
    /// </returns>
    private static DateTimeOffset? TryGetUnixSeconds(JsonElement item, string name) =>
        JsonValues.TryGetDecimal(item, name, out var seconds) && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds((long)seconds)
            : null;

    /// <summary>
    /// Parses a time series response into close-price samples.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The samples, oldest first. Intraday times are in the time zone of the exchange, which the answer names; a daily
    /// candle is dated at 16:00 of its day in that zone.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// The request failed.
    /// </exception>
    public static IReadOnlyList<PricePoint> ParseTimeSeries(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        ThrowIfError(root);
        if (!root.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var zone = TimeZoneInfo.Utc;
        if (root.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object
            && meta.TryGetProperty("exchange_timezone", out var zoneElement) && zoneElement.GetString() is { Length: > 0 } zoneId
            && TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var found))
        {
            zone = found;
        }

        var points = new List<PricePoint>();
        foreach (var row in values.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object
                || !row.TryGetProperty("datetime", out var stamp) || stamp.GetString() is not { } text
                || !JsonValues.TryGetDecimal(row, "close", out var price) || price <= 0
                || !TryParseTime(text, zone, out var time))
            {
                continue;
            }

            points.Add(new PricePoint(time, price));
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
    /// <param name="zone">
    /// The time zone of the exchange.
    /// </param>
    /// <param name="time">
    /// The time.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the text was a time.
    /// </returns>
    private static bool TryParseTime(string text, TimeZoneInfo zone, out DateTimeOffset time)
    {
        time = default;
        if (!DateTime.TryParseExact(text, ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return false;
        }

        if (text.Length == 10)
        {
            local = local.AddHours(16);
        }

        time = new DateTimeOffset(local, zone.GetUtcOffset(local));
        return true;
    }

    /// <summary>
    /// Parses a symbol search response into instruments; each symbol is listed once, because the same ticker is listed
    /// on several exchanges and the quote service takes the symbol alone.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <param name="providerId">
    /// The provider id to stamp on the instruments.
    /// </param>
    /// <returns>
    /// The stocks, funds and indices; currencies and cryptocurrencies are left out.
    /// </returns>
    public static IReadOnlyList<Asset> ParseSearch(string json, string providerId)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var assets = new List<Asset>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("symbol", out var symbolElement) || symbolElement.GetString() is not { Length: > 0 } symbol)
            {
                continue;
            }

            var type = item.TryGetProperty("instrument_type", out var typeElement) ? typeElement.GetString() ?? string.Empty : string.Empty;
            if (type.Contains("Currency", StringComparison.OrdinalIgnoreCase) || !seen.Add(symbol))
            {
                continue;
            }

            var name = item.TryGetProperty("instrument_name", out var nameElement) ? nameElement.GetString() : null;
            var assetClass = type.Equals("Index", StringComparison.OrdinalIgnoreCase) ? AssetClass.Index : AssetClass.Equity;
            assets.Add(new Asset(providerId, symbol, string.IsNullOrWhiteSpace(name) ? symbol : name, assetClass));
        }

        return assets;
    }

    /// <summary>
    /// Throws when the answer is an error of the whole request.
    /// </summary>
    /// <param name="root">
    /// The response root.
    /// </param>
    /// <exception cref="HttpRequestException">
    /// The answer reports an error.
    /// </exception>
    private static void ThrowIfError(JsonElement root)
    {
        if (root.TryGetProperty("status", out var status) && status.GetString() == "error")
        {
            var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            var code = root.TryGetProperty("code", out var c) && c.TryGetInt32(out var parsed) ? parsed : 0;
            throw new HttpRequestException($"Twelve Data error {code}: {message}");
        }
    }
}

/// <summary>
/// One price of the stream.
/// </summary>
/// <param name="Symbol">
/// The symbol the price is of.
/// </param>
/// <param name="Price">
/// The price.
/// </param>
/// <param name="Time">
/// When it was traded, or <see langword="null"/> when the event carries no time.
/// </param>
internal sealed record StreamPrice(string Symbol, decimal Price, DateTimeOffset? Time);
