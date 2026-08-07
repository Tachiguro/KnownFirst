using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema8;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

/// <summary>
/// Maps a captured <see cref="Schema8BackupSnapshot"/> into the external/archive <see cref="BackupPayloadV2"/>
/// model (KF-MEANING-001 Slice 2). Mirrors <see cref="BackupModelMapper"/>'s deterministic-ordering
/// pattern, but corrected: no output collection is ordered (even as a final tiebreak) by a local SQLite
/// numeric id anywhere in the graph — every collection uses its own StableId, canonical parent identity,
/// content fingerprint, or already-required-unique semantic key instead. Local numeric ids are used only
/// to build the in-memory FK-resolution dictionaries during mapping.
///
/// <para><b>One deliberate exception:</b> <c>ReviewSessions</c> ends its ordering with the local row id.
/// The <c>Id</c> tie-break is reached only after all retained session-level fields used by this mapper's
/// ordering compare equal. Two such sessions may still contain different candidate rows and therefore may
/// be genuinely distinct under the Schema-9 v2 full-history identity. <see cref="Merge.MergeWriterTargetIndex"/>
/// rejects wholly identical full-history identities, but does not reject sessions that differ only through
/// candidate history. The <c>Id</c> tie-break guarantees deterministic ordering within one captured snapshot
/// and follows the established v1 precedent in <see cref="BackupModelMapper"/>. Cross-installation canonical
/// ordering for sessions whose session-level fields tie but candidate histories differ is not claimed by
/// Package B and remains a Package C convergence concern.</para>
/// </summary>
public static class BackupModelMapperV2
{
    public static BackupPayloadV2 MapToExternal(Schema8BackupSnapshot snapshot)
    {
        // ---- SourceMaterials (Documents): ordered by content hash, never local id ----
        var sortedDocs = snapshot.Documents
            .OrderBy(d => d.ContentFingerprint, StringComparer.Ordinal)
            .ThenBy(d => d.Title, StringComparer.Ordinal)
            .ToList();
        var docIdMap = BuildIdMap(sortedDocs, d => d.Id, "sm-");

        var sentenceIdMap = new Dictionary<int, string>();
        foreach (var doc in sortedDocs)
        {
            var docSentences = snapshot.SentenceSpans.Where(s => s.DocumentId == doc.Id).OrderBy(s => s.Order).ToList();
            foreach (var sentence in docSentences)
            {
                sentenceIdMap[sentence.Id] = $"ss-{docIdMap[doc.Id]}-{sentence.Order:D6}";
            }
        }

        // ---- Vocabulary (Words): ordered by (Language, IdentityKey) — required-unique ----
        var sortedWords = snapshot.Words
            .OrderBy(w => w.Language.ToLowerInvariant(), StringComparer.Ordinal)
            .ThenBy(w => w.NormalizedTerm.ToLowerInvariant(), StringComparer.Ordinal)
            .ToList();
        var vocabIdMap = BuildIdMap(sortedWords, w => w.Id, "v-");

        // ---- Senses: ordered by StableId ----
        var sortedSenses = snapshot.Senses.OrderBy(s => s.StableId, StringComparer.Ordinal).ToList();
        var senseIdMap = BuildIdMap(sortedSenses, s => s.Id, "se-");

        // ---- Meanings: ordered by StableId ----
        var sortedMeanings = snapshot.Meanings.OrderBy(m => m.StableId, StringComparer.Ordinal).ToList();
        var meaningIdMap = BuildIdMap(sortedMeanings, m => m.Id, "m-");

        // ---- AnswerVariants: ordered by StableId ----
        var sortedVariants = snapshot.AnswerVariants.OrderBy(v => v.StableId, StringComparer.Ordinal).ToList();
        var variantIdMap = BuildIdMap(sortedVariants, v => v.Id, "av-");

        // ---- Assignments: ordered by StableId (StableId doubles as the archive-local id — no other
        // entity references an assignment by a separate archive-local id, so a distinct prefix-based id
        // would be unused bookkeeping) ----
        var sortedAssignments = snapshot.Assignments.OrderBy(a => a.StableId, StringComparer.Ordinal).ToList();

        // ---- Cards: ordered by (SenseStableId, Direction) — unique by construction ----
        var sortedCards = snapshot.LearningCards
            .OrderBy(c => c.SenseId.HasValue && senseIdMap.ContainsKey(c.SenseId.Value) ? SenseStableIdOf(c.SenseId.Value, snapshot) : string.Empty, StringComparer.Ordinal)
            .ThenBy(c => (int)c.Direction)
            .ToList();
        var cardIdMap = BuildIdMap(sortedCards, c => c.Id, "c-");

        // ---- Workflow parents: content-fingerprint ordered (no natural single unique field) ----
        //
        // ReviewSessions use explicit typed comparisons over every retained completed-review field rather
        // than a delimiter-free ContentKey, and end in the local row id as a guaranteed final tie-break.
        // Schema 9 replaced the legacy unique ReviewSessions(DocumentId) index with a non-unique index plus
        // a partial unique index restricted to Active sessions, so two independently completed histories for
        // one document became representable; the previous key omitted KnownCount/UnknownCount/IgnoredCount
        // and CompletedAt, which are exactly the fields that distinguish two such histories, leaving the
        // ordering non-total and the emitted archive-local ids dependent on raw row enumeration order.
        var sortedReviewSessions = snapshot.ReviewSessions
            .OrderBy(rs => docIdMap.TryGetValue(rs.DocumentId, out var d) ? d : string.Empty, StringComparer.Ordinal)
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
        var reviewSessionIdMap = BuildIdMap(sortedReviewSessions, rs => rs.Id, "vr-");

        var sortedReviewCandidates = snapshot.ReviewCandidates
            .OrderBy(rc => reviewSessionIdMap.TryGetValue(rc.SessionId, out var s) ? s : string.Empty, StringComparer.Ordinal)
            .ThenBy(rc => rc.Order)
            .ToList();
        var reviewCandidateIdMap = BuildIdMap(sortedReviewCandidates, rc => rc.Id, "rc-");

        var sortedPrepSessions = snapshot.PreparationSessions
            .OrderBy(ps => ContentKey(ps.Method, ps.Status, ps.TotalItems, ps.CompletedItems, ps.StartedAtUtc.Ticks, ps.UpdatedAtUtc.Ticks))
            .ToList();
        var prepSessionIdMap = BuildIdMap(sortedPrepSessions, ps => ps.Id, "pb-");

        var sortedPrepCandidates = snapshot.PreparationCandidates
            .OrderBy(pc => prepSessionIdMap.TryGetValue(pc.SessionId, out var s) ? s : string.Empty, StringComparer.Ordinal)
            .ThenBy(pc => pc.Order)
            .ToList();
        var prepCandidateIdMap = BuildIdMap(sortedPrepCandidates, pc => pc.Id, "pi-");

        var sortedLearningSessions = snapshot.LearningSessions
            .OrderBy(ls => ContentKey(ls.Status, ls.TotalCards, ls.CompletedCards, ls.AgainCount, ls.HardCount, ls.GoodCount, ls.EasyCount, ls.StartedAtUtc.Ticks, ls.UpdatedAtUtc.Ticks))
            .ToList();
        var learningSessionIdMap = BuildIdMap(sortedLearningSessions, ls => ls.Id, "ls-");

        var sortedQueueItems = snapshot.LearningSessionCards
            .OrderBy(q => learningSessionIdMap.TryGetValue(q.SessionId, out var s) ? s : string.Empty, StringComparer.Ordinal)
            .ThenBy(q => q.QueueOrder)
            .ToList();
        var queueIdMap = BuildIdMap(sortedQueueItems, q => q.Id, "lq-");

        // ---- Build output collections ----
        var sourceMaterials = sortedDocs.Select(doc => MapSourceMaterial(doc, snapshot, docIdMap, sentenceIdMap, vocabIdMap)).ToList();
        var vocabulary = sortedWords.Select(w => MapVocabularyItem(w, snapshot, vocabIdMap)).ToList();
        var senses = sortedSenses.Select(s => MapSense(s, senseIdMap, vocabIdMap, meaningIdMap)).ToList();
        var preparedLearning = sortedMeanings.Select(m => MapPreparedItem(m, snapshot, meaningIdMap, vocabIdMap, senseIdMap, docIdMap)).ToList();
        var answerVariants = sortedVariants.Select(v => MapAnswerVariant(v, variantIdMap, senseIdMap, meaningIdMap)).ToList();
        var assignments = sortedAssignments.Select(a => MapAssignment(a, senseIdMap, variantIdMap)).ToList();
        var cards = sortedCards.Select(c => MapCard(c, cardIdMap, vocabIdMap, senseIdMap, meaningIdMap)).ToList();

        var sortedReviews = snapshot.LearningReviews
            .OrderBy(r => cardIdMap.TryGetValue(r.CardId, out var c) ? c : string.Empty, StringComparer.Ordinal)
            .ThenBy(r => r.ReviewedAtUtc.Ticks)
            .ThenBy(r => ContentKey(r.Rating, r.WasTypedAnswer, r.WasCorrect, r.DueAtUtc.Ticks, r.IntervalDays, r.EaseFactor))
            .Select(r => MapReview(r, cardIdMap, learningSessionIdMap, variantIdMap))
            .ToList();

        var progressOut = snapshot.AnswerVariantProgress
            .OrderBy(p => cardIdMap.TryGetValue(p.CardId, out var c) ? c : string.Empty, StringComparer.Ordinal)
            .ThenBy(p => variantIdMap.TryGetValue(p.AnswerVariantId, out var v) ? v : string.Empty, StringComparer.Ordinal)
            .Select(p => MapProgress(p, cardIdMap, variantIdMap))
            .ToList();

        var vocabReviews = sortedReviewSessions.Select(rs => MapVocabularyReviewWorkflow(rs, snapshot, reviewSessionIdMap, docIdMap, reviewCandidateIdMap, vocabIdMap)).ToList();
        var prepBatches = sortedPrepSessions.Select(ps => MapPreparationWorkflow(ps, snapshot, prepSessionIdMap, prepCandidateIdMap, vocabIdMap)).ToList();
        var learningSessionsOut = sortedLearningSessions.Select(ls => MapLearningWorkflow(ls, snapshot, learningSessionIdMap, queueIdMap, cardIdMap, variantIdMap)).ToList();

        return new BackupPayloadV2(
            sourceMaterials,
            vocabulary,
            senses,
            preparedLearning,
            answerVariants,
            assignments,
            progressOut,
            new BackupLearningDataV2(cards, sortedReviews),
            new BackupWorkflowDataV2(vocabReviews, prepBatches, learningSessionsOut),
            new BackupExtensions(new Dictionary<string, BackupExtensionPayload>(StringComparer.Ordinal)));
    }

    private static string SenseStableIdOf(int senseId, Schema8BackupSnapshot snapshot) =>
        snapshot.Senses.First(s => s.Id == senseId).StableId;

    private static Dictionary<int, string> BuildIdMap<T>(List<T> sorted, Func<T, int> idSelector, string prefix)
    {
        var map = new Dictionary<int, string>();
        for (var i = 0; i < sorted.Count; i++)
        {
            map[idSelector(sorted[i])] = $"{prefix}{(i + 1):D6}";
        }
        return map;
    }

    private static string ContentKey(params object?[] fields) =>
        string.Join("", fields.Select(f => f?.ToString() ?? string.Empty));

    private static DateTime EnsureUtc(DateTime dt) => dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    private static DateTime? EnsureUtc(DateTime? dt) => dt.HasValue ? EnsureUtc(dt.Value) : null;

    private static BackupSourceMaterial MapSourceMaterial(
        DocumentEntity doc, Schema8BackupSnapshot snapshot,
        Dictionary<int, string> docIdMap, Dictionary<int, string> sentenceIdMap, Dictionary<int, string> vocabIdMap)
    {
        var docSentences = snapshot.SentenceSpans.Where(s => s.DocumentId == doc.Id).OrderBy(s => s.Order)
            .Select(s => new BackupSentenceRange(
                sentenceIdMap.TryGetValue(s.Id, out var sId) ? sId : "ss-000000-missing", s.Order, s.StartPosition, s.Length))
            .ToList();

        var docOccurrences = snapshot.WordOccurrences.Where(o => o.DocumentId == doc.Id).OrderBy(o => o.Order)
            .Select(o => new BackupOccurrence(
                vocabIdMap.TryGetValue(o.WordId, out var vId) ? vId : "v-000000-missing",
                sentenceIdMap.TryGetValue(o.SentenceSpanId, out var sId) ? sId : "ss-000000-missing",
                o.StartPosition, o.Length, o.SurfaceForm, o.Order,
                BackupEnumMappings.ToBackup(o.TechnicalFamily), o.TechnicalInstanceYear,
                string.IsNullOrEmpty(o.TechnicalInstanceIdentifier) ? null : o.TechnicalInstanceIdentifier,
                string.IsNullOrEmpty(o.TechnicalVariant) ? null : o.TechnicalVariant))
            .ToList();

        return new BackupSourceMaterial(
            docIdMap.TryGetValue(doc.Id, out var dId) ? dId : "sm-000000-missing",
            doc.Title, doc.TextLanguage, doc.ExplanationLanguage, BackupEnumMappings.ToBackup(doc.LookupMode),
            string.IsNullOrEmpty(doc.TargetLanguage) ? null : doc.TargetLanguage, doc.Content,
            doc.ContentFingerprint.ToLowerInvariant(), EnsureUtc(doc.ImportedAt), doc.WordCount, docSentences, docOccurrences);
    }

    private static BackupVocabularyItem MapVocabularyItem(
        WordEntity word, Schema8BackupSnapshot snapshot, Dictionary<int, string> vocabIdMap)
    {
        var forms = snapshot.WordForms.Where(f => f.WordId == word.Id)
            .OrderBy(f => f.SurfaceForm, StringComparer.Ordinal)
            .Select(f => new BackupEncounteredForm(f.SurfaceForm, f.OccurrenceCount)).ToList();

        var legacySummaries = snapshot.ReviewStates.Where(r => r.WordId == word.Id)
            .OrderBy(r => ContentKey(r.ReviewCount, r.ForgotCount, r.PartialCount, r.KnownCount, r.LastReviewedAt?.Ticks ?? 0))
            .Select(r => new BackupLegacyReviewSummary(r.ReviewCount, r.ForgotCount, r.PartialCount, r.KnownCount, EnsureUtc(r.LastReviewedAt)))
            .ToList();

        return new BackupVocabularyItem(
            vocabIdMap.TryGetValue(word.Id, out var vId) ? vId : "v-000000-missing",
            word.Language, word.CanonicalTerm, word.NormalizedTerm, BackupEnumMappings.ToBackup(word.TokenKind),
            BackupEnumMappings.ToBackup(word.Status), BackupEnumMappings.ToBackup(word.PreparationState),
            word.TotalOccurrenceCount, word.DocumentCount, EnsureUtc(word.CreatedAt), EnsureUtc(word.UpdatedAt),
            forms,
            new BackupAutomaticLearningState(
                BackupEnumMappings.ToBackup(word.AutomaticInteractionMode), word.ConsecutiveRecallSuccessCount,
                word.ConsecutiveTypingSuccessCount, word.ConsecutiveTypingFailureCount, word.MasteryReviewExtensionScheduled),
            legacySummaries);
    }

    private static BackupSense MapSense(
        SenseRow sense, Dictionary<int, string> senseIdMap, Dictionary<int, string> vocabIdMap,
        Dictionary<int, string> meaningIdMap)
    {
        string? defaultMeaningId = sense.DefaultMeaningId.HasValue
            ? (meaningIdMap.TryGetValue(sense.DefaultMeaningId.Value, out var mId) ? mId : "m-000000-missing")
            : null;

        return new BackupSense(
            senseIdMap.TryGetValue(sense.Id, out var seId) ? seId : "se-000000-missing",
            sense.StableId,
            vocabIdMap.TryGetValue(sense.WordId, out var vId) ? vId : "v-000000-missing",
            sense.SourceLanguage, sense.ExplanationLanguage, sense.ProviderSenseId, sense.TopicOrDomain,
            sense.PartOfSpeech, sense.GrammaticalRelationship, sense.AcronymExpansion, defaultMeaningId,
            (BackupSenseStatus)(int)sense.Status, EnsureUtc(sense.CreatedAtUtc), EnsureUtc(sense.UpdatedAtUtc));
    }

    private static BackupPreparedItemV2 MapPreparedItem(
        Schema8MeaningRow meaning, Schema8BackupSnapshot snapshot, Dictionary<int, string> meaningIdMap,
        Dictionary<int, string> vocabIdMap, Dictionary<int, string> senseIdMap, Dictionary<int, string> docIdMap)
    {
        var contexts = snapshot.ContextSnapshots.Where(c => c.MeaningId == meaning.Id)
            .OrderBy(c => c.NormalizedFingerprint, StringComparer.Ordinal)
            .Select(c => new BackupContextSnapshotV2(
                docIdMap.TryGetValue(c.SourceDocumentId, out var dId) ? dId : "sm-000000-missing",
                c.SourceDocumentTitle, c.Text, c.TargetStart, c.TargetLength, c.NormalizedFingerprint,
                EnsureUtc(c.CreatedAtUtc),
                c.SenseId.HasValue && senseIdMap.TryGetValue(c.SenseId.Value, out var cSenseId) ? cSenseId : "se-000000-missing"))
            .ToList();

        var aliases = DeserializeAliases(meaning.AcceptedAliasesJson);

        return new BackupPreparedItemV2(
            meaningIdMap.TryGetValue(meaning.Id, out var mId) ? mId : "m-000000-missing",
            meaning.SenseId.HasValue && senseIdMap.TryGetValue(meaning.SenseId.Value, out var seId) ? seId : "se-000000-missing",
            meaning.StableId,
            vocabIdMap.TryGetValue(meaning.WordId, out var vId) ? vId : "v-000000-missing",
            meaning.SourceLanguage, meaning.ExplanationLanguage, meaning.DisplayTerm,
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
            aliases, meaning.ConfirmedByUser,
            new BackupSourceReference(meaning.Source, meaning.SourceProject, meaning.SourcePageTitle, meaning.SourceRevisionId, meaning.Attribution),
            EnsureUtc(meaning.CreatedAt), EnsureUtc(meaning.UpdatedAt), EnsureUtc(meaning.PreparedAt), contexts);
    }

    private static BackupAnswerVariant MapAnswerVariant(
        AnswerVariantRow variant, Dictionary<int, string> variantIdMap, Dictionary<int, string> senseIdMap, Dictionary<int, string> meaningIdMap)
    {
        return new BackupAnswerVariant(
            variantIdMap.TryGetValue(variant.Id, out var avId) ? avId : "av-000000-missing", variant.StableId,
            senseIdMap.TryGetValue(variant.SenseId, out var seId) ? seId : "se-000000-missing",
            variant.AnswerLanguage, variant.DisplayText, variant.NormalizedText,
            variant.SourceMeaningId.HasValue
                ? (meaningIdMap.TryGetValue(variant.SourceMeaningId.Value, out var mId) ? mId : "m-000000-missing")
                : null,
            EnsureUtc(variant.CreatedAtUtc), EnsureUtc(variant.UpdatedAtUtc));
    }

    private static BackupSenseAnswerVariantAssignment MapAssignment(
        SenseAnswerVariantAssignmentRow assignment, Dictionary<int, string> senseIdMap, Dictionary<int, string> variantIdMap)
    {
        return new BackupSenseAnswerVariantAssignment(
            assignment.StableId, assignment.StableId,
            senseIdMap.TryGetValue(assignment.SenseId, out var seId) ? seId : "se-000000-missing",
            BackupEnumMappings.ToBackup(assignment.CardDirection),
            variantIdMap.TryGetValue(assignment.AnswerVariantId, out var vId) ? vId : "av-000000-missing",
            (BackupAnswerVariantRequirement)(int)assignment.Requirement, assignment.IsPreferred,
            EnsureUtc(assignment.CreatedAtUtc), EnsureUtc(assignment.UpdatedAtUtc),
            EnsureUtc(assignment.RequiredSinceUtc));
    }

    private static BackupLearningCardV2 MapCard(
        Schema8CardRow card, Dictionary<int, string> cardIdMap, Dictionary<int, string> vocabIdMap,
        Dictionary<int, string> senseIdMap, Dictionary<int, string> meaningIdMap)
    {
        return new BackupLearningCardV2(
            cardIdMap.TryGetValue(card.Id, out var cId) ? cId : "c-000000-missing",
            vocabIdMap.TryGetValue(card.WordId, out var vId) ? vId : "v-000000-missing",
            card.SenseId.HasValue && senseIdMap.TryGetValue(card.SenseId.Value, out var seId) ? seId : "se-000000-missing",
            meaningIdMap.TryGetValue(card.PreferredMeaningId, out var mId) ? mId : "m-000000-missing",
            BackupEnumMappings.ToBackup(card.Direction), BackupEnumMappings.ToBackup(card.State), EnsureUtc(card.DueAtUtc),
            card.IntervalDays, card.EaseFactor, card.SuccessfulReviewCount, card.LapseCount,
            EnsureUtc(card.LastReviewedAtUtc), card.LastRating.HasValue ? BackupEnumMappings.ToBackup(card.LastRating.Value) : null,
            EnsureUtc(card.CreatedAtUtc), EnsureUtc(card.UpdatedAtUtc));
    }

    private static BackupLearningReviewV2 MapReview(
        Schema8ReviewRow review, Dictionary<int, string> cardIdMap, Dictionary<int, string> sessionIdMap, Dictionary<int, string> variantIdMap)
    {
        return new BackupLearningReviewV2(
            cardIdMap.TryGetValue(review.CardId, out var cId) ? cId : "c-000000-missing",
            sessionIdMap.TryGetValue(review.SessionId, out var sId) ? sId : "ls-000000-missing",
            BackupEnumMappings.ToBackup(review.Rating), review.WasTypedAnswer, review.WasCorrect,
            EnsureUtc(review.ReviewedAtUtc), EnsureUtc(review.DueAtUtc), review.IntervalDays, review.EaseFactor,
            review.TargetAnswerVariantId.HasValue
                ? (variantIdMap.TryGetValue(review.TargetAnswerVariantId.Value, out var tId) ? tId : "av-000000-missing")
                : null,
            review.MatchedAnswerVariantId.HasValue
                ? (variantIdMap.TryGetValue(review.MatchedAnswerVariantId.Value, out var mId) ? mId : "av-000000-missing")
                : null);
    }

    private static BackupAnswerVariantProgress MapProgress(
        AnswerVariantProgressRow progress, Dictionary<int, string> cardIdMap, Dictionary<int, string> variantIdMap)
    {
        return new BackupAnswerVariantProgress(
            cardIdMap.TryGetValue(progress.CardId, out var cId) ? cId : "c-000000-missing",
            variantIdMap.TryGetValue(progress.AnswerVariantId, out var vId) ? vId : "av-000000-missing",
            BackupEnumMappings.ToBackup(progress.InteractionMode), progress.ConsecutiveReadingSuccessCount,
            progress.ConsecutiveTypingSuccessCount, progress.ConsecutiveTypingFailureCount,
            EnsureUtc(progress.LastAssessedAtUtc), progress.MasteryReviewExtensionScheduled, progress.IsMastered,
            progress.ReplayVersion, EnsureUtc(progress.CreatedAtUtc), EnsureUtc(progress.UpdatedAtUtc));
    }

    private static BackupVocabularyReviewWorkflow MapVocabularyReviewWorkflow(
        ReviewSessionEntity session, Schema8BackupSnapshot snapshot, Dictionary<int, string> reviewSessionIdMap,
        Dictionary<int, string> docIdMap, Dictionary<int, string> reviewCandidateIdMap, Dictionary<int, string> vocabIdMap)
    {
        var items = snapshot.ReviewCandidates.Where(i => i.SessionId == session.Id).OrderBy(i => i.Order)
            .Select(i => new BackupVocabularyReviewItem(
                reviewCandidateIdMap.TryGetValue(i.Id, out var rcId) ? rcId : "rc-000000-missing",
                vocabIdMap.TryGetValue(i.WordId, out var vId) ? vId : "v-000000-missing",
                i.Order, BackupEnumMappings.ToBackup(i.Status), BackupEnumMappings.ToBackup(i.PreviousWordStatus),
                i.PreviousTotalOccurrenceCount, i.PreviousDocumentCount, EnsureUtc(i.PreviousUpdatedAt),
                i.DecisionSequence, i.WasWordCreatedForSession, EnsureUtc(i.DecidedAt)))
            .ToList();

        return new BackupVocabularyReviewWorkflow(
            reviewSessionIdMap.TryGetValue(session.Id, out var rsId) ? rsId : "vr-000000-missing",
            docIdMap.TryGetValue(session.DocumentId, out var dId) ? dId : "sm-000000-missing",
            BackupEnumMappings.ToBackup(session.Status), session.TotalCandidates, session.ReviewedCount,
            session.KnownCount, session.UnknownCount, session.IgnoredCount, session.DecisionSequence,
            EnsureUtc(session.StartedAt), EnsureUtc(session.CompletedAt), items);
    }

    private static BackupPreparationWorkflow MapPreparationWorkflow(
        PreparationSessionEntity session, Schema8BackupSnapshot snapshot,
        Dictionary<int, string> prepSessionIdMap, Dictionary<int, string> prepCandidateIdMap, Dictionary<int, string> vocabIdMap)
    {
        var items = snapshot.PreparationCandidates.Where(i => i.SessionId == session.Id).OrderBy(i => i.Order)
            .Select(i => new BackupPreparationItem(
                prepCandidateIdMap.TryGetValue(i.Id, out var piId) ? piId : "pi-000000-missing",
                vocabIdMap.TryGetValue(i.WordId, out var vId) ? vId : "v-000000-missing",
                i.Order, BackupEnumMappings.ToBackup(i.Status), i.SelectedMeaningIndex,
                string.IsNullOrEmpty(i.LastErrorCode) ? null : i.LastErrorCode, i.LookupAttemptCount,
                EnsureUtc(i.UpdatedAtUtc),
                string.IsNullOrEmpty(i.ResultJson) ? null : BackupModelMapper.ParseLookupDraft(i.ResultJson)))
            .ToList();

        return new BackupPreparationWorkflow(
            prepSessionIdMap.TryGetValue(session.Id, out var psId) ? psId : "pb-000000-missing",
            BackupEnumMappings.ToBackup(session.Status), BackupEnumMappings.ToBackup(session.Method),
            session.TotalItems, session.CompletedItems, EnsureUtc(session.StartedAtUtc),
            EnsureUtc(session.UpdatedAtUtc), EnsureUtc(session.CompletedAtUtc), items);
    }

    private static BackupLearningWorkflowV2 MapLearningWorkflow(
        LearningSessionEntity session, Schema8BackupSnapshot snapshot,
        Dictionary<int, string> learningSessionIdMap, Dictionary<int, string> queueIdMap,
        Dictionary<int, string> cardIdMap, Dictionary<int, string> variantIdMap)
    {
        var items = snapshot.LearningSessionCards.Where(c => c.SessionId == session.Id).OrderBy(c => c.QueueOrder)
            .Select(c => new BackupLearningQueueItemV2(
                queueIdMap.TryGetValue(c.Id, out var lqId) ? lqId : "lq-000000-missing",
                cardIdMap.TryGetValue(c.CardId, out var cardId) ? cardId : "c-000000-missing",
                c.QueueOrder, c.IsDueCard, c.IsAgainRepeat, c.AnswerRevealed, c.SpellingChecked, c.SpellingCorrect,
                c.IsCompleted, c.Rating.HasValue ? BackupEnumMappings.ToBackup(c.Rating.Value) : null,
                EnsureUtc(c.CompletedAtUtc),
                c.TargetAnswerVariantId.HasValue
                    ? (variantIdMap.TryGetValue(c.TargetAnswerVariantId.Value, out var tId) ? tId : "av-000000-missing")
                    : null))
            .ToList();

        return new BackupLearningWorkflowV2(
            learningSessionIdMap.TryGetValue(session.Id, out var lsId) ? lsId : "ls-000000-missing",
            BackupEnumMappings.ToBackup(session.Status), session.TotalCards, session.CompletedCards,
            session.AgainCount, session.HardCount, session.GoodCount, session.EasyCount,
            EnsureUtc(session.StartedAtUtc), EnsureUtc(session.UpdatedAtUtc), EnsureUtc(session.CompletedAtUtc), items);
    }

    private static IReadOnlyList<string> DeserializeAliases(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var result = new List<string>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.GetString() is { } str)
                {
                    result.Add(str);
                }
            }
            return result;
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
