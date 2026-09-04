using KnownFirst.Core.Preparation;
using KnownFirst.Core.Learning;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Services.Study;

public sealed partial class PreparationService
{
    /// <summary>
    /// Schema-8 <c>StartAsync</c> candidate selection (KF-MEANING-001 Slice 3 §2). Unlike the unchanged
    /// Schema-7 path, a Word already <see cref="PreparationState.Prepared"/> with existing Senses can
    /// become eligible again when it has genuinely new evidence (per <see cref="Schema8EvidenceLedger"/>);
    /// occurrence frequency is ordering only, never an eligibility filter. Selected evidence snapshots are
    /// only computed — and frozen into each candidate's envelope while it is still Pending — after a Word
    /// has survived priority ordering and the requested-limit cut, never before.
    /// </summary>
    private int StartSchema8(
        SQLiteConnection connection,
        PreparationMethod method,
        int requestedLimit,
        ValidatedPreparationSchema8Capability capability,
        PreparationTargetAdditionRequest? targetAddition = null)
    {
        ArgumentNullException.ThrowIfNull(capability);

        var now = clock.UtcNow;
        var selectionCandidates = new List<Schema8SelectionCandidate>();

        if (targetAddition is not null)
        {
            ValidateTargetAddition(targetAddition);
            var targetWord = connection.Find<WordEntity>(targetAddition.WordId);
            var targetSenseWordId = connection.ExecuteScalar<int>(
                "SELECT COALESCE((SELECT WordId FROM Senses WHERE Id = ?), 0)",
                targetAddition.SenseId);
            if (targetWord is not null
                && targetSenseWordId == targetWord.Id
                && ReviewIsResolved(connection, targetWord.Id)
                && targetWord.PreparationState == PreparationState.Prepared
                && targetWord.Status is WordStatus.UnknownBacklog or WordStatus.Prepared or WordStatus.Learning or WordStatus.Mastered
                && WordHasSenses(connection, targetWord.Id))
            {
                selectionCandidates.Add(new Schema8SelectionCandidate(targetWord, Math.Max(1, targetWord.TotalOccurrenceCount), targetWord.CreatedAt));
            }
        }
        else
        {
            var words = connection.Table<WordEntity>().ToList();
            foreach (var word in words)
            {
                if (!ReviewIsResolved(connection, word.Id))
                {
                    continue;
                }

                if (word.Status == WordStatus.UnknownBacklog && word.PreparationState == PreparationState.Unprepared)
                {
                    if (WordHasSenses(connection, word.Id))
                    {
                        // Defensive: an Unprepared word should never already own a Sense in normal operation.
                        continue;
                    }

                    selectionCandidates.Add(new Schema8SelectionCandidate(word, word.TotalOccurrenceCount, word.CreatedAt));
                }
                else if (word.PreparationState == PreparationState.Prepared && WordHasSenses(connection, word.Id))
                {
                    // §5: throws PreparationCandidateStateException before any mutation when this Word's own
                    // candidate history cannot be classified.
                    var effectiveProcessedKeys = Schema8EvidenceLedger.ComputeEffectiveProcessedKeys(connection, word.Id);
                    if (Schema8EvidenceScanner.HasGenuinelyNewEvidence(connection, word.Id, effectiveProcessedKeys))
                    {
                        selectionCandidates.Add(new Schema8SelectionCandidate(word, word.TotalOccurrenceCount, word.CreatedAt));
                    }
                }
            }
        }

        var limit = Math.Clamp(requestedLimit, 0, PreparationSelectionPolicy.HardMaximum);
        var selected = selectionCandidates
            .OrderByDescending(candidate => candidate.OccurrenceCount)
            .ThenBy(candidate => candidate.FirstSeenAtUtc)
            .ThenBy(candidate => candidate.Word.CanonicalTerm, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();

        if (selected.Length == 0)
        {
            return 0;
        }

        var session = new PreparationSessionEntity
        {
            Status = PreparationSessionStatus.Active,
            Method = method,
            TotalItems = selected.Length,
            StartedAtUtc = now,
            UpdatedAtUtc = now
        };
        connection.Insert(session);

        for (var index = 0; index < selected.Length; index++)
        {
            var word = selected[index].Word;
            var candidate = new PreparationCandidateEntity
            {
                SessionId = session.Id,
                WordId = word.Id,
                Order = index,
                Status = PreparationCandidateStatus.Pending,
                UpdatedAtUtc = now
            };
            connection.Insert(candidate);

            // §2: evidence snapshots are selected only now — after this Word has survived both priority
            // ordering and the requested-limit cut — and frozen while the candidate is still Pending.
            var frozen = Schema8EvidenceScanner.SelectFrozenEvidence(connection, word.Id, MaximumContextSnapshots);
            if (frozen.Count == 0 && targetAddition is not null)
            {
                var recognizedSurfaceForms = LoadRecognizedSurfaceForms(connection, word.Id);
                var allContexts = Schema8EvidenceScanner.EnumerateOccurrenceContexts(connection, word.Id);
                var validContexts = allContexts.Where(c => IsAttributableToCandidate(word.Id, c.Text, c.TargetStart, c.TargetLength, recognizedSurfaceForms));
                frozen = validContexts
                    .Take(MaximumContextSnapshots)
                    .Select(c => new PreparationCandidateEvidence(c.DocumentId, PreparationContextEvidencePolicy.Fingerprint(c.Text), c.TargetStart, c.TargetLength))
                    .ToList();
            }
            candidate.ResultJson = PreparationCandidatePayloadCodec.Write(
                PreparationCandidatePayloadV1.CreatePending(frozen, targetAddition));
            connection.Update(candidate);

            word.PreparationState = PreparationState.Preparing;
            word.UpdatedAt = now;
            connection.Update(word);
        }

        return session.Id;
    }

    private static bool WordHasSenses(SQLiteConnection connection, int wordId) =>
        connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Senses WHERE WordId = ?", wordId) > 0;

    private static void ValidateTargetAddition(PreparationTargetAdditionRequest request)
    {
        if (request.WordId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "An existing positive WordId is required.");
        }

        if (request.SenseId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "An existing positive SenseId is required.");
        }

        if (!Enum.IsDefined(request.TargetKind))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "A supported learning-target kind is required.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetLanguage);
    }

    private sealed record Schema8SelectionCandidate(WordEntity Word, int OccurrenceCount, DateTime FirstSeenAtUtc);
}
