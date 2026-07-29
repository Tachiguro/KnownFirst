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
    /// <summary>Every valid context for the Word, in (DocumentId, Order) order — no cap, no dedup.</summary>
    public static List<PreparationService.ContextData> EnumerateAllValidContexts(SQLiteConnection connection, int wordId)
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
