namespace KnownFirst.Core.Learning;

public sealed record SpellingComparisonResult(
    bool IsCorrect,
    string EnteredAnswer,
    string ExpectedAnswer,
    string Difference,
    string? MatchedAlias);
