using System.Net;
using System.Net.Http.Headers;
using System.Text;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Services.Lexical;
using KnownFirst.Tests.LexicalEvidence;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LexicalEvidenceArtifactWriterTests
{
    [TestMethod]
    public void ResolveRepositoryRoot_AndGetArtifactDirectory_TargetAuthoritativeLexicalLookupEvidencePath()
    {
        var root = LexicalEvidenceArtifactWriter.ResolveRepositoryRoot();
        Assert.IsTrue(Directory.Exists(root));
        Assert.IsTrue(
            File.Exists(Path.Combine(root, "KnownFirst.slnx"))
            || File.Exists(Path.Combine(root, "KnownFirst.csproj")));

        var artifactDir = LexicalEvidenceArtifactWriter.GetArtifactDirectory();
        StringAssert.EndsWith(
            artifactDir,
            Path.Combine("artifacts", "lexical-lookup-evidence"));
        Assert.IsFalse(
            artifactDir.EndsWith(Path.Combine("artifacts", "lexical-evidence"), StringComparison.OrdinalIgnoreCase),
            "Must use authoritative artifacts/lexical-lookup-evidence/ path, never artifacts/lexical-evidence/.");
    }

    [TestMethod]
    public async Task WriteBundle_EmitsCandidatesAndSummaryInUtf8WithoutBomAndLfNormalized()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"kf-lex-writer-test-{Guid.NewGuid():N}");
        try
        {
            var candidates = await CaptureSampleCandidatesInReverseOrderAsync();

            var bundle = LexicalEvidenceArtifactWriter.WriteBundle(
                suiteId: "KF-LEX-005-WRITER-SUITE",
                description: "Deterministic writer verification suite.",
                candidates: candidates,
                targetDirectory: tempDir,
                capturedAtUtc: LexicalEvidenceCaptureHarness.DefaultUtcNow);

            Assert.AreEqual(tempDir, bundle.OutputDirectory);
            Assert.AreEqual(Path.Combine(tempDir, "candidates"), bundle.CandidatesDirectory);
            Assert.AreEqual(Path.Combine(tempDir, "summary.json"), bundle.SummaryJsonPath);
            Assert.AreEqual(Path.Combine(tempDir, "summary.txt"), bundle.SummaryTextPath);

            Assert.IsTrue(File.Exists(bundle.SummaryJsonPath));
            Assert.IsTrue(File.Exists(bundle.SummaryTextPath));
            Assert.HasCount(3, bundle.CandidateFiles);

            // Deterministic ordinal ordering regardless of input order (003, 001, 002 -> 001, 002, 003)
            CollectionAssert.AreEqual(
                new[] { "LEX-WRITER-001", "LEX-WRITER-002", "LEX-WRITER-003" },
                bundle.CandidateFiles.Select(c => c.CaseId).ToArray());
            CollectionAssert.AreEqual(
                new[] { "LEX-WRITER-001", "LEX-WRITER-002", "LEX-WRITER-003" },
                bundle.Document.Candidates.Select(c => c.CaseId).ToArray());

            var allEmittedPaths = new List<string> { bundle.SummaryJsonPath, bundle.SummaryTextPath };
            foreach (var entry in bundle.CandidateFiles)
            {
                Assert.AreEqual(Path.Combine(tempDir, "candidates", $"{entry.CaseId}.json"), entry.JsonPath);
                Assert.AreEqual(Path.Combine(tempDir, "candidates", $"{entry.CaseId}.txt"), entry.TextPath);
                Assert.IsTrue(File.Exists(entry.JsonPath));
                Assert.IsTrue(File.Exists(entry.TextPath));
                allEmittedPaths.Add(entry.JsonPath);
                allEmittedPaths.Add(entry.TextPath);
            }

            foreach (var path in allEmittedPaths)
            {
                var bytes = await File.ReadAllBytesAsync(path);
                Assert.IsGreaterThan(0, bytes.Length, $"File '{path}' must not be empty.");
                var hasUtf8Bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                Assert.IsFalse(hasUtf8Bom, $"File '{path}' must be UTF-8 without BOM.");

                var text = Encoding.UTF8.GetString(bytes);
                Assert.IsFalse(text.Contains("\r\n", StringComparison.Ordinal), $"File '{path}' must use LF-only line endings.");
                Assert.IsTrue(text.EndsWith('\n'), $"File '{path}' must end with LF.");
                Assert.IsFalse(text.Contains(tempDir, StringComparison.OrdinalIgnoreCase), $"File '{path}' must not embed host-specific output directory paths.");
                Assert.IsFalse(text.Contains("C:\\", StringComparison.OrdinalIgnoreCase), $"File '{path}' must not contain Windows drive paths.");
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

    [TestMethod]
    public async Task WriteBundle_PreservesExplicitNumeratorsDenominatorsPercentagesAndConcreteFailedAndProblematicTerms()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"kf-lex-writer-metrics-{Guid.NewGuid():N}");
        try
        {
            var candidates = await CaptureSampleCandidatesInReverseOrderAsync();

            var bundle = LexicalEvidenceArtifactWriter.WriteBundle(
                suiteId: "KF-LEX-005-METRICS-SUITE",
                description: "Denominator and concrete term verification.",
                candidates: candidates,
                targetDirectory: tempDir,
                capturedAtUtc: LexicalEvidenceCaptureHarness.DefaultUtcNow);

            var summaryJsonText = await File.ReadAllTextAsync(bundle.SummaryJsonPath, Encoding.UTF8);
            var summaryTxtText = await File.ReadAllTextAsync(bundle.SummaryTextPath, Encoding.UTF8);

            var roundTrippedDoc = LexicalEvidenceReportFormatter.DeserializeJson(summaryJsonText);
            Assert.AreEqual("KF-LEX-005-METRICS-SUITE", roundTrippedDoc.SuiteId);
            Assert.AreEqual(LexicalEvidenceCaptureHarness.DefaultUtcNow, roundTrippedDoc.CapturedAtUtc);
            Assert.AreEqual(3, roundTrippedDoc.Summary.TotalCandidateCount);
            Assert.AreEqual(2, roundTrippedDoc.Summary.SuccessCount);
            Assert.AreEqual(1, roundTrippedDoc.Summary.TransientFailureCount);

            // Verify explicit rate numerator/denominator/percentage arithmetic
            var overallSuccessRate = roundTrippedDoc.Summary.RateMetrics.Single(m => m.MetricCode == "overall-success-rate");
            Assert.AreEqual(2, overallSuccessRate.Numerator);
            Assert.AreEqual(3, overallSuccessRate.Denominator);
            Assert.AreEqual("TotalCandidateCount", overallSuccessRate.DenominatorName);
            Assert.AreEqual(66.67, overallSuccessRate.Percentage);

            // Concrete failed terms with status and error code
            Assert.HasCount(1, roundTrippedDoc.Summary.FailedTerms);
            var failed = roundTrippedDoc.Summary.FailedTerms[0];
            Assert.AreEqual("LEX-WRITER-003", failed.CaseId);
            Assert.AreEqual(LexicalLookupStatus.TransientFailure, failed.FinalStatus);
            Assert.AreEqual("transient-server-error", failed.FinalErrorCode);

            // Concrete problematic terms with status and error code
            Assert.IsTrue(roundTrippedDoc.Summary.ProblematicTerms.Any(p =>
                p.CaseId == "LEX-WRITER-003"
                && p.ProblemKind == LexicalProblemKind.LookupFailed
                && p.FinalStatus == LexicalLookupStatus.TransientFailure
                && p.FinalErrorCode == "transient-server-error"));

            // No fabricated unobservable internal MeaningRanker scores
            Assert.IsFalse(summaryJsonText.Contains("score", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(summaryTxtText.Contains("score", StringComparison.OrdinalIgnoreCase));

            // Round-trip every per-candidate JSON artifact
            foreach (var entry in bundle.CandidateFiles)
            {
                var candidateJson = await File.ReadAllTextAsync(entry.JsonPath, Encoding.UTF8);
                var deserializedCandidate = LexicalEvidenceReportFormatter.DeserializeCandidateJson(candidateJson);
                Assert.AreEqual(entry.CaseId, deserializedCandidate.CaseId);
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

    [TestMethod]
    public async Task WriteBundle_RepeatedGenerationIntoSeparateTemporaryDirectories_IsByteForByteIdentical()
    {
        var tempDir1 = Path.Combine(Path.GetTempPath(), $"kf-lex-writer-det1-{Guid.NewGuid():N}");
        var tempDir2 = Path.Combine(Path.GetTempPath(), $"kf-lex-writer-det2-{Guid.NewGuid():N}");
        try
        {
            var candidates1 = await CaptureSampleCandidatesInReverseOrderAsync();
            var candidates2 = candidates1.Reverse().ToArray();

            var bundle1 = LexicalEvidenceArtifactWriter.WriteBundle(
                suiteId: "KF-LEX-005-DETERMINISM",
                description: "Byte-stability check across repeated runs.",
                candidates: candidates1,
                targetDirectory: tempDir1,
                capturedAtUtc: LexicalEvidenceCaptureHarness.DefaultUtcNow);

            var bundle2 = LexicalEvidenceArtifactWriter.WriteBundle(
                suiteId: "KF-LEX-005-DETERMINISM",
                description: "Byte-stability check across repeated runs.",
                candidates: candidates2,
                targetDirectory: tempDir2,
                capturedAtUtc: LexicalEvidenceCaptureHarness.DefaultUtcNow);

            CollectionAssert.AreEqual(
                await File.ReadAllBytesAsync(bundle1.SummaryJsonPath),
                await File.ReadAllBytesAsync(bundle2.SummaryJsonPath));
            CollectionAssert.AreEqual(
                await File.ReadAllBytesAsync(bundle1.SummaryTextPath),
                await File.ReadAllBytesAsync(bundle2.SummaryTextPath));

            Assert.HasCount(bundle1.CandidateFiles.Count, bundle2.CandidateFiles);
            for (var i = 0; i < bundle1.CandidateFiles.Count; i++)
            {
                Assert.AreEqual(bundle1.CandidateFiles[i].CaseId, bundle2.CandidateFiles[i].CaseId);
                CollectionAssert.AreEqual(
                    await File.ReadAllBytesAsync(bundle1.CandidateFiles[i].JsonPath),
                    await File.ReadAllBytesAsync(bundle2.CandidateFiles[i].JsonPath));
                CollectionAssert.AreEqual(
                    await File.ReadAllBytesAsync(bundle1.CandidateFiles[i].TextPath),
                    await File.ReadAllBytesAsync(bundle2.CandidateFiles[i].TextPath));
            }
        }
        finally
        {
            if (Directory.Exists(tempDir1)) Directory.Delete(tempDir1, recursive: true);
            if (Directory.Exists(tempDir2)) Directory.Delete(tempDir2, recursive: true);
        }
    }

    [TestMethod]
    public async Task WriteBundle_RejectsDuplicateOrPathTraversalCaseIds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"kf-lex-writer-invalid-{Guid.NewGuid():N}");
        try
        {
            var candidates = await CaptureSampleCandidatesInReverseOrderAsync();
            var duplicateCandidates = new[] { candidates[0], candidates[0] };

            Assert.ThrowsExactly<ArgumentException>(() =>
                LexicalEvidenceArtifactWriter.WriteBundle(
                    suiteId: "DUP-SUITE",
                    description: "Duplicate case IDs",
                    candidates: duplicateCandidates,
                    targetDirectory: tempDir));

            var traversalCandidate = candidates[0] with { CaseId = "../traversal" };
            Assert.ThrowsExactly<ArgumentException>(() =>
                LexicalEvidenceArtifactWriter.WriteBundle(
                    suiteId: "TRAVERSAL-SUITE",
                    description: "Invalid case ID",
                    candidates: [traversalCandidate],
                    targetDirectory: tempDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static async Task<IReadOnlyList<LexicalCandidateEvidence>> CaptureSampleCandidatesInReverseOrderAsync()
    {
        var case3Failure = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "LEX-WRITER-003",
                Description: "Transient HTTP 503 failure case.",
                Request: new LexicalLookupRequest(
                    sourceLanguage: "de",
                    lookupMode: LexicalLookupMode.Definition,
                    targetLanguage: null,
                    canonicalLookupTerm: "Störung",
                    tokenKind: TokenKind.Word,
                    provider: WiktionaryLookupProvider.Name,
                    displayedSurfaceForm: "Störung",
                    vocabularyCanonicalTerm: "Störung"),
                OriginalDocumentContent: "Eine Störung trat auf.",
                RepresentativeContext: "Eine Störung trat auf.",
                OfflineHttpHandler: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));

        var case1German = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "LEX-WRITER-001",
                Description: "German direct definition success case.",
                Request: new LexicalLookupRequest(
                    sourceLanguage: "de",
                    lookupMode: LexicalLookupMode.Definition,
                    targetLanguage: null,
                    canonicalLookupTerm: "Netzwerk",
                    tokenKind: TokenKind.Word,
                    provider: WiktionaryLookupProvider.Name,
                    displayedSurfaceForm: "Netzwerk",
                    vocabularyCanonicalTerm: "Netzwerk"),
                OriginalDocumentContent: "Das Netzwerk läuft stabil.",
                RepresentativeContext: "Das Netzwerk läuft stabil.",
                OfflineHttpHandler: (_, _) => Task.FromResult(JsonResponse(BuildWiktionaryJson(
                    "Netzwerk",
                    "<div class=\"mw-heading mw-heading2\"><h2 id=\"Netzwerk_(Deutsch)\">Netzwerk (Deutsch)</h2></div><div class=\"mw-heading mw-heading3\"><h3>Substantiv</h3></div><p style=\"font-weight:bold;\">Bedeutungen:</p><dl><dd>[1] Ein System verbundener Einheiten.</dd></dl>")))));

        var case2Ranked = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "LEX-WRITER-002",
                Description: "English multi-meaning ranking reorder case.",
                Request: new LexicalLookupRequest(
                    sourceLanguage: "en",
                    lookupMode: LexicalLookupMode.Definition,
                    targetLanguage: null,
                    canonicalLookupTerm: "craft",
                    tokenKind: TokenKind.Word,
                    provider: WiktionaryLookupProvider.Name,
                    displayedSurfaceForm: "craft",
                    vocabularyCanonicalTerm: "craft"),
                OriginalDocumentContent: "She learned the craft quickly.",
                RepresentativeContext: "She learned the craft quickly.",
                OfflineHttpHandler: (_, _) => Task.FromResult(JsonResponse(BuildWiktionaryJson(
                    "craft",
                    "<div class=\"mw-heading mw-heading2\"><h2 id=\"English\">English</h2></div><div class=\"mw-heading mw-heading3\"><h3>Noun</h3></div><ol><li><span class=\"usage-label\">archaic</span><span class=\"definition\">Occult art or magic.</span></li><li><span class=\"definition\">A skill or trade requiring manual dexterity.</span></li></ol>")))));

        return [case3Failure, case1German, case2Ranked];
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        return response;
    }

    private static string BuildWiktionaryJson(string title, string html)
    {
        var escapedTitle = title.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        var escapedHtml = html.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        return $$"""
        {
          "parse": {
            "title": "{{escapedTitle}}",
            "pageid": 100,
            "revid": 5000,
            "text": "{{escapedHtml}}"
          }
        }
        """;
    }
}
