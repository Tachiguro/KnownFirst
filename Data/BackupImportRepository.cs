using System.Text.Json;
using KnownFirst.Core.Preparation;
using KnownFirst.Data.Entities;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using SQLite;

namespace KnownFirst.Data;

public static class BackupImportRepository
{
    public static bool HasDurableUserData(SQLiteConnection connection) =>
        connection.Table<DocumentEntity>().Count() != 0
        || connection.Table<WordEntity>().Count() != 0
        || connection.Table<WordFormEntity>().Count() != 0
        || connection.Table<SentenceSpanEntity>().Count() != 0
        || connection.Table<WordOccurrenceEntity>().Count() != 0
        || connection.Table<MeaningEntity>().Count() != 0
        || connection.Table<ReviewStateEntity>().Count() != 0
        || connection.Table<ReviewSessionEntity>().Count() != 0
        || connection.Table<ReviewCandidateEntity>().Count() != 0
        || connection.Table<PreparationSessionEntity>().Count() != 0
        || connection.Table<PreparationCandidateEntity>().Count() != 0
        || connection.Table<ContextSnapshotEntity>().Count() != 0
        || connection.Table<LearningCardEntity>().Count() != 0
        || connection.Table<LearningReviewEntity>().Count() != 0
        || connection.Table<LearningSessionEntity>().Count() != 0
        || connection.Table<LearningSessionCardEntity>().Count() != 0;

    public static void ImportIntoEmptyDatabase(
        SQLiteConnection connection,
        BackupPayload payload,
        CancellationToken cancellationToken,
        IBackupImportFailureInjector? failureInjector = null)
    {
        if (HasDurableUserData(connection))
        {
            throw new InvalidOperationException(BackupErrorCodes.TargetNotEmpty);
        }

        var mutationCount = 0;
        var documentIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var sentenceIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var wordIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var meaningIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var cardIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var reviewSessionIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var preparationSessionIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var learningSessionIds = new Dictionary<string, int>(StringComparer.Ordinal);

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
                AutomaticInteractionMode =
                    BackupEnumMappings.ToPersistence(source.AutomaticLearning.InteractionMode),
                ConsecutiveRecallSuccessCount =
                    source.AutomaticLearning.ConsecutiveRecallSuccessCount,
                ConsecutiveTypingSuccessCount =
                    source.AutomaticLearning.ConsecutiveTypingSuccessCount,
                ConsecutiveTypingFailureCount =
                    source.AutomaticLearning.ConsecutiveTypingFailureCount,
                MasteryReviewExtensionScheduled =
                    source.AutomaticLearning.MasteryReviewExtensionScheduled,
                CreatedAt = source.CreatedAtUtc,
                UpdatedAt = source.UpdatedAtUtc
            };
            Insert(connection, word, cancellationToken, failureInjector, ref mutationCount);
            wordIds.Add(source.Id, word.Id);

            foreach (var sourceForm in source.EncounteredForms)
            {
                Insert(
                    connection,
                    new WordFormEntity
                    {
                        WordId = word.Id,
                        SurfaceForm = sourceForm.SurfaceForm,
                        OccurrenceCount = sourceForm.OccurrenceCount
                    },
                    cancellationToken,
                    failureInjector,
                    ref mutationCount);
            }

            foreach (var sourceSummary in source.LegacyReviewSummaries)
            {
                Insert(
                    connection,
                    new ReviewStateEntity
                    {
                        WordId = word.Id,
                        ReviewCount = sourceSummary.ReviewCount,
                        ForgotCount = sourceSummary.ForgotCount,
                        PartialCount = sourceSummary.PartialCount,
                        KnownCount = sourceSummary.KnownCount,
                        LastReviewedAt = sourceSummary.LastReviewedAtUtc
                    },
                    cancellationToken,
                    failureInjector,
                    ref mutationCount);
            }
        }

        foreach (var source in payload.SourceMaterials)
        {
            var documentId = RequireId(documentIds, source.Id);
            foreach (var sourceOccurrence in source.Occurrences.OrderBy(item => item.Order))
            {
                Insert(
                    connection,
                    new WordOccurrenceEntity
                    {
                        WordId = RequireId(wordIds, sourceOccurrence.VocabularyId),
                        DocumentId = documentId,
                        SentenceSpanId = RequireId(sentenceIds, sourceOccurrence.SentenceId),
                        StartPosition = sourceOccurrence.Start,
                        Length = sourceOccurrence.Length,
                        SurfaceForm = sourceOccurrence.SurfaceForm,
                        TechnicalFamily =
                            BackupEnumMappings.ToPersistence(sourceOccurrence.TechnicalFamily),
                        TechnicalInstanceYear = sourceOccurrence.TechnicalInstanceYear,
                        TechnicalInstanceIdentifier =
                            sourceOccurrence.TechnicalInstanceIdentifier ?? string.Empty,
                        TechnicalVariant = sourceOccurrence.TechnicalVariant ?? string.Empty,
                        Order = sourceOccurrence.Order
                    },
                    cancellationToken,
                    failureInjector,
                    ref mutationCount);
            }
        }

        foreach (var source in payload.PreparedLearning)
        {
            var meaning = new MeaningEntity
            {
                WordId = RequireId(wordIds, source.VocabularyId),
                ExplanationLanguage = source.ExplanationLanguage,
                SourceLanguage = source.SourceLanguage,
                DisplayTerm = source.DisplayTerm,
                EncounteredSurfaceForm = source.EncounteredSurfaceForm ?? string.Empty,
                GrammaticalRelationship = source.GrammaticalRelationship ?? string.Empty,
                TokenKind = BackupEnumMappings.ToPersistence(source.TokenKind),
                SelectedMeaningId = source.ProviderMeaningId ?? string.Empty,
                AcronymExpansion = source.AcronymExpansion ?? string.Empty,
                Translation = source.Translation ?? string.Empty,
                Definition = source.Definition ?? string.Empty,
                DictionaryExample = source.DictionaryExample ?? string.Empty,
                AdditionalNote = source.AdditionalNote ?? string.Empty,
                TranslationOrDefinition = source.LegacyAnswerText ?? string.Empty,
                AcceptedAliasesJson = JsonSerializer.Serialize(
                    source.AcceptedAliases.ToArray(),
                    LexicalJsonSerializerContext.Default.StringArray),
                ConfirmedByUser = source.ConfirmedByUser,
                Source = source.Source.ProviderName,
                SourceProject = source.Source.SourceProject,
                SourcePageTitle = source.Source.PageTitle,
                SourceRevisionId = source.Source.RevisionId,
                Attribution = source.Source.Attribution,
                CreatedAt = source.CreatedAtUtc,
                UpdatedAt = source.UpdatedAtUtc,
                PreparedAt = source.PreparedAtUtc
            };
            Insert(connection, meaning, cancellationToken, failureInjector, ref mutationCount);
            meaningIds.Add(source.Id, meaning.Id);

            foreach (var sourceContext in source.Contexts)
            {
                Insert(
                    connection,
                    new ContextSnapshotEntity
                    {
                        MeaningId = meaning.Id,
                        WordId = meaning.WordId,
                        SourceDocumentId =
                            RequireId(documentIds, sourceContext.SourceMaterialId),
                        SourceDocumentTitle = sourceContext.SourceTitle,
                        Text = sourceContext.Text,
                        TargetStart = sourceContext.TargetStart,
                        TargetLength = sourceContext.TargetLength,
                        NormalizedFingerprint = sourceContext.NormalizedFingerprint,
                        CreatedAtUtc = sourceContext.CreatedAtUtc
                    },
                    cancellationToken,
                    failureInjector,
                    ref mutationCount);
            }
        }

        foreach (var source in payload.Learning.Cards)
        {
            var card = new LearningCardEntity
            {
                WordId = RequireId(wordIds, source.VocabularyId),
                MeaningId = RequireId(meaningIds, source.PreparedItemId),
                Direction = BackupEnumMappings.ToPersistence(source.Direction),
                State = BackupEnumMappings.ToPersistence(source.State),
                DueAtUtc = source.DueAtUtc,
                IntervalDays = source.IntervalDays,
                EaseFactor = source.EaseFactor,
                SuccessfulReviewCount = source.SuccessfulReviewCount,
                LapseCount = source.LapseCount,
                LastReviewedAtUtc = source.LastReviewedAtUtc,
                LastRating = source.LastRating is null
                    ? null
                    : BackupEnumMappings.ToPersistence(source.LastRating.Value),
                CreatedAtUtc = source.CreatedAtUtc,
                UpdatedAtUtc = source.UpdatedAtUtc
            };
            Insert(connection, card, cancellationToken, failureInjector, ref mutationCount);
            cardIds.Add(source.Id, card.Id);
        }

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
                Insert(
                    connection,
                    new ReviewCandidateEntity
                    {
                        SessionId = session.Id,
                        WordId = RequireId(wordIds, sourceItem.VocabularyId),
                        Order = sourceItem.Order,
                        Status = BackupEnumMappings.ToPersistence(sourceItem.Status),
                        PreviousWordStatus =
                            BackupEnumMappings.ToPersistence(sourceItem.PreviousKnowledgeState),
                        PreviousTotalOccurrenceCount = sourceItem.PreviousTotalOccurrenceCount,
                        PreviousDocumentCount = sourceItem.PreviousDocumentCount,
                        PreviousUpdatedAt = sourceItem.PreviousUpdatedAtUtc,
                        DecisionSequence = sourceItem.DecisionSequence,
                        WasWordCreatedForSession = sourceItem.WasVocabularyCreatedForSession,
                        DecidedAt = sourceItem.DecidedAtUtc
                    },
                    cancellationToken,
                    failureInjector,
                    ref mutationCount);
            }
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
                Insert(
                    connection,
                    new PreparationCandidateEntity
                    {
                        SessionId = session.Id,
                        WordId = RequireId(wordIds, sourceItem.VocabularyId),
                        Order = sourceItem.Order,
                        Status = BackupEnumMappings.ToPersistence(sourceItem.Status),
                        ResultJson = sourceItem.LookupDraft is null
                            ? string.Empty
                            : SerializeLookupDraft(sourceItem.LookupDraft),
                        SelectedMeaningIndex = sourceItem.SelectedMeaningIndex,
                        LastErrorCode = sourceItem.LastErrorCode ?? string.Empty,
                        LookupAttemptCount = sourceItem.LookupAttemptCount,
                        UpdatedAtUtc = sourceItem.UpdatedAtUtc
                    },
                    cancellationToken,
                    failureInjector,
                    ref mutationCount);
            }
        }

        foreach (var source in payload.Workflows.LearningSessions)
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
            Insert(connection, session, cancellationToken, failureInjector, ref mutationCount);
            learningSessionIds.Add(source.Id, session.Id);

            foreach (var sourceItem in source.QueueItems.OrderBy(item => item.QueueOrder))
            {
                Insert(
                    connection,
                    new LearningSessionCardEntity
                    {
                        SessionId = session.Id,
                        CardId = RequireId(cardIds, sourceItem.CardId),
                        QueueOrder = sourceItem.QueueOrder,
                        IsDueCard = sourceItem.IsDueCard,
                        IsAgainRepeat = sourceItem.IsAgainRepeat,
                        AnswerRevealed = sourceItem.AnswerRevealed,
                        SpellingChecked = sourceItem.SpellingChecked,
                        SpellingCorrect = sourceItem.SpellingCorrect,
                        IsCompleted = sourceItem.IsCompleted,
                        Rating = sourceItem.Rating is null
                            ? null
                            : BackupEnumMappings.ToPersistence(sourceItem.Rating.Value),
                        CompletedAtUtc = sourceItem.CompletedAtUtc
                    },
                    cancellationToken,
                    failureInjector,
                    ref mutationCount);
            }
        }

        foreach (var source in payload.Learning.ReviewEvents)
        {
            Insert(
                connection,
                new LearningReviewEntity
                {
                    CardId = RequireId(cardIds, source.CardId),
                    SessionId = RequireId(learningSessionIds, source.LearningSessionId),
                    Rating = BackupEnumMappings.ToPersistence(source.Rating),
                    WasTypedAnswer = source.WasTypedAnswer,
                    WasCorrect = source.WasCorrect,
                    ReviewedAtUtc = source.ReviewedAtUtc,
                    DueAtUtc = source.DueAtUtc,
                    IntervalDays = source.IntervalDays,
                    EaseFactor = source.EaseFactor
                },
                cancellationToken,
                failureInjector,
                ref mutationCount);
        }
    }

    private static string SerializeLookupDraft(BackupLookupDraft source)
    {
        var result = new LexicalResult(
            BackupEnumMappings.ToPersistence(source.Status),
            source.QueriedLemma,
            source.DisplayTerm,
            BackupEnumMappings.ToPersistence(source.TokenKind),
            source.SourceLanguage,
            source.ExplanationLanguage,
            source.AcronymExpansion,
            source.Meanings.Select(meaning => new LexicalMeaning(
                meaning.MeaningId,
                meaning.PartOfSpeech,
                meaning.Definition,
                meaning.Translation,
                meaning.Example,
                meaning.UsageLabels)).ToList(),
            source.Source.ProviderName,
            source.Source.SourceProject,
            source.Source.PageTitle,
            source.Source.RevisionId,
            source.Source.Attribution,
            source.LookupAtUtc,
            IsFromCache: false,
            ErrorCode: null,
            EncounteredSurfaceForm: source.EncounteredSurfaceForm,
            GrammaticalRelationship: source.GrammaticalRelationship,
            RedirectDepth: source.RedirectDepth,
            FormRelations: source.FormRelations.Select(relation => new ProviderFormRelation(
                BackupEnumMappings.ToPersistence(relation.Kind),
                relation.BaseLemma,
                relation.Relationship)).ToList(),
            Diagnostics: null,
            LookupMode: BackupEnumMappings.ToPersistence(source.LookupMode),
            TargetLanguage: source.TargetLanguage);

        return JsonSerializer.Serialize(
            result,
            LexicalJsonSerializerContext.Default.LexicalResult);
    }

    private static int RequireId(IReadOnlyDictionary<string, int> ids, string archiveId)
    {
        if (!ids.TryGetValue(archiveId, out var id))
        {
            throw new BackupFormatException(BackupErrorCodes.MissingReference);
        }

        return id;
    }

    private static void Insert(
        SQLiteConnection connection,
        object entity,
        CancellationToken cancellationToken,
        IBackupImportFailureInjector? failureInjector,
        ref int mutationCount)
    {
        cancellationToken.ThrowIfCancellationRequested();
        connection.Insert(entity);
        mutationCount++;
        failureInjector?.AfterMutation(mutationCount);
    }
}
