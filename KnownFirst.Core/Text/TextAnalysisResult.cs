namespace KnownFirst.Core.Text;

public sealed record TextAnalysisResult(
    IReadOnlyList<TextSpan> Sentences,
    IReadOnlyList<VocabularyCandidate> Candidates,
    int OccurrenceCount)
{
#if DEBUG
    public TextAnalysisDiagnostics? Diagnostics { get; init; }
#endif
}
