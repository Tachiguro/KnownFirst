using System.Text.Json.Serialization;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BackupManifestV2))]
[JsonSerializable(typeof(BackupRecordCountsV2))]
[JsonSerializable(typeof(BackupPayloadV2))]
[JsonSerializable(typeof(BackupSourceMaterial))]
[JsonSerializable(typeof(BackupSentenceRange))]
[JsonSerializable(typeof(BackupOccurrence))]
[JsonSerializable(typeof(BackupVocabularyItem))]
[JsonSerializable(typeof(BackupEncounteredForm))]
[JsonSerializable(typeof(BackupAutomaticLearningState))]
[JsonSerializable(typeof(BackupLegacyReviewSummary))]
[JsonSerializable(typeof(BackupSense))]
[JsonSerializable(typeof(BackupPreparedItemV2))]
[JsonSerializable(typeof(BackupSourceReference))]
[JsonSerializable(typeof(BackupContextSnapshotV2))]
[JsonSerializable(typeof(BackupAnswerVariant))]
[JsonSerializable(typeof(BackupSenseAnswerVariantAssignment))]
[JsonSerializable(typeof(BackupAnswerVariantProgress))]
[JsonSerializable(typeof(BackupLearningDataV2))]
[JsonSerializable(typeof(BackupLearningCardV2))]
[JsonSerializable(typeof(BackupLearningReviewV2))]
[JsonSerializable(typeof(BackupWorkflowDataV2))]
[JsonSerializable(typeof(BackupVocabularyReviewWorkflow))]
[JsonSerializable(typeof(BackupVocabularyReviewItem))]
[JsonSerializable(typeof(BackupPreparationWorkflow))]
[JsonSerializable(typeof(BackupPreparationItem))]
[JsonSerializable(typeof(BackupLookupDraft))]
[JsonSerializable(typeof(BackupLookupMeaning))]
[JsonSerializable(typeof(BackupFormRelation))]
[JsonSerializable(typeof(BackupLearningWorkflowV2))]
[JsonSerializable(typeof(BackupLearningQueueItemV2))]
[JsonSerializable(typeof(BackupDerivedTermEvidenceV2))]
[JsonSerializable(typeof(BackupExtensions))]
[JsonSerializable(typeof(BackupExtensionPayload))]
[JsonSerializable(typeof(BackupFormatVersionEnvelope))]
[JsonSerializable(typeof(BackupError))]
internal sealed partial class BackupJsonSerializerContextV2 : JsonSerializerContext
{
}
