using System.Text.Json.Serialization;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BackupManifestV4))]
[JsonSerializable(typeof(BackupRecordCountsV4))]
[JsonSerializable(typeof(BackupPayloadV4))]
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
[JsonSerializable(typeof(BackupWordLearningControl))]
[JsonSerializable(typeof(BackupSenseLearningControl))]
[JsonSerializable(typeof(BackupLearningTarget))]
[JsonSerializable(typeof(BackupTargetAnswerVariant))]
[JsonSerializable(typeof(BackupTargetFsrsState))]
[JsonSerializable(typeof(BackupTargetFsrsReviewHistoryEntry))]
[JsonSerializable(typeof(BackupTargetReview))]
[JsonSerializable(typeof(BackupExtensions))]
[JsonSerializable(typeof(BackupExtensionPayload))]
[JsonSerializable(typeof(BackupFormatVersionEnvelope))]
[JsonSerializable(typeof(BackupError))]
internal sealed partial class BackupJsonSerializerContextV4 : JsonSerializerContext
{
}
