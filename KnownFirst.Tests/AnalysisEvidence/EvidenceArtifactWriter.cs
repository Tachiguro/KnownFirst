using System.Text;

namespace KnownFirst.Tests.AnalysisEvidence;

public static class EvidenceArtifactWriter
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "KnownFirst.slnx"))
                || File.Exists(Path.Combine(current.FullName, "KnownFirst.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing KnownFirst.slnx or KnownFirst.csproj.");
    }

    public static string GetArtifactDirectory() =>
        Path.Combine(ResolveRepositoryRoot(), "artifacts", "text-analysis-evidence", "german-gold-corpus");

    public static string FormatText(GermanGoldCorpusEvidenceArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var sb = new StringBuilder();
        sb.Append("=== GERMAN GOLD CORPUS EVIDENCE ARTIFACT ===\n");
        sb.Append("Case ID: ").Append(artifact.CaseId).Append('\n');
        sb.Append("Description: ").Append(artifact.Description).Append('\n');
        sb.Append("Authority: ").Append(artifact.Authority switch
        {
            CorpusExpectationAuthority.BindingContract => "BINDING_CONTRACT",
            CorpusExpectationAuthority.VerifiedCurrentBehavior => "VERIFIED_CURRENT_BEHAVIOR",
            CorpusExpectationAuthority.CharacterizationExpectation => "CHARACTERIZATION_EXPECTATION",
            CorpusExpectationAuthority.UnspecifiedRequiresDecision => "UNSPECIFIED_REQUIRES_DECISION",
            _ => artifact.Authority.ToString()
        }).Append('\n');
        sb.Append('\n');

        sb.Append(AnalysisEvidenceTextFormatter.Format(artifact.AnalyzerEvidence));
        sb.Append('\n');

        sb.Append("=== REVIEW ADMISSION EVIDENCE ===\n");
        if (!artifact.ReviewAdmission.AdmissionExecuted)
        {
            sb.Append("Admission Executed: False (Review admission execution was not requested for this case)\n");
            if (!string.IsNullOrWhiteSpace(artifact.ReviewAdmission.Notes))
            {
                sb.Append("Notes: ").Append(artifact.ReviewAdmission.Notes).Append('\n');
            }
        }
        else
        {
            sb.Append("Admission Executed: True\n");
            sb.Append("Import Outcome: ").Append(artifact.ReviewAdmission.Outcome?.ToString() ?? "<null>").Append('\n');
            sb.Append("Candidate Count: ").Append(artifact.ReviewAdmission.CandidateCount?.ToString() ?? "<null>").Append('\n');

            if (artifact.ReviewAdmission.PreImportVocabulary is { Count: > 0 } preVocab)
            {
                sb.Append("\n--- Pre-Import Vocabulary (").Append(preVocab.Count).Append(") ---\n");
                for (var i = 0; i < preVocab.Count; i++)
                {
                    var entry = preVocab[i];
                    sb.Append('[').Append(i).Append("] Identity: \"").Append(entry.Identity)
                      .Append("\" | Canonical: \"").Append(entry.CanonicalTerm ?? "<null>")
                      .Append("\" | Status: ").Append(entry.Status)
                      .Append(" | Occurrences: ").Append(entry.TotalOccurrenceCount)
                      .Append(" | Docs: ").Append(entry.DocumentCount).Append('\n');
                }
            }

            if (artifact.ReviewAdmission.CandidateAdmissions is { Count: > 0 } admissions)
            {
                sb.Append("\n--- Candidate Admissions (").Append(admissions.Count).Append(") ---\n");
                for (var i = 0; i < admissions.Count; i++)
                {
                    var entry = admissions[i];
                    sb.Append('[').Append(i).Append("] Identity: \"").Append(entry.Identity)
                      .Append("\" | Canonical: \"").Append(entry.CanonicalTerm)
                      .Append("\" | PreImportStatus: ").Append(entry.PreImportStatus?.ToString() ?? "<null>")
                      .Append(" | Disposition: ").Append(entry.Disposition)
                      .Append(" | ReviewOrder: ").Append(entry.PersistedReviewOrder?.ToString() ?? "<null>")
                      .Append(" | WasCreated: ").Append(entry.WasWordCreatedForSession ? "True" : "False")
                      .Append(" | Occurrences: ").Append(entry.TotalOccurrenceCountBefore).Append(" -> ").Append(entry.TotalOccurrenceCountAfter)
                      .Append(" | Docs: ").Append(entry.DocumentCountBefore).Append(" -> ").Append(entry.DocumentCountAfter)
                      .Append(" | Contribution: ").Append(entry.OccurrenceContributionCount).Append('\n');
                }
            }

            if (artifact.ReviewAdmission.PersistedReviewCandidates is { Count: > 0 } persisted)
            {
                sb.Append("\n--- Persisted Review Candidates (").Append(persisted.Count).Append(") ---\n");
                for (var i = 0; i < persisted.Count; i++)
                {
                    var entry = persisted[i];
                    sb.Append('[').Append(i).Append("] Order: ").Append(entry.Order)
                      .Append(" | Identity: \"").Append(entry.Identity)
                      .Append("\" | Canonical: \"").Append(entry.CanonicalTerm)
                      .Append("\" | Status: ").Append(entry.Status)
                      .Append(" | PrevStatus: ").Append(entry.PreviousWordStatus)
                      .Append(" | WasCreated: ").Append(entry.WasWordCreatedForSession ? "True" : "False")
                      .Append(" | Occurrences: ").Append(entry.OccurrenceCount).Append('\n');
                }
            }
        }

        return sb.ToString();
    }

    public static string FormatJson(GermanGoldCorpusEvidenceArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        return AnalysisEvidenceJsonFormatter.Format(artifact);
    }

    public static (string TxtPath, string JsonPath) WriteArtifacts(
        GermanGoldCorpusEvidenceArtifact artifact,
        string? targetDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        targetDirectory ??= GetArtifactDirectory();
        Directory.CreateDirectory(targetDirectory);

        var txtPath = Path.Combine(targetDirectory, $"{artifact.CaseId}.txt");
        var jsonPath = Path.Combine(targetDirectory, $"{artifact.CaseId}.json");

        var txtContent = FormatText(artifact);
        var jsonContent = FormatJson(artifact);

        File.WriteAllText(txtPath, txtContent, Utf8WithoutBom);
        File.WriteAllText(jsonPath, jsonContent, Utf8WithoutBom);

        return (txtPath, jsonPath);
    }

    public static IReadOnlyList<(string TxtPath, string JsonPath)> WriteAll(
        IEnumerable<GermanGoldCorpusEvidenceArtifact> artifacts,
        string? targetDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(artifacts);

        var results = new List<(string TxtPath, string JsonPath)>();
        foreach (var artifact in artifacts)
        {
            results.Add(WriteArtifacts(artifact, targetDirectory));
        }

        return results;
    }
}
