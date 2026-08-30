using KnownFirst.Data.Entities;
using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Services.Study;

internal static class DocumentCleanupOperations
{
    public static int CleanupEligibleDocuments(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // German Enhanced Term Recognition Package 5A: TextReviewService.CompleteSession retains a
        // derived component's DerivedTermEvidenceEntity (and its owning ReviewCandidateEntity) past
        // review completion while the word stays Unknown, even though the document may otherwise have
        // zero WordOccurrenceEntity rows. DerivedTermEvidenceEntries is a Schema-11-only table
        // (Data/Migrations/Schema11/Schema11Ddl.cs) — every other query in this method is valid at every
        // schema level 7+ — so this capability check runs once, up front, instead of letting a raw query
        // against a possibly-missing table throw mid-sweep. Both the owning document (below) and the exact
        // sentence span the evidence points at (the final loop) must survive, or Schema11EvidenceValidator
        // fails closed on the next database open.
        var (derivedEvidenceOwningCandidateIds, protectedSentenceSpanIds) =
            ResolveRetainedDerivedEvidenceProtection(connection);

        var deletedCount = 0;
        foreach (var document in connection.Table<DocumentEntity>().ToList())
        {
            var hasActiveReview = connection.Table<ReviewSessionEntity>()
                .Any(session => session.DocumentId == document.Id
                    && session.Status == ReviewSessionStatus.Active);
            var hasOccurrences = connection.Table<WordOccurrenceEntity>()
                .Any(occurrence => occurrence.DocumentId == document.Id);
            var hasActiveSnapshots = connection.Table<ContextSnapshotEntity>()
                .Where(snapshot => snapshot.SourceDocumentId == document.Id)
                .ToList()
                .Any(snapshot =>
                {
                    var word = connection.Find<WordEntity>(snapshot.WordId);
                    return word is not null && word.Status != WordStatus.Known;
                });
            var hasRetainedDerivedEvidence = derivedEvidenceOwningCandidateIds.Count > 0
                && HasRetainedDerivedEvidence(connection, document.Id, derivedEvidenceOwningCandidateIds);
            if (hasActiveReview || hasOccurrences || hasActiveSnapshots || hasRetainedDerivedEvidence)
            {
                continue;
            }

            connection.Execute("DELETE FROM ContextSnapshots WHERE SourceDocumentId = ?", document.Id);
            connection.Execute("DELETE FROM SentenceSpans WHERE DocumentId = ?", document.Id);
            foreach (var session in connection.Table<ReviewSessionEntity>()
                         .Where(item => item.DocumentId == document.Id)
                         .ToList())
            {
                connection.Execute("DELETE FROM ReviewCandidates WHERE SessionId = ?", session.Id);
                connection.Delete(session);
            }

            connection.Delete(document);
            deletedCount++;
        }

        foreach (var sentence in connection.Table<SentenceSpanEntity>().ToList())
        {
            if (protectedSentenceSpanIds.Contains(sentence.Id))
            {
                continue;
            }

            if (!connection.Table<WordOccurrenceEntity>()
                .Any(occurrence => occurrence.SentenceSpanId == sentence.Id))
            {
                connection.Delete(sentence);
            }
        }

        return deletedCount;
    }

    /// <summary>
    /// True when a surviving <see cref="DerivedTermEvidenceEntity"/> row is owned — via
    /// <see cref="ReviewCandidateEntity"/>'s <c>SessionId</c> → <see cref="ReviewSessionEntity.DocumentId"/> —
    /// by exactly this document. Scoped through the real foreign-key relationships (never bare sentence
    /// order or any other document-independent signal), so it can never protect an unrelated document.
    /// </summary>
    private static bool HasRetainedDerivedEvidence(
        SQLiteConnection connection, int documentId, IReadOnlySet<int> derivedEvidenceOwningCandidateIds)
    {
        var sessionIds = connection.Table<ReviewSessionEntity>()
            .Where(session => session.DocumentId == documentId)
            .ToList()
            .Select(session => session.Id)
            .ToHashSet();
        if (sessionIds.Count == 0)
        {
            return false;
        }

        return connection.Table<ReviewCandidateEntity>()
            .ToList()
            .Any(candidate => sessionIds.Contains(candidate.SessionId)
                && derivedEvidenceOwningCandidateIds.Contains(candidate.Id));
    }

    /// <summary>
    /// Resolves, once per sweep: the set of <see cref="ReviewCandidateEntity"/> ids that own a surviving
    /// <see cref="DerivedTermEvidenceEntity"/> row (used to protect their owning document), and the exact
    /// globally-unique <see cref="SentenceSpanEntity"/> ids those evidence rows point at (used to protect
    /// the final unreferenced-sentence sweep below). Resolved through the real
    /// candidate → session → document/(document, sentence order) relationships — never bare sentence order
    /// alone — so two documents can never collide on the same protected span. Returns empty sets on any
    /// schema below 11, where <c>DerivedTermEvidenceEntries</c> does not exist.
    /// </summary>
    private static (IReadOnlySet<int> OwningCandidateIds, IReadOnlySet<int> ProtectedSentenceSpanIds)
        ResolveRetainedDerivedEvidenceProtection(SQLiteConnection connection)
    {
        if (PreparationSchemaCapability.Resolve(connection) is not (PreparationSchema11CapabilityResult or PreparationSchema12CapabilityResult or PreparationSchema13CapabilityResult))
        {
            return (new HashSet<int>(), new HashSet<int>());
        }

        var evidenceRows = connection.Table<DerivedTermEvidenceEntity>().ToList();
        if (evidenceRows.Count == 0)
        {
            return (new HashSet<int>(), new HashSet<int>());
        }

        var owningCandidateIds = evidenceRows.Select(evidence => evidence.ReviewCandidateId).ToHashSet();
        var candidatesById = connection.Table<ReviewCandidateEntity>().ToList()
            .Where(candidate => owningCandidateIds.Contains(candidate.Id))
            .ToDictionary(candidate => candidate.Id);
        var sessionsById = connection.Table<ReviewSessionEntity>().ToList()
            .Where(session => candidatesById.Values.Any(candidate => candidate.SessionId == session.Id))
            .ToDictionary(session => session.Id);
        var sentenceIdByDocumentAndOrder = connection.Table<SentenceSpanEntity>().ToList()
            .ToDictionary(sentence => (sentence.DocumentId, sentence.Order), sentence => sentence.Id);

        var protectedSentenceSpanIds = new HashSet<int>();
        foreach (var evidence in evidenceRows)
        {
            if (!candidatesById.TryGetValue(evidence.ReviewCandidateId, out var candidate)
                || !sessionsById.TryGetValue(candidate.SessionId, out var session))
            {
                continue;
            }

            if (sentenceIdByDocumentAndOrder.TryGetValue(
                    (session.DocumentId, evidence.SourceSentenceOrder), out var sentenceSpanId))
            {
                protectedSentenceSpanIds.Add(sentenceSpanId);
            }
        }

        return (owningCandidateIds, protectedSentenceSpanIds);
    }
}
