using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxTick.Core.Infrastructure;

/// <summary>
/// Reads an optional enum of <c>settings.json</c> so that a value that is not a member of the enum, a name that is
/// unknown or a number that is out of range, comes out as <see langword="null"/>, which <c>SettingsFile</c> turns into
/// the default. Without it, an unknown name would make the whole file count as corrupt.
/// </summary>
internal sealed class LenientEnumConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => Nullable.GetUnderlyingType(typeToConvert) is { IsEnum: true };

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var enumType = Nullable.GetUnderlyingType(typeToConvert)!;
        return (JsonConverter)Activator.CreateInstance(typeof(LenientEnumConverter<>).MakeGenericType(enumType))!;
    }

    /// <summary>
    /// Converts one optional enum.
    /// </summary>
    /// <typeparam name="T">
    /// The enum type.
    /// </typeparam>
    private sealed class LenientEnumConverter<T> : JsonConverter<T?>
        where T : struct, Enum
    {
        /// <inheritdoc />
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    return Defined(Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var named) ? named : null);
                case JsonTokenType.Number:
                    return reader.TryGetInt64(out var number) ? Defined((T)Enum.ToObject(typeof(T), number)) : null;
                case JsonTokenType.Null:
                    return null;
                default:
                    reader.Skip();
                    return null;
            }
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
        {
            if (value is { } member)
            {
                writer.WriteStringValue(member.ToString());
            }
            else
            {
                writer.WriteNullValue();
            }
        }

        /// <summary>
        /// Keeps a value only when it is a member of the enum.
        /// </summary>
        /// <param name="value">
        /// The value that was read.
        /// </param>
        /// <returns>
        /// The value, or <see langword="null"/> when it is not a member.
        /// </returns>
        private static T? Defined(T? value) => value is { } member && Enum.IsDefined(member) ? member : null;
    }
}
