namespace KnownFirst.Core.Preparation;

public sealed record PreparationSelectionCandidate(
    int WordId,
    string CanonicalTerm,
    int AcceptedOccurrenceCount,
    DateTime FirstSeenAtUtc,
    bool IsUnknown,
    PreparationState State,
    bool ReviewIsResolved,
    bool HasPreparedItem);

public static class PreparationSelectionPolicy
{
    public const int HardMaximum = 50;

    public static IReadOnlyList<PreparationSelectionCandidate> Select(
        IEnumerable<PreparationSelectionCandidate> candidates,
        int requestedLimit)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var limit = Math.Clamp(requestedLimit, 0, HardMaximum);
        return candidates
            .Where(candidate => candidate.IsUnknown
                && candidate.State == PreparationState.Unprepared
                && candidate.ReviewIsResolved
                && !candidate.HasPreparedItem)
            .OrderByDescending(candidate => candidate.AcceptedOccurrenceCount)
            .ThenBy(candidate => candidate.FirstSeenAtUtc)
            .ThenBy(candidate => candidate.CanonicalTerm, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }
}
