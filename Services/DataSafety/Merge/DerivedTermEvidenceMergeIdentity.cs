using System.Globalization;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// German Enhanced Term Recognition Package 5A-2: the stable cross-installation identity of one
/// transported <c>DerivedTermEvidenceEntity</c> row, and its ephemeral archive action-key label — kept in
/// one place so <see cref="MergePreflightPlannerV2"/> and <see cref="MergeWriterExecutor"/> can never drift
/// apart, mirroring <see cref="Schema9LearningReviewMergeIdentity"/>'s exact split for the same reason.
///
/// <para><b>Identity.</b> Built from the existing stable <see cref="ReviewCandidateIdentity"/> of the
/// owning candidate (itself derived from the owning <see cref="ReviewSessionIdentity"/> plus the derived
/// component's <see cref="VocabularyIdentity"/> — see <see cref="ReviewWorkflowIdentityPolicy.ComputeCandidateIdentityV2"/>),
/// the source-compound's own <see cref="VocabularyIdentity"/> (owning-document language plus
/// <c>SourceIdentity</c>), <c>SourceStartPosition</c>, <c>SourceLength</c>, <c>SourceSentenceOrder</c>, and
/// <c>ComponentForm</c>. Contains no SQLite id and no archive-local id. <c>SourceSurfaceForm</c> is
/// deliberately not a separate identity component: the graph-validation contract
/// (<see cref="BackupArchiveWriterV2.ValidatePayloadGraphV2"/>) already proves it is uniquely determined by
/// the owning document plus the source range, consistent with the binding Schema-11 physical uniqueness
/// semantics (<c>Schema11EvidenceValidator</c>).</para>
///
/// <para><b>Action key.</b> A transported evidence row has no archive-local id of its own (see
/// <c>BackupDerivedTermEvidenceV2</c>'s doc comment), so <see cref="MergePlanAction.ArchiveLocalId"/> carries
/// a synthesized positional label derived from the row's position in <c>archive.DerivedTermEvidence</c>
/// alone — a lookup label only, never a merge identity.</para>
/// </summary>
internal static class DerivedTermEvidenceMergeIdentity
{
    private const string Domain = "KnownFirst.Merge.DerivedTermEvidence.v1";

    public static string ArchiveActionKey(int archiveRowIndex) =>
        "dte#" + archiveRowIndex.ToString(CultureInfo.InvariantCulture);

    public static DerivedTermEvidenceIdentity Compute(
        ReviewCandidateIdentity owningCandidateIdentity,
        VocabularyIdentity sourceCompoundVocabularyIdentity,
        int sourceStartPosition,
        int sourceLength,
        int sourceSentenceOrder,
        string componentForm)
    {
        var builder = new CanonicalFingerprintBuilder(Domain)
            .WriteString(owningCandidateIdentity.Value)
            .WriteString(sourceCompoundVocabularyIdentity.Value)
            .WriteInt32(sourceStartPosition)
            .WriteInt32(sourceLength)
            .WriteInt32(sourceSentenceOrder)
            .WriteString(componentForm);

        return new DerivedTermEvidenceIdentity(builder.ComputeSha256Hex());
    }
}
