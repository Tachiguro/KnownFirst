using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Computes the stable cross-device vocabulary identity from the archive's own already-computed
/// identity key (KnownFirst.Core.Text.VocabularyIdentityPolicy's output, persisted at export time as
/// BackupVocabularyItem.IdentityKey) plus a canonicalized language code. This deliberately does not
/// recompute word normalization — per design §"Vocabulary", the authoritative normalization algorithm
/// already lives in KnownFirst.Core.Text.VocabularyIdentityPolicy and must not be duplicated here.
/// </summary>
public static class VocabularyMergeIdentityPolicy
{
    private const string Domain = "KnownFirst.Merge.Vocabulary.v1";

    public static VocabularyIdentity Compute(BackupVocabularyItem vocabulary)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);
        return Compute(vocabulary.Language, vocabulary.IdentityKey);
    }

    public static VocabularyIdentity Compute(string language, string identityKey)
    {
        ArgumentNullException.ThrowIfNull(identityKey);

        var builder = new CanonicalFingerprintBuilder(Domain)
            .WriteString(CanonicalText.CanonicalLanguageCode(language))
            .WriteString(identityKey);

        return new VocabularyIdentity(builder.ComputeSha256Hex());
    }
}
