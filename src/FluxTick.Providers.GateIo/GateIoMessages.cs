using System.Text.Json;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;

namespace FluxTick.Providers.GateIo;

/// <summary>
/// Pure functions that build and parse Gate.io v4 WebSocket and REST payloads.
/// </summary>
internal static class GateIoMessages
{
    /// <summary>
    /// The application-level keep-alive Gate.io answers with a pong.
    /// </summary>
    public const string Ping = """{"channel":"spot.ping"}""";

    /// <summary>
    /// The name of the spot ticker channel.
    /// </summary>
    private const string TickerChannel = "spot.tickers";

    /// <summary>
    /// Builds subscribe or unsubscribe requests for the ticker channel, one per pair: Gate.io rejects a whole request
    /// when it names a single unknown pair, so a bad symbol must not take its neighbours down with it.
    /// </summary>
    /// <param name="operation">
    /// <c>subscribe</c> or <c>unsubscribe</c>.
    /// </param>
    /// <param name="symbols">
    /// The pairs, for example <c>BTC_USDT</c>.
    /// </param>
    /// <param name="now">
    /// The current time, sent as the request time in seconds.
    /// </param>
    /// <returns>
    /// The JSON texts to send.
    /// </returns>
    public static IEnumerable<string> ControlMessages(string operation, IEnumerable<string> symbols, DateTimeOffset now) =>
        symbols.Select(symbol => JsonSerializer.Serialize(new
        {
            time = now.ToUnixTimeSeconds(),
            channel = TickerChannel,
            @event = operation,
            payload = new[] { symbol },
        }));

    /// <summary>
    /// Parses a spot ticker push. The price 24 hours ago is derived from the last price and the change in percent.
    /// </summary>
    /// <param name="json">
    /// The received text.
    /// </param>
    /// <param name="providerId">
    /// The provider id to stamp on the resulting quote.
    /// </param>
    /// <param name="fallbackTime">
    /// The timestamp used when the message carries none.
    /// </param>
    /// <param name="quote">
    /// The parsed quote.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the message was a ticker update with a price.
    /// </returns>
    public static bool TryParseTicker(string json, string providerId, DateTimeOffset fallbackTime, out Quote quote)
    {
        quote = null!;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("channel", out var channel) || channel.GetString() != TickerChannel
            || !root.TryGetProperty("event", out var kind) || kind.GetString() != "update"
            || !root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("currency_pair", out var pair) || pair.GetString() is not { Length: > 0 } symbol
            || !JsonValues.TryGetDecimal(result, "last", out var price))
        {
            return false;
        }

        decimal? reference = null;
        if (JsonValues.TryGetDecimal(result, "change_percentage", out var percent) && percent > -100m)
        {
            reference = price / (1m + (percent / 100m));
        }

        DateTimeOffset? traded = root.TryGetProperty("time_ms", out var ms) && JsonValues.TryGetUnixMilliseconds(ms, out var parsed)
            ? parsed
            : null;
        quote = CryptoQuote.Create(new AssetId(providerId, symbol), price, reference, traded ?? fallbackTime, traded is null);
        return true;
    }

    /// <summary>
    /// Reads the error of a failed control reply.
    /// </summary>
    /// <param name="json">
    /// The received text.
    /// </param>
    /// <returns>
    /// The error text, or <see langword="null"/> when the message is not a failed reply.
    /// </returns>
    public static string? TryParseError(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
            ? message.GetString()
            : "Request failed";
    }

    /// <summary>
    /// Parses a candlesticks response into close-price samples. Each candle is
    /// <c>[time (s), quote volume, close, high, low, open, base volume, closed]</c>.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The samples, oldest first.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// The response is an error object.
    /// </exception>
    public static IReadOnlyList<PricePoint> ParseCandles(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = EnsureArray(document.RootElement);
        var points = new List<PricePoint>();
        foreach (var candle in root.EnumerateArray())
        {
            if (candle.ValueKind == JsonValueKind.Array
                && candle.GetArrayLength() > 2
                && JsonValues.TryGetDecimal(candle[0], out var seconds)
                && JsonValues.TryGetDecimal(candle[2], out var close))
            {
                points.Add(new PricePoint(DateTimeOffset.FromUnixTimeSeconds((long)seconds), close));
            }
        }

        return points;
    }

    /// <summary>
    /// Parses a currency pair listing into the tradable pairs.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The pairs whose status is <c>tradable</c>.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// The response is an error object.
    /// </exception>
    public static IReadOnlyList<ListedSymbol> ParsePairs(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = EnsureArray(document.RootElement);
        var symbols = new List<ListedSymbol>();
        foreach (var item in root.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idElement) || idElement.GetString() is not { Length: > 0 } id)
            {
                continue;
            }

            if (item.TryGetProperty("trade_status", out var status) && status.GetString() is { } text && text != "tradable")
            {
                continue;
            }

            var baseCoin = item.TryGetProperty("base", out var b) ? b.GetString() : null;
            var quoteCoin = item.TryGetProperty("quote", out var q) ? q.GetString() : null;
            var display = !string.IsNullOrEmpty(baseCoin) && !string.IsNullOrEmpty(quoteCoin) ? $"{baseCoin}/{quoteCoin}" : id;
            symbols.Add(new ListedSymbol(id, display));
        }

        return symbols;
    }

    /// <summary>
    /// Checks that a REST response is the array Gate.io answers with, and turns its error object into an exception.
    /// </summary>
    /// <param name="root">
    /// The response root.
    /// </param>
    /// <returns>
    /// The array.
    /// </returns>
    private static JsonElement EnsureArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root;
        }

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("label", out var label))
        {
            var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            throw new HttpRequestException($"Gate.io error {label.GetString()}: {message}");
        }

        throw new JsonException("Unexpected Gate.io response.");
    }
}
