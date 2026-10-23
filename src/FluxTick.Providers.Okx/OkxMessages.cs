using System.Text.Json;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;

namespace FluxTick.Providers.Okx;

/// <summary>
/// Pure functions that build and parse OKX WebSocket and REST payloads.
/// </summary>
internal static class OkxMessages
{
    /// <summary>
    /// The text OKX expects as an application-level keep-alive.
    /// </summary>
    public const string Ping = "ping";

    /// <summary>
    /// The most instruments sent in one subscribe request.
    /// </summary>
    public const int MaxArgsPerMessage = 100;

    /// <summary>
    /// Builds subscribe or unsubscribe requests for the <c>tickers</c> channel, split to a safe size.
    /// </summary>
    /// <param name="operation">
    /// <c>subscribe</c> or <c>unsubscribe</c>.
    /// </param>
    /// <param name="symbols">
    /// The instrument ids, for example <c>BTC-USDT</c>.
    /// </param>
    /// <returns>
    /// The JSON texts to send.
    /// </returns>
    public static IEnumerable<string> ControlMessages(string operation, IEnumerable<string> symbols) =>
        symbols
            .Chunk(MaxArgsPerMessage)
            .Select(chunk => JsonSerializer.Serialize(new
            {
                op = operation,
                args = chunk.Select(s => new { channel = "tickers", instId = s }).ToArray(),
            }));

    /// <summary>
    /// Parses a received message with a single parse: the tickers of a <c>tickers</c> push, or the error of an
    /// <c>error</c> event.
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
    /// What the message carried; empty for pongs, control replies and other channels.
    /// </returns>
    public static StreamMessage Parse(string json, string providerId, DateTimeOffset fallbackTime)
    {
        // The keep-alive reply is the bare text "pong", not JSON.
        if (!json.TrimStart().StartsWith('{'))
        {
            return StreamMessage.Empty;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return StreamMessage.Empty;
        }

        return new StreamMessage(ReadTickers(root, providerId, fallbackTime), ReadError(root));
    }

    /// <summary>
    /// Reads the tickers of a <c>tickers</c> channel push.
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
    /// The quotes, empty when the message is not a ticker push.
    /// </returns>
    private static IReadOnlyList<Quote> ReadTickers(JsonElement root, string providerId, DateTimeOffset fallbackTime)
    {
        if (!root.TryGetProperty("arg", out var arg)
            || arg.ValueKind != JsonValueKind.Object
            || !arg.TryGetProperty("channel", out var channel)
            || channel.ValueKind != JsonValueKind.String
            || channel.GetString() != "tickers"
            || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var quotes = new List<Quote>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("instId", out var instId)
                || instId.ValueKind != JsonValueKind.String
                || instId.GetString() is not { Length: > 0 } symbol
                || !JsonValues.TryGetDecimal(item, "last", out var price))
            {
                continue;
            }

            decimal? reference = JsonValues.TryGetPositiveDecimal(item, "open24h", out var open) ? open : null;
            var timestamp = item.TryGetProperty("ts", out var ts) && JsonValues.TryGetUnixMilliseconds(ts, out var parsed)
                ? parsed
                : fallbackTime;
            quotes.Add(new Quote(new AssetId(providerId, symbol), price, reference, timestamp));
        }

        return quotes;
    }

    /// <summary>
    /// Reads the error an <c>error</c> event carries.
    /// </summary>
    /// <param name="root">
    /// The message root.
    /// </param>
    /// <returns>
    /// The error text, or <see langword="null"/> when the message is not an error event.
    /// </returns>
    private static string? ReadError(JsonElement root)
    {
        if (!root.TryGetProperty("event", out var kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != "error")
        {
            return null;
        }

        var code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        var message = root.TryGetProperty("msg", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
        return $"{code} {message}".Trim();
    }

    /// <summary>
    /// Parses a candles response into close-price samples.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The samples, oldest first.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// The response reports an error code.
    /// </exception>
    public static IReadOnlyList<PricePoint> ParseCandles(string json)
    {
        using var document = JsonDocument.Parse(json);
        var data = EnsureOk(document.RootElement);
        var points = new List<PricePoint>();
        foreach (var candle in data.EnumerateArray())
        {
            if (candle.ValueKind == JsonValueKind.Array
                && candle.GetArrayLength() > 4
                && JsonValues.TryGetUnixMilliseconds(candle[0], out var time)
                && JsonValues.TryGetDecimal(candle[4], out var close))
            {
                points.Add(new PricePoint(time, close));
            }
        }

        // OKX lists the newest candle first.
        points.Reverse();
        return points;
    }

    /// <summary>
    /// Parses an instruments response into the tradable pairs.
    /// </summary>
    /// <param name="json">
    /// The response body.
    /// </param>
    /// <returns>
    /// The live instruments.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// The response reports an error code.
    /// </exception>
    public static IReadOnlyList<ListedSymbol> ParseInstruments(string json)
    {
        using var document = JsonDocument.Parse(json);
        var data = EnsureOk(document.RootElement);
        var symbols = new List<ListedSymbol>();
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("instId", out var instId) || instId.GetString() is not { Length: > 0 } symbol)
            {
                continue;
            }

            if (item.TryGetProperty("state", out var state) && state.GetString() is { } text && text != "live")
            {
                continue;
            }

            symbols.Add(new ListedSymbol(symbol, SymbolSearch.PairNameOfDashedId(symbol)));
        }

        return symbols;
    }

    /// <summary>
    /// Checks the response envelope and returns its <c>data</c> array.
    /// </summary>
    /// <param name="root">
    /// The response root.
    /// </param>
    /// <returns>
    /// The data array.
    /// </returns>
    private static JsonElement EnsureOk(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Unexpected OKX response.");
        }

        var code = root.TryGetProperty("code", out var c) ? c.GetString() : null;
        if (code != "0")
        {
            var message = root.TryGetProperty("msg", out var m) ? m.GetString() : null;
            throw new HttpRequestException($"OKX error {code}: {message}");
        }

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("OKX response has no data array.");
        }

        return data;
    }
}
