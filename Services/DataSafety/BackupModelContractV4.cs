using System.Security.Cryptography;
using System.Text;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Data.Schema10;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

/// <summary>
/// Archive format v4 field/collection/enum contract validation (KF-LEARN-011 Slice 5).
/// Validates Schema-14 LearningTarget runtime data and target-centric FSRS scheduling.
/// </summary>
public static class BackupModelContractV4
{
    public const int Schema14Version = 14;

    public static void ValidateManifest(BackupManifestV4 manifest)
    {
        Require(manifest, BackupErrorCodes.ManifestInvalid);
        if (manifest.FormatVersion != BackupFormatLimits.FormatVersionV4)
        {
            throw Error(BackupErrorCodes.UnsupportedFormat);
        }

        ValidateRequiredString(manifest.SourceAppVersion);
        if (manifest.SourceDatabaseSchemaVersion != Schema14Version)
        {
            throw Error(BackupErrorCodes.ManifestInvalid);
        }

        ValidateUtc(manifest.CreatedAtUtc);
        BackupEnumMappings.ToExternalString(manifest.SourcePlatform);
        ValidateRecordCountsShape(Require(manifest.RecordCounts, BackupErrorCodes.ManifestInvalid));
        ValidateChecksum(manifest.DataChecksum, includesPrefix: true);
        ValidateFeatureList(manifest.OptionalFeatures);
        ValidateFeatureList(manifest.RequiredFeatures);

        if (manifest.OptionalFeatures.Intersect(manifest.RequiredFeatures, StringComparer.Ordinal).Any())
        {
            throw Error(BackupErrorCodes.ManifestInvalid);
        }
    }

    public static void ValidatePayload(BackupPayloadV4 payload)
    {
        Require(payload, BackupErrorCodes.InvariantViolation);
        var sourceMaterials = ValidateCollection(payload.SourceMaterials, BackupFormatLimits.MaxSourceMaterials);
        var vocabulary = ValidateCollection(payload.Vocabulary, BackupFormatLimits.MaxVocabularyItems);
        var senses = ValidateCollection(payload.Senses);
        var preparedLearning = ValidateCollection(payload.PreparedLearning);
        var workflows = Require(payload.Workflows, BackupErrorCodes.InvariantViolation);
        var extensions = Require(payload.Extensions, BackupErrorCodes.InvariantViolation);
        var wordControls = ValidateCollection(payload.WordLearningControls);
        var senseControls = ValidateCollection(payload.SenseLearningControls);
        var targets = ValidateCollection(payload.LearningTargets);
        var targetVariants = ValidateCollection(payload.TargetAnswerVariants);
        var targetStates = ValidateCollection(payload.TargetFsrsStates);
        var targetHistory = ValidateCollection(payload.TargetFsrsReviewHistoryEntries);
        var targetReviews = ValidateCollection(payload.TargetReviews);

        foreach (var item in sourceMaterials)
        {
            ValidateSourceMaterial(Require(item, BackupErrorCodes.InvariantViolation));
        }

        foreach (var item in vocabulary)
        {
            ValidateVocabulary(Require(item, BackupErrorCodes.InvariantViolation));
        }

        foreach (var sense in senses)
        {
            ValidateSense(Require(sense, BackupErrorCodes.InvariantViolation));
        }

        foreach (var item in preparedLearning)
        {
            ValidatePreparedItem(Require(item, BackupErrorCodes.InvariantViolation));
        }

        foreach (var workflow in ValidateCollection(workflows.VocabularyReviews))
        {
            ValidateVocabularyReviewWorkflow(Require(workflow, BackupErrorCodes.InvariantViolation));
        }

        foreach (var workflow in ValidateCollection(workflows.PreparationBatches))
        {
            ValidatePreparationWorkflow(Require(workflow, BackupErrorCodes.InvariantViolation));
        }

        foreach (var workflow in ValidateCollection(workflows.LearningSessions))
        {
            ValidateLearningWorkflow(Require(workflow, BackupErrorCodes.InvariantViolation));
        }

        foreach (var evidence in ValidateCollection(payload.DerivedTermEvidence))
        {
            ValidateDerivedTermEvidence(Require(evidence, BackupErrorCodes.InvariantViolation));
        }

        foreach (var control in wordControls)
        {
            ValidateWordLearningControl(Require(control, BackupErrorCodes.InvariantViolation));
        }

        foreach (var control in senseControls)
        {
            ValidateSenseLearningControl(Require(control, BackupErrorCodes.InvariantViolation));
        }

        foreach (var target in targets)
        {
            ValidateLearningTarget(Require(target, BackupErrorCodes.InvariantViolation));
        }

        foreach (var variant in targetVariants)
        {
            ValidateTargetAnswerVariant(Require(variant, BackupErrorCodes.InvariantViolation));
        }

        foreach (var state in targetStates)
        {
            ValidateTargetFsrsState(Require(state, BackupErrorCodes.InvariantViolation));
        }

        foreach (var entry in targetHistory)
        {
            ValidateTargetFsrsReviewHistoryEntry(Require(entry, BackupErrorCodes.InvariantViolation));
        }

        foreach (var review in targetReviews)
        {
            ValidateTargetReview(Require(review, BackupErrorCodes.InvariantViolation));
        }

        ValidateExtensions(extensions);
        ValidateRecordCountsShape(CountRecordsWithoutValidation(payload));
    }

    public static BackupRecordCountsV4 CountRecords(BackupPayloadV4 payload)
    {
        ValidatePayload(payload);
        return CountRecordsWithoutValidation(payload);
    }

    public static void ValidateRecordCounts(BackupManifestV4 manifest, BackupPayloadV4 payload)
    {
        ValidateManifest(manifest);
        var actual = CountRecords(payload);
        if (manifest.RecordCounts != actual)
        {
            throw Error(BackupErrorCodes.RecordCountMismatch);
        }

        ValidateLearningWorkflowStableIds(manifest.SourceDatabaseSchemaVersion, payload);
    }

    public static void ValidateLearningWorkflowStableIds(int sourceDatabaseSchemaVersion, BackupPayloadV4 payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var required = sourceDatabaseSchemaVersion >= ValidatedSchema10Capability.SchemaVersion;
        var seenWorkflowIds = new HashSet<string>(StringComparer.Ordinal);
        var seenQueueIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var workflow in payload.Workflows.LearningSessions)
        {
            ValidateOptionalStableId(workflow.StableId, required, seenWorkflowIds);
            foreach (var item in workflow.QueueItems)
            {
                ValidateOptionalStableId(item.StableId, required, seenQueueIds);
            }
        }
    }

    private static void ValidateOptionalStableId(string? stableId, bool required, HashSet<string> seen)
    {
        if (stableId is null)
        {
            if (required)
            {
                throw Error(BackupErrorCodes.InvariantViolation);
            }

            return;
        }

        if (!LearningWorkflowStableId.IsValid(stableId))
        {
            throw Error(BackupErrorCodes.InvariantViolation);
        }

        if (!seen.Add(stableId))
        {
            throw Error(BackupErrorCodes.DuplicateId);
        }
    }

    private static BackupRecordCountsV4 CountRecordsWithoutValidation(BackupPayloadV4 payload) => new(
        payload.SourceMaterials.Count,
        CheckedCount(payload.SourceMaterials.Sum(item => (long)item.Sentences.Count)),
        payload.Vocabulary.Count,
        CheckedCount(payload.Vocabulary.Sum(item => (long)item.EncounteredForms.Count)),
        CheckedCount(payload.SourceMaterials.Sum(item => (long)item.Occurrences.Count)),
        payload.PreparedLearning.Count,
        CheckedCount(payload.PreparedLearning.Sum(item => (long)item.Contexts.Count)),
        CheckedCount(payload.Vocabulary.Sum(item => (long)item.LegacyReviewSummaries.Count)),
        payload.Workflows.VocabularyReviews.Count,
        CheckedCount(payload.Workflows.VocabularyReviews.Sum(item => (long)item.Items.Count)),
        payload.Workflows.PreparationBatches.Count,
        CheckedCount(payload.Workflows.PreparationBatches.Sum(item => (long)item.Items.Count)),
        payload.Senses.Count,
        payload.DerivedTermEvidence.Count,
        payload.WordLearningControls.Count,
        payload.SenseLearningControls.Count,
        payload.Workflows.LearningSessions.Count,
        CheckedCount(payload.Workflows.LearningSessions.Sum(item => (long)item.QueueItems.Count)),
        payload.LearningTargets.Count,
        payload.TargetAnswerVariants.Count,
        payload.TargetFsrsStates.Count,
        payload.TargetFsrsReviewHistoryEntries.Count,
        payload.TargetReviews.Count);

    private static void ValidateWordLearningControl(BackupWordLearningControl control)
    {
        ValidateArchiveId(control.VocabularyId);
        ValidateUtc(control.DecidedAtUtc);
    }

    private static void ValidateSenseLearningControl(BackupSenseLearningControl control)
    {
        ValidateArchiveId(control.SenseId);
        ValidateUtc(control.DecidedAtUtc);
    }

    private static void ValidateLearningTarget(BackupLearningTarget target)
    {
        ValidateArchiveId(target.Id);
        ValidateRequiredString(target.StableId);
        ValidateArchiveId(target.SenseId);
        BackupEnumMappings.ToExternalString(target.TargetKind);
        ValidateRequiredString(target.SourceLanguage);
        ValidateRequiredString(target.TargetLanguage);
        ValidateUtc(target.CreatedAtUtc);
        ValidateUtc(target.UpdatedAtUtc);
    }

    private static void ValidateTargetAnswerVariant(BackupTargetAnswerVariant variant)
    {
        ValidateArchiveId(variant.Id);
        ValidateRequiredString(variant.StableId);
        ValidateArchiveId(variant.TargetId);
        ValidateRequiredString(variant.AnswerLanguage);
        ValidateRequiredString(variant.DisplayText);
        ValidateRequiredString(variant.NormalizedText);
        BackupEnumMappings.ToExternalString(variant.Requirement);
        if (variant.Requirement == BackupAnswerVariantRequirement.Required)
        {
            if (!variant.RequiredSinceUtc.HasValue)
            {
                throw Error(BackupErrorCodes.InvariantViolation);
            }
            ValidateUtc(variant.RequiredSinceUtc.Value);
        }
        else
        {
            if (variant.RequiredSinceUtc.HasValue)
            {
                throw Error(BackupErrorCodes.InvariantViolation);
            }
        }
        if (variant.SourceMeaningId is not null)
        {
            ValidateArchiveId(variant.SourceMeaningId);
        }
        ValidateUtc(variant.CreatedAtUtc);
        ValidateUtc(variant.UpdatedAtUtc);
    }

    private static void ValidateTargetFsrsState(BackupTargetFsrsState state)
    {
        ValidateArchiveId(state.TargetId);
        BackupEnumMappings.ToExternalString(state.State);

        switch (state.State)
        {
            case BackupFsrsCardStateKind.New:
                if (state.Stability.HasValue || state.Difficulty.HasValue || state.LastReviewedAtUtc.HasValue || state.StepIndex.HasValue)
                {
                    throw Error(BackupErrorCodes.InvariantViolation);
                }
                ValidateOptionalUtc(state.DueAtUtc);
                break;

            case BackupFsrsCardStateKind.Learning:
                ValidateFsrsStability(state.Stability);
                ValidateFsrsDifficulty(state.Difficulty);
                if (!state.LastReviewedAtUtc.HasValue || !state.StepIndex.HasValue || state.StepIndex.Value != 0)
                {
                    throw Error(BackupErrorCodes.InvariantViolation);
                }
                ValidateUtc(state.LastReviewedAtUtc.Value);
                ValidateOptionalUtc(state.DueAtUtc);
                break;

            case BackupFsrsCardStateKind.Review:
                ValidateFsrsStability(state.Stability);
                ValidateFsrsDifficulty(state.Difficulty);
                if (!state.LastReviewedAtUtc.HasValue || state.StepIndex.HasValue)
                {
                    throw Error(BackupErrorCodes.InvariantViolation);
                }
                ValidateUtc(state.LastReviewedAtUtc.Value);
                ValidateOptionalUtc(state.DueAtUtc);
                break;

            case BackupFsrsCardStateKind.Relearning:
                ValidateFsrsStability(state.Stability);
                ValidateFsrsDifficulty(state.Difficulty);
                if (!state.LastReviewedAtUtc.HasValue || !state.StepIndex.HasValue || state.StepIndex.Value != 0)
                {
                    throw Error(BackupErrorCodes.InvariantViolation);
                }
                ValidateUtc(state.LastReviewedAtUtc.Value);
                ValidateOptionalUtc(state.DueAtUtc);
                break;

            default:
                throw Error(BackupErrorCodes.InvariantViolation);
        }
    }

    private static void ValidateTargetFsrsReviewHistoryEntry(BackupTargetFsrsReviewHistoryEntry entry)
    {
        ValidateArchiveId(entry.Id);
        ValidateRequiredString(entry.StableId);
        ValidateArchiveId(entry.TargetId);
        ValidatePositive(entry.SequenceNumber);
        BackupEnumMappings.ToExternalString(entry.Rating);
        ValidateUtc(entry.ReviewedAtUtc);
    }

    private static void ValidateTargetReview(BackupTargetReview review)
    {
        ValidateArchiveId(review.Id);
        ValidateRequiredString(review.StableId);
        ValidateArchiveId(review.TargetId);
        ValidateArchiveId(review.SessionId);
        BackupEnumMappings.ToExternalString(review.Rating);
        if (review.TargetAnswerVariantId is not null)
        {
            ValidateArchiveId(review.TargetAnswerVariantId);
        }
        if (review.MatchedAnswerVariantId is not null)
        {
            ValidateArchiveId(review.MatchedAnswerVariantId);
        }
        ValidateUtc(review.ReviewedAtUtc);
        ValidateUtc(review.DueAtUtc);
    }

    private static void ValidateFsrsStability(double? stability)
    {
        if (!stability.HasValue || !double.IsFinite(stability.Value) || stability.Value < Fsrs6Card.MinimumStability)
        {
            throw Error(BackupErrorCodes.InvariantViolation);
        }
    }

    private static void ValidateFsrsDifficulty(double? difficulty)
    {
        if (!difficulty.HasValue
            || !double.IsFinite(difficulty.Value)
            || difficulty.Value < Fsrs6Card.MinimumDifficulty
            || difficulty.Value > Fsrs6Card.MaximumDifficulty)
        {
            throw Error(BackupErrorCodes.InvariantViolation);
        }
    }

    private static void ValidateSourceMaterial(BackupSourceMaterial item)
    {
        ValidateArchiveId(item.Id);
        ValidateRequiredString(item.Title);
        ValidateRequiredString(item.TextLanguage);
        ValidateRequiredString(item.ExplanationLanguage);
        BackupEnumMappings.ToExternalString(item.LookupMode);
        ValidateString(item.OriginalText, BackupFormatLimits.MaxDocumentOrContextUtf8Bytes, allowEmpty: true);
        ValidateChecksum(item.ContentSha256, includesPrefix: false);
        ValidateContentChecksum(item.OriginalText, item.ContentSha256);
        ValidateUtc(item.ImportedAtUtc);
        ValidateNonNegative(item.StoredWordCount);

        foreach (var sentence in ValidateCollection(item.Sentences))
        {
            var value = Require(sentence, BackupErrorCodes.InvariantViolation);
            ValidateArchiveId(value.Id);
            ValidateNonNegative(value.Order);
            ValidateNonNegative(value.Start);
            ValidatePositive(value.Length);
        }

        foreach (var occurrence in ValidateCollection(item.Occurrences))
        {
            var value = Require(occurrence, BackupErrorCodes.InvariantViolation);
            ValidateArchiveId(value.VocabularyId);
            ValidateArchiveId(value.SentenceId);
            ValidateNonNegative(value.Start);
            ValidatePositive(value.Length);
            ValidateRequiredString(value.SurfaceForm);
            ValidateNonNegative(value.Order);
            BackupEnumMappings.ToExternalString(value.TechnicalFamily);
        }
    }

    private static void ValidateVocabulary(BackupVocabularyItem item)
    {
        ValidateArchiveId(item.Id);
        ValidateRequiredString(item.Language);
        ValidateRequiredString(item.CanonicalTerm);
        ValidateRequiredString(item.IdentityKey);
        BackupEnumMappings.ToExternalString(item.TokenKind);
        BackupEnumMappings.ToExternalString(item.KnowledgeState);
        BackupEnumMappings.ToExternalString(item.PreparationState);
        ValidateNonNegative(item.TotalOccurrenceCount);
        ValidateNonNegative(item.DocumentCount);
        ValidateUtc(item.CreatedAtUtc);
        ValidateUtc(item.UpdatedAtUtc);

        if (item.KnowledgeState is BackupKnowledgeState.Prepared or BackupKnowledgeState.Learning or BackupKnowledgeState.Mastered)
        {
            throw Error(BackupErrorCodes.InvariantViolation);
        }

        foreach (var form in ValidateCollection(item.EncounteredForms))
        {
            var value = Require(form, BackupErrorCodes.InvariantViolation);
            ValidateRequiredString(value.SurfaceForm);
            ValidatePositive(value.OccurrenceCount);
        }

        var automatic = Require(item.AutomaticLearning, BackupErrorCodes.InvariantViolation);
        BackupEnumMappings.ToExternalString(automatic.InteractionMode);
        ValidateNonNegative(automatic.ConsecutiveRecallSuccessCount);
        ValidateNonNegative(automatic.ConsecutiveTypingSuccessCount);
        ValidateNonNegative(automatic.ConsecutiveTypingFailureCount);

        foreach (var summary in ValidateCollection(item.LegacyReviewSummaries))
        {
            var value = Require(summary, BackupErrorCodes.InvariantViolation);
            ValidateNonNegative(value.ReviewCount);
            ValidateNonNegative(value.ForgotCount);
            ValidateNonNegative(value.PartialCount);
            ValidateNonNegative(value.KnownCount);
            ValidateOptionalUtc(value.LastReviewedAtUtc);
        }
    }

    private static void ValidateSense(BackupSense sense)
    {
        ValidateArchiveId(sense.Id);
        ValidateRequiredString(sense.StableId);
        ValidateArchiveId(sense.VocabularyId);
        ValidateRequiredString(sense.SourceLanguage);
        ValidateRequiredString(sense.ExplanationLanguage);
        ValidateOptionalString(EmptyToNull(sense.ProviderSenseId));
        ValidateOptionalString(EmptyToNull(sense.TopicOrDomain));
        ValidateOptionalString(EmptyToNull(sense.PartOfSpeech));
        ValidateOptionalString(EmptyToNull(sense.GrammaticalRelationship));
        ValidateOptionalString(EmptyToNull(sense.AcronymExpansion));
        if (sense.DefaultMeaningId is not null)
        {
            ValidateArchiveId(sense.DefaultMeaningId);
        }

        BackupEnumMappings.ToExternalString(sense.Status);
        ValidateUtc(sense.CreatedAtUtc);
        ValidateUtc(sense.UpdatedAtUtc);
    }

    private static void ValidatePreparedItem(BackupPreparedItemV2 item)
    {
        ValidateArchiveId(item.Id);
        ValidateArchiveId(item.SenseId);
        ValidateRequiredString(item.StableId);
        ValidateArchiveId(item.VocabularyId);
        ValidateRequiredString(item.SourceLanguage);
        ValidateRequiredString(item.ExplanationLanguage);
        ValidateRequiredString(item.DisplayTerm);
        ValidateOptionalString(item.EncounteredSurfaceForm);
        ValidateOptionalString(item.GrammaticalRelationship);
        BackupEnumMappings.ToExternalString(item.TokenKind);
        ValidateOptionalString(item.ProviderMeaningId);
        ValidateOptionalString(item.AcronymExpansion);
        ValidateOptionalString(item.Translation);
        ValidateOptionalString(item.Definition);
        ValidateOptionalString(item.DictionaryExample);
        ValidateOptionalString(item.AdditionalNote);
        ValidateOptionalString(item.LegacyAnswerText);

        foreach (var alias in ValidateCollection(item.AcceptedAliases))
        {
            ValidateRequiredString(alias);
        }

        ValidateSourceReference(Require(item.Source, BackupErrorCodes.InvariantViolation));
        ValidateUtc(item.CreatedAtUtc);
        ValidateUtc(item.UpdatedAtUtc);
        ValidateUtc(item.PreparedAtUtc);

        foreach (var context in ValidateCollection(item.Contexts))
        {
            var value = Require(context, BackupErrorCodes.InvariantViolation);
            ValidateArchiveId(value.SourceMaterialId);
            ValidateRequiredString(value.SourceTitle);
            ValidateString(value.Text, BackupFormatLimits.MaxDocumentOrContextUtf8Bytes);
            ValidateNonNegative(value.TargetStart);
            ValidatePositive(value.TargetLength);
            ValidateRequiredString(value.NormalizedFingerprint);
            ValidateUtc(value.CreatedAtUtc);
            ValidateArchiveId(value.SenseId);
        }
    }

    private static void ValidateSourceReference(BackupSourceReference source)
    {
        ValidateRequiredString(source.ProviderName);
        ValidateString(source.SourceProject, BackupFormatLimits.MaxStringUtf8Bytes, allowEmpty: true);
        ValidateString(source.PageTitle, BackupFormatLimits.MaxStringUtf8Bytes, allowEmpty: true);
        ValidateOptionalNonNegative(source.RevisionId);
        ValidateString(source.Attribution, BackupFormatLimits.MaxStringUtf8Bytes, allowEmpty: true);
    }

    private static void ValidateVocabularyReviewWorkflow(BackupVocabularyReviewWorkflow workflow)
    {
        ValidateArchiveId(workflow.Id);
        ValidateArchiveId(workflow.SourceMaterialId);
        BackupEnumMappings.ToExternalString(workflow.Status);
        ValidateNonNegative(workflow.TotalCandidates);
        ValidateNonNegative(workflow.ReviewedCount);
        ValidateUtc(workflow.StartedAtUtc);
        ValidateOptionalUtc(workflow.CompletedAtUtc);

        foreach (var item in ValidateCollection(workflow.Items))
        {
            var value = Require(item, BackupErrorCodes.InvariantViolation);
            ValidateArchiveId(value.Id);
            ValidateArchiveId(value.VocabularyId);
            ValidateNonNegative(value.Order);
            BackupEnumMappings.ToExternalString(value.Status);
            BackupEnumMappings.ToExternalString(value.PreviousKnowledgeState);
        }
    }

    private static void ValidatePreparationWorkflow(BackupPreparationWorkflow workflow)
    {
        ValidateArchiveId(workflow.Id);
        BackupEnumMappings.ToExternalString(workflow.Status);
        BackupEnumMappings.ToExternalString(workflow.Method);
        ValidateNonNegative(workflow.TotalItems);
        ValidateNonNegative(workflow.CompletedItems);
        ValidateUtc(workflow.StartedAtUtc);
        ValidateUtc(workflow.UpdatedAtUtc);
        ValidateOptionalUtc(workflow.CompletedAtUtc);

        foreach (var item in ValidateCollection(workflow.Items))
        {
            var value = Require(item, BackupErrorCodes.InvariantViolation);
            ValidateArchiveId(value.Id);
            ValidateArchiveId(value.VocabularyId);
            ValidateNonNegative(value.Order);
            BackupEnumMappings.ToExternalString(value.Status);
        }
    }

    private static void ValidateLearningWorkflow(BackupLearningWorkflowV2 workflow)
    {
        ValidateArchiveId(workflow.Id);
        BackupEnumMappings.ToExternalString(workflow.Status);
        ValidateNonNegative(workflow.TotalCards);
        ValidateNonNegative(workflow.CompletedCards);
        ValidateUtc(workflow.StartedAtUtc);
        ValidateUtc(workflow.UpdatedAtUtc);
        ValidateOptionalUtc(workflow.CompletedAtUtc);

        foreach (var item in ValidateCollection(workflow.QueueItems))
        {
            var value = Require(item, BackupErrorCodes.InvariantViolation);
            ValidateArchiveId(value.Id);
            ValidateArchiveId(value.CardId);
            ValidateNonNegative(value.QueueOrder);
            if (value.Rating is { } rating)
            {
                BackupEnumMappings.ToExternalString(rating);
            }

            if (value.TargetAnswerVariantId is not null)
            {
                ValidateArchiveId(value.TargetAnswerVariantId);
            }

            ValidateOptionalUtc(value.CompletedAtUtc);
        }
    }

    private static void ValidateDerivedTermEvidence(BackupDerivedTermEvidenceV2 evidence)
    {
        ValidateArchiveId(evidence.ReviewItemId);
        ValidateRequiredString(evidence.SourceIdentity);
        ValidateRequiredString(evidence.SourceSurfaceForm);
        ValidateNonNegative(evidence.SourceStartPosition);
        ValidatePositive(evidence.SourceLength);
        ValidateNonNegative(evidence.SourceSentenceOrder);
        ValidateRequiredString(evidence.ComponentForm);
    }

    private static void ValidateExtensions(BackupExtensions extensions)
    {
        var features = Require(extensions.Features, BackupErrorCodes.InvariantViolation);
        if (features.Count > BackupFormatLimits.MaxFeatureCount)
        {
            throw Error(BackupErrorCodes.LimitExceeded);
        }

        foreach (var pair in features)
        {
            ValidateFeatureIdentifier(pair.Key);
            var payload = Require(pair.Value, BackupErrorCodes.InvariantViolation);
            ValidateString(payload.Json, BackupFormatLimits.MaxStringUtf8Bytes, allowEmpty: false);
        }
    }

    private static void ValidateRecordCountsShape(BackupRecordCountsV4 counts)
    {
        Require(counts, BackupErrorCodes.ManifestInvalid);
        ValidateNonNegative(counts.SourceMaterials);
        ValidateNonNegative(counts.SentenceRanges);
        ValidateNonNegative(counts.VocabularyItems);
        ValidateNonNegative(counts.EncounteredForms);
        ValidateNonNegative(counts.Occurrences);
        ValidateNonNegative(counts.PreparedItems);
        ValidateNonNegative(counts.ContextSnapshots);
        ValidateNonNegative(counts.LegacyReviewSummaries);
        ValidateNonNegative(counts.VocabularyReviewWorkflows);
        ValidateNonNegative(counts.VocabularyReviewItems);
        ValidateNonNegative(counts.PreparationWorkflows);
        ValidateNonNegative(counts.PreparationItems);
        ValidateNonNegative(counts.Senses);
        ValidateNonNegative(counts.DerivedTermEvidence);
        ValidateNonNegative(counts.WordLearningControls);
        ValidateNonNegative(counts.SenseLearningControls);
        ValidateNonNegative(counts.LearningWorkflows);
        ValidateNonNegative(counts.LearningQueueItems);
        ValidateNonNegative(counts.LearningTargets);
        ValidateNonNegative(counts.TargetAnswerVariants);
        ValidateNonNegative(counts.TargetFsrsStates);
        ValidateNonNegative(counts.TargetFsrsReviewHistoryEntries);
        ValidateNonNegative(counts.TargetReviews);
    }

    private static void ValidateFeatureList(IReadOnlyList<string> features)
    {
        Require(features, BackupErrorCodes.ManifestInvalid);
        if (features.Count > BackupFormatLimits.MaxFeatureCount)
        {
            throw Error(BackupErrorCodes.LimitExceeded);
        }

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in features)
        {
            ValidateFeatureIdentifier(item);
            if (!set.Add(item))
            {
                throw Error(BackupErrorCodes.ManifestInvalid);
            }
        }
    }

    private static void ValidateFeatureIdentifier(string value)
    {
        ValidateString(value, BackupFormatLimits.MaxFeatureIdentifierUtf8Bytes);
        if (!value.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.'))
        {
            throw Error(BackupErrorCodes.ManifestInvalid);
        }
    }

    private static void ValidateArchiveId(string value)
    {
        ValidateString(value, BackupFormatLimits.MaxArchiveIdUtf8Bytes);
        if (!value.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
        {
            throw Error(BackupErrorCodes.InvalidArchiveId);
        }
    }

    private static void ValidateRequiredString(string value) =>
        ValidateString(value, BackupFormatLimits.MaxStringUtf8Bytes);

    private static void ValidateOptionalString(string? value)
    {
        if (value is not null)
        {
            ValidateString(value, BackupFormatLimits.MaxStringUtf8Bytes);
        }
    }

    private static void ValidateString(string value, int maxBytes, bool allowEmpty = false)
    {
        Require(value, BackupErrorCodes.InvariantViolation);
        if (!allowEmpty && value.Length == 0)
        {
            throw Error(BackupErrorCodes.InvariantViolation);
        }

        if (Encoding.UTF8.GetByteCount(value) > maxBytes)
        {
            throw Error(BackupErrorCodes.LimitExceeded);
        }
    }

    private static void ValidateChecksum(string checksum, bool includesPrefix)
    {
        ValidateRequiredString(checksum);
        const string prefix = "sha256:";
        var value = checksum;
        if (includesPrefix)
        {
            if (!checksum.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw Error(BackupErrorCodes.ManifestInvalid);
            }

            value = checksum[prefix.Length..];
        }

        if (value.Length != 64 || !value.All(IsLowerHex))
        {
            throw Error(includesPrefix ? BackupErrorCodes.ManifestInvalid : BackupErrorCodes.InvariantViolation);
        }
    }

    private static void ValidateContentChecksum(string originalText, string checksum)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(originalText));
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        if (!string.Equals(actual, checksum, StringComparison.Ordinal))
        {
            throw Error(BackupErrorCodes.InvariantViolation);
        }
    }

    private static void ValidateUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw Error(BackupErrorCodes.InvalidTimestamp);
        }
    }

    private static void ValidateOptionalUtc(DateTime? value)
    {
        if (value.HasValue)
        {
            ValidateUtc(value.Value);
        }
    }

    private static void ValidateNonNegative(int value)
    {
        if (value < 0)
        {
            throw Error(BackupErrorCodes.InvariantViolation);
        }
    }

    private static void ValidateOptionalNonNegative(long? value)
    {
        if (value is < 0)
        {
            throw Error(BackupErrorCodes.InvariantViolation);
        }
    }

    private static void ValidatePositive(int value)
    {
        if (value <= 0)
        {
            throw Error(BackupErrorCodes.InvariantViolation);
        }
    }

    private static int CheckedCount(long value)
    {
        if (value > BackupFormatLimits.MaxArrayItems)
        {
            throw Error(BackupErrorCodes.LimitExceeded);
        }

        return checked((int)value);
    }

    private static IReadOnlyList<T> ValidateCollection<T>(
        IReadOnlyList<T> collection,
        int maximum = BackupFormatLimits.MaxArrayItems)
    {
        Require(collection, BackupErrorCodes.InvariantViolation);
        if (collection.Count > maximum)
        {
            throw Error(BackupErrorCodes.LimitExceeded);
        }

        return collection;
    }

    private static T Require<T>(T? value, string errorCode) where T : class =>
        value ?? throw Error(errorCode);

    private static BackupFormatException Error(string code) => new(code);

    private static bool IsLowerHex(char character) =>
        character is (>= '0' and <= '9') or (>= 'a' and <= 'f');

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
