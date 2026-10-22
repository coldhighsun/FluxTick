using System.Text.Json;
using FluxTick.Abstractions;

namespace FluxTick.Providers.Yahoo;

/// <summary>
/// The parsed content of a Yahoo chart response.
/// </summary>
/// <param name="Quote">
/// The latest quote.
/// </param>
/// <param name="History">
/// Intraday samples, oldest first.
/// </param>
internal sealed record YahooChartData(Quote Quote, IReadOnlyList<PricePoint> History);

/// <summary>
/// Raised when Yahoo answers with an error object instead of data (unknown symbol, delisted, ...).
/// </summary>
/// <param name="message">
/// The message reported by Yahoo.
/// </param>
internal sealed class YahooApiException(string message) : Exception(message);

/// <summary>
/// Pure functions that parse Yahoo Finance JSON payloads.
/// </summary>
internal static class YahooParser
{
    /// <summary>
    /// Parses a <c>v8/finance/chart</c> response.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <param name="providerId">
    /// The provider id to stamp on the quote.
    /// </param>
    /// <param name="symbol">
    /// The symbol that was requested.
    /// </param>
    /// <returns>
    /// The quote and intraday history.
    /// </returns>
    /// <exception cref="YahooApiException">
    /// Yahoo reported an error or the response had no price.
    /// </exception>
    public static YahooChartData ParseChart(string json, string providerId, string symbol)
    {
        using var document = JsonDocument.Parse(json);
        var chart = document.RootElement.GetProperty("chart");
        if (chart.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            var description = error.TryGetProperty("description", out var d) ? d.GetString() : null;
            throw new YahooApiException(description ?? "Yahoo returned an error.");
        }

        var results = chart.GetProperty("result");
        if (results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
        {
            throw new YahooApiException("Yahoo returned no result.");
        }

        var result = results[0];
        var meta = result.GetProperty("meta");
        if (!meta.TryGetProperty("regularMarketPrice", out var priceElement) || !priceElement.TryGetDecimal(out var price))
        {
            throw new YahooApiException("Yahoo returned no price.");
        }

        decimal? reference = null;
        foreach (var name in new[] { "previousClose", "chartPreviousClose" })
        {
            if (meta.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var value))
            {
                reference = value;
                break;
            }
        }

        var timestamp = meta.TryGetProperty("regularMarketTime", out var time) && time.TryGetInt64(out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.UtcNow;

        return new YahooChartData(new Quote(new AssetId(providerId, symbol), price, reference, timestamp), ParseHistory(result));
    }

    /// <summary>
    /// Parses a <c>v1/finance/search</c> response into assets.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <param name="providerId">
    /// The provider id to stamp on the assets.
    /// </param>
    /// <returns>
    /// The instruments Yahoo can quote; unsupported quote types (such as options) are skipped.
    /// </returns>
    public static IReadOnlyList<Asset> ParseSearch(string json, string providerId)
    {
        using var document = JsonDocument.Parse(json);
        var assets = new List<Asset>();
        if (!document.RootElement.TryGetProperty("quotes", out var quotes))
        {
            return assets;
        }

        foreach (var item in quotes.EnumerateArray())
        {
            if (!item.TryGetProperty("symbol", out var symbol) || !item.TryGetProperty("quoteType", out var type))
            {
                continue;
            }

            if (MapQuoteType(type.GetString()) is not { } assetClass)
            {
                continue;
            }

            var name = GetString(item, "longname") ?? GetString(item, "shortname") ?? symbol.GetString()!;
            assets.Add(new Asset(providerId, symbol.GetString()!, name, assetClass));
        }

        return assets;
    }

    /// <summary>
    /// Maps a Yahoo quote type to an asset class.
    /// </summary>
    /// <param name="quoteType">
    /// The Yahoo quote type.
    /// </param>
    /// <returns>
    /// The asset class, or <see langword="null"/> when the type is not supported.
    /// </returns>
    internal static AssetClass? MapQuoteType(string? quoteType) => quoteType switch
    {
        "EQUITY" or "ETF" or "MUTUALFUND" => AssetClass.Equity,
        "INDEX" => AssetClass.Index,
        "CURRENCY" => AssetClass.Forex,
        "FUTURE" => AssetClass.Commodity,
        "CRYPTOCURRENCY" => AssetClass.Crypto,
        _ => null,
    };

    /// <summary>
    /// Extracts the intraday close series from a chart result.
    /// </summary>
    /// <param name="result">
    /// The first element of <c>chart.result</c>.
    /// </param>
    /// <returns>
    /// The samples, oldest first; empty when the response carries none.
    /// </returns>
    private static IReadOnlyList<PricePoint> ParseHistory(JsonElement result)
    {
        if (!result.TryGetProperty("timestamp", out var timestamps)
            || timestamps.ValueKind != JsonValueKind.Array
            || !result.TryGetProperty("indicators", out var indicators)
            || !indicators.TryGetProperty("quote", out var quoteArray)
            || quoteArray.GetArrayLength() == 0
            || !quoteArray[0].TryGetProperty("close", out var closes))
        {
            return [];
        }

        var points = new List<PricePoint>();
        var count = Math.Min(timestamps.GetArrayLength(), closes.GetArrayLength());
        for (var i = 0; i < count; i++)
        {
            if (closes[i].ValueKind == JsonValueKind.Number && closes[i].TryGetDecimal(out var close))
            {
                points.Add(new PricePoint(DateTimeOffset.FromUnixTimeSeconds(timestamps[i].GetInt64()), close));
            }
        }

        return points;
    }

    /// <summary>
    /// Reads an optional string property.
    /// </summary>
    /// <param name="element">
    /// The object to read from.
    /// </param>
    /// <param name="name">
    /// The property name.
    /// </param>
    /// <returns>
    /// The value, or <see langword="null"/> when missing.
    /// </returns>
    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
