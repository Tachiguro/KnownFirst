using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Review-session/candidate identities per design §4.4. The v1 ReviewSession identity is the stable
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
    private const string SessionDomainV2 = "KnownFirst.Merge.ReviewSession.v2";
    private const string CandidateDigestDomainV2 = "KnownFirst.Merge.ReviewSession.CandidateDigest.v2";
    private const string CandidateDomainV2 = "KnownFirst.Merge.ReviewCandidate.v2";

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

        return ComputeCandidateIdentity(documentIdentity, ResolveVocabularyIdentity(item.VocabularyId, vocabularyIdentitiesByArchiveId));
    }

    // ---- v2: full completed-history ReviewSession identity (Schema-9 planner + writer target index) ----

    /// <summary>
    /// Full-history ReviewSession identity: (stable SourceMaterial identity, symbolic workflow status,
    /// StartedAtUtc, CompletedAtUtc, DecisionSequence, the five retained outcome counters, candidate digest).
    /// Two independently completed review histories over the same document are therefore two distinct
    /// identities, which is what lets the Schema-9 planner preserve both instead of declaring an
    /// unrepresentable conflict.
    ///
    /// <para>The counters (TotalCandidates/ReviewedCount/KnownCount/UnknownCount/IgnoredCount) are retained
    /// completed-review outcome data, not derived data: ordinary completion deletes every ReviewCandidate row
    /// of the session, so a retained Completed session normally exports with <c>Items</c> empty and these five
    /// values are its only persisted session-level outcome summary. They are generally not recomputable from
    /// child rows after completion and therefore participate in the identity.</para>
    ///
    /// <para>Deliberately excluded: the archive-local workflow id, every target-local numeric id, and the
    /// absolute candidate <c>Order</c> values, which are positional and safe to renumber. Order still
    /// determines the digest's ordering, with the stable VocabularyIdentity as the ordinal tie-break, so the
    /// result never depends on enumeration order.</para>
    /// </summary>
    public static ReviewSessionIdentityV2Result TryComputeSessionIdentityV2(
        SourceMaterialIdentity documentIdentity,
        BackupReviewSessionStatus status,
        DateTime startedAtUtc,
        DateTime? completedAtUtc,
        int decisionSequence,
        ReviewSessionOutcomeCounters counters,
        IReadOnlyList<ReviewSessionCandidateContent> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var orderedCandidates = candidates
            .OrderBy(candidate => candidate.Order)
            .ThenBy(candidate => candidate.VocabularyIdentity.Value, StringComparer.Ordinal)
            .ToList();

        var seenVocabularyIdentities = new HashSet<VocabularyIdentity>();
        var digestBuilder = new CanonicalFingerprintBuilder(CandidateDigestDomainV2);
        foreach (var candidate in orderedCandidates)
        {
            if (!seenVocabularyIdentities.Add(candidate.VocabularyIdentity))
            {
                // Fail closed rather than silently collapsing two decision records onto one candidate. The
                // caller layer chooses the exception; this policy only reports the violation.
                return new ReviewSessionIdentityV2Result(default, candidate.VocabularyIdentity);
            }

            digestBuilder
                .WriteString(candidate.VocabularyIdentity.Value)
                .WriteEnum(candidate.Status)
                .WriteEnum(candidate.PreviousKnowledgeState)
                .WriteInt32(candidate.PreviousTotalOccurrenceCount)
                .WriteInt32(candidate.PreviousDocumentCount)
                .WriteUtcTimestamp(candidate.PreviousUpdatedAtUtc)
                .WriteInt32(candidate.DecisionSequence)
                .WriteBoolean(candidate.WasVocabularyCreatedForSession)
                .WriteNullableUtcTimestamp(candidate.DecidedAtUtc);
        }

        var builder = new CanonicalFingerprintBuilder(SessionDomainV2)
            .WriteString(documentIdentity.Value)
            .WriteEnum(status)
            .WriteUtcTimestamp(startedAtUtc)
            .WriteNullableUtcTimestamp(completedAtUtc)
            .WriteInt32(decisionSequence)
            .WriteInt32(counters.TotalCandidates)
            .WriteInt32(counters.ReviewedCount)
            .WriteInt32(counters.KnownCount)
            .WriteInt32(counters.UnknownCount)
            .WriteInt32(counters.IgnoredCount)
            .WriteString(digestBuilder.ComputeSha256Hex());

        return new ReviewSessionIdentityV2Result(new ReviewSessionIdentity(builder.ComputeSha256Hex()), null);
    }

    public static ReviewSessionIdentityV2Result TryComputeSessionIdentityV2(
        BackupVocabularyReviewWorkflow workflow,
        IReadOnlyDictionary<string, SourceMaterialIdentity> sourceMaterialIdentitiesByArchiveId,
        IReadOnlyDictionary<string, VocabularyIdentity> vocabularyIdentitiesByArchiveId)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(sourceMaterialIdentitiesByArchiveId);
        ArgumentNullException.ThrowIfNull(vocabularyIdentitiesByArchiveId);

        if (!sourceMaterialIdentitiesByArchiveId.TryGetValue(workflow.SourceMaterialId, out var documentIdentity))
        {
            throw new KeyNotFoundException(
                $"No stable source-material identity supplied for archive source-material id '{workflow.SourceMaterialId}'.");
        }

        var candidates = workflow.Items
            .Select(item => new ReviewSessionCandidateContent(
                ResolveVocabularyIdentity(item.VocabularyId, vocabularyIdentitiesByArchiveId),
                item.Order,
                item.Status,
                item.PreviousKnowledgeState,
                item.PreviousTotalOccurrenceCount,
                item.PreviousDocumentCount,
                item.PreviousUpdatedAtUtc,
                item.DecisionSequence,
                item.WasVocabularyCreatedForSession,
                item.DecidedAtUtc))
            .ToList();

        return TryComputeSessionIdentityV2(
            documentIdentity, workflow.Status, workflow.StartedAtUtc, workflow.CompletedAtUtc, workflow.DecisionSequence,
            new ReviewSessionOutcomeCounters(
                workflow.TotalCandidates, workflow.ReviewedCount, workflow.KnownCount, workflow.UnknownCount, workflow.IgnoredCount),
            candidates);
    }

    /// <summary>
    /// ReviewCandidate identity cascading from its parent session's full-history identity. Order and the
    /// decision fields are deliberately not repeated here: they already participate through the parent
    /// session's candidate digest, so repeating them would only re-encode the same facts.
    /// </summary>
    public static ReviewCandidateIdentity ComputeCandidateIdentityV2(
        ReviewSessionIdentity sessionIdentity, VocabularyIdentity vocabularyIdentity)
    {
        var builder = new CanonicalFingerprintBuilder(CandidateDomainV2)
            .WriteString(sessionIdentity.Value)
            .WriteString(vocabularyIdentity.Value);

        return new ReviewCandidateIdentity(builder.ComputeSha256Hex());
    }

    private static VocabularyIdentity ResolveVocabularyIdentity(
        string archiveVocabularyId, IReadOnlyDictionary<string, VocabularyIdentity> vocabularyIdentitiesByArchiveId)
    {
        if (!vocabularyIdentitiesByArchiveId.TryGetValue(archiveVocabularyId, out var vocabularyIdentity))
        {
            throw new KeyNotFoundException(
                $"No stable vocabulary identity supplied for archive vocabulary id '{archiveVocabularyId}'.");
        }

        return vocabularyIdentity;
    }
}

/// <summary>
/// One archived/persisted ReviewCandidate reduced to exactly the content that participates in the v2
/// ReviewSession identity. <see cref="Order"/> only orders the digest; its absolute value is never encoded.
/// </summary>
public readonly record struct ReviewSessionCandidateContent(
    VocabularyIdentity VocabularyIdentity,
    int Order,
    BackupKnowledgeState Status,
    BackupKnowledgeState PreviousKnowledgeState,
    int PreviousTotalOccurrenceCount,
    int PreviousDocumentCount,
    DateTime PreviousUpdatedAtUtc,
    int DecisionSequence,
    bool WasVocabularyCreatedForSession,
    DateTime? DecidedAtUtc);

/// <summary>
/// The five retained completed-review outcome counters of one ReviewSession. Grouped so the two production
/// call paths (archive DTO and raw SQLite row) cannot silently transpose two same-typed values.
/// </summary>
public readonly record struct ReviewSessionOutcomeCounters(
    int TotalCandidates,
    int ReviewedCount,
    int KnownCount,
    int UnknownCount,
    int IgnoredCount);

/// <summary>
/// Shared, caller-neutral outcome of a v2 ReviewSession identity computation. A valid review workflow may
/// never contain two candidates resolving to the same stable <see cref="VocabularyIdentity"/>; when it does,
/// <see cref="DuplicateCandidateVocabularyIdentity"/> is non-null and <see cref="Identity"/> must not be used.
/// The policy deliberately does not throw: the planner path fails with
/// <c>MergePlanningException(BackupErrorCodes.DuplicateId)</c> and the writer target-index path with
/// <c>BackupFormatException(BackupErrorCodes.DuplicateId)</c>, and each caller keeps its own contract.
/// </summary>
public readonly record struct ReviewSessionIdentityV2Result(
    ReviewSessionIdentity Identity,
    VocabularyIdentity? DuplicateCandidateVocabularyIdentity)
{
    public bool HasDuplicateCandidateVocabularyIdentity => DuplicateCandidateVocabularyIdentity is not null;
}
