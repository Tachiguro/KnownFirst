namespace KnownFirst.Services.Lexical.Wikipedia;

public sealed record WikipediaArticleResult(
    WikipediaArticleStatus Status,
    string RequestedTitle,
    string CanonicalTitle,
    string Extract,
    string SourceLanguage,
    string SourceProject,
    long PageId,
    long RevisionId,
    string CanonicalUrl,
    bool IsRedirect,
    string? RedirectedFrom,
    string? TargetLanguage,
    string? TargetTitleCandidate,
    string? TargetUrlCandidate,
    string Attribution,
    string? ErrorCode,
    TimeSpan? RetryAfter);
