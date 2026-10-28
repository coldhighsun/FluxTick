using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace FluxTick.Providers.Yahoo;

/// <summary>
/// One price update of the Yahoo streamer.
/// </summary>
/// <param name="Symbol">
/// The Yahoo symbol, in the case the streamer sends it.
/// </param>
/// <param name="Price">
/// The last price.
/// </param>
/// <param name="Time">
/// When the price was traded, or <see langword="null"/> when the update carries no usable time.
/// </param>
/// <param name="Change">
/// The change against the previous close, or <see langword="null"/> when the update carries none.
/// </param>
/// <param name="QuoteType">
/// The instrument type as the streamer numbers it (8 equity, 9 index, 20 ETF, 41 crypto), or
/// <see langword="null"/> when absent.
/// </param>
/// <param name="MarketHours">
/// The session the price was traded in (0 pre-market, 1 regular, 2 post-market, 3 extended hours, 4 overnight), or
/// <see langword="null"/> when absent.
/// </param>
internal sealed record YahooStreamQuote(
    string Symbol,
    decimal Price,
    DateTimeOffset? Time,
    decimal? Change,
    int? QuoteType,
    int? MarketHours);

/// <summary>
/// Reads the price updates of the Yahoo streamer, which sends a protocol buffer message encoded as base64 (alone, or in
/// the <c>message</c> property of a <c>pricing</c> JSON object). Only the few fields the app needs are read; the
/// numbers are those of the public streamer's <c>PricingData</c> message.
/// </summary>
internal static class YahooStreamParser
{
    /// <summary>
    /// The field number of the symbol.
    /// </summary>
    private const int SymbolField = 1;

    /// <summary>
    /// The field number of the price, a 32-bit float.
    /// </summary>
    private const int PriceField = 2;

    /// <summary>
    /// The field number of the trade time in milliseconds, a zigzag-encoded varint.
    /// </summary>
    private const int TimeField = 3;

    /// <summary>
    /// The field number of the instrument type.
    /// </summary>
    private const int QuoteTypeField = 6;

    /// <summary>
    /// The field number of the session.
    /// </summary>
    private const int MarketHoursField = 7;

    /// <summary>
    /// The field number of the change against the previous close, a 32-bit float.
    /// </summary>
    private const int ChangeField = 12;

    /// <summary>
    /// Reads one update.
    /// </summary>
    /// <param name="base64">
    /// The base64 text of the message.
    /// </param>
    /// <param name="quote">
    /// The update; <see langword="null"/> when the text is not a price update.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the text holds a symbol and a price.
    /// </returns>
    public static bool TryParse(string base64, [NotNullWhen(true)] out YahooStreamQuote? quote)
    {
        quote = null;
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return false;
        }

        string? symbol = null;
        float? price = null;
        float? change = null;
        long? time = null;
        int? quoteType = null;
        int? marketHours = null;
        var position = 0;
        while (position < bytes.Length)
        {
            if (!TryReadVarint(bytes, ref position, out var key))
            {
                return false;
            }

            var field = (int)(key >> 3);
            switch ((int)(key & 7))
            {
                case 0:
                    if (!TryReadVarint(bytes, ref position, out var number))
                    {
                        return false;
                    }

                    switch (field)
                    {
                        case TimeField:
                            time = (long)(number >> 1) ^ -(long)(number & 1);
                            break;
                        case QuoteTypeField:
                            quoteType = (int)number;
                            break;
                        case MarketHoursField:
                            marketHours = (int)number;
                            break;
                    }

                    break;
                case 1:
                    position += 8;
                    break;
                case 2:
                    if (!TryReadVarint(bytes, ref position, out var length) || length > (ulong)(bytes.Length - position))
                    {
                        return false;
                    }

                    if (field == SymbolField)
                    {
                        symbol = Encoding.UTF8.GetString(bytes, position, (int)length);
                    }

                    position += (int)length;
                    break;
                case 5:
                    if (position + 4 > bytes.Length)
                    {
                        return false;
                    }

                    var value = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(position, 4));
                    position += 4;
                    if (field == PriceField)
                    {
                        price = value;
                    }
                    else if (field == ChangeField)
                    {
                        change = value;
                    }

                    break;
                default:
                    return false;
            }

            if (position > bytes.Length)
            {
                return false;
            }
        }

        if (string.IsNullOrEmpty(symbol) || price is not { } last || !float.IsFinite(last))
        {
            return false;
        }

        quote = new YahooStreamQuote(
            symbol,
            (decimal)last,
            ToTime(time),
            change is { } delta && float.IsFinite(delta) ? (decimal)delta : null,
            quoteType,
            marketHours);
        return true;
    }

    /// <summary>
    /// Converts the trade time of an update.
    /// </summary>
    /// <param name="milliseconds">
    /// The Unix time in milliseconds, or <see langword="null"/> when the update carries none.
    /// </param>
    /// <returns>
    /// The time, or <see langword="null"/> when it is absent, zero or no date can hold it.
    /// </returns>
    private static DateTimeOffset? ToTime(long? milliseconds)
    {
        if (milliseconds is null or <= 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a variable-length integer.
    /// </summary>
    /// <param name="bytes">
    /// The message.
    /// </param>
    /// <param name="position">
    /// Where to read; moved past the integer.
    /// </param>
    /// <param name="value">
    /// The integer.
    /// </param>
    /// <returns>
    /// <see langword="false"/> when the message ends inside the integer or the integer is longer than 64 bits.
    /// </returns>
    private static bool TryReadVarint(byte[] bytes, ref int position, out ulong value)
    {
        value = 0;
        for (var shift = 0; shift < 64 && position < bytes.Length; shift += 7)
        {
            var current = bytes[position++];
            value |= (ulong)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                return true;
            }
        }

        return false;
    }
}
