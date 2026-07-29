using KnownFirst.Core.Preparation;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Services.Study;

/// <summary>
/// Computes the effective-processed-evidence ledger for one Word (KF-MEANING-001 Slice 3), the input to
/// Schema-8 eligibility (a <see cref="Models.PreparationState.Prepared"/> Word only re-qualifies when it
/// has evidence outside this set) and to freezing new candidate evidence.
/// </summary>
/// <remarks>
/// <para>
/// <c>existingContextSnapshotKeys</c> = the four-field keys of every existing <c>ContextSnapshots</c> row
/// for the Word. <c>allKeysMentionedByAnyValidEnvelope</c> = keys from every genuine EnvelopeV1 for the
/// Word, regardless of candidate status (Empty/LegacyLexicalResult candidates own no envelope evidence).
/// <c>fullyResolvedPreparedEnvelopeKeys</c> = keys from EnvelopeV1 rows whose candidate status is
/// <see cref="Models.PreparationCandidateStatus.Prepared"/> and whose provider indexes are fully resolved.
/// <c>legacyBaselineKeys</c> = <c>existingContextSnapshotKeys</c> minus <c>allKeysMentionedByAnyValidEnvelope</c>
/// (grandfathered evidence predating the envelope ledger). The result is the union of the legacy baseline
/// and the fully-resolved-Prepared keys — evidence owned only by a Skipped/Cancelled/Failed/Pending/
/// ResultReady/partially-resolved envelope is neither, and is therefore still available for re-offering.
/// </para>
/// </remarks>
internal static class Schema8EvidenceLedger
{
    public static IReadOnlySet<ContextEvidenceKey> ComputeEffectiveProcessedKeys(SQLiteConnection connection, int wordId)
    {
        var existingContextSnapshotKeys = LoadContextSnapshotKeys(connection, wordId);
        var (allMentioned, fullyResolvedPrepared) = ScanCandidateEnvelopes(connection, wordId);

        var legacyBaselineKeys = new HashSet<ContextEvidenceKey>(existingContextSnapshotKeys);
        legacyBaselineKeys.ExceptWith(allMentioned);
        legacyBaselineKeys.UnionWith(fullyResolvedPrepared);
        return legacyBaselineKeys;
    }

    private static HashSet<ContextEvidenceKey> LoadContextSnapshotKeys(SQLiteConnection connection, int wordId) =>
        connection.Query<ContextSnapshotKeyRow>(
                "SELECT SourceDocumentId, NormalizedFingerprint, TargetStart, TargetLength FROM ContextSnapshots WHERE WordId = ?",
                wordId)
            .Select(row => new ContextEvidenceKey(row.SourceDocumentId, row.NormalizedFingerprint, row.TargetStart, row.TargetLength))
            .ToHashSet();

    /// <summary>
    /// Walks every <see cref="PreparationCandidateEntity"/> ever created for the Word. Empty and
    /// LegacyLexicalResult history own no envelope evidence (skipped). A genuine EnvelopeV1's frozen
    /// evidence keys are always added to <c>allMentioned</c>; they are additionally added to
    /// <c>fullyResolvedPrepared</c> only when the candidate is Prepared and every provider index has been
    /// resolved. Unsupported/malformed history throws <see cref="PreparationCandidateStateException"/>
    /// before the caller can proceed to any session/candidate mutation.
    /// </summary>
    private static (HashSet<ContextEvidenceKey> AllMentioned, HashSet<ContextEvidenceKey> FullyResolvedPrepared) ScanCandidateEnvelopes(
        SQLiteConnection connection, int wordId)
    {
        var allMentioned = new HashSet<ContextEvidenceKey>();
        var fullyResolvedPrepared = new HashSet<ContextEvidenceKey>();

        var candidates = connection.Table<PreparationCandidateEntity>()
            .Where(candidate => candidate.WordId == wordId)
            .ToList();

        foreach (var candidate in candidates)
        {
            var read = PreparationCandidatePayloadCodec.Read(candidate.ResultJson);
            switch (read.Kind)
            {
                case PreparationCandidatePayloadKind.Empty:
                case PreparationCandidatePayloadKind.LegacyLexicalResult:
                    continue;

                case PreparationCandidatePayloadKind.EnvelopeV1:
                {
                    var envelope = read.Envelope!;
                    var keys = envelope.FrozenEvidence
                        .Select(evidence => new ContextEvidenceKey(
                            evidence.SourceDocumentId, evidence.NormalizedFingerprint, evidence.TargetStart, evidence.TargetLength))
                        .ToArray();
                    allMentioned.UnionWith(keys);

                    var fullyResolved = envelope.Result is not null
                        && envelope.ResolvedProviderMeaningIndexes.Count == envelope.Result.Meanings.Count;
                    if (candidate.Status == PreparationCandidateStatus.Prepared && fullyResolved)
                    {
                        fullyResolvedPrepared.UnionWith(keys);
                    }

                    continue;
                }

                default:
                    throw new PreparationCandidateStateException(
                        "preparation-candidate-history-unreadable",
                        $"Word {wordId} has a preparation candidate (Id={candidate.Id}) with unsupported or malformed " +
                        $"history and cannot be evaluated for new preparation eligibility: " +
                        (read.Kind == PreparationCandidatePayloadKind.UnsupportedEnvelopeVersion
                            ? $"unsupported envelope version {read.UnsupportedVersion}."
                            : read.FailureDetail ?? "malformed payload."));
            }
        }

        return (allMentioned, fullyResolvedPrepared);
    }

    private sealed class ContextSnapshotKeyRow
    {
        public int SourceDocumentId { get; set; }
        public string NormalizedFingerprint { get; set; } = string.Empty;
        public int TargetStart { get; set; }
        public int TargetLength { get; set; }
    }
}
