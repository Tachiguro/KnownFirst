namespace KnownFirst.Core.Text.German;

/// <summary>
/// Machine-readable provenance for a German lexicon runtime bundle, embedded inside the bundle
/// itself (see <see cref="GermanLexiconRuntimeAssetFormat"/>) so that lexical data and its
/// provenance can never be paired from two different generations.
///
/// KnownFirst source code (Apache-2.0). The data this record describes is processed/derived
/// from upstream CC BY-SA 4.0 German morphological data; see the sibling
/// <c>Assets/NOTICE.md</c> for provenance.
/// </summary>
public sealed record GermanLexiconBundleProvenance(
    string UpstreamProjectName,
    string UpstreamRepositoryUrl,
    string UpstreamCommit,
    string UpstreamSourceAssetPath,
    string UpstreamSourceSha256,
    string DataLicenseIdentifier,
    string ProvenanceStatement);
