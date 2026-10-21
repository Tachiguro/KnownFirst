using KnownFirst.Core.Text;

namespace KnownFirst.Models;

public enum VocabularySearchStatus
{
    Active = 0,
    EmptyQuery = 1,
    InvalidSourceLanguage = 2,
    QueryTooLong = 3,
    InvalidUnicode = 4,
    InvalidPageSize = 5,
    InvalidCursor = 6
}

/// <summary>
/// Immutable keyset continuation token bound to a specific normalized query, source language, and page size.
/// </summary>
public sealed record VocabularySearchCursor(
    string SourceLanguage,
    string NormalizedQuery,
    int PageSize,
    string LastNormalizedTerm,
    int LastWordId);

/// <summary>
/// Immutable request parameters for a bounded, indexed vocabulary search page.
/// </summary>
public sealed record VocabularySearchRequest(
    string? SourceLanguage,
    string? RawQuery,
    int PageSize = VocabularySearchRequest.DefaultPageSize,
    VocabularySearchCursor? Cursor = null)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 30;

    public string? Query => RawQuery;
}

/// <summary>
/// Compact immutable projection representing a persisted vocabulary identity returned by indexed search.
/// </summary>
public sealed record VocabularySearchItem(
    int WordId,
    string CanonicalTerm,
    string NormalizedTerm,
    string SourceLanguage,
    TokenKind TokenKind,
    DateTime CreatedAt)
{
    public int Id => WordId;

    public string Language => SourceLanguage;

    public VocabularyOverviewItem ToOverviewItem() =>
        new(WordId, CanonicalTerm, SourceLanguage, CreatedAt);
}

/// <summary>
/// Immutable result page for a bounded vocabulary search request.
/// </summary>
public sealed record VocabularySearchPage(
    VocabularySearchStatus Status,
    VocabularySearchQueryStatus QueryStatus,
    string SourceLanguage,
    string NormalizedQuery,
    int PageSize,
    IReadOnlyList<VocabularySearchItem> Items,
    bool HasMore,
    VocabularySearchCursor? NextCursor)
{
    public static IReadOnlyList<VocabularySearchItem> EmptyItems { get; } =
        Array.AsReadOnly(Array.Empty<VocabularySearchItem>());

    public bool IsValid =>
        Status is VocabularySearchStatus.Active or VocabularySearchStatus.EmptyQuery;

    public bool IsSearchActive => Status == VocabularySearchStatus.Active;

    public bool UseRecentOverview => Status == VocabularySearchStatus.EmptyQuery;
}
