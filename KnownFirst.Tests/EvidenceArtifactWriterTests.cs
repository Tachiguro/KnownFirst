using KnownFirst.Core.Text;
using KnownFirst.Models;
using KnownFirst.Tests.AnalysisEvidence;

namespace KnownFirst.Tests;

[TestClass]
public sealed class EvidenceArtifactWriterTests
{
    private static GermanGoldCorpusEvidenceArtifact CreateSampleArtifact()
    {
        var doc = AnalysisEvidenceCollector.Collect("Das Buch ist gut.", sourceLanguage: "de");
        return new GermanGoldCorpusEvidenceArtifact(
            CaseId: "DE-TEST-001",
            Description: "Test artifact description",
            Authority: CorpusExpectationAuthority.BindingContract,
            AnalyzerEvidence: doc,
            ReviewAdmission: new ReviewAdmissionArtifactEvidence(
                AdmissionExecuted: true,
                Outcome: ImportAnalysisOutcome.Accepted,
                CandidateCount: 4,
                PreImportVocabulary: Array.Empty<PreImportVocabularyEntryArtifact>(),
                CandidateAdmissions: new[]
                {
                    new CandidateAdmissionEntryArtifact(
                        Identity: "W:buch",
                        CanonicalTerm: "Buch",
                        PreImportStatus: null,
                        Disposition: ReviewAdmissionDisposition.AdmittedNewWord,
                        PersistedReviewOrder: 0,
                        WasWordCreatedForSession: true,
                        TotalOccurrenceCountBefore: 0,
                        TotalOccurrenceCountAfter: 1,
                        DocumentCountBefore: 0,
                        DocumentCountAfter: 1,
                        OccurrenceContributionCount: 1)
                },
                PersistedReviewCandidates: new[]
                {
                    new PersistedReviewCandidateEntryArtifact(
                        Order: 0,
                        Identity: "W:buch",
                        CanonicalTerm: "Buch",
                        Status: WordStatus.Unreviewed,
                        PreviousWordStatus: WordStatus.Unreviewed,
                        WasWordCreatedForSession: true,
                        OccurrenceCount: 1)
                }));
    }

    private static GermanGoldCorpusEvidenceArtifact CreateSampleEnglishArtifact()
    {
        var doc = AnalysisEvidenceCollector.Collect("Information protects systems.", sourceLanguage: "en");
        return new GermanGoldCorpusEvidenceArtifact(
            CaseId: "EN-TEST-001",
            Description: "English test artifact description",
            Authority: CorpusExpectationAuthority.BindingContract,
            AnalyzerEvidence: doc,
            ReviewAdmission: new ReviewAdmissionArtifactEvidence(
                AdmissionExecuted: true,
                Outcome: ImportAnalysisOutcome.Accepted,
                CandidateCount: 3,
                PreImportVocabulary: Array.Empty<PreImportVocabularyEntryArtifact>(),
                CandidateAdmissions: new[]
                {
                    new CandidateAdmissionEntryArtifact(
                        Identity: "W:information",
                        CanonicalTerm: "Information",
                        PreImportStatus: null,
                        Disposition: ReviewAdmissionDisposition.AdmittedNewWord,
                        PersistedReviewOrder: 0,
                        WasWordCreatedForSession: true,
                        TotalOccurrenceCountBefore: 0,
                        TotalOccurrenceCountAfter: 1,
                        DocumentCountBefore: 0,
                        DocumentCountAfter: 1,
                        OccurrenceContributionCount: 1)
                },
                PersistedReviewCandidates: new[]
                {
                    new PersistedReviewCandidateEntryArtifact(
                        Order: 0,
                        Identity: "W:information",
                        CanonicalTerm: "Information",
                        Status: WordStatus.Unreviewed,
                        PreviousWordStatus: WordStatus.Unreviewed,
                        WasWordCreatedForSession: true,
                        OccurrenceCount: 1)
                }));
    }

    [TestMethod]
    public void ResolveRepositoryRoot_FindsRepoRootWithSlnxOrCsproj()
    {
        var root = EvidenceArtifactWriter.ResolveRepositoryRoot();
        Assert.IsTrue(Directory.Exists(root));
        Assert.IsTrue(File.Exists(Path.Combine(root, "KnownFirst.slnx")) || File.Exists(Path.Combine(root, "KnownFirst.csproj")));
    }

    [TestMethod]
    public void GetArtifactDirectory_ResolvesSeparateGermanAndEnglishDirectories()
    {
        var defaultDir = EvidenceArtifactWriter.GetArtifactDirectory();
        var germanDir = EvidenceArtifactWriter.GetArtifactDirectory("de");
        var englishDir = EvidenceArtifactWriter.GetEnglishArtifactDirectory();
        var englishByLangDir = EvidenceArtifactWriter.GetArtifactDirectory("en");

        Assert.AreEqual(defaultDir, germanDir);
        Assert.AreEqual(englishDir, englishByLangDir);
        Assert.AreNotEqual(germanDir, englishDir);

        StringAssert.EndsWith(germanDir, Path.Combine("artifacts", "text-analysis-evidence", "german-gold-corpus"));
        StringAssert.EndsWith(englishDir, Path.Combine("artifacts", "text-analysis-evidence", "english-gold-corpus"));
    }

    [TestMethod]
    public void FormatText_ProducesDeterministicHumanReadableReport()
    {
        var artifact = CreateSampleArtifact();
        var text = EvidenceArtifactWriter.FormatText(artifact);

        Assert.IsNotNull(text);
        StringAssert.StartsWith(text, "=== GERMAN GOLD CORPUS EVIDENCE ARTIFACT ===\n");
        Assert.IsTrue(text.Contains("DE-TEST-001", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("Test artifact description", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("BINDING_CONTRACT", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("W:buch", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("\r\n", StringComparison.Ordinal));
    }

    [TestMethod]
    public void FormatText_EnglishArtifact_UsesEnglishHeaderAndPreservesGermanHeader()
    {
        var englishArtifact = CreateSampleEnglishArtifact();
        var englishText = EvidenceArtifactWriter.FormatText(englishArtifact);

        Assert.IsNotNull(englishText);
        StringAssert.StartsWith(englishText, "=== ENGLISH GOLD CORPUS EVIDENCE ARTIFACT ===\n");
        Assert.IsFalse(englishText.Contains("=== GERMAN GOLD CORPUS EVIDENCE ARTIFACT ===", StringComparison.Ordinal));
        Assert.IsTrue(englishText.Contains("EN-TEST-001", StringComparison.Ordinal));
        Assert.IsTrue(englishText.Contains("Source Language: en\n", StringComparison.Ordinal));
        Assert.IsTrue(englishText.Contains("W:information", StringComparison.Ordinal));
        Assert.IsFalse(englishText.Contains("\r\n", StringComparison.Ordinal));

        var germanArtifact = CreateSampleArtifact();
        var germanText = EvidenceArtifactWriter.FormatText(germanArtifact);
        StringAssert.StartsWith(germanText, "=== GERMAN GOLD CORPUS EVIDENCE ARTIFACT ===\n");
        Assert.IsFalse(germanText.Contains("=== ENGLISH GOLD CORPUS EVIDENCE ARTIFACT ===", StringComparison.Ordinal));
    }

    [TestMethod]
    public void FormatJson_AndWriteArtifacts_EnglishArtifact_RoundTripsAndIsDeterministic()
    {
        var tempDir1 = Path.Combine(Path.GetTempPath(), $"kf-en-artifact-test-1-{Guid.NewGuid():N}");
        var tempDir2 = Path.Combine(Path.GetTempPath(), $"kf-en-artifact-test-2-{Guid.NewGuid():N}");
        try
        {
            var artifact = CreateSampleEnglishArtifact();
            var (txt1, json1) = EvidenceArtifactWriter.WriteArtifacts(artifact, tempDir1);
            var (txt2, json2) = EvidenceArtifactWriter.WriteArtifacts(artifact, tempDir2);

            Assert.IsTrue(File.Exists(txt1));
            Assert.IsTrue(File.Exists(json1));
            Assert.AreEqual("EN-TEST-001.txt", Path.GetFileName(txt1));
            Assert.AreEqual("EN-TEST-001.json", Path.GetFileName(json1));

            CollectionAssert.AreEqual(File.ReadAllBytes(txt1), File.ReadAllBytes(txt2));
            CollectionAssert.AreEqual(File.ReadAllBytes(json1), File.ReadAllBytes(json2));

            var jsonContent = File.ReadAllText(json1);
            var deserialized = AnalysisEvidenceJsonFormatter.DeserializeCorpusArtifact(jsonContent);
            Assert.AreEqual("EN-TEST-001", deserialized.CaseId);
            Assert.AreEqual("Information protects systems.", deserialized.AnalyzerEvidence.Input.Text);
            Assert.AreEqual("en", deserialized.AnalyzerEvidence.Input.SourceLanguage);
            var candidateAdmissions = deserialized.ReviewAdmission.CandidateAdmissions!;
            Assert.HasCount(1, candidateAdmissions);
            Assert.AreEqual("W:information", candidateAdmissions[0].Identity);
        }
        finally
        {
            if (Directory.Exists(tempDir1)) Directory.Delete(tempDir1, recursive: true);
            if (Directory.Exists(tempDir2)) Directory.Delete(tempDir2, recursive: true);
        }
    }

    [TestMethod]
    public void FormatJson_AndDeserialize_RoundTripsAccuratelyWithoutReflection()
    {
        var artifact = CreateSampleArtifact();
        var json = EvidenceArtifactWriter.FormatJson(artifact);

        Assert.IsNotNull(json);
        var deserialized = AnalysisEvidenceJsonFormatter.DeserializeCorpusArtifact(json);
        Assert.AreEqual(artifact.CaseId, deserialized.CaseId);
        Assert.AreEqual(artifact.Description, deserialized.Description);
        Assert.AreEqual(artifact.Authority, deserialized.Authority);
        Assert.AreEqual(artifact.AnalyzerEvidence.Input.Text, deserialized.AnalyzerEvidence.Input.Text);
        var candidateAdmissions = deserialized.ReviewAdmission.CandidateAdmissions!;
        Assert.HasCount(1, candidateAdmissions);
        Assert.AreEqual("W:buch", candidateAdmissions[0].Identity);
    }

    [TestMethod]
    public void WriteArtifacts_EmitsTxtAndJsonFilesUnderTargetDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"kf-artifact-test-{Guid.NewGuid():N}");
        try
        {
            var artifact = CreateSampleArtifact();
            var (txtPath, jsonPath) = EvidenceArtifactWriter.WriteArtifacts(artifact, tempDir);

            Assert.IsTrue(File.Exists(txtPath));
            Assert.IsTrue(File.Exists(jsonPath));
            Assert.AreEqual("DE-TEST-001.txt", Path.GetFileName(txtPath));
            Assert.AreEqual("DE-TEST-001.json", Path.GetFileName(jsonPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void WriteArtifacts_ProducesByteForByteIdenticalOutputOnRepeatedWrites()
    {
        var tempDir1 = Path.Combine(Path.GetTempPath(), $"kf-artifact-test-1-{Guid.NewGuid():N}");
        var tempDir2 = Path.Combine(Path.GetTempPath(), $"kf-artifact-test-2-{Guid.NewGuid():N}");
        try
        {
            var artifact = CreateSampleArtifact();
            var (txt1, json1) = EvidenceArtifactWriter.WriteArtifacts(artifact, tempDir1);
            var (txt2, json2) = EvidenceArtifactWriter.WriteArtifacts(artifact, tempDir2);

            var txtBytes1 = File.ReadAllBytes(txt1);
            var txtBytes2 = File.ReadAllBytes(txt2);
            var jsonBytes1 = File.ReadAllBytes(json1);
            var jsonBytes2 = File.ReadAllBytes(json2);

            CollectionAssert.AreEqual(txtBytes1, txtBytes2);
            CollectionAssert.AreEqual(jsonBytes1, jsonBytes2);
        }
        finally
        {
            if (Directory.Exists(tempDir1)) Directory.Delete(tempDir1, recursive: true);
            if (Directory.Exists(tempDir2)) Directory.Delete(tempDir2, recursive: true);
        }
    }
}
