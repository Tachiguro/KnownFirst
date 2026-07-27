using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Review-session/candidate identities per design §4.4. ReviewSession identity is the stable
/// SourceMaterial (Document) identity alone — verified safe because the corrected 4-part Document
/// identity (§4.1) already guarantees two devices only collide on the same ReviewSession when they
/// analyzed byte-identical text under the same LookupMode/ExplanationLanguage/TargetLanguage.
/// ReviewCandidate identity adds the stable VocabularyIdentity, since (SessionId, Order) is positional,
/// not cross-run-stable.
/// </summary>
public static class ReviewWorkflowIdentityPolicy
{
    private const string SessionDomain = "KnownFirst.Merge.ReviewSession.v1";
    private const string CandidateDomain = "KnownFirst.Merge.ReviewCandidate.v1";

    public static ReviewSessionIdentity ComputeSessionIdentity(SourceMaterialIdentity documentIdentity)
    {
        var builder = new CanonicalFingerprintBuilder(SessionDomain)
            .WriteString(documentIdentity.Value);

        return new ReviewSessionIdentity(builder.ComputeSha256Hex());
    }

    public static ReviewSessionIdentity ComputeSessionIdentity(
        BackupVocabularyReviewWorkflow workflow,
        IReadOnlyDictionary<string, SourceMaterialIdentity> sourceMaterialIdentitiesByArchiveId)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(sourceMaterialIdentitiesByArchiveId);

        if (!sourceMaterialIdentitiesByArchiveId.TryGetValue(workflow.SourceMaterialId, out var documentIdentity))
        {
            throw new KeyNotFoundException(
                $"No stable source-material identity supplied for archive source-material id '{workflow.SourceMaterialId}'.");
        }

        return ComputeSessionIdentity(documentIdentity);
    }

    public static ReviewCandidateIdentity ComputeCandidateIdentity(
        SourceMaterialIdentity documentIdentity, VocabularyIdentity vocabularyIdentity)
    {
        var builder = new CanonicalFingerprintBuilder(CandidateDomain)
            .WriteString(documentIdentity.Value)
            .WriteString(vocabularyIdentity.Value);

        return new ReviewCandidateIdentity(builder.ComputeSha256Hex());
    }

    public static ReviewCandidateIdentity ComputeCandidateIdentity(
        BackupVocabularyReviewItem item,
        SourceMaterialIdentity documentIdentity,
        IReadOnlyDictionary<string, VocabularyIdentity> vocabularyIdentitiesByArchiveId)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(vocabularyIdentitiesByArchiveId);

        if (!vocabularyIdentitiesByArchiveId.TryGetValue(item.VocabularyId, out var vocabularyIdentity))
        {
            throw new KeyNotFoundException(
                $"No stable vocabulary identity supplied for archive vocabulary id '{item.VocabularyId}'.");
        }

        return ComputeCandidateIdentity(documentIdentity, vocabularyIdentity);
    }
}
