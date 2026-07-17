using KnownFirst.Core.Text;
using System.Text;

namespace KnownFirst.Core.Preparation;

public enum LexicalLookupMode
{
    Definition = 0,
    Translation = 1,
    DefinitionAndTranslation = 2
}

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

public sealed record LexicalLookupRequest
{
    public LexicalLookupRequest(
        string sourceLanguage,
        LexicalLookupMode lookupMode,
        string? targetLanguage,
        string canonicalLookupTerm,
        TokenKind tokenKind,
        string provider,
        string? displayedSurfaceForm = null,
        string? vocabularyCanonicalTerm = null)
    {
        SourceLanguage = NormalizeLanguage(sourceLanguage);
        LookupMode = lookupMode;
        TargetLanguage = string.IsNullOrWhiteSpace(targetLanguage)
            ? null
            : NormalizeLanguage(targetLanguage);
        TokenKind = tokenKind;
        Provider = string.IsNullOrWhiteSpace(provider)
            ? throw new ArgumentException("A lexical provider is required.", nameof(provider))
            : provider.Trim();
        CanonicalLookupTerm = LexicalLookupTermPolicy.Normalize(
            canonicalLookupTerm,
            SourceLanguage,
            tokenKind);
        DisplayedSurfaceForm = string.IsNullOrWhiteSpace(displayedSurfaceForm)
            ? canonicalLookupTerm.Trim()
            : displayedSurfaceForm.Trim();
        VocabularyCanonicalTerm = string.IsNullOrWhiteSpace(vocabularyCanonicalTerm)
            ? canonicalLookupTerm.Trim()
            : vocabularyCanonicalTerm.Trim();

        LexicalLookupLanguagePolicy.Validate(SourceLanguage, LookupMode, TargetLanguage);
    }

    public LexicalLookupRequest(
        string term,
        string normalizedLemma,
        TokenKind tokenKind,
        string sourceLanguage,
        string explanationLanguage)
        : this(
            sourceLanguage,
            string.Equals(sourceLanguage, explanationLanguage, StringComparison.OrdinalIgnoreCase)
                ? LexicalLookupMode.Definition
                : LexicalLookupMode.DefinitionAndTranslation,
            string.Equals(sourceLanguage, explanationLanguage, StringComparison.OrdinalIgnoreCase)
                ? null
                : explanationLanguage,
            tokenKind == TokenKind.Word ? normalizedLemma : term,
            tokenKind,
            "Wiktionary",
            term,
            term)
    {
    }

    public string SourceLanguage { get; }

    public LexicalLookupMode LookupMode { get; }

    public string? TargetLanguage { get; }

    public string CanonicalLookupTerm { get; }

    public TokenKind TokenKind { get; }

    public string Provider { get; }

    public string DisplayedSurfaceForm { get; }

    public string VocabularyCanonicalTerm { get; }

    public string Term => CanonicalLookupTerm;

    public string NormalizedLemma => CanonicalLookupTerm;

    public string ExplanationLanguage => TargetLanguage ?? SourceLanguage;

    private static string NormalizeLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException("A language code is required.", nameof(language));
        }

        return language.Trim().ToLowerInvariant();
    }
}

public static class LexicalLookupLanguagePolicy
{
    public static void Validate(
        string sourceLanguage,
        LexicalLookupMode lookupMode,
        string? targetLanguage)
    {
        ValidateSupportedLanguage(sourceLanguage, nameof(sourceLanguage));
        if (!Enum.IsDefined(lookupMode))
        {
            throw new ArgumentOutOfRangeException(nameof(lookupMode));
        }

        if (lookupMode == LexicalLookupMode.Definition)
        {
            if (targetLanguage is not null)
            {
                throw new ArgumentException(
                    "Definition lookup must not have a target language.",
                    nameof(targetLanguage));
            }

            return;
        }

        if (targetLanguage is null)
        {
            throw new ArgumentException(
                "Translation lookup requires a target language.",
                nameof(targetLanguage));
        }

        ValidateSupportedLanguage(targetLanguage, nameof(targetLanguage));
        if (string.Equals(sourceLanguage, targetLanguage, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The source and target languages must differ.",
                nameof(targetLanguage));
        }
    }

    private static void ValidateSupportedLanguage(string language, string parameterName)
    {
        if (language is not ("en" or "de"))
        {
            throw new ArgumentException("Only English and German are supported.", parameterName);
        }
    }
}

public static class LexicalLookupTermPolicy
{
    public static string Normalize(string term, string sourceLanguage, TokenKind tokenKind)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            throw new ArgumentException("A canonical lookup term is required.", nameof(term));
        }

        var normalized = term.Trim().Normalize(NormalizationForm.FormC);
        return string.Equals(sourceLanguage, "en", StringComparison.OrdinalIgnoreCase)
            && tokenKind == TokenKind.Word
                ? normalized.ToLowerInvariant()
                : normalized;
    }
}

public sealed record LexicalMeaning(
    string MeaningId,
    string? PartOfSpeech,
    string Definition,
    string? Translation,
    string? Example,
    IReadOnlyList<string> UsageLabels);

public sealed record LexicalLookupDiagnostics(
    string DisplayedSurfaceForm,
    string VocabularyCanonicalTerm,
    string LookupTerm,
    string SourceLanguage,
    LexicalLookupMode LookupMode,
    string? TargetLanguage,
    string CacheKey,
    string ProviderRequest,
    string ProviderOutcome);

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
    int RedirectDepth = 0,
    IReadOnlyList<ProviderFormRelation>? FormRelations = null,
    LexicalLookupDiagnostics? Diagnostics = null,
    LexicalLookupMode? LookupMode = null,
    string? TargetLanguage = null)
{
    public bool HasUsableData => Status == LexicalLookupStatus.Success
        && (!string.IsNullOrWhiteSpace(AcronymExpansion)
            || Meanings.Any(meaning => !string.IsNullOrWhiteSpace(meaning.Definition)
                || !string.IsNullOrWhiteSpace(meaning.Translation)));

    public bool HasReferenceData => Status == LexicalLookupStatus.Success
        && (HasUsableData || FormRelations is { Count: > 0 });
}
