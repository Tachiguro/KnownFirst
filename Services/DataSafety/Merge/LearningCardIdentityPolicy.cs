using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// LearningCard matching identity per design §4.3: (stable VocabularyIdentity, Direction) — the true,
/// schema-enforced identity (DB unique index is (WordId, Direction), not (MeaningId, Direction)). Which
/// Meaning a matched card ends up referencing is a separate, non-identity concern handled by the
/// merge writer (slice 5), not by this pure matching key.
/// </summary>
public static class LearningCardIdentityPolicy
{
    private const string Domain = "KnownFirst.Merge.LearningCard.v1";

    public static LearningCardMatchIdentity ComputeMatchIdentity(VocabularyIdentity vocabularyIdentity, BackupCardDirection direction)
    {
        var builder = new CanonicalFingerprintBuilder(Domain)
            .WriteString(vocabularyIdentity.Value)
            .WriteEnum(direction);

        return new LearningCardMatchIdentity(builder.ComputeSha256Hex());
    }

    public static LearningCardMatchIdentity ComputeMatchIdentity(
        BackupLearningCard card,
        IReadOnlyDictionary<string, VocabularyIdentity> vocabularyIdentitiesByArchiveId)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(vocabularyIdentitiesByArchiveId);

        if (!vocabularyIdentitiesByArchiveId.TryGetValue(card.VocabularyId, out var vocabularyIdentity))
        {
            throw new KeyNotFoundException(
                $"No stable vocabulary identity supplied for archive vocabulary id '{card.VocabularyId}'.");
        }

        return ComputeMatchIdentity(vocabularyIdentity, card.Direction);
    }
}
