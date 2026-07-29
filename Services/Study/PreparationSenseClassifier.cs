using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety.Merge;

namespace KnownFirst.Services.Study;

/// <summary>
/// The discriminator fields <see cref="PreparationSenseClassifier"/> compares — either a live provider
/// candidate (from a <see cref="KnownFirst.Core.Preparation.LexicalMeaning"/>) or an existing persisted
/// Sense row. <see cref="TopicOrDomain"/> is user-entered at Accept time (KF-MEANING-001 Slice 3 — the
/// architecture doc's forward-compatible parameter is finally populated here for the first time).
/// </summary>
public readonly record struct SenseDiscriminatorFacts(
    string SourceLanguage,
    string ExplanationLanguage,
    string ProviderSenseId,
    string TopicOrDomain,
    string GrammaticalRelationship,
    string AcronymExpansion);

/// <summary>
/// The three-valued outcome of comparing a candidate provider Meaning against one existing Sense
/// (KF-MEANING-001 Slice 3, architecture doc line "the future preparation selector must compare the
/// context/provider candidate against existing Senses ... using the same matching-key/
/// HasReliableSenseDiscriminator policy already defined for migration grouping").
/// </summary>
public enum SenseMatchOutcome
{
    /// <summary>Both sides carry a reliable discriminator and their computed identity agrees — the same Sense.</summary>
    Equal,

    /// <summary>Both sides carry a reliable discriminator but their computed identity disagrees — definitely a different Sense.</summary>
    Conflict,

    /// <summary>
    /// At least one side carries no reliable discriminator — sameness cannot be determined; the migration's
    /// own split-not-merge rule applies (never guess, never block) — a new candidate Sense is surfaced for
    /// review rather than silently merged or silently rejected.
    /// </summary>
    Unknown
}

/// <summary>
/// Pure, connection-free three-valued Sense matcher (KF-MEANING-001 Slice 3). Reuses the exact same
/// <see cref="SemanticMeaningIdentityPolicy"/>/<c>HasReliableSenseDiscriminator</c> policy the dormant
/// Schema 7→8 migration already uses for grouping (<see cref="Schema8SemanticUpgradePolicy"/>), so live
/// preparation-time Sense matching and offline migration grouping can never silently drift apart.
/// </summary>
public static class PreparationSenseClassifier
{
    public static SenseMatchOutcome Classify(
        string vocabularyLanguage,
        string vocabularyIdentityKey,
        SenseDiscriminatorFacts candidate,
        SenseDiscriminatorFacts existing)
    {
        var vocabularyIdentity = VocabularyMergeIdentityPolicy.Compute(vocabularyLanguage, vocabularyIdentityKey);
        var candidateItem = ToPreparedItem(candidate);
        var existingItem = ToPreparedItem(existing);

        var candidateHasDiscriminator = SemanticMeaningIdentityPolicy.HasReliableSenseDiscriminator(
            candidateItem, candidate.TopicOrDomain);
        var existingHasDiscriminator = SemanticMeaningIdentityPolicy.HasReliableSenseDiscriminator(
            existingItem, existing.TopicOrDomain);
        if (!candidateHasDiscriminator || !existingHasDiscriminator)
        {
            return SenseMatchOutcome.Unknown;
        }

        var candidateIdentity = SemanticMeaningIdentityPolicy.Compute(candidateItem, vocabularyIdentity, candidate.TopicOrDomain);
        var existingIdentity = SemanticMeaningIdentityPolicy.Compute(existingItem, vocabularyIdentity, existing.TopicOrDomain);
        return candidateIdentity == existingIdentity ? SenseMatchOutcome.Equal : SenseMatchOutcome.Conflict;
    }

    /// <summary>
    /// Classifies <paramref name="candidate"/> against every <paramref name="existingSenses"/> row for the
    /// same Word and returns the first <see cref="SenseMatchOutcome.Equal"/> match, if any — the migration's
    /// grouping is itself first-match, not best-match, so live matching stays consistent with it.
    /// </summary>
    public static (SenseRow? Match, SenseMatchOutcome Outcome) ClassifyAgainstExisting(
        string vocabularyLanguage,
        string vocabularyIdentityKey,
        SenseDiscriminatorFacts candidate,
        IReadOnlyList<SenseRow> existingSenses)
    {
        var sawUnknown = false;
        foreach (var sense in existingSenses)
        {
            var outcome = Classify(
                vocabularyLanguage,
                vocabularyIdentityKey,
                candidate,
                new SenseDiscriminatorFacts(
                    sense.SourceLanguage,
                    sense.ExplanationLanguage,
                    sense.ProviderSenseId,
                    sense.TopicOrDomain,
                    sense.GrammaticalRelationship,
                    sense.AcronymExpansion));

            if (outcome == SenseMatchOutcome.Equal)
            {
                return (sense, SenseMatchOutcome.Equal);
            }

            if (outcome == SenseMatchOutcome.Unknown)
            {
                sawUnknown = true;
            }
        }

        // No Equal match. Unknown (ambiguous — cannot rule out a hidden duplicate) outranks Conflict
        // (every comparison was a confident, discriminator-backed "definitely different"); no existing
        // Senses at all is trivially Unknown (nothing to conflict with).
        if (sawUnknown || existingSenses.Count == 0)
        {
            return (null, SenseMatchOutcome.Unknown);
        }

        return (null, SenseMatchOutcome.Conflict);
    }

    private static BackupPreparedItem ToPreparedItem(SenseDiscriminatorFacts facts) => new(
        Id: string.Empty,
        VocabularyId: string.Empty,
        SourceLanguage: facts.SourceLanguage,
        ExplanationLanguage: facts.ExplanationLanguage,
        DisplayTerm: string.Empty,
        EncounteredSurfaceForm: string.Empty,
        GrammaticalRelationship: facts.GrammaticalRelationship,
        TokenKind: BackupTokenKind.Word,
        ProviderMeaningId: facts.ProviderSenseId,
        AcronymExpansion: facts.AcronymExpansion,
        Translation: string.Empty,
        Definition: string.Empty,
        DictionaryExample: string.Empty,
        AdditionalNote: string.Empty,
        LegacyAnswerText: null,
        AcceptedAliases: [],
        ConfirmedByUser: false,
        Source: new BackupSourceReference(string.Empty, string.Empty, string.Empty, null, string.Empty),
        CreatedAtUtc: default,
        UpdatedAtUtc: default,
        PreparedAtUtc: default,
        Contexts: []);
}
