using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace KnownFirst.Core.Text.German;

/// <summary>
/// Production, offline, deterministic <see cref="IGermanLexicon"/> implementation backed by the
/// single authoritative runtime bundle described by <see cref="GermanLexiconRuntimeAssetFormat"/>
/// (lexical data, provenance, and generation counts together, closed under one SHA-256 trailer).
///
/// The entire encoded bundle is kept in memory as a single byte buffer with two ordinal-sorted
/// offset arrays for binary search; individual lemma/stem strings are decoded from UTF-8 only
/// on a successful match. This avoids materializing one <see cref="string"/> object per
/// dictionary entry (hundreds of thousands of entries) purely to answer lookups, which is the
/// dominant memory cost of a naive <c>Dictionary&lt;string, ...&gt;</c> representation at this
/// scale.
///
/// <see cref="Load(Stream)"/> rejects any malformed or corrupted bundle deterministically with
/// <see cref="InvalidDataException"/> rather than ever returning wrong-but-plausible lexical
/// data: it verifies the trailing SHA-256 over the whole bundle before trusting any parsed
/// content, derives entry-count sanity bounds from the bundle's own byte length before
/// allocating anything, validates every category byte eagerly, and requires both the lemma and
/// stem tables to be strictly ordinal-ascending with no duplicate keys (the precondition binary
/// search itself depends on for soundness).
///
/// No reflection, no network access, no assembly scanning. Lookups compare Unicode
/// NFC-normalized forms using ordinal UTF-8 byte order, which matches
/// <see cref="StringComparer.Ordinal"/> UTF-16 order for the Latin/Latin-Extended-A text this
/// bundle contains (no surrogate pairs are present in German lexical data).
///
/// KnownFirst source code (Apache-2.0). The bytes this class reads are processed/derived from
/// upstream CC BY-SA 4.0 German morphological data; see the sibling
/// <c>Assets/NOTICE.md</c> for provenance.
/// </summary>
public sealed class GeneratedGermanLexicon : IGermanLexicon
{
    /// <summary>Minimum plausible bundle size before any variable-length content: magic(4) + version(2) + seven 2-byte provenance length prefixes(14) + nine int32 counts(36) + lemmaCount(4) + stemCount(4) + trailer(32).</summary>
    private const int MinimumFixedStructureLength = 4 + 2 + (7 * 2) + (9 * 4) + 4 + 4 + GermanLexiconRuntimeAssetFormat.ChecksumTrailerLength;

    /// <summary>Minimum bytes a lemma entry can occupy: 2-byte length prefix + 0-byte form + 1-byte category.</summary>
    private const int MinLemmaEntryBytes = 2 + 1;

    /// <summary>Minimum bytes a stem entry can occupy: two 2-byte length prefixes + two 0-byte forms + 1-byte category.</summary>
    private const int MinStemEntryBytes = 2 + 2 + 1;

    private readonly byte[] _data;
    private readonly int[] _lemmaEntryOffsets;
    private readonly int[] _stemEntryOffsets;

    public GermanLexiconBundleProvenance Provenance { get; }

    public GermanLexiconBundleCounts Counts { get; }

    private GeneratedGermanLexicon(
        byte[] data,
        int[] lemmaEntryOffsets,
        int[] stemEntryOffsets,
        GermanLexiconBundleProvenance provenance,
        GermanLexiconBundleCounts counts)
    {
        _data = data;
        _lemmaEntryOffsets = lemmaEntryOffsets;
        _stemEntryOffsets = stemEntryOffsets;
        Provenance = provenance;
        Counts = counts;
    }

    public static GeneratedGermanLexicon LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    public static GeneratedGermanLexicon Load(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        var data = buffer.ToArray();

        if (data.Length < MinimumFixedStructureLength)
        {
            throw new InvalidDataException("German lexicon runtime bundle is truncated.");
        }

        var magic = data.AsSpan(0, GermanLexiconRuntimeAssetFormat.Magic.Length);
        if (!magic.SequenceEqual(GermanLexiconRuntimeAssetFormat.Magic))
        {
            throw new InvalidDataException("German lexicon runtime bundle has an invalid magic header.");
        }

        var offset = GermanLexiconRuntimeAssetFormat.Magic.Length;
        var version = ReadUInt16Checked(data, ref offset);
        if (version != GermanLexiconRuntimeAssetFormat.CurrentVersion)
        {
            throw new InvalidDataException(
                $"German lexicon runtime bundle format version {version} is not supported by this reader.");
        }

        // Verify the whole-bundle integrity trailer before trusting any parsed content. The
        // trailer's own 32 bytes are never part of the input to their own hash.
        var contentLength = data.Length - GermanLexiconRuntimeAssetFormat.ChecksumTrailerLength;
        var expectedChecksum = SHA256.HashData(data.AsSpan(0, contentLength));
        var actualChecksum = data.AsSpan(contentLength, GermanLexiconRuntimeAssetFormat.ChecksumTrailerLength);
        if (!expectedChecksum.AsSpan().SequenceEqual(actualChecksum))
        {
            throw new InvalidDataException("German lexicon runtime bundle failed its integrity checksum.");
        }

        var upstreamProjectName = ReadFormChecked(data, ref offset, contentLength);
        var upstreamRepositoryUrl = ReadFormChecked(data, ref offset, contentLength);
        var upstreamCommit = ReadFormChecked(data, ref offset, contentLength);
        var upstreamSourceAssetPath = ReadFormChecked(data, ref offset, contentLength);
        var upstreamSourceSha256 = ReadFormChecked(data, ref offset, contentLength);
        var dataLicenseIdentifier = ReadFormChecked(data, ref offset, contentLength);
        var provenanceStatement = ReadFormChecked(data, ref offset, contentLength);

        var provenance = new GermanLexiconBundleProvenance(
            upstreamProjectName,
            upstreamRepositoryUrl,
            upstreamCommit,
            upstreamSourceAssetPath,
            upstreamSourceSha256,
            dataLicenseIdentifier,
            provenanceStatement);

        var counts = new GermanLexiconBundleCounts(
            ReadInt32Checked(data, ref offset, contentLength),
            ReadInt32Checked(data, ref offset, contentLength),
            ReadInt32Checked(data, ref offset, contentLength),
            ReadInt32Checked(data, ref offset, contentLength),
            ReadInt32Checked(data, ref offset, contentLength),
            ReadInt32Checked(data, ref offset, contentLength),
            ReadInt32Checked(data, ref offset, contentLength),
            ReadInt32Checked(data, ref offset, contentLength),
            ReadInt32Checked(data, ref offset, contentLength));

        var lemmaCount = ReadInt32Checked(data, ref offset, contentLength);
        var stemCount = ReadInt32Checked(data, ref offset, contentLength);

        if (lemmaCount < 0 || stemCount < 0)
        {
            throw new InvalidDataException("German lexicon runtime bundle declares a negative entry count.");
        }

        // Derive a sanity bound for the declared counts purely from the bundle's own remaining
        // byte length, before allocating any offset array — never from an arbitrary product
        // ceiling. This turns a corrupted/adversarial count into an immediate, cheap rejection
        // instead of an oversized allocation attempt.
        var remainingForEntries = (long)contentLength - offset;
        var minimumRequiredBytes = ((long)lemmaCount * MinLemmaEntryBytes) + ((long)stemCount * MinStemEntryBytes);
        if (minimumRequiredBytes > remainingForEntries)
        {
            throw new InvalidDataException(
                "German lexicon runtime bundle declares more lemma/stem entries than its remaining bytes could hold.");
        }

        var lemmaOffsets = new int[lemmaCount];
        ReadOnlySpan<byte> previousLemmaKey = default;
        for (var i = 0; i < lemmaCount; i++)
        {
            lemmaOffsets[i] = offset;
            var formEnd = ReadFormLengthChecked(data, offset, contentLength, out var formStart);
            var key = data.AsSpan(formStart, formEnd - formStart);
            if (i > 0 && CompareOrdinal(previousLemmaKey, key) >= 0)
            {
                throw new InvalidDataException(
                    "German lexicon runtime bundle lemma table is not strictly ordinal-ascending (out of order or duplicate key).");
            }

            previousLemmaKey = key;
            offset = formEnd;
            EnsureBytesAvailable(offset, 1, contentLength);
            _ = DecodeCategory(data[offset]);
            offset += 1;
        }

        var stemOffsets = new int[stemCount];
        ReadOnlySpan<byte> previousStemKey = default;
        for (var i = 0; i < stemCount; i++)
        {
            stemOffsets[i] = offset;
            var componentEnd = ReadFormLengthChecked(data, offset, contentLength, out var componentStart);
            var componentKey = data.AsSpan(componentStart, componentEnd - componentStart);
            if (i > 0 && CompareOrdinal(previousStemKey, componentKey) >= 0)
            {
                throw new InvalidDataException(
                    "German lexicon runtime bundle stem table is not strictly ordinal-ascending (out of order or duplicate key).");
            }

            previousStemKey = componentKey;
            offset = componentEnd;
            offset = ReadFormLengthChecked(data, offset, contentLength, out _);
            EnsureBytesAvailable(offset, 1, contentLength);
            _ = DecodeCategory(data[offset]);
            offset += 1;
        }

        if (offset != contentLength)
        {
            throw new InvalidDataException("German lexicon runtime bundle has trailing or missing bytes.");
        }

        return new GeneratedGermanLexicon(data, lemmaOffsets, stemOffsets, provenance, counts);
    }

    public bool TryLookupLemma(string form, out GermanLexemeEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(form);

        var queryBytes = Encoding.UTF8.GetBytes(form.Normalize(NormalizationForm.FormC));
        if (TryFindLemmaOffset(queryBytes, out var offset))
        {
            var formEnd = ReadFormLength(_data, offset, out var formStart);
            var category = DecodeCategory(_data[formEnd]);
            entry = new GermanLexemeEntry(Encoding.UTF8.GetString(_data, formStart, formEnd - formStart), category);
            return true;
        }

        entry = null;
        return false;
    }

    public bool TryLookupStem(string componentForm, out GermanCompoundStemEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(componentForm);

        var queryBytes = Encoding.UTF8.GetBytes(componentForm.Normalize(NormalizationForm.FormC));

        if (TryFindStemOffset(queryBytes, out var stemOffset))
        {
            var componentEnd = ReadFormLength(_data, stemOffset, out var componentStart);
            var lemmaEnd = ReadFormLength(_data, componentEnd, out var lemmaStart);
            var category = DecodeCategory(_data[lemmaEnd]);
            entry = new GermanCompoundStemEntry(
                Encoding.UTF8.GetString(_data, componentStart, componentEnd - componentStart),
                Encoding.UTF8.GetString(_data, lemmaStart, lemmaEnd - lemmaStart),
                category);
            return true;
        }

        if (TryFindLemmaOffset(queryBytes, out var lemmaOffset))
        {
            var formEnd = ReadFormLength(_data, lemmaOffset, out var formStart);
            var category = DecodeCategory(_data[formEnd]);
            if (category == GermanLexemeCategory.Noun)
            {
                var form = Encoding.UTF8.GetString(_data, formStart, formEnd - formStart);
                entry = new GermanCompoundStemEntry(form, form, GermanLexemeCategory.Noun);
                return true;
            }
        }

        entry = null;
        return false;
    }

    private bool TryFindLemmaOffset(byte[] queryBytes, out int offset) =>
        TryBinarySearch(_lemmaEntryOffsets, queryBytes, out offset);

    private bool TryFindStemOffset(byte[] queryBytes, out int offset) =>
        TryBinarySearch(_stemEntryOffsets, queryBytes, out offset);

    private bool TryBinarySearch(int[] entryOffsets, byte[] queryBytes, out int offset)
    {
        var low = 0;
        var high = entryOffsets.Length - 1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var midOffset = entryOffsets[mid];
            var midEnd = ReadFormLength(_data, midOffset, out var midStart);
            var comparison = CompareOrdinal(_data.AsSpan(midStart, midEnd - midStart), queryBytes);

            if (comparison == 0)
            {
                offset = midOffset;
                return true;
            }

            if (comparison < 0)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        offset = -1;
        return false;
    }

    private static int CompareOrdinal(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        var shared = Math.Min(left.Length, right.Length);
        for (var i = 0; i < shared; i++)
        {
            var diff = left[i] - right[i];
            if (diff != 0)
            {
                return diff;
            }
        }

        return left.Length - right.Length;
    }

    /// <summary>Throws unless at least <paramref name="needed"/> bytes are available starting at <paramref name="offset"/>, within <paramref name="contentLength"/>.</summary>
    private static void EnsureBytesAvailable(int offset, int needed, int contentLength)
    {
        if (offset < 0 || offset > contentLength || needed > contentLength - offset)
        {
            throw new InvalidDataException("German lexicon runtime bundle is truncated.");
        }
    }

    private static ushort ReadUInt16Checked(byte[] data, ref int offset)
    {
        EnsureBytesAvailable(offset, 2, data.Length);
        var value = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
        offset += 2;
        return value;
    }

    private static int ReadInt32Checked(byte[] data, ref int offset, int contentLength)
    {
        EnsureBytesAvailable(offset, 4, contentLength);
        var value = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
        offset += 4;
        return value;
    }

    /// <summary>Bounds-checked read of a length-prefixed UTF-8 form's end offset; does not advance <paramref name="offset"/> itself.</summary>
    private static int ReadFormLengthChecked(byte[] data, int offset, int contentLength, out int formStart)
    {
        EnsureBytesAvailable(offset, 2, contentLength);
        var length = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
        formStart = offset + 2;
        EnsureBytesAvailable(formStart, length, contentLength);
        return formStart + length;
    }

    private static string ReadFormChecked(byte[] data, ref int offset, int contentLength)
    {
        var formEnd = ReadFormLengthChecked(data, offset, contentLength, out var formStart);
        var text = Encoding.UTF8.GetString(data, formStart, formEnd - formStart);
        offset = formEnd;
        return text;
    }

    private static int ReadFormLength(byte[] data, int offset, out int formStart)
    {
        var length = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
        formStart = offset + 2;
        return formStart + length;
    }

    private static GermanLexemeCategory DecodeCategory(byte code) => code switch
    {
        1 => GermanLexemeCategory.Noun,
        2 => GermanLexemeCategory.Verb,
        3 => GermanLexemeCategory.Adjective,
        _ => throw new InvalidDataException($"German lexicon runtime bundle has an unknown category code {code}."),
    };
}
