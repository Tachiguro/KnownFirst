using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// PreparationSession/PreparationCandidate identities per design §4.4. A PreparationSession has no
/// natural foreign key tying it to anything else, so its identity is a content fingerprint of
/// (StartedAtUtc, CompletedAtUtc, SHA-256 digest over the ordered list of stable VocabularyIdentity
/// values among its items). PreparationCandidate identity cascades from the parent session's identity,
/// one level down.
/// </summary>
public static class PreparationWorkflowIdentityPolicy
{
    private const string SessionDomain = "KnownFirst.Merge.PreparationSession.v1";
    private const string ItemsDigestDomain = "KnownFirst.Merge.PreparationSession.ItemsDigest.v1";
    private const string CandidateDomain = "KnownFirst.Merge.PreparationCandidate.v1";

    public static PreparationSessionIdentity ComputeSessionIdentity(
        DateTime startedAtUtc,
        DateTime? completedAtUtc,
        IReadOnlyList<VocabularyIdentity> orderedItemVocabularyIdentities)
    {
        ArgumentNullException.ThrowIfNull(orderedItemVocabularyIdentities);

        var digestBuilder = new CanonicalFingerprintBuilder(ItemsDigestDomain);
        foreach (var vocabularyIdentity in orderedItemVocabularyIdentities)
        {
            digestBuilder.WriteString(vocabularyIdentity.Value);
        }

        var itemsDigest = digestBuilder.ComputeSha256Hex();

        var builder = new CanonicalFingerprintBuilder(SessionDomain)
            .WriteUtcTimestamp(startedAtUtc)
            .WriteNullableUtcTimestamp(completedAtUtc)
            .WriteString(itemsDigest);

        return new PreparationSessionIdentity(builder.ComputeSha256Hex());
    }

    public static PreparationSessionIdentity ComputeSessionIdentity(
        BackupPreparationWorkflow workflow,
        IReadOnlyDictionary<string, VocabularyIdentity> vocabularyIdentitiesByArchiveId)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(vocabularyIdentitiesByArchiveId);

        var orderedIdentities = workflow.Items
            .OrderBy(item => item.Order)
            .Select(item => ResolveVocabularyIdentity(item.VocabularyId, vocabularyIdentitiesByArchiveId))
            .ToList();

        return ComputeSessionIdentity(workflow.StartedAtUtc, workflow.CompletedAtUtc, orderedIdentities);
    }

    public static PreparationCandidateIdentity ComputeCandidateIdentity(
        PreparationSessionIdentity sessionIdentity, VocabularyIdentity vocabularyIdentity, int order)
    {
        var builder = new CanonicalFingerprintBuilder(CandidateDomain)
            .WriteString(sessionIdentity.Value)
            .WriteString(vocabularyIdentity.Value)
            .WriteInt32(order);

        return new PreparationCandidateIdentity(builder.ComputeSha256Hex());
    }

    public static PreparationCandidateIdentity ComputeCandidateIdentity(
        BackupPreparationItem item,
        PreparationSessionIdentity sessionIdentity,
        IReadOnlyDictionary<string, VocabularyIdentity> vocabularyIdentitiesByArchiveId)
    {
        ArgumentNullException.ThrowIfNull(item);

        var vocabularyIdentity = ResolveVocabularyIdentity(item.VocabularyId, vocabularyIdentitiesByArchiveId);
        return ComputeCandidateIdentity(sessionIdentity, vocabularyIdentity, item.Order);
    }

    private static VocabularyIdentity ResolveVocabularyIdentity(
        string archiveVocabularyId, IReadOnlyDictionary<string, VocabularyIdentity> vocabularyIdentitiesByArchiveId)
    {
        ArgumentNullException.ThrowIfNull(vocabularyIdentitiesByArchiveId);
        if (!vocabularyIdentitiesByArchiveId.TryGetValue(archiveVocabularyId, out var identity))
        {
            throw new KeyNotFoundException(
                $"No stable vocabulary identity supplied for archive vocabulary id '{archiveVocabularyId}'.");
        }

        return identity;
    }
}
