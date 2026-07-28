using System.Text.Json;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

public static class BackupModelMapper
{
    public static BackupPayload MapToExternal(BackupSnapshot snapshot)
    {
        // 1. Sort and map SourceMaterials (Documents)
        var sortedDocs = snapshot.Documents
            .OrderBy(d => d.Title, StringComparer.Ordinal)
            .ThenBy(d => d.TextLanguage, StringComparer.Ordinal)
            .ThenBy(d => d.ExplanationLanguage, StringComparer.Ordinal)
            .ThenBy(d => d.ContentFingerprint, StringComparer.Ordinal)
            .ThenBy(d => d.Content, StringComparer.Ordinal)
            .ThenBy(d => (int)d.LookupMode)
            .ThenBy(d => d.TargetLanguage ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(d => d.WordCount)
            .ThenBy(d => EnsureUtc(d.ImportedAt).Ticks)
            .ThenBy(d => d.Id)
            .ToList();

        var docIdMap = new Dictionary<int, string>();
        for (int i = 0; i < sortedDocs.Count; i++)
        {
            docIdMap[sortedDocs[i].Id] = $"sm-{(i + 1):D6}";
        }

        // 2. Sort and map SentenceSpans
        var sortedSentences = snapshot.SentenceSpans
            .OrderBy(s => docIdMap.TryGetValue(s.DocumentId, out var externalDocId) ? externalDocId : string.Empty, StringComparer.Ordinal)
            .ThenBy(s => s.Order)
            .ThenBy(s => s.StartPosition)
            .ThenBy(s => s.Length)
            .ThenBy(s => s.Id)
            .ToList();

        var sentenceIdMap = new Dictionary<int, string>();
        for (int i = 0; i < sortedSentences.Count; i++)
        {
            sentenceIdMap[sortedSentences[i].Id] = $"ss-{(i + 1):D6}";
        }

        // 3. Sort and map Vocabulary (Words)
        var sortedWords = snapshot.Words
            .OrderBy(w => w.Language.ToLowerInvariant(), StringComparer.Ordinal)
            .ThenBy(w => w.NormalizedTerm.ToLowerInvariant(), StringComparer.Ordinal)
            .ThenBy(w => w.CanonicalTerm, StringComparer.Ordinal)
            .ThenBy(w => (int)w.TokenKind)
            .ThenBy(w => (int)w.Status)
            .ThenBy(w => (int)w.PreparationState)
            .ThenBy(w => w.TotalOccurrenceCount)
            .ThenBy(w => w.DocumentCount)
            .ThenBy(w => EnsureUtc(w.CreatedAt).Ticks)
            .ThenBy(w => EnsureUtc(w.UpdatedAt).Ticks)
            .ThenBy(w => w.Id)
            .ToList();

        var vocabIdMap = new Dictionary<int, string>();
        for (int i = 0; i < sortedWords.Count; i++)
        {
            vocabIdMap[sortedWords[i].Id] = $"v-{(i + 1):D6}";
        }

        // 4. Sort and map PreparedItems (Meanings)
        var sortedMeanings = snapshot.Meanings
            .OrderBy(m => vocabIdMap.TryGetValue(m.WordId, out var externalVocabId) ? externalVocabId : string.Empty, StringComparer.Ordinal)
            .ThenBy(m => m.SourceLanguage, StringComparer.Ordinal)
            .ThenBy(m => m.ExplanationLanguage, StringComparer.Ordinal)
            .ThenBy(m => m.DisplayTerm, StringComparer.Ordinal)
            .ThenBy(m => m.EncounteredSurfaceForm ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(m => m.GrammaticalRelationship ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(m => (int)m.TokenKind)
            .ThenBy(m => m.SelectedMeaningId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(m => m.AcronymExpansion ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(m => m.Translation ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(m => m.Definition ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(m => m.DictionaryExample ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(m => m.AdditionalNote ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(m => m.TranslationOrDefinition ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(m => m.AcceptedAliasesJson ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(m => m.ConfirmedByUser)
            .ThenBy(m => m.Source ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(m => m.SourceProject ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(m => m.SourcePageTitle ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(m => m.SourceRevisionId ?? 0)
            .ThenBy(m => m.Attribution ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(m => EnsureUtc(m.CreatedAt).Ticks)
            .ThenBy(m => EnsureUtc(m.UpdatedAt).Ticks)
            .ThenBy(m => EnsureUtc(m.PreparedAt).Ticks)
            .ThenBy(m => m.Id)
            .ToList();

        var meaningIdMap = new Dictionary<int, string>();
        for (int i = 0; i < sortedMeanings.Count; i++)
        {
            meaningIdMap[sortedMeanings[i].Id] = $"m-{(i + 1):D6}";
        }

        // 5. Sort and map LearningCards
        var sortedCards = snapshot.LearningCards
            .OrderBy(c => vocabIdMap.TryGetValue(c.WordId, out var externalVocabId) ? externalVocabId : string.Empty, StringComparer.Ordinal)
            .ThenBy(c => meaningIdMap.TryGetValue(c.MeaningId, out var externalMeaningId) ? externalMeaningId : string.Empty, StringComparer.Ordinal)
            .ThenBy(c => (int)c.Direction)
            .ThenBy(c => (int)c.State)
            .ThenBy(c => EnsureUtc(c.DueAtUtc).Ticks)
            .ThenBy(c => c.IntervalDays)
            .ThenBy(c => c.EaseFactor)
            .ThenBy(c => c.SuccessfulReviewCount)
            .ThenBy(c => c.LapseCount)
            .ThenBy(c => c.LastReviewedAtUtc.HasValue ? EnsureUtc(c.LastReviewedAtUtc.Value).Ticks : 0)
            .ThenBy(c => c.LastRating.HasValue ? (int)c.LastRating.Value : -1)
            .ThenBy(c => EnsureUtc(c.CreatedAtUtc).Ticks)
            .ThenBy(c => EnsureUtc(c.UpdatedAtUtc).Ticks)
            .ThenBy(c => c.Id)
            .ToList();

        var cardIdMap = new Dictionary<int, string>();
        for (int i = 0; i < sortedCards.Count; i++)
        {
            cardIdMap[sortedCards[i].Id] = $"c-{(i + 1):D6}";
        }

        // 6. Sort and map VocabularyReviewWorkflows (ReviewSessions)
        var sortedReviewSessions = snapshot.ReviewSessions
            .OrderBy(rs => docIdMap.TryGetValue(rs.DocumentId, out var externalDocId) ? externalDocId : string.Empty, StringComparer.Ordinal)
            .ThenBy(rs => (int)rs.Status)
            .ThenBy(rs => rs.TotalCandidates)
            .ThenBy(rs => rs.ReviewedCount)
            .ThenBy(rs => rs.KnownCount)
            .ThenBy(rs => rs.UnknownCount)
            .ThenBy(rs => rs.IgnoredCount)
            .ThenBy(rs => rs.DecisionSequence)
            .ThenBy(rs => EnsureUtc(rs.StartedAt).Ticks)
            .ThenBy(rs => rs.CompletedAt.HasValue ? EnsureUtc(rs.CompletedAt.Value).Ticks : 0)
            .ThenBy(rs => rs.Id)
            .ToList();

        var reviewSessionIdMap = new Dictionary<int, string>();
        for (int i = 0; i < sortedReviewSessions.Count; i++)
        {
            reviewSessionIdMap[sortedReviewSessions[i].Id] = $"vr-{(i + 1):D6}";
        }

        // 7. Sort and map VocabularyReviewItems (ReviewCandidates)
        var sortedReviewCandidates = snapshot.ReviewCandidates
            .OrderBy(rc => reviewSessionIdMap.TryGetValue(rc.SessionId, out var externalSessionId) ? externalSessionId : string.Empty, StringComparer.Ordinal)
            .ThenBy(rc => rc.Order)
            .ThenBy(rc => vocabIdMap.TryGetValue(rc.WordId, out var externalVocabId) ? externalVocabId : string.Empty, StringComparer.Ordinal)
            .ThenBy(rc => (int)rc.Status)
            .ThenBy(rc => (int)rc.PreviousWordStatus)
            .ThenBy(rc => rc.PreviousTotalOccurrenceCount)
            .ThenBy(rc => rc.PreviousDocumentCount)
            .ThenBy(rc => EnsureUtc(rc.PreviousUpdatedAt).Ticks)
            .ThenBy(rc => rc.DecisionSequence)
            .ThenBy(rc => rc.WasWordCreatedForSession)
            .ThenBy(rc => rc.DecidedAt.HasValue ? EnsureUtc(rc.DecidedAt.Value).Ticks : 0)
            .ThenBy(rc => rc.Id)
            .ToList();

        var reviewCandidateIdMap = new Dictionary<int, string>();
        for (int i = 0; i < sortedReviewCandidates.Count; i++)
        {
            reviewCandidateIdMap[sortedReviewCandidates[i].Id] = $"rc-{(i + 1):D6}";
        }

        // 8. Sort and map PreparationWorkflows (PreparationSessions)
        var sortedPrepSessions = snapshot.PreparationSessions
            .OrderBy(ps => EnsureUtc(ps.StartedAtUtc).Ticks)
            .ThenBy(ps => (int)ps.Method)
            .ThenBy(ps => (int)ps.Status)
            .ThenBy(ps => ps.TotalItems)
            .ThenBy(ps => ps.CompletedItems)
            .ThenBy(ps => EnsureUtc(ps.UpdatedAtUtc).Ticks)
            .ThenBy(ps => ps.CompletedAtUtc.HasValue ? EnsureUtc(ps.CompletedAtUtc.Value).Ticks : 0)
            .ThenBy(ps => ps.Id)
            .ToList();

        var prepSessionIdMap = new Dictionary<int, string>();
        for (int i = 0; i < sortedPrepSessions.Count; i++)
        {
            prepSessionIdMap[sortedPrepSessions[i].Id] = $"pb-{(i + 1):D6}";
        }

        // 9. Sort and map PreparationItems (PreparationCandidates)
        var sortedPrepCandidates = snapshot.PreparationCandidates
            .OrderBy(pc => prepSessionIdMap.TryGetValue(pc.SessionId, out var externalSessionId) ? externalSessionId : string.Empty, StringComparer.Ordinal)
            .ThenBy(pc => pc.Order)
            .ThenBy(pc => vocabIdMap.TryGetValue(pc.WordId, out var externalVocabId) ? externalVocabId : string.Empty, StringComparer.Ordinal)
            .ThenBy(pc => (int)pc.Status)
            .ThenBy(pc => pc.SelectedMeaningIndex)
            .ThenBy(pc => pc.LastErrorCode ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(pc => pc.LookupAttemptCount)
            .ThenBy(pc => EnsureUtc(pc.UpdatedAtUtc).Ticks)
            .ThenBy(pc => pc.ResultJson ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(pc => pc.Id)
            .ToList();

        var prepCandidateIdMap = new Dictionary<int, string>();
        for (int i = 0; i < sortedPrepCandidates.Count; i++)
        {
            prepCandidateIdMap[sortedPrepCandidates[i].Id] = $"pi-{(i + 1):D6}";
        }

        // 10. Sort and map LearningWorkflows (LearningSessions)
        var sortedLearningSessions = snapshot.LearningSessions
            .OrderBy(ls => EnsureUtc(ls.StartedAtUtc).Ticks)
            .ThenBy(ls => (int)ls.Status)
            .ThenBy(ls => ls.TotalCards)
            .ThenBy(ls => ls.CompletedCards)
            .ThenBy(ls => ls.AgainCount)
            .ThenBy(ls => ls.HardCount)
            .ThenBy(ls => ls.GoodCount)
            .ThenBy(ls => ls.EasyCount)
            .ThenBy(ls => EnsureUtc(ls.UpdatedAtUtc).Ticks)
            .ThenBy(ls => ls.CompletedAtUtc.HasValue ? EnsureUtc(ls.CompletedAtUtc.Value).Ticks : 0)
            .ThenBy(ls => ls.Id)
            .ToList();

        var learningSessionIdMap = new Dictionary<int, string>();
        for (int i = 0; i < sortedLearningSessions.Count; i++)
        {
            learningSessionIdMap[sortedLearningSessions[i].Id] = $"ls-{(i + 1):D6}";
        }

        // 11. Sort and map LearningQueueItems (LearningSessionCards)
        var sortedLearningSessionCards = snapshot.LearningSessionCards
            .OrderBy(lsc => learningSessionIdMap.TryGetValue(lsc.SessionId, out var externalSessionId) ? externalSessionId : string.Empty, StringComparer.Ordinal)
            .ThenBy(lsc => lsc.QueueOrder)
            .ThenBy(lsc => cardIdMap.TryGetValue(lsc.CardId, out var externalCardId) ? externalCardId : string.Empty, StringComparer.Ordinal)
            .ThenBy(lsc => lsc.IsDueCard)
            .ThenBy(lsc => lsc.IsAgainRepeat)
            .ThenBy(lsc => lsc.AnswerRevealed)
            .ThenBy(lsc => lsc.SpellingChecked)
            .ThenBy(lsc => lsc.SpellingCorrect)
            .ThenBy(lsc => lsc.IsCompleted)
            .ThenBy(lsc => lsc.Rating.HasValue ? (int)lsc.Rating.Value : -1)
            .ThenBy(lsc => lsc.CompletedAtUtc.HasValue ? EnsureUtc(lsc.CompletedAtUtc.Value).Ticks : 0)
            .ThenBy(lsc => lsc.Id)
            .ToList();

        var learningQueueItemIdMap = new Dictionary<int, string>();
        for (int i = 0; i < sortedLearningSessionCards.Count; i++)
        {
            learningQueueItemIdMap[sortedLearningSessionCards[i].Id] = $"lq-{(i + 1):D6}";
        }

        var sourceMaterials = sortedDocs.Select(doc => MapSourceMaterial(doc, snapshot, docIdMap, sentenceIdMap, vocabIdMap)).ToList();
        var vocabulary = sortedWords.Select(w => MapVocabularyItem(w, snapshot, vocabIdMap)).ToList();
        var preparedLearning = sortedMeanings.Select(m => MapPreparedItem(m, snapshot, meaningIdMap, vocabIdMap, docIdMap)).ToList();

        var cards = sortedCards.Select(c => MapLearningCard(c, snapshot, cardIdMap, vocabIdMap, meaningIdMap)).ToList();
        var reviews = snapshot.LearningReviews
            .OrderBy(r => cardIdMap.TryGetValue(r.CardId, out var cId) ? cId : string.Empty, StringComparer.Ordinal)
            .ThenBy(r => learningSessionIdMap.TryGetValue(r.SessionId, out var sId) ? sId : string.Empty, StringComparer.Ordinal)
            .ThenBy(r => EnsureUtc(r.ReviewedAtUtc).Ticks)
            .ThenBy(r => EnsureUtc(r.DueAtUtc).Ticks)
            .ThenBy(r => (int)r.Rating)
            .ThenBy(r => r.WasTypedAnswer)
            .ThenBy(r => r.WasCorrect)
            .ThenBy(r => r.IntervalDays)
            .ThenBy(r => r.EaseFactor)
            .ThenBy(r => r.Id)
            .Select(r => MapLearningReview(r, snapshot, cardIdMap, learningSessionIdMap))
            .ToList();
        var learningData = new BackupLearningData(cards, reviews);

        var vocabReviews = sortedReviewSessions.Select(rs => MapVocabularyReviewWorkflow(rs, snapshot, reviewSessionIdMap, docIdMap, reviewCandidateIdMap, vocabIdMap)).ToList();
        var prepBatches = sortedPrepSessions.Select(ps => MapPreparationWorkflow(ps, snapshot, prepSessionIdMap, prepCandidateIdMap, vocabIdMap)).ToList();
        var learningSessions = sortedLearningSessions.Select(ls => MapLearningWorkflow(ls, snapshot, learningSessionIdMap, learningQueueItemIdMap, cardIdMap)).ToList();

        var workflows = new BackupWorkflowData(vocabReviews, prepBatches, learningSessions);

        return new BackupPayload(
            sourceMaterials,
            vocabulary,
            preparedLearning,
            learningData,
            workflows,
            new BackupExtensions(new Dictionary<string, BackupExtensionPayload>(StringComparer.Ordinal)));
    }

    private static BackupSourceMaterial MapSourceMaterial(
        DocumentEntity doc,
        BackupSnapshot snapshot,
        Dictionary<int, string> docIdMap,
        Dictionary<int, string> sentenceIdMap,
        Dictionary<int, string> vocabIdMap)
    {
        var docSentences = snapshot.SentenceSpans
            .Where(s => s.DocumentId == doc.Id)
            .OrderBy(s => s.Order)
            .Select(s => new BackupSentenceRange(
                sentenceIdMap.TryGetValue(s.Id, out var sId) ? sId : "ss-000000-missing",
                s.Order,
                s.StartPosition,
                s.Length))
            .ToList();

        var docOccurrences = snapshot.WordOccurrences
            .Where(o => o.DocumentId == doc.Id)
            .OrderBy(o => o.Order)
            .Select(o => new BackupOccurrence(
                vocabIdMap.TryGetValue(o.WordId, out var vId) ? vId : "v-000000-missing",
                sentenceIdMap.TryGetValue(o.SentenceSpanId, out var sId) ? sId : "ss-000000-missing",
                o.StartPosition,
                o.Length,
                o.SurfaceForm,
                o.Order,
                BackupEnumMappings.ToBackup(o.TechnicalFamily),
                o.TechnicalInstanceYear,
                string.IsNullOrEmpty(o.TechnicalInstanceIdentifier) ? null : o.TechnicalInstanceIdentifier,
                string.IsNullOrEmpty(o.TechnicalVariant) ? null : o.TechnicalVariant))
            .ToList();

        return new BackupSourceMaterial(
            docIdMap.TryGetValue(doc.Id, out var dId) ? dId : "sm-000000-missing",
            doc.Title,
            doc.TextLanguage,
            doc.ExplanationLanguage,
            BackupEnumMappings.ToBackup(doc.LookupMode),
            string.IsNullOrEmpty(doc.TargetLanguage) ? null : doc.TargetLanguage,
            doc.Content,
            doc.ContentFingerprint.ToLowerInvariant(),
            EnsureUtc(doc.ImportedAt),
            doc.WordCount,
            docSentences,
            docOccurrences);
    }

    private static BackupVocabularyItem MapVocabularyItem(
        WordEntity word,
        BackupSnapshot snapshot,
        Dictionary<int, string> vocabIdMap)
    {
        var forms = snapshot.WordForms
            .Where(f => f.WordId == word.Id)
            .Select(f => new BackupEncounteredForm(f.SurfaceForm, f.OccurrenceCount))
            .ToList();

        var legacySummaries = snapshot.ReviewStates
            .Where(r => r.WordId == word.Id)
            .Select(r => new BackupLegacyReviewSummary(
                r.ReviewCount,
                r.ForgotCount,
                r.PartialCount,
                r.KnownCount,
                EnsureUtc(r.LastReviewedAt)))
            .ToList();

        return new BackupVocabularyItem(
            vocabIdMap.TryGetValue(word.Id, out var vId) ? vId : "v-000000-missing",
            word.Language,
            word.CanonicalTerm,
            word.NormalizedTerm,
            BackupEnumMappings.ToBackup(word.TokenKind),
            BackupEnumMappings.ToBackup(word.Status),
            BackupEnumMappings.ToBackup(word.PreparationState),
            word.TotalOccurrenceCount,
            word.DocumentCount,
            EnsureUtc(word.CreatedAt),
            EnsureUtc(word.UpdatedAt),
            forms,
            new BackupAutomaticLearningState(
                BackupEnumMappings.ToBackup(word.AutomaticInteractionMode),
                word.ConsecutiveRecallSuccessCount,
                word.ConsecutiveTypingSuccessCount,
                word.ConsecutiveTypingFailureCount,
                word.MasteryReviewExtensionScheduled),
            legacySummaries);
    }

    private static BackupPreparedItem MapPreparedItem(
        MeaningEntity meaning,
        BackupSnapshot snapshot,
        Dictionary<int, string> meaningIdMap,
        Dictionary<int, string> vocabIdMap,
        Dictionary<int, string> docIdMap)
    {
        var contexts = snapshot.ContextSnapshots
            .Where(c => c.MeaningId == meaning.Id)
            .Select(c => new BackupContextSnapshot(
                docIdMap.TryGetValue(c.SourceDocumentId, out var dId) ? dId : "sm-000000-missing",
                c.SourceDocumentTitle,
                c.Text,
                c.TargetStart,
                c.TargetLength,
                c.NormalizedFingerprint,
                EnsureUtc(c.CreatedAtUtc)))
            .ToList();

        List<string> aliases = new();
        if (!string.IsNullOrEmpty(meaning.AcceptedAliasesJson))
        {
            using var doc = JsonDocument.Parse(meaning.AcceptedAliasesJson);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.GetString() is { } str)
                {
                    aliases.Add(str);
                }
            }
        }

        return new BackupPreparedItem(
            meaningIdMap.TryGetValue(meaning.Id, out var mId) ? mId : "m-000000-missing",
            vocabIdMap.TryGetValue(meaning.WordId, out var vId) ? vId : "v-000000-missing",
            meaning.SourceLanguage,
            meaning.ExplanationLanguage,
            meaning.DisplayTerm,
            string.IsNullOrEmpty(meaning.EncounteredSurfaceForm) ? null : meaning.EncounteredSurfaceForm,
            string.IsNullOrEmpty(meaning.GrammaticalRelationship) ? null : meaning.GrammaticalRelationship,
            BackupEnumMappings.ToBackup(meaning.TokenKind),
            string.IsNullOrEmpty(meaning.SelectedMeaningId) ? null : meaning.SelectedMeaningId,
            string.IsNullOrEmpty(meaning.AcronymExpansion) ? null : meaning.AcronymExpansion,
            string.IsNullOrEmpty(meaning.Translation) ? null : meaning.Translation,
            string.IsNullOrEmpty(meaning.Definition) ? null : meaning.Definition,
            string.IsNullOrEmpty(meaning.DictionaryExample) ? null : meaning.DictionaryExample,
            string.IsNullOrEmpty(meaning.AdditionalNote) ? null : meaning.AdditionalNote,
            string.IsNullOrEmpty(meaning.TranslationOrDefinition) ? null : meaning.TranslationOrDefinition,
            aliases,
            meaning.ConfirmedByUser,
            new BackupSourceReference(
                meaning.Source,
                meaning.SourceProject,
                meaning.SourcePageTitle,
                meaning.SourceRevisionId,
                meaning.Attribution),
            EnsureUtc(meaning.CreatedAt),
            EnsureUtc(meaning.UpdatedAt),
            EnsureUtc(meaning.PreparedAt),
            contexts);
    }

    private static BackupLearningCard MapLearningCard(
        LearningCardEntity card,
        BackupSnapshot snapshot,
        Dictionary<int, string> cardIdMap,
        Dictionary<int, string> vocabIdMap,
        Dictionary<int, string> meaningIdMap)
    {
        return new BackupLearningCard(
            cardIdMap.TryGetValue(card.Id, out var cId) ? cId : "c-000000-missing",
            vocabIdMap.TryGetValue(card.WordId, out var vId) ? vId : "v-000000-missing",
            meaningIdMap.TryGetValue(card.MeaningId, out var mId) ? mId : "m-000000-missing",
            BackupEnumMappings.ToBackup(card.Direction),
            BackupEnumMappings.ToBackup(card.State),
            EnsureUtc(card.DueAtUtc),
            card.IntervalDays,
            card.EaseFactor,
            card.SuccessfulReviewCount,
            card.LapseCount,
            EnsureUtc(card.LastReviewedAtUtc),
            card.LastRating.HasValue ? BackupEnumMappings.ToBackup(card.LastRating.Value) : null,
            EnsureUtc(card.CreatedAtUtc),
            EnsureUtc(card.UpdatedAtUtc));
    }

    private static BackupLearningReview MapLearningReview(
        LearningReviewEntity review,
        BackupSnapshot snapshot,
        Dictionary<int, string> cardIdMap,
        Dictionary<int, string> learningSessionIdMap)
    {
        return new BackupLearningReview(
            cardIdMap.TryGetValue(review.CardId, out var cId) ? cId : "c-000000-missing",
            learningSessionIdMap.TryGetValue(review.SessionId, out var sId) ? sId : "ls-000000-missing",
            BackupEnumMappings.ToBackup(review.Rating),
            review.WasTypedAnswer,
            review.WasCorrect,
            EnsureUtc(review.ReviewedAtUtc),
            EnsureUtc(review.DueAtUtc),
            review.IntervalDays,
            review.EaseFactor);
    }

    private static BackupVocabularyReviewWorkflow MapVocabularyReviewWorkflow(
        ReviewSessionEntity session,
        BackupSnapshot snapshot,
        Dictionary<int, string> reviewSessionIdMap,
        Dictionary<int, string> docIdMap,
        Dictionary<int, string> reviewCandidateIdMap,
        Dictionary<int, string> vocabIdMap)
    {
        var items = snapshot.ReviewCandidates
            .Where(i => i.SessionId == session.Id)
            .OrderBy(i => i.Order)
            .Select(i => new BackupVocabularyReviewItem(
                reviewCandidateIdMap.TryGetValue(i.Id, out var rcId) ? rcId : "rc-000000-missing",
                vocabIdMap.TryGetValue(i.WordId, out var vId) ? vId : "v-000000-missing",
                i.Order,
                BackupEnumMappings.ToBackup(i.Status),
                BackupEnumMappings.ToBackup(i.PreviousWordStatus),
                i.PreviousTotalOccurrenceCount,
                i.PreviousDocumentCount,
                EnsureUtc(i.PreviousUpdatedAt),
                i.DecisionSequence,
                i.WasWordCreatedForSession,
                EnsureUtc(i.DecidedAt)))
            .ToList();

        return new BackupVocabularyReviewWorkflow(
            reviewSessionIdMap.TryGetValue(session.Id, out var rsId) ? rsId : "vr-000000-missing",
            docIdMap.TryGetValue(session.DocumentId, out var dId) ? dId : "sm-000000-missing",
            BackupEnumMappings.ToBackup(session.Status),
            session.TotalCandidates,
            session.ReviewedCount,
            session.KnownCount,
            session.UnknownCount,
            session.IgnoredCount,
            session.DecisionSequence,
            EnsureUtc(session.StartedAt),
            EnsureUtc(session.CompletedAt),
            items);
    }

    private static BackupPreparationWorkflow MapPreparationWorkflow(
        PreparationSessionEntity session,
        BackupSnapshot snapshot,
        Dictionary<int, string> prepSessionIdMap,
        Dictionary<int, string> prepCandidateIdMap,
        Dictionary<int, string> vocabIdMap)
    {
        var items = snapshot.PreparationCandidates
            .Where(i => i.SessionId == session.Id)
            .OrderBy(i => i.Order)
            .Select(i =>
            {
                BackupLookupDraft? draft = null;
                if (!string.IsNullOrEmpty(i.ResultJson))
                {
                    draft = ParseLookupDraft(i.ResultJson);
                }

                return new BackupPreparationItem(
                    prepCandidateIdMap.TryGetValue(i.Id, out var piId) ? piId : "pi-000000-missing",
                    vocabIdMap.TryGetValue(i.WordId, out var vId) ? vId : "v-000000-missing",
                    i.Order,
                    BackupEnumMappings.ToBackup(i.Status),
                    i.SelectedMeaningIndex,
                    string.IsNullOrEmpty(i.LastErrorCode) ? null : i.LastErrorCode,
                    i.LookupAttemptCount,
                    EnsureUtc(i.UpdatedAtUtc),
                    draft);
            })
            .ToList();

        return new BackupPreparationWorkflow(
            prepSessionIdMap.TryGetValue(session.Id, out var psId) ? psId : "pb-000000-missing",
            BackupEnumMappings.ToBackup(session.Status),
            BackupEnumMappings.ToBackup(session.Method),
            session.TotalItems,
            session.CompletedItems,
            EnsureUtc(session.StartedAtUtc),
            EnsureUtc(session.UpdatedAtUtc),
            EnsureUtc(session.CompletedAtUtc),
            items);
    }

    private static BackupLearningWorkflow MapLearningWorkflow(
        LearningSessionEntity session,
        BackupSnapshot snapshot,
        Dictionary<int, string> learningSessionIdMap,
        Dictionary<int, string> learningQueueItemIdMap,
        Dictionary<int, string> cardIdMap)
    {
        var items = snapshot.LearningSessionCards
            .Where(c => c.SessionId == session.Id)
            .OrderBy(c => c.QueueOrder)
            .Select(c => new BackupLearningQueueItem(
                learningQueueItemIdMap.TryGetValue(c.Id, out var lqId) ? lqId : "lq-000000-missing",
                cardIdMap.TryGetValue(c.CardId, out var cardId) ? cardId : "c-000000-missing",
                c.QueueOrder,
                c.IsDueCard,
                c.IsAgainRepeat,
                c.AnswerRevealed,
                c.SpellingChecked,
                c.SpellingCorrect,
                c.IsCompleted,
                c.Rating.HasValue ? BackupEnumMappings.ToBackup(c.Rating.Value) : null,
                EnsureUtc(c.CompletedAtUtc)))
            .ToList();

        return new BackupLearningWorkflow(
            learningSessionIdMap.TryGetValue(session.Id, out var lsId) ? lsId : "ls-000000-missing",
            BackupEnumMappings.ToBackup(session.Status),
            session.TotalCards,
            session.CompletedCards,
            session.AgainCount,
            session.HardCount,
            session.GoodCount,
            session.EasyCount,
            EnsureUtc(session.StartedAtUtc),
            EnsureUtc(session.UpdatedAtUtc),
            EnsureUtc(session.CompletedAtUtc),
            items);
    }

    private static DateTime EnsureUtc(DateTime dt) => dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    private static DateTime? EnsureUtc(DateTime? dt) => dt.HasValue ? EnsureUtc(dt.Value) : null;

    internal static BackupLookupDraft ParseLookupDraft(string resultJson)
    {
        try
        {
            var internalResult = JsonSerializer.Deserialize(
                resultJson,
                KnownFirst.Core.Preparation.LexicalJsonSerializerContext.Default.LexicalResult)
                ?? throw new BackupFormatException(BackupErrorCodes.DataJsonInvalid);

            var meanings = internalResult.Meanings.Select(m => new BackupLookupMeaning(
                m.MeaningId,
                string.IsNullOrEmpty(m.PartOfSpeech) ? null : m.PartOfSpeech,
                m.Definition ?? string.Empty,
                string.IsNullOrEmpty(m.Translation) ? null : m.Translation,
                string.IsNullOrEmpty(m.Example) ? null : m.Example,
                m.UsageLabels ?? Array.Empty<string>())).ToList();

            var relations = internalResult.FormRelations?.Select(r => new BackupFormRelation(
                BackupEnumMappings.ToBackup(r.Kind),
                r.BaseLemma,
                r.Relationship)).ToList() ?? new List<BackupFormRelation>();

            return new BackupLookupDraft(
                BackupEnumMappings.ToBackup(internalResult.Status),
                BackupEnumMappings.ToBackup(internalResult.LookupMode ?? KnownFirst.Core.Preparation.LexicalLookupMode.Definition),
                internalResult.SourceLanguage,
                internalResult.ExplanationLanguage,
                string.IsNullOrEmpty(internalResult.TargetLanguage) ? null : internalResult.TargetLanguage,
                internalResult.QueriedLemma,
                internalResult.DisplayTerm,
                BackupEnumMappings.ToBackup(internalResult.TokenKind),
                string.IsNullOrEmpty(internalResult.AcronymExpansion) ? null : internalResult.AcronymExpansion,
                string.IsNullOrEmpty(internalResult.EncounteredSurfaceForm) ? null : internalResult.EncounteredSurfaceForm,
                string.IsNullOrEmpty(internalResult.GrammaticalRelationship) ? null : internalResult.GrammaticalRelationship,
                meanings,
                new BackupSourceReference(
                    internalResult.ProviderName,
                    internalResult.SourceProject,
                    internalResult.PageTitle,
                    internalResult.RevisionId,
                    internalResult.Attribution),
                internalResult.LookupAtUtc,
                internalResult.RedirectDepth,
                relations);
        }
        catch (BackupFormatException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            throw new BackupFormatException(BackupErrorCodes.DataJsonInvalid, exception);
        }
    }
}
