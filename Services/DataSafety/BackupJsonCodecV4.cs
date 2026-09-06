using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

/// <summary>
/// Archive format v4 JSON codec (KF-LEARN-011 Slice 5).
/// Strict source-generated serialization throughout — no reflection fallback.
/// </summary>
public static class BackupJsonCodecV4
{
    private static readonly BackupJsonSerializerContextV4 SerializerContext =
        new(CreateSerializerOptions());

    public static byte[] SerializeManifest(BackupManifestV4 manifest)
    {
        BackupModelContractV4.ValidateManifest(manifest);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            manifest,
            SerializerContext.BackupManifestV4);
        EnforceMaximumLength(bytes, BackupFormatLimits.MaxManifestUncompressedBytes);
        return bytes;
    }

    public static BackupManifestV4 DeserializeManifest(ReadOnlySpan<byte> utf8Json)
    {
        EnforceInput(utf8Json, BackupFormatLimits.MaxManifestUncompressedBytes, BackupErrorCodes.ManifestInvalid);
        try
        {
            var manifest = JsonSerializer.Deserialize(
                utf8Json,
                SerializerContext.BackupManifestV4)
                ?? throw new BackupFormatException(BackupErrorCodes.ManifestInvalid);
            BackupModelContractV4.ValidateManifest(manifest);
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

    public static byte[] SerializeData(BackupPayloadV4 payload)
    {
        BackupModelContractV4.ValidatePayload(payload);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            payload,
            SerializerContext.BackupPayloadV4);
        EnforceMaximumLength(bytes, BackupFormatLimits.MaxDataUncompressedBytes);
        return bytes;
    }

    public static BackupPayloadV4 DeserializeData(ReadOnlySpan<byte> utf8Json)
    {
        EnforceInput(utf8Json, BackupFormatLimits.MaxDataUncompressedBytes, BackupErrorCodes.DataJsonInvalid);
        try
        {
            var payload = JsonSerializer.Deserialize(
                utf8Json,
                SerializerContext.BackupPayloadV4)
                ?? throw new BackupFormatException(BackupErrorCodes.DataJsonInvalid);
            BackupModelContractV4.ValidatePayload(payload);
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

        AddSharedConverters(options);
        options.Converters.Add(new StrictBackupEnumJsonConverter<BackupSenseStatus>(
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseSenseStatus));
        options.Converters.Add(new StrictBackupEnumJsonConverter<BackupAnswerVariantRequirement>(
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseAnswerVariantRequirement));
        options.Converters.Add(new StrictBackupEnumJsonConverter<BackupFsrsCardStateKind>(
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseFsrsCardStateKind));
        options.Converters.Add(new StrictBackupEnumJsonConverter<BackupLearningTargetKind>(
            BackupEnumMappings.ToExternalString,
            BackupEnumMappings.ParseLearningTargetKind));
        return options;
    }

    private static void AddSharedConverters(JsonSerializerOptions options)
    {
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
    }

    private static void EnforceInput(ReadOnlySpan<byte> utf8Json, int maximumLength, string invalidJsonCode)
    {
        if (utf8Json.Length > maximumLength)
        {
            throw new BackupFormatException(BackupErrorCodes.LimitExceeded);
        }

        if (utf8Json.Length >= 3 && utf8Json[0] == 0xEF && utf8Json[1] == 0xBB && utf8Json[2] == 0xBF)
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
}
