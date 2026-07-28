using System.Text.Json;
using System.Text.Json.Serialization;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

// Shared, stateless System.Text.Json converters used by both the v1 (BackupJsonCodec) and v2
// (BackupJsonCodecV2) source-generated serializer options (KF-MEANING-001 Slice 2). Extracted
// verbatim from BackupJsonCodec's former private nested classes — a pure mechanical move, zero
// behavior change, so v1's existing byte-for-byte output and its tests are unaffected.

internal sealed class StrictBackupEnumJsonConverter<T>(
    Func<T, string> format,
    Func<string, T> parse) : JsonConverter<T>
    where T : struct, Enum
{
    public override T Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new BackupFormatException(BackupErrorCodes.UnknownEnum);
        }

        var value = reader.GetString();
        if (value is null)
        {
            throw new BackupFormatException(BackupErrorCodes.UnknownEnum);
        }

        return parse(value);
    }

    public override void Write(
        Utf8JsonWriter writer,
        T value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(format(value));
}

internal sealed class StrictUtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    internal const string UtcTimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    public override DateTime Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new BackupFormatException(BackupErrorCodes.InvalidTimestamp);
        }

        var value = reader.GetString();
        if (value is null
            || !DateTime.TryParseExact(
                value,
                UtcTimestampFormat,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var timestamp)
            || timestamp.Kind != DateTimeKind.Utc)
        {
            throw new BackupFormatException(BackupErrorCodes.InvalidTimestamp);
        }

        return timestamp;
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTime value,
        JsonSerializerOptions options)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new BackupFormatException(BackupErrorCodes.InvalidTimestamp);
        }

        writer.WriteStringValue(value.ToString(UtcTimestampFormat, System.Globalization.CultureInfo.InvariantCulture));
    }
}

internal sealed class BackupExtensionsJsonConverter : JsonConverter<BackupExtensions>
{
    public override BackupExtensions Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new BackupFormatException(BackupErrorCodes.DataJsonInvalid);
        }

        var features = new Dictionary<string, BackupExtensionPayload>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new BackupExtensions(features);
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new BackupFormatException(BackupErrorCodes.DataJsonInvalid);
            }

            var name = reader.GetString()
                ?? throw new BackupFormatException(BackupErrorCodes.DataJsonInvalid);
            if (!reader.Read())
            {
                throw new BackupFormatException(BackupErrorCodes.DataJsonInvalid);
            }

            using var document = JsonDocument.ParseValue(ref reader);
            features[name] = new BackupExtensionPayload(document.RootElement.GetRawText());
        }

        throw new BackupFormatException(BackupErrorCodes.DataJsonInvalid);
    }

    public override void Write(
        Utf8JsonWriter writer,
        BackupExtensions value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var feature in value.Features.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(feature.Key);
            writer.WriteRawValue(feature.Value.Json, skipInputValidation: false);
        }

        writer.WriteEndObject();
    }
}
