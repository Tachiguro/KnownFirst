using KnownFirst.Services.Study;

namespace KnownFirst.Tests;

/// <summary>
/// KF-MEANING-001 Slice 3 §8: <see cref="PreparationMetadataPolicy"/> — trim/Form-C normalize/empty-collapse,
/// UTF-8 byte-length boundaries (including multi-byte characters), and PartOfSpeech precedence.
/// </summary>
[TestClass]
public sealed class PreparationMetadataPolicyTests
{
    [TestMethod]
    public void NormalizeTopicOrDomain_TrimsAndCollapsesEmptyToEmpty()
    {
        Assert.AreEqual(string.Empty, PreparationMetadataPolicy.NormalizeTopicOrDomain(null));
        Assert.AreEqual(string.Empty, PreparationMetadataPolicy.NormalizeTopicOrDomain(""));
        Assert.AreEqual(string.Empty, PreparationMetadataPolicy.NormalizeTopicOrDomain("   "));
        Assert.AreEqual("finance", PreparationMetadataPolicy.NormalizeTopicOrDomain("  finance  "));
    }

    [TestMethod]
    public void NormalizeTopicOrDomain_AtExactByteLimit_Succeeds()
    {
        var value = new string('a', PreparationMetadataPolicy.MaxTopicOrDomainUtf8Bytes);
        var normalized = PreparationMetadataPolicy.NormalizeTopicOrDomain(value);
        Assert.AreEqual(value, normalized);
    }

    [TestMethod]
    public void NormalizeTopicOrDomain_OneByteOverLimit_Throws()
    {
        var value = new string('a', PreparationMetadataPolicy.MaxTopicOrDomainUtf8Bytes + 1);
        var exception = Assert.ThrowsExactly<PreparationMetadataValidationException>(
            () => PreparationMetadataPolicy.NormalizeTopicOrDomain(value));
        Assert.AreEqual("preparation-topic-or-domain-too-long", exception.ErrorCode);
    }

    [TestMethod]
    public void NormalizePartOfSpeech_OneByteOverLimit_Throws()
    {
        var value = new string('a', PreparationMetadataPolicy.MaxPartOfSpeechUtf8Bytes + 1);
        var exception = Assert.ThrowsExactly<PreparationMetadataValidationException>(
            () => PreparationMetadataPolicy.NormalizePartOfSpeech(value));
        Assert.AreEqual("preparation-part-of-speech-too-long", exception.ErrorCode);
    }

    [TestMethod]
    public void NormalizeTopicOrDomain_MultiByteCharacters_CountedByUtf8Bytes_NotCharCount()
    {
        // Each 'é' is 2 UTF-8 bytes but 1 UTF-16 char; 200 of them is 400 bytes, over the 256-byte limit,
        // even though the .NET string.Length is only 200.
        var value = new string('é', 200);
        Assert.AreEqual(200, value.Length);
        var exception = Assert.ThrowsExactly<PreparationMetadataValidationException>(
            () => PreparationMetadataPolicy.NormalizeTopicOrDomain(value));
        Assert.AreEqual("preparation-topic-or-domain-too-long", exception.ErrorCode);
    }

    [TestMethod]
    public void NormalizeTopicOrDomain_AppliesUnicodeFormC()
    {
        // "e" + combining acute accent (decomposed, Form D) must normalize to the precomposed "é" (Form C).
        var decomposed = "é";
        var normalized = PreparationMetadataPolicy.NormalizeTopicOrDomain(decomposed);
        Assert.AreEqual("é".Normalize(System.Text.NormalizationForm.FormC), normalized);
        Assert.AreEqual(1, normalized.Length);
    }

    [TestMethod]
    public void ResolvePartOfSpeech_ExplicitInputTakesPrecedenceOverProvider()
    {
        var resolved = PreparationMetadataPolicy.ResolvePartOfSpeech("proper noun", "noun");
        Assert.AreEqual("proper noun", resolved);
    }

    [TestMethod]
    public void ResolvePartOfSpeech_FallsBackToProviderWhenExplicitIsEmpty()
    {
        Assert.AreEqual("noun", PreparationMetadataPolicy.ResolvePartOfSpeech(null, "noun"));
        Assert.AreEqual("noun", PreparationMetadataPolicy.ResolvePartOfSpeech("   ", "noun"));
    }

    [TestMethod]
    public void ResolvePartOfSpeech_BothEmpty_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, PreparationMetadataPolicy.ResolvePartOfSpeech(null, null));
    }
}
