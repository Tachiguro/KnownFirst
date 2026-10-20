using System.Text;

namespace KnownFirst.Tests.LexicalEvidence;

public sealed record LexicalCandidateArtifactEntry(
    string CaseId,
    string JsonPath,
    string TextPath);

public sealed record LexicalEvidenceArtifactBundle(
    string OutputDirectory,
    string CandidatesDirectory,
    string SummaryJsonPath,
    string SummaryTextPath,
    IReadOnlyList<LexicalCandidateArtifactEntry> CandidateFiles,
    LexicalEvidenceReportDocument Document);

public static class LexicalEvidenceArtifactWriter
{
    public const string CanonicalRelativeDirectory = "artifacts/lexical-lookup-evidence";

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly char[] InvalidCaseIdChars = Path.GetInvalidFileNameChars();

    public static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "KnownFirst.slnx"))
                || File.Exists(Path.Combine(current.FullName, "KnownFirst.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate repository root containing KnownFirst.slnx or KnownFirst.csproj.");
    }

    public static string GetArtifactDirectory() =>
        Path.Combine(ResolveRepositoryRoot(), "artifacts", "lexical-lookup-evidence");

    public static LexicalEvidenceArtifactBundle WriteBundle(
        string suiteId,
        string description,
        IReadOnlyList<LexicalCandidateEvidence> candidates,
        string? targetDirectory = null,
        DateTime? capturedAtUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suiteId);
        ArgumentNullException.ThrowIfNull(candidates);

        ValidateCandidates(candidates);

        var document = LexicalEvidenceCaptureHarness.BuildReportDocument(
            suiteId: suiteId,
            description: description ?? string.Empty,
            candidates: candidates,
            capturedAtUtc: capturedAtUtc ?? LexicalEvidenceCaptureHarness.DefaultUtcNow);

        return WriteNormalizedDocument(document, targetDirectory);
    }

    public static LexicalEvidenceArtifactBundle WriteBundle(
        LexicalEvidenceReportDocument document,
        string? targetDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.SuiteId);
        ArgumentNullException.ThrowIfNull(document.Candidates);

        ValidateCandidates(document.Candidates);

        var normalizedDocument = LexicalEvidenceCaptureHarness.BuildReportDocument(
            suiteId: document.SuiteId,
            description: document.Description,
            candidates: document.Candidates,
            capturedAtUtc: document.CapturedAtUtc == default
                ? LexicalEvidenceCaptureHarness.DefaultUtcNow
                : document.CapturedAtUtc);

        return WriteNormalizedDocument(normalizedDocument, targetDirectory);
    }

    private static LexicalEvidenceArtifactBundle WriteNormalizedDocument(
        LexicalEvidenceReportDocument document,
        string? targetDirectory)
    {
        var outputDir = string.IsNullOrWhiteSpace(targetDirectory)
            ? GetArtifactDirectory()
            : targetDirectory;
        var candidatesDir = Path.Combine(outputDir, "candidates");

        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(candidatesDir);

        var candidateEntries = new List<LexicalCandidateArtifactEntry>(document.Candidates.Count);
        foreach (var candidate in document.Candidates)
        {
            var jsonPath = Path.Combine(candidatesDir, $"{candidate.CaseId}.json");
            var textPath = Path.Combine(candidatesDir, $"{candidate.CaseId}.txt");

            var candidateJson = NormalizeLf(LexicalEvidenceReportFormatter.FormatCandidateJson(candidate));
            var candidateText = NormalizeLf(LexicalEvidenceReportFormatter.FormatCandidateText(candidate));

            File.WriteAllText(jsonPath, candidateJson, Utf8WithoutBom);
            File.WriteAllText(textPath, candidateText, Utf8WithoutBom);

            candidateEntries.Add(new LexicalCandidateArtifactEntry(
                CaseId: candidate.CaseId,
                JsonPath: jsonPath,
                TextPath: textPath));
        }

        var summaryJsonPath = Path.Combine(outputDir, "summary.json");
        var summaryTextPath = Path.Combine(outputDir, "summary.txt");

        var summaryJson = NormalizeLf(LexicalEvidenceReportFormatter.FormatJson(document));
        var summaryText = NormalizeLf(LexicalEvidenceReportFormatter.FormatText(document));

        File.WriteAllText(summaryJsonPath, summaryJson, Utf8WithoutBom);
        File.WriteAllText(summaryTextPath, summaryText, Utf8WithoutBom);

        return new LexicalEvidenceArtifactBundle(
            OutputDirectory: outputDir,
            CandidatesDirectory: candidatesDir,
            SummaryJsonPath: summaryJsonPath,
            SummaryTextPath: summaryTextPath,
            CandidateFiles: candidateEntries,
            Document: document);
    }

    private static void ValidateCandidates(IReadOnlyList<LexicalCandidateEvidence> candidates)
    {
        var seenCaseIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            if (string.IsNullOrWhiteSpace(candidate.CaseId))
            {
                throw new ArgumentException("Candidate CaseId must not be null or whitespace.", nameof(candidates));
            }

            if (candidate.CaseId.Contains('/', StringComparison.Ordinal)
                || candidate.CaseId.Contains('\\', StringComparison.Ordinal)
                || candidate.CaseId.Contains("..", StringComparison.Ordinal)
                || candidate.CaseId.IndexOfAny(InvalidCaseIdChars) >= 0)
            {
                throw new ArgumentException(
                    $"Candidate CaseId '{candidate.CaseId}' contains invalid file name or path characters.",
                    nameof(candidates));
            }

            if (!seenCaseIds.Add(candidate.CaseId))
            {
                throw new ArgumentException(
                    $"Duplicate candidate CaseId '{candidate.CaseId}' is not permitted in an evidence bundle.",
                    nameof(candidates));
            }
        }
    }

    private static string NormalizeLf(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        return normalized.EndsWith('\n') ? normalized : normalized + "\n";
    }
}
