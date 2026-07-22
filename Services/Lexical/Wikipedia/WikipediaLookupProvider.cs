using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace KnownFirst.Services.Lexical.Wikipedia;

public sealed class WikipediaLookupProvider : ILexicalLookupProvider
{
    private readonly IWikipediaApiClient _client;
    private readonly IClock _clock;
    private readonly ILogger<WikipediaLookupProvider>? _logger;

    public const string Name = "Wikipedia";
    public const int SchemaVersion = 1;

    public WikipediaLookupProvider(
        IWikipediaApiClient client,
        IClock clock,
        ILogger<WikipediaLookupProvider>? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger;
    }

    public string ProviderName => Name;
    public int ProviderSchemaVersion => SchemaVersion;

    public string DescribeRequest(LexicalLookupRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return $"Wikipedia Action API; source={request.SourceLanguage}; mode={request.LookupMode}; title-length={request.CanonicalLookupTerm.Length}";
    }

    public async Task<LexicalResult> LookupAsync(LexicalLookupRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (!string.Equals(request.Provider, ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Invalid provider. Expected {ProviderName}.", nameof(request));
        }

        LexicalLookupLanguagePolicy.Validate(request.SourceLanguage, request.LookupMode, request.TargetLanguage);

        if (request.LookupMode == LexicalLookupMode.Translation)
        {
            _logger?.LogInformation("Wikipedia does not support translation-only mode.");
            return CreateEmptyResult(request, LexicalLookupStatus.NotFound, "translation-not-supported");
        }

        var startTime = _clock.UtcNow;
        try
        {
            var targetLanguage = request.LookupMode == LexicalLookupMode.DefinitionAndTranslation
                ? request.TargetLanguage
                : null;

            var articleRequest = new WikipediaArticleRequest(
                request.SourceLanguage,
                request.CanonicalLookupTerm,
                targetLanguage);

            var apiResult = await _client.GetArticleAsync(articleRequest, cancellationToken).ConfigureAwait(false);
            
            var duration = _clock.UtcNow - startTime;
            _logger?.LogInformation(
                "Wikipedia API lookup completed in {Duration}ms. Status: {Status}",
                duration.TotalMilliseconds,
                apiResult.Status);

            return MapResult(request, apiResult);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError("Unexpected error during Wikipedia lookup. ExceptionType: {ExceptionType}", ex.GetType().Name);
            return CreateEmptyResult(request, LexicalLookupStatus.PermanentFailure, "provider-error");
        }
    }

    private LexicalResult MapResult(LexicalLookupRequest request, WikipediaArticleResult apiResult)
    {
        var status = MapStatus(apiResult.Status);
        var errorCode = MapErrorCode(apiResult.Status, apiResult.ErrorCode);

        var meanings = new List<LexicalMeaning>();
        if (status == LexicalLookupStatus.Success && !string.IsNullOrWhiteSpace(apiResult.Extract))
        {
            var meaningId = GenerateMeaningId(apiResult);
            meanings.Add(new LexicalMeaning(
                MeaningId: meaningId,
                PartOfSpeech: null,
                Definition: apiResult.Extract,
                Translation: null,
                Example: null,
                UsageLabels: []));
        }
        else if (status == LexicalLookupStatus.Success)
        {
            // Edge case rule: niemals Success ohne verwendbaren Extract
            status = LexicalLookupStatus.NotFound;
            errorCode = "no-usable-content";
        }

        return new LexicalResult(
            Status: status,
            QueriedLemma: request.CanonicalLookupTerm,
            DisplayTerm: request.DisplayedSurfaceForm,
            TokenKind: request.TokenKind,
            SourceLanguage: request.SourceLanguage,
            ExplanationLanguage: request.ExplanationLanguage,
            AcronymExpansion: null,
            Meanings: meanings,
            ProviderName: ProviderName,
            SourceProject: apiResult.SourceProject,
            PageTitle: apiResult.CanonicalTitle,
            RevisionId: apiResult.RevisionId > 0 ? apiResult.RevisionId : null,
            Attribution: apiResult.Attribution,
            LookupAtUtc: _clock.UtcNow,
            IsFromCache: false,
            ErrorCode: errorCode,
            EncounteredSurfaceForm: null,
            GrammaticalRelationship: null,
            RedirectDepth: 0,
            FormRelations: null,
            Diagnostics: null,
            LookupMode: request.LookupMode,
            TargetLanguage: request.TargetLanguage
        );
    }

    private LexicalResult CreateEmptyResult(LexicalLookupRequest request, LexicalLookupStatus status, string errorCode)
    {
        return new LexicalResult(
            Status: status,
            QueriedLemma: request.CanonicalLookupTerm,
            DisplayTerm: request.DisplayedSurfaceForm,
            TokenKind: request.TokenKind,
            SourceLanguage: request.SourceLanguage,
            ExplanationLanguage: request.ExplanationLanguage,
            AcronymExpansion: null,
            Meanings: [],
            ProviderName: ProviderName,
            SourceProject: string.Empty,
            PageTitle: string.Empty,
            RevisionId: null,
            Attribution: string.Empty,
            LookupAtUtc: _clock.UtcNow,
            IsFromCache: false,
            ErrorCode: errorCode,
            EncounteredSurfaceForm: null,
            GrammaticalRelationship: null,
            RedirectDepth: 0,
            FormRelations: null,
            Diagnostics: null,
            LookupMode: request.LookupMode,
            TargetLanguage: request.TargetLanguage
        );
    }

    private static LexicalLookupStatus MapStatus(WikipediaArticleStatus status) => status switch
    {
        WikipediaArticleStatus.Success => LexicalLookupStatus.Success,
        WikipediaArticleStatus.NotFound => LexicalLookupStatus.NotFound,
        WikipediaArticleStatus.Disambiguation => LexicalLookupStatus.NotFound,
        WikipediaArticleStatus.NoUsableContent => LexicalLookupStatus.NotFound,
        WikipediaArticleStatus.RateLimited => LexicalLookupStatus.TransientFailure,
        WikipediaArticleStatus.TimedOut => LexicalLookupStatus.TransientFailure,
        WikipediaArticleStatus.TransientFailure => LexicalLookupStatus.TransientFailure,
        WikipediaArticleStatus.PermanentFailure => LexicalLookupStatus.PermanentFailure,
        WikipediaArticleStatus.ParseFailure => LexicalLookupStatus.ParseFailure,
        _ => LexicalLookupStatus.PermanentFailure
    };

    private static string? MapErrorCode(WikipediaArticleStatus status, string? existingErrorCode)
    {
        if (status == WikipediaArticleStatus.Success)
        {
            return null;
        }

        if (status == WikipediaArticleStatus.Disambiguation)
        {
            return "disambiguation";
        }
        
        if (status == WikipediaArticleStatus.RateLimited)
        {
            return "rate-limited";
        }
        
        if (status == WikipediaArticleStatus.TimedOut)
        {
            return "timeout";
        }

        if (!string.IsNullOrWhiteSpace(existingErrorCode))
        {
            return existingErrorCode;
        }

        return status switch
        {
            WikipediaArticleStatus.NotFound => "wikipedia-not-found",
            WikipediaArticleStatus.NoUsableContent => "no-usable-content",
            WikipediaArticleStatus.TransientFailure => "wikipedia-transient-failure",
            WikipediaArticleStatus.PermanentFailure => "wikipedia-permanent-failure",
            WikipediaArticleStatus.ParseFailure => "wikipedia-parse-failure",
            _ => "wikipedia-unknown-error"
        };
    }

    private static string GenerateMeaningId(WikipediaArticleResult result)
    {
        if (result.PageId > 0)
        {
            return $"wp_{result.SourceProject}_{result.PageId}";
        }

        var hashInput = $"{result.SourceProject}_{result.CanonicalTitle}";
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(hashInput));
        return $"wp_{Convert.ToHexString(hashBytes).ToLowerInvariant()[..16]}";
    }
}
