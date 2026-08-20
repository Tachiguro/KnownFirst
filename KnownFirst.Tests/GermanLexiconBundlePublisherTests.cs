using KnownFirst.Core.Text;
using KnownFirst.Core.Text.German;
using KnownFirst.Tools.GermanLexicon;

namespace KnownFirst.Tests;

/// <summary>
/// F2 publication-consistency contract for <see cref="GermanLexiconBundlePublisher"/>: the
/// single authoritative bundle file must never be visible at its stable path in a partial or
/// unvalidated state, and a failed publish attempt must never disturb a previously accepted
/// bundle.
/// </summary>
[TestClass]
public sealed class GermanLexiconBundlePublisherTests
{
    private const string FileName = "german-lexicon.v2.kfgl";

    private static readonly GermanLexiconBundleProvenance SampleProvenance = new(
        UpstreamProjectName: "german-morph-dictionaries",
        UpstreamRepositoryUrl: "https://github.com/DuyguA/german-morph-dictionaries",
        UpstreamCommit: "1780890c0fd25a989201c96000af323cd201fa5c",
        UpstreamSourceAssetPath: "morf_dict.zip",
        UpstreamSourceSha256: "842e0b2f922e74afbc5961154c6e7935605ac8abbeb8af2fc83e4940db86af52",
        DataLicenseIdentifier: "CC BY-SA 4.0 (Creative Commons Attribution-ShareAlike 4.0 International)",
        ProvenanceStatement: "This runtime lexical index is processed/derived from the upstream German morphological dictionary data.");

    private static readonly GermanLexiconBundleCounts SampleCounts = new(10, 1, 1, 1, 4, 3, 0, 1, 2);

    private string _outDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _outDir = Path.Combine(Path.GetTempPath(), "kf-bundle-publisher-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outDir);
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_outDir))
        {
            Directory.Delete(_outDir, recursive: true);
        }
    }

    private static void WriteValidBundle(Stream stream, GermanLexemeEntry[] lemmas) =>
        GermanLexiconRuntimeAssetWriter.Write(stream, SampleProvenance, SampleCounts, lemmas, []);

    [TestMethod]
    public void Publish_ValidBundle_CreatesFileWithMatchingLexicalDataAndProvenance()
    {
        var lemmas = new[] { new GermanLexemeEntry("Arbeit", GermanLexemeCategory.Noun) };

        var lexicon = GermanLexiconBundlePublisher.Publish(
            _outDir, FileName, s => WriteValidBundle(s, lemmas), SampleProvenance, SampleCounts);

        Assert.IsTrue(File.Exists(Path.Combine(_outDir, FileName)));
        Assert.AreEqual(SampleProvenance, lexicon.Provenance);
        Assert.IsTrue(lexicon.TryLookupLemma("Arbeit", out var entry));
        Assert.AreEqual(GermanLexemeCategory.Noun, entry!.Category);

        var reloaded = GeneratedGermanLexicon.LoadFromFile(Path.Combine(_outDir, FileName));
        Assert.IsTrue(reloaded.TryLookupLemma("Arbeit", out _));
        Assert.AreEqual(SampleProvenance, reloaded.Provenance);
    }

    [TestMethod]
    public void Publish_Success_LeavesNoTemporaryStagingFilesBehind()
    {
        var lemmas = new[] { new GermanLexemeEntry("Arbeit", GermanLexemeCategory.Noun) };

        GermanLexiconBundlePublisher.Publish(
            _outDir, FileName, s => WriteValidBundle(s, lemmas), SampleProvenance, SampleCounts);

        var files = Directory.GetFiles(_outDir);
        CollectionAssert.AreEqual(new[] { Path.Combine(_outDir, FileName) }, files);
    }

    [TestMethod]
    public void Publish_Success_CompletelyReplacesPreviousBundle()
    {
        var lemmasA = new[] { new GermanLexemeEntry("Arbeit", GermanLexemeCategory.Noun) };
        var lemmasB = new[] { new GermanLexemeEntry("Zimmer", GermanLexemeCategory.Noun) };

        GermanLexiconBundlePublisher.Publish(
            _outDir, FileName, s => WriteValidBundle(s, lemmasA), SampleProvenance, SampleCounts);
        GermanLexiconBundlePublisher.Publish(
            _outDir, FileName, s => WriteValidBundle(s, lemmasB), SampleProvenance, SampleCounts);

        var final = GeneratedGermanLexicon.LoadFromFile(Path.Combine(_outDir, FileName));
        Assert.IsFalse(final.TryLookupLemma("Arbeit", out _));
        Assert.IsTrue(final.TryLookupLemma("Zimmer", out _));
    }

    [TestMethod]
    public void Publish_WriteDelegateThrowsPartway_LeavesExistingAcceptedBundleByteIdentical()
    {
        var lemmas = new[] { new GermanLexemeEntry("Arbeit", GermanLexemeCategory.Noun) };
        GermanLexiconBundlePublisher.Publish(
            _outDir, FileName, s => WriteValidBundle(s, lemmas), SampleProvenance, SampleCounts);
        var finalPath = Path.Combine(_outDir, FileName);
        var acceptedBytesBefore = File.ReadAllBytes(finalPath);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            GermanLexiconBundlePublisher.Publish(_outDir, FileName, s =>
            {
                s.Write([1, 2, 3, 4], 0, 4);
                throw new InvalidOperationException("simulated interruption partway through writing");
            },
            SampleProvenance,
            SampleCounts));

        var acceptedBytesAfter = File.ReadAllBytes(finalPath);
        CollectionAssert.AreEqual(acceptedBytesBefore, acceptedBytesAfter);

        var remainingFiles = Directory.GetFiles(_outDir);
        CollectionAssert.AreEqual(new[] { finalPath }, remainingFiles);
    }

    [TestMethod]
    public void Publish_WriteDelegateProducesInvalidBundle_LeavesExistingAcceptedBundleByteIdentical()
    {
        var lemmas = new[] { new GermanLexemeEntry("Arbeit", GermanLexemeCategory.Noun) };
        GermanLexiconBundlePublisher.Publish(
            _outDir, FileName, s => WriteValidBundle(s, lemmas), SampleProvenance, SampleCounts);
        var finalPath = Path.Combine(_outDir, FileName);
        var acceptedBytesBefore = File.ReadAllBytes(finalPath);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            GermanLexiconBundlePublisher.Publish(_outDir, FileName, s =>
            {
                var garbage = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 };
                s.Write(garbage, 0, garbage.Length);
            },
            SampleProvenance,
            SampleCounts));

        var acceptedBytesAfter = File.ReadAllBytes(finalPath);
        CollectionAssert.AreEqual(acceptedBytesBefore, acceptedBytesAfter);

        var remainingFiles = Directory.GetFiles(_outDir);
        CollectionAssert.AreEqual(new[] { finalPath }, remainingFiles);
    }

    [TestMethod]
    public void Publish_FirstAttemptFails_NoBundleIsAcceptedAtStablePathAtAll()
    {
        Assert.ThrowsExactly<InvalidDataException>(() =>
            GermanLexiconBundlePublisher.Publish(_outDir, FileName, s =>
            {
                var garbage = new byte[] { 0, 1, 2, 3 };
                s.Write(garbage, 0, garbage.Length);
            },
            SampleProvenance,
            SampleCounts));

        Assert.IsFalse(File.Exists(Path.Combine(_outDir, FileName)));
        Assert.AreEqual(0, Directory.GetFiles(_outDir).Length);
    }

    // ----- F2-2 correction: semantic cross-check against the caller's intended provenance/counts -----

    [TestMethod]
    public void Publish_StagedBundleProvenanceDiffersFromIntended_ThrowsAndLeavesExistingAcceptedBundleByteIdentical()
    {
        var lemmas = new[] { new GermanLexemeEntry("Arbeit", GermanLexemeCategory.Noun) };
        GermanLexiconBundlePublisher.Publish(
            _outDir, FileName, s => WriteValidBundle(s, lemmas), SampleProvenance, SampleCounts);
        var finalPath = Path.Combine(_outDir, FileName);
        var acceptedBytesBefore = File.ReadAllBytes(finalPath);

        var conflictingProvenance = SampleProvenance with { UpstreamCommit = "0000000000000000000000000000000000000000" };

        Assert.ThrowsExactly<InvalidDataException>(() =>
            GermanLexiconBundlePublisher.Publish(
                _outDir,
                FileName,
                // Structurally valid, checksum-valid bundle — but written with provenance B while
                // the caller declares it intends provenance A (SampleProvenance) below.
                s => GermanLexiconRuntimeAssetWriter.Write(s, conflictingProvenance, SampleCounts, lemmas, []),
                SampleProvenance,
                SampleCounts));

        var acceptedBytesAfter = File.ReadAllBytes(finalPath);
        CollectionAssert.AreEqual(acceptedBytesBefore, acceptedBytesAfter);

        var remainingFiles = Directory.GetFiles(_outDir);
        CollectionAssert.AreEqual(new[] { finalPath }, remainingFiles);
    }

    [TestMethod]
    public void Publish_StagedBundleCountsDifferFromIntended_ThrowsAndLeavesExistingAcceptedBundleByteIdentical()
    {
        var lemmas = new[] { new GermanLexemeEntry("Arbeit", GermanLexemeCategory.Noun) };
        GermanLexiconBundlePublisher.Publish(
            _outDir, FileName, s => WriteValidBundle(s, lemmas), SampleProvenance, SampleCounts);
        var finalPath = Path.Combine(_outDir, FileName);
        var acceptedBytesBefore = File.ReadAllBytes(finalPath);

        var conflictingCounts = SampleCounts with { TotalDistinctWordForms = SampleCounts.TotalDistinctWordForms + 1 };

        Assert.ThrowsExactly<InvalidDataException>(() =>
            GermanLexiconBundlePublisher.Publish(
                _outDir,
                FileName,
                s => GermanLexiconRuntimeAssetWriter.Write(s, SampleProvenance, conflictingCounts, lemmas, []),
                SampleProvenance,
                SampleCounts));

        var acceptedBytesAfter = File.ReadAllBytes(finalPath);
        CollectionAssert.AreEqual(acceptedBytesBefore, acceptedBytesAfter);

        var remainingFiles = Directory.GetFiles(_outDir);
        CollectionAssert.AreEqual(new[] { finalPath }, remainingFiles);
    }
}
