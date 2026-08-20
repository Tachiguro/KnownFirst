using KnownFirst.Core.Preparation;
using KnownFirst.Data.Entities;
using SQLite;

namespace KnownFirst.Services.Study;

/// <summary>
/// Schema-8 evidence scanner (KF-MEANING-001 Slice 3): walks every valid <see cref="WordOccurrenceEntity"/>
/// for a Word in deterministic (DocumentId, Order) order — never capped at the first three, never
/// short-circuited because early occurrences are already processed — and classifies each occurrence's
/// four-field <see cref="ContextEvidenceKey"/> against the word's effective-processed-evidence ledger
/// (<see cref="Schema8EvidenceLedger"/>). Shares <see cref="PreparationContextEvidencePolicy"/> with the
/// unmodified Schema-7 context path (<see cref="PreparationService"/>) and reuses
/// <see cref="PreparationService.TryCreateContext"/>/<see cref="PreparationService.ContextData"/> for the
/// underlying document/sentence-bounds validation instead of a second, independently-maintained copy.
/// </summary>
internal static class Schema8EvidenceScanner
{
    /// <summary>
    /// Every valid context for the Word, in (DocumentId, Order) order — no cap, no dedup. Falls back to
    /// <see cref="EnumerateDerivedEvidenceContexts"/> when the Word has no occurrences at all (a derived
    /// component intentionally never receives one). Callers that must apply the occurrence-only
    /// surface-form attribution check (KF-MEANING-002) should use <see cref="EnumerateOccurrenceContexts"/>
    /// and <see cref="EnumerateDerivedEvidenceContexts"/> separately instead, since the latter is already
    /// self-attributing via its evidence row's FK ownership chain and must not be filtered the same way.
    /// </summary>
    public static List<PreparationService.ContextData> EnumerateAllValidContexts(SQLiteConnection connection, int wordId)
    {
        var occurrenceContexts = EnumerateOccurrenceContexts(connection, wordId);
        return occurrenceContexts.Count > 0
            ? occurrenceContexts
            : EnumerateDerivedEvidenceContexts(connection, wordId).ToList();
    }

    /// <summary>Every valid occurrence-based context for the Word, in (DocumentId, Order) order — no cap, no dedup.</summary>
    public static List<PreparationService.ContextData> EnumerateOccurrenceContexts(SQLiteConnection connection, int wordId)
    {
        var result = new List<PreparationService.ContextData>();
        var occurrences = connection.Table<WordOccurrenceEntity>()
            .Where(occurrence => occurrence.WordId == wordId)
            .OrderBy(occurrence => occurrence.DocumentId)
            .ThenBy(occurrence => occurrence.Order)
            .ToList();

        foreach (var occurrence in occurrences)
        {
            var document = connection.Find<DocumentEntity>(occurrence.DocumentId);
            var sentence = connection.Find<SentenceSpanEntity>(occurrence.SentenceSpanId);
            if (document is null || sentence is null
                || !PreparationService.TryCreateContext(document, sentence, occurrence, out var context))
            {
                continue;
            }

            result.Add(new PreparationService.ContextData(
                context.DocumentId, context.DocumentTitle, document.ExplanationLanguage,
                context.Text, context.TargetStart, context.TargetLength));
        }

        return result;
    }

    /// <summary>
    /// Fallback for a derived component (<c>CandidateProvenanceKind.DerivedFromCompound</c>), which
    /// intentionally never receives a <see cref="WordOccurrenceEntity"/>: builds context directly from any
    /// surviving <see cref="DerivedTermEvidenceEntity"/> row owned (via <see cref="ReviewCandidateEntity"/>)
    /// by this Word — retained only while the Word remains Unknown (see
    /// <c>TextReviewService.CompleteSession</c>) — pointing at the real whole-compound source span. Fails
    /// closed per row whose document/sentence relationship no longer validates.
    /// </summary>
    public static IEnumerable<PreparationService.ContextData> EnumerateDerivedEvidenceContexts(
        SQLiteConnection connection, int wordId)
    {
        var candidateIds = connection.Table<ReviewCandidateEntity>()
            .Where(candidate => candidate.WordId == wordId)
            .ToList()
            .Select(candidate => candidate.Id);

        foreach (var candidateId in candidateIds)
        {
            var evidenceRows = connection.Table<DerivedTermEvidenceEntity>()
                .Where(evidence => evidence.ReviewCandidateId == candidateId)
                .ToList();
            if (evidenceRows.Count == 0)
            {
                continue;
            }

            var candidate = connection.Find<ReviewCandidateEntity>(candidateId);
            var session = candidate is null ? null : connection.Find<ReviewSessionEntity>(candidate.SessionId);
            var document = session is null ? null : connection.Find<DocumentEntity>(session.DocumentId);
            if (document is null)
            {
                continue;
            }

            foreach (var evidence in evidenceRows)
            {
                var sentence = connection.Table<SentenceSpanEntity>()
                    .FirstOrDefault(item => item.DocumentId == document.Id && item.Order == evidence.SourceSentenceOrder);
                if (sentence is null || !PreparationService.TryCreateDerivedContext(document, sentence, evidence, out var context))
                {
                    continue;
                }

                yield return context;
            }
        }
    }

    /// <summary>True as soon as any valid context's key is outside <paramref name="effectiveProcessedKeys"/>.</summary>
    public static bool HasGenuinelyNewEvidence(
        SQLiteConnection connection, int wordId, IReadOnlySet<ContextEvidenceKey> effectiveProcessedKeys)
    {
        foreach (var context in EnumerateAllValidContexts(connection, wordId))
        {
            if (!effectiveProcessedKeys.Contains(CreateKey(context)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Up to <paramref name="maxCount"/> genuinely new evidence contexts, in scan order.</summary>
    public static List<PreparationService.ContextData> SelectGenuinelyNewEvidence(
        SQLiteConnection connection, int wordId, IReadOnlySet<ContextEvidenceKey> effectiveProcessedKeys, int maxCount)
    {
        var selected = new List<PreparationService.ContextData>();
        var seen = new HashSet<ContextEvidenceKey>();
        foreach (var context in EnumerateAllValidContexts(connection, wordId))
        {
            var key = CreateKey(context);
            if (effectiveProcessedKeys.Contains(key) || !seen.Add(key))
            {
                continue;
            }

            selected.Add(context);
            if (selected.Count == maxCount)
            {
                break;
            }
        }

        return selected;
    }

    /// <summary>Convenience: computes the ledger and selects up to <paramref name="maxCount"/> new evidence,
    /// already converted to the frozen <see cref="PreparationCandidateEvidence"/> shape.</summary>
    public static List<PreparationCandidateEvidence> SelectFrozenEvidence(SQLiteConnection connection, int wordId, int maxCount)
    {
        var effectiveProcessedKeys = Schema8EvidenceLedger.ComputeEffectiveProcessedKeys(connection, wordId);
        return SelectGenuinelyNewEvidence(connection, wordId, effectiveProcessedKeys, maxCount)
            .Select(context => new PreparationCandidateEvidence(
                context.DocumentId,
                PreparationContextEvidencePolicy.Fingerprint(context.Text),
                context.TargetStart,
                context.TargetLength))
            .ToList();
    }

    private static ContextEvidenceKey CreateKey(PreparationService.ContextData context) =>
        PreparationContextEvidencePolicy.CreateKey(context.DocumentId, context.Text, context.TargetStart, context.TargetLength);
}
