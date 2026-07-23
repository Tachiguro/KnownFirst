using KnownFirst.Core.Preparation;

namespace KnownFirst.Tests;

[TestClass]
public sealed class SourceReferencePolicyTests
{
    [TestMethod]
    [DataRow("en.wiktionary.org", "hello", "https://en.wiktionary.org/wiki/hello")]
    [DataRow("de.wiktionary.org", "Hallo Welt", "https://de.wiktionary.org/wiki/Hallo_Welt")]
    [DataRow("en.wikipedia.org", "Artificial intelligence", "https://en.wikipedia.org/wiki/Artificial_intelligence")]
    [DataRow("de.wikipedia.org", "Künstliche Intelligenz", "https://de.wikipedia.org/wiki/K%C3%BCnstliche_Intelligenz")]
    [DataRow("EN.WIKIPEDIA.ORG", "Exact Title", "https://en.wikipedia.org/wiki/Exact_Title")]
    [DataRow("DE.WIKTIONARY.ORG", "Titel", "https://de.wiktionary.org/wiki/Titel")]
    public void CreatePageUri_TrustedSourceHosts_ReturnsValidHttpsUri(string sourceProject, string pageTitle, string expectedUri)
    {
        var result = SourceReferencePolicy.CreatePageUri(sourceProject, pageTitle);

        Assert.IsNotNull(result);
        Assert.AreEqual(expectedUri, result.AbsoluteUri);
        Assert.AreEqual("https", result.Scheme);
    }

    [TestMethod]
    [DataRow("wikipedia.org", "Title")]
    [DataRow("wiktionary.org", "Title")]
    [DataRow("commons.wikimedia.org", "File:Example.jpg")]
    [DataRow("arbitrary.wikimedia.org", "Title")]
    [DataRow("wikimedia.org", "Title")]
    [DataRow("wikipedia.org.evil.example", "Title")]
    [DataRow("evilwikipedia.org", "Title")]
    [DataRow("https://en.wikipedia.org", "Title")]
    [DataRow("en.wikipedia.org/wiki/Foo", "Title")]
    [DataRow("en.wikipedia.org?a=b", "Title")]
    [DataRow("en.wikipedia.org#hash", "Title")]
    [DataRow("user:pass@en.wikipedia.org", "Title")]
    [DataRow("en.wikipedia.org:443", "Title")]
    [DataRow("sub.en.wikipedia.org", "Title")]
    [DataRow("  ", "Title")]
    [DataRow(null, "Title")]
    [DataRow("en.wikipedia.org", "")]
    [DataRow("en.wikipedia.org", "   ")]
    [DataRow("en.wikipedia.org", null)]
    public void CreatePageUri_HostileOrUnsupportedHosts_ReturnsNull(string? sourceProject, string? pageTitle)
    {
        var result = SourceReferencePolicy.CreatePageUri(sourceProject!, pageTitle!);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void CreatePageUri_PathOrQueryInjectionInTitle_SafelyEscapesTitle()
    {
        var result = SourceReferencePolicy.CreatePageUri("en.wikipedia.org", "../evil?a=b#c");

        Assert.IsNotNull(result);
        Assert.AreEqual("https://en.wikipedia.org/wiki/..%2Fevil%3Fa%3Db%23c", result.AbsoluteUri);
    }

    [TestMethod]
    [DataRow("Wikipedia contributors; text available under Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0).")]
    [DataRow("Text is available under the Creative Commons Attribution-ShareAlike License.")]
    [DataRow("Licensed under CC BY-SA 4.0")]
    public void GetLicenseReference_RecognizedAttribution_ReturnsCcBySaName(string attribution)
    {
        var name = SourceReferencePolicy.GetLicenseReference(attribution);
        var uri = SourceReferencePolicy.GetLicenseUri(attribution);

        Assert.AreEqual("Creative Commons Attribution-ShareAlike 4.0 International", name);
        Assert.IsNotNull(uri);
        Assert.AreEqual("https://creativecommons.org/licenses/by-sa/4.0/", uri.AbsoluteUri);
    }

    [TestMethod]
    [DataRow("Public Domain")]
    [DataRow("MIT License")]
    [DataRow("Copyright 2026 Author")]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    public void GetLicenseReference_UnrecognizedAttribution_ReturnsNull(string? attribution)
    {
        var name = SourceReferencePolicy.GetLicenseReference(attribution!);
        var uri = SourceReferencePolicy.GetLicenseUri(attribution!);

        Assert.IsNull(name);
        Assert.IsNull(uri);
    }
}
