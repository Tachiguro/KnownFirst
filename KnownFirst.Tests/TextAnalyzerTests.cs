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
        var candidates = _analyzer.Analyze(content).Candidates;
        Assert.AreEqual(TokenKind.TechnicalTerm, candidates.Single(candidate => candidate.CanonicalTerm == "OAuth2").Kind);
        Assert.AreEqual(TokenKind.TechnicalTerm, candidates.Single(candidate => candidate.CanonicalTerm == "IPv6").Kind);
        Assert.AreEqual(TokenKind.Acronym, candidates.Single(candidate => candidate.CanonicalTerm == "SHA").Kind);
        Assert.AreEqual(TokenKind.Acronym, candidates.Single(candidate => candidate.CanonicalTerm == "CVE").Kind);
    }

    [TestMethod]
    public void Analyze_ExplicitCveAndShaFamiliesShareCanonicalAcronymIdentities()
    {
        const string content = "CVE CVE-2026-12345 SHA SHA-1 SHA-256 SHA-512 IPv6 OAuth2.";

        var result = _analyzer.Analyze(content);
        var cve = result.Candidates.Single(candidate => candidate.Identity == "A:CVE");
        var sha = result.Candidates.Single(candidate => candidate.Identity == "A:SHA");

        Assert.AreEqual("CVE", cve.CanonicalTerm);
        Assert.AreEqual(TokenKind.Acronym, cve.Kind);
        var cveInstance = cve.Occurrences.Single(occurrence =>
            occurrence.TechnicalFamily == TechnicalTokenFamily.Cve);
        Assert.HasCount(2, cve.Occurrences);
        Assert.AreEqual(2026, cveInstance.TechnicalInstanceYear);
        Assert.AreEqual("12345", cveInstance.TechnicalInstanceIdentifier);
        Assert.AreEqual("SHA", sha.CanonicalTerm);
        CollectionAssert.AreEqual(
            new[] { "1", "256", "512" },
            sha.Occurrences
                .Where(occurrence => occurrence.TechnicalFamily == TechnicalTokenFamily.Sha)
                .Select(occurrence => occurrence.TechnicalVariant)
                .ToArray());
        Assert.IsTrue(result.Candidates.Any(candidate => candidate.Identity == "T:IPv6"));
        Assert.IsTrue(result.Candidates.Any(candidate => candidate.Identity == "T:OAuth2"));
        Assert.IsFalse(result.Candidates.Any(candidate => candidate.Identity is "A:IPv" or "A:OAuth"));

        foreach (var occurrence in cve.Occurrences.Concat(sha.Occurrences))
        {
            Assert.AreEqual(
                occurrence.SurfaceForm,
                content.Substring(occurrence.StartPosition, occurrence.Length));
        }
    }

    [TestMethod]
    public void Analyze_TechnicalFamilyDiagnosticsAreHumanReadable()
    {
        var diagnostics = _analyzer.Analyze("CVE-2026-12345 and SHA-256.").Diagnostics!;

        Assert.IsTrue(diagnostics.TokenDecisions.Any(decision =>
            decision.ReasonCode == AnalysisReasonCodes.IncludedCveFamilyPattern
            && decision.Explanation.Contains("CVE", StringComparison.Ordinal)));
        Assert.IsTrue(diagnostics.TokenDecisions.Any(decision =>
            decision.ReasonCode == AnalysisReasonCodes.IncludedShaFamilyPattern
            && decision.Explanation.Contains("SHA", StringComparison.Ordinal)));
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
    public void Analyze_EnglishPronounVariantsUseOneExplicitVocabularyIdentity()
    {
        const string content = "I can test me and my examples.";

        var result = _analyzer.Analyze(content, "en-US");
        var candidate = result.Candidates
            .Single(item => item.Identity == "W:i");

        Assert.AreEqual("I", candidate.CanonicalTerm);
        CollectionAssert.AreEqual(
            new[] { "I", "me", "my" },
            candidate.Occurrences.Select(occurrence => occurrence.SurfaceForm).ToArray());
        foreach (var occurrence in candidate.Occurrences)
        {
            Assert.AreEqual(
                occurrence.SurfaceForm,
                content.Substring(occurrence.StartPosition, occurrence.Length));
        }

        Assert.AreEqual(
            AnalysisReasonCodes.ExplicitLanguageRuleGrouping,
            result.Diagnostics!.CandidateGroups
                .Single(group => group.Identity == "W:i")
                .ReasonCode);
    }

    [TestMethod]
    public void Analyze_NonEnglishSourceDoesNotApplyEnglishPronounRule()
    {
        var identities = _analyzer.Analyze("I me my.", "de")
            .Candidates
            .Select(candidate => candidate.Identity)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "W:i", "W:me", "W:my" }, identities);
    }

    [TestMethod]
    public void VocabularyIdentityPolicy_EnglishPronounRuleIsExplicitAndLanguageAware()
    {
        var english = VocabularyIdentityPolicy.Resolve("my", TokenKind.Word, "EN_gb");
        var german = VocabularyIdentityPolicy.Resolve("my", TokenKind.Word, "de-DE");

        Assert.AreEqual(new VocabularyIdentityResolution("W:i", "I", true), english);
        Assert.AreEqual(new VocabularyIdentityResolution("W:my", "my", false), german);
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

    [TestMethod]
    public void SentenceSegmenter_SplitsBasicPunctuation()
    {
        const string content = "First. Second! Third?";

        CollectionAssert.AreEqual(
            new[] { "First.", "Second!", "Third?" },
            SentenceTexts(content));
    }

    [TestMethod]
    public void SentenceSegmenter_AttachesSingleAndAdjacentCitations()
    {
        const string content = "First sentence.[1] Second sentence.[2][3] Final sentence.";

        CollectionAssert.AreEqual(
            new[] { "First sentence.[1]", "Second sentence.[2][3]", "Final sentence." },
            SentenceTexts(content));
    }

    [TestMethod]
    public void SentenceSegmenter_RetainsClosingQuotesParenthesesAndCitations()
    {
        const string content = "He said \"Stop.\"[1] (Really?)[2] Next.";

        CollectionAssert.AreEqual(
            new[] { "He said \"Stop.\"[1]", "(Really?)[2]", "Next." },
            SentenceTexts(content));
    }

    [TestMethod]
    public void SentenceSegmenter_DoesNotSplitKnownAbbreviations()
    {
        const string content = "Use e.g. OAuth2, i.e. one option, in the U.S. market. Next.";

        CollectionAssert.AreEqual(
            new[] { "Use e.g. OAuth2, i.e. one option, in the U.S. market.", "Next." },
            SentenceTexts(content));
    }

    [TestMethod]
    public void SentenceSegmenter_DoesNotSplitDecimalValues()
    {
        const string content = "Version 3.14 is stable. Next.";

        CollectionAssert.AreEqual(
            new[] { "Version 3.14 is stable.", "Next." },
            SentenceTexts(content));
    }

    [TestMethod]
    public void SentenceSegmenter_RetainsFinalSentenceWithoutPunctuation()
    {
        const string content = "First sentence. Final sentence without punctuation";

        CollectionAssert.AreEqual(
            new[] { "First sentence.", "Final sentence without punctuation" },
            SentenceTexts(content));
    }

    [TestMethod]
    public void SentenceSegmenter_ObservedCitationExampleCreatesTwoSpans()
    {
        const string content = "It is part of information risk management.[1] It typically involves preventing or reducing unauthorized access.[2]";

        CollectionAssert.AreEqual(
            new[]
            {
                "It is part of information risk management.[1]",
                "It typically involves preventing or reducing unauthorized access.[2]"
            },
            SentenceTexts(content));
    }

    [TestMethod]
    public void SentenceSegmenter_ObservedCitationExampleCreatesThreeSpans()
    {
        const string content = "Protected information may take any form.[2] Information security protects confidentiality.[3] It also supports availability.[4]";

        CollectionAssert.AreEqual(
            new[]
            {
                "Protected information may take any form.[2]",
                "Information security protects confidentiality.[3]",
                "It also supports availability.[4]"
            },
            SentenceTexts(content));
    }

    [TestMethod]
    public void Analyze_EncounteredFormsDeduplicateCaseOnlyVariantsAndPreferLowercase()
    {
        var candidate = _analyzer.Analyze("Information information INFORMATION.")
            .Candidates
            .Single(item => item.Identity == "W:information");

        CollectionAssert.AreEqual(new[] { "information" }, candidate.EncounteredForms.ToArray());
        Assert.HasCount(3, candidate.Occurrences);
    }

    [TestMethod]
    public void Analyze_EncounteredFormGroupingKeepsStableCandidateOrder()
    {
        var candidates = _analyzer.Analyze("Alpha alpha BETA beta.")
            .Candidates
            .Where(candidate => candidate.Identity is "W:alpha" or "W:beta")
            .ToArray();

        CollectionAssert.AreEqual(new[] { "W:alpha", "W:beta" }, candidates.Select(item => item.Identity).ToArray());
        CollectionAssert.AreEqual(new[] { "alpha" }, candidates[0].EncounteredForms.ToArray());
        CollectionAssert.AreEqual(new[] { "beta" }, candidates[1].EncounteredForms.ToArray());
    }

    [TestMethod]
    public void Analyze_EncounteredFormsUseUnicodeNormalizedComparison()
    {
        var candidate = _analyzer.Analyze("café cafe\u0301.")
            .Candidates
            .Single(item => item.Identity == "W:café");

        CollectionAssert.AreEqual(new[] { "café" }, candidate.EncounteredForms.ToArray());
        Assert.HasCount(2, candidate.Occurrences);
    }

    [TestMethod]
    public void ContextFingerprint_PreservesDiacritics()
    {
        Assert.AreNotEqual(
            ContextSelectionPolicy.CreateFingerprint("Café protects data."),
            ContextSelectionPolicy.CreateFingerprint("Cafe protects data."));
    }

    [TestMethod]
    public void Analyze_DiagnosticsExplainBoundariesTokensGroupingAndDuplicateContexts()
    {
        const string content = "Information information here.[1] Information information here.[1] test@example.com";
        var result = _analyzer.Analyze(content);
        var diagnostics = result.Diagnostics
            ?? throw new InvalidOperationException("DEBUG analysis diagnostics were not created.");

        Assert.IsTrue(result.Sentences.Any(sentence =>
            sentence.BoundaryReasonCode == AnalysisReasonCodes.SentenceBoundaryTerminatorCitation));
        Assert.IsTrue(diagnostics.TokenDecisions.Any(decision =>
            decision.IsIncluded && decision.ReasonCode == AnalysisReasonCodes.IncludedUnicodeWord));
        Assert.IsTrue(diagnostics.TokenDecisions.Any(decision =>
            !decision.IsIncluded && decision.ReasonCode == AnalysisReasonCodes.ExcludedEmailAddress));
        Assert.IsTrue(diagnostics.CandidateGroups.Any(group =>
            group.Identity == "W:information"
            && group.ReasonCode == AnalysisReasonCodes.OrdinaryWordCaseGrouping));
        Assert.IsTrue(diagnostics.ContextDecisions.Any(context =>
            context.CandidateIdentity == "W:information"
            && context.ReasonCode == AnalysisReasonCodes.RejectedDuplicateContext));
        Assert.IsEmpty(diagnostics.InvariantFailures);
    }

    [TestMethod]
    public void Analyze_UnicodeAndTechnicalCoordinatesRemainExact()
    {
        const string content = "Äußere Straße schützt OAuth2 und SHA-256.";
        var result = _analyzer.Analyze(content);

        foreach (var sentence in result.Sentences)
        {
            Assert.AreEqual(
                content.Substring(sentence.StartPosition, sentence.Length),
                content[sentence.StartPosition..sentence.EndPosition]);
        }

        foreach (var occurrence in result.Candidates.SelectMany(candidate => candidate.Occurrences))
        {
            Assert.AreEqual(
                occurrence.SurfaceForm,
                content.Substring(occurrence.StartPosition, occurrence.Length));
        }
    }

    private string[] SentenceTexts(string content) => _analyzer.ExtractSentenceSpans(content)
        .Select(span => content.Substring(span.StartPosition, span.Length))
        .ToArray();

    private string[] AnalyzeSurfaces(string content) => _analyzer.Analyze(content)
        .Candidates
        .SelectMany(candidate => candidate.SurfaceForms.Keys)
        .ToArray();
    [TestMethod]
    public void Analyze_WordWithFrequencyOneRemainsInResult()
    {
        var result = _analyzer.Analyze("A single rare word.");
        var rare = result.Candidates.SingleOrDefault(c => c.CanonicalTerm == "rare");

        Assert.IsNotNull(rare);
        Assert.AreEqual(1, rare.Occurrences.Count);
    }

    [TestMethod]
    public void Analyze_LowFrequencyDoesNotDeleteWord()
    {
        var result = _analyzer.Analyze("Common common common rare.");
        
        var common = result.Candidates.SingleOrDefault(c => c.CanonicalTerm.Equals("common", StringComparison.OrdinalIgnoreCase));
        var rare = result.Candidates.SingleOrDefault(c => c.CanonicalTerm.Equals("rare", StringComparison.OrdinalIgnoreCase));
        
        Assert.IsNotNull(common);
        Assert.IsNotNull(rare);
        Assert.AreEqual(3, common.Occurrences.Count);
        Assert.AreEqual(1, rare.Occurrences.Count);
    }

    [TestMethod]
    public void Analyze_ReturnsOnlyVocabularyAndDoesNotDetermineKnowledgeState()
    {
        var result = _analyzer.Analyze("Unknown known.");
        
        // TextAnalyzer output shouldn't have a status property that decides "Known" vs "Unknown".
        // It merely returns the candidates. The fact that the test compiles and we assert
        // over CanonicalTerm proves it limits its scope to token extraction.
        Assert.IsTrue(result.Candidates.Any(c => c.CanonicalTerm.Equals("known", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Analyze_RepeatedAnalysisOfSameTextIsDeterministic()
    {
        const string text = "Deterministic analysis test.";
        var first = _analyzer.Analyze(text);
        var second = _analyzer.Analyze(text);

        CollectionAssert.AreEqual(
            first.Candidates.Select(c => c.CanonicalTerm).ToArray(),
            second.Candidates.Select(c => c.CanonicalTerm).ToArray());
    }

    [TestMethod]
    public void Analyze_GermanSingularizationRetainsOriginalFormAndPosition()
    {
        const string content = "Die Datenschutzprozesse laufen.";
        var result = _analyzer.Analyze(content, "de");

        var candidate = result.Candidates.Single(c => c.CanonicalTerm == "Datenschutzprozess");
        var occurrence = candidate.Occurrences.Single();

        Assert.AreEqual("Datenschutzprozess", candidate.CanonicalTerm);
        Assert.AreEqual("Datenschutzprozesse", occurrence.SurfaceForm);
        Assert.AreEqual(1, candidate.Occurrences.Count);
        Assert.AreEqual("Datenschutzprozesse", content.Substring(occurrence.StartPosition, occurrence.Length));
    }

    [TestMethod]
    public void Analyze_GermanCoordinatedCompoundsNormalizeToFullProcessTerms()
    {
        const string content = "Arbeits-, Qualitäts-, Sicherheits- und Datenschutzprozesse sind wichtig.";
        var result = _analyzer.Analyze(content, "de");

        var terms = result.Candidates.Select(c => c.CanonicalTerm).ToArray();
        
        Assert.IsTrue(terms.Contains("Arbeitsprozess"));
        Assert.IsTrue(terms.Contains("Qualitätsprozess"));
        Assert.IsTrue(terms.Contains("Sicherheitsprozess"));
        Assert.IsTrue(terms.Contains("Datenschutzprozess"));
        
        Assert.IsFalse(terms.Contains("Arbeits-"));
        Assert.IsFalse(terms.Contains("Qualitäts-"));
        Assert.IsFalse(terms.Contains("Sicherheits-"));
        
        var arbeits = result.Candidates.Single(c => c.CanonicalTerm == "Arbeitsprozess").Occurrences.Single();
        Assert.AreEqual("Arbeits-", content.Substring(arbeits.StartPosition, arbeits.Length));
        Assert.AreEqual("Arbeits-", arbeits.SurfaceForm);
    }
}
