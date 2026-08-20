using System.Security.Cryptography;
using KnownFirst.Core.Text;
using KnownFirst.Core.Text.German;

namespace KnownFirst.Tests;

/// <summary>
/// Round-trip and determinism contract for the v2 German lexicon runtime bundle: a single
/// authoritative file carrying provenance, generation counts, and lexical data together, closed
/// under one SHA-256 trailer (see <see cref="GermanLexiconRuntimeAssetFormat"/>).
/// Malformed/corrupted bundle rejection is covered separately in
/// <see cref="GeneratedGermanLexiconMalformedBundleTests"/>.
/// </summary>
[TestClass]
public sealed class GermanLexiconBundleTests
{
    private static readonly GermanLexiconBundleProvenance SampleProvenance = new(
        UpstreamProjectName: "german-morph-dictionaries",
        UpstreamRepositoryUrl: "https://github.com/DuyguA/german-morph-dictionaries",
        UpstreamCommit: "1780890c0fd25a989201c96000af323cd201fa5c",
        UpstreamSourceAssetPath: "morf_dict.zip",
        UpstreamSourceSha256: "842e0b2f922e74afbc5961154c6e7935605ac8abbeb8af2fc83e4940db86af52",
        DataLicenseIdentifier: "CC BY-SA 4.0 (Creative Commons Attribution-ShareAlike 4.0 International)",
        ProvenanceStatement: "This runtime lexical index is processed/derived from the upstream German morphological dictionary data.");

    private static readonly GermanLexiconBundleCounts SampleCounts =
        new(10, 1, 1, 1, 4, 3, 0, 1, 2);

    private static readonly GermanLexemeEntry[] SampleLemmas =
    [
        new("Arbeit", GermanLexemeCategory.Noun),
        new("Maschine", GermanLexemeCategory.Noun),
        new("schreiben", GermanLexemeCategory.Verb),
    ];

    private static readonly GermanCompoundStemEntry[] SampleStems =
    [
        new("Schreib", "schreiben", GermanLexemeCategory.Verb),
    ];

    private static byte[] WriteBundle(
        GermanLexiconBundleProvenance provenance,
        GermanLexiconBundleCounts counts,
        IReadOnlyList<GermanLexemeEntry> lemmas,
        IReadOnlyList<GermanCompoundStemEntry> stems)
    {
        using var stream = new MemoryStream();
        GermanLexiconRuntimeAssetWriter.Write(stream, provenance, counts, lemmas, stems);
        return stream.ToArray();
    }

    private static GeneratedGermanLexicon WriteAndLoad(
        GermanLexiconBundleProvenance provenance,
        GermanLexiconBundleCounts counts,
        IReadOnlyList<GermanLexemeEntry> lemmas,
        IReadOnlyList<GermanCompoundStemEntry> stems)
    {
        var bytes = WriteBundle(provenance, counts, lemmas, stems);
        return GeneratedGermanLexicon.Load(new MemoryStream(bytes));
    }

    [TestMethod]
    public void WriteThenLoad_Provenance_SurvivesExactly()
    {
        var lexicon = WriteAndLoad(SampleProvenance, SampleCounts, SampleLemmas, SampleStems);

        Assert.AreEqual(SampleProvenance, lexicon.Provenance);
    }

    [TestMethod]
    public void WriteThenLoad_Counts_SurvivesExactly()
    {
        var lexicon = WriteAndLoad(SampleProvenance, SampleCounts, SampleLemmas, SampleStems);

        Assert.AreEqual(SampleCounts, lexicon.Counts);
    }

    [TestMethod]
    public void WriteThenLoad_LexicalLookups_SurviveExactly()
    {
        var lexicon = WriteAndLoad(SampleProvenance, SampleCounts, SampleLemmas, SampleStems);

        Assert.IsTrue(lexicon.TryLookupLemma("Maschine", out var noun));
        Assert.AreEqual("Maschine", noun!.Lemma);
        Assert.AreEqual(GermanLexemeCategory.Noun, noun.Category);

        Assert.IsTrue(lexicon.TryLookupStem("Schreib", out var stem));
        Assert.AreEqual("schreiben", stem!.Lemma);

        Assert.IsTrue(lexicon.TryLookupStem("Arbeit", out var selfStem));
        Assert.AreEqual("Arbeit", selfStem!.ComponentForm);
        Assert.AreEqual("Arbeit", selfStem.Lemma);

        Assert.IsFalse(lexicon.TryLookupLemma("Zeppelinwerft", out var unknown));
        Assert.IsNull(unknown);
        Assert.IsFalse(lexicon.TryLookupStem("Zeppelin", out var unknownStem));
        Assert.IsNull(unknownStem);
    }

    [TestMethod]
    public void TryLookupLemma_DecomposedAndPrecomposedUnicodeForms_ResolveToTheSameEntry()
    {
        var precomposedForm = "Büro";
        var lexicon = WriteAndLoad(
            SampleProvenance, SampleCounts, [new GermanLexemeEntry(precomposedForm, GermanLexemeCategory.Noun)], []);

        var decomposedQuery = "Büro";
        Assert.AreNotEqual(precomposedForm, decomposedQuery);

        Assert.IsTrue(lexicon.TryLookupLemma(decomposedQuery, out var entry));
        Assert.AreEqual(precomposedForm, entry!.Lemma);
    }

    [TestMethod]
    public void RepeatedLookups_AreDeterministic()
    {
        var lexicon = WriteAndLoad(SampleProvenance, SampleCounts, SampleLemmas, SampleStems);

        for (var i = 0; i < 5; i++)
        {
            Assert.IsTrue(lexicon.TryLookupLemma("schreiben", out var entry));
            Assert.AreEqual(GermanLexemeCategory.Verb, entry!.Category);

            Assert.IsTrue(lexicon.TryLookupStem("Schreib", out var stem));
            Assert.AreEqual("schreiben", stem!.Lemma);

            Assert.IsFalse(lexicon.TryLookupLemma("NichtVorhanden", out _));
        }
    }

    [TestMethod]
    public void Write_SameInputTwice_ProducesByteIdenticalOutput()
    {
        var first = WriteBundle(SampleProvenance, SampleCounts, SampleLemmas, SampleStems);
        var second = WriteBundle(SampleProvenance, SampleCounts, SampleLemmas, SampleStems);

        CollectionAssert.AreEqual(first, second);
    }

    [TestMethod]
    public void Write_ChecksumTrailer_ChangesWhenProvenanceChanges()
    {
        var bytesA = WriteBundle(SampleProvenance, SampleCounts, SampleLemmas, SampleStems);
        var differentProvenance = SampleProvenance with { UpstreamCommit = "0000000000000000000000000000000000000000" };
        var bytesB = WriteBundle(differentProvenance, SampleCounts, SampleLemmas, SampleStems);

        var trailerA = bytesA[^32..];
        var trailerB = bytesB[^32..];

        CollectionAssert.AreNotEqual(trailerA, trailerB);
    }

    [TestMethod]
    public void Write_ChecksumTrailer_ChangesWhenLexicalDataChanges()
    {
        var bytesA = WriteBundle(SampleProvenance, SampleCounts, SampleLemmas, SampleStems);
        var differentLemmas = new[] { new GermanLexemeEntry("Zimmer", GermanLexemeCategory.Noun) };
        var bytesB = WriteBundle(SampleProvenance, SampleCounts, differentLemmas, SampleStems);

        var trailerA = bytesA[^32..];
        var trailerB = bytesB[^32..];

        CollectionAssert.AreNotEqual(trailerA, trailerB);
    }

    [TestMethod]
    public void Write_ChecksumTrailer_EqualsSha256OfEverythingBeforeIt()
    {
        var bytes = WriteBundle(SampleProvenance, SampleCounts, SampleLemmas, SampleStems);

        var expected = SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32));
        var actualTrailer = bytes[^32..];

        CollectionAssert.AreEqual(expected, actualTrailer);
    }
}
