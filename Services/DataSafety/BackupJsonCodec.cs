using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

public static class BackupJsonCodec
{
    private const string UtcTimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    private static readonly BackupJsonSerializerContext SerializerContext =
        new(CreateSerializerOptions());

    public static byte[] SerializeManifest(BackupManifest manifest)
    {
        BackupModelContract.ValidateManifest(manifest);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            manifest,
            SerializerContext.BackupManifest);
        EnforceMaximumLength(bytes, BackupFormatLimits.MaxManifestUncompressedBytes);
        return bytes;
    }

    public static BackupManifest DeserializeManifest(ReadOnlySpan<byte> utf8Json)
    {
        EnforceInput(
            utf8Json,
            BackupFormatLimits.MaxManifestUncompressedBytes,
            BackupErrorCodes.ManifestInvalid);
        try
        {
            var manifest = JsonSerializer.Deserialize(
                utf8Json,
                SerializerContext.BackupManifest)
                ?? throw new BackupFormatException(BackupErrorCodes.ManifestInvalid);
            BackupModelContract.ValidateManifest(manifest);
            return manifest;
        }
        catch (BackupFormatException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new BackupFormatException(BackupErrorCodes.ManifestInvalid, exception);
        }
    }

    public static byte[] SerializeData(BackupPayload payload)
    {
        BackupModelContract.ValidatePayload(payload);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            payload,
            SerializerContext.BackupPayload);
        EnforceMaximumLength(bytes, BackupFormatLimits.MaxDataUncompressedBytes);
        return bytes;
    }

    public static BackupPayload DeserializeData(ReadOnlySpan<byte> utf8Json)
    {
        EnforceInput(
            utf8Json,
            BackupFormatLimits.MaxDataUncompressedBytes,
            BackupErrorCodes.DataJsonInvalid);
        try
        {
            var payload = JsonSerializer.Deserialize(
                utf8Json,
                SerializerContext.BackupPayload)
                ?? throw new BackupFormatException(BackupErrorCodes.DataJsonInvalid);
            BackupModelContract.ValidatePayload(payload);
            return payload;
        }
        catch (BackupFormatException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new BackupFormatException(BackupErrorCodes.DataJsonInvalid, exception);
        }
    }

    internal static JsonTypeInfo? GetGeneratedTypeInfo(Type type) =>
        SerializerContext.GetTypeInfo(type);

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = false,
            MaxDepth = BackupFormatLimits.MaxJsonDepth,
            NumberHandling = JsonNumberHandling.Strict,
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = false
        };

        options.Converters.Add(new StrictBackupEnumJsonConverter<BackupSourcePlatform>(
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseSourcePlatform));
        options.Converters.Add(new StrictBackupEnumJsonConverter<BackupLexicalLookupMode>(
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseLexicalLookupMode));
        options.Converters.Add(new StrictBackupEnumJsonConverter<BackupKnowledgeState>(
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseKnowledgeState));
        options.Converters.Add(new StrictBackupEnumJsonConverter<BackupTokenKind>(
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseTokenKind));
        options.Converters.Add(new StrictBackupEnumJsonConverter<BackupPreparationState>(
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParsePreparationState));
        options.Converters.Add(new StrictBackupEnumJsonConverter<BackupLearningInteractionMode>(
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseLearningInteractionMode));
        options.Converters.Add(new StrictBackupEnumJsonConverter<BackupTechnicalTokenFamily>(
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseTechnicalTokenFamily));
        options.Converters.Add(new StrictBackupEnumJsonConverter<BackupCardDirection>(
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseCardDirection));
        options.Converters.Add(new StrictBackupEnumJsonConverter<BackupCardState>(
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseCardState));
        options.Converters.Add(new StrictBackupEnumJsonConverter<BackupReviewRating>(
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseReviewRating));
        options.Converters.Add(new StrictBackupEnumJsonConverter<BackupReviewSessionStatus>(
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseReviewSessionStatus));
        options.Converters.Add(new StrictBackupEnumJsonConverter<BackupPreparationMethod>(
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParsePreparationMethod));
        options.Converters.Add(new StrictBackupEnumJsonConverter<BackupPreparationSessionStatus>(
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParsePreparationSessionStatus));
        options.Converters.Add(new StrictBackupEnumJsonConverter<BackupPreparationCandidateStatus>(
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParsePreparationCandidateStatus));
        options.Converters.Add(new StrictBackupEnumJsonConverter<BackupLearningSessionStatus>(
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseLearningSessionStatus));
        options.Converters.Add(new StrictBackupEnumJsonConverter<BackupLexicalLookupStatus>(
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseLexicalLookupStatus));
        options.Converters.Add(new StrictBackupEnumJsonConverter<BackupGrammaticalRelationKind>(
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseGrammaticalRelationKind));
        options.Converters.Add(new StrictUtcDateTimeJsonConverter());
        options.Converters.Add(new BackupExtensionsJsonConverter());
        return options;
    }

    private static void EnforceInput(
        ReadOnlySpan<byte> utf8Json,
        int maximumLength,
        string invalidJsonCode)
    {
        if (utf8Json.Length > maximumLength)
        {
            throw new BackupFormatException(BackupErrorCodes.LimitExceeded);
        }

        if (utf8Json.Length >= 3
            && utf8Json[0] == 0xEF
            && utf8Json[1] == 0xBB
            && utf8Json[2] == 0xBF)
        {
            throw new BackupFormatException(invalidJsonCode);
        }
    }

    private static void EnforceMaximumLength(byte[] bytes, int maximumLength)
    {
        if (bytes.Length > maximumLength)
        {
            throw new BackupFormatException(BackupErrorCodes.LimitExceeded);
        }
    }

    private sealed class StrictBackupEnumJsonConverter<T>(
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

    private sealed class StrictUtcDateTimeJsonConverter : JsonConverter<DateTime>
    {
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
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
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

            writer.WriteStringValue(value.ToString(UtcTimestampFormat, CultureInfo.InvariantCulture));
        }
    }

    private sealed class BackupExtensionsJsonConverter : JsonConverter<BackupExtensions>
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
}
