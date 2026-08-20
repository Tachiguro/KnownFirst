namespace KnownFirst.Core.Text.German;

/// <summary>
/// Binary layout constants for the single authoritative, compact, offline, AOT/trimming-safe
/// German lexicon runtime bundle produced by the German lexicon maintenance tool and consumed
/// by <see cref="GeneratedGermanLexicon"/>.
///
/// Format version 2 carries lexical data and its machine-readable provenance together in one
/// file, closed under one trailing SHA-256, so lexical data from one generation can never be
/// paired with provenance from another: there is exactly one file, and it is either fully valid
/// or rejected outright. Version 1 (lexical-data-only, with a separate JSON manifest) is
/// retired; this reader does not accept it.
///
/// Layout (all multi-byte integers little-endian):
/// <code>
/// header:
///   4 bytes  magic "KFGL"
///   2 bytes  format version (ushort) = 2
/// provenance section, fixed field order, each a length-prefixed UTF-8 string
/// (2-byte ushort length + N bytes), matching GermanLexiconBundleProvenance's constructor order:
///   UpstreamProjectName, UpstreamRepositoryUrl, UpstreamCommit, UpstreamSourceAssetPath,
///   UpstreamSourceSha256, DataLicenseIdentifier, ProvenanceStatement
/// counts section, fixed field order, each an int32, matching GermanLexiconBundleCounts's
/// constructor order:
///   TotalDistinctWordForms, AmbiguousWordForms, UnsupportedCategoryWordForms,
///   InflectedFormWordFormsExcluded, UnambiguousBaseFormLemmaEntries,
///   ImperativeSingularCandidateForms, AmbiguousImperativeSingularForms,
///   DerivedVerbStemCollisionExclusions, DerivedVerbStemEntries
/// lexical payload:
///   4 bytes  lemma entry count (int32)
///   4 bytes  stem entry count (int32)
///   lemma table, sorted ascending by the UTF-8 bytes of Form (ordinal), no duplicates:
///     repeated lemma entry count times:
///       2 bytes  UTF-8 byte length of Form (ushort)
///       N bytes  UTF-8 bytes of Form
///       1 byte   GermanLexemeCategory code
///   stem table (verb-imperative-derived compound stems only), sorted ascending by the UTF-8
///   bytes of ComponentForm (ordinal), no duplicates:
///     repeated stem entry count times:
///       2 bytes  UTF-8 byte length of ComponentForm (ushort)
///       N bytes  UTF-8 bytes of ComponentForm
///       2 bytes  UTF-8 byte length of Lemma (ushort)
///       N bytes  UTF-8 bytes of Lemma
///       1 byte   GermanLexemeCategory code
/// trailer:
///   32 bytes  SHA-256 over every preceding byte (magic through the last stem entry)
/// </code>
///
/// Noun compound stems are not duplicated in the stem table: a noun lemma is always its own
/// compound stem, so <see cref="GeneratedGermanLexicon"/> falls back to the lemma table for
/// stem lookups that are not present in the stem table.
/// </summary>
public static class GermanLexiconRuntimeAssetFormat
{
    internal static readonly byte[] Magic = "KFGL"u8.ToArray();

    public const ushort CurrentVersion = 2;

    public const int ChecksumTrailerLength = 32;
}
