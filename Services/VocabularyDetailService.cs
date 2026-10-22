using System.Globalization;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema13;
using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Services;

/// <summary>
/// Read-only Schema-14 Vocabulary Detail service.
/// Projects a single persisted Word and its bounded Sense, Meaning, ContextSnapshot,
/// LearningTarget, AnswerVariant, TargetFsrsState, review history, and explicit control children
/// inside one database snapshot without fabricating state or executing N+1 queries.
/// </summary>
public sealed class VocabularyDetailService(IKnownFirstDatabase database) : IVocabularyDetailService
{
    private readonly IKnownFirstDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task<VocabularyDetailLookupResult> GetVocabularyDetailAsync(
        int wordId,
        int maxItemsPerCollection = IVocabularyDetailService.DefaultMaxItemsPerCollection,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var effectiveLimit = Math.Clamp(
            maxItemsPerCollection,
            1,
            IVocabularyDetailService.MaxItemsPerCollection);

        if (wordId <= 0)
        {
            return VocabularyDetailLookupResult.InvalidWordId(wordId, effectiveLimit);
        }

        return await _database.ExecuteSnapshotAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return LoadDetailSnapshot(connection, wordId, effectiveLimit, cancellationToken);
        }).ConfigureAwait(false);
    }

    internal static VocabularyDetailLookupResult LoadDetailSnapshot(
        SQLiteConnection connection,
        int wordId,
        int effectiveLimit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();

        var wordRows = connection.Query<WordProjectionRow>(
            """
            SELECT Id, Language, CanonicalTerm, NormalizedTerm, TokenKind,
                   TotalOccurrenceCount, DocumentCount, CreatedAt, UpdatedAt
            FROM Words
            WHERE Id = ?
            LIMIT 1
            """,
            wordId);

        if (wordRows.Count == 0)
        {
            return VocabularyDetailLookupResult.NotFound(wordId, effectiveLimit);
        }

        var wordRow = wordRows[0];
        var wordControl = LoadWordLearningControl(connection, wordId);

        var fetchLimit = effectiveLimit + 1;
        var senseRows = connection.Query<SenseProjectionRow>(
            """
            SELECT Id, StableId, WordId, SourceLanguage, ExplanationLanguage, ProviderSenseId,
                   TopicOrDomain, PartOfSpeech, GrammaticalRelationship, AcronymExpansion,
                   DefaultMeaningId, CreatedAtUtc, UpdatedAtUtc
            FROM Senses
            WHERE WordId = ?
            ORDER BY Id ASC
            LIMIT ?
            """,
            wordId,
            fetchLimit);

        if (senseRows.Count == 0)
        {
            var emptyDetail = new VocabularyDetail(
                wordId: wordRow.Id,
                canonicalTerm: wordRow.CanonicalTerm ?? string.Empty,
                normalizedTerm: wordRow.NormalizedTerm ?? string.Empty,
                sourceLanguage: wordRow.Language ?? string.Empty,
                tokenKind: (TokenKind)wordRow.TokenKind,
                totalOccurrenceCount: wordRow.TotalOccurrenceCount,
                documentCount: wordRow.DocumentCount,
                createdAtUtc: ToUtcDateTimeOffset(wordRow.CreatedAt),
                updatedAtUtc: ToUtcDateTimeOffset(wordRow.UpdatedAt),
                wordLearningControl: wordControl,
                senses: [],
                hasMoreSenses: false,
                maxItemsPerCollection: effectiveLimit);

            return VocabularyDetailLookupResult.Found(emptyDetail, effectiveLimit);
        }

        var hasMoreSenses = senseRows.Count > effectiveLimit;
        var keptSenseRows = hasMoreSenses
            ? senseRows.GetRange(0, effectiveLimit)
            : senseRows;

        var senseIds = new int[keptSenseRows.Count];
        for (var i = 0; i < keptSenseRows.Count; i++)
        {
            senseIds[i] = keptSenseRows[i].Id;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var senseControlsBySenseId = LoadSenseLearningControls(connection, wordId, senseIds);
        var (meaningsBySenseId, hasMoreMeaningsBySenseId) = LoadBoundedMeaningsBySense(
            connection,
            wordId,
            senseIds,
            effectiveLimit,
            fetchLimit);
        var (contextsBySenseId, hasMoreContextsBySenseId) = LoadBoundedContextsBySense(
            connection,
            wordId,
            senseIds,
            effectiveLimit,
            fetchLimit);
        var (targetRowsBySenseId, hasMoreTargetsBySenseId, keptTargetIds) = LoadBoundedTargetRowsBySense(
            connection,
            wordId,
            senseIds,
            effectiveLimit,
            fetchLimit);

        var fsrsStatesByTargetId = new Dictionary<int, VocabularyTargetFsrsStateDetail>();
        var variantsByTargetId = new Dictionary<int, IReadOnlyList<VocabularyAnswerVariantDetail>>();
        var hasMoreVariantsByTargetId = new HashSet<int>();
        var historyByTargetId = new Dictionary<int, IReadOnlyList<VocabularyTargetReviewHistoryEntryDetail>>();
        var hasMoreHistoryByTargetId = new HashSet<int>();

        if (keptTargetIds.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            fsrsStatesByTargetId = LoadTargetFsrsStates(connection, wordId, keptTargetIds);
            (variantsByTargetId, hasMoreVariantsByTargetId) = LoadBoundedAnswerVariantsByTarget(
                connection,
                wordId,
                keptTargetIds,
                effectiveLimit,
                fetchLimit);
            (historyByTargetId, hasMoreHistoryByTargetId) = LoadBoundedReviewHistoryByTarget(
                connection,
                wordId,
                keptTargetIds,
                effectiveLimit,
                fetchLimit);
        }

        var senseDetails = new List<VocabularySenseDetail>(keptSenseRows.Count);
        foreach (var senseRow in keptSenseRows)
        {
            senseControlsBySenseId.TryGetValue(senseRow.Id, out var senseControl);
            var isSenseUnblockedByLearningControls = wordControl is null && senseControl is null;

            var meanings = meaningsBySenseId.TryGetValue(senseRow.Id, out var senseMeanings)
                ? senseMeanings
                : [];
            var hasMoreMeanings = hasMoreMeaningsBySenseId.Contains(senseRow.Id);

            var contexts = contextsBySenseId.TryGetValue(senseRow.Id, out var senseContexts)
                ? senseContexts
                : [];
            var hasMoreContexts = hasMoreContextsBySenseId.Contains(senseRow.Id);

            var targetDetails = new List<VocabularyLearningTargetDetail>();
            if (targetRowsBySenseId.TryGetValue(senseRow.Id, out var senseTargetRows))
            {
                targetDetails.Capacity = senseTargetRows.Count;
                foreach (var targetRow in senseTargetRows)
                {
                    fsrsStatesByTargetId.TryGetValue(targetRow.Id, out var fsrsState);
                    var variants = variantsByTargetId.TryGetValue(targetRow.Id, out var targetVariants)
                        ? targetVariants
                        : [];
                    var hasMoreVariants = hasMoreVariantsByTargetId.Contains(targetRow.Id);

                    var history = historyByTargetId.TryGetValue(targetRow.Id, out var targetHistory)
                        ? targetHistory
                        : [];
                    var hasMoreHistory = hasMoreHistoryByTargetId.Contains(targetRow.Id);

                    targetDetails.Add(new VocabularyLearningTargetDetail(
                        targetId: targetRow.Id,
                        senseId: targetRow.SenseId,
                        stableId: targetRow.StableId ?? string.Empty,
                        targetKind: (LearningTargetKind)targetRow.TargetKind,
                        sourceLanguage: targetRow.SourceLanguage ?? string.Empty,
                        targetLanguage: targetRow.TargetLanguage ?? string.Empty,
                        typingOptOut: targetRow.TypingOptOut != 0,
                        createdAtUtc: ParseUtcTimestamp(targetRow.CreatedAtUtc),
                        updatedAtUtc: ParseUtcTimestamp(targetRow.UpdatedAtUtc),
                        fsrsState: fsrsState,
                        answerVariants: variants,
                        hasMoreAnswerVariants: hasMoreVariants,
                        reviewHistory: history,
                        hasMoreReviewHistory: hasMoreHistory,
                        isUnblockedByLearningControls: isSenseUnblockedByLearningControls));
                }
            }

            var hasMoreTargets = hasMoreTargetsBySenseId.Contains(senseRow.Id);

            senseDetails.Add(new VocabularySenseDetail(
                senseId: senseRow.Id,
                wordId: senseRow.WordId,
                stableId: senseRow.StableId ?? string.Empty,
                sourceLanguage: senseRow.SourceLanguage ?? string.Empty,
                explanationLanguage: senseRow.ExplanationLanguage ?? string.Empty,
                providerSenseId: senseRow.ProviderSenseId ?? string.Empty,
                topicOrDomain: senseRow.TopicOrDomain ?? string.Empty,
                partOfSpeech: senseRow.PartOfSpeech ?? string.Empty,
                grammaticalRelationship: senseRow.GrammaticalRelationship ?? string.Empty,
                acronymExpansion: senseRow.AcronymExpansion ?? string.Empty,
                defaultMeaningId: senseRow.DefaultMeaningId,
                createdAtUtc: ParseUtcTimestamp(senseRow.CreatedAtUtc),
                updatedAtUtc: ParseUtcTimestamp(senseRow.UpdatedAtUtc),
                senseLearningControl: senseControl,
                meanings: meanings,
                hasMoreMeanings: hasMoreMeanings,
                contextSnapshots: contexts,
                hasMoreContextSnapshots: hasMoreContexts,
                learningTargets: targetDetails,
                hasMoreLearningTargets: hasMoreTargets));
        }

        var detail = new VocabularyDetail(
            wordId: wordRow.Id,
            canonicalTerm: wordRow.CanonicalTerm ?? string.Empty,
            normalizedTerm: wordRow.NormalizedTerm ?? string.Empty,
            sourceLanguage: wordRow.Language ?? string.Empty,
            tokenKind: (TokenKind)wordRow.TokenKind,
            totalOccurrenceCount: wordRow.TotalOccurrenceCount,
            documentCount: wordRow.DocumentCount,
            createdAtUtc: ToUtcDateTimeOffset(wordRow.CreatedAt),
            updatedAtUtc: ToUtcDateTimeOffset(wordRow.UpdatedAt),
            wordLearningControl: wordControl,
            senses: senseDetails,
            hasMoreSenses: hasMoreSenses,
            maxItemsPerCollection: effectiveLimit);

        return VocabularyDetailLookupResult.Found(detail, effectiveLimit);
    }

    private static VocabularyWordLearningControlDetail? LoadWordLearningControl(
        SQLiteConnection connection,
        int wordId)
    {
        var rows = connection.Query<WordControlProjectionRow>(
            """
            SELECT WordId, DecidedAtUtc
            FROM WordLearningControls
            WHERE WordId = ?
            LIMIT 1
            """,
            wordId);

        if (rows.Count == 0)
        {
            return null;
        }

        var row = rows[0];
        return new VocabularyWordLearningControlDetail(
            row.WordId,
            ParseUtcTimestamp(row.DecidedAtUtc));
    }

    private static Dictionary<int, VocabularySenseLearningControlDetail> LoadSenseLearningControls(
        SQLiteConnection connection,
        int wordId,
        IReadOnlyList<int> senseIds)
    {
        var placeholders = BuildParameterPlaceholders(senseIds.Count);
        var args = new object[1 + senseIds.Count];
        args[0] = wordId;
        for (var i = 0; i < senseIds.Count; i++)
        {
            args[i + 1] = senseIds[i];
        }

        var rows = connection.Query<SenseControlProjectionRow>(
            $"""
            SELECT c.SenseId, c.DecidedAtUtc
            FROM SenseLearningControls c
            INNER JOIN Senses s ON s.Id = c.SenseId
            WHERE s.WordId = ? AND c.SenseId IN ({placeholders})
            ORDER BY c.SenseId ASC
            """,
            args);

        var result = new Dictionary<int, VocabularySenseLearningControlDetail>(rows.Count);
        foreach (var row in rows)
        {
            result[row.SenseId] = new VocabularySenseLearningControlDetail(
                row.SenseId,
                ParseUtcTimestamp(row.DecidedAtUtc));
        }

        return result;
    }

    private static (Dictionary<int, IReadOnlyList<VocabularyMeaningDetail>> MeaningsBySenseId, HashSet<int> HasMoreBySenseId)
        LoadBoundedMeaningsBySense(
            SQLiteConnection connection,
            int wordId,
            IReadOnlyList<int> senseIds,
            int effectiveLimit,
            int fetchLimit)
    {
        var placeholders = BuildParameterPlaceholders(senseIds.Count);
        var args = new object[2 + senseIds.Count];
        args[0] = wordId;
        for (var i = 0; i < senseIds.Count; i++)
        {
            args[i + 1] = senseIds[i];
        }
        args[1 + senseIds.Count] = fetchLimit;

        var rows = connection.Query<MeaningProjectionRow>(
            $"""
            SELECT Id, SenseId, WordId, StableId, SourceLanguage, ExplanationLanguage,
                   DisplayTerm, EncounteredSurfaceForm, GrammaticalRelationship, TokenKind,
                   SelectedMeaningId, AcronymExpansion, Definition, Translation,
                   DictionaryExample, AdditionalNote, AcceptedAliasesJson,
                   Source, SourceProject, SourcePageTitle, SourceRevisionId,
                   Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt
            FROM (
                SELECT m.Id, m.SenseId, m.WordId, m.StableId, m.SourceLanguage, m.ExplanationLanguage,
                       m.DisplayTerm, m.EncounteredSurfaceForm, m.GrammaticalRelationship, m.TokenKind,
                       m.SelectedMeaningId, m.AcronymExpansion, m.Definition, m.Translation,
                       m.DictionaryExample, m.AdditionalNote, m.AcceptedAliasesJson,
                       m.Source, m.SourceProject, m.SourcePageTitle, m.SourceRevisionId,
                       m.Attribution, m.ConfirmedByUser, m.CreatedAt, m.UpdatedAt, m.PreparedAt,
                       ROW_NUMBER() OVER (
                           PARTITION BY m.SenseId
                           ORDER BY CASE WHEN s.DefaultMeaningId IS NOT NULL AND m.Id = s.DefaultMeaningId THEN 0 ELSE 1 END ASC,
                                    m.Id ASC
                       ) AS RowNum
                FROM Meanings m
                INNER JOIN Senses s ON s.Id = m.SenseId AND s.WordId = m.WordId
                WHERE m.WordId = ? AND m.SenseId IN ({placeholders})
            )
            WHERE RowNum <= ?
            ORDER BY SenseId ASC, RowNum ASC
            """,
            args);

        var grouped = new Dictionary<int, List<VocabularyMeaningDetail>>();
        var hasMore = new HashSet<int>();

        foreach (var row in rows)
        {
            if (!grouped.TryGetValue(row.SenseId, out var list))
            {
                list = [];
                grouped[row.SenseId] = list;
            }

            if (list.Count >= effectiveLimit)
            {
                hasMore.Add(row.SenseId);
                continue;
            }

            list.Add(new VocabularyMeaningDetail(
                meaningId: row.Id,
                senseId: row.SenseId,
                wordId: row.WordId,
                stableId: row.StableId,
                sourceLanguage: row.SourceLanguage ?? string.Empty,
                explanationLanguage: row.ExplanationLanguage ?? string.Empty,
                displayTerm: row.DisplayTerm ?? string.Empty,
                encounteredSurfaceForm: row.EncounteredSurfaceForm ?? string.Empty,
                grammaticalRelationship: row.GrammaticalRelationship ?? string.Empty,
                tokenKind: (TokenKind)row.TokenKind,
                selectedMeaningId: row.SelectedMeaningId ?? string.Empty,
                acronymExpansion: row.AcronymExpansion ?? string.Empty,
                definition: row.Definition ?? string.Empty,
                translation: row.Translation ?? string.Empty,
                dictionaryExample: row.DictionaryExample ?? string.Empty,
                additionalNote: row.AdditionalNote ?? string.Empty,
                acceptedAliases: Schema8SemanticUpgradePolicy.DeserializeAliases(row.AcceptedAliasesJson ?? string.Empty),
                source: row.Source ?? string.Empty,
                sourceProject: row.SourceProject ?? string.Empty,
                sourcePageTitle: row.SourcePageTitle ?? string.Empty,
                sourceRevisionId: row.SourceRevisionId,
                attribution: row.Attribution ?? string.Empty,
                confirmedByUser: row.ConfirmedByUser != 0,
                createdAtUtc: ToUtcDateTimeOffset(row.CreatedAt),
                updatedAtUtc: ToUtcDateTimeOffset(row.UpdatedAt),
                preparedAtUtc: ToUtcDateTimeOffset(row.PreparedAt)));
        }

        var result = new Dictionary<int, IReadOnlyList<VocabularyMeaningDetail>>(grouped.Count);
        foreach (var (senseId, list) in grouped)
        {
            result[senseId] = list;
        }

        return (result, hasMore);
    }

    private static (Dictionary<int, IReadOnlyList<VocabularyContextSnapshotDetail>> ContextsBySenseId, HashSet<int> HasMoreBySenseId)
        LoadBoundedContextsBySense(
            SQLiteConnection connection,
            int wordId,
            IReadOnlyList<int> senseIds,
            int effectiveLimit,
            int fetchLimit)
    {
        var placeholders = BuildParameterPlaceholders(senseIds.Count);
        var args = new object[2 + senseIds.Count];
        args[0] = wordId;
        for (var i = 0; i < senseIds.Count; i++)
        {
            args[i + 1] = senseIds[i];
        }
        args[1 + senseIds.Count] = fetchLimit;

        var rows = connection.Query<ContextSnapshotProjectionRow>(
            $"""
            SELECT Id, MeaningId, SenseId, WordId, SourceDocumentId, SourceDocumentTitle,
                   Text, TargetStart, TargetLength, NormalizedFingerprint, CreatedAtUtc
            FROM (
                SELECT x.Id, x.MeaningId, x.SenseId, x.WordId, x.SourceDocumentId, x.SourceDocumentTitle,
                       x.Text, x.TargetStart, x.TargetLength, x.NormalizedFingerprint, x.CreatedAtUtc,
                       ROW_NUMBER() OVER (
                           PARTITION BY x.SenseId
                           ORDER BY x.Id ASC
                       ) AS RowNum
                FROM ContextSnapshots x
                INNER JOIN Senses s ON s.Id = x.SenseId AND s.WordId = x.WordId
                INNER JOIN Meanings m ON m.Id = x.MeaningId AND m.SenseId = x.SenseId AND m.WordId = x.WordId
                WHERE x.WordId = ? AND x.SenseId IN ({placeholders})
            )
            WHERE RowNum <= ?
            ORDER BY SenseId ASC, RowNum ASC
            """,
            args);

        var grouped = new Dictionary<int, List<VocabularyContextSnapshotDetail>>();
        var hasMore = new HashSet<int>();

        foreach (var row in rows)
        {
            if (!grouped.TryGetValue(row.SenseId, out var list))
            {
                list = [];
                grouped[row.SenseId] = list;
            }

            if (list.Count >= effectiveLimit)
            {
                hasMore.Add(row.SenseId);
                continue;
            }

            list.Add(new VocabularyContextSnapshotDetail(
                contextSnapshotId: row.Id,
                meaningId: row.MeaningId,
                senseId: row.SenseId,
                wordId: row.WordId,
                sourceDocumentId: row.SourceDocumentId,
                sourceDocumentTitle: row.SourceDocumentTitle ?? string.Empty,
                text: row.Text ?? string.Empty,
                targetStart: row.TargetStart,
                targetLength: row.TargetLength,
                normalizedFingerprint: row.NormalizedFingerprint ?? string.Empty,
                createdAtUtc: ToUtcDateTimeOffset(row.CreatedAtUtc)));
        }

        var result = new Dictionary<int, IReadOnlyList<VocabularyContextSnapshotDetail>>(grouped.Count);
        foreach (var (senseId, list) in grouped)
        {
            result[senseId] = list;
        }

        return (result, hasMore);
    }

    private static (Dictionary<int, List<LearningTargetProjectionRow>> TargetRowsBySenseId, HashSet<int> HasMoreBySenseId, List<int> KeptTargetIds)
        LoadBoundedTargetRowsBySense(
            SQLiteConnection connection,
            int wordId,
            IReadOnlyList<int> senseIds,
            int effectiveLimit,
            int fetchLimit)
    {
        var placeholders = BuildParameterPlaceholders(senseIds.Count);
        var args = new object[2 + senseIds.Count];
        args[0] = wordId;
        for (var i = 0; i < senseIds.Count; i++)
        {
            args[i + 1] = senseIds[i];
        }
        args[1 + senseIds.Count] = fetchLimit;

        var rows = connection.Query<LearningTargetProjectionRow>(
            $"""
            SELECT Id, StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage,
                   TypingOptOut, CreatedAtUtc, UpdatedAtUtc
            FROM (
                SELECT t.Id, t.StableId, t.SenseId, t.TargetKind, t.SourceLanguage, t.TargetLanguage,
                       t.TypingOptOut, t.CreatedAtUtc, t.UpdatedAtUtc,
                       ROW_NUMBER() OVER (
                           PARTITION BY t.SenseId
                           ORDER BY t.Id ASC
                       ) AS RowNum
                FROM LearningTargets t
                INNER JOIN Senses s ON s.Id = t.SenseId
                WHERE s.WordId = ? AND t.SenseId IN ({placeholders})
            )
            WHERE RowNum <= ?
            ORDER BY SenseId ASC, RowNum ASC
            """,
            args);

        var grouped = new Dictionary<int, List<LearningTargetProjectionRow>>();
        var hasMore = new HashSet<int>();
        var keptTargetIds = new List<int>(rows.Count);

        foreach (var row in rows)
        {
            if (!grouped.TryGetValue(row.SenseId, out var list))
            {
                list = [];
                grouped[row.SenseId] = list;
            }

            if (list.Count >= effectiveLimit)
            {
                hasMore.Add(row.SenseId);
                continue;
            }

            list.Add(row);
            keptTargetIds.Add(row.Id);
        }

        return (grouped, hasMore, keptTargetIds);
    }

    private static Dictionary<int, VocabularyTargetFsrsStateDetail> LoadTargetFsrsStates(
        SQLiteConnection connection,
        int wordId,
        IReadOnlyList<int> targetIds)
    {
        var placeholders = BuildParameterPlaceholders(targetIds.Count);
        var args = new object[1 + targetIds.Count];
        args[0] = wordId;
        for (var i = 0; i < targetIds.Count; i++)
        {
            args[i + 1] = targetIds[i];
        }

        var rows = connection.Query<TargetFsrsStateProjectionRow>(
            $"""
            SELECT fs.TargetId, fs.State, fs.Stability, fs.Difficulty,
                   fs.LastReviewedAtUtc, fs.StepIndex, fs.DueAtUtc
            FROM TargetFsrsStates fs
            INNER JOIN LearningTargets t ON t.Id = fs.TargetId
            INNER JOIN Senses s ON s.Id = t.SenseId
            WHERE s.WordId = ? AND fs.TargetId IN ({placeholders})
            ORDER BY fs.TargetId ASC
            """,
            args);

        var result = new Dictionary<int, VocabularyTargetFsrsStateDetail>(rows.Count);
        foreach (var row in rows)
        {
            result[row.TargetId] = new VocabularyTargetFsrsStateDetail(
                TargetId: row.TargetId,
                State: (Fsrs6CardState)row.State,
                Stability: row.Stability,
                Difficulty: row.Difficulty,
                LastReviewedAtUtc: ParseOptionalUtcTimestamp(row.LastReviewedAtUtc),
                StepIndex: row.StepIndex,
                DueAtUtc: ParseOptionalUtcTimestamp(row.DueAtUtc));
        }

        return result;
    }

    private static (Dictionary<int, IReadOnlyList<VocabularyAnswerVariantDetail>> VariantsByTargetId, HashSet<int> HasMoreByTargetId)
        LoadBoundedAnswerVariantsByTarget(
            SQLiteConnection connection,
            int wordId,
            IReadOnlyList<int> targetIds,
            int effectiveLimit,
            int fetchLimit)
    {
        var placeholders = BuildParameterPlaceholders(targetIds.Count);
        var args = new object[2 + targetIds.Count];
        args[0] = wordId;
        for (var i = 0; i < targetIds.Count; i++)
        {
            args[i + 1] = targetIds[i];
        }
        args[1 + targetIds.Count] = fetchLimit;

        var rows = connection.Query<TargetAnswerVariantProjectionRow>(
            $"""
            SELECT Id, StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText,
                   Requirement, IsPreferred, RequiredSinceUtc, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc
            FROM (
                SELECT v.Id, v.StableId, v.TargetId, v.AnswerLanguage, v.DisplayText, v.NormalizedText,
                       v.Requirement, v.IsPreferred, v.RequiredSinceUtc, v.SourceMeaningId, v.CreatedAtUtc, v.UpdatedAtUtc,
                       ROW_NUMBER() OVER (
                           PARTITION BY v.TargetId
                           ORDER BY v.IsPreferred DESC, v.Id ASC
                       ) AS RowNum
                FROM TargetAnswerVariants v
                INNER JOIN LearningTargets t ON t.Id = v.TargetId
                INNER JOIN Senses s ON s.Id = t.SenseId
                WHERE s.WordId = ? AND v.TargetId IN ({placeholders})
            )
            WHERE RowNum <= ?
            ORDER BY TargetId ASC, RowNum ASC
            """,
            args);

        var grouped = new Dictionary<int, List<VocabularyAnswerVariantDetail>>();
        var hasMore = new HashSet<int>();

        foreach (var row in rows)
        {
            if (!grouped.TryGetValue(row.TargetId, out var list))
            {
                list = [];
                grouped[row.TargetId] = list;
            }

            if (list.Count >= effectiveLimit)
            {
                hasMore.Add(row.TargetId);
                continue;
            }

            list.Add(new VocabularyAnswerVariantDetail(
                variantId: row.Id,
                targetId: row.TargetId,
                stableId: row.StableId ?? string.Empty,
                answerLanguage: row.AnswerLanguage ?? string.Empty,
                displayText: row.DisplayText ?? string.Empty,
                normalizedText: row.NormalizedText ?? string.Empty,
                requirement: (AnswerVariantRequirement)row.Requirement,
                isPreferred: row.IsPreferred != 0,
                requiredSinceUtc: ParseOptionalUtcTimestamp(row.RequiredSinceUtc),
                sourceMeaningId: row.SourceMeaningId,
                createdAtUtc: ParseUtcTimestamp(row.CreatedAtUtc),
                updatedAtUtc: ParseUtcTimestamp(row.UpdatedAtUtc)));
        }

        var result = new Dictionary<int, IReadOnlyList<VocabularyAnswerVariantDetail>>(grouped.Count);
        foreach (var (targetId, list) in grouped)
        {
            result[targetId] = list;
        }

        return (result, hasMore);
    }

    private static (Dictionary<int, IReadOnlyList<VocabularyTargetReviewHistoryEntryDetail>> HistoryByTargetId, HashSet<int> HasMoreByTargetId)
        LoadBoundedReviewHistoryByTarget(
            SQLiteConnection connection,
            int wordId,
            IReadOnlyList<int> targetIds,
            int effectiveLimit,
            int fetchLimit)
    {
        var placeholders = BuildParameterPlaceholders(targetIds.Count);
        var args = new object[2 + targetIds.Count];
        args[0] = wordId;
        for (var i = 0; i < targetIds.Count; i++)
        {
            args[i + 1] = targetIds[i];
        }
        args[1 + targetIds.Count] = fetchLimit;

        var rows = connection.Query<TargetReviewHistoryProjectionRow>(
            $"""
            SELECT Id, StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc
            FROM (
                SELECT h.Id, h.StableId, h.TargetId, h.SequenceNumber, h.Rating, h.ReviewedAtUtc,
                       ROW_NUMBER() OVER (
                           PARTITION BY h.TargetId
                           ORDER BY h.SequenceNumber ASC, h.Id ASC
                       ) AS RowNum
                FROM TargetFsrsReviewHistoryEntries h
                INNER JOIN LearningTargets t ON t.Id = h.TargetId
                INNER JOIN Senses s ON s.Id = t.SenseId
                WHERE s.WordId = ? AND h.TargetId IN ({placeholders})
            )
            WHERE RowNum <= ?
            ORDER BY TargetId ASC, RowNum ASC
            """,
            args);

        var grouped = new Dictionary<int, List<VocabularyTargetReviewHistoryEntryDetail>>();
        var hasMore = new HashSet<int>();

        foreach (var row in rows)
        {
            if (!grouped.TryGetValue(row.TargetId, out var list))
            {
                list = [];
                grouped[row.TargetId] = list;
            }

            if (list.Count >= effectiveLimit)
            {
                hasMore.Add(row.TargetId);
                continue;
            }

            list.Add(new VocabularyTargetReviewHistoryEntryDetail(
                entryId: row.Id,
                targetId: row.TargetId,
                stableId: row.StableId ?? string.Empty,
                sequenceNumber: row.SequenceNumber,
                rating: (ReviewRating)row.Rating,
                reviewedAtUtc: ParseUtcTimestamp(row.ReviewedAtUtc)));
        }

        var result = new Dictionary<int, IReadOnlyList<VocabularyTargetReviewHistoryEntryDetail>>(grouped.Count);
        foreach (var (targetId, list) in grouped)
        {
            result[targetId] = list;
        }

        return (result, hasMore);
    }

    private static string BuildParameterPlaceholders(int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be positive.");
        }

        return string.Join(", ", Enumerable.Repeat("?", count));
    }

    private static DateTimeOffset ToUtcDateTimeOffset(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private static DateTimeOffset? ParseOptionalUtcTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return ParseUtcTimestamp(raw);
    }

    private static DateTimeOffset ParseUtcTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DateTimeOffset.MinValue;
        }

        var trimmed = raw.Trim();
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
            && ticks >= DateTime.MinValue.Ticks
            && ticks <= DateTime.MaxValue.Ticks)
        {
            return new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc), TimeSpan.Zero);
        }

        try
        {
            return Schema13TimestampCodec.ParseUtcDateTimeOffset(trimmed);
        }
        catch (FormatException)
        {
            if (DateTimeOffset.TryParse(
                    trimmed,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return parsed;
            }

            throw;
        }
    }

    private sealed class WordProjectionRow
    {
        public int Id { get; set; }

        public string? Language { get; set; }

        public string? CanonicalTerm { get; set; }

        public string? NormalizedTerm { get; set; }

        public int TokenKind { get; set; }

        public int TotalOccurrenceCount { get; set; }

        public int DocumentCount { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }

    private sealed class SenseProjectionRow
    {
        public int Id { get; set; }

        public string? StableId { get; set; }

        public int WordId { get; set; }

        public string? SourceLanguage { get; set; }

        public string? ExplanationLanguage { get; set; }

        public string? ProviderSenseId { get; set; }

        public string? TopicOrDomain { get; set; }

        public string? PartOfSpeech { get; set; }

        public string? GrammaticalRelationship { get; set; }

        public string? AcronymExpansion { get; set; }

        public int? DefaultMeaningId { get; set; }

        public string? CreatedAtUtc { get; set; }

        public string? UpdatedAtUtc { get; set; }
    }

    private sealed class MeaningProjectionRow
    {
        public int Id { get; set; }

        public int SenseId { get; set; }

        public int WordId { get; set; }

        public string? StableId { get; set; }

        public string? SourceLanguage { get; set; }

        public string? ExplanationLanguage { get; set; }

        public string? DisplayTerm { get; set; }

        public string? EncounteredSurfaceForm { get; set; }

        public string? GrammaticalRelationship { get; set; }

        public int TokenKind { get; set; }

        public string? SelectedMeaningId { get; set; }

        public string? AcronymExpansion { get; set; }

        public string? Definition { get; set; }

        public string? Translation { get; set; }

        public string? DictionaryExample { get; set; }

        public string? AdditionalNote { get; set; }

        public string? AcceptedAliasesJson { get; set; }

        public string? Source { get; set; }

        public string? SourceProject { get; set; }

        public string? SourcePageTitle { get; set; }

        public long? SourceRevisionId { get; set; }

        public string? Attribution { get; set; }

        public int ConfirmedByUser { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public DateTime PreparedAt { get; set; }
    }

    private sealed class ContextSnapshotProjectionRow
    {
        public int Id { get; set; }

        public int MeaningId { get; set; }

        public int SenseId { get; set; }

        public int WordId { get; set; }

        public int SourceDocumentId { get; set; }

        public string? SourceDocumentTitle { get; set; }

        public string? Text { get; set; }

        public int TargetStart { get; set; }

        public int TargetLength { get; set; }

        public string? NormalizedFingerprint { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }

    private sealed class LearningTargetProjectionRow
    {
        public int Id { get; set; }

        public string? StableId { get; set; }

        public int SenseId { get; set; }

        public int TargetKind { get; set; }

        public string? SourceLanguage { get; set; }

        public string? TargetLanguage { get; set; }

        public int TypingOptOut { get; set; }

        public string? CreatedAtUtc { get; set; }

        public string? UpdatedAtUtc { get; set; }
    }

    private sealed class TargetFsrsStateProjectionRow
    {
        public int TargetId { get; set; }

        public int State { get; set; }

        public double? Stability { get; set; }

        public double? Difficulty { get; set; }

        public string? LastReviewedAtUtc { get; set; }

        public int? StepIndex { get; set; }

        public string? DueAtUtc { get; set; }
    }

    private sealed class TargetAnswerVariantProjectionRow
    {
        public int Id { get; set; }

        public string? StableId { get; set; }

        public int TargetId { get; set; }

        public string? AnswerLanguage { get; set; }

        public string? DisplayText { get; set; }

        public string? NormalizedText { get; set; }

        public int Requirement { get; set; }

        public int IsPreferred { get; set; }

        public string? RequiredSinceUtc { get; set; }

        public int? SourceMeaningId { get; set; }

        public string? CreatedAtUtc { get; set; }

        public string? UpdatedAtUtc { get; set; }
    }

    private sealed class TargetReviewHistoryProjectionRow
    {
        public int Id { get; set; }

        public string? StableId { get; set; }

        public int TargetId { get; set; }

        public int SequenceNumber { get; set; }

        public int Rating { get; set; }

        public string? ReviewedAtUtc { get; set; }
    }

    private sealed class WordControlProjectionRow
    {
        public int WordId { get; set; }

        public string? DecidedAtUtc { get; set; }
    }

    private sealed class SenseControlProjectionRow
    {
        public int SenseId { get; set; }

        public string? DecidedAtUtc { get; set; }
    }
}
