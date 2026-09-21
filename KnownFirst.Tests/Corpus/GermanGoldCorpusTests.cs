using KnownFirst.Core.Text;
using KnownFirst.Models;
using KnownFirst.Tests.AnalysisEvidence;

namespace KnownFirst.Tests.Corpus;

[TestClass]
public sealed class GermanGoldCorpusTests
{
    private TextAnalyzer _analyzer = null!;

    [TestInitialize]
    public void Initialize()
    {
        _analyzer = new TextAnalyzer();
    }

    [TestMethod]
    public async Task DE001_BasicWords_AssertStructuralExpectations()
    {
        var artifact = await GermanGoldCorpusFixtures.ExecuteCaseAsync(
            GermanGoldCorpusFixtures.DE001_BasicWords, _analyzer);

        Assert.IsTrue(artifact.AnalyzerEvidence.InvariantsPassed);
        Assert.AreEqual(2, artifact.AnalyzerEvidence.Sentences.Count);

        var candidates = artifact.AnalyzerEvidence.Candidates;
        var buch = candidates.Single(c => c.CanonicalTerm == "Buch");
        Assert.AreEqual("W:buch", buch.Identity);
        Assert.AreEqual(CandidateProvenanceKind.Direct, buch.Provenance);
        Assert.AreEqual(2, buch.OccurrenceCount);
        Assert.AreEqual(2, buch.Occurrences.Count);

        foreach (var occ in buch.Occurrences)
        {
            Assert.IsTrue(occ.ExactSubstringMatch);
            Assert.AreEqual("Buch", occ.SurfaceForm);
        }

        // Frequency-1 candidates
        var freq1Terms = new[] { "Das", "ist", "gut", "Ein", "hilft", "oft" };
        foreach (var term in freq1Terms)
        {
            var candidate = candidates.Single(c => c.CanonicalTerm == term);
            Assert.AreEqual(1, candidate.OccurrenceCount);
            Assert.AreEqual(1, candidate.Occurrences.Count);
            Assert.IsTrue(candidate.Occurrences[0].ExactSubstringMatch);
        }

        // Context decisions: 2 selected contexts for "Buch" across 2 sentences
        var buchContexts = artifact.AnalyzerEvidence.ContextDecisions
            .Where(cd => cd.CandidateIdentity == "W:buch")
            .ToArray();
        Assert.AreEqual(2, buchContexts.Length);
        Assert.IsTrue(buchContexts.All(cd => cd.IsSelected));
        Assert.AreNotEqual(buchContexts[0].Fingerprint, buchContexts[1].Fingerprint);
    }

    [TestMethod]
    public async Task DE002_UmlautsAndEszett_AssertStructuralExpectations()
    {
        var artifact = await GermanGoldCorpusFixtures.ExecuteCaseAsync(
            GermanGoldCorpusFixtures.DE002_UmlautsAndEszett, _analyzer);

        Assert.IsTrue(artifact.AnalyzerEvidence.InvariantsPassed);
        Assert.AreEqual(1, artifact.AnalyzerEvidence.Sentences.Count);
        Assert.AreEqual(AnalysisReasonCodes.SentenceBoundaryFinalRemainder, artifact.AnalyzerEvidence.Sentences[0].BoundaryReasonCode);

        var identities = artifact.AnalyzerEvidence.Candidates.Select(c => c.Identity).ToArray();
        CollectionAssert.Contains(identities, "W:große");
        CollectionAssert.Contains(identities, "W:füße");
        CollectionAssert.Contains(identities, "W:schöne");
        CollectionAssert.Contains(identities, "W:vögel");
        CollectionAssert.Contains(identities, "W:weiße");
        CollectionAssert.Contains(identities, "W:mäuse");

        foreach (var candidate in artifact.AnalyzerEvidence.Candidates)
        {
            foreach (var occ in candidate.Occurrences)
            {
                Assert.IsTrue(occ.ExactSubstringMatch);
            }
        }
    }

    [TestMethod]
    public async Task DE003_UnicodeComposedDecomposed_AssertStructuralExpectations()
    {
        var artifact = await GermanGoldCorpusFixtures.ExecuteCaseAsync(
            GermanGoldCorpusFixtures.DE003_UnicodeComposedDecomposed, _analyzer);

        Assert.IsTrue(artifact.AnalyzerEvidence.InvariantsPassed);

        var schoene = artifact.AnalyzerEvidence.Candidates.Single(c => c.Identity == "W:schöne");
        Assert.AreEqual(2, schoene.OccurrenceCount);
        Assert.AreEqual(2, schoene.Occurrences.Count);

        var occ0 = schoene.Occurrences[0];
        var occ1 = schoene.Occurrences[1];

        // Occ 0 is composed (length 6)
        Assert.AreEqual(6, occ0.Length);
        Assert.IsTrue(occ0.ExactSubstringMatch);

        // Occ 1 is decomposed (length 7)
        Assert.AreEqual(7, occ1.Length);
        Assert.IsTrue(occ1.ExactSubstringMatch);

        // Deduplicated encountered forms has 1 representative
        var schoeneGroup = artifact.AnalyzerEvidence.CandidateGroups.Single(g => g.Identity == "W:schöne");
        Assert.AreEqual(1, schoeneGroup.FormsAfterDeduplication.Count);
    }

    [TestMethod]
    public async Task DE004_PunctuationQuotesApostrophes_AssertStructuralExpectations()
    {
        var artifact = await GermanGoldCorpusFixtures.ExecuteCaseAsync(
            GermanGoldCorpusFixtures.DE004_PunctuationQuotesApostrophes, _analyzer);

        Assert.IsTrue(artifact.AnalyzerEvidence.InvariantsPassed);
        Assert.AreEqual(2, artifact.AnalyzerEvidence.Sentences.Count);

        // Typographic apostrophe in Geht’s is treated as connector inside word token
        var gehts = artifact.AnalyzerEvidence.Candidates.Single(c => c.CanonicalTerm == "Geht’s");
        Assert.AreEqual(TokenKind.Word, gehts.Kind);
        Assert.AreEqual("W:geht’s", gehts.Identity);
        Assert.IsTrue(gehts.Occurrences[0].ExactSubstringMatch);

        // Quotations excluded
        var punctuationDecisions = artifact.AnalyzerEvidence.TokenDecisions
            .Where(td => !td.IsIncluded && td.ReasonCode == AnalysisReasonCodes.ExcludedPunctuationOnly)
            .ToArray();
        Assert.IsTrue(punctuationDecisions.Length > 0);
    }

    [TestMethod]
    public async Task DE005_HyphenatedWords_AssertStructuralExpectations()
    {
        var artifact = await GermanGoldCorpusFixtures.ExecuteCaseAsync(
            GermanGoldCorpusFixtures.DE005_HyphenatedWords, _analyzer);

        Assert.IsTrue(artifact.AnalyzerEvidence.InvariantsPassed);

        var woerterbuch = artifact.AnalyzerEvidence.Candidates.Single(c => c.CanonicalTerm == "Deutsch-Englisch-Wörterbuch");
        Assert.AreEqual(TokenKind.TechnicalTerm, woerterbuch.Kind);
        Assert.AreEqual("T:Deutsch-Englisch-Wörterbuch", woerterbuch.Identity);
        Assert.IsTrue(woerterbuch.Occurrences[0].ExactSubstringMatch);

        var vergleich = artifact.AnalyzerEvidence.Candidates.Single(c => c.CanonicalTerm == "Nachteile-Vergleich");
        Assert.AreEqual(TokenKind.TechnicalTerm, vergleich.Kind);
        Assert.AreEqual("T:Nachteile-Vergleich", vergleich.Identity);
    }

    [TestMethod]
    public async Task DE006_LineEndingsCrlfLfCr_AssertStructuralExpectations()
    {
        var artifact = await GermanGoldCorpusFixtures.ExecuteCaseAsync(
            GermanGoldCorpusFixtures.DE006_LineEndingsCrlfLfCr, _analyzer);

        Assert.IsTrue(artifact.AnalyzerEvidence.InvariantsPassed);
        Assert.AreEqual(4, artifact.AnalyzerEvidence.Sentences.Count);

        Assert.AreEqual("Erste Zeile.", artifact.AnalyzerEvidence.Sentences[0].Text);
        Assert.AreEqual("Zweite Zeile.", artifact.AnalyzerEvidence.Sentences[1].Text);
        Assert.AreEqual("Dritte Zeile.", artifact.AnalyzerEvidence.Sentences[2].Text);
        Assert.AreEqual("Vierte Zeile.", artifact.AnalyzerEvidence.Sentences[3].Text);

        foreach (var s in artifact.AnalyzerEvidence.Sentences)
        {
            Assert.IsTrue(s.ExactSubstringMatch);
        }
    }

    [TestMethod]
    public async Task DE007_AbbreviationsAndDecimals_AssertStructuralExpectations()
    {
        var artifact = await GermanGoldCorpusFixtures.ExecuteCaseAsync(
            GermanGoldCorpusFixtures.DE007_AbbreviationsAndDecimals, _analyzer);

        Assert.IsTrue(artifact.AnalyzerEvidence.InvariantsPassed);
        Assert.AreEqual(1, artifact.AnalyzerEvidence.Sentences.Count);

        var abbreviations = artifact.AnalyzerEvidence.Candidates
            .Where(c => c.Kind == TokenKind.Abbreviation)
            .Select(c => c.CanonicalTerm)
            .ToArray();
        CollectionAssert.Contains(abbreviations, "Dr.");
        CollectionAssert.Contains(abbreviations, "ca.");
        CollectionAssert.Contains(abbreviations, "bzw.");

        var standaloneNumbers = artifact.AnalyzerEvidence.TokenDecisions
            .Where(td => !td.IsIncluded && td.ReasonCode == AnalysisReasonCodes.ExcludedStandaloneNumber)
            .Select(td => td.RawValue)
            .ToArray();
        CollectionAssert.Contains(standaloneNumbers, "12.50");
        CollectionAssert.Contains(standaloneNumbers, "3");
    }

    [TestMethod]
    public async Task DE008_TechnicalTokensAndExclusions_AssertStructuralExpectations()
    {
        var artifact = await GermanGoldCorpusFixtures.ExecuteCaseAsync(
            GermanGoldCorpusFixtures.DE008_TechnicalTokensAndExclusions, _analyzer);

        Assert.IsTrue(artifact.AnalyzerEvidence.InvariantsPassed);

        var oauth2 = artifact.AnalyzerEvidence.Candidates.Single(c => c.CanonicalTerm == "OAuth2");
        Assert.AreEqual(TokenKind.TechnicalTerm, oauth2.Kind);
        Assert.AreEqual("T:OAuth2", oauth2.Identity);

        var ipv6 = artifact.AnalyzerEvidence.Candidates.Single(c => c.CanonicalTerm == "IPv6");
        Assert.AreEqual(TokenKind.TechnicalTerm, ipv6.Kind);
        Assert.AreEqual("T:IPv6", ipv6.Identity);

        var cve = artifact.AnalyzerEvidence.Candidates.Single(c => c.CanonicalTerm == "CVE");
        Assert.AreEqual(TokenKind.Acronym, cve.Kind);
        Assert.AreEqual("A:CVE", cve.Identity);
        Assert.AreEqual(TechnicalTokenFamily.Cve, cve.Occurrences[0].TechnicalFamily);
        Assert.AreEqual(2023, cve.Occurrences[0].TechnicalInstanceYear);
        Assert.AreEqual("1234", cve.Occurrences[0].TechnicalInstanceIdentifier);

        var sha = artifact.AnalyzerEvidence.Candidates.Single(c => c.CanonicalTerm == "SHA");
        Assert.AreEqual(TokenKind.Acronym, sha.Kind);
        Assert.AreEqual("A:SHA", sha.Identity);
        Assert.AreEqual(TechnicalTokenFamily.Sha, sha.Occurrences[0].TechnicalFamily);
        Assert.AreEqual("256", sha.Occurrences[0].TechnicalVariant);

        var urlExclusions = artifact.AnalyzerEvidence.TokenDecisions
            .Where(td => !td.IsIncluded && td.ReasonCode == AnalysisReasonCodes.ExcludedUrl)
            .ToArray();
        Assert.AreEqual(1, urlExclusions.Length);
        Assert.IsTrue(urlExclusions[0].RawValue.StartsWith("https://example.org", StringComparison.Ordinal));

        var emailExclusions = artifact.AnalyzerEvidence.TokenDecisions
            .Where(td => !td.IsIncluded && td.ReasonCode == AnalysisReasonCodes.ExcludedEmailAddress)
            .ToArray();
        Assert.AreEqual(1, emailExclusions.Length);
        Assert.AreEqual("info@example.com", emailExclusions[0].RawValue);
    }

    [TestMethod]
    public async Task DE009_DuplicateContextSuppression_AssertStructuralExpectations()
    {
        var artifact = await GermanGoldCorpusFixtures.ExecuteCaseAsync(
            GermanGoldCorpusFixtures.DE009_DuplicateContextSuppression, _analyzer);

        Assert.IsTrue(artifact.AnalyzerEvidence.InvariantsPassed);

        var buchContexts = artifact.AnalyzerEvidence.ContextDecisions
            .Where(cd => cd.CandidateIdentity == "W:buch")
            .ToArray();
        Assert.AreEqual(2, buchContexts.Length);

        var selected = buchContexts.Single(cd => cd.IsSelected);
        var suppressed = buchContexts.Single(cd => !cd.IsSelected);

        Assert.AreEqual(AnalysisReasonCodes.SelectedFirstUniqueContext, selected.ReasonCode);
        Assert.AreEqual(AnalysisReasonCodes.RejectedDuplicateContext, suppressed.ReasonCode);
        Assert.AreEqual(selected.Fingerprint, suppressed.Fingerprint);
    }

    [TestMethod]
    public async Task DE010_GermanCoordinatedCompounds_AssertStructuralExpectations()
    {
        var artifact = await GermanGoldCorpusFixtures.ExecuteCaseAsync(
            GermanGoldCorpusFixtures.DE010_GermanCoordinatedCompounds, _analyzer);

        Assert.IsTrue(artifact.AnalyzerEvidence.InvariantsPassed);

        var canonicalTerms = artifact.AnalyzerEvidence.Candidates.Select(c => c.CanonicalTerm).ToArray();
        CollectionAssert.Contains(canonicalTerms, "Arbeitsprozess");
        CollectionAssert.Contains(canonicalTerms, "Qualitätsprozess");
        CollectionAssert.Contains(canonicalTerms, "Sicherheitsprozess");
        CollectionAssert.Contains(canonicalTerms, "Datenschutzprozess");

        var arbeitsOcc = artifact.AnalyzerEvidence.Candidates.Single(c => c.CanonicalTerm == "Arbeitsprozess").Occurrences.Single();
        Assert.AreEqual("Arbeits-", arbeitsOcc.SurfaceForm);
        Assert.IsTrue(arbeitsOcc.ExactSubstringMatch);
    }

    [TestMethod]
    public async Task DE011_ConservativeCompoundDecomposition_AssertStructuralExpectations()
    {
        var artifact = await GermanGoldCorpusFixtures.ExecuteCaseAsync(
            GermanGoldCorpusFixtures.DE011_ConservativeCompoundDecomposition, _analyzer);

        Assert.IsTrue(artifact.AnalyzerEvidence.InvariantsPassed);

        var whole = artifact.AnalyzerEvidence.Candidates.Single(c => c.CanonicalTerm == "Schreibmaschine");
        Assert.AreEqual(CandidateProvenanceKind.Direct, whole.Provenance);
        Assert.AreEqual(1, whole.OccurrenceCount);

        var schreiben = artifact.AnalyzerEvidence.Candidates.Single(c => c.Identity == "W:schreiben");
        Assert.AreEqual(CandidateProvenanceKind.DerivedFromCompound, schreiben.Provenance);
        Assert.AreEqual(0, schreiben.OccurrenceCount);
        Assert.AreEqual(1, schreiben.DerivedEvidence.Count);
        Assert.AreEqual("W:schreibmaschine", schreiben.DerivedEvidence[0].SourceIdentity);
        Assert.AreEqual("Schreib", schreiben.DerivedEvidence[0].ComponentForm);

        var maschine = artifact.AnalyzerEvidence.Candidates.Single(c => c.Identity == "W:maschine");
        Assert.AreEqual(CandidateProvenanceKind.DerivedFromCompound, maschine.Provenance);
        Assert.AreEqual(0, maschine.OccurrenceCount);
        Assert.AreEqual(1, maschine.DerivedEvidence.Count);
        Assert.AreEqual("W:schreibmaschine", maschine.DerivedEvidence[0].SourceIdentity);
        Assert.AreEqual("maschine", maschine.DerivedEvidence[0].ComponentForm);
    }

    [TestMethod]
    public async Task DE012_CompoundDirectDerivedCollision_AssertStructuralExpectations()
    {
        var artifact = await GermanGoldCorpusFixtures.ExecuteCaseAsync(
            GermanGoldCorpusFixtures.DE012_CompoundDirectDerivedCollision, _analyzer);

        Assert.IsTrue(artifact.AnalyzerEvidence.InvariantsPassed);

        var maschineCandidates = artifact.AnalyzerEvidence.Candidates.Where(c => c.Identity == "W:maschine").ToArray();
        Assert.AreEqual(1, maschineCandidates.Length);
        Assert.AreEqual(CandidateProvenanceKind.Direct, maschineCandidates[0].Provenance);
        Assert.AreEqual(1, maschineCandidates[0].OccurrenceCount);

        var schreiben = artifact.AnalyzerEvidence.Candidates.Single(c => c.Identity == "W:schreiben");
        Assert.AreEqual(CandidateProvenanceKind.DerivedFromCompound, schreiben.Provenance);
    }

    [TestMethod]
    public async Task DE013_AmbiguousFailClosedCompound_AssertStructuralExpectations()
    {
        var artifact = await GermanGoldCorpusFixtures.ExecuteCaseAsync(
            GermanGoldCorpusFixtures.DE013_AmbiguousFailClosedCompound, _analyzer);

        Assert.IsTrue(artifact.AnalyzerEvidence.InvariantsPassed);

        var derivedCandidates = artifact.AnalyzerEvidence.Candidates.Where(c => c.Provenance == CandidateProvenanceKind.DerivedFromCompound).ToArray();
        Assert.AreEqual(0, derivedCandidates.Length);

        var direct = artifact.AnalyzerEvidence.Candidates.Single(c => c.CanonicalTerm == "Xyzabc");
        Assert.AreEqual(CandidateProvenanceKind.Direct, direct.Provenance);
    }

    [TestMethod]
    public async Task DE014_ReviewAdmissionNewAndExistingUnreviewed_AssertStructuralExpectations()
    {
        var artifact = await GermanGoldCorpusFixtures.ExecuteCaseAsync(
            GermanGoldCorpusFixtures.DE014_ReviewAdmissionNewAndExistingUnreviewed, _analyzer);

        Assert.IsTrue(artifact.AnalyzerEvidence.InvariantsPassed);
        Assert.IsTrue(artifact.ReviewAdmission.AdmissionExecuted);
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, artifact.ReviewAdmission.Outcome);

        var admissions = artifact.ReviewAdmission.CandidateAdmissions!;
        Assert.AreEqual(2, admissions.Count);

        var alpha = admissions.Single(a => a.CanonicalTerm == "Alpha");
        Assert.IsNull(alpha.PreImportStatus);
        Assert.AreEqual(ReviewAdmissionDisposition.AdmittedNewWord, alpha.Disposition);
        Assert.AreEqual(0, alpha.PersistedReviewOrder);
        Assert.IsTrue(alpha.WasWordCreatedForSession);
        Assert.AreEqual(1, alpha.OccurrenceContributionCount);

        var beta = admissions.Single(a => a.CanonicalTerm == "Beta");
        Assert.AreEqual(WordStatus.Unreviewed, beta.PreImportStatus);
        Assert.AreEqual(ReviewAdmissionDisposition.AdmittedExistingUnreviewed, beta.Disposition);
        Assert.AreEqual(1, beta.PersistedReviewOrder);
        Assert.IsFalse(beta.WasWordCreatedForSession);
        Assert.AreEqual(1, beta.TotalOccurrenceCountBefore);
        Assert.AreEqual(2, beta.TotalOccurrenceCountAfter);
        Assert.AreEqual(1, beta.OccurrenceContributionCount);

        Assert.AreEqual(2, artifact.ReviewAdmission.PersistedReviewCandidates!.Count);
    }

    [TestMethod]
    public async Task DE015_ReviewAdmissionStatusExclusions_AssertStructuralExpectations()
    {
        var artifact = await GermanGoldCorpusFixtures.ExecuteCaseAsync(
            GermanGoldCorpusFixtures.DE015_ReviewAdmissionStatusExclusions, _analyzer);

        Assert.IsTrue(artifact.AnalyzerEvidence.InvariantsPassed);
        Assert.IsTrue(artifact.ReviewAdmission.AdmissionExecuted);
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, artifact.ReviewAdmission.Outcome);

        var admissions = artifact.ReviewAdmission.CandidateAdmissions!;
        Assert.AreEqual(4, admissions.Count);

        var alpha = admissions.Single(a => a.CanonicalTerm == "Alpha");
        Assert.AreEqual(ReviewAdmissionDisposition.AdmittedNewWord, alpha.Disposition);
        Assert.AreEqual(0, alpha.PersistedReviewOrder);

        var backlog = admissions.Single(a => a.CanonicalTerm == "Backlog");
        Assert.AreEqual(ReviewAdmissionDisposition.NotAdmittedExistingUnknownBacklog, backlog.Disposition);
        Assert.IsNull(backlog.PersistedReviewOrder);
        Assert.AreEqual(2, backlog.TotalOccurrenceCountBefore);
        Assert.AreEqual(3, backlog.TotalOccurrenceCountAfter);
        Assert.AreEqual(1, backlog.OccurrenceContributionCount);

        var known = admissions.Single(a => a.CanonicalTerm == "Known");
        Assert.AreEqual(ReviewAdmissionDisposition.NotAdmittedExistingKnown, known.Disposition);
        Assert.IsNull(known.PersistedReviewOrder);
        Assert.AreEqual(3, known.TotalOccurrenceCountBefore);
        Assert.AreEqual(3, known.TotalOccurrenceCountAfter);
        Assert.AreEqual(0, known.OccurrenceContributionCount);

        var ignored = admissions.Single(a => a.CanonicalTerm == "Ignored");
        Assert.AreEqual(ReviewAdmissionDisposition.NotAdmittedExistingIgnored, ignored.Disposition);
        Assert.IsNull(ignored.PersistedReviewOrder);
        Assert.AreEqual(4, ignored.TotalOccurrenceCountBefore);
        Assert.AreEqual(4, ignored.TotalOccurrenceCountAfter);
        Assert.AreEqual(0, ignored.OccurrenceContributionCount);

        Assert.AreEqual(1, artifact.ReviewAdmission.PersistedReviewCandidates!.Count);
    }

    [TestMethod]
    public async Task DE016_ReviewAdmissionNoNewVocabulary_AssertStructuralExpectations()
    {
        var artifact = await GermanGoldCorpusFixtures.ExecuteCaseAsync(
            GermanGoldCorpusFixtures.DE016_ReviewAdmissionNoNewVocabulary, _analyzer);

        Assert.IsTrue(artifact.AnalyzerEvidence.InvariantsPassed);
        Assert.IsTrue(artifact.ReviewAdmission.AdmissionExecuted);
        Assert.AreEqual(ImportAnalysisOutcome.NoNewVocabulary, artifact.ReviewAdmission.Outcome);

        var admissions = artifact.ReviewAdmission.CandidateAdmissions!;
        Assert.AreEqual(2, admissions.Count);

        var backlog = admissions.Single(a => a.CanonicalTerm == "Backlog");
        Assert.AreEqual(ReviewAdmissionDisposition.NotAdmittedExistingUnknownBacklog, backlog.Disposition);
        Assert.AreEqual(0, backlog.OccurrenceContributionCount);

        Assert.AreEqual(0, artifact.ReviewAdmission.PersistedReviewCandidates!.Count);
    }

    [TestMethod]
    public async Task DE017_ReviewAdmissionExactDuplicate_AssertStructuralExpectations()
    {
        var artifact = await GermanGoldCorpusFixtures.ExecuteCaseAsync(
            GermanGoldCorpusFixtures.DE017_ReviewAdmissionExactDuplicate, _analyzer);

        Assert.IsTrue(artifact.AnalyzerEvidence.InvariantsPassed);
        Assert.IsTrue(artifact.ReviewAdmission.AdmissionExecuted);
        Assert.AreEqual(ImportAnalysisOutcome.ExactDuplicate, artifact.ReviewAdmission.Outcome);
        Assert.AreEqual(0, artifact.ReviewAdmission.CandidateAdmissions!.Count);
        Assert.AreEqual(0, artifact.ReviewAdmission.PersistedReviewCandidates!.Count);
    }

    [TestMethod]
    public async Task AllCorpusCases_GenerateArtifacts_VerifyDeterminismAndRoundTrip()
    {
        var targetDir = EvidenceArtifactWriter.GetArtifactDirectory();

        var artifacts = new List<GermanGoldCorpusEvidenceArtifact>();
        foreach (var corpusCase in GermanGoldCorpusFixtures.AllCases)
        {
            var artifact = await GermanGoldCorpusFixtures.ExecuteCaseAsync(corpusCase, _analyzer);
            artifacts.Add(artifact);

            Assert.IsTrue(artifact.AnalyzerEvidence.InvariantsPassed,
                $"Invariant failure in case {corpusCase.Id}: {string.Join(", ", artifact.AnalyzerEvidence.InvariantFailures.Select(f => f.Explanation))}");
        }

        Assert.AreEqual(17, artifacts.Count);

        // Write first pass
        var writtenFiles = EvidenceArtifactWriter.WriteAll(artifacts, targetDir);
        Assert.AreEqual(17, writtenFiles.Count);

        foreach (var (txtPath, jsonPath) in writtenFiles)
        {
            Assert.IsTrue(File.Exists(txtPath), $"Missing TXT artifact: {txtPath}");
            Assert.IsTrue(File.Exists(jsonPath), $"Missing JSON artifact: {jsonPath}");

            var jsonContent = File.ReadAllText(jsonPath);
            var deserialized = AnalysisEvidenceJsonFormatter.DeserializeCorpusArtifact(jsonContent);
            Assert.IsNotNull(deserialized);
            Assert.AreEqual(Path.GetFileNameWithoutExtension(jsonPath), deserialized.CaseId);
        }

        // Write second pass to temp directory and verify byte-for-byte identity
        var tempDir = Path.Combine(Path.GetTempPath(), $"kf-determinism-{Guid.NewGuid():N}");
        try
        {
            var repeatFiles = EvidenceArtifactWriter.WriteAll(artifacts, tempDir);
            for (var i = 0; i < writtenFiles.Count; i++)
            {
                var txtOriginal = File.ReadAllBytes(writtenFiles[i].TxtPath);
                var txtRepeat = File.ReadAllBytes(repeatFiles[i].TxtPath);
                CollectionAssert.AreEqual(txtOriginal, txtRepeat, $"TXT artifact {writtenFiles[i].TxtPath} not byte-for-byte identical.");

                var jsonOriginal = File.ReadAllBytes(writtenFiles[i].JsonPath);
                var jsonRepeat = File.ReadAllBytes(repeatFiles[i].JsonPath);
                CollectionAssert.AreEqual(jsonOriginal, jsonRepeat, $"JSON artifact {writtenFiles[i].JsonPath} not byte-for-byte identical.");
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
