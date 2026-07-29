using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using KnownFirst.Core.Preparation;

namespace KnownFirst.Tests;

/// <summary>
/// KF-MEANING-001 Slice 3: direct parity regression for <see cref="PreparationContextEvidencePolicy"/>
/// against the pre-extraction Schema-7 <c>PreparationService.NormalizeContext</c>/<c>CreateFingerprint</c>
/// algorithm, reimplemented here verbatim (not by calling the shared type) so a future accidental change
/// to the shared policy is caught by comparing against an independent, frozen reference implementation.
/// </summary>
[TestClass]
public sealed class PreparationContextEvidencePolicyTests
{
    [TestMethod]
    [DataRow("  The   bank\r\nprotects  money.\r\n  ")]
    [DataRow("Line1\rLine2\r\nLine3\n\nLine4")]
    [DataRow("Ångström café naïve")]
    [DataRow("single")]
    [DataRow("")]
    public void NormalizeAndFingerprint_MatchesFrozenPreExtractionReferenceAlgorithm(string rawText)
    {
        var expectedNormalized = ReferenceNormalize(rawText);
        var expectedFingerprint = ReferenceFingerprint(expectedNormalized);

        var actualNormalized = PreparationContextEvidencePolicy.NormalizeText(rawText);
        var actualFingerprint = PreparationContextEvidencePolicy.CreateFingerprint(actualNormalized);

        Assert.AreEqual(expectedNormalized, actualNormalized);
        Assert.AreEqual(expectedFingerprint, actualFingerprint);
    }

    [TestMethod]
    public void CreateKey_ProducesTheCanonicalFourFields()
    {
        var key = PreparationContextEvidencePolicy.CreateKey(
            sourceDocumentId: 7, rawText: "bank text here.", targetStart: 3, targetLength: 4);

        Assert.AreEqual(7, key.SourceDocumentId);
        Assert.AreEqual(3, key.TargetStart);
        Assert.AreEqual(4, key.TargetLength);
        Assert.AreEqual(ReferenceFingerprint(ReferenceNormalize("bank text here.")), key.NormalizedFingerprint);
    }

    // Frozen, independently-maintained copy of the pre-Slice-3 PreparationService algorithm.
    private static string ReferenceNormalize(string value) =>
        Regex.Replace(value.Replace("\r\n", "\n").Replace('\r', '\n').Trim(), @"\s+", " ")
            .Normalize(NormalizationForm.FormC);

    private static string ReferenceFingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
