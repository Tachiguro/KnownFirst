using System.Text;

namespace KnownFirst.Tests.AnalysisEvidence;

public static class AnalysisEvidenceTextFormatter
{
    public static string Format(AnalysisEvidenceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var sb = new StringBuilder();

        sb.Append("=== ANALYSIS EVIDENCE REPORT ===\n");
        sb.Append("Input Length: ").Append(document.Input.Length).Append(" UTF-16 code units\n");
        sb.Append("Source Language: ").Append(document.Input.SourceLanguage ?? "<null>").Append('\n');
        sb.Append("Content SHA-256: ").Append(document.Input.ContentSha256).Append('\n');
        sb.Append("Input Text (Escaped): \"").Append(EscapeString(document.Input.Text)).Append("\"\n\n");

        sb.Append("--- SENTENCES (").Append(document.Sentences.Count).Append(") ---\n");
        foreach (var sentence in document.Sentences)
        {
            sb.Append('[').Append(sentence.Order).Append("] (")
              .Append(sentence.StartPosition).Append("..")
              .Append(sentence.StartPosition + sentence.Length).Append(", len ")
              .Append(sentence.Length).Append(") [ExactMatch: ")
              .Append(sentence.ExactSubstringMatch ? "True" : "False").Append("] [Boundary: ")
              .Append(sentence.BoundaryReasonCode).Append("]\n");
            sb.Append("  Text: \"").Append(EscapeString(sentence.Text)).Append("\"\n");
            sb.Append("  Explanation: ").Append(sentence.BoundaryExplanation).Append('\n');
        }
        sb.Append('\n');

        sb.Append("--- TOKEN DECISIONS (").Append(document.TokenDecisions.Count).Append(") ---\n");
        for (var i = 0; i < document.TokenDecisions.Count; i++)
        {
            var token = document.TokenDecisions[i];
            sb.Append('[').Append(i).Append("] (")
              .Append(token.StartPosition).Append("..")
              .Append(token.StartPosition + token.Length).Append(", len ")
              .Append(token.Length).Append(", Sent: ")
              .Append(token.SentenceOrder?.ToString() ?? "<null>").Append(") [Included: ")
              .Append(token.IsIncluded ? "True" : "False").Append("] [Kind: ")
              .Append(token.Kind?.ToString() ?? "<null>").Append("] [Reason: ")
              .Append(token.ReasonCode).Append("] [ExactMatch: ")
              .Append(token.ExactSubstringMatch ? "True" : "False").Append("]\n");
            sb.Append("  Raw: \"").Append(EscapeString(token.RawValue)).Append("\" -> Normalized: \"")
              .Append(EscapeString(token.NormalizedValue)).Append("\"\n");
            sb.Append("  Explanation: ").Append(token.Explanation).Append('\n');
        }
        sb.Append('\n');

        sb.Append("--- CANDIDATES (").Append(document.Candidates.Count).Append(") ---\n");
        for (var i = 0; i < document.Candidates.Count; i++)
        {
            var candidate = document.Candidates[i];
            sb.Append('[').Append(i).Append("] Identity: \"").Append(candidate.Identity)
              .Append("\" | Canonical: \"").Append(candidate.CanonicalTerm)
              .Append("\" | Kind: ").Append(candidate.Kind)
              .Append(" | Provenance: ").Append(candidate.Provenance)
              .Append(" | Occurrences: ").Append(candidate.OccurrenceCount).Append('\n');

            sb.Append("  Surface Forms (").Append(candidate.SurfaceForms.Count).Append("):\n");
            foreach (var form in candidate.SurfaceForms)
            {
                sb.Append("    \"").Append(EscapeString(form.Form)).Append("\": ").Append(form.Count).Append('\n');
            }

            sb.Append("  Occurrences (").Append(candidate.Occurrences.Count).Append("):\n");
            foreach (var occ in candidate.Occurrences)
            {
                sb.Append("    #").Append(occ.Order).Append(" (").Append(occ.StartPosition).Append("..")
                  .Append(occ.StartPosition + occ.Length).Append(", len ").Append(occ.Length)
                  .Append(", Sent: ").Append(occ.SentenceOrder).Append(") [ExactMatch: ")
                  .Append(occ.ExactSubstringMatch ? "True" : "False").Append("] Surface: \"")
                  .Append(EscapeString(occ.SurfaceForm)).Append("\"\n");
            }

            if (candidate.DerivedEvidence.Count > 0)
            {
                sb.Append("  Derived Evidence (").Append(candidate.DerivedEvidence.Count).Append("):\n");
                foreach (var derived in candidate.DerivedEvidence)
                {
                    sb.Append("    Source: \"").Append(derived.SourceIdentity).Append("\" (")
                      .Append(derived.SourceStartPosition).Append("..")
                      .Append(derived.SourceStartPosition + derived.SourceLength).Append(", Sent: ")
                      .Append(derived.SourceSentenceOrder).Append(") [ExactMatch: ")
                      .Append(derived.ExactSubstringMatch ? "True" : "False").Append("] Surface: \"")
                      .Append(EscapeString(derived.SourceSurfaceForm)).Append("\" Component: \"")
                      .Append(EscapeString(derived.ComponentForm)).Append("\"\n");
                }
            }
        }
        sb.Append('\n');

        if (document.CandidateGroups.Count > 0)
        {
            sb.Append("--- CANDIDATE GROUPS (").Append(document.CandidateGroups.Count).Append(") ---\n");
            for (var i = 0; i < document.CandidateGroups.Count; i++)
            {
                var group = document.CandidateGroups[i];
                sb.Append('[').Append(i).Append("] Identity: \"").Append(group.Identity)
                  .Append("\" | Canonical: \"").Append(group.CanonicalTerm)
                  .Append("\" | Kind: ").Append(group.Kind)
                  .Append(" | Reason: ").Append(group.ReasonCode)
                  .Append(" | Occurrences: ").Append(group.OccurrenceCount).Append('\n');
                sb.Append("  Forms Before: [").Append(string.Join(", ", group.FormsBeforeDeduplication.Select(f => $"\"{EscapeString(f)}\""))).Append("]\n");
                sb.Append("  Forms After:  [").Append(string.Join(", ", group.FormsAfterDeduplication.Select(f => $"\"{EscapeString(f)}\""))).Append("]\n");
                sb.Append("  Explanation: ").Append(group.Explanation).Append('\n');
            }
            sb.Append('\n');
        }

        if (document.ContextDecisions.Count > 0)
        {
            sb.Append("--- CONTEXT DECISIONS (").Append(document.ContextDecisions.Count).Append(") ---\n");
            for (var i = 0; i < document.ContextDecisions.Count; i++)
            {
                var context = document.ContextDecisions[i];
                sb.Append('[').Append(i).Append("] Candidate: \"").Append(context.CandidateIdentity)
                  .Append("\" | Occ: ").Append(context.OccurrenceOrder)
                  .Append(" | Sent: ").Append(context.SentenceOrder)
                  .Append(" | Selected: ").Append(context.IsSelected ? "True" : "False")
                  .Append(" | Reason: ").Append(context.ReasonCode).Append('\n');
                sb.Append("  Target: \"").Append(EscapeString(context.Target)).Append("\" (at index ")
                  .Append(context.TargetStartInSentence).Append(" in sentence)\n");
                sb.Append("  Fingerprint: ").Append(context.Fingerprint).Append('\n');
                sb.Append("  Sentence Text: \"").Append(EscapeString(context.SentenceText)).Append("\"\n");
                sb.Append("  Explanation: ").Append(context.Explanation).Append('\n');
            }
            sb.Append('\n');
        }

        sb.Append("--- INVARIANT VALIDATION ---\n");
        sb.Append("Failures: ").Append(document.InvariantFailures.Count)
          .Append(document.InvariantsPassed ? " (PASSED)" : " (FAILED)").Append('\n');
        foreach (var failure in document.InvariantFailures)
        {
            sb.Append("  [").Append(failure.Code).Append("] ").Append(failure.Explanation).Append('\n');
        }

        return sb.ToString();
    }

    public static string EscapeString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length + 16);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '\"': sb.Append(@"\"""); break;
                case '\r': sb.Append(@"\r"); break;
                case '\n': sb.Append(@"\n"); break;
                case '\t': sb.Append(@"\t"); break;
                case '\0': sb.Append(@"\0"); break;
                default:
                    if (char.IsControl(c))
                    {
                        sb.Append($@"\u{(int)c:x4}");
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.ToString();
    }
}
