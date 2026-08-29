using KnownFirst.Data.Entities;
using KnownFirst.Data.Schema10;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.DataSafety.Merge;
using SQLite;

namespace KnownFirst.Data.Schema8;

internal sealed record Schema8BackupImportMaps(
    IReadOnlyDictionary<string, int> WordIds,
    IReadOnlyDictionary<string, int> SenseIds,
    IReadOnlyDictionary<string, int> CardIds);

/// <summary>
/// Schema-8 counterpart of <see cref="BackupImportRepository"/> (KF-MEANING-001 Slice 2). Requires a
/// <see cref="ValidatedSchema8Capability"/> — reachable only after <c>BackupSchemaCapability.Resolve</c>
/// has proven the target is an already shape-valid, already-migrated Schema-8 database. Never reads or
/// writes <c>PRAGMA user_version</c> under any outcome — success, cancellation, or any injected/reference
/// failure — the target's version is validated once, up front, by the capability resolver and never
/// touched here.
/// </summary>
public static class Schema8BackupImportRepository
{
    /// <summary>
    /// Named, semantically-stable Schema-8 restore checkpoints (KF-MEANING-001 Slice 2 correction),
    /// fired via <see cref="IBackupImportFailureInjector.AtCheckpoint"/> in addition to the existing raw
    /// <see cref="IBackupImportFailureInjector.AfterMutation"/> count. Each name identifies an exact,
    /// stable boundary in <see cref="ImportIntoEmptySchema8Database"/> rather than an incidental mutation
    /// index that shifts whenever an unrelated earlier collection gains or loses a row.
    /// </summary>
    public static class Checkpoints
    {
        public const string AfterSenseInsertion = "AfterSenseInsertion";
        public const string DuringMeaningVariantAssignmentInsertion = "DuringMeaningVariantAssignmentInsertion";
        public const string AfterDefaultMeaningBackfill = "AfterDefaultMeaningBackfill";
        public const string AfterCardsBeforeLearningHistory = "AfterCardsBeforeLearningHistory";
        public const string BeforeCompletion = "BeforeCompletion";
    }

    public static bool HasDurableUserData(SQLiteConnection connection) =>
        connection.Table<DocumentEntity>().Count() != 0
        || connection.Table<WordEntity>().Count() != 0
        || connection.Table<WordFormEntity>().Count() != 0
        || connection.Table<SentenceSpanEntity>().Count() != 0
        || connection.Table<WordOccurrenceEntity>().Count() != 0
        || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Meanings") != 0
        || connection.Table<ReviewStateEntity>().Count() != 0
        || connection.Table<ReviewSessionEntity>().Count() != 0
        || connection.Table<ReviewCandidateEntity>().Count() != 0
        || connection.Table<PreparationSessionEntity>().Count() != 0
        || connection.Table<PreparationCandidateEntity>().Count() != 0
        || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM ContextSnapshots") != 0
        || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningCards") != 0
        || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningReviews") != 0
        || connection.Table<LearningSessionEntity>().Count() != 0
        || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningSessionCards") != 0
        || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Senses") != 0
        || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM AnswerVariants") != 0
        || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM SenseAnswerVariantAssignments") != 0
        || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM AnswerVariantProgress") != 0;

    public static void ImportIntoEmptySchema8Database(
        SQLiteConnection connection,
        ValidatedSchema8Capability capability,
        BackupPayloadV2 payload,
        CancellationToken cancellationToken,
        IBackupImportFailureInjector? failureInjector = null)
    {
        _ = ImportIntoEmptySchema8DatabaseWithMappings(
            connection,
            capability,
            payload,
            cancellationToken,
            failureInjector);
    }

    internal static Schema8BackupImportMaps ImportIntoEmptySchema8DatabaseWithMappings(
        SQLiteConnection connection,
        ValidatedSchema8Capability capability,
        BackupPayloadV2 payload,
        CancellationToken cancellationToken,
        IBackupImportFailureInjector? failureInjector = null)
    {
        ArgumentNullException.ThrowIfNull(capability);

        if (HasDurableUserData(connection))
        {
            throw new InvalidOperationException(BackupErrorCodes.TargetNotEmpty);
        }

        var mutationCount = 0;
        var documentIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var sentenceIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var wordIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var senseIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var meaningIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var variantIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var cardIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var reviewSessionIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var reviewCandidateIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var preparationSessionIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var learningSessionIds = new Dictionary<string, int>(StringComparer.Ordinal);

        // 1. Documents + Sentences.
        foreach (var source in payload.SourceMaterials)
        {
            var document = new DocumentEntity
            {
                Title = source.Title,
                TextLanguage = source.TextLanguage,
                ExplanationLanguage = source.ExplanationLanguage,
                LookupMode = BackupEnumMappings.ToPersistence(source.LookupMode),
                TargetLanguage = source.TargetLanguage ?? string.Empty,
                Content = source.OriginalText,
                ContentFingerprint = source.ContentSha256,
                ImportedAt = source.ImportedAtUtc,
                WordCount = source.StoredWordCount
            };
            Insert(connection, document, cancellationToken, failureInjector, ref mutationCount);
            documentIds.Add(source.Id, document.Id);

            foreach (var sourceSentence in source.Sentences.OrderBy(item => item.Order))
            {
                var sentence = new SentenceSpanEntity
                {
                    DocumentId = document.Id,
                    StartPosition = sourceSentence.Start,
                    Length = sourceSentence.Length,
                    Order = sourceSentence.Order
                };
                Insert(connection, sentence, cancellationToken, failureInjector, ref mutationCount);
                sentenceIds.Add(sourceSentence.Id, sentence.Id);
            }
        }

        // 2. Vocabulary (Words) + WordForms + ReviewStates.
        foreach (var source in payload.Vocabulary)
        {
            var word = new WordEntity
            {
                Language = source.Language,
                CanonicalTerm = source.CanonicalTerm,
                NormalizedTerm = source.IdentityKey,
                TokenKind = BackupEnumMappings.ToPersistence(source.TokenKind),
                Status = BackupEnumMappings.ToPersistence(source.KnowledgeState),
                PreparationState = BackupEnumMappings.ToPersistence(source.PreparationState),
                TotalOccurrenceCount = source.TotalOccurrenceCount,
                DocumentCount = source.DocumentCount,
                AutomaticInteractionMode = BackupEnumMappings.ToPersistence(source.AutomaticLearning.InteractionMode),
                ConsecutiveRecallSuccessCount = source.AutomaticLearning.ConsecutiveRecallSuccessCount,
                ConsecutiveTypingSuccessCount = source.AutomaticLearning.ConsecutiveTypingSuccessCount,
                ConsecutiveTypingFailureCount = source.AutomaticLearning.ConsecutiveTypingFailureCount,
                MasteryReviewExtensionScheduled = source.AutomaticLearning.MasteryReviewExtensionScheduled,
                CreatedAt = source.CreatedAtUtc,
                UpdatedAt = source.UpdatedAtUtc
            };
            Insert(connection, word, cancellationToken, failureInjector, ref mutationCount);
            wordIds.Add(source.Id, word.Id);

            foreach (var sourceForm in source.EncounteredForms)
            {
                Insert(connection, new WordFormEntity
                {
                    WordId = word.Id,
                    SurfaceForm = sourceForm.SurfaceForm,
                    OccurrenceCount = sourceForm.OccurrenceCount
                }, cancellationToken, failureInjector, ref mutationCount);
            }

            foreach (var sourceSummary in source.LegacyReviewSummaries)
            {
                Insert(connection, new ReviewStateEntity
                {
                    WordId = word.Id,
                    ReviewCount = sourceSummary.ReviewCount,
                    ForgotCount = sourceSummary.ForgotCount,
                    PartialCount = sourceSummary.PartialCount,
                    KnownCount = sourceSummary.KnownCount,
                    LastReviewedAt = sourceSummary.LastReviewedAtUtc
                }, cancellationToken, failureInjector, ref mutationCount);
            }
        }

        // 3. Occurrences (second pass — needs Documents/Sentences/Vocabulary).
        foreach (var source in payload.SourceMaterials)
        {
            var documentId = RequireId(documentIds, source.Id);
            foreach (var sourceOccurrence in source.Occurrences.OrderBy(item => item.Order))
            {
                Insert(connection, new WordOccurrenceEntity
                {
                    WordId = RequireId(wordIds, sourceOccurrence.VocabularyId),
                    DocumentId = documentId,
                    SentenceSpanId = RequireId(sentenceIds, sourceOccurrence.SentenceId),
                    StartPosition = sourceOccurrence.Start,
                    Length = sourceOccurrence.Length,
                    SurfaceForm = sourceOccurrence.SurfaceForm,
                    TechnicalFamily = BackupEnumMappings.ToPersistence(sourceOccurrence.TechnicalFamily),
                    TechnicalInstanceYear = sourceOccurrence.TechnicalInstanceYear,
                    TechnicalInstanceIdentifier = sourceOccurrence.TechnicalInstanceIdentifier ?? string.Empty,
                    TechnicalVariant = sourceOccurrence.TechnicalVariant ?? string.Empty,
                    Order = sourceOccurrence.Order
                }, cancellationToken, failureInjector, ref mutationCount);
            }
        }

        // 4. Senses, DefaultMeaningId inserted as NULL regardless of the archive value (deferred —
        // Sense.DefaultMeaningId may reference a Meaning, which does not exist yet).
        foreach (var source in payload.Senses)
        {
            var senseId = InsertRaw(
                connection,
                """
                INSERT INTO Senses
                    (StableId, WordId, SourceLanguage, ExplanationLanguage, ProviderSenseId, TopicOrDomain,
                     PartOfSpeech, GrammaticalRelationship, AcronymExpansion, DefaultMeaningId, Status,
                     CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, NULL, ?, ?, ?)
                """,
                cancellationToken, failureInjector, ref mutationCount,
                source.StableId, RequireId(wordIds, source.VocabularyId), source.SourceLanguage, source.ExplanationLanguage,
                source.ProviderSenseId, source.TopicOrDomain, source.PartOfSpeech, source.GrammaticalRelationship,
                source.AcronymExpansion, (int)source.Status, source.CreatedAtUtc, source.UpdatedAtUtc);
            senseIds.Add(source.Id, senseId);
        }

        failureInjector?.AtCheckpoint(Checkpoints.AfterSenseInsertion);

        // 5. Meanings, referencing the Vocabulary and Sense maps.
        foreach (var source in payload.PreparedLearning)
        {
            var meaningId = InsertRaw(
                connection,
                """
                INSERT INTO Meanings
                    (WordId, SenseId, StableId, ExplanationLanguage, SourceLanguage, DisplayTerm,
                     EncounteredSurfaceForm, GrammaticalRelationship, TokenKind, SelectedMeaningId,
                     AcronymExpansion, Translation, Definition, DictionaryExample, AdditionalNote,
                     AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle,
                     SourceRevisionId, Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                cancellationToken, failureInjector, ref mutationCount,
                RequireId(wordIds, source.VocabularyId), RequireId(senseIds, source.SenseId), source.StableId,
                source.ExplanationLanguage, source.SourceLanguage, source.DisplayTerm,
                source.EncounteredSurfaceForm ?? string.Empty, source.GrammaticalRelationship ?? string.Empty,
                (int)BackupEnumMappings.ToPersistence(source.TokenKind), source.ProviderMeaningId ?? string.Empty,
                source.AcronymExpansion ?? string.Empty, source.Translation ?? string.Empty, source.Definition ?? string.Empty,
                source.DictionaryExample ?? string.Empty, source.AdditionalNote ?? string.Empty,
                System.Text.Json.JsonSerializer.Serialize(source.AcceptedAliases.ToArray(), Core.Preparation.LexicalJsonSerializerContext.Default.StringArray),
                source.LegacyAnswerText ?? string.Empty, source.Source.ProviderName, source.Source.SourceProject,
                source.Source.PageTitle, source.Source.RevisionId, source.Source.Attribution, source.ConfirmedByUser,
                source.CreatedAtUtc, source.UpdatedAtUtc, source.PreparedAtUtc);
            meaningIds.Add(source.Id, meaningId);
        }

        // 6. Backfill stage — its own independently fault-injectable checkpoint.
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var source in payload.Senses)
        {
            if (source.DefaultMeaningId is null)
            {
                continue;
            }

            var senseLocalId = RequireId(senseIds, source.Id);
            var meaningLocalId = RequireId(meaningIds, source.DefaultMeaningId);
            connection.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", meaningLocalId, senseLocalId);
            mutationCount++;
            failureInjector?.AfterMutation(mutationCount);
        }

        failureInjector?.AtCheckpoint(Checkpoints.AfterDefaultMeaningBackfill);

        // 7. AnswerVariants.
        foreach (var source in payload.AnswerVariants)
        {
            var variantId = InsertRaw(
                connection,
                """
                INSERT INTO AnswerVariants
                    (StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                cancellationToken, failureInjector, ref mutationCount,
                source.StableId, RequireId(senseIds, source.SenseId), source.AnswerLanguage, source.DisplayText,
                source.NormalizedText, source.SourceMeaningId is null ? null : (int?)RequireId(meaningIds, source.SourceMeaningId),
                source.CreatedAtUtc, source.UpdatedAtUtc);
            variantIds.Add(source.Id, variantId);
            failureInjector?.AtCheckpoint(Checkpoints.DuringMeaningVariantAssignmentInsertion);
        }

        // 8. SenseAnswerVariantAssignments.
        foreach (var source in payload.SenseAnswerVariantAssignments)
        {
            InsertRaw(
                connection,
                """
                INSERT INTO SenseAnswerVariantAssignments
                    (StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                cancellationToken, failureInjector, ref mutationCount,
                source.StableId, RequireId(senseIds, source.SenseId), (int)BackupEnumMappings.ToPersistence(source.CardDirection),
                RequireId(variantIds, source.AnswerVariantId), (int)source.Requirement, source.IsPreferred,
                source.RequiredSinceUtc, source.CreatedAtUtc, source.UpdatedAtUtc);
            failureInjector?.AtCheckpoint(Checkpoints.DuringMeaningVariantAssignmentInsertion);
        }

        // 9. Contexts, referencing the Meaning and Sense maps; SenseId validated to agree with the parent
        // Meaning's Sense (already enforced at the payload-graph-validation stage, before restore ever
        // begins — this is a defensive re-check, not a redundant primary validation).
        foreach (var source in payload.PreparedLearning)
        {
            var meaningLocalId = RequireId(meaningIds, source.Id);
            foreach (var sourceContext in source.Contexts)
            {
                if (sourceContext.SenseId != source.SenseId)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }

                InsertRaw(
                    connection,
                    """
                    INSERT INTO ContextSnapshots
                        (MeaningId, SenseId, WordId, SourceDocumentId, SourceDocumentTitle, Text, TargetStart,
                         TargetLength, NormalizedFingerprint, CreatedAtUtc)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    cancellationToken, failureInjector, ref mutationCount,
                    meaningLocalId, RequireId(senseIds, sourceContext.SenseId), RequireId(wordIds, source.VocabularyId),
                    RequireId(documentIds, sourceContext.SourceMaterialId), sourceContext.SourceTitle, sourceContext.Text,
                    sourceContext.TargetStart, sourceContext.TargetLength, sourceContext.NormalizedFingerprint,
                    sourceContext.CreatedAtUtc);
            }
        }

        // 10. Cards, referencing Vocabulary/Sense/Meaning(PreferredMeaningId).
        foreach (var source in payload.Learning.Cards)
        {
            var cardId = InsertRaw(
                connection,
                """
                INSERT INTO LearningCards
                    (WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor,
                     SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                cancellationToken, failureInjector, ref mutationCount,
                RequireId(wordIds, source.VocabularyId), RequireId(senseIds, source.SenseId),
                RequireId(meaningIds, source.PreferredMeaningId), (int)BackupEnumMappings.ToPersistence(source.Direction),
                (int)BackupEnumMappings.ToPersistence(source.State), source.DueAtUtc, source.IntervalDays, source.EaseFactor,
                source.SuccessfulReviewCount, source.LapseCount, source.LastReviewedAtUtc,
                source.LastRating is null ? null : (int?)BackupEnumMappings.ToPersistence(source.LastRating.Value),
                source.CreatedAtUtc, source.UpdatedAtUtc);
            cardIds.Add(source.Id, cardId);
        }

        failureInjector?.AtCheckpoint(Checkpoints.AfterCardsBeforeLearningHistory);

        // 11. Workflow parents/children, queue rows, reviews, progress.
        foreach (var source in payload.Workflows.VocabularyReviews)
        {
            var session = new ReviewSessionEntity
            {
                DocumentId = RequireId(documentIds, source.SourceMaterialId),
                Status = BackupEnumMappings.ToPersistence(source.Status),
                TotalCandidates = source.TotalCandidates,
                ReviewedCount = source.ReviewedCount,
                KnownCount = source.KnownCount,
                UnknownCount = source.UnknownCount,
                IgnoredCount = source.IgnoredCount,
                DecisionSequence = source.DecisionSequence,
                StartedAt = source.StartedAtUtc,
                CompletedAt = source.CompletedAtUtc
            };
            Insert(connection, session, cancellationToken, failureInjector, ref mutationCount);
            reviewSessionIds.Add(source.Id, session.Id);

            foreach (var sourceItem in source.Items.OrderBy(item => item.Order))
            {
                var candidateEntity = new ReviewCandidateEntity
                {
                    SessionId = session.Id,
                    WordId = RequireId(wordIds, sourceItem.VocabularyId),
                    Order = sourceItem.Order,
                    Status = BackupEnumMappings.ToPersistence(sourceItem.Status),
                    PreviousWordStatus = BackupEnumMappings.ToPersistence(sourceItem.PreviousKnowledgeState),
                    PreviousTotalOccurrenceCount = sourceItem.PreviousTotalOccurrenceCount,
                    PreviousDocumentCount = sourceItem.PreviousDocumentCount,
                    PreviousUpdatedAt = sourceItem.PreviousUpdatedAtUtc,
                    DecisionSequence = sourceItem.DecisionSequence,
                    WasWordCreatedForSession = sourceItem.WasVocabularyCreatedForSession,
                    DecidedAt = sourceItem.DecidedAtUtc
                };
                Insert(connection, candidateEntity, cancellationToken, failureInjector, ref mutationCount);
                reviewCandidateIds.Add(sourceItem.Id, candidateEntity.Id);
            }
        }

        // German Enhanced Term Recognition Package 5A-2: transported DerivedTermEvidence rows, resolved to
        // the just-inserted local ReviewCandidate ids above. Never synthesizes a WordOccurrence.
        foreach (var source in payload.DerivedTermEvidence)
        {
            Insert(connection, new DerivedTermEvidenceEntity
            {
                ReviewCandidateId = RequireId(reviewCandidateIds, source.ReviewItemId),
                SourceIdentity = source.SourceIdentity,
                SourceSurfaceForm = source.SourceSurfaceForm,
                SourceStartPosition = source.SourceStartPosition,
                SourceLength = source.SourceLength,
                SourceSentenceOrder = source.SourceSentenceOrder,
                ComponentForm = source.ComponentForm
            }, cancellationToken, failureInjector, ref mutationCount);
        }

        foreach (var source in payload.Workflows.PreparationBatches)
        {
            var session = new PreparationSessionEntity
            {
                Status = BackupEnumMappings.ToPersistence(source.Status),
                Method = BackupEnumMappings.ToPersistence(source.Method),
                TotalItems = source.TotalItems,
                CompletedItems = source.CompletedItems,
                StartedAtUtc = source.StartedAtUtc,
                UpdatedAtUtc = source.UpdatedAtUtc,
                CompletedAtUtc = source.CompletedAtUtc
            };
            Insert(connection, session, cancellationToken, failureInjector, ref mutationCount);
            preparationSessionIds.Add(source.Id, session.Id);

            foreach (var sourceItem in source.Items.OrderBy(item => item.Order))
            {
                Insert(connection, new PreparationCandidateEntity
                {
                    SessionId = session.Id,
                    WordId = RequireId(wordIds, sourceItem.VocabularyId),
                    Order = sourceItem.Order,
                    Status = BackupEnumMappings.ToPersistence(sourceItem.Status),
                    ResultJson = sourceItem.LookupDraft is null ? string.Empty : SerializeLookupDraft(sourceItem.LookupDraft),
                    SelectedMeaningIndex = sourceItem.SelectedMeaningIndex,
                    LastErrorCode = sourceItem.LastErrorCode ?? string.Empty,
                    LookupAttemptCount = sourceItem.LookupAttemptCount,
                    UpdatedAtUtc = sourceItem.UpdatedAtUtc
                }, cancellationToken, failureInjector, ref mutationCount);
            }
        }

        // Persistent learning-workflow identities for the incoming archive: preserved when the archive
        // carries them (Schema-10 source), deterministically reconstructed when it does not (legacy
        // Schema-8/9 source, whose portable workflows are Completed). Resolved once, before the first
        // insert, so a session and its queue rows can never disagree about which identity they belong to.
        var archiveStableIds = LearningWorkflowStableIdArchiveResolver.Resolve(payload);

        foreach (var source in payload.Workflows.LearningSessions)
        {
            var sessionInsert = Schema10LearningIdentityWriter.BuildSessionInsert(
                connection,
                (int)BackupEnumMappings.ToPersistence(source.Status),
                source.TotalCards, source.CompletedCards, source.AgainCount, source.HardCount,
                source.GoodCount, source.EasyCount, source.StartedAtUtc, source.UpdatedAtUtc,
                source.CompletedAtUtc,
                archiveStableIds.WorkflowStableIdsByArchiveId.GetValueOrDefault(source.Id));
            InsertRaw(
                connection, sessionInsert.Sql, cancellationToken, failureInjector, ref mutationCount,
                sessionInsert.Arguments);
            var sessionId = (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
            learningSessionIds.Add(source.Id, sessionId);

            foreach (var sourceItem in source.QueueItems.OrderBy(item => item.QueueOrder))
            {
                var queueInsert = Schema10LearningIdentityWriter.BuildQueueInsert(
                    connection,
                    sessionId, RequireId(cardIds, sourceItem.CardId), sourceItem.QueueOrder, sourceItem.IsDueCard,
                    sourceItem.IsAgainRepeat, sourceItem.AnswerRevealed, sourceItem.SpellingChecked,
                    sourceItem.SpellingCorrect, sourceItem.IsCompleted,
                    sourceItem.Rating is null ? null : (int?)BackupEnumMappings.ToPersistence(sourceItem.Rating.Value),
                    sourceItem.CompletedAtUtc,
                    sourceItem.TargetAnswerVariantId is null ? null : (int?)RequireId(variantIds, sourceItem.TargetAnswerVariantId),
                    archiveStableIds.QueueStableIdsByArchiveId.GetValueOrDefault(sourceItem.Id));
                InsertRaw(
                    connection, queueInsert.Sql, cancellationToken, failureInjector, ref mutationCount,
                    queueInsert.Arguments);
            }
        }

        foreach (var source in payload.Learning.ReviewEvents)
        {
            InsertRaw(
                connection,
                """
                INSERT INTO LearningReviews
                    (CardId, SessionId, Rating, WasTypedAnswer, WasCorrect, ReviewedAtUtc, DueAtUtc, IntervalDays,
                     EaseFactor, TargetAnswerVariantId, MatchedAnswerVariantId)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                cancellationToken, failureInjector, ref mutationCount,
                RequireId(cardIds, source.CardId), RequireId(learningSessionIds, source.LearningSessionId),
                (int)BackupEnumMappings.ToPersistence(source.Rating), source.WasTypedAnswer, source.WasCorrect,
                source.ReviewedAtUtc, source.DueAtUtc, source.IntervalDays, source.EaseFactor,
                source.TargetAnswerVariantId is null ? null : (int?)RequireId(variantIds, source.TargetAnswerVariantId),
                source.MatchedAnswerVariantId is null ? null : (int?)RequireId(variantIds, source.MatchedAnswerVariantId));
        }

        foreach (var source in payload.AnswerVariantProgress)
        {
            InsertRaw(
                connection,
                """
                INSERT INTO AnswerVariantProgress
                    (CardId, AnswerVariantId, InteractionMode, ConsecutiveReadingSuccessCount,
                     ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, LastAssessedAtUtc,
                     MasteryReviewExtensionScheduled, IsMastered, ReplayVersion, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                cancellationToken, failureInjector, ref mutationCount,
                RequireId(cardIds, source.CardId), RequireId(variantIds, source.AnswerVariantId),
                (int)BackupEnumMappings.ToPersistence(source.InteractionMode), source.ConsecutiveReadingSuccessCount,
                source.ConsecutiveTypingSuccessCount, source.ConsecutiveTypingFailureCount, source.LastAssessedAtUtc,
                source.MasteryReviewExtensionScheduled, source.IsMastered, source.ReplayVersion,
                source.CreatedAtUtc, source.UpdatedAtUtc);
        }

        failureInjector?.AtCheckpoint(Checkpoints.BeforeCompletion);

        // Final validation pass — confirms every deferred reference, especially the backfilled
        // Sense.DefaultMeaningId values, actually resolves before returning success.
        var unresolvedDefaultMeaning = connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*) FROM Senses s
            LEFT JOIN Meanings m ON m.Id = s.DefaultMeaningId
            WHERE s.DefaultMeaningId IS NOT NULL AND (m.Id IS NULL OR m.SenseId <> s.Id)
            """);
        if (unresolvedDefaultMeaning > 0)
        {
            throw new BackupFormatException(BackupErrorCodes.MissingReference);
        }

        return new Schema8BackupImportMaps(wordIds, senseIds, cardIds);
    }

    /// <summary>Internal (not private) so the Slice 8 populated-target merge writer can reuse the exact
    /// same field mapping when re-inserting a preserved-divergent-history PreparationCandidate — avoids a
    /// second, drifting copy of this mechanical projection.</summary>
    internal static string SerializeLookupDraft(BackupLookupDraft source)
    {
        var result = new Core.Preparation.LexicalResult(
            BackupEnumMappings.ToPersistence(source.Status),
            source.QueriedLemma,
            source.DisplayTerm,
            BackupEnumMappings.ToPersistence(source.TokenKind),
            source.SourceLanguage,
            source.ExplanationLanguage,
            source.AcronymExpansion,
            source.Meanings.Select(meaning => new Core.Preparation.LexicalMeaning(
                meaning.MeaningId, meaning.PartOfSpeech, meaning.Definition, meaning.Translation, meaning.Example, meaning.UsageLabels)).ToList(),
            source.Source.ProviderName, source.Source.SourceProject, source.Source.PageTitle,
            source.Source.RevisionId, source.Source.Attribution, source.LookupAtUtc,
            IsFromCache: false, ErrorCode: null, EncounteredSurfaceForm: source.EncounteredSurfaceForm,
            GrammaticalRelationship: source.GrammaticalRelationship, RedirectDepth: source.RedirectDepth,
            FormRelations: source.FormRelations.Select(relation => new Core.Preparation.ProviderFormRelation(
                BackupEnumMappings.ToPersistence(relation.Kind), relation.BaseLemma, relation.Relationship)).ToList(),
            Diagnostics: null, LookupMode: BackupEnumMappings.ToPersistence(source.LookupMode), TargetLanguage: source.TargetLanguage);

        return System.Text.Json.JsonSerializer.Serialize(result, Core.Preparation.LexicalJsonSerializerContext.Default.LexicalResult);
    }

    private static int RequireId(IReadOnlyDictionary<string, int> ids, string archiveId)
    {
        if (!ids.TryGetValue(archiveId, out var id))
        {
            throw new BackupFormatException(BackupErrorCodes.MissingReference);
        }
        return id;
    }

    /// <summary>Internal (not private) so the Slice 8 populated-target merge writer can reuse the exact
    /// same cancellation/mutation-count/failure-injector plumbing for its own inserts.</summary>
    internal static void Insert(
        SQLiteConnection connection, object entity, CancellationToken cancellationToken,
        IBackupImportFailureInjector? failureInjector, ref int mutationCount)
    {
        cancellationToken.ThrowIfCancellationRequested();
        connection.Insert(entity);
        mutationCount++;
        failureInjector?.AfterMutation(mutationCount);
    }

    /// <summary>Internal (not private) — see <see cref="Insert"/>.</summary>
    internal static int InsertRaw(
        SQLiteConnection connection, string sql, CancellationToken cancellationToken,
        IBackupImportFailureInjector? failureInjector, ref int mutationCount, params object?[] args)
    {
        cancellationToken.ThrowIfCancellationRequested();
        connection.Execute(sql, args);
        mutationCount++;
        failureInjector?.AfterMutation(mutationCount);
        return (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
    }
}
