using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Stable Meaning identity per design §4.2: (stable VocabularyIdentity, canonical SourceLanguage,
/// canonical ExplanationLanguage, normalized Definition, normalized Translation,
/// provider/source-project/page-title/revision identity). Two Meanings for the same word are the same
/// entity only when every component matches exactly; any distinct non-empty Definition or Translation
/// produces a distinct identity, by design — see §5.4 ("distinct non-empty content is preserved").
/// </summary>
public static class MeaningIdentityPolicy
{
    private const string Domain = "KnownFirst.Merge.Meaning.v1";

    public static MeaningIdentity Compute(BackupPreparedItem preparedItem, VocabularyIdentity vocabularyIdentity)
    {
        ArgumentNullException.ThrowIfNull(preparedItem);

        var builder = new CanonicalFingerprintBuilder(Domain)
            .WriteString(vocabularyIdentity.Value)
            .WriteString(CanonicalText.CanonicalLanguageCode(preparedItem.SourceLanguage))
            .WriteString(CanonicalText.CanonicalLanguageCode(preparedItem.ExplanationLanguage))
            .WriteString(CanonicalText.NormalizeOptional(preparedItem.Definition))
            .WriteString(CanonicalText.NormalizeOptional(preparedItem.Translation))
            .WriteString(preparedItem.Source.ProviderName)
            .WriteString(preparedItem.Source.SourceProject)
            .WriteString(preparedItem.Source.PageTitle)
            .WriteNullableInt64(preparedItem.Source.RevisionId);

        return new MeaningIdentity(builder.ComputeSha256Hex());
    }

    /// <summary>
    /// Resolves the archive-local VocabularyId through the supplied map of already-computed stable
    /// vocabulary identities. Never derives an identity from the raw archive-local id itself.
    /// </summary>
    public static MeaningIdentity Compute(
        BackupPreparedItem preparedItem,
        IReadOnlyDictionary<string, VocabularyIdentity> vocabularyIdentitiesByArchiveId)
    {
        ArgumentNullException.ThrowIfNull(preparedItem);
        ArgumentNullException.ThrowIfNull(vocabularyIdentitiesByArchiveId);

        if (!vocabularyIdentitiesByArchiveId.TryGetValue(preparedItem.VocabularyId, out var vocabularyIdentity))
        {
            throw new KeyNotFoundException(
                $"No stable vocabulary identity supplied for archive vocabulary id '{preparedItem.VocabularyId}'.");
        }

        return Compute(preparedItem, vocabularyIdentity);
    }
}
