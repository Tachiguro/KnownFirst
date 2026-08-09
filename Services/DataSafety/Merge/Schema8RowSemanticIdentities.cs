using System.Globalization;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema8;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Turns raw Schema-8/9/10 physical rows into the stable semantic identities the merge identity policies
/// are defined over. Exists so there is exactly <em>one</em> definition of "what the semantic material of
/// a persisted Sense is" — <see cref="MergeWriterTargetIndex"/> (merge into a populated target) and
/// <c>Schema10LearningIdentityBootstrap</c> (the one-time Schema-10 identity backfill) must derive the
/// identical <see cref="FutureCardIdentity"/> for the same physical card, or a workflow bootstrapped at
/// migration time would not match the same workflow arriving later through an archive.
///
/// <para>No identity is computed here: every value comes from calling
/// <see cref="VocabularyMergeIdentityPolicy"/>, <see cref="SemanticMeaningIdentityPolicy"/>, and
/// <see cref="FutureCardIdentityPolicy"/>. This type only performs the raw-row field plumbing those
/// policies need.</para>
/// </summary>
public static class Schema8RowSemanticIdentities
{
    /// <summary>Projects one persisted <see cref="SenseRow"/> onto the archive shape the identity policies read.</summary>
    public static BackupSense ToBackupSense(SenseRow sense)
    {
        ArgumentNullException.ThrowIfNull(sense);

        return new BackupSense(
            sense.Id.ToString(CultureInfo.InvariantCulture),
            sense.StableId,
            string.Empty,
            sense.SourceLanguage,
            sense.ExplanationLanguage,
            sense.ProviderSenseId,
            sense.TopicOrDomain,
            sense.PartOfSpeech,
            sense.GrammaticalRelationship,
            sense.AcronymExpansion,
            null,
            (BackupSenseStatus)(int)sense.Status,
            EnsureUtc(sense.CreatedAtUtc),
            EnsureUtc(sense.UpdatedAtUtc));
    }

    /// <summary>
    /// The <see cref="FutureCardIdentity"/> of every supplied learning card, keyed by its physical row id.
    /// <paramref name="onUnresolvableCard"/> decides what an unresolvable card means to the caller: the
    /// merge writer treats it as a corrupt archive reference, the Schema-10 migration treats it as an
    /// invariant violation that rolls the migration back.
    /// </summary>
    public static Dictionary<int, FutureCardIdentity> ComputeFutureCardIdentitiesByCardId(
        IReadOnlyList<Data.Entities.WordEntity> words,
        IReadOnlyList<SenseRow> senses,
        IReadOnlyList<Schema8CardRow> cards,
        Func<int, Exception> onUnresolvableCard)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(senses);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(onUnresolvableCard);

        var vocabularyIdentityByWordId = new Dictionary<int, VocabularyIdentity>();
        foreach (var word in words)
        {
            vocabularyIdentityByWordId[word.Id] = VocabularyMergeIdentityPolicy.Compute(word.Language, word.NormalizedTerm);
        }

        var semanticIdentityBySenseId = new Dictionary<int, SemanticMeaningIdentity>();
        foreach (var sense in senses)
        {
            if (!vocabularyIdentityByWordId.TryGetValue(sense.WordId, out var vocabularyIdentity))
            {
                continue;
            }

            semanticIdentityBySenseId[sense.Id] = SemanticMeaningIdentityPolicy.Compute(ToBackupSense(sense), vocabularyIdentity);
        }

        var identityByCardId = new Dictionary<int, FutureCardIdentity>();
        foreach (var card in cards)
        {
            if (card.SenseId is not { } senseId || !semanticIdentityBySenseId.TryGetValue(senseId, out var semanticIdentity))
            {
                throw onUnresolvableCard(card.Id);
            }

            identityByCardId[card.Id] = FutureCardIdentityPolicy.Compute(
                semanticIdentity, BackupEnumMappings.ToBackup(card.Direction));
        }

        return identityByCardId;
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
