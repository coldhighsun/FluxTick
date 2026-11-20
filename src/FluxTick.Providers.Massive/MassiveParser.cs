using System.Text.Json;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;

namespace FluxTick.Providers.Massive;

/// <summary>
/// Pure functions that parse payloads of the Massive aggregates and ticker search services.
/// </summary>
internal static class MassiveParser
{
    /// <summary>
    /// The time zone of the US exchanges, in which a daily bar belongs to its date; the system's when it knows it, else a
    /// fixed offset, which is right outside of daylight saving time only.
    /// </summary>
    private static readonly TimeZoneInfo Eastern = ExchangeHours.Us.Zone ?? TimeZoneInfo.CreateCustomTimeZone("ET", TimeSpan.FromHours(-5), "ET", "ET");

    /// <summary>
    /// Parses the daily bars of a request, newest first or oldest first as it was asked for.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The close of each bar, dated at 16:00 of its day in New York, in the order of the answer.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// The answer reports an error, such as a wrong key or the rate limit.
    /// </exception>
    public static IReadOnlyList<PricePoint> ParseDailyBars(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        ThrowIfError(root);
        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var points = new List<PricePoint>();
        foreach (var bar in results.EnumerateArray())
        {
            if (bar.ValueKind == JsonValueKind.Object
                && bar.TryGetProperty("t", out var t) && JsonValues.TryGetUnixMilliseconds(t, out var start)
                && JsonValues.TryGetDecimal(bar, "c", out var close) && close > 0)
            {
                points.Add(new PricePoint(CloseOfDay(start), close));
            }
        }

        return points;
    }

    /// <summary>
    /// Turns the last two daily bars, newest first, into a quote: the close of the newest is the price and the close of the
    /// one before it the previous close.
    /// </summary>
    /// <param name="symbol">
    /// The symbol that was asked for.
    /// </param>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The quote, or <see langword="null"/> when the symbol has no bar.
    /// </returns>
    public static ParsedQuote? ParseQuote(string symbol, string json)
    {
        var bars = ParseDailyBars(json);
        return bars.Count == 0
            ? null
            : new ParsedQuote(symbol, bars[0].Price, bars.Count > 1 ? bars[1].Price : null, bars[0].Timestamp);
    }

    /// <summary>
    /// Dates a daily bar at the close of its day.
    /// </summary>
    /// <param name="barStart">
    /// The start of the bar, which the service gives as a Unix time in milliseconds.
    /// </param>
    /// <returns>
    /// 16:00 of the day of the bar in New York.
    /// </returns>
    private static DateTimeOffset CloseOfDay(DateTimeOffset barStart)
    {
        var day = TimeZoneInfo.ConvertTime(barStart, Eastern).Date.AddHours(16);
        return new DateTimeOffset(day, Eastern.GetUtcOffset(day));
    }

    /// <summary>
    /// Parses a ticker search response into instruments.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <param name="providerId">
    /// The provider id to stamp on the instruments.
    /// </param>
    /// <returns>
    /// The tickers of the answer, as stocks.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// The answer reports an error.
    /// </exception>
    public static IReadOnlyList<Asset> ParseSearch(string json, string providerId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        ThrowIfError(root);
        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var assets = new List<Asset>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in results.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("ticker", out var tickerElement) || tickerElement.GetString() is not { Length: > 0 } ticker
                || !seen.Add(ticker))
            {
                continue;
            }

            var name = item.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            assets.Add(new Asset(providerId, ticker, string.IsNullOrWhiteSpace(name) ? ticker : name, AssetClass.Equity));
        }

        return assets;
    }

    /// <summary>
    /// Throws when the answer reports an error.
    /// </summary>
    /// <param name="root">
    /// The response root.
    /// </param>
    /// <exception cref="HttpRequestException">
    /// The answer has the status <c>ERROR</c> or <c>NOT_AUTHORIZED</c>.
    /// </exception>
    private static void ThrowIfError(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("status", out var status)
            && status.GetString() is "ERROR" or "NOT_AUTHORIZED")
        {
            var message = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString()
                : root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            throw new HttpRequestException($"Massive error: {message}");
        }
    }
}
