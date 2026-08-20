using System.Security.Cryptography;
using System.Text;

namespace KnownFirst.Core.Text.German;

/// <summary>
/// Deterministically serializes a single authoritative German lexicon runtime bundle (format
/// version 2) to the binary format described by <see cref="GermanLexiconRuntimeAssetFormat"/>:
/// provenance, generation counts, and the lexical index together, closed under one trailing
/// SHA-256 covering everything that precedes it.
///
/// KnownFirst source code (Apache-2.0). Writes only; carries no knowledge of the upstream
/// data source beyond the provenance values it is given.
/// </summary>
public static class GermanLexiconRuntimeAssetWriter
{
    /// <summary>
    /// Writes the bundle to <paramref name="destination"/>. <paramref name="lemmas"/> must
    /// already be sorted ascending by <see cref="GermanLexemeEntry.Lemma"/> using ordinal UTF-8
    /// byte order, and <paramref name="stems"/> ascending by
    /// <see cref="GermanCompoundStemEntry.ComponentForm"/> the same way. No sorting or
    /// deduplication is performed here, so byte-identical output requires byte-identical,
    /// already-normalized input.
    /// </summary>
    public static void Write(
        Stream destination,
        GermanLexiconBundleProvenance provenance,
        GermanLexiconBundleCounts counts,
        IReadOnlyList<GermanLexemeEntry> lemmas,
        IReadOnlyList<GermanCompoundStemEntry> stems)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(lemmas);
        ArgumentNullException.ThrowIfNull(stems);

        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(GermanLexiconRuntimeAssetFormat.Magic);
            writer.Write(GermanLexiconRuntimeAssetFormat.CurrentVersion);

            WriteForm(writer, provenance.UpstreamProjectName);
            WriteForm(writer, provenance.UpstreamRepositoryUrl);
            WriteForm(writer, provenance.UpstreamCommit);
            WriteForm(writer, provenance.UpstreamSourceAssetPath);
            WriteForm(writer, provenance.UpstreamSourceSha256);
            WriteForm(writer, provenance.DataLicenseIdentifier);
            WriteForm(writer, provenance.ProvenanceStatement);

            writer.Write(counts.TotalDistinctWordForms);
            writer.Write(counts.AmbiguousWordForms);
            writer.Write(counts.UnsupportedCategoryWordForms);
            writer.Write(counts.InflectedFormWordFormsExcluded);
            writer.Write(counts.UnambiguousBaseFormLemmaEntries);
            writer.Write(counts.ImperativeSingularCandidateForms);
            writer.Write(counts.AmbiguousImperativeSingularForms);
            writer.Write(counts.DerivedVerbStemCollisionExclusions);
            writer.Write(counts.DerivedVerbStemEntries);

            writer.Write(lemmas.Count);
            writer.Write(stems.Count);

            foreach (var lemma in lemmas)
            {
                WriteForm(writer, lemma.Lemma);
                writer.Write(CategoryCode(lemma.Category));
            }

            foreach (var stem in stems)
            {
                WriteForm(writer, stem.ComponentForm);
                WriteForm(writer, stem.Lemma);
                writer.Write(CategoryCode(stem.Category));
            }

            writer.Flush();
        }

        var payload = buffer.ToArray();
        var checksum = SHA256.HashData(payload);

        destination.Write(payload, 0, payload.Length);
        destination.Write(checksum, 0, checksum.Length);
    }

    private static void WriteForm(BinaryWriter writer, string form)
    {
        var bytes = Encoding.UTF8.GetBytes(form);
        if (bytes.Length > ushort.MaxValue)
        {
            throw new InvalidOperationException(
                $"German lexicon bundle value '{form}' exceeds the maximum encodable UTF-8 byte length.");
        }

        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    private static byte CategoryCode(GermanLexemeCategory category) => category switch
    {
        GermanLexemeCategory.Noun => 1,
        GermanLexemeCategory.Verb => 2,
        GermanLexemeCategory.Adjective => 3,
        _ => throw new ArgumentOutOfRangeException(
            nameof(category), category, "Unsupported German lexeme category for the runtime bundle."),
    };
}
