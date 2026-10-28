using System.Text.Json;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;

namespace FluxTick.Providers.Kraken;

/// <summary>
/// Pure functions that build and parse Kraken WebSocket v2 and REST payloads.
/// </summary>
internal static class KrakenMessages
{
    /// <summary>
    /// The most symbols sent in one subscribe request.
    /// </summary>
    public const int MaxSymbolsPerMessage = 100;

    /// <summary>
    /// The number of one-minute candles kept for a sparkline.
    /// </summary>
    public const int HistoryLength = 60;

    /// <summary>
    /// Builds subscribe or unsubscribe requests for the <c>ticker</c> channel, split to a safe size.
    /// </summary>
    /// <param name="method">
    /// <c>subscribe</c> or <c>unsubscribe</c>.
    /// </param>
    /// <param name="symbols">
    /// The v2 symbols, for example <c>BTC/USD</c>.
    /// </param>
    /// <returns>
    /// The JSON texts to send.
    /// </returns>
    public static IEnumerable<string> ControlMessages(string method, IEnumerable<string> symbols) =>
        symbols
            .Chunk(MaxSymbolsPerMessage)
            .Select(chunk => JsonSerializer.Serialize(new { method, @params = new { channel = "ticker", symbol = chunk } }));

    /// <summary>
    /// Parses a received message with a single parse: the tickers of a <c>ticker</c> snapshot or update, or the
    /// error of a failed control reply. Kraken sends no 24h open, so the reference price is the last price minus
    /// the 24h change.
    /// </summary>
    /// <param name="json">
    /// The received text.
    /// </param>
    /// <param name="providerId">
    /// The provider id to stamp on the resulting quotes.
    /// </param>
    /// <param name="fallbackTime">
    /// The timestamp used when a ticker carries none.
    /// </param>
    /// <returns>
    /// What the message carried; empty for heartbeats, status and successful control replies.
    /// </returns>
    public static StreamMessage Parse(string json, string providerId, DateTimeOffset fallbackTime)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return StreamMessage.Empty;
        }

        return new StreamMessage(ReadTickers(root, providerId, fallbackTime), ReadError(root));
    }

    /// <summary>
    /// Reads the tickers of a <c>ticker</c> channel message.
    /// </summary>
    /// <param name="root">
    /// The message root.
    /// </param>
    /// <param name="providerId">
    /// The provider id to stamp on the resulting quotes.
    /// </param>
    /// <param name="fallbackTime">
    /// The timestamp used when a ticker carries none.
    /// </param>
    /// <returns>
    /// The quotes, empty when the message is not a ticker message.
    /// </returns>
    private static IReadOnlyList<Quote> ReadTickers(JsonElement root, string providerId, DateTimeOffset fallbackTime)
    {
        if (!root.TryGetProperty("channel", out var channel)
            || channel.ValueKind != JsonValueKind.String
            || channel.GetString() != "ticker"
            || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var quotes = new List<Quote>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("symbol", out var symbolElement)
                || symbolElement.ValueKind != JsonValueKind.String
                || symbolElement.GetString() is not { Length: > 0 } symbol
                || !JsonValues.TryGetDecimal(item, "last", out var price))
            {
                continue;
            }

            decimal? reference = JsonValues.TryGetDecimal(item, "change", out var change) && price - change > 0m
                ? price - change
                : null;
            DateTimeOffset? traded = JsonValues.TryGetIsoTime(item, "timestamp", out var parsed) ? parsed : null;
            quotes.Add(new Quote(new AssetId(providerId, symbol), price, reference, traded ?? fallbackTime) { TimeIsEstimated = traded is null });
        }

        return quotes;
    }

    /// <summary>
    /// Reads the error of a failed control reply.
    /// </summary>
    /// <param name="root">
    /// The message root.
    /// </param>
    /// <returns>
    /// The error text, or <see langword="null"/> when the message is not a failed reply.
    /// </returns>
    private static string? ReadError(JsonElement root)
    {
        if (!root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.False)
        {
            return null;
        }

        return root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
            ? error.GetString()
            : "Request failed";
    }

    /// <summary>
    /// Parses an OHLC response into close-price samples.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The most recent samples, oldest first.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// The response reports errors.
    /// </exception>
    public static IReadOnlyList<PricePoint> ParseOhlc(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = EnsureOk(document.RootElement);

        // The result holds the candle array under the pair name next to a numeric "last" cursor.
        var candles = result.EnumerateObject().FirstOrDefault(p => p.Value.ValueKind == JsonValueKind.Array).Value;
        var points = new List<PricePoint>();
        if (candles.ValueKind == JsonValueKind.Array)
        {
            foreach (var candle in candles.EnumerateArray())
            {
                if (candle.ValueKind == JsonValueKind.Array
                    && candle.GetArrayLength() > 4
                    && candle[0].TryGetInt64(out var seconds)
                    && JsonValues.TryGetDecimal(candle[4], out var close))
                {
                    points.Add(new PricePoint(DateTimeOffset.FromUnixTimeSeconds(seconds), close));
                }
            }
        }

        return points.Count > HistoryLength ? points.GetRange(points.Count - HistoryLength, HistoryLength) : points;
    }

    /// <summary>
    /// Parses an asset pairs response into the pairs the v2 WebSocket accepts.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The online pairs, named as the v2 API names them.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// The response reports errors.
    /// </exception>
    public static IReadOnlyList<ListedSymbol> ParsePairs(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = EnsureOk(document.RootElement);
        var symbols = new List<ListedSymbol>();
        foreach (var pair in result.EnumerateObject())
        {
            // ".d" marks dark pool pairs, which have no public ticker.
            if (pair.Name.EndsWith(".d", StringComparison.Ordinal) || pair.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (pair.Value.TryGetProperty("status", out var status) && status.GetString() is { } text && text != "online")
            {
                continue;
            }

            if (pair.Value.TryGetProperty("wsname", out var wsname) && wsname.GetString() is { Length: > 0 } name)
            {
                var symbol = ToV2Symbol(name);
                symbols.Add(new ListedSymbol(symbol, symbol));
            }
        }

        return symbols;
    }

    /// <summary>
    /// Converts a REST <c>wsname</c> to the symbol the v2 WebSocket expects. v1 names Bitcoin <c>XBT</c> and Dogecoin
    /// <c>XDG</c>, while v2 uses <c>BTC</c> and <c>DOGE</c>.
    /// </summary>
    /// <param name="wsname">
    /// The name, for example <c>XBT/USD</c>.
    /// </param>
    /// <returns>
    /// The v2 symbol, for example <c>BTC/USD</c>.
    /// </returns>
    public static string ToV2Symbol(string wsname) =>
        string.Join('/', wsname.Split('/').Select(part => part switch
        {
            "XBT" => "BTC",
            "XDG" => "DOGE",
            _ => part,
        }));

    /// <summary>
    /// Checks the response envelope and returns its <c>result</c> object.
    /// </summary>
    /// <param name="root">
    /// The response root.
    /// </param>
    /// <returns>
    /// The result object.
    /// </returns>
    private static JsonElement EnsureOk(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Unexpected Kraken response.");
        }

        if (root.TryGetProperty("error", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            throw new HttpRequestException($"Kraken error: {string.Join("; ", errors.EnumerateArray().Select(e => e.GetString()))}");
        }

        if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Kraken response has no result object.");
        }

        return result;
    }
}
