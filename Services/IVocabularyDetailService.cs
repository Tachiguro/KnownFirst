using KnownFirst.Models;

namespace KnownFirst.Services;

/// <summary>
/// Provides a bounded, read-only Schema-14 Vocabulary Detail projection for a single persisted Word.
/// </summary>
public interface IVocabularyDetailService
{
    /// <summary>
    /// Default maximum number of items returned for each repeating child collection.
    /// </summary>
    public const int DefaultMaxItemsPerCollection = 30;

    /// <summary>
    /// Hard upper bound on items returned for each repeating child collection in V1.
    /// </summary>
    public const int MaxItemsPerCollection = 30;

    /// <summary>
    /// Loads the read-only Vocabulary Detail projection for the specified <paramref name="wordId"/>
    /// inside a single database snapshot.
    /// </summary>
    Task<VocabularyDetailLookupResult> GetVocabularyDetailAsync(
        int wordId,
        int maxItemsPerCollection = DefaultMaxItemsPerCollection,
        CancellationToken cancellationToken = default);
}
