using KnownFirst.Core.Text;

namespace KnownFirst.Core.Preparation;

public enum LexicalLookupStatus
{
    Success = 0,
    NotFound = 1,
    TransientFailure = 2,
    PermanentFailure = 3,
    ParseFailure = 4,

    Unavailable = TransientFailure,
    RateLimited = TransientFailure,
    MalformedResponse = ParseFailure,
    TimedOut = TransientFailure
}

public sealed record LexicalLookupRequest(
    string Term,
    string NormalizedLemma,
    TokenKind TokenKind,
    string SourceLanguage,
    string ExplanationLanguage);

public sealed record LexicalMeaning(
    string MeaningId,
    string? PartOfSpeech,
    string Definition,
    string? Translation,
    string? Example,
    IReadOnlyList<string> UsageLabels);

public sealed record LexicalResult(
    LexicalLookupStatus Status,
    string QueriedLemma,
    string DisplayTerm,
    TokenKind TokenKind,
    string SourceLanguage,
    string ExplanationLanguage,
    string? AcronymExpansion,
    IReadOnlyList<LexicalMeaning> Meanings,
    string ProviderName,
    string SourceProject,
    string PageTitle,
    long? RevisionId,
    string Attribution,
    DateTime LookupAtUtc,
    bool IsFromCache = false,
    string? ErrorCode = null,
    string? EncounteredSurfaceForm = null,
    string? GrammaticalRelationship = null,
    int RedirectDepth = 0)
{
    public bool HasUsableData => Status == LexicalLookupStatus.Success
        && (!string.IsNullOrWhiteSpace(AcronymExpansion)
            || Meanings.Any(meaning => !string.IsNullOrWhiteSpace(meaning.Definition)
                || !string.IsNullOrWhiteSpace(meaning.Translation)));
}
