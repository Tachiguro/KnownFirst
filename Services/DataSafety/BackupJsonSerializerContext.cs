using System.Text.Json.Serialization;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BackupManifest))]
[JsonSerializable(typeof(BackupRecordCounts))]
[JsonSerializable(typeof(BackupPayload))]
[JsonSerializable(typeof(BackupSourceMaterial))]
[JsonSerializable(typeof(BackupSentenceRange))]
[JsonSerializable(typeof(BackupOccurrence))]
[JsonSerializable(typeof(BackupVocabularyItem))]
[JsonSerializable(typeof(BackupEncounteredForm))]
[JsonSerializable(typeof(BackupAutomaticLearningState))]
[JsonSerializable(typeof(BackupLegacyReviewSummary))]
[JsonSerializable(typeof(BackupPreparedItem))]
[JsonSerializable(typeof(BackupSourceReference))]
[JsonSerializable(typeof(BackupContextSnapshot))]
[JsonSerializable(typeof(BackupLearningData))]
[JsonSerializable(typeof(BackupLearningCard))]
[JsonSerializable(typeof(BackupLearningReview))]
[JsonSerializable(typeof(BackupWorkflowData))]
[JsonSerializable(typeof(BackupVocabularyReviewWorkflow))]
[JsonSerializable(typeof(BackupVocabularyReviewItem))]
[JsonSerializable(typeof(BackupPreparationWorkflow))]
[JsonSerializable(typeof(BackupPreparationItem))]
[JsonSerializable(typeof(BackupLookupDraft))]
[JsonSerializable(typeof(BackupLookupMeaning))]
[JsonSerializable(typeof(BackupFormRelation))]
[JsonSerializable(typeof(BackupLearningWorkflow))]
[JsonSerializable(typeof(BackupLearningQueueItem))]
[JsonSerializable(typeof(BackupExtensions))]
[JsonSerializable(typeof(BackupExtensionPayload))]
[JsonSerializable(typeof(BackupRestorePreview))]
[JsonSerializable(typeof(BackupReplaceAllScope))]
[JsonSerializable(typeof(BackupError))]
internal sealed partial class BackupJsonSerializerContext : JsonSerializerContext
{
}
