using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Stable source-material (Document) identity per design §4.1:
/// (ContentFingerprint, canonical TextLanguage, LookupMode, canonical TargetLanguage), where
/// CanonicalTargetLanguage is forced to empty for LookupMode = Definition. Title is deliberately
/// excluded — it is free-text presentation metadata typed at import time, never derived from content.
/// </summary>
public static class SourceMaterialIdentityPolicy
{
    private const string Domain = "KnownFirst.Merge.SourceMaterial.v1";

    public static SourceMaterialIdentity Compute(BackupSourceMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);

        var canonicalTargetLanguage = material.LookupMode == BackupLexicalLookupMode.Definition
            ? string.Empty
            : CanonicalText.CanonicalLanguageCode(material.TargetLanguage);

        var builder = new CanonicalFingerprintBuilder(Domain)
            .WriteString(material.ContentSha256.ToUpperInvariant())
            .WriteString(CanonicalText.CanonicalLanguageCode(material.TextLanguage))
            .WriteEnum(material.LookupMode)
            .WriteString(canonicalTargetLanguage);

        return new SourceMaterialIdentity(builder.ComputeSha256Hex());
    }
}
