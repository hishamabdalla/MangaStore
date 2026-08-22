namespace MangaStore.API.Infrastructure.Json;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>The nullable counterpart of <see cref="UtcDateTimeConverter"/>.</summary>
/// <remarks>Registered separately because a converter for <c>T</c> is not applied to <c>T?</c>.</remarks>
public sealed class NullableUtcDateTimeConverter : JsonConverter<DateTime?>
{
    /// <inheritdoc/>
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : UtcDateTimeConverter.ReadUtc(ref reader);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(UtcDateTimeConverter.ToUtc(value.Value));
    }
}
