using System.Globalization;
using System.Text.Json;
using KnownFirst.Core.Text;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety.Merge;

namespace KnownFirst.Data.Migrations.Schema8;

/// <summary>
/// Pure, connection-free semantic decision functions shared between the live
/// <see cref="Schema8DormantMigration"/> and <c>BackupArchiveV1UpgradePolicy</c> (archive format v1→v2
/// in-memory upgrade, KF-MEANING-001 Slice 2), so both consumers use the identical Sense-grouping and
/// preferred-candidate-selection rules rather than two independently-written implementations that could
/// silently drift apart. No SQLite, no I/O, no <see cref="Guid.NewGuid"/>, no current-time dependency.
///
/// <para><b>Source-order key.</b> Every decision here that breaks a content tie (which Meaning becomes a
/// group's representative; which Meaning "wins" as an AnswerVariant's SourceMeaning provenance when two
/// grouped Meanings share identical deduplicated text; which Meaning is chosen for a direction's preferred
/// assignment when no single candidate is forced) depends on the <em>order</em> the caller's Meaning list
/// is already sorted in — this policy never re-derives that order itself. The live migration sorts by
/// preserved local <c>Meaning.Id</c> (its own unchanged, existing behavior); the archive upgrader sorts by
/// ordinal v1 Meaning archive id, because a v1 archive never preserved the live database's local numeric
/// ids. These are genuinely different total orders in general — a v1 archive cannot always reconstruct
/// exactly which specific Meaning the live migration would have picked when a real content tie exists.
/// What both orders <em>do</em> guarantee identically is: when no tie exists, both converge on the same
/// functional outcome; when a tie does exist, the two candidates are by definition content-identical, so
/// the resulting AnswerVariant/preferred-assignment <em>text</em> is the same regardless of which specific
/// tied Meaning row "won" internally. See <c>docs/architecture/meaning-centric-learning-v1-design.md</c>
/// §5 for the corresponding architecture-doc correction.</para>
/// </summary>
internal static class Schema8SemanticUpgradePolicy
{
    public static SenseStatus TranslateInitialSenseStatus(WordStatus status) => status switch
    {
        WordStatus.Prepared => SenseStatus.Prepared,
        WordStatus.Learning => SenseStatus.Learning,
        WordStatus.Mastered => SenseStatus.Mastered,
        _ => SenseStatus.Prepared
    };

    public static VocabularyIdentity ComputeVocabularyIdentity(LegacyWordRow word)
    {
        var resolution = VocabularyIdentityPolicy.Resolve(word.CanonicalTerm, word.TokenKind, word.Language);
        return VocabularyMergeIdentityPolicy.Compute(word.Language, resolution.Identity);
    }

    public static BackupPreparedItem BuildPreparedItem(LegacyMeaningRow meaning) =>
        new(
            Id: meaning.Id.ToString(CultureInfo.InvariantCulture),
            VocabularyId: string.Empty,
            SourceLanguage: meaning.SourceLanguage,
            ExplanationLanguage: meaning.ExplanationLanguage,
            DisplayTerm: meaning.DisplayTerm,
            EncounteredSurfaceForm: meaning.EncounteredSurfaceForm,
            GrammaticalRelationship: meaning.GrammaticalRelationship,
            TokenKind: (BackupTokenKind)(int)meaning.TokenKind,
            ProviderMeaningId: meaning.SelectedMeaningId,
            AcronymExpansion: meaning.AcronymExpansion,
            Translation: meaning.Translation,
            Definition: meaning.Definition,
            DictionaryExample: meaning.DictionaryExample,
            AdditionalNote: meaning.AdditionalNote,
            LegacyAnswerText: null,
            AcceptedAliases: DeserializeAliases(meaning.AcceptedAliasesJson),
            ConfirmedByUser: meaning.ConfirmedByUser,
            Source: new BackupSourceReference(meaning.Source, meaning.SourceProject, meaning.SourcePageTitle, meaning.SourceRevisionId, meaning.Attribution),
            CreatedAtUtc: meaning.CreatedAt,
            UpdatedAtUtc: meaning.UpdatedAt,
            PreparedAtUtc: meaning.CreatedAt,
            Contexts: []);

    public static string[] DeserializeAliases(string json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json)
                ? []
                : JsonSerializer.Deserialize(json, KnownFirst.Core.Preparation.LexicalJsonSerializerContext.Default.StringArray) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Groups <paramref name="meanings"/> into Senses using the same conservative, split-not-merge
    /// discriminator policy the live migration always used: two Meanings merge into one Sense only when
    /// both carry a reliable discriminator (provider sense id, topic/domain, grammatical relationship, or
    /// acronym expansion) and their computed <see cref="SemanticMeaningIdentity"/> agrees; every other case
    /// splits into a separate Sense. <paramref name="meanings"/> must already be in the caller's chosen
    /// source-order (see type doc comment) — this function never reorders its input, it only partitions it.
    /// </summary>
    public static List<List<LegacyMeaningRow>> GroupMeaningsIntoSenses(
        IReadOnlyList<LegacyMeaningRow> meanings, VocabularyIdentity vocabularyIdentity)
    {
        var groups = new List<List<LegacyMeaningRow>>();
        var groupKeys = new List<string?>();

        foreach (var meaning in meanings)
        {
            var preparedItem = BuildPreparedItem(meaning);
            var hasDiscriminator = SemanticMeaningIdentityPolicy.HasReliableSenseDiscriminator(preparedItem);
            var identity = SemanticMeaningIdentityPolicy.Compute(preparedItem, vocabularyIdentity);

            if (hasDiscriminator)
            {
                var existingGroupIndex = groupKeys.FindIndex(k => k == identity.Value);
                if (existingGroupIndex >= 0)
                {
                    groups[existingGroupIndex].Add(meaning);
                    continue;
                }
            }

            groups.Add([meaning]);
            groupKeys.Add(hasDiscriminator ? identity.Value : null);
        }

        return groups;
    }

    /// <summary>
    /// Chooses the deterministic preferred-assignment candidate for one Sense group and one card
    /// direction: the first (in the caller's source-order) Meaning in <paramref name="group"/> that
    /// carries a non-empty expression for <paramref name="textSelector"/>, tie-broken by lowest normalized
    /// text (ordinal). Returns <see langword="null"/> when no Meaning in the group has any expression for
    /// this direction — a preferred assignment must never be invented (Focused invariant 2).
    /// </summary>
    public static LegacyMeaningRow? SelectPreferredCandidate(
        IReadOnlyList<LegacyMeaningRow> group,
        Func<LegacyMeaningRow, string> textSelector)
    {
        var candidates = group
            .Select((meaning, index) => (Meaning: meaning, SourceOrderIndex: index))
            .Where(entry => !string.IsNullOrEmpty(textSelector(entry.Meaning)))
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates
            .OrderBy(entry => entry.SourceOrderIndex)
            .ThenBy(entry => CanonicalText.NormalizeOptional(textSelector(entry.Meaning)), StringComparer.Ordinal)
            .First()
            .Meaning;
    }
}
