using KnownFirst.Core.Text;
using KnownFirst.Tests.AnalysisEvidence;

namespace KnownFirst.Tests.Corpus;

[TestClass]
public sealed class EnglishGoldCorpusTests
{
    private TextAnalyzer _analyzer = null!;

    [TestInitialize]
    public void Initialize()
    {
        _analyzer = new TextAnalyzer();
    }

    [TestMethod]
    public void EN001_RepeatedWordsAndContexts_AssertStructuralExpectations()
    {
        var corpusCase = EnglishGoldCorpusFixtures.EN001_RepeatedWordsAndContexts;
        var result = EnglishGoldCorpusFixtures.ExecuteCase(corpusCase, _analyzer);
        var evidence = result.AnalyzerEvidence;

        Assert.AreEqual(CorpusExpectationAuthority.BindingContract, result.Authority);
        Assert.AreEqual(corpusCase.Input, evidence.Input.Text);
        Assert.AreEqual("en", evidence.Input.SourceLanguage);
        Assert.IsTrue(evidence.InvariantsPassed);
        Assert.HasCount(3, evidence.Sentences);

        Assert.AreEqual("Information protects systems.[1]", evidence.Sentences[0].Text);
        Assert.AreEqual(AnalysisReasonCodes.SentenceBoundaryTerminatorCitation, evidence.Sentences[0].BoundaryReasonCode);
        Assert.AreEqual("Protected information helps users.[2]", evidence.Sentences[1].Text);
        Assert.AreEqual(AnalysisReasonCodes.SentenceBoundaryTerminatorCitation, evidence.Sentences[1].BoundaryReasonCode);
        Assert.AreEqual("INFORMATION remains available.", evidence.Sentences[2].Text);
        Assert.AreEqual(AnalysisReasonCodes.SentenceBoundaryTerminatorEnd, evidence.Sentences[2].BoundaryReasonCode);

        foreach (var sentence in evidence.Sentences)
        {
            Assert.IsTrue(sentence.ExactSubstringMatch);
            Assert.AreEqual(sentence.Text, corpusCase.Input.Substring(sentence.StartPosition, sentence.Length));
        }

        var information = evidence.Candidates.Single(c => c.Identity == "W:information");
        Assert.AreEqual("Information", information.CanonicalTerm);
        Assert.AreEqual(TokenKind.Word, information.Kind);
        Assert.AreEqual(CandidateProvenanceKind.Direct, information.Provenance);
        Assert.AreEqual(3, information.OccurrenceCount);
        Assert.HasCount(3, information.Occurrences);
        CollectionAssert.AreEqual(
            new[] { "Information", "information", "INFORMATION" },
            information.Occurrences.Select(o => o.SurfaceForm).ToArray());

        foreach (var occ in information.Occurrences)
        {
            Assert.IsTrue(occ.ExactSubstringMatch);
            Assert.AreEqual(occ.SurfaceForm, corpusCase.Input.Substring(occ.StartPosition, occ.Length));
        }

        var infoGroup = evidence.CandidateGroups.Single(g => g.Identity == "W:information");
        Assert.AreEqual(AnalysisReasonCodes.OrdinaryWordCaseGrouping, infoGroup.ReasonCode);
        CollectionAssert.AreEqual(
            new[] { "Information", "information", "INFORMATION" },
            infoGroup.FormsBeforeDeduplication.ToArray());
        CollectionAssert.AreEqual(
            new[] { "information" },
            infoGroup.FormsAfterDeduplication.ToArray());

        // Frequency-1 English vocabulary candidates are retained without filtering
        var freq1Identities = new[]
        {
            "W:protects",
            "W:systems",
            "W:protected",
            "W:helps",
            "W:users",
            "W:remains",
            "W:available"
        };
        foreach (var identity in freq1Identities)
        {
            var candidate = evidence.Candidates.Single(c => c.Identity == identity);
            Assert.AreEqual(1, candidate.OccurrenceCount);
            Assert.HasCount(1, candidate.Occurrences);
            Assert.IsTrue(candidate.Occurrences[0].ExactSubstringMatch);
        }

        // Three distinct single-sentence contexts selected for "W:information"
        var infoContexts = evidence.ContextDecisions
            .Where(cd => cd.CandidateIdentity == "W:information")
            .ToArray();
        Assert.HasCount(3, infoContexts);
        Assert.IsTrue(infoContexts.All(cd => cd.IsSelected));
        Assert.AreEqual(AnalysisReasonCodes.SelectedFirstUniqueContext, infoContexts[0].ReasonCode);
        Assert.AreEqual(AnalysisReasonCodes.SelectedUniqueContext, infoContexts[1].ReasonCode);
        Assert.AreEqual(AnalysisReasonCodes.SelectedUniqueContext, infoContexts[2].ReasonCode);
        Assert.AreEqual(3, infoContexts.Select(cd => cd.Fingerprint).Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    public void EN002_UnicodeOffsetsApostrophesPunctuation_AssertStructuralExpectations()
    {
        var corpusCase = EnglishGoldCorpusFixtures.EN002_UnicodeOffsetsApostrophesPunctuation;
        var result = EnglishGoldCorpusFixtures.ExecuteCase(corpusCase, _analyzer);
        var evidence = result.AnalyzerEvidence;

        Assert.AreEqual(CorpusExpectationAuthority.VerifiedCurrentBehavior, result.Authority);
        Assert.AreEqual(corpusCase.Input, evidence.Input.Text);
        Assert.AreEqual("en", evidence.Input.SourceLanguage);
        Assert.IsTrue(evidence.InvariantsPassed);
        Assert.HasCount(1, evidence.Sentences);
        Assert.AreEqual(AnalysisReasonCodes.SentenceBoundaryFinalRemainder, evidence.Sentences[0].BoundaryReasonCode);
        Assert.IsTrue(evidence.Sentences[0].ExactSubstringMatch);

        // Straight apostrophe in "don't" and typographic apostrophe in "user’s" remain intact inside word tokens
        var dont = evidence.Candidates.Single(c => c.Identity == "W:don't");
        Assert.AreEqual("don't", dont.CanonicalTerm);
        Assert.AreEqual(TokenKind.Word, dont.Kind);
        Assert.AreEqual(1, dont.OccurrenceCount);
        Assert.IsTrue(dont.Occurrences[0].ExactSubstringMatch);
        Assert.AreEqual("don't", corpusCase.Input.Substring(dont.Occurrences[0].StartPosition, dont.Occurrences[0].Length));

        var users = evidence.Candidates.Single(c => c.Identity == "W:user\u2019s");
        Assert.AreEqual("user\u2019s", users.CanonicalTerm);
        Assert.AreEqual(TokenKind.Word, users.Kind);
        Assert.AreEqual(1, users.OccurrenceCount);
        Assert.IsTrue(users.Occurrences[0].ExactSubstringMatch);
        Assert.AreEqual("user\u2019s", corpusCase.Input.Substring(users.Occurrences[0].StartPosition, users.Occurrences[0].Length));

        // Composed ("café", UTF-16 length 4) and decomposed ("cafe\u0301", UTF-16 length 5) merge under Form C identity
        var cafe = evidence.Candidates.Single(c => c.Identity == "W:caf\u00e9");
        Assert.AreEqual("caf\u00e9", cafe.CanonicalTerm);
        Assert.AreEqual(2, cafe.OccurrenceCount);
        Assert.HasCount(2, cafe.Occurrences);

        var composedOcc = cafe.Occurrences[0];
        var decomposedOcc = cafe.Occurrences[1];
        Assert.AreEqual(4, composedOcc.Length);
        Assert.AreEqual("caf\u00e9", composedOcc.SurfaceForm);
        Assert.IsTrue(composedOcc.ExactSubstringMatch);
        Assert.AreEqual(composedOcc.SurfaceForm, corpusCase.Input.Substring(composedOcc.StartPosition, composedOcc.Length));

        Assert.AreEqual(5, decomposedOcc.Length);
        Assert.AreEqual("cafe\u0301", decomposedOcc.SurfaceForm);
        Assert.IsTrue(decomposedOcc.ExactSubstringMatch);
        Assert.AreEqual(decomposedOcc.SurfaceForm, corpusCase.Input.Substring(decomposedOcc.StartPosition, decomposedOcc.Length));

        var cafeGroup = evidence.CandidateGroups.Single(g => g.Identity == "W:caf\u00e9");
        Assert.HasCount(2, cafeGroup.FormsBeforeDeduplication);
        Assert.HasCount(1, cafeGroup.FormsAfterDeduplication);
        Assert.AreEqual("caf\u00e9", cafeGroup.FormsAfterDeduplication[0]);

        // Surrounding punctuation and quotation marks are excluded with explicit reason code
        var punctuationExclusions = evidence.TokenDecisions
            .Where(td => !td.IsIncluded && td.ReasonCode == AnalysisReasonCodes.ExcludedPunctuationOnly)
            .ToArray();
        Assert.IsGreaterThanOrEqualTo(2, punctuationExclusions.Length);
        Assert.IsTrue(punctuationExclusions.All(td => td.ExactSubstringMatch));
    }

    [TestMethod]
    public void EN003_BoundariesLineEndingsAbbreviationsDecimals_AssertStructuralExpectations()
    {
        var corpusCase = EnglishGoldCorpusFixtures.EN003_BoundariesLineEndingsAbbreviationsDecimals;
        var result = EnglishGoldCorpusFixtures.ExecuteCase(corpusCase, _analyzer);
        var evidence = result.AnalyzerEvidence;

        Assert.AreEqual(CorpusExpectationAuthority.BindingContract, result.Authority);
        Assert.AreEqual(corpusCase.Input, evidence.Input.Text);
        Assert.IsTrue(corpusCase.Input.Contains("\r\n", StringComparison.Ordinal));
        Assert.IsTrue(corpusCase.Input.Contains("\n", StringComparison.Ordinal));
        Assert.IsTrue(corpusCase.Input.Contains("\r", StringComparison.Ordinal));
        Assert.IsTrue(evidence.InvariantsPassed);

        Assert.HasCount(4, evidence.Sentences);
        Assert.AreEqual("Overview", evidence.Sentences[0].Text);
        Assert.AreEqual(AnalysisReasonCodes.SentenceBoundaryTerminatorLineBreak, evidence.Sentences[0].BoundaryReasonCode);
        Assert.AreEqual("Dr. Smith used e.g. version 3.14, i.e. option 7, in the U.S. lab.", evidence.Sentences[1].Text);
        Assert.AreEqual(AnalysisReasonCodes.SentenceBoundaryTerminatorWhitespace, evidence.Sentences[1].BoundaryReasonCode);
        Assert.AreEqual("Second line", evidence.Sentences[2].Text);
        Assert.AreEqual(AnalysisReasonCodes.SentenceBoundaryTerminatorLineBreak, evidence.Sentences[2].BoundaryReasonCode);
        Assert.AreEqual("Final note", evidence.Sentences[3].Text);
        Assert.AreEqual(AnalysisReasonCodes.SentenceBoundaryFinalRemainder, evidence.Sentences[3].BoundaryReasonCode);

        foreach (var sentence in evidence.Sentences)
        {
            Assert.IsTrue(sentence.ExactSubstringMatch);
            Assert.AreEqual(sentence.Text, corpusCase.Input.Substring(sentence.StartPosition, sentence.Length));
        }

        // Known English abbreviations retained as TokenKind.Abbreviation without splitting sentence 1
        var abbreviations = evidence.Candidates
            .Where(c => c.Kind == TokenKind.Abbreviation)
            .ToDictionary(c => c.CanonicalTerm, c => c, StringComparer.Ordinal);
        CollectionAssert.AreEquivalent(
            new[] { "Dr.", "e.g.", "i.e.", "U.S." },
            abbreviations.Keys.ToArray());
        Assert.AreEqual("B:Dr.", abbreviations["Dr."].Identity);
        Assert.AreEqual("B:e.g.", abbreviations["e.g."].Identity);
        Assert.AreEqual("B:i.e.", abbreviations["i.e."].Identity);
        Assert.AreEqual("B:U.S.", abbreviations["U.S."].Identity);

        // Decimal value 3.14 and integer 7 excluded as standalone numbers
        var standaloneNumbers = evidence.TokenDecisions
            .Where(td => !td.IsIncluded && td.ReasonCode == AnalysisReasonCodes.ExcludedStandaloneNumber)
            .Select(td => td.RawValue)
            .ToArray();
        CollectionAssert.AreEqual(new[] { "3.14", "7" }, standaloneNumbers);
    }

    [TestMethod]
    public void EN004_TechnicalTokensAndExclusions_AssertStructuralExpectations()
    {
        var corpusCase = EnglishGoldCorpusFixtures.EN004_TechnicalTokensAndExclusions;
        var result = EnglishGoldCorpusFixtures.ExecuteCase(corpusCase, _analyzer);
        var evidence = result.AnalyzerEvidence;

        Assert.AreEqual(CorpusExpectationAuthority.BindingContract, result.Authority);
        Assert.AreEqual(corpusCase.Input, evidence.Input.Text);
        Assert.IsTrue(evidence.InvariantsPassed);
        Assert.HasCount(1, evidence.Sentences);

        var oauth2 = evidence.Candidates.Single(c => c.Identity == "T:OAuth2");
        Assert.AreEqual("OAuth2", oauth2.CanonicalTerm);
        Assert.AreEqual(TokenKind.TechnicalTerm, oauth2.Kind);
        Assert.AreEqual(1, oauth2.OccurrenceCount);

        var ipv6 = evidence.Candidates.Single(c => c.Identity == "T:IPv6");
        Assert.AreEqual("IPv6", ipv6.CanonicalTerm);
        Assert.AreEqual(TokenKind.TechnicalTerm, ipv6.Kind);
        Assert.AreEqual(1, ipv6.OccurrenceCount);

        var endToEnd = evidence.Candidates.Single(c => c.Identity == "T:end-to-end");
        Assert.AreEqual("end-to-end", endToEnd.CanonicalTerm);
        Assert.AreEqual(TokenKind.TechnicalTerm, endToEnd.Kind);
        Assert.AreEqual(1, endToEnd.OccurrenceCount);

        // CVE family: CVE-2026-12345 and bare CVE share A:CVE
        var cve = evidence.Candidates.Single(c => c.Identity == "A:CVE");
        Assert.AreEqual("CVE", cve.CanonicalTerm);
        Assert.AreEqual(TokenKind.Acronym, cve.Kind);
        Assert.AreEqual(2, cve.OccurrenceCount);
        Assert.AreEqual("CVE-2026-12345", cve.Occurrences[0].SurfaceForm);
        Assert.AreEqual(TechnicalTokenFamily.Cve, cve.Occurrences[0].TechnicalFamily);
        Assert.AreEqual(2026, cve.Occurrences[0].TechnicalInstanceYear);
        Assert.AreEqual("12345", cve.Occurrences[0].TechnicalInstanceIdentifier);
        Assert.AreEqual("CVE", cve.Occurrences[1].SurfaceForm);
        Assert.AreEqual(TechnicalTokenFamily.None, cve.Occurrences[1].TechnicalFamily);

        // SHA family: SHA-256 and SHA-512 share A:SHA
        var sha = evidence.Candidates.Single(c => c.Identity == "A:SHA");
        Assert.AreEqual("SHA", sha.CanonicalTerm);
        Assert.AreEqual(TokenKind.Acronym, sha.Kind);
        Assert.AreEqual(2, sha.OccurrenceCount);
        CollectionAssert.AreEqual(
            new[] { "SHA-256", "SHA-512" },
            sha.Occurrences.Select(o => o.SurfaceForm).ToArray());
        CollectionAssert.AreEqual(
            new[] { "256", "512" },
            sha.Occurrences.Select(o => o.TechnicalVariant).ToArray());

        var urlExclusions = evidence.TokenDecisions
            .Where(td => !td.IsIncluded && td.ReasonCode == AnalysisReasonCodes.ExcludedUrl)
            .ToArray();
        Assert.HasCount(1, urlExclusions);
        Assert.AreEqual("https://example.com/docs", urlExclusions[0].RawValue);

        var emailExclusions = evidence.TokenDecisions
            .Where(td => !td.IsIncluded && td.ReasonCode == AnalysisReasonCodes.ExcludedEmailAddress)
            .ToArray();
        Assert.HasCount(1, emailExclusions);
        Assert.AreEqual("security@example.org", emailExclusions[0].RawValue);

        var numberExclusions = evidence.TokenDecisions
            .Where(td => !td.IsIncluded && td.ReasonCode == AnalysisReasonCodes.ExcludedStandaloneNumber)
            .ToArray();
        Assert.HasCount(1, numberExclusions);
        Assert.AreEqual("42", numberExclusions[0].RawValue);
    }

    [TestMethod]
    public void EN005_DuplicateContextsAndEnglishIdentityRules_AssertStructuralExpectations()
    {
        var corpusCase = EnglishGoldCorpusFixtures.EN005_DuplicateContextsAndEnglishIdentityRules;
        var result = EnglishGoldCorpusFixtures.ExecuteCase(corpusCase, _analyzer);
        var evidence = result.AnalyzerEvidence;

        Assert.AreEqual(CorpusExpectationAuthority.VerifiedCurrentBehavior, result.Authority);
        Assert.AreEqual(corpusCase.Input, evidence.Input.Text);
        Assert.AreEqual("en", evidence.Input.SourceLanguage);
        Assert.IsTrue(evidence.InvariantsPassed);
        Assert.HasCount(3, evidence.Sentences);

        // Case-sensitive acronym vs ordinary word separation: IT != it and US != us
        var itAcronym = evidence.Candidates.Single(c => c.Identity == "A:IT");
        Assert.AreEqual("IT", itAcronym.CanonicalTerm);
        Assert.AreEqual(TokenKind.Acronym, itAcronym.Kind);
        Assert.AreEqual(2, itAcronym.OccurrenceCount);

        var itWord = evidence.Candidates.Single(c => c.Identity == "W:it");
        Assert.AreEqual("it", itWord.CanonicalTerm);
        Assert.AreEqual(TokenKind.Word, itWord.Kind);
        Assert.AreEqual(1, itWord.OccurrenceCount);

        var usAcronym = evidence.Candidates.Single(c => c.Identity == "A:US");
        Assert.AreEqual("US", usAcronym.CanonicalTerm);
        Assert.AreEqual(TokenKind.Acronym, usAcronym.Kind);
        Assert.AreEqual(1, usAcronym.OccurrenceCount);

        var usWord = evidence.Candidates.Single(c => c.Identity == "W:us");
        Assert.AreEqual("us", usWord.CanonicalTerm);
        Assert.AreEqual(TokenKind.Word, usWord.Kind);
        Assert.AreEqual(2, usWord.OccurrenceCount);

        // Explicit English first-person pronoun rule: "me", "my", "I" group under "W:i" with canonical "I"
        var pronounI = evidence.Candidates.Single(c => c.Identity == "W:i");
        Assert.AreEqual("I", pronounI.CanonicalTerm);
        Assert.AreEqual(TokenKind.Word, pronounI.Kind);
        Assert.AreEqual(3, pronounI.OccurrenceCount);
        CollectionAssert.AreEqual(
            new[] { "me", "my", "I" },
            pronounI.Occurrences.Select(o => o.SurfaceForm).ToArray());

        var pronounGroup = evidence.CandidateGroups.Single(g => g.Identity == "W:i");
        Assert.AreEqual(AnalysisReasonCodes.ExplicitLanguageRuleGrouping, pronounGroup.ReasonCode);

        // No broad stemming: "network" and "networking" remain distinct candidates
        var network = evidence.Candidates.Single(c => c.Identity == "W:network");
        var networking = evidence.Candidates.Single(c => c.Identity == "W:networking");
        Assert.AreEqual(2, network.OccurrenceCount);
        Assert.AreEqual(1, networking.OccurrenceCount);
        Assert.AreNotEqual(network.Identity, networking.Identity);

        // Duplicate-context suppression preserves occurrence counts while rejecting duplicate sentence contexts
        var networkContexts = evidence.ContextDecisions
            .Where(cd => cd.CandidateIdentity == "W:network")
            .ToArray();
        Assert.HasCount(2, networkContexts);
        var selectedContext = networkContexts.Single(cd => cd.IsSelected);
        var rejectedContext = networkContexts.Single(cd => !cd.IsSelected);
        Assert.AreEqual(AnalysisReasonCodes.SelectedFirstUniqueContext, selectedContext.ReasonCode);
        Assert.AreEqual(AnalysisReasonCodes.RejectedDuplicateContext, rejectedContext.ReasonCode);
        Assert.AreEqual(selectedContext.Fingerprint, rejectedContext.Fingerprint);

        // Candidate appearing in duplicate sentences (0, 1) plus a third distinct sentence (2) selects 2 contexts and rejects 1
        var helpsContexts = evidence.ContextDecisions
            .Where(cd => cd.CandidateIdentity == "W:helps")
            .ToArray();
        Assert.HasCount(3, helpsContexts);
        Assert.AreEqual(2, helpsContexts.Count(cd => cd.IsSelected));
        Assert.AreEqual(1, helpsContexts.Count(cd => !cd.IsSelected && cd.ReasonCode == AnalysisReasonCodes.RejectedDuplicateContext));
    }

    [TestMethod]
    public void AllEnglishCorpusCases_VerifyInvariantsExactSubstringsAndDeterminism()
    {
        Assert.HasCount(5, EnglishGoldCorpusFixtures.AllCases);

        foreach (var corpusCase in EnglishGoldCorpusFixtures.AllCases)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(corpusCase.Id));
            Assert.IsFalse(string.IsNullOrWhiteSpace(corpusCase.Description));
            Assert.IsFalse(string.IsNullOrWhiteSpace(corpusCase.Input));
            Assert.AreEqual("en", corpusCase.SourceLanguage);
            Assert.IsTrue(
                corpusCase.Authority is CorpusExpectationAuthority.BindingContract
                    or CorpusExpectationAuthority.VerifiedCurrentBehavior,
                $"Unexpected authority {corpusCase.Authority} for case {corpusCase.Id}.");

            var firstRun = EnglishGoldCorpusFixtures.ExecuteCase(corpusCase, _analyzer);
            var secondRun = EnglishGoldCorpusFixtures.ExecuteCase(corpusCase, _analyzer);

            Assert.IsTrue(
                firstRun.AnalyzerEvidence.InvariantsPassed,
                $"Invariant failure in case {corpusCase.Id}: {string.Join(", ", firstRun.AnalyzerEvidence.InvariantFailures.Select(f => f.Explanation))}");
            Assert.AreEqual(0, firstRun.AnalyzerEvidence.Candidates.Count(c => c.Provenance == CandidateProvenanceKind.DerivedFromCompound));

            foreach (var sentence in firstRun.AnalyzerEvidence.Sentences)
            {
                Assert.IsTrue(sentence.ExactSubstringMatch, $"Sentence substring mismatch in {corpusCase.Id}.");
                Assert.AreEqual(sentence.Text, corpusCase.Input.Substring(sentence.StartPosition, sentence.Length));
            }

            foreach (var occurrence in firstRun.AnalyzerEvidence.Candidates.SelectMany(c => c.Occurrences))
            {
                Assert.IsTrue(occurrence.ExactSubstringMatch, $"Occurrence substring mismatch in {corpusCase.Id}.");
                Assert.AreEqual(occurrence.SurfaceForm, corpusCase.Input.Substring(occurrence.StartPosition, occurrence.Length));
            }

            var firstText = AnalysisEvidenceTextFormatter.Format(firstRun.AnalyzerEvidence);
            var secondText = AnalysisEvidenceTextFormatter.Format(secondRun.AnalyzerEvidence);
            Assert.AreEqual(firstText, secondText, $"Non-deterministic text report for {corpusCase.Id}.");

            var firstJson = AnalysisEvidenceJsonFormatter.Format(firstRun.AnalyzerEvidence);
            var secondJson = AnalysisEvidenceJsonFormatter.Format(secondRun.AnalyzerEvidence);
            Assert.AreEqual(firstJson, secondJson, $"Non-deterministic JSON report for {corpusCase.Id}.");
        }
    }
}
