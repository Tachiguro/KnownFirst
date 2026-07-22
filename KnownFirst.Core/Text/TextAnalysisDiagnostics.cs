namespace KnownFirst.Core.Text;

public sealed record TokenAnalysisDecision(
    string RawValue,
    int StartPosition,
    int Length,
    string NormalizedValue,
    TokenKind? Kind,
    bool IsIncluded,
    string ReasonCode,
    string Explanation,
    int? SentenceOrder);

public sealed record CandidateGroupingAnalysis(
    string Identity,
    string CanonicalTerm,
    TokenKind Kind,
    IReadOnlyList<string> FormsBeforeDeduplication,
    IReadOnlyList<string> FormsAfterDeduplication,
    int OccurrenceCount,
    string ReasonCode,
    string Explanation);

public sealed record AnalysisInvariantFailure(string Code, string Explanation);

public sealed record TextAnalysisDiagnostics(
    IReadOnlyList<TokenAnalysisDecision> TokenDecisions,
    IReadOnlyList<CandidateGroupingAnalysis> CandidateGroups,
    IReadOnlyList<ContextSelectionDecision> ContextDecisions,
    IReadOnlyList<AnalysisInvariantFailure> InvariantFailures);
