using System.Text.Json.Serialization;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Private sidecar written next to a safety-copy archive for a future Recovery UI. Contains only
/// non-secret operational metadata about the archive itself — never archive contents, vocabulary,
/// source text, definitions, translations, consent/preferences, or absolute source-import paths.
/// </summary>
public sealed record MergeSafetyCopyMetadata(
    int MetadataSchemaVersion,
    string ArchiveFileName,
    DateTime CreatedAtUtc,
    long SizeBytes,
    BackupPortableArchiveCounts RecordCounts,
    string? SourceDescription,
    int ArchiveFormatVersion,
    string SourceAppVersion,
    int SourceDatabaseSchemaVersion,
    BackupSourcePlatform SourcePlatform)
{
    public const int CurrentSchemaVersion = 1;
}

// Reflection-based JSON serialization is disabled for this application (AOT/trimming); every DTO
// this service serializes must be registered in a source-generated context, matching the pattern
// already used by BackupJsonSerializerContext and DiagnosticsJsonSerializerContext.
[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = false)]
[JsonSerializable(typeof(MergeSafetyCopyMetadata))]
[JsonSerializable(typeof(BackupPortableArchiveCounts))]
internal sealed partial class MergeSafetyCopyMetadataJsonSerializerContext : JsonSerializerContext
{
}
