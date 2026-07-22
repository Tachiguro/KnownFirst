namespace KnownFirst.Core.Text;

public sealed record TextSpan(
    int StartPosition,
    int Length,
    int Order,
    string BoundaryReasonCode = AnalysisReasonCodes.SentenceBoundaryFinalRemainder,
    string BoundaryExplanation = "Final non-empty remainder retained as one sentence.")
{
    public int EndPosition => StartPosition + Length;
}
