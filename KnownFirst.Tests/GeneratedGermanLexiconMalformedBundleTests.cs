using System.Security.Cryptography;
using KnownFirst.Core.Text;
using KnownFirst.Core.Text.German;

namespace KnownFirst.Tests;

/// <summary>
/// Malformed/corrupted German lexicon runtime bundle rejection contract for
/// <see cref="GeneratedGermanLexicon.Load(Stream)"/>. Uses tiny hand-built byte fixtures only —
/// never the real production bundle, and never an actually-huge declared entry count.
///
/// Tests are split into two groups:
/// - Characterization: behavior the reader already gets right and must not regress.
/// - Genuine missing-behavior: corruption classes the reader must reject but, before this
///   correction's hardening, silently accepted or rejected with the wrong exception type.
///
/// <c>Load</c> verifies the whole-bundle SHA-256 trailer before parsing any provenance, counts,
/// or lexical content. A test that mutates or truncates bytes without producing a matching
/// trailer is therefore rejected via checksum mismatch, not via whichever downstream structural
/// guard (negative count, truncation, missing category byte, ...) it is nominally named for.
/// Tests below that target a structural guard past the checksum check construct a bundle whose
/// trailer is a freshly recomputed hash of the (mutated/truncated) content, via
/// <see cref="RecomputeChecksum"/> or <see cref="AppendChecksum"/>, so the checksum passes and
/// the intended downstream guard is the one that actually rejects the input. Tests whose
/// specific purpose is to prove whole-bundle integrity detection itself (<see
/// cref="Load_ChecksumMismatch_ThrowsInvalidDataException"/>, <see
/// cref="Load_CorruptedProvenanceBytes_ThrowsInvalidDataException"/>, <see
/// cref="Load_CorruptedLexicalPayloadBytes_ThrowsInvalidDataException"/>) intentionally leave the
/// checksum invalid instead.
/// </summary>
[TestClass]
public sealed class GeneratedGermanLexiconMalformedBundleTests
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

    private static byte[] ValidTwoEntryBundle()
    {
        var lemmas = new[]
        {
            new GermanLexemeEntry("Arbeit", GermanLexemeCategory.Noun),
            new GermanLexemeEntry("Zimmer", GermanLexemeCategory.Noun),
        };
        var stems = new[]
        {
            new GermanCompoundStemEntry("Schreib", "schreiben", GermanLexemeCategory.Verb),
        };

        using var stream = new MemoryStream();
        GermanLexiconRuntimeAssetWriter.Write(stream, SampleProvenance, SampleCounts, lemmas, stems);
        return stream.ToArray();
    }

    // ----- Characterization: already correct, must not regress -----

    [TestMethod]
    public void Load_ValidBundle_LoadsSuccessfully()
    {
        var lexicon = GeneratedGermanLexicon.Load(new MemoryStream(ValidTwoEntryBundle()));

        Assert.IsTrue(lexicon.TryLookupLemma("Arbeit", out _));
        Assert.IsTrue(lexicon.TryLookupStem("Schreib", out _));
    }

    [TestMethod]
    public void Load_WrongMagic_ThrowsInvalidDataException()
    {
        var bytes = ValidTwoEntryBundle();
        bytes[0] = (byte)'X';

        Assert.ThrowsExactly<InvalidDataException>(() => GeneratedGermanLexicon.Load(new MemoryStream(bytes)));
    }

    [TestMethod]
    public void Load_UnsupportedVersion_ThrowsInvalidDataException()
    {
        var bytes = ValidTwoEntryBundle();
        bytes[4] = 99; // version low byte, immediately after the 4-byte magic

        Assert.ThrowsExactly<InvalidDataException>(() => GeneratedGermanLexicon.Load(new MemoryStream(bytes)));
    }

    [TestMethod]
    public void Load_NegativeLemmaCount_ThrowsInvalidDataException()
    {
        var bytes = ValidTwoEntryBundle();
        var lemmaCountOffset = FindLemmaCountOffset(bytes);
        WriteInt32(bytes, lemmaCountOffset, -1);
        // Recompute the trailer so this reaches the negative-count guard itself, rather than
        // being rejected earlier by the whole-bundle checksum check.
        RecomputeChecksum(bytes);

        Assert.ThrowsExactly<InvalidDataException>(() => GeneratedGermanLexicon.Load(new MemoryStream(bytes)));
    }

    [TestMethod]
    public void Load_TrailingGarbageAfterChecksum_ThrowsInvalidDataException()
    {
        // Appending bytes past the end of an already-complete bundle (content + its own trailer)
        // shifts where the reader looks for the trailer, so this is rejected via checksum
        // mismatch rather than the exact-content-end structural guard. That guard is exercised
        // directly, with a recomputed valid checksum, by
        // Load_ExtraBytesInsertedBeforeChecksumTrailer_ThrowsInvalidDataException below.
        var bytes = ValidTwoEntryBundle();
        var withGarbage = bytes.Concat(new byte[] { 1, 2, 3 }).ToArray();

        Assert.ThrowsExactly<InvalidDataException>(() => GeneratedGermanLexicon.Load(new MemoryStream(withGarbage)));
    }

    [TestMethod]
    public void Load_ExtraBytesInsertedBeforeChecksumTrailer_ThrowsInvalidDataException()
    {
        // Insert extra bytes inside the checksum-covered content, after the last real lexical
        // entry, and recompute the trailer over that new content. The checksum now passes, so
        // parsing genuinely reaches the exact-content-end guard: it consumes exactly the
        // declared lemma/stem entries and then finds unconsumed bytes still remaining before the
        // trailer.
        var bytes = ValidTwoEntryBundle();
        var contentWithoutTrailer = bytes[..^GermanLexiconRuntimeAssetFormat.ChecksumTrailerLength];
        var contentWithExtraBytes = contentWithoutTrailer.Concat(new byte[] { 9, 9, 9 }).ToArray();
        var withValidChecksum = AppendChecksum(contentWithExtraBytes);

        Assert.ThrowsExactly<InvalidDataException>(() => GeneratedGermanLexicon.Load(new MemoryStream(withValidChecksum)));
    }

    // ----- Genuine missing-behavior cases -----

    [TestMethod]
    public void Load_StructurallyImpossibleLemmaCount_ThrowsInvalidDataExceptionBeforeLargeAllocation()
    {
        // Declares far more lemma entries than the remaining bytes could ever hold, without
        // requiring an actually-huge allocation attempt in the test itself.
        var bytes = ValidTwoEntryBundle();
        var lemmaCountOffset = FindLemmaCountOffset(bytes);
        WriteInt32(bytes, lemmaCountOffset, 10_000);
        // Recompute the trailer so this reaches the entry-count sanity-bound guard itself,
        // rather than being rejected earlier by the whole-bundle checksum check.
        RecomputeChecksum(bytes);

        Assert.ThrowsExactly<InvalidDataException>(() => GeneratedGermanLexicon.Load(new MemoryStream(bytes)));
    }

    [TestMethod]
    public void Load_TruncationBeforeLengthPrefix_ThrowsInvalidDataException()
    {
        // Two lemma entries; truncate exactly after the first entry's payload, so the file ends
        // with zero bytes of the second entry's 2-byte length prefix present at all. The trailer
        // is a fresh checksum of this exact truncated content, so the checksum itself passes and
        // parsing genuinely reaches the truncated second entry.
        var bytes = ValidTwoEntryBundle();
        var firstLemmaOffset = FindFirstLemmaEntryOffset(bytes);
        var firstFormLength = ReadUInt16(bytes, firstLemmaOffset);
        var firstEntryEnd = firstLemmaOffset + 2 + firstFormLength + 1; // length prefix + form + category byte
        var truncated = AppendChecksum(bytes[..firstEntryEnd]);

        Assert.ThrowsExactly<InvalidDataException>(() => GeneratedGermanLexicon.Load(new MemoryStream(truncated)));
    }

    [TestMethod]
    public void Load_TruncationInsideFormBytes_ThrowsInvalidDataException()
    {
        var bytes = ValidTwoEntryBundle();
        var firstLemmaOffset = FindFirstLemmaEntryOffset(bytes);
        var firstFormLength = ReadUInt16(bytes, firstLemmaOffset);
        var firstEntryEnd = firstLemmaOffset + 2 + firstFormLength + 1;
        // Truncate a few bytes into the second entry's form bytes (past its length prefix), with
        // a fresh checksum over this exact truncated content so parsing genuinely reaches it.
        var truncated = AppendChecksum(bytes[..(firstEntryEnd + 3)]);

        Assert.ThrowsExactly<InvalidDataException>(() => GeneratedGermanLexicon.Load(new MemoryStream(truncated)));
    }

    [TestMethod]
    public void Load_MissingCategoryByte_ThrowsInvalidDataException()
    {
        var bytes = ValidTwoEntryBundle();
        var firstLemmaOffset = FindFirstLemmaEntryOffset(bytes);
        var firstFormLength = ReadUInt16(bytes, firstLemmaOffset);
        // Truncate exactly at the first entry's category byte (form bytes present, category byte
        // missing), with a second entry still expected to follow and a fresh checksum over this
        // exact truncated content so parsing genuinely reaches the missing category byte.
        var truncated = AppendChecksum(bytes[..(firstLemmaOffset + 2 + firstFormLength)]);

        Assert.ThrowsExactly<InvalidDataException>(() => GeneratedGermanLexicon.Load(new MemoryStream(truncated)));
    }

    [TestMethod]
    public void Load_UnknownCategoryByte_ThrowsInvalidDataException()
    {
        var bytes = ValidTwoEntryBundle();
        var firstLemmaOffset = FindFirstLemmaEntryOffset(bytes);
        var firstFormLength = ReadUInt16(bytes, firstLemmaOffset);
        var categoryByteOffset = firstLemmaOffset + 2 + firstFormLength;
        bytes[categoryByteOffset] = 99;
        RecomputeChecksum(bytes);

        Assert.ThrowsExactly<InvalidDataException>(() => GeneratedGermanLexicon.Load(new MemoryStream(bytes)));
    }

    [TestMethod]
    public void Load_UnsortedLemmaEntries_ThrowsInvalidDataException()
    {
        var lemmas = new[]
        {
            new GermanLexemeEntry("Zimmer", GermanLexemeCategory.Noun),
            new GermanLexemeEntry("Arbeit", GermanLexemeCategory.Noun),
        };

        using var stream = new MemoryStream();
        GermanLexiconRuntimeAssetWriter.Write(stream, SampleProvenance, SampleCounts, lemmas, []);
        var bytes = stream.ToArray();

        Assert.ThrowsExactly<InvalidDataException>(() => GeneratedGermanLexicon.Load(new MemoryStream(bytes)));
    }

    [TestMethod]
    public void Load_DuplicateLemmaKey_ThrowsInvalidDataException()
    {
        var lemmas = new[]
        {
            new GermanLexemeEntry("Arbeit", GermanLexemeCategory.Noun),
            new GermanLexemeEntry("Arbeit", GermanLexemeCategory.Verb),
        };

        using var stream = new MemoryStream();
        GermanLexiconRuntimeAssetWriter.Write(stream, SampleProvenance, SampleCounts, lemmas, []);
        var bytes = stream.ToArray();

        Assert.ThrowsExactly<InvalidDataException>(() => GeneratedGermanLexicon.Load(new MemoryStream(bytes)));
    }

    [TestMethod]
    public void Load_UnsortedStemEntries_ThrowsInvalidDataException()
    {
        var stems = new[]
        {
            new GermanCompoundStemEntry("Wasch", "waschen", GermanLexemeCategory.Verb),
            new GermanCompoundStemEntry("Aal", "aalen", GermanLexemeCategory.Verb),
        };

        using var stream = new MemoryStream();
        GermanLexiconRuntimeAssetWriter.Write(stream, SampleProvenance, SampleCounts, [], stems);
        var bytes = stream.ToArray();

        Assert.ThrowsExactly<InvalidDataException>(() => GeneratedGermanLexicon.Load(new MemoryStream(bytes)));
    }

    [TestMethod]
    public void Load_DuplicateStemKey_ThrowsInvalidDataException()
    {
        var stems = new[]
        {
            new GermanCompoundStemEntry("Schreib", "schreiben", GermanLexemeCategory.Verb),
            new GermanCompoundStemEntry("Schreib", "schreien", GermanLexemeCategory.Verb),
        };

        using var stream = new MemoryStream();
        GermanLexiconRuntimeAssetWriter.Write(stream, SampleProvenance, SampleCounts, [], stems);
        var bytes = stream.ToArray();

        Assert.ThrowsExactly<InvalidDataException>(() => GeneratedGermanLexicon.Load(new MemoryStream(bytes)));
    }

    [TestMethod]
    public void Load_ChecksumMismatch_ThrowsInvalidDataException()
    {
        // Intentionally checksum-invalid: this proves whole-bundle integrity detection itself,
        // so the trailer is deliberately left unrecomputed.
        var bytes = ValidTwoEntryBundle();
        bytes[^1] ^= 0xFF; // flip a bit in the trailer itself, leaving content bytes untouched

        Assert.ThrowsExactly<InvalidDataException>(() => GeneratedGermanLexicon.Load(new MemoryStream(bytes)));
    }

    [TestMethod]
    public void Load_CorruptedProvenanceBytes_ThrowsInvalidDataException()
    {
        // Intentionally checksum-invalid: this proves whole-bundle integrity detection covers
        // the provenance section specifically, so the trailer is deliberately left unrecomputed
        // (the structural bounds-checking helpers this corruption would otherwise reach —
        // ReadFormLengthChecked/EnsureBytesAvailable — are the same shared code already
        // exercised independently by the lemma-entry truncation tests above).
        var bytes = ValidTwoEntryBundle();
        // Byte 6 falls inside the first provenance string's length-prefixed UTF-8 content
        // (offset 0-3 magic, 4-5 version, 6.. = UpstreamProjectName's 2-byte length prefix + text).
        bytes[8] ^= 0xFF;

        Assert.ThrowsExactly<InvalidDataException>(() => GeneratedGermanLexicon.Load(new MemoryStream(bytes)));
    }

    [TestMethod]
    public void Load_CorruptedLexicalPayloadBytes_ThrowsInvalidDataException()
    {
        // Intentionally checksum-invalid: this proves whole-bundle integrity detection covers
        // the lexical payload specifically, so the trailer is deliberately left unrecomputed.
        var bytes = ValidTwoEntryBundle();
        var firstLemmaOffset = FindFirstLemmaEntryOffset(bytes);
        bytes[firstLemmaOffset + 2] ^= 0xFF; // first byte of the first lemma's form text

        Assert.ThrowsExactly<InvalidDataException>(() => GeneratedGermanLexicon.Load(new MemoryStream(bytes)));
    }

    // ----- Fixture helpers (locate offsets inside a bundle produced by the real writer) -----

    private static void RecomputeChecksum(byte[] bytes)
    {
        var checksum = SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32));
        checksum.CopyTo(bytes, bytes.Length - 32);
    }

    /// <summary>Appends a freshly computed SHA-256 trailer over <paramref name="contentWithoutTrailer"/>, producing a self-consistent (checksum-valid) bundle out of arbitrary, possibly-truncated content.</summary>
    private static byte[] AppendChecksum(byte[] contentWithoutTrailer) =>
        [.. contentWithoutTrailer, .. SHA256.HashData(contentWithoutTrailer)];

    private static int FindLemmaCountOffset(byte[] bytes)
    {
        // header (6) + 7 provenance strings + 9 int32 counts, immediately followed by lemma count.
        var offset = 6;
        for (var i = 0; i < 7; i++)
        {
            offset = SkipForm(bytes, offset);
        }

        offset += 9 * 4;
        return offset;
    }

    private static int FindFirstLemmaEntryOffset(byte[] bytes) => FindLemmaCountOffset(bytes) + 8; // + lemmaCount(4) + stemCount(4)

    private static int SkipForm(byte[] bytes, int offset)
    {
        var length = ReadUInt16(bytes, offset);
        return offset + 2 + length;
    }

    private static ushort ReadUInt16(byte[] bytes, int offset) =>
        (ushort)(bytes[offset] | (bytes[offset + 1] << 8));

    private static void WriteInt32(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }
}
