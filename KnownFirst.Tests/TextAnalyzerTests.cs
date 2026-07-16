using KnownFirst.Core.Text;

namespace KnownFirst.Tests;

[TestClass]
public sealed class TextAnalyzerTests
{
    private readonly TextAnalyzer _analyzer = new();

    [TestMethod]
    public void Analyze_DoesNotChangeOriginalText()
    {
        const string original = "  Häuser, OAuth2!\r\n\r\nПривет κόσμος 42.  ";
        var snapshot = new string(original.AsSpan());

        _analyzer.Analyze(original);

        Assert.AreEqual(original, snapshot);
    }

    [TestMethod]
    public void Analyze_SentenceAndOccurrenceOffsetsReturnExactOriginalSubstrings()
    {
        const string content = "First sentence.  Die Häuser stehen hier!";
        var result = _analyzer.Analyze(content);

        CollectionAssert.AreEqual(
            new[] { "First sentence.", "Die Häuser stehen hier!" },
            result.Sentences
                .Select(span => content.Substring(span.StartPosition, span.Length))
                .ToArray());

        foreach (var occurrence in result.Candidates.SelectMany(candidate => candidate.Occurrences))
        {
            Assert.AreEqual(
                occurrence.SurfaceForm,
                content.Substring(occurrence.StartPosition, occurrence.Length));
        }
    }

    [TestMethod]
    public void Analyze_PreservesUmlautsEszettAccentedLatinGreekAndCyrillic()
    {
        const string content = "Häuser Straße café κόσμος Привет";
        var surfaces = AnalyzeSurfaces(content);

        CollectionAssert.IsSubsetOf(
            new[] { "Häuser", "Straße", "café", "κόσμος", "Привет" },
            surfaces);
    }

    [TestMethod]
    public void Analyze_ExcludesUrlsEmailsAndStandaloneNumbers()
    {
        const string content = "Contact test@example.com, visit https://example.com/path, or use 42 and 2026.";
        var surfaces = AnalyzeSurfaces(content);

        Assert.IsFalse(surfaces.Contains("test", StringComparer.Ordinal));
        Assert.IsFalse(surfaces.Contains("example", StringComparer.Ordinal));
        Assert.IsFalse(surfaces.Contains("https", StringComparer.Ordinal));
        Assert.IsFalse(surfaces.Contains("42", StringComparer.Ordinal));
        Assert.IsFalse(surfaces.Contains("2026", StringComparer.Ordinal));
    }

    [TestMethod]
    public void Analyze_ExcludesWhitespacePunctuationAndSymbolOnlyValues()
    {
        var surfaces = AnalyzeSurfaces("   ... © ™ + = — 42 word");

        CollectionAssert.AreEqual(new[] { "word" }, surfaces);
    }

    [TestMethod]
    public void Analyze_RetainsTechnicalAlphanumericTerms()
    {
        const string content = "OAuth2 IPv6 SHA-256 CVE-2026-12345";
        var surfaces = AnalyzeSurfaces(content);

        CollectionAssert.AreEqual(
            new[] { "OAuth2", "IPv6", "SHA-256", "CVE-2026-12345" },
            surfaces);
        Assert.IsTrue(_analyzer.Analyze(content).Candidates.All(
            candidate => candidate.Kind == TokenKind.TechnicalTerm));
    }

    [TestMethod]
    public void Analyze_ItAndItRemainDistinct()
    {
        var candidates = _analyzer.Analyze("IT helps and it works.").Candidates;

        Assert.AreEqual(2, candidates.Count(candidate => candidate.CanonicalTerm is "IT" or "it"));
        Assert.AreNotEqual(
            candidates.Single(candidate => candidate.CanonicalTerm == "IT").Identity,
            candidates.Single(candidate => candidate.CanonicalTerm == "it").Identity);
    }

    [TestMethod]
    public void Analyze_UsAndUsRemainDistinct()
    {
        var candidates = _analyzer.Analyze("US teams help us.").Candidates;

        Assert.AreNotEqual(
            candidates.Single(candidate => candidate.CanonicalTerm == "US").Identity,
            candidates.Single(candidate => candidate.CanonicalTerm == "us").Identity);
    }

    [TestMethod]
    public void Analyze_EquivalentDuplicatesAreReviewedOnceAndCounted()
    {
        var candidate = _analyzer.Analyze("Network network NETWORK.")
            .Candidates
            .Single(candidate => candidate.Identity == "W:network");

        Assert.HasCount(3, candidate.Occurrences);
        Assert.HasCount(3, candidate.SurfaceForms);
        Assert.AreEqual(1, candidate.SurfaceForms["Network"]);
        Assert.AreEqual(1, candidate.SurfaceForms["network"]);
        Assert.AreEqual(1, candidate.SurfaceForms["NETWORK"]);
    }

    [TestMethod]
    public void Analyze_RequiredPreflightCorpusProducesExactCandidatesOccurrencesAndOffsets()
    {
        const string content = "IT protects smart systems. It protects smart networks. Smart systems use OAuth2.";

        var result = _analyzer.Analyze(content);
        var occurrences = result.Candidates.SelectMany(candidate => candidate.Occurrences).ToArray();

        Assert.HasCount(3, result.Sentences);
        Assert.AreEqual(12, result.OccurrenceCount);
        Assert.HasCount(12, occurrences);
        Assert.HasCount(8, result.Candidates);
        Assert.AreNotEqual(
            result.Candidates.Single(candidate => candidate.CanonicalTerm == "IT").Identity,
            result.Candidates.Single(candidate => candidate.CanonicalTerm == "It").Identity);
        Assert.HasCount(2, result.Candidates.Single(candidate => candidate.Identity == "W:protects").Occurrences);
        Assert.HasCount(3, result.Candidates.Single(candidate => candidate.Identity == "W:smart").Occurrences);
        Assert.HasCount(2, result.Candidates.Single(candidate => candidate.Identity == "W:systems").Occurrences);
        Assert.HasCount(1, result.Candidates.Single(candidate => candidate.Identity == "W:networks").Occurrences);
        Assert.HasCount(1, result.Candidates.Single(candidate => candidate.Identity == "W:use").Occurrences);
        Assert.HasCount(1, result.Candidates.Single(candidate => candidate.Identity == "T:OAuth2").Occurrences);

        var firstIt = occurrences.Single(occurrence => occurrence.SurfaceForm == "IT");
        var firstSmart = occurrences.Single(occurrence => occurrence.StartPosition == 12);
        var oauth = occurrences.Single(occurrence => occurrence.SurfaceForm == "OAuth2");
        Assert.AreEqual((0, 2), (firstIt.StartPosition, firstIt.Length));
        Assert.AreEqual((12, 5), (firstSmart.StartPosition, firstSmart.Length));
        Assert.AreEqual((73, 6), (oauth.StartPosition, oauth.Length));
    }

    [TestMethod]
    public void Analyze_RetainsKnownAbbreviationsWithTheirOriginalSurface()
    {
        const string content = "Dr. Smith uses e.g. OAuth2.";
        var result = _analyzer.Analyze(content);

        Assert.IsTrue(result.Candidates.Any(candidate =>
            candidate.CanonicalTerm == "Dr." && candidate.Kind == TokenKind.Abbreviation));
        Assert.IsTrue(result.Candidates.Any(candidate =>
            candidate.CanonicalTerm == "e.g." && candidate.Kind == TokenKind.Abbreviation));
        Assert.AreEqual("Dr. Smith uses e.g. OAuth2.",
            content.Substring(result.Sentences[0].StartPosition, result.Sentences[0].Length));
    }

    [TestMethod]
    public void Analyze_UncertainWordFamiliesAreNotMerged()
    {
        var identities = _analyzer.Analyze(
                "network networking networked Netzwerk Netzwerken Häuser Haus")
            .Candidates
            .Select(candidate => candidate.Identity)
            .ToArray();

        Assert.AreEqual(7, identities.Distinct(StringComparer.Ordinal).Count());
    }

    private string[] AnalyzeSurfaces(string content) => _analyzer.Analyze(content)
        .Candidates
        .SelectMany(candidate => candidate.SurfaceForms.Keys)
        .ToArray();
}
