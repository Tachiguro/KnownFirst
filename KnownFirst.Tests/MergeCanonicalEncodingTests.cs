using System.Globalization;
using System.Text.RegularExpressions;
using KnownFirst.Services.DataSafety.Merge;

namespace KnownFirst.Tests;

[TestClass]
public sealed class MergeCanonicalEncodingTests
{
    private const string Domain = "KnownFirst.Merge.Test.v1";

    [TestMethod]
    public void WriteNullableString_NullAndEmpty_ProduceDifferentBytes()
    {
        var nullBytes = new CanonicalFingerprintBuilder(Domain).WriteNullableString(null).ToByteArray();
        var emptyBytes = new CanonicalFingerprintBuilder(Domain).WriteNullableString(string.Empty).ToByteArray();

        CollectionAssert.AreNotEqual(nullBytes, emptyBytes);

        var nullHash = new CanonicalFingerprintBuilder(Domain).WriteNullableString(null).ComputeSha256Hex();
        var emptyHash = new CanonicalFingerprintBuilder(Domain).WriteNullableString(string.Empty).ComputeSha256Hex();
        Assert.AreNotEqual(nullHash, emptyHash);
    }

    [TestMethod]
    public void DelimiterLikeContent_DoesNotCreateAmbiguousBoundaries()
    {
        // Naive "|"-joining of ("a|b","c") and ("a","b|c") would collide ("a|b|c" both ways).
        var hash1 = new CanonicalFingerprintBuilder(Domain).WriteString("a|b").WriteString("c").ComputeSha256Hex();
        var hash2 = new CanonicalFingerprintBuilder(Domain).WriteString("a").WriteString("b|c").ComputeSha256Hex();

        Assert.AreNotEqual(hash1, hash2);
    }

    [TestMethod]
    public void UnicodeAndCombiningCharacters_EncodeDeterministically()
    {
        // Combining marks, a surrogate-pair emoji, and Arabic diacritics.
        var combining = "café \U0001F600 مَرْحَبً";

        var hash1 = new CanonicalFingerprintBuilder(Domain).WriteString(combining).ComputeSha256Hex();
        var hash2 = new CanonicalFingerprintBuilder(Domain).WriteString(combining).ComputeSha256Hex();

        Assert.AreEqual(hash1, hash2);

        var precomposedCafe = "café"; // "e" + U+00E9 (precomposed e-acute)
        var decomposedCafe = "café"; // "e" + U+0301 (combining acute accent)
        Assert.AreNotEqual(precomposedCafe, decomposedCafe, "Test fixture sanity: the two forms must differ at the string level.");

        var precomposedHash = new CanonicalFingerprintBuilder(Domain).WriteString(precomposedCafe).ComputeSha256Hex();
        var decomposedHash = new CanonicalFingerprintBuilder(Domain).WriteString(decomposedCafe).ComputeSha256Hex();
        // Distinct byte sequences are expected to hash distinctly; this encoder does not perform
        // Unicode normalization itself (that is a caller-side decision, e.g. VocabularyIdentityPolicy).
        Assert.AreNotEqual(precomposedHash, decomposedHash);

        Assert.AreEqual(
            new CanonicalFingerprintBuilder(Domain).WriteString(precomposedCafe).ComputeSha256Hex(),
            new CanonicalFingerprintBuilder(Domain).WriteString(precomposedCafe).ComputeSha256Hex());
    }

    [TestMethod]
    public void Encoding_IsInvariantUnderNonEnglishCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariantHash = BuildSampleFields();

            CultureInfo.CurrentCulture = new CultureInfo("tr-TR"); // Turkish I/i casing and comma decimals
            var turkishHash = BuildSampleFields();

            CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // comma decimal separator
            var germanHash = BuildSampleFields();

            Assert.AreEqual(invariantHash, turkishHash);
            Assert.AreEqual(invariantHash, germanHash);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        static string BuildSampleFields() =>
            new CanonicalFingerprintBuilder(Domain)
                .WriteString("Istanbul")
                .WriteInt32(-42)
                .WriteDouble(2.5)
                .WriteUtcTimestamp(new DateTime(2026, 7, 27, 10, 30, 0, DateTimeKind.Utc))
                .ComputeSha256Hex();
    }

    [TestMethod]
    public void WriteUtcTimestamp_SameInstantDifferentKind_NormalizesIdentically()
    {
        var utcKind = new DateTime(2026, 7, 27, 10, 30, 0, 123, DateTimeKind.Utc);
        var unspecifiedKind = new DateTime(2026, 7, 27, 10, 30, 0, 123, DateTimeKind.Unspecified);

        var hash1 = new CanonicalFingerprintBuilder(Domain).WriteUtcTimestamp(utcKind).ComputeSha256Hex();
        var hash2 = new CanonicalFingerprintBuilder(Domain).WriteUtcTimestamp(unspecifiedKind).ComputeSha256Hex();

        Assert.AreEqual(hash1, hash2);
    }

    [TestMethod]
    public void WriteUtcTimestamp_LocalKind_Throws()
    {
        var local = new DateTime(2026, 7, 27, 10, 30, 0, DateTimeKind.Local);

        Assert.ThrowsExactly<ArgumentException>(() => new CanonicalFingerprintBuilder(Domain).WriteUtcTimestamp(local));
    }

    [TestMethod]
    public void ChangingAnyEncodedField_ChangesTheResultingHash()
    {
        var baseline = new CanonicalFingerprintBuilder(Domain)
            .WriteString("alpha")
            .WriteInt32(1)
            .WriteBoolean(true)
            .WriteDouble(1.5)
            .WriteUtcTimestamp(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .ComputeSha256Hex();

        var changedString = new CanonicalFingerprintBuilder(Domain)
            .WriteString("beta")
            .WriteInt32(1)
            .WriteBoolean(true)
            .WriteDouble(1.5)
            .WriteUtcTimestamp(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .ComputeSha256Hex();

        var changedInt = new CanonicalFingerprintBuilder(Domain)
            .WriteString("alpha")
            .WriteInt32(2)
            .WriteBoolean(true)
            .WriteDouble(1.5)
            .WriteUtcTimestamp(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .ComputeSha256Hex();

        var changedBool = new CanonicalFingerprintBuilder(Domain)
            .WriteString("alpha")
            .WriteInt32(1)
            .WriteBoolean(false)
            .WriteDouble(1.5)
            .WriteUtcTimestamp(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .ComputeSha256Hex();

        var changedDouble = new CanonicalFingerprintBuilder(Domain)
            .WriteString("alpha")
            .WriteInt32(1)
            .WriteBoolean(true)
            .WriteDouble(1.500001)
            .WriteUtcTimestamp(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .ComputeSha256Hex();

        var changedTimestamp = new CanonicalFingerprintBuilder(Domain)
            .WriteString("alpha")
            .WriteInt32(1)
            .WriteBoolean(true)
            .WriteDouble(1.5)
            .WriteUtcTimestamp(new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc))
            .ComputeSha256Hex();

        var allHashes = new[] { baseline, changedString, changedInt, changedBool, changedDouble, changedTimestamp };
        CollectionAssert.AllItemsAreUnique(allHashes);
    }

    [TestMethod]
    public void DomainDiscriminator_SeparatesOtherwiseIdenticalFieldSequences()
    {
        var hashA = new CanonicalFingerprintBuilder("KnownFirst.Merge.FamilyA.v1").WriteString("same").ComputeSha256Hex();
        var hashB = new CanonicalFingerprintBuilder("KnownFirst.Merge.FamilyB.v1").WriteString("same").ComputeSha256Hex();

        Assert.AreNotEqual(hashA, hashB);
    }

    [TestMethod]
    public void DomainDiscriminator_VersionBumpSeparatesFingerprintFamily()
    {
        var v1 = new CanonicalFingerprintBuilder("KnownFirst.Merge.Sample.v1").WriteString("same").ComputeSha256Hex();
        var v2 = new CanonicalFingerprintBuilder("KnownFirst.Merge.Sample.v2").WriteString("same").ComputeSha256Hex();

        Assert.AreNotEqual(v1, v2);
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    public void WriteDouble_NonFiniteValue_Throws(double value)
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CanonicalFingerprintBuilder(Domain).WriteDouble(value));
    }

    [TestMethod]
    public void WriteDouble_NegativeZeroAndPositiveZero_EncodeDifferently()
    {
        // Documented current behavior: "G17" preserves the IEEE-754 sign of zero ("-0" vs "0"), so
        // +0.0 and -0.0 are distinct canonical values. This is a non-issue for every currently encoded
        // field (e.g. EaseFactor's domain floor is 1.3), but is asserted here so a future caller cannot
        // be silently surprised by it.
        var negativeZeroHash = new CanonicalFingerprintBuilder(Domain).WriteDouble(-0.0).ComputeSha256Hex();
        var positiveZeroHash = new CanonicalFingerprintBuilder(Domain).WriteDouble(0.0).ComputeSha256Hex();

        Assert.AreNotEqual(negativeZeroHash, positiveZeroHash);

        // Each is still fully deterministic on its own.
        Assert.AreEqual(negativeZeroHash, new CanonicalFingerprintBuilder(Domain).WriteDouble(-0.0).ComputeSha256Hex());
    }

    [TestMethod]
    public void WriteEnum_UndefinedValue_Throws()
    {
        const DayOfWeek undefined = (DayOfWeek)999;

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CanonicalFingerprintBuilder(Domain).WriteEnum(undefined));
    }

    [TestMethod]
    public void WriteEnum_UsesSymbolicNameNotNumericOrdinal()
    {
        // Two enums whose members share a numeric ordinal but differ in name must not collide.
        var hashByName = new CanonicalFingerprintBuilder(Domain).WriteEnum(DayOfWeek.Monday).ComputeSha256Hex();
        var hashByRawOrdinal = new CanonicalFingerprintBuilder(Domain).WriteInt32((int)DayOfWeek.Monday).ComputeSha256Hex();

        Assert.AreNotEqual(hashByName, hashByRawOrdinal, "Enum encoding must use the symbolic name, not the numeric ordinal.");
    }

    [TestMethod]
    public void ComputeSha256Hex_IsUppercaseSha256Hex()
    {
        var hash = new CanonicalFingerprintBuilder(Domain).WriteString("anything").ComputeSha256Hex();

        Assert.AreEqual(64, hash.Length);
        Assert.IsTrue(Regex.IsMatch(hash, "^[0-9A-F]{64}$"), $"Expected uppercase hex SHA-256, got '{hash}'.");
    }
}
