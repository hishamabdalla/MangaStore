namespace MangaStore.API.Infrastructure.Json;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Reads and writes every <see cref="DateTime"/> as UTC, always with a <c>Z</c> designator.</summary>
/// <remarks>
/// SQL Server hands <c>datetime2</c> back as <see cref="DateTimeKind.Unspecified"/>, which serialises
/// with no designator; the browser then reads it as local time and the rendered date can slip a day.
/// Deliberately scoped to <see cref="DateTime"/> — a <c>DateOnly</c> must stay a bare <c>YYYY-MM-DD</c>,
/// because a date-only value given a time is re-localised by the client and lands a day early.
/// </remarks>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    /// <inheritdoc/>
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ReadUtc(ref reader);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(ToUtc(value));
    }

    /// <summary>Parses a timestamp, treating a missing offset as UTC rather than as local time.</summary>
    internal static DateTime ReadUtc(ref Utf8JsonReader reader)
    {
        string? text = reader.GetString();

        // AssumeUniversal covers the offset-less form; AdjustToUniversal normalises anything that
        // does carry one. Reading with GetDateTime() instead would apply the server's local offset.
        return DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : throw new JsonException($"Expected a UTC timestamp but found '{text}'.");
    }

    /// <summary>Restates <paramref name="value"/> as UTC, assuming an unspecified kind already is.</summary>
    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
