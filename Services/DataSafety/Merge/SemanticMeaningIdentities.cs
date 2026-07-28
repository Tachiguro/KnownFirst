using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

// Stable, content-derived identities for the approved Word -> SemanticMeaning -> AnswerVariant model
// (KF-MEANING-001). These are additive to Slice 1's MeaningIdentity/LearningCardMatchIdentity
// (Services/DataSafety/Merge/MergeIdentities.cs, MeaningIdentityPolicy.cs, LearningCardIdentityPolicy.cs),
// which remain unchanged so existing Slice 1/2 contract tests keep passing unweakened. The types here
// are what KF-BACKUP-002 Slice 3's planner uses going forward for meaning-centric classification; the
// old MeaningIdentity continues to exist only as the historical Slice 1 contract artifact.

/// <summary>
/// One learnable sense of a Word (e.g. "bank" the financial institution vs. "bank" the river edge).
/// Distinct <see cref="SemanticMeaningIdentity"/> values require separate future LearningCards and
/// independent learning progress (§"Product model" of the approved meaning-centric design). Computed
/// from stable Word identity, source/explanation language, provider sense id (when present), a
/// forward-compatible topic/domain component (always empty today — see
/// <c>docs/architecture/backup-merge-v1-design.md</c> "Topic/domain persistence gap"), and a
/// grammatical/part-of-speech discriminator (plus acronym expansion). Deliberately excludes free-text
/// Definition/Translation wording (final focused review correction — neither is a reliable sense
/// discriminator on its own), notes, examples, aliases, attribution, and confirmation state — those
/// distinguish an <see cref="ExactMeaningVariantIdentity"/>, never a semantic meaning.
/// </summary>
public readonly record struct SemanticMeaningIdentity(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// The identity of one persisted Meaning row's complete content: every field the current schema
/// actually stores, including notes, examples, aliases, confirmation, provenance, provider metadata,
/// and manual-versus-provider origin (via <see cref="BackupSourceReference.ProviderName"/>). Two rows
/// with the same <see cref="ExactMeaningVariantIdentity"/> are byte-identical persisted content; two
/// rows with the same <see cref="SemanticMeaningIdentity"/> but a different
/// <see cref="ExactMeaningVariantIdentity"/> are the same learnable sense with distinct preserved
/// content (design rule: "same SemanticMeaning plus different note/example/provenance: preserve exact
/// content variant without creating a second semantic card").
/// </summary>
public readonly record struct ExactMeaningVariantIdentity(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// One valid expression (answer text) for a <see cref="SemanticMeaningIdentity"/> — e.g. "bank" or
/// "financial institution" for the same sense. Identity is deliberately text-content-based only
/// (SemanticMeaningIdentity + normalized answer text + answer language); <see cref="AnswerVariantRole"/>
/// is presentational/behavioral metadata, not an identity component, so the same accepted text is the
/// same variant regardless of which row or role first introduced it — this is what makes "equivalent
/// normalized aliases deduplicate" hold even across two different Meaning rows for the same sense.
/// </summary>
public readonly record struct AnswerVariantIdentity(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// The stable matching identity a future meaning-centric LearningCard would use:
/// (SemanticMeaningIdentity, CardDirection) — deliberately narrower than (VocabularyIdentity,
/// Direction), which is today's schema-enforced key and conflates every sense of a word into one
/// physical card slot. This type is provided and tested as a forward-compatible primitive for
/// KF-MEANING-001's future merge writer; Slice 3's planner does not use it to match real
/// <c>LearningCardEntity</c> rows, because the live schema does not yet support more than one card per
/// (WordId, Direction) — see "Current-schema compatibility" in the architecture document.
/// </summary>
public readonly record struct FutureCardIdentity(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// A correlatable, deterministic reference between a plan action classified
/// <see cref="MergeEntityClassification.UnresolvedConflict"/> and the decision object that resolves it.
/// Every unresolved-conflict action carries a non-null <see cref="MergePlanAction.DecisionId"/>, and
/// exactly one decision record in the plan carries the same <see cref="DecisionId"/> — this is what
/// makes "no unresolved count exists without a decision" a checkable invariant rather than an assertion.
/// </summary>
public readonly record struct DecisionId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// The role an <see cref="AnswerVariantIdentity"/> plays for its SemanticMeaning (KF-MEANING-001).
/// <see cref="RequiredAnswerVariant"/> can never be produced by Slice 3: the current schema has no field
/// recording which alias a user selected as individually required for mastery — every variant the
/// current data can express is either the primary display answer or an unranked accepted alias. This is
/// part of the documented "answer-variant-progress-migration-required" gap.
/// </summary>
public enum AnswerVariantRole
{
    PrimaryAnswer,
    RequiredAnswerVariant,
    AcceptedAlias
}

/// <summary>
/// Forward-compatible role reconciliation for two <see cref="AnswerVariantRole"/> values describing the
/// <b>same</b> <see cref="AnswerVariantIdentity"/> (same text, same SemanticMeaning) seen on two sides of
/// a merge. Role never splits identity — the identical answer text is stored once regardless of role —
/// but when the two sides disagree on role, <see cref="AnswerVariantRole.RequiredAnswerVariant"/> outranks
/// <see cref="AnswerVariantRole.AcceptedAlias"/>, because either device explicitly requiring the variant
/// for mastery must be preserved (a stricter role can never be silently downgraded by merging with a
/// looser one). <see cref="AnswerVariantRole.PrimaryAnswer"/> is a separate, singleton per-SemanticMeaning
/// preference, not a rank in this ordering — conflicting <c>PrimaryAnswer</c> choices are resolved by
/// <see cref="PreferredVariantSelectionDecision"/>, never by this comparison. Reconciling two roles for the
/// same identity is always an enrichment of the existing answer variant, never a second answer entity.
/// <see cref="AnswerVariantRole.RequiredAnswerVariant"/> can never actually appear as an input from current
/// archive data (see that value's own doc comment) — this method exists so the ranking is already correct
/// once a future schema change can supply it.
/// </summary>
public static class AnswerVariantRolePrecedencePolicy
{
    /// <summary>Returns the role that should win when the same AnswerVariantIdentity is seen with two (possibly different) roles.</summary>
    public static AnswerVariantRole Reconcile(AnswerVariantRole existing, AnswerVariantRole incoming)
    {
        if (existing == AnswerVariantRole.PrimaryAnswer || incoming == AnswerVariantRole.PrimaryAnswer)
        {
            // PrimaryAnswer is a separate, singleton preference axis — never compared/ranked here.
            throw new ArgumentException(
                "PrimaryAnswer is not part of the Required/Alias ranking; conflicting primary choices are a PreferredVariantSelectionDecision concern.");
        }

        return existing == AnswerVariantRole.RequiredAnswerVariant || incoming == AnswerVariantRole.RequiredAnswerVariant
            ? AnswerVariantRole.RequiredAnswerVariant
            : AnswerVariantRole.AcceptedAlias;
    }
}

/// <summary>
/// Stable-identity policy for <see cref="SemanticMeaningIdentity"/>. See the type's own doc comment for
/// the field list and rationale. <paramref name="canonicalTopicOrDomain"/> defaults to empty because the
/// current schema and archive format never persist topic/domain data (see architecture doc "Topic/domain
/// persistence gap") — every caller today passes the default, so this parameter exists purely so the
/// function is already correct once a future schema change adds the field.
///
/// <para><b>Revision v2</b> (KF-BACKUP-002 Slice 3 correction): <c>Translation</c> was removed from this
/// identity. Translation/answer text is an <see cref="AnswerVariantIdentity"/> concern, never a semantic
/// discriminator — including it previously caused synonyms or alternative translations of the exact same
/// sense to be misclassified as separate semantic meanings. The domain discriminator was bumped from
/// <c>.v1</c> to <c>.v2</c> so this correction can never collide with the old, incorrect hash space.</para>
///
/// <para><b>Revision v3</b> (final focused review correction): <c>Definition</c> was removed from this
/// identity for the identical reason <c>Translation</c> was removed in v2 — free-text wording, even
/// Definition wording, is not a reliable semantic-sense discriminator on its own. Hashing it
/// unconditionally caused a same-provider-sense-id pair with merely differently-worded definitions to be
/// misclassified as two distinct semantic meanings, and caused a no-discriminator pair with differing
/// Definition wording to be silently auto-split instead of raising a
/// <see cref="SemanticMeaningGroupingDecision"/>. Definition now only ever distinguishes an
/// <see cref="ExactMeaningVariantIdentity"/> (which now hashes it directly, see that type) or informs a
/// grouping decision's ambiguity check — never this identity. Domain bumped to <c>.v3</c> so this
/// correction can never collide with the v1/v2 hash spaces.</para>
/// </summary>
public static class SemanticMeaningIdentityPolicy
{
    private const string Domain = "KnownFirst.Merge.SemanticMeaning.v3";

    public static SemanticMeaningIdentity Compute(
        BackupPreparedItem preparedItem, VocabularyIdentity vocabularyIdentity, string canonicalTopicOrDomain = "")
    {
        ArgumentNullException.ThrowIfNull(preparedItem);

        var builder = new CanonicalFingerprintBuilder(Domain)
            .WriteString(vocabularyIdentity.Value)
            .WriteString(CanonicalText.CanonicalLanguageCode(preparedItem.SourceLanguage))
            .WriteString(CanonicalText.CanonicalLanguageCode(preparedItem.ExplanationLanguage))
            .WriteString(CanonicalText.NormalizeOptional(preparedItem.ProviderMeaningId))
            .WriteString(CanonicalText.NormalizeOptional(canonicalTopicOrDomain))
            .WriteString(CanonicalText.NormalizeOptional(preparedItem.GrammaticalRelationship))
            .WriteString(CanonicalText.NormalizeOptional(preparedItem.AcronymExpansion));

        return new SemanticMeaningIdentity(builder.ComputeSha256Hex());
    }

    /// <summary>
    /// True if <paramref name="preparedItem"/> carries at least one field strong enough to reliably
    /// discriminate one semantic sense from another on its own (a stable provider sense id, a persisted
    /// topic/domain, a grammatical/part-of-speech discriminator, or an acronym expansion).
    /// <b>Deliberately excludes Definition</b> (final focused review correction): free-text Definition
    /// wording — present or absent, matching or differing — is not itself trustworthy evidence of sameness
    /// or difference; only a stable provider sense id, a persisted topic/domain, or an explicit
    /// grammatical/part-of-speech discriminator qualifies. When neither side of a same-Word/same-language
    /// comparison has any such field, the two sides' <see cref="SemanticMeaningIdentity"/> values
    /// collapsing to equal is not trustworthy evidence that they really are the same sense — see
    /// <see cref="SemanticMeaningGroupingDecision"/>.
    /// </summary>
    public static bool HasReliableSenseDiscriminator(BackupPreparedItem preparedItem, string canonicalTopicOrDomain = "")
    {
        ArgumentNullException.ThrowIfNull(preparedItem);

        return !string.IsNullOrWhiteSpace(preparedItem.ProviderMeaningId)
            || !string.IsNullOrWhiteSpace(canonicalTopicOrDomain)
            || !string.IsNullOrWhiteSpace(preparedItem.GrammaticalRelationship)
            || !string.IsNullOrWhiteSpace(preparedItem.AcronymExpansion);
    }

    public static SemanticMeaningIdentity Compute(
        BackupPreparedItem preparedItem,
        IReadOnlyDictionary<string, VocabularyIdentity> vocabularyIdentitiesByArchiveId,
        string canonicalTopicOrDomain = "")
    {
        ArgumentNullException.ThrowIfNull(preparedItem);
        ArgumentNullException.ThrowIfNull(vocabularyIdentitiesByArchiveId);

        if (!vocabularyIdentitiesByArchiveId.TryGetValue(preparedItem.VocabularyId, out var vocabularyIdentity))
        {
            throw new KeyNotFoundException(
                $"No stable vocabulary identity supplied for archive vocabulary id '{preparedItem.VocabularyId}'.");
        }

        return Compute(preparedItem, vocabularyIdentity, canonicalTopicOrDomain);
    }
}

/// <summary>
/// Stable-identity policy for <see cref="ExactMeaningVariantIdentity"/>.
/// <b>Correction</b> (KF-BACKUP-002 Slice 3 focused review): earlier revisions of this hash omitted
/// <c>Translation</c> entirely, once <c>Translation</c> was removed from <see cref="SemanticMeaningIdentity"/>
/// (§"Correct SemanticMeaningIdentity") — leaving no field anywhere that protected differing translation
/// text, which contradicts this type's own purpose ("protect all persisted Meaning content"). Translation
/// is now written directly into this hash. Domain bumped to <c>.v2</c> so the fix can never collide with
/// the earlier, incomplete hash space.
///
/// <b>Correction v3</b> (final focused review): the same gap existed for <c>Definition</c> once it was
/// removed from <see cref="SemanticMeaningIdentity"/> in that type's own v3 correction — previously
/// Definition differences were only visible transitively (via the embedded, now-removed
/// <c>SemanticMeaningIdentity</c> component), so two rows differing only in Definition would have wrongly
/// collapsed to one exact duplicate/variant. Definition is now written directly into this hash, alongside
/// Translation. Domain bumped to <c>.v3</c> so this correction can never collide with the v1/v2 hash spaces.
/// </summary>
public static class ExactMeaningVariantIdentityPolicy
{
    private const string Domain = "KnownFirst.Merge.ExactMeaningVariant.v3";

    public static ExactMeaningVariantIdentity Compute(BackupPreparedItem preparedItem, SemanticMeaningIdentity semanticMeaningIdentity)
    {
        ArgumentNullException.ThrowIfNull(preparedItem);

        var builder = new CanonicalFingerprintBuilder(Domain)
            .WriteString(semanticMeaningIdentity.Value)
            .WriteString(CanonicalText.NormalizeOptional(preparedItem.DisplayTerm))
            .WriteString(CanonicalText.NormalizeOptional(preparedItem.Definition))
            .WriteString(CanonicalText.NormalizeOptional(preparedItem.Translation))
            .WriteString(CanonicalText.NormalizeOptional(preparedItem.EncounteredSurfaceForm))
            .WriteString(CanonicalText.NormalizeOptional(preparedItem.DictionaryExample))
            .WriteString(CanonicalText.NormalizeOptional(preparedItem.AdditionalNote))
            .WriteBoolean(preparedItem.ConfirmedByUser)
            .WriteString(preparedItem.Source.ProviderName)
            .WriteString(preparedItem.Source.SourceProject)
            .WriteString(preparedItem.Source.PageTitle)
            .WriteNullableInt64(preparedItem.Source.RevisionId)
            .WriteString(preparedItem.Source.Attribution);

        // Order-independent: alias list order is not semantically meaningful, so the hash is computed
        // over the ordinal-sorted distinct normalized alias set, never the archive's raw input order.
        var normalizedAliases = preparedItem.AcceptedAliases
            .Select(CanonicalText.NormalizeOptional)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(alias => alias, StringComparer.Ordinal);
        foreach (var alias in normalizedAliases)
        {
            builder.WriteString(alias);
        }

        return new ExactMeaningVariantIdentity(builder.ComputeSha256Hex());
    }
}

/// <summary>Stable-identity policy for <see cref="AnswerVariantIdentity"/>.</summary>
public static class AnswerVariantIdentityPolicy
{
    private const string Domain = "KnownFirst.Merge.AnswerVariant.v1";

    public static AnswerVariantIdentity Compute(SemanticMeaningIdentity semanticMeaningIdentity, string answerText, string answerLanguage)
    {
        ArgumentNullException.ThrowIfNull(answerText);

        var builder = new CanonicalFingerprintBuilder(Domain)
            .WriteString(semanticMeaningIdentity.Value)
            .WriteString(CanonicalText.NormalizeOptional(answerText))
            .WriteString(CanonicalText.CanonicalLanguageCode(answerLanguage));

        return new AnswerVariantIdentity(builder.ComputeSha256Hex());
    }
}

/// <summary>Stable-identity policy for <see cref="FutureCardIdentity"/> (forward-compatible primitive; see type doc comment).</summary>
public static class FutureCardIdentityPolicy
{
    private const string Domain = "KnownFirst.Merge.FutureCard.v1";

    public static FutureCardIdentity Compute(SemanticMeaningIdentity semanticMeaningIdentity, BackupCardDirection direction)
    {
        var builder = new CanonicalFingerprintBuilder(Domain)
            .WriteString(semanticMeaningIdentity.Value)
            .WriteEnum(direction);

        return new FutureCardIdentity(builder.ComputeSha256Hex());
    }
}
