using System.Globalization;
using KnownFirst.Core.Learning;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Schema8;
using KnownFirst.Models.Backup;
using SQLite;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Applies one already-validated, executable <see cref="MergePreflightPlan"/> to a populated Schema-8
/// target inside an already-open write transaction (KF-MEANING-001 Slice 8). Extends
/// <see cref="Schema8BackupImportRepository.ImportIntoEmptySchema8Database"/>'s dependency-ordered
/// insertion pattern (documents/sentences, vocabulary/forms/legacy-summaries, occurrences, senses,
/// meanings, default-meaning backfill, answer variants, assignments, contexts, cards, workflows/reviews/
/// progress) with insert/reuse/enrich branching driven by each <see cref="MergePlanAction.Classification"/>.
///
/// <para>Never computes a merge identity itself — every stable identity it needs is either the plan
/// action's own <see cref="MergePlanAction.StableIdentity"/> (resolved against a
/// <see cref="MergeWriterTargetIndex"/>) or an archive-local id already present on the source payload.
/// Never invents a conflict-resolution rule: the only "enrichment" mutations it performs (Vocabulary
/// KnowledgeState/PreparationState, PreparationWorkflow Status) recompute the exact same
/// <see cref="KnowledgeStateConflictPolicy"/>/<see cref="PreparationStateConflictPolicy"/>/
/// <see cref="WorkflowSessionStateConflictPolicy"/> result the planner already used to classify the row —
/// same code path, executed once, at write time.</para>
/// </summary>
internal sealed class MergeWriterExecutor
{
    private readonly SQLiteConnection _connection;
    private readonly MergeWriterTargetIndex _targetIndex;
    private readonly BackupPayloadV2 _archive;
    private readonly CancellationToken _cancellationToken;
    private readonly IBackupImportFailureInjector? _failureInjector;
    private readonly Dictionary<(MergeEntityKind Kind, string ArchiveLocalId), MergePlanAction> _actionsByKey;
    private readonly Dictionary<int, WordEntity> _targetWordsById;
    private readonly Dictionary<int, PreparationSessionEntity> _targetPreparationSessionsById;
    private readonly Dictionary<int, Schema8CardRow> _targetCardsById;
    private readonly HashSet<int> _cardsNeedingScheduleReplay = [];
    private static readonly ISpacedRepetitionScheduler ReplayScheduler = new SimpleSpacedRepetitionScheduler();

    private int _mutationCount;
    private readonly Dictionary<string, int> _documentIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _sentenceIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _wordIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _senseIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _meaningIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _variantIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _cardIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _reviewSessionIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _preparationSessionIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _learningSessionIds = new(StringComparer.Ordinal);

    private MergeWriterExecutor(
        SQLiteConnection connection,
        Schema8BackupSnapshot targetSnapshot,
        MergeWriterTargetIndex targetIndex,
        BackupPayloadV2 archive,
        MergePreflightPlan plan,
        CancellationToken cancellationToken,
        IBackupImportFailureInjector? failureInjector)
    {
        _connection = connection;
        _targetIndex = targetIndex;
        _archive = archive;
        _cancellationToken = cancellationToken;
        _failureInjector = failureInjector;
        _targetWordsById = targetSnapshot.Words.ToDictionary(w => w.Id);
        _targetPreparationSessionsById = targetSnapshot.PreparationSessions.ToDictionary(s => s.Id);
        _targetCardsById = targetSnapshot.LearningCards.ToDictionary(c => c.Id);

        _actionsByKey = new Dictionary<(MergeEntityKind, string), MergePlanAction>();
        foreach (var action in plan.Actions)
        {
            _actionsByKey[(action.EntityKind, action.ArchiveLocalId)] = action;
        }
    }

    public static class Checkpoints
    {
        public const string AfterSenseInsertion = "MergeWriter.AfterSenseInsertion";
        public const string AfterDefaultMeaningBackfill = "MergeWriter.AfterDefaultMeaningBackfill";
        public const string AfterCardsBeforeLearningHistory = "MergeWriter.AfterCardsBeforeLearningHistory";
        public const string DuringSchedulerReplay = "MergeWriter.DuringSchedulerReplay";
        public const string AfterSchedulerReplay = "MergeWriter.AfterSchedulerReplay";
        public const string BeforeCompletion = "MergeWriter.BeforeCompletion";
    }

    public static void Execute(
        SQLiteConnection connection,
        Schema8BackupSnapshot targetSnapshot,
        MergeWriterTargetIndex targetIndex,
        BackupPayloadV2 archive,
        MergePreflightPlan plan,
        CancellationToken cancellationToken,
        IBackupImportFailureInjector? failureInjector)
    {
        var executor = new MergeWriterExecutor(connection, targetSnapshot, targetIndex, archive, plan, cancellationToken, failureInjector);
        executor.Run();
    }

    private void Run()
    {
        WriteSourceMaterials();
        WriteVocabulary();
        WriteOccurrences();
        WriteSenses();
        WriteMeanings();
        BackfillSenseDefaultMeanings();
        WriteAnswerVariants();
        WriteAssignments();
        WriteContexts();
        WriteCards();
        _failureInjector?.AtCheckpoint(Checkpoints.AfterCardsBeforeLearningHistory);
        WriteVocabularyReviewWorkflows();
        WritePreparationWorkflows();
        WriteLearningWorkflows();
        WriteLearningReviews();
        ReplayCardSchedules();
        WriteAnswerVariantProgress();

        _failureInjector?.AtCheckpoint(Checkpoints.BeforeCompletion);

        var unresolvedDefaultMeaning = _connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*) FROM Senses s
            LEFT JOIN Meanings m ON m.Id = s.DefaultMeaningId
            WHERE s.DefaultMeaningId IS NOT NULL AND (m.Id IS NULL OR m.SenseId <> s.Id)
            """);
        if (unresolvedDefaultMeaning > 0)
        {
            throw new BackupFormatException(BackupErrorCodes.MissingReference);
        }
    }

    private MergePlanAction GetAction(MergeEntityKind kind, string archiveLocalId)
    {
        if (!_actionsByKey.TryGetValue((kind, archiveLocalId), out var action))
        {
            throw new BackupFormatException(BackupErrorCodes.MissingReference);
        }

        return action;
    }

    private static int ResolveExisting<TIdentity>(IReadOnlyDictionary<TIdentity, int> map, TIdentity identity)
        where TIdentity : notnull
    {
        if (!map.TryGetValue(identity, out var id))
        {
            throw new BackupFormatException(BackupErrorCodes.MissingReference);
        }

        return id;
    }

    private static int RequireId(IReadOnlyDictionary<string, int> ids, string archiveId)
    {
        if (!ids.TryGetValue(archiveId, out var id))
        {
            throw new BackupFormatException(BackupErrorCodes.MissingReference);
        }

        return id;
    }

    private static bool IsPlainInsert(MergeEntityClassification classification) =>
        classification is MergeEntityClassification.New or MergeEntityClassification.Enriched or MergeEntityClassification.PreservedVariant;

    private void Insert(object entity) =>
        Schema8BackupImportRepository.Insert(_connection, entity, _cancellationToken, _failureInjector, ref _mutationCount);

    private int InsertRaw(string sql, params object?[] args) =>
        Schema8BackupImportRepository.InsertRaw(_connection, sql, _cancellationToken, _failureInjector, ref _mutationCount, args);

    private void ExecuteRaw(string sql, params object?[] args)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        _connection.Execute(sql, args);
        _mutationCount++;
        _failureInjector?.AfterMutation(_mutationCount);
    }

    // ---- 1. SourceMaterials + Sentences ----
    private void WriteSourceMaterials()
    {
        foreach (var source in _archive.SourceMaterials)
        {
            var action = GetAction(MergeEntityKind.SourceMaterial, source.Id);
            int documentId;
            if (action.Classification == MergeEntityClassification.New)
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
                Insert(document);
                documentId = document.Id;
            }
            else
            {
                documentId = ResolveExisting(_targetIndex.DocumentIdByIdentity, new SourceMaterialIdentity(action.StableIdentity));
            }

            _documentIds[source.Id] = documentId;

            foreach (var sourceSentence in source.Sentences.OrderBy(item => item.Order))
            {
                var sentenceAction = GetAction(MergeEntityKind.SentenceRange, sourceSentence.Id);
                int sentenceId;
                if (sentenceAction.Classification == MergeEntityClassification.New)
                {
                    var sentence = new SentenceSpanEntity
                    {
                        DocumentId = documentId,
                        StartPosition = sourceSentence.Start,
                        Length = sourceSentence.Length,
                        Order = sourceSentence.Order
                    };
                    Insert(sentence);
                    sentenceId = sentence.Id;
                }
                else
                {
                    sentenceId = ResolveExisting(_targetIndex.SentenceIdByIdentity, sentenceAction.StableIdentity);
                }

                _sentenceIds[sourceSentence.Id] = sentenceId;
            }
        }
    }

    // ---- 2. Vocabulary + WordForms + LegacyReviewSummaries ----
    private void WriteVocabulary()
    {
        foreach (var source in _archive.Vocabulary)
        {
            var action = GetAction(MergeEntityKind.Vocabulary, source.Id);
            int wordId;
            if (action.Classification == MergeEntityClassification.New)
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
                Insert(word);
                wordId = word.Id;
            }
            else
            {
                wordId = ResolveExisting(_targetIndex.WordIdByIdentity, new VocabularyIdentity(action.StableIdentity));
                if (action.Classification == MergeEntityClassification.Enriched)
                {
                    var targetWord = _targetWordsById[wordId];
                    var resolvedKnowledge = KnowledgeStateConflictPolicy.Resolve(
                        BackupEnumMappings.ToBackup(targetWord.Status), source.KnowledgeState).ResolvedState;
                    var resolvedPreparation = PreparationStateConflictPolicy.Resolve(
                        BackupEnumMappings.ToBackup(targetWord.PreparationState), source.PreparationState).ResolvedState;
                    if (resolvedKnowledge is null || resolvedPreparation is null)
                    {
                        throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                    }

                    ExecuteRaw(
                        "UPDATE Words SET Status = ?, PreparationState = ? WHERE Id = ?",
                        (int)BackupEnumMappings.ToPersistence(resolvedKnowledge.Value),
                        (int)BackupEnumMappings.ToPersistence(resolvedPreparation.Value),
                        wordId);
                }
            }

            _wordIds[source.Id] = wordId;

            foreach (var form in source.EncounteredForms)
            {
                var formAction = GetAction(MergeEntityKind.EncounteredForm, source.Id + ":" + form.SurfaceForm);
                if (formAction.Classification == MergeEntityClassification.New)
                {
                    Insert(new WordFormEntity { WordId = wordId, SurfaceForm = form.SurfaceForm, OccurrenceCount = form.OccurrenceCount });
                }
            }

            for (var i = 0; i < source.LegacyReviewSummaries.Count; i++)
            {
                var summary = source.LegacyReviewSummaries[i];
                var summaryAction = GetAction(MergeEntityKind.LegacyReviewSummary, source.Id + ":" + i.ToString(CultureInfo.InvariantCulture));
                if (IsPlainInsert(summaryAction.Classification))
                {
                    Insert(new ReviewStateEntity
                    {
                        WordId = wordId,
                        ReviewCount = summary.ReviewCount,
                        ForgotCount = summary.ForgotCount,
                        PartialCount = summary.PartialCount,
                        KnownCount = summary.KnownCount,
                        LastReviewedAt = summary.LastReviewedAtUtc
                    });
                }
            }
        }
    }

    // ---- 3. Occurrences (second pass) ----
    private void WriteOccurrences()
    {
        foreach (var source in _archive.SourceMaterials)
        {
            var documentId = RequireId(_documentIds, source.Id);
            foreach (var occurrence in source.Occurrences.OrderBy(item => item.Order))
            {
                var action = GetAction(MergeEntityKind.Occurrence, occurrence.SentenceId + ":" + occurrence.VocabularyId);
                if (action.Classification != MergeEntityClassification.New)
                {
                    continue;
                }

                Insert(new WordOccurrenceEntity
                {
                    WordId = RequireId(_wordIds, occurrence.VocabularyId),
                    DocumentId = documentId,
                    SentenceSpanId = RequireId(_sentenceIds, occurrence.SentenceId),
                    StartPosition = occurrence.Start,
                    Length = occurrence.Length,
                    SurfaceForm = occurrence.SurfaceForm,
                    TechnicalFamily = BackupEnumMappings.ToPersistence(occurrence.TechnicalFamily),
                    TechnicalInstanceYear = occurrence.TechnicalInstanceYear,
                    TechnicalInstanceIdentifier = occurrence.TechnicalInstanceIdentifier ?? string.Empty,
                    TechnicalVariant = occurrence.TechnicalVariant ?? string.Empty,
                    Order = occurrence.Order
                });
            }
        }
    }

    // ---- 4. Senses ----
    private void WriteSenses()
    {
        foreach (var source in _archive.Senses)
        {
            var action = GetAction(MergeEntityKind.Sense, source.Id);
            int senseId;
            if (IsPlainInsert(action.Classification))
            {
                senseId = InsertRaw(
                    """
                    INSERT INTO Senses
                        (StableId, WordId, SourceLanguage, ExplanationLanguage, ProviderSenseId, TopicOrDomain,
                         PartOfSpeech, GrammaticalRelationship, AcronymExpansion, DefaultMeaningId, Status,
                         CreatedAtUtc, UpdatedAtUtc)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, NULL, ?, ?, ?)
                    """,
                    source.StableId, RequireId(_wordIds, source.VocabularyId), source.SourceLanguage, source.ExplanationLanguage,
                    source.ProviderSenseId, source.TopicOrDomain, source.PartOfSpeech, source.GrammaticalRelationship,
                    source.AcronymExpansion, (int)source.Status, source.CreatedAtUtc, source.UpdatedAtUtc);
            }
            else
            {
                senseId = ResolveExisting(_targetIndex.SenseIdByIdentity, new SemanticMeaningIdentity(action.StableIdentity));
            }

            _senseIds[source.Id] = senseId;
        }

        _failureInjector?.AtCheckpoint(Checkpoints.AfterSenseInsertion);
    }

    // ---- 5. Meanings ----
    private void WriteMeanings()
    {
        foreach (var source in _archive.PreparedLearning)
        {
            var action = GetAction(MergeEntityKind.PreparedMeaning, source.Id);
            int meaningId;
            if (IsPlainInsert(action.Classification))
            {
                meaningId = InsertRaw(
                    """
                    INSERT INTO Meanings
                        (WordId, SenseId, StableId, ExplanationLanguage, SourceLanguage, DisplayTerm,
                         EncounteredSurfaceForm, GrammaticalRelationship, TokenKind, SelectedMeaningId,
                         AcronymExpansion, Translation, Definition, DictionaryExample, AdditionalNote,
                         AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle,
                         SourceRevisionId, Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    RequireId(_wordIds, source.VocabularyId), RequireId(_senseIds, source.SenseId), source.StableId,
                    source.ExplanationLanguage, source.SourceLanguage, source.DisplayTerm,
                    source.EncounteredSurfaceForm ?? string.Empty, source.GrammaticalRelationship ?? string.Empty,
                    (int)BackupEnumMappings.ToPersistence(source.TokenKind), source.ProviderMeaningId ?? string.Empty,
                    source.AcronymExpansion ?? string.Empty, source.Translation ?? string.Empty, source.Definition ?? string.Empty,
                    source.DictionaryExample ?? string.Empty, source.AdditionalNote ?? string.Empty,
                    System.Text.Json.JsonSerializer.Serialize(source.AcceptedAliases.ToArray(), Core.Preparation.LexicalJsonSerializerContext.Default.StringArray),
                    source.LegacyAnswerText ?? string.Empty, source.Source.ProviderName, source.Source.SourceProject,
                    source.Source.PageTitle, source.Source.RevisionId, source.Source.Attribution, source.ConfirmedByUser,
                    source.CreatedAtUtc, source.UpdatedAtUtc, source.PreparedAtUtc);
            }
            else
            {
                meaningId = ResolveExisting(_targetIndex.MeaningIdByIdentity, new ExactMeaningVariantIdentity(action.StableIdentity));
            }

            _meaningIds[source.Id] = meaningId;
        }
    }

    // ---- 6. Sense.DefaultMeaningId backfill (freshly-inserted Senses only) ----
    private void BackfillSenseDefaultMeanings()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        foreach (var source in _archive.Senses)
        {
            var action = GetAction(MergeEntityKind.Sense, source.Id);
            if (!IsPlainInsert(action.Classification) || source.DefaultMeaningId is null)
            {
                continue;
            }

            var senseLocalId = RequireId(_senseIds, source.Id);
            var meaningLocalId = RequireId(_meaningIds, source.DefaultMeaningId);
            ExecuteRaw("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", meaningLocalId, senseLocalId);
        }

        _failureInjector?.AtCheckpoint(Checkpoints.AfterDefaultMeaningBackfill);
    }

    // ---- 7. AnswerVariants ----
    private void WriteAnswerVariants()
    {
        foreach (var source in _archive.AnswerVariants)
        {
            var action = GetAction(MergeEntityKind.AnswerVariant, source.Id);
            int variantId;
            if (action.Classification == MergeEntityClassification.New)
            {
                variantId = InsertRaw(
                    """
                    INSERT INTO AnswerVariants
                        (StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    source.StableId, RequireId(_senseIds, source.SenseId), source.AnswerLanguage, source.DisplayText,
                    source.NormalizedText, source.SourceMeaningId is null ? null : (int?)RequireId(_meaningIds, source.SourceMeaningId),
                    source.CreatedAtUtc, source.UpdatedAtUtc);
            }
            else
            {
                variantId = ResolveExisting(_targetIndex.AnswerVariantIdByIdentity, new AnswerVariantIdentity(action.StableIdentity));
            }

            _variantIds[source.Id] = variantId;
        }
    }

    // ---- 8. SenseAnswerVariantAssignments ----
    private void WriteAssignments()
    {
        foreach (var source in _archive.SenseAnswerVariantAssignments)
        {
            var action = GetAction(MergeEntityKind.SenseAnswerVariantAssignment, source.Id);
            if (action.Classification != MergeEntityClassification.New)
            {
                continue;
            }

            InsertRaw(
                """
                INSERT INTO SenseAnswerVariantAssignments
                    (StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                source.StableId, RequireId(_senseIds, source.SenseId), (int)BackupEnumMappings.ToPersistence(source.CardDirection),
                RequireId(_variantIds, source.AnswerVariantId), (int)source.Requirement, source.IsPreferred,
                source.RequiredSinceUtc, source.CreatedAtUtc, source.UpdatedAtUtc);
        }
    }

    // ---- 9. ContextSnapshots ----
    private void WriteContexts()
    {
        foreach (var source in _archive.PreparedLearning)
        {
            var meaningLocalId = RequireId(_meaningIds, source.Id);
            foreach (var context in source.Contexts)
            {
                if (context.SenseId != source.SenseId)
                {
                    throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                }

                var action = GetAction(MergeEntityKind.ContextSnapshot, source.Id + ":" + context.NormalizedFingerprint);
                if (action.Classification != MergeEntityClassification.New)
                {
                    continue;
                }

                InsertRaw(
                    """
                    INSERT INTO ContextSnapshots
                        (MeaningId, SenseId, WordId, SourceDocumentId, SourceDocumentTitle, Text, TargetStart,
                         TargetLength, NormalizedFingerprint, CreatedAtUtc)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    meaningLocalId, RequireId(_senseIds, context.SenseId), RequireId(_wordIds, source.VocabularyId),
                    RequireId(_documentIds, context.SourceMaterialId), context.SourceTitle, context.Text,
                    context.TargetStart, context.TargetLength, context.NormalizedFingerprint, context.CreatedAtUtc);
            }
        }
    }

    // ---- 10. LearningCards ----
    private void WriteCards()
    {
        foreach (var source in _archive.Learning.Cards)
        {
            var action = GetAction(MergeEntityKind.LearningCard, source.Id);
            int cardId;
            if (action.Classification == MergeEntityClassification.New)
            {
                cardId = InsertRaw(
                    """
                    INSERT INTO LearningCards
                        (WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor,
                         SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    RequireId(_wordIds, source.VocabularyId), RequireId(_senseIds, source.SenseId),
                    RequireId(_meaningIds, source.PreferredMeaningId), (int)BackupEnumMappings.ToPersistence(source.Direction),
                    (int)BackupEnumMappings.ToPersistence(source.State), source.DueAtUtc, source.IntervalDays, source.EaseFactor,
                    source.SuccessfulReviewCount, source.LapseCount, source.LastReviewedAtUtc,
                    source.LastRating is null ? null : (int?)BackupEnumMappings.ToPersistence(source.LastRating.Value),
                    source.CreatedAtUtc, source.UpdatedAtUtc);
            }
            else
            {
                // Enriched or ExactDuplicateSkipped: the matched card is reused as-is — PreferredMeaningId,
                // SenseId, and Direction are never touched. Enriched means "this matched card gained at
                // least one archive-only review event" (see MergePreflightPlan.RequiresSchedulerReplay); its
                // derived scheduling fields are recomputed by ReplayCardSchedules() after every surviving
                // review row for it has been written, never left stale.
                cardId = ResolveExisting(_targetIndex.CardIdByIdentity, new FutureCardIdentity(action.StableIdentity));
                if (action.Classification == MergeEntityClassification.Enriched)
                {
                    _cardsNeedingScheduleReplay.Add(cardId);
                }
            }

            _cardIds[source.Id] = cardId;
        }
    }

    // ---- 11a. VocabularyReviewWorkflow + VocabularyReviewItem ----
    private void WriteVocabularyReviewWorkflows()
    {
        foreach (var source in _archive.Workflows.VocabularyReviews)
        {
            var action = GetAction(MergeEntityKind.VocabularyReviewWorkflow, source.Id);
            int sessionId;
            if (action.Classification == MergeEntityClassification.New)
            {
                var session = new ReviewSessionEntity
                {
                    DocumentId = RequireId(_documentIds, source.SourceMaterialId),
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
                Insert(session);
                sessionId = session.Id;
            }
            else
            {
                sessionId = ResolveExisting(_targetIndex.ReviewSessionIdByIdentity, new ReviewSessionIdentity(action.StableIdentity));
            }

            _reviewSessionIds[source.Id] = sessionId;

            foreach (var item in source.Items.OrderBy(item => item.Order))
            {
                var itemAction = GetAction(MergeEntityKind.VocabularyReviewItem, item.Id);
                if (itemAction.Classification != MergeEntityClassification.New)
                {
                    continue;
                }

                Insert(new ReviewCandidateEntity
                {
                    SessionId = sessionId,
                    WordId = RequireId(_wordIds, item.VocabularyId),
                    Order = item.Order,
                    Status = BackupEnumMappings.ToPersistence(item.Status),
                    PreviousWordStatus = BackupEnumMappings.ToPersistence(item.PreviousKnowledgeState),
                    PreviousTotalOccurrenceCount = item.PreviousTotalOccurrenceCount,
                    PreviousDocumentCount = item.PreviousDocumentCount,
                    PreviousUpdatedAt = item.PreviousUpdatedAtUtc,
                    DecisionSequence = item.DecisionSequence,
                    WasWordCreatedForSession = item.WasVocabularyCreatedForSession,
                    DecidedAt = item.DecidedAtUtc
                });
            }
        }
    }

    // ---- 11b. PreparationWorkflow + PreparationItem ----
    private void WritePreparationWorkflows()
    {
        foreach (var source in _archive.Workflows.PreparationBatches)
        {
            var action = GetAction(MergeEntityKind.PreparationWorkflow, source.Id);
            int sessionId;
            if (action.Classification == MergeEntityClassification.New)
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
                Insert(session);
                sessionId = session.Id;
            }
            else
            {
                sessionId = ResolveExisting(_targetIndex.PreparationSessionIdByIdentity, new PreparationSessionIdentity(action.StableIdentity));
                if (action.Classification == MergeEntityClassification.Enriched)
                {
                    var targetSession = _targetPreparationSessionsById[sessionId];
                    var resolvedStatus = WorkflowSessionStateConflictPolicy.ResolvePreparationSession(
                        BackupEnumMappings.ToBackup(targetSession.Status), source.Status).ResolvedState;
                    if (resolvedStatus is null)
                    {
                        throw new BackupFormatException(BackupErrorCodes.InvariantViolation);
                    }

                    ExecuteRaw(
                        "UPDATE PreparationSessions SET Status = ? WHERE Id = ?",
                        (int)BackupEnumMappings.ToPersistence(resolvedStatus.Value), sessionId);
                }
            }

            _preparationSessionIds[source.Id] = sessionId;

            foreach (var item in source.Items.OrderBy(item => item.Order))
            {
                var itemAction = GetAction(MergeEntityKind.PreparationItem, item.Id);
                if (itemAction.Classification != MergeEntityClassification.New)
                {
                    continue;
                }

                Insert(new PreparationCandidateEntity
                {
                    SessionId = sessionId,
                    WordId = RequireId(_wordIds, item.VocabularyId),
                    Order = item.Order,
                    Status = BackupEnumMappings.ToPersistence(item.Status),
                    ResultJson = item.LookupDraft is null ? string.Empty : Schema8BackupImportRepository.SerializeLookupDraft(item.LookupDraft),
                    SelectedMeaningIndex = item.SelectedMeaningIndex,
                    LastErrorCode = item.LastErrorCode ?? string.Empty,
                    LookupAttemptCount = item.LookupAttemptCount,
                    UpdatedAtUtc = item.UpdatedAtUtc
                });
            }
        }
    }

    // ---- 11c. LearningWorkflow + LearningQueueItem ----
    private void WriteLearningWorkflows()
    {
        foreach (var source in _archive.Workflows.LearningSessions)
        {
            var action = GetAction(MergeEntityKind.LearningWorkflow, source.Id);
            int sessionId;
            if (action.Classification == MergeEntityClassification.New)
            {
                var session = new LearningSessionEntity
                {
                    Status = BackupEnumMappings.ToPersistence(source.Status),
                    TotalCards = source.TotalCards,
                    CompletedCards = source.CompletedCards,
                    AgainCount = source.AgainCount,
                    HardCount = source.HardCount,
                    GoodCount = source.GoodCount,
                    EasyCount = source.EasyCount,
                    StartedAtUtc = source.StartedAtUtc,
                    UpdatedAtUtc = source.UpdatedAtUtc,
                    CompletedAtUtc = source.CompletedAtUtc
                };
                Insert(session);
                sessionId = session.Id;
            }
            else
            {
                sessionId = ResolveExisting(_targetIndex.LearningSessionIdByIdentity, action.StableIdentity);
            }

            _learningSessionIds[source.Id] = sessionId;

            foreach (var item in source.QueueItems.OrderBy(item => item.QueueOrder))
            {
                var itemAction = GetAction(MergeEntityKind.LearningQueueItem, item.Id);
                if (itemAction.Classification != MergeEntityClassification.New)
                {
                    continue;
                }

                InsertRaw(
                    """
                    INSERT INTO LearningSessionCards
                        (SessionId, CardId, QueueOrder, IsDueCard, IsAgainRepeat, AnswerRevealed, SpellingChecked,
                         SpellingCorrect, IsCompleted, Rating, CompletedAtUtc, TargetAnswerVariantId)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    sessionId, RequireId(_cardIds, item.CardId), item.QueueOrder, item.IsDueCard,
                    item.IsAgainRepeat, item.AnswerRevealed, item.SpellingChecked, item.SpellingCorrect,
                    item.IsCompleted, item.Rating is null ? null : (int?)BackupEnumMappings.ToPersistence(item.Rating.Value),
                    item.CompletedAtUtc,
                    item.TargetAnswerVariantId is null ? null : (int?)RequireId(_variantIds, item.TargetAnswerVariantId));
            }
        }
    }

    // ---- 11d. LearningReviews ----
    private void WriteLearningReviews()
    {
        foreach (var source in _archive.Learning.ReviewEvents)
        {
            var label = source.CardId + "@" + source.ReviewedAtUtc.ToString("O", CultureInfo.InvariantCulture);
            var action = GetAction(MergeEntityKind.LearningReview, label);
            if (action.Classification != MergeEntityClassification.New)
            {
                continue;
            }

            InsertRaw(
                """
                INSERT INTO LearningReviews
                    (CardId, SessionId, Rating, WasTypedAnswer, WasCorrect, ReviewedAtUtc, DueAtUtc, IntervalDays,
                     EaseFactor, TargetAnswerVariantId, MatchedAnswerVariantId)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                RequireId(_cardIds, source.CardId), RequireId(_learningSessionIds, source.LearningSessionId),
                (int)BackupEnumMappings.ToPersistence(source.Rating), source.WasTypedAnswer, source.WasCorrect,
                source.ReviewedAtUtc, source.DueAtUtc, source.IntervalDays, source.EaseFactor,
                source.TargetAnswerVariantId is null ? null : (int?)RequireId(_variantIds, source.TargetAnswerVariantId),
                source.MatchedAnswerVariantId is null ? null : (int?)RequireId(_variantIds, source.MatchedAnswerVariantId));
        }
    }

    private sealed class ReplayReviewRow
    {
        public ReviewRating Rating { get; set; }
        public bool WasTypedAnswer { get; set; }
        public bool WasCorrect { get; set; }
        public DateTime ReviewedAtUtc { get; set; }
        public DateTime DueAtUtc { get; set; }
        public int IntervalDays { get; set; }
        public double EaseFactor { get; set; }
    }

    /// <summary>
    /// Recomputes the derived scheduling fields (State, DueAtUtc, IntervalDays, EaseFactor,
    /// SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating) for every matched card whose
    /// surviving review-event set changed during this merge (<see cref="_cardsNeedingScheduleReplay"/>,
    /// populated only for <see cref="MergeEntityClassification.Enriched"/> cards in <see cref="WriteCards"/>).
    /// Review history is authoritative and these fields are derived from it — never carried over from
    /// either side's stale snapshot. Loads the card's <em>complete</em> current review set (target rows
    /// already present plus every archive-only row <see cref="WriteLearningReviews"/> just inserted — both
    /// visible on this same connection/transaction), deduplicates by the existing review fingerprint
    /// contract (<see cref="LearningReviewFingerprintPolicy"/>), and replays via
    /// <see cref="CardScheduleReplayer"/> from the card's own creation-time default
    /// (<see cref="CardSchedule.New"/>) — the same initial state <see cref="Schema8LearningReviewReplayPolicy.FirstPreEventSchedule"/>
    /// already uses for the analogous per-variant progress replay. Never touches PreferredMeaningId,
    /// SenseId, or Direction.
    /// </summary>
    private void ReplayCardSchedules()
    {
        foreach (var cardId in _cardsNeedingScheduleReplay.OrderBy(id => id))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _failureInjector?.AtCheckpoint(Checkpoints.DuringSchedulerReplay);

            var rows = _connection.Query<ReplayReviewRow>(
                "SELECT Rating, WasTypedAnswer, WasCorrect, ReviewedAtUtc, DueAtUtc, IntervalDays, EaseFactor FROM LearningReviews WHERE CardId = ?",
                cardId);

            // A fixed per-card identity: fingerprints are only ever compared within this one card's own
            // event list, so any constant value distinguishes events correctly here.
            var cardIdentity = new LearningCardMatchIdentity(cardId.ToString(CultureInfo.InvariantCulture));

            // sqlite-net returns every DateTime with DateTimeKind.Unspecified regardless of how it was
            // written (see Schema8Utc's own doc comment) — normalized to Utc here before use, exactly like
            // every other reader of a raw Schema-8 timestamp in this codebase. Left un-normalized, a
            // Kind=Unspecified value fed to ISpacedRepetitionScheduler.Schedule would be silently
            // reinterpreted as local time and shifted by the host machine's UTC offset.
            var events = rows.Select(row =>
            {
                var reviewedAtUtc = Schema8Utc.Normalize(row.ReviewedAtUtc);
                var dueAtUtc = Schema8Utc.Normalize(row.DueAtUtc);
                return new CardScheduleReplayer.ReviewEvent(
                    reviewedAtUtc,
                    row.Rating,
                    LearningReviewFingerprintPolicy.Compute(
                        cardIdentity, reviewedAtUtc, BackupEnumMappings.ToBackup(row.Rating),
                        row.WasTypedAnswer, row.WasCorrect, dueAtUtc, row.IntervalDays, row.EaseFactor).Value);
            });

            var initial = CardSchedule.New(Schema8Utc.Normalize(_targetCardsById[cardId].CreatedAtUtc));
            var final = CardScheduleReplayer.Replay(initial, events, ReplayScheduler, _cancellationToken);

            ExecuteRaw(
                """
                UPDATE LearningCards
                SET State = ?, DueAtUtc = ?, IntervalDays = ?, EaseFactor = ?, SuccessfulReviewCount = ?,
                    LapseCount = ?, LastReviewedAtUtc = ?, LastRating = ?
                WHERE Id = ?
                """,
                (int)final.State, final.DueAtUtc, final.IntervalDays, final.EaseFactor,
                final.SuccessfulReviewCount, final.LapseCount, final.LastReviewedAtUtc,
                final.LastRating is null ? null : (int?)final.LastRating.Value,
                cardId);
        }

        _failureInjector?.AtCheckpoint(Checkpoints.AfterSchedulerReplay);
    }

    // ---- 11e. AnswerVariantProgress ----
    private void WriteAnswerVariantProgress()
    {
        foreach (var source in _archive.AnswerVariantProgress)
        {
            var action = GetAction(MergeEntityKind.AnswerVariantProgress, source.CardId + ":" + source.AnswerVariantId);
            if (action.Classification != MergeEntityClassification.New)
            {
                continue;
            }

            InsertRaw(
                """
                INSERT INTO AnswerVariantProgress
                    (CardId, AnswerVariantId, InteractionMode, ConsecutiveReadingSuccessCount,
                     ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, LastAssessedAtUtc,
                     MasteryReviewExtensionScheduled, IsMastered, ReplayVersion, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                RequireId(_cardIds, source.CardId), RequireId(_variantIds, source.AnswerVariantId),
                (int)BackupEnumMappings.ToPersistence(source.InteractionMode), source.ConsecutiveReadingSuccessCount,
                source.ConsecutiveTypingSuccessCount, source.ConsecutiveTypingFailureCount, source.LastAssessedAtUtc,
                source.MasteryReviewExtensionScheduled, source.IsMastered, source.ReplayVersion,
                source.CreatedAtUtc, source.UpdatedAtUtc);
        }
    }
}
