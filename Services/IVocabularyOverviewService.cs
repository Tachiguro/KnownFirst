using KnownFirst.Models;

namespace KnownFirst.Services;

/// <summary>
/// Small deterministic, read-only service for retrieving a bounded recent-vocabulary overview.
/// </summary>
public interface IVocabularyOverviewService
{
    public const int DefaultLimit = 30;
    public const int MaxLimit = 30;

    /// <summary>
    /// Returns a bounded collection of recent vocabulary identities ordered descending by creation
    /// timestamp with deterministic persistent Word ID tie-breaking.
    /// </summary>
    Task<IReadOnlyList<VocabularyOverviewItem>> GetRecentVocabularyAsync(int limit = DefaultLimit);

    Task<IReadOnlyList<VocabularyOverviewItem>> GetRecentOverviewAsync(int limit = DefaultLimit) =>
        GetRecentVocabularyAsync(limit);
}
