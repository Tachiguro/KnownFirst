namespace KnownFirst.Services.Lexical.Wikipedia;

public enum WikipediaArticleStatus
{
    Success,
    NotFound,
    Disambiguation,
    NoUsableContent,
    RateLimited,
    TimedOut,
    TransientFailure,
    PermanentFailure,
    ParseFailure
}
