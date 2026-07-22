using System.Security.Cryptography;
using System.Text;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

public static class BackupModelContract
{
    public static void ValidateManifest(BackupManifest manifest)
    {
        Require(manifest, BackupErrorCodes.ManifestInvalid);
        if (manifest.FormatVersion != BackupFormatLimits.FormatVersion)
        {
            throw Error(BackupErrorCodes.UnsupportedFormat);
        }

        ValidateRequiredString(manifest.SourceAppVersion);
        ValidateNonNegative(manifest.SourceDatabaseSchemaVersion);
        ValidateUtc(manifest.CreatedAtUtc);
        BackupEnumMappings.ToExternalString(manifest.SourcePlatform);
        ValidateRecordCounts(Require(manifest.RecordCounts, BackupErrorCodes.ManifestInvalid));
        ValidateChecksum(manifest.DataChecksum, includesPrefix: true);
        ValidateFeatureList(manifest.OptionalFeatures);
        ValidateFeatureList(manifest.RequiredFeatures);

        var duplicateFeature = manifest.OptionalFeatures.Intersect(
            manifest.RequiredFeatures,
            StringComparer.Ordinal).Any();
        if (duplicateFeature)
        {
            throw Error(BackupErrorCodes.ManifestInvalid);
        }
    }

    public static void ValidatePayload(BackupPayload payload)
    {
        Require(payload, BackupErrorCodes.InvariantViolation);
        var sourceMaterials = ValidateCollection(payload.SourceMaterials, BackupFormatLimits.MaxSourceMaterials);
        var vocabulary = ValidateCollection(payload.Vocabulary, BackupFormatLimits.MaxVocabularyItems);
        var preparedLearning = ValidateCollection(payload.PreparedLearning);
        var learning = Require(payload.Learning, BackupErrorCodes.InvariantViolation);
        var workflows = Require(payload.Workflows, BackupErrorCodes.InvariantViolation);
        var extensions = Require(payload.Extensions, BackupErrorCodes.InvariantViolation);

        foreach (var item in sourceMaterials)
        {
            ValidateSourceMaterial(Require(item, BackupErrorCodes.InvariantViolation));
        }

        foreach (var item in vocabulary)
        {
            ValidateVocabulary(Require(item, BackupErrorCodes.InvariantViolation));
        }

        foreach (var item in preparedLearning)
        {
            ValidatePreparedItem(Require(item, BackupErrorCodes.InvariantViolation));
        }

        foreach (var card in ValidateCollection(learning.Cards))
        {
            ValidateLearningCard(Require(card, BackupErrorCodes.InvariantViolation));
        }

        foreach (var review in ValidateCollection(learning.ReviewEvents))
        {
            ValidateLearningReview(Require(review, BackupErrorCodes.InvariantViolation));
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

        ValidateExtensions(extensions);
        ValidatePayloadRecordLimits(payload);
    }

    public static void ValidatePreview(BackupRestorePreview preview)
    {
        Require(preview, BackupErrorCodes.InvariantViolation);
        if (preview.FormatVersion != BackupFormatLimits.FormatVersion)
        {
            throw Error(BackupErrorCodes.UnsupportedFormat);
        }

        ValidateRequiredString(preview.SourceAppVersion);
        ValidateNonNegative(preview.SourceDatabaseSchemaVersion);
        ValidateUtc(preview.CreatedAtUtc);
        BackupEnumMappings.ToExternalString(preview.SourcePlatform);
        ValidateRecordCounts(Require(preview.RecordCounts, BackupErrorCodes.InvariantViolation));
        ValidateFeatureList(preview.OptionalFeatures);
        foreach (var warningCode in ValidateCollection(preview.WarningCodes, BackupFormatLimits.MaxFeatureCount))
        {
            ValidateMachineCode(warningCode);
        }

        Require(preview.ReplaceAllScope, BackupErrorCodes.InvariantViolation);
    }

    public static BackupRecordCounts CountRecords(BackupPayload payload)
    {
        ValidatePayload(payload);
        return new BackupRecordCounts(
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
            payload.Learning.Cards.Count,
            payload.Learning.ReviewEvents.Count,
            payload.Workflows.LearningSessions.Count,
            CheckedCount(payload.Workflows.LearningSessions.Sum(item => (long)item.QueueItems.Count)));
    }

    public static void ValidateRecordCounts(BackupManifest manifest, BackupPayload payload)
    {
        ValidateManifest(manifest);
        var actual = CountRecords(payload);
        if (manifest.RecordCounts != actual)
        {
            throw Error(BackupErrorCodes.RecordCountMismatch);
        }
    }

    private static void ValidateSourceMaterial(BackupSourceMaterial item)
    {
        ValidateArchiveId(item.Id);
        ValidateRequiredString(item.Title);
        ValidateRequiredString(item.TextLanguage);
        ValidateRequiredString(item.ExplanationLanguage);
        BackupEnumMappings.ToExternalString(item.LookupMode);
        ValidateLookupLanguages(item.TextLanguage, item.LookupMode, item.TargetLanguage);
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
            ValidateOptionalNonNegative(value.TechnicalInstanceYear);
            ValidateOptionalString(value.TechnicalInstanceIdentifier);
            ValidateOptionalString(value.TechnicalVariant);
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

    private static void ValidatePreparedItem(BackupPreparedItem item)
    {
        ValidateArchiveId(item.Id);
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

        if (item.ConfirmedByUser
            && item.AcronymExpansion is null
            && item.Translation is null
            && item.Definition is null)
        {
            throw Error(BackupErrorCodes.InvariantViolation);
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
            ValidateString(value.Text, BackupFormatLimits.MaxDocumentOrContextUtf8Bytes, allowEmpty: false);
            ValidateNonNegative(value.TargetStart);
            ValidatePositive(value.TargetLength);
            ValidateRequiredString(value.NormalizedFingerprint);
            ValidateUtc(value.CreatedAtUtc);
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

    private static void ValidateLearningCard(BackupLearningCard card)
    {
        ValidateArchiveId(card.Id);
        ValidateArchiveId(card.VocabularyId);
        ValidateArchiveId(card.PreparedItemId);
        BackupEnumMappings.ToExternalString(card.Direction);
        BackupEnumMappings.ToExternalString(card.State);
        ValidateUtc(card.DueAtUtc);
        ValidateNonNegative(card.IntervalDays);
        ValidateFiniteNonNegative(card.EaseFactor);
        ValidateNonNegative(card.SuccessfulReviewCount);
        ValidateNonNegative(card.LapseCount);
        ValidateOptionalUtc(card.LastReviewedAtUtc);
        if (card.LastRating is { } rating)
        {
            BackupEnumMappings.ToExternalString(rating);
        }

        ValidateUtc(card.CreatedAtUtc);
        ValidateUtc(card.UpdatedAtUtc);
    }

    private static void ValidateLearningReview(BackupLearningReview review)
    {
        ValidateArchiveId(review.CardId);
        ValidateArchiveId(review.LearningSessionId);
        BackupEnumMappings.ToExternalString(review.Rating);
        ValidateUtc(review.ReviewedAtUtc);
        ValidateUtc(review.DueAtUtc);
        ValidateNonNegative(review.IntervalDays);
        ValidateFiniteNonNegative(review.EaseFactor);
    }

    private static void ValidateVocabularyReviewWorkflow(BackupVocabularyReviewWorkflow workflow)
    {
        ValidateArchiveId(workflow.Id);
        ValidateArchiveId(workflow.SourceMaterialId);
        BackupEnumMappings.ToExternalString(workflow.Status);
        ValidateNonNegative(workflow.TotalCandidates);
        ValidateNonNegative(workflow.ReviewedCount);
        ValidateNonNegative(workflow.KnownCount);
        ValidateNonNegative(workflow.UnknownCount);
        ValidateNonNegative(workflow.IgnoredCount);
        ValidateNonNegative(workflow.DecisionSequence);
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
            ValidateNonNegative(value.PreviousTotalOccurrenceCount);
            ValidateNonNegative(value.PreviousDocumentCount);
            ValidateUtc(value.PreviousUpdatedAtUtc);
            ValidateNonNegative(value.DecisionSequence);
            ValidateOptionalUtc(value.DecidedAtUtc);
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
            ValidateNonNegative(value.SelectedMeaningIndex);
            ValidateOptionalMachineCode(value.LastErrorCode);
            ValidateNonNegative(value.LookupAttemptCount);
            ValidateUtc(value.UpdatedAtUtc);
            if (value.LookupDraft is not null)
            {
                ValidateLookupDraft(value.LookupDraft);
            }
        }
    }

    private static void ValidateLookupDraft(BackupLookupDraft draft)
    {
        BackupEnumMappings.ToExternalString(draft.Status);
        BackupEnumMappings.ToExternalString(draft.LookupMode);
        ValidateRequiredString(draft.SourceLanguage);
        ValidateRequiredString(draft.ExplanationLanguage);
        ValidateLookupLanguages(draft.SourceLanguage, draft.LookupMode, draft.TargetLanguage);
        ValidateRequiredString(draft.QueriedLemma);
        ValidateRequiredString(draft.DisplayTerm);
        BackupEnumMappings.ToExternalString(draft.TokenKind);
        ValidateOptionalString(draft.AcronymExpansion);
        ValidateOptionalString(draft.EncounteredSurfaceForm);
        ValidateOptionalString(draft.GrammaticalRelationship);

        foreach (var meaning in ValidateCollection(draft.Meanings))
        {
            var value = Require(meaning, BackupErrorCodes.InvariantViolation);
            ValidateRequiredString(value.MeaningId);
            ValidateOptionalString(value.PartOfSpeech);
            ValidateString(value.Definition, BackupFormatLimits.MaxStringUtf8Bytes, allowEmpty: true);
            ValidateOptionalString(value.Translation);
            ValidateOptionalString(value.Example);
            foreach (var label in ValidateCollection(value.UsageLabels))
            {
                ValidateRequiredString(label);
            }
        }

        ValidateSourceReference(Require(draft.Source, BackupErrorCodes.InvariantViolation));
        ValidateUtc(draft.LookupAtUtc);
        ValidateNonNegative(draft.RedirectDepth);

        foreach (var relation in ValidateCollection(draft.FormRelations))
        {
            var value = Require(relation, BackupErrorCodes.InvariantViolation);
            BackupEnumMappings.ToExternalString(value.Kind);
            ValidateRequiredString(value.BaseLemma);
            ValidateRequiredString(value.Relationship);
        }
    }

    private static void ValidateLearningWorkflow(BackupLearningWorkflow workflow)
    {
        ValidateArchiveId(workflow.Id);
        BackupEnumMappings.ToExternalString(workflow.Status);
        ValidateNonNegative(workflow.TotalCards);
        ValidateNonNegative(workflow.CompletedCards);
        ValidateNonNegative(workflow.AgainCount);
        ValidateNonNegative(workflow.HardCount);
        ValidateNonNegative(workflow.GoodCount);
        ValidateNonNegative(workflow.EasyCount);
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

            ValidateOptionalUtc(value.CompletedAtUtc);
        }
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

    private static void ValidatePayloadRecordLimits(BackupPayload payload)
    {
        var counts = CountRecordsWithoutValidation(payload);
        ValidateRecordCounts(counts);
    }

    private static BackupRecordCounts CountRecordsWithoutValidation(BackupPayload payload) => new(
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
        payload.Learning.Cards.Count,
        payload.Learning.ReviewEvents.Count,
        payload.Workflows.LearningSessions.Count,
        CheckedCount(payload.Workflows.LearningSessions.Sum(item => (long)item.QueueItems.Count)));

    private static void ValidateRecordCounts(BackupRecordCounts counts)
    {
        var values = new[]
        {
            counts.SourceMaterials,
            counts.SentenceRanges,
            counts.VocabularyItems,
            counts.EncounteredForms,
            counts.Occurrences,
            counts.PreparedItems,
            counts.ContextSnapshots,
            counts.LegacyReviewSummaries,
            counts.VocabularyReviewWorkflows,
            counts.VocabularyReviewItems,
            counts.PreparationWorkflows,
            counts.PreparationItems,
            counts.LearningCards,
            counts.LearningReviews,
            counts.LearningWorkflows,
            counts.LearningQueueItems
        };

        if (values.Any(value => value < 0)
            || counts.SourceMaterials > BackupFormatLimits.MaxSourceMaterials
            || counts.VocabularyItems > BackupFormatLimits.MaxVocabularyItems
            || counts.Occurrences > BackupFormatLimits.MaxOccurrences)
        {
            throw Error(BackupErrorCodes.LimitExceeded);
        }

        var otherRecords = values.Sum(value => (long)value)
            - counts.SourceMaterials
            - counts.VocabularyItems
            - counts.Occurrences;
        if (otherRecords > BackupFormatLimits.MaxOtherCountedRecords)
        {
            throw Error(BackupErrorCodes.LimitExceeded);
        }
    }

    private static void ValidateFeatureList(IReadOnlyList<string> features)
    {
        var values = ValidateCollection(features, BackupFormatLimits.MaxFeatureCount);
        string? previous = null;
        foreach (var feature in values)
        {
            ValidateFeatureIdentifier(feature);
            if (previous is not null
                && string.CompareOrdinal(previous, feature) >= 0)
            {
                throw Error(BackupErrorCodes.ManifestInvalid);
            }

            previous = feature;
        }
    }

    private static void ValidateLookupLanguages(
        string sourceLanguage,
        BackupLexicalLookupMode lookupMode,
        string? targetLanguage)
    {
        if (sourceLanguage is not ("en" or "de"))
        {
            throw Error(BackupErrorCodes.InvariantViolation);
        }

        if (lookupMode == BackupLexicalLookupMode.Definition)
        {
            if (targetLanguage is not null)
            {
                throw Error(BackupErrorCodes.InvariantViolation);
            }

            return;
        }

        ValidateOptionalString(targetLanguage);
        if (targetLanguage is not ("en" or "de")
            || string.Equals(sourceLanguage, targetLanguage, StringComparison.Ordinal))
        {
            throw Error(BackupErrorCodes.InvariantViolation);
        }
    }

    private static void ValidateFeatureIdentifier(string value)
    {
        ValidateString(value, BackupFormatLimits.MaxFeatureIdentifierUtf8Bytes, allowEmpty: false);
        if (value[0] == '-' || value[^1] == '-')
        {
            throw Error(BackupErrorCodes.ManifestInvalid);
        }

        var previousHyphen = false;
        foreach (var character in value)
        {
            var hyphen = character == '-';
            if ((!hyphen && !(character is >= 'a' and <= 'z') && !(character is >= '0' and <= '9'))
                || (hyphen && previousHyphen))
            {
                throw Error(BackupErrorCodes.ManifestInvalid);
            }

            previousHyphen = hyphen;
        }
    }

    private static void ValidateArchiveId(string value)
    {
        try
        {
            ValidateString(value, BackupFormatLimits.MaxArchiveIdUtf8Bytes, allowEmpty: false);
        }
        catch (BackupFormatException)
        {
            throw Error(BackupErrorCodes.InvalidArchiveId);
        }

        if (value.Any(character => character is '/' or '\\' or ':')
            || value is "." or ".."
            || value.All(character => character is >= '0' and <= '9'))
        {
            throw Error(BackupErrorCodes.InvalidArchiveId);
        }
    }

    private static void ValidateChecksum(string value, bool includesPrefix)
    {
        var expectedLength = includesPrefix ? 71 : 64;
        var offset = includesPrefix ? 7 : 0;
        if (value is null
            || value.Length != expectedLength
            || (includesPrefix && !value.StartsWith("sha256:", StringComparison.Ordinal)))
        {
            throw Error(BackupErrorCodes.ManifestInvalid);
        }

        for (var index = offset; index < value.Length; index++)
        {
            var character = value[index];
            if (!(character is >= '0' and <= '9')
                && !(character is >= 'a' and <= 'f'))
            {
                throw Error(BackupErrorCodes.ManifestInvalid);
            }
        }
    }

    private static void ValidateContentChecksum(string content, string expected)
    {
        var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))
            .ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(actual),
                Encoding.ASCII.GetBytes(expected)))
        {
            throw Error(BackupErrorCodes.ChecksumMismatch);
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
        if (value is { } timestamp)
        {
            ValidateUtc(timestamp);
        }
    }

    private static void ValidateNonNegative(int value)
    {
        if (value < 0)
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

    private static void ValidateOptionalNonNegative(int? value)
    {
        if (value is < 0)
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

    private static void ValidateFiniteNonNegative(double value)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw Error(BackupErrorCodes.InvariantViolation);
        }
    }

    private static void ValidateRequiredString(string value) =>
        ValidateString(value, BackupFormatLimits.MaxStringUtf8Bytes, allowEmpty: false);

    private static void ValidateOptionalString(string? value)
    {
        if (value is not null)
        {
            ValidateRequiredString(value);
        }
    }

    private static void ValidateMachineCode(string value)
    {
        ValidateRequiredString(value);
        if (value.Any(character =>
                character != '-'
                && !(character is >= 'a' and <= 'z')
                && !(character is >= '0' and <= '9')))
        {
            throw Error(BackupErrorCodes.InvariantViolation);
        }
    }

    private static void ValidateOptionalMachineCode(string? value)
    {
        if (value is not null)
        {
            ValidateMachineCode(value);
        }
    }

    private static void ValidateString(string value, int maxUtf8Bytes, bool allowEmpty)
    {
        if (value is null || (!allowEmpty && value.Length == 0))
        {
            throw Error(BackupErrorCodes.InvariantViolation);
        }

        if (Encoding.UTF8.GetByteCount(value) > maxUtf8Bytes)
        {
            throw Error(BackupErrorCodes.LimitExceeded);
        }
    }

    private static IReadOnlyList<T> ValidateCollection<T>(
        IReadOnlyList<T> values,
        int maximum = BackupFormatLimits.MaxArrayItems)
    {
        if (values is null)
        {
            throw Error(BackupErrorCodes.InvariantViolation);
        }

        if (values.Count > maximum)
        {
            throw Error(BackupErrorCodes.LimitExceeded);
        }

        return values;
    }

    private static T Require<T>(T? value, string errorCode)
        where T : class => value ?? throw Error(errorCode);

    private static int CheckedCount(long value)
    {
        if (value > int.MaxValue)
        {
            throw Error(BackupErrorCodes.LimitExceeded);
        }

        return (int)value;
    }

    private static BackupFormatException Error(string code) => new(code);
}
