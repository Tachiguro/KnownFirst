using KnownFirst.Models;

namespace KnownFirst.Services;

/// <summary>
/// Bounded, database-indexed, read-only SQLite vocabulary search service with deterministic keyset pagination.
/// </summary>
public interface IVocabularySearchService
{
    public const int DefaultPageSize = VocabularySearchRequest.DefaultPageSize;
    public const int MaxPageSize = VocabularySearchRequest.MaxPageSize;

    Task<VocabularySearchPage> SearchAsync(
        VocabularySearchRequest request,
        CancellationToken cancellationToken = default);

    Task<VocabularySearchPage> SearchAsync(
        string? sourceLanguage,
        string? rawQuery,
        int pageSize = DefaultPageSize,
        VocabularySearchCursor? cursor = null,
        CancellationToken cancellationToken = default) =>
        SearchAsync(
            new VocabularySearchRequest(sourceLanguage, rawQuery, pageSize, cursor),
            cancellationToken);
}
