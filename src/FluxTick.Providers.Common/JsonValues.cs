using System.Globalization;
using System.Text.Json;

namespace FluxTick.Providers.Common;

/// <summary>
/// Helpers for reading values that exchanges transmit as either JSON numbers or JSON strings.
/// </summary>
public static class JsonValues
{
    /// <summary>
    /// Parses a decimal that is transmitted as a JSON number or a JSON string.
    /// </summary>
    /// <param name="element">
    /// The JSON element.
    /// </param>
    /// <param name="value">
    /// The parsed value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when parsing succeeded.
    /// </returns>
    public static bool TryGetDecimal(JsonElement element, out decimal value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetDecimal(out value);
            case JsonValueKind.String:
                return decimal.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            default:
                value = 0m;
                return false;
        }
    }

    /// <summary>
    /// Parses a decimal property of an object.
    /// </summary>
    /// <param name="parent">
    /// The object holding the property.
    /// </param>
    /// <param name="name">
    /// The property name.
    /// </param>
    /// <param name="value">
    /// The parsed value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the property exists and holds a decimal.
    /// </returns>
    public static bool TryGetDecimal(JsonElement parent, string name, out decimal value)
    {
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var element))
        {
            return TryGetDecimal(element, out value);
        }

        value = 0m;
        return false;
    }

    /// <summary>
    /// Parses a decimal property of an object that must be greater than zero, such as the reference price a change is
    /// measured from. An exchange reports <c>0</c> for an instrument without a trading history, which is no reference.
    /// </summary>
    /// <param name="parent">
    /// The object holding the property.
    /// </param>
    /// <param name="name">
    /// The property name.
    /// </param>
    /// <param name="value">
    /// The parsed value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the property exists and holds a decimal above zero.
    /// </returns>
    public static bool TryGetPositiveDecimal(JsonElement parent, string name, out decimal value) =>
        TryGetDecimal(parent, name, out value) && value > 0m;

    /// <summary>
    /// Parses an ISO 8601 timestamp property of an object, tolerating the nanosecond precision some APIs use. A time
    /// without an offset is taken as UTC.
    /// </summary>
    /// <param name="parent">
    /// The object holding the property.
    /// </param>
    /// <param name="name">
    /// The property name.
    /// </param>
    /// <param name="timestamp">
    /// The parsed time.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the property exists and holds a time.
    /// </returns>
    public static bool TryGetIsoTime(JsonElement parent, string name, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                element.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out timestamp);
    }

    /// <summary>
    /// Parses a Unix timestamp in milliseconds that is transmitted as a JSON number or a JSON string.
    /// </summary>
    /// <param name="element">
    /// The JSON element.
    /// </param>
    /// <param name="timestamp">
    /// The parsed time.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when parsing succeeded.
    /// </returns>
    public static bool TryGetUnixMilliseconds(JsonElement element, out DateTimeOffset timestamp)
    {
        timestamp = default;
        long milliseconds;
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetInt64(out milliseconds):
                break;
            case JsonValueKind.String when long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out milliseconds):
                break;
            default:
                return false;
        }

        try
        {
            timestamp = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
