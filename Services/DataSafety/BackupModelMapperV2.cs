using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema8;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

/// <summary>
/// Maps a captured <see cref="Schema8BackupSnapshot"/> into the external/archive <see cref="BackupPayloadV2"/>
/// model (KF-MEANING-001 Slice 2). Mirrors <see cref="BackupModelMapper"/>'s deterministic-ordering
/// pattern, but corrected: no output collection's emitted content is ordered by a local SQLite numeric id
/// anywhere in the graph — every collection uses its own StableId, canonical parent identity, content
/// fingerprint, or already-required-unique semantic key instead. Local numeric ids are used only to build
/// the in-memory FK-resolution dictionaries during mapping.
///
/// <para><b>Cross-installation canonical output (KF-BACKUP-002 Package C).</b> The emitted payload is a
/// pure function of the exported logical content: two installations holding the same content produce the
/// same archive-local ids and the same collection order even when their SQLite row ids differ. Package C
/// closed the two remaining gaps — <c>SourceMaterials</c>, whose <c>(ContentFingerprint, Title)</c> key was
/// not total over valid distinct documents and whose scalar comparisons alone could not tell apart two
/// documents differing only in their emitted <c>Sentences</c>/<c>Occurrences</c>, and <c>ReviewSessions</c>,
/// whose ordering fell through to the local row id for two completed histories that tie on every
/// session-level field and differ only through candidate content. <c>SourceMaterials</c> now ends with a
/// content-derived key over the emitted child subgraph and needs no local-id fallback at all;
/// <c>ReviewSessions</c> continues with the full Schema-9 completed-review identity
/// (<see cref="Merge.ReviewWorkflowIdentityPolicy"/>'s <c>TryComputeSessionIdentityV2</c>, reached through
/// the shared <see cref="Merge.Schema9ReviewSessionRowIdentities"/> plumbing that
/// <see cref="Merge.MergeWriterTargetIndex"/> also uses) and then with a content key over the emitted
/// candidate rows. A final row-id comparison remains only as a syntactic total-order guarantee; see
/// <c>ReviewSessionOrderingKey</c> for the proof that reaching it cannot change the emitted payload.</para>
///
/// <para>This mapper is not a validation stage: a snapshot whose references do not resolve, or whose
/// completed-review candidates repeat one vocabulary identity, is still mapped without throwing, exactly as
/// before. Such payloads are rejected downstream by <see cref="BackupArchiveWriterV2"/>, the merge planner,
/// and <see cref="Merge.MergeWriterTargetIndex"/>, which keep their own fail-closed contracts.</para>
/// </summary>
public static class BackupModelMapperV2
{
    public static BackupPayloadV2 MapToExternal(Schema8BackupSnapshot snapshot)
    {
        // ---- Vocabulary (Words): ordered by (Language, IdentityKey) — required-unique ----
        //
        // Sorted before SourceMaterials because the source-material child-subgraph ordering key below encodes
        // each occurrence's emitted vocabulary reference, which this map produces.
        var sortedWords = snapshot.Words
            .OrderBy(w => w.Language.ToLowerInvariant(), StringComparer.Ordinal)
            .ThenBy(w => w.NormalizedTerm.ToLowerInvariant(), StringComparer.Ordinal)
            .ToList();
        var vocabIdMap = BuildIdMap(sortedWords, w => w.Id, "v-");

        // ---- SourceMaterials (Documents): ordered by every retained exported field, never a local id ----
        //
        // Title is deliberately excluded from the stable document identity (free text typed at import time)
        // and the archive writer enforces no semantic uniqueness for source materials, so byte-identical
        // content is a valid distinct document under a different TextLanguage (the live duplicate check is
        // (ContentFingerprint, TextLanguage)) or, after a merge, under a different LookupMode/TargetLanguage.
        // (ContentFingerprint, Title) alone therefore left the ordering non-total, and the positional sm-*
        // ids — plus every ss-*/vr-*/rc-* id derived from them — fell back to raw row enumeration order.
        //
        // The nine scalar comparisons mirror the long-standing v1 BackupModelMapper precedent, but they cover
        // only the scalar half of the emitted DTO: MapSourceMaterial also emits Sentences and Occurrences,
        // selected through the local DocumentId foreign key. Two documents equal on all nine scalars can
        // therefore still emit different child subgraphs, so the ordering ends with a content-derived key over
        // exactly that child content (see BuildSourceMaterialChildOrderingKeys). With it the ordering is total
        // over everything the DTO emits and needs no local-id fallback.
        var sourceMaterialChildKeys = BuildSourceMaterialChildOrderingKeys(snapshot, vocabIdMap);

        var sortedDocs = snapshot.Documents
            .OrderBy(d => d.ContentFingerprint, StringComparer.Ordinal)
            .ThenBy(d => d.Title, StringComparer.Ordinal)
            .ThenBy(d => d.TextLanguage, StringComparer.Ordinal)
            .ThenBy(d => d.ExplanationLanguage, StringComparer.Ordinal)
            .ThenBy(d => (int)d.LookupMode)
            .ThenBy(d => d.TargetLanguage ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(d => d.Content, StringComparer.Ordinal)
            .ThenBy(d => d.WordCount)
            .ThenBy(d => EnsureUtc(d.ImportedAt).Ticks)
            .ThenBy(d => sourceMaterialChildKeys[d.Id], StringComparer.Ordinal)
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
        // than a delimiter-free ContentKey. Schema 9 replaced the legacy unique ReviewSessions(DocumentId)
        // index with a non-unique index plus a partial unique index restricted to Active sessions, so two
        // independently completed histories for one document became representable. Package B added the
        // session-level fields that distinguish two such histories; Package C closes the remaining gap:
        // sessions that tie on every session-level field can still differ through their candidate rows, so
        // the ordering continues with the full Schema-9 completed-review identity and then with a content
        // key over the emitted candidate rows. See ReviewSessionOrderingKeys below for both components and
        // for why the final row-id comparison can no longer influence the emitted payload.
        var reviewSessionOrderingKeys = BuildReviewSessionOrderingKeys(snapshot, vocabIdMap);

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
            // HasValue precedes Ticks so an absent CompletedAt and a present one at tick 0 — which emit
            // different payload values — can never compare equal and fall through to the row id below.
            .ThenBy(rs => rs.CompletedAt.HasValue)
            .ThenBy(rs => rs.CompletedAt.HasValue ? EnsureUtc(rs.CompletedAt.Value).Ticks : 0)
            .ThenBy(rs => reviewSessionOrderingKeys[rs.Id].IdentityKey, StringComparer.Ordinal)
            .ThenBy(rs => reviewSessionOrderingKeys[rs.Id].CandidateContentKey, StringComparer.Ordinal)
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

    /// <summary>
    /// Ordering material only — never an identity. Its own domain keeps it in a separate hash family from
    /// every merge identity, because it encodes archive-emission concerns (absolute child <c>Order</c>
    /// values, the emitted vocabulary reference) that no merge identity encodes.
    /// </summary>
    private const string SourceMaterialChildOrderingDomain =
        "KnownFirst.Archive.SourceMaterial.ChildGraphOrdering.v1";

    /// <summary>
    /// A deterministic, content-derived key over one document's complete emitted child subgraph — the exact
    /// content <see cref="MapSourceMaterial"/> puts into <c>Sentences</c> and <c>Occurrences</c>, which the
    /// scalar Document comparisons cannot see.
    ///
    /// <para><b>Sentences</b> contribute <c>Order</c>, <c>Start</c> and <c>Length</c>. The emitted
    /// <c>BackupSentenceRange.Id</c> is <c>ss-{owning sm-id}-{Order:D6}</c>, so it is fully determined by the
    /// position being decided plus <c>Order</c> — encoding <c>Order</c> covers it without referencing a row
    /// id.</para>
    ///
    /// <para><b>Occurrences</b> contribute every emitted field: the vocabulary reference as the emitted
    /// <c>v-*</c> value (never the local <c>WordId</c>); the sentence reference as the referenced sentence's
    /// <c>Order</c> within this same document (never the local <c>SentenceSpanId</c>, and null when the
    /// reference does not resolve within this document, which is exactly when the mapper emits the collapsed
    /// <c>ss-000000-missing</c> literal); <c>Start</c>; <c>Length</c>; <c>SurfaceForm</c>; the absolute
    /// <c>Order</c>; the symbolic technical-token family; and the three optional technical fields under the
    /// same empty-to-null semantics the DTO itself uses. These DTOs carry no timestamps, so no UTC
    /// normalization applies.</para>
    ///
    /// <para>Both collections are ordered inside the key by <c>Order</c> — which the emitted arrays also sort
    /// by, and which <c>IX_SentenceSpans_Document_Order</c> makes unique per document for sentences and the
    /// archive graph validator makes unique per document for occurrences — with the remaining encoded content
    /// as a further tie-break, so the key itself never depends on row enumeration order even for a snapshot
    /// that violates those invariants. Encoding is the length-prefixed, domain-discriminated
    /// <see cref="Merge.CanonicalFingerprintBuilder"/> form, never a delimiter-joined <c>ToString()</c>
    /// concatenation.</para>
    /// </summary>
    private static Dictionary<int, string> BuildSourceMaterialChildOrderingKeys(
        Schema8BackupSnapshot snapshot, Dictionary<int, string> vocabIdMap)
    {
        var sentencesByDocumentRowId = new Dictionary<int, List<SentenceSpanEntity>>();
        foreach (var sentence in snapshot.SentenceSpans)
        {
            if (!sentencesByDocumentRowId.TryGetValue(sentence.DocumentId, out var documentSentences))
            {
                documentSentences = [];
                sentencesByDocumentRowId[sentence.DocumentId] = documentSentences;
            }

            documentSentences.Add(sentence);
        }

        var occurrencesByDocumentRowId = new Dictionary<int, List<WordOccurrenceEntity>>();
        foreach (var occurrence in snapshot.WordOccurrences)
        {
            if (!occurrencesByDocumentRowId.TryGetValue(occurrence.DocumentId, out var documentOccurrences))
            {
                documentOccurrences = [];
                occurrencesByDocumentRowId[occurrence.DocumentId] = documentOccurrences;
            }

            documentOccurrences.Add(occurrence);
        }

        var keys = new Dictionary<int, string>();
        foreach (var document in snapshot.Documents)
        {
            var documentSentences = sentencesByDocumentRowId.TryGetValue(document.Id, out var foundSentences)
                ? foundSentences
                : [];
            var documentOccurrences = occurrencesByDocumentRowId.TryGetValue(document.Id, out var foundOccurrences)
                ? foundOccurrences
                : [];

            // Resolves an occurrence's sentence reference to that sentence's own Order within this document —
            // the only part of the emitted ss-* value that is not already the document's own position.
            var sentenceOrderByRowId = new Dictionary<int, int>();
            foreach (var sentence in documentSentences)
            {
                sentenceOrderByRowId[sentence.Id] = sentence.Order;
            }

            var builder = new Merge.CanonicalFingerprintBuilder(SourceMaterialChildOrderingDomain);

            var orderedSentences = documentSentences
                .OrderBy(sentence => sentence.Order)
                .ThenBy(sentence => sentence.StartPosition)
                .ThenBy(sentence => sentence.Length)
                .ToList();
            builder.WriteInt32(orderedSentences.Count);
            foreach (var sentence in orderedSentences)
            {
                builder
                    .WriteInt32(sentence.Order)
                    .WriteInt32(sentence.StartPosition)
                    .WriteInt32(sentence.Length);
            }

            var orderedOccurrences = documentOccurrences
                .Select(occurrence => (
                    Occurrence: occurrence,
                    VocabularyKey: vocabIdMap.TryGetValue(occurrence.WordId, out var vocabularyId)
                        ? vocabularyId
                        : "v-000000-missing",
                    SentenceOrder: sentenceOrderByRowId.TryGetValue(occurrence.SentenceSpanId, out var sentenceOrder)
                        ? sentenceOrder
                        : (int?)null))
                .OrderBy(entry => entry.Occurrence.Order)
                .ThenBy(entry => entry.VocabularyKey, StringComparer.Ordinal)
                .ThenBy(entry => entry.SentenceOrder ?? int.MinValue)
                .ThenBy(entry => entry.Occurrence.StartPosition)
                .ThenBy(entry => entry.Occurrence.Length)
                .ThenBy(entry => entry.Occurrence.SurfaceForm, StringComparer.Ordinal)
                .ToList();
            builder.WriteInt32(orderedOccurrences.Count);
            foreach (var (occurrence, vocabularyKey, sentenceOrder) in orderedOccurrences)
            {
                builder
                    .WriteString(vocabularyKey)
                    .WriteNullableInt32(sentenceOrder)
                    .WriteInt32(occurrence.Order)
                    .WriteInt32(occurrence.StartPosition)
                    .WriteInt32(occurrence.Length)
                    .WriteString(occurrence.SurfaceForm)
                    .WriteEnum(BackupEnumMappings.ToBackup(occurrence.TechnicalFamily))
                    .WriteNullableInt32(occurrence.TechnicalInstanceYear)
                    .WriteNullableString(
                        string.IsNullOrEmpty(occurrence.TechnicalInstanceIdentifier) ? null : occurrence.TechnicalInstanceIdentifier)
                    .WriteNullableString(
                        string.IsNullOrEmpty(occurrence.TechnicalVariant) ? null : occurrence.TechnicalVariant);
            }

            keys[document.Id] = builder.ComputeSha256Hex();
        }

        return keys;
    }

    /// <summary>
    /// The two content-derived components that continue the <c>ReviewSessions</c> ordering once every
    /// session-level field has compared equal.
    ///
    /// <para><see cref="IdentityKey"/> is the full Schema-9 completed-review identity — the same
    /// <see cref="Merge.ReviewWorkflowIdentityPolicy"/> <c>TryComputeSessionIdentityV2</c> computation the
    /// merge planner and <see cref="Merge.MergeWriterTargetIndex"/> use, reached here through the shared
    /// <see cref="Merge.Schema9ReviewSessionRowIdentities"/> plumbing so no second definition of
    /// completed-review identity exists. It is prefixed so it can never be confused with the fallback form
    /// below.</para>
    ///
    /// <para><see cref="CandidateContentKey"/> covers what the identity deliberately omits: the identity
    /// treats candidate <c>Order</c> as positional and encodes only the relative sequence, while the archive
    /// emits the absolute values. Two sessions sharing one identity can therefore still emit different item
    /// rows, so this key is compared next.</para>
    ///
    /// <para><b>Why the final row-id comparison is output-neutral.</b> Reaching it requires equality on the
    /// parent source-material id, Status, all five retained counters, DecisionSequence, StartedAt, the
    /// presence and value of CompletedAt, and this candidate content key. Those are exactly the inputs to
    /// every field <see cref="MapVocabularyReviewWorkflow"/> emits — the workflow's own scalars and, through
    /// the content key, each emitted item's vocabulary id, Order, and full decision content, in the emitted
    /// order (items are emitted ordered by <c>Order</c>, which <c>IX_ReviewCandidates_Session_Order</c>
    /// makes unique within a session). Two sessions that tie this far therefore emit byte-identical content,
    /// so whichever the row id happens to place first, the resulting payload is the same.</para>
    /// </summary>
    private readonly record struct ReviewSessionOrderingKey(string IdentityKey, string CandidateContentKey);

    private const string ReviewSessionIdentityOrderingPrefix = "v2:";
    private const string ReviewSessionContentOrderingPrefix = "cd:";

    /// <summary>
    /// Computes both ordering components for every review session in the snapshot.
    ///
    /// <para>The identity component falls back to the content key — prefixed distinctly, never a shared
    /// sentinel — for the two snapshot shapes whose identity is undefined: a dangling document or vocabulary
    /// reference, and a session whose candidates repeat one vocabulary identity. Both are shapes the archive
    /// writer rejects (<c>missing-reference</c> / the planner's and writer's <c>duplicate-id</c>), and this
    /// mapper deliberately does not become an additional, earlier throw site for them; the fallback keeps the
    /// ordering deterministic and content-derived instead, so two malformed sessions with different candidate
    /// content still receive different keys rather than colliding onto the local row id.</para>
    /// </summary>
    private static Dictionary<int, ReviewSessionOrderingKey> BuildReviewSessionOrderingKeys(
        Schema8BackupSnapshot snapshot, Dictionary<int, string> vocabIdMap)
    {
        var documentIdentityByRowId = new Dictionary<int, Merge.SourceMaterialIdentity>();
        foreach (var document in snapshot.Documents)
        {
            documentIdentityByRowId[document.Id] = Merge.Schema9ReviewSessionRowIdentities.ComputeDocumentIdentity(document);
        }

        var vocabularyIdentityByRowId = new Dictionary<int, Merge.VocabularyIdentity>();
        foreach (var word in snapshot.Words)
        {
            vocabularyIdentityByRowId[word.Id] = Merge.Schema9ReviewSessionRowIdentities.ComputeVocabularyIdentity(word);
        }

        var candidatesBySessionRowId = new Dictionary<int, List<ReviewCandidateEntity>>();
        foreach (var candidate in snapshot.ReviewCandidates)
        {
            if (!candidatesBySessionRowId.TryGetValue(candidate.SessionId, out var sessionCandidates))
            {
                sessionCandidates = [];
                candidatesBySessionRowId[candidate.SessionId] = sessionCandidates;
            }

            sessionCandidates.Add(candidate);
        }

        var orderingKeys = new Dictionary<int, ReviewSessionOrderingKey>();
        foreach (var session in snapshot.ReviewSessions)
        {
            var sessionCandidates = candidatesBySessionRowId.TryGetValue(session.Id, out var found)
                ? found
                : [];

            // Built from the same vocabulary ids this mapper will actually emit, so the key corresponds
            // exactly to the emitted item content.
            var candidateContentKey = Merge.Schema9ReviewSessionRowIdentities.ComputeCandidateContentOrderingKey(
                sessionCandidates.Select(candidate => (
                    candidate,
                    vocabIdMap.TryGetValue(candidate.WordId, out var vocabularyId) ? vocabularyId : "v-000000-missing")));

            orderingKeys[session.Id] = new ReviewSessionOrderingKey(
                ComputeReviewSessionIdentityKey(
                    session, sessionCandidates, documentIdentityByRowId, vocabularyIdentityByRowId, candidateContentKey),
                candidateContentKey);
        }

        return orderingKeys;
    }

    private static string ComputeReviewSessionIdentityKey(
        ReviewSessionEntity session,
        List<ReviewCandidateEntity> sessionCandidates,
        Dictionary<int, Merge.SourceMaterialIdentity> documentIdentityByRowId,
        Dictionary<int, Merge.VocabularyIdentity> vocabularyIdentityByRowId,
        string candidateContentKey)
    {
        if (!documentIdentityByRowId.TryGetValue(session.DocumentId, out var documentIdentity))
        {
            return ReviewSessionContentOrderingPrefix + candidateContentKey;
        }

        var candidateContent = new List<Merge.ReviewSessionCandidateContent>(sessionCandidates.Count);
        foreach (var candidate in sessionCandidates)
        {
            if (!vocabularyIdentityByRowId.TryGetValue(candidate.WordId, out var vocabularyIdentity))
            {
                return ReviewSessionContentOrderingPrefix + candidateContentKey;
            }

            candidateContent.Add(
                Merge.Schema9ReviewSessionRowIdentities.BuildCandidateContent(candidate, vocabularyIdentity));
        }

        var result = Merge.Schema9ReviewSessionRowIdentities.ComputeSessionIdentity(
            session, documentIdentity, candidateContent);

        return result.HasDuplicateCandidateVocabularyIdentity
            ? ReviewSessionContentOrderingPrefix + candidateContentKey
            : ReviewSessionIdentityOrderingPrefix + result.Identity.Value;
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
