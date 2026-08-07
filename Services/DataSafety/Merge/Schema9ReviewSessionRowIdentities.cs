using KnownFirst.Data.Entities;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Caller-neutral raw-row plumbing for the Schema-9 completed-review identity (KF-BACKUP-002 Package C).
/// Two callers need the identical computation over raw <c>Schema8BackupSnapshot</c> rows — the writer's
/// <see cref="MergeWriterTargetIndex"/>, which maps each stable identity back to a real SQLite row id, and
/// <see cref="BackupModelMapperV2"/>, which needs a content-derived ordering key for its
/// <c>ReviewSessions</c> output so the emitted archive-local ids never depend on installation-local row
/// ids. Centralizing the row conversion here keeps exactly one definition of the completed-review identity:
/// every method below ultimately delegates to <see cref="ReviewWorkflowIdentityPolicy"/>.
///
/// <para><b>Deliberately caller-neutral.</b> Nothing here selects behavior per caller: there is no mode
/// flag, no error-contract switch, and no exception policy. Every method is a pure function of its
/// arguments, and reference resolution stays with the caller so each keeps its own existing contract —
/// <see cref="MergeWriterTargetIndex"/> continues to fail closed with
/// <see cref="BackupErrorCodes.DuplicateId"/>, and <see cref="BackupModelMapperV2"/> continues to map
/// every snapshot it is given without introducing a new error surface.</para>
/// </summary>
internal static class Schema9ReviewSessionRowIdentities
{
    /// <summary>
    /// Ordering material only — never an identity, and deliberately a separate hash family from
    /// <c>KnownFirst.Merge.ReviewSession.v2</c>: unlike the identity, this key encodes the <b>absolute</b>
    /// candidate <c>Order</c> values, because those are emitted into the archive while the identity
    /// deliberately excludes them (they are positional and safe to renumber).
    /// </summary>
    private const string CandidateContentOrderingDomain =
        "KnownFirst.Merge.ReviewSession.RowCandidateContentOrdering.v1";

    /// <summary>Stable source-material identity of one raw <see cref="DocumentEntity"/> row.</summary>
    public static SourceMaterialIdentity ComputeDocumentIdentity(DocumentEntity document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return SourceMaterialIdentityPolicy.Compute(ToBackupSourceMaterial(document));
    }

    /// <summary>Stable vocabulary identity of one raw <see cref="WordEntity"/> row.</summary>
    public static VocabularyIdentity ComputeVocabularyIdentity(WordEntity word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return VocabularyMergeIdentityPolicy.Compute(word.Language, word.NormalizedTerm);
    }

    /// <summary>
    /// Reduces one raw <see cref="ReviewCandidateEntity"/> row plus its already-resolved stable vocabulary
    /// identity to the content the v2 session identity consumes. Raw SQLite timestamps pass through
    /// <see cref="EnsureUtc(DateTime)"/> first, so an <see cref="DateTimeKind.Unspecified"/> row and the
    /// archive DTO's <see cref="DateTimeKind.Utc"/> value with the same ticks canonicalize to byte-identical
    /// input.
    /// </summary>
    public static ReviewSessionCandidateContent BuildCandidateContent(
        ReviewCandidateEntity candidate, VocabularyIdentity vocabularyIdentity)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return new ReviewSessionCandidateContent(
            vocabularyIdentity,
            candidate.Order,
            BackupEnumMappings.ToBackup(candidate.Status),
            BackupEnumMappings.ToBackup(candidate.PreviousWordStatus),
            candidate.PreviousTotalOccurrenceCount,
            candidate.PreviousDocumentCount,
            EnsureUtc(candidate.PreviousUpdatedAt),
            candidate.DecisionSequence,
            candidate.WasWordCreatedForSession,
            EnsureUtc(candidate.DecidedAt));
    }

    /// <summary>
    /// The full Schema-9 completed-review identity of one raw <see cref="ReviewSessionEntity"/> row,
    /// delegating to <see cref="ReviewWorkflowIdentityPolicy"/>'s <c>TryComputeSessionIdentityV2</c>. The
    /// duplicate candidate-vocabulary condition is returned as data, exactly as that policy reports it; each
    /// caller applies its own contract to it.
    /// </summary>
    public static ReviewSessionIdentityV2Result ComputeSessionIdentity(
        ReviewSessionEntity session,
        SourceMaterialIdentity documentIdentity,
        IReadOnlyList<ReviewSessionCandidateContent> candidateContent)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(candidateContent);

        return ReviewWorkflowIdentityPolicy.TryComputeSessionIdentityV2(
            documentIdentity,
            BackupEnumMappings.ToBackup(session.Status),
            EnsureUtc(session.StartedAt),
            EnsureUtc(session.CompletedAt),
            session.DecisionSequence,
            new ReviewSessionOutcomeCounters(
                session.TotalCandidates, session.ReviewedCount, session.KnownCount,
                session.UnknownCount, session.IgnoredCount),
            candidateContent);
    }

    /// <summary>
    /// Deterministic, content-derived ordering material over one session's complete emitted candidate
    /// content, including the absolute <c>Order</c> values. Always computable — it needs no stable identity
    /// resolution at all, so it is defined even for a snapshot whose vocabulary references do not resolve or
    /// whose candidates repeat one vocabulary identity, and it never degrades to a shared sentinel: two
    /// sessions receive the same key only when the candidate rows they emit are identical.
    ///
    /// <para><paramref name="candidates"/> pairs each raw row with the caller's own stable per-candidate
    /// vocabulary key (the value that determines the emitted <c>vocabularyId</c>). Candidates are ordered by
    /// <c>(Order, vocabulary key)</c> — the same ordering the v2 identity's candidate digest uses — so the
    /// key never depends on row enumeration order. Encoding is the length-prefixed, domain-discriminated
    /// <see cref="CanonicalFingerprintBuilder"/> form, never a delimiter-joined <c>ToString()</c>
    /// concatenation.</para>
    /// </summary>
    public static string ComputeCandidateContentOrderingKey(
        IEnumerable<(ReviewCandidateEntity Candidate, string VocabularyKey)> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var builder = new CanonicalFingerprintBuilder(CandidateContentOrderingDomain);
        var ordered = candidates
            .OrderBy(entry => entry.Candidate.Order)
            .ThenBy(entry => entry.VocabularyKey, StringComparer.Ordinal)
            .ToList();

        builder.WriteInt32(ordered.Count);
        foreach (var (candidate, vocabularyKey) in ordered)
        {
            builder
                .WriteString(vocabularyKey)
                .WriteInt32(candidate.Order)
                .WriteEnum(BackupEnumMappings.ToBackup(candidate.Status))
                .WriteEnum(BackupEnumMappings.ToBackup(candidate.PreviousWordStatus))
                .WriteInt32(candidate.PreviousTotalOccurrenceCount)
                .WriteInt32(candidate.PreviousDocumentCount)
                .WriteUtcTimestamp(EnsureUtc(candidate.PreviousUpdatedAt))
                .WriteInt32(candidate.DecisionSequence)
                .WriteBoolean(candidate.WasWordCreatedForSession)
                .WriteNullableUtcTimestamp(EnsureUtc(candidate.DecidedAt));
        }

        return builder.ComputeSha256Hex();
    }

    private static BackupSourceMaterial ToBackupSourceMaterial(DocumentEntity document) => new(
        document.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        document.Title,
        document.TextLanguage,
        document.ExplanationLanguage,
        BackupEnumMappings.ToBackup(document.LookupMode),
        string.IsNullOrEmpty(document.TargetLanguage) ? null : document.TargetLanguage,
        document.Content,
        document.ContentFingerprint,
        EnsureUtc(document.ImportedAt),
        document.WordCount,
        [],
        []);

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? EnsureUtc(DateTime? value) => value.HasValue ? EnsureUtc(value.Value) : null;
}
