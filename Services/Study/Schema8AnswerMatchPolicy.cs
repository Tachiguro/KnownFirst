using KnownFirst.Core.Learning;
using KnownFirst.Core.Text;
using KnownFirst.Data.Schema8;

namespace KnownFirst.Services.Study;

/// <summary>The outcome of comparing a typed answer against one card direction's complete assignment set.</summary>
public enum Schema8MatchKind
{
    /// <summary>No assigned variant of this direction matched.</summary>
    NoMatch,

    /// <summary>The targeted variant itself matched.</summary>
    MatchedTarget,

    /// <summary>A different variant matched whose assignment is Required.</summary>
    MatchedOtherRequired,

    /// <summary>A different variant matched whose assignment is AcceptedOnly — semantically correct, but it never creates or advances a progress row.</summary>
    MatchedOtherAcceptedOnly
}

/// <summary>
/// The resolved match. <see cref="MatchedAnswerVariantId"/> is null exactly for
/// <see cref="Schema8MatchKind.NoMatch"/>.
/// </summary>
public sealed record Schema8AnswerMatch(
    Schema8MatchKind Kind,
    int? MatchedAnswerVariantId,
    string ExpectedAnswer,
    string EnteredAnswer,
    string Difference)
{
    public bool IsCorrect => Kind != Schema8MatchKind.NoMatch;

    /// <summary>
    /// Whether this match may produce or advance a progress row. An AcceptedOnly match is correct but
    /// deliberately grants nothing (Decision 12).
    /// </summary>
    public bool GrantsProgress =>
        Kind is Schema8MatchKind.MatchedTarget or Schema8MatchKind.MatchedOtherRequired;
}

/// <summary>
/// Pure resolution of a typed answer against the complete assignment set of one <c>(SenseId, CardDirection)</c>
/// (KF-MEANING-001 Slice 4).
/// <para>
/// <c>AnswerLanguage</c> is never consulted to infer or reject a card direction — the assignment's own
/// <c>CardDirection</c> is the sole authority, which matters whenever source and explanation language are
/// identical. Comparison reuses the existing <see cref="SpellingAnswerComparer"/> so Schema-8 spelling
/// semantics (Unicode Form C, acronym and German-noun case sensitivity) stay identical to Schema 7. Two
/// different variants matching the same input is ambiguous and is reported so the caller can fail closed
/// before any mutation.
/// </para>
/// </summary>
public static class Schema8AnswerMatchPolicy
{
    public static Schema8AnswerMatch Resolve(
        SpellingAnswerComparer comparer,
        string? enteredAnswer,
        int targetAnswerVariantId,
        IReadOnlyList<Schema8AttributionCandidateRow> assignments,
        TokenKind tokenKind,
        string sourceLanguage)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        ArgumentNullException.ThrowIfNull(assignments);

        var target = assignments.SingleOrDefault(a => a.AnswerVariantId == targetAnswerVariantId)
            ?? throw Schema8LearningDataException.Create(
                Schema8LearningDataErrorCode.InvalidAssignmentGraph,
                $"Target variant {targetAnswerVariantId} has no assignment for this card's direction.");

        var matches = new List<Schema8AttributionCandidateRow>();
        var difference = string.Empty;
        foreach (var candidate in assignments.OrderBy(a => a.AnswerVariantId))
        {
            var comparison = comparer.Compare(
                enteredAnswer, candidate.DisplayText, acceptedAliases: null, tokenKind, sourceLanguage);
            if (comparison.IsCorrect)
            {
                matches.Add(candidate);
            }
            else if (candidate.AnswerVariantId == targetAnswerVariantId)
            {
                difference = comparison.Difference;
            }
        }

        if (matches.Count == 0)
        {
            return new Schema8AnswerMatch(
                Schema8MatchKind.NoMatch, null, target.DisplayText, enteredAnswer ?? string.Empty, difference);
        }

        if (matches.Count > 1)
        {
            throw Schema8LearningDataException.Create(
                Schema8LearningDataErrorCode.InvalidMatchEvidence,
                $"The entered answer matches {matches.Count} assigned variants of this direction " +
                $"({string.Join(", ", matches.Select(m => m.AnswerVariantId))}); the assignment graph is ambiguous.");
        }

        var matched = matches[0];
        var kind = matched.AnswerVariantId == targetAnswerVariantId
            ? Schema8MatchKind.MatchedTarget
            : matched.IsRequired
                ? Schema8MatchKind.MatchedOtherRequired
                : Schema8MatchKind.MatchedOtherAcceptedOnly;

        return new Schema8AnswerMatch(
            kind, matched.AnswerVariantId, target.DisplayText, enteredAnswer ?? string.Empty, string.Empty);
    }
}
