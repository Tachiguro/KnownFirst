using KnownFirst.Core.Preparation;

namespace KnownFirst.Services.Lexical;

public sealed class LexicalEnrichmentService(
    AcronymExpansionDetector acronymDetector,
    MeaningRanker meaningRanker,
    ILexicalCacheRepository cache,
    ILexicalLookupProviderResolver providerResolver,
    ILexicalDiagnosticLog? diagnosticLog = null) : ILexicalEnrichmentService
{
    public const int MaximumLemmaRedirectDepth = 3;
    private readonly ILexicalDiagnosticLog _diagnosticLog =
        diagnosticLog ?? NullLexicalDiagnosticLog.Instance;

    public async Task<LexicalResult> EnrichAsync(
        LexicalLookupRequest request,
        string originalDocumentContent,
        string? representativeContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var primaryExecution = await ExecuteSingleProviderAsync(request, originalDocumentContent, representativeContext, cancellationToken);
        var primaryResult = primaryExecution.Result;

        if (primaryResult.Status == LexicalLookupStatus.NotFound
            && KnownFirst.Services.Lexical.Wikipedia.WikipediaFallbackPolicy.IsEligibleForFallback(request, primaryResult))
        {
            _diagnosticLog.Write(Event(primaryExecution.FinalRequest, "Wikipedia", "enrichment.fallback.start"));
            var fallbackRequest = KnownFirst.Services.Lexical.Wikipedia.WikipediaFallbackPolicy.CreateFallbackRequest(primaryExecution.FinalRequest);
            var fallbackExecution = await ExecuteSingleProviderAsync(fallbackRequest, originalDocumentContent, representativeContext, cancellationToken);
            _diagnosticLog.Write(Event(primaryExecution.FinalRequest, "Wikipedia", "enrichment.fallback.complete"));

            var fallbackResult = fallbackExecution.Result;
            if (primaryResult.EncounteredSurfaceForm is not null)
            {
                fallbackResult = fallbackResult with
                {
                    EncounteredSurfaceForm = primaryResult.EncounteredSurfaceForm,
                    GrammaticalRelationship = primaryResult.GrammaticalRelationship,
                    RedirectDepth = primaryResult.RedirectDepth
                };
            }

            return fallbackResult;
        }

        return primaryResult;
    }

    private readonly record struct SingleProviderExecution(LexicalResult Result, LexicalLookupRequest FinalRequest);

    private async Task<SingleProviderExecution> ExecuteSingleProviderAsync(
        LexicalLookupRequest request,
        string originalDocumentContent,
        string? representativeContext,
        CancellationToken cancellationToken)
    {
        _diagnosticLog.Write(Event(request, request.Provider, "enrichment.start"));
        var displayedSurfaceForm = request.DisplayedSurfaceForm;
        _diagnosticLog.Write(Event(request, request.Provider, "enrichment.acronym-detection.start"));
        var detectionKind = request.TokenKind == KnownFirst.Core.Text.TokenKind.Word
            && AcronymExpansionDetector.IsAcronymCandidate(displayedSurfaceForm)
                ? KnownFirst.Core.Text.TokenKind.Acronym
                : request.TokenKind;
        var importedExpansion = acronymDetector.FindExpansion(
            originalDocumentContent,
            displayedSurfaceForm,
            detectionKind);
        _diagnosticLog.Write(Event(request, request.Provider, "enrichment.acronym-detection.complete"));

        var originalTerm = displayedSurfaceForm;
        var currentRequest = request;
        _diagnosticLog.Write(Event(request, request.Provider, "enrichment.lemma-normalization.start"));
        var visitedLemmas = new HashSet<string>(StringComparer.Ordinal)
        {
            NormalizeLemma(request.CanonicalLookupTerm)
        };
        _diagnosticLog.Write(Event(request, request.Provider, "enrichment.lemma-normalization.complete"));
        ProviderFormRelation? initialRelation = null;
        var redirectDepth = 0;

        while (true)
        {
            _diagnosticLog.Write(Event(currentRequest, currentRequest.Provider, "enrichment.lookup.start"));
            var result = await LookupOneAsync(currentRequest, cancellationToken);
            _diagnosticLog.Write(Event(currentRequest, currentRequest.Provider, "enrichment.lookup.complete"));

            if (result.Status != LexicalLookupStatus.Success)
            {
                return new(ApplyRelationMetadata(result, originalTerm, initialRelation, redirectDepth), currentRequest);
            }

            var directMeanings = result.Meanings
                .Where(meaning => ProviderFormRelationPolicy.Resolve(meaning.Definition) is null)
                .ToArray();
            var relations = (result.FormRelations ?? [])
                .Concat(result.Meanings
                    .Select(meaning => ProviderFormRelationPolicy.Resolve(meaning.Definition))
                    .Where(relation => relation is not null)
                    .Cast<ProviderFormRelation>())
                .Distinct()
                .ToArray();
            if (directMeanings.Length > 0)
            {
                _diagnosticLog.Write(Event(currentRequest, currentRequest.Provider, "enrichment.ranking.start"));
                var ranked = RankAndApplyExpansion(
                    result with
                    {
                        QueriedLemma = currentRequest.CanonicalLookupTerm,
                        DisplayTerm = currentRequest.CanonicalLookupTerm,
                        Meanings = directMeanings,
                        FormRelations = relations
                    },
                    importedExpansion,
                    representativeContext);
                _diagnosticLog.Write(Event(currentRequest, currentRequest.Provider, "enrichment.ranking.complete"));
                return new(ApplyRelationMetadata(ranked, originalTerm, initialRelation, redirectDepth), currentRequest);
            }

            var relation = relations.FirstOrDefault();
            if (relation is null)
            {
                return new(ApplyRelationMetadata(
                    result with
                    {
                        Status = LexicalLookupStatus.NotFound,
                        ErrorCode = "no-suitable-direct-sense"
                    },
                    originalTerm,
                    initialRelation,
                    redirectDepth), currentRequest);
            }

            initialRelation ??= relation;
            if (redirectDepth >= MaximumLemmaRedirectDepth)
            {
                return new(CreateRedirectFailure(
                    result,
                    originalTerm,
                    initialRelation,
                    redirectDepth,
                    "lemma-redirect-depth-exceeded"), currentRequest);
            }

            var normalizedLemma = NormalizeLemma(relation.BaseLemma);
            if (!visitedLemmas.Add(normalizedLemma))
            {
                return new(CreateRedirectFailure(
                    result,
                    originalTerm,
                    initialRelation,
                    redirectDepth,
                    "lemma-redirect-loop"), currentRequest);
            }

            redirectDepth++;
            _diagnosticLog.Write(Event(currentRequest, currentRequest.Provider, "enrichment.redirect-request.start"));
            currentRequest = new LexicalLookupRequest(
                request.SourceLanguage,
                request.LookupMode,
                request.TargetLanguage,
                relation.BaseLemma,
                KnownFirst.Core.Text.TokenKind.Word,
                request.Provider,
                request.DisplayedSurfaceForm,
                request.VocabularyCanonicalTerm);
            _diagnosticLog.Write(Event(currentRequest, currentRequest.Provider, "enrichment.redirect-request.complete"));
        }
    }

    private async Task<LexicalResult> LookupOneAsync(
        LexicalLookupRequest request,
        CancellationToken cancellationToken)
    {
        var provider = providerResolver.TryResolve(request.Provider);
        if (provider is null)
        {
            _diagnosticLog.Write(Event(request, request.Provider, "enrichment.provider-not-registered"));
            var failure = new LexicalResult(
                LexicalLookupStatus.PermanentFailure,
                request.NormalizedLemma,
                request.Term,
                request.TokenKind,
                request.SourceLanguage,
                request.ExplanationLanguage,
                null,
                [],
                request.Provider,
                "unknown",
                request.Term,
                0,
                string.Empty,
                DateTime.UtcNow)
            {
                ErrorCode = "provider-not-registered"
            };
            return AddDiagnostics(failure, request, provider, "PermanentFailure: provider-not-registered");
        }

        LexicalResult? cached = null;
        try
        {
            cached = await cache.GetAsync(
                request,
                provider.ProviderName,
                provider.ProviderSchemaVersion);
        }
        catch (Exception exception)
        {
            _diagnosticLog.Write(Event(request, provider.ProviderName, "enrichment.cache-read-failed", "read-failed"), exception);
        }

        if (cached is not null)
        {
            return AddDiagnostics(cached, request, provider, "cache-hit");
        }

        try
        {
            var online = await provider.LookupAsync(request, cancellationToken);
            if (!string.Equals(online.ProviderName, provider.ProviderName, StringComparison.OrdinalIgnoreCase))
            {
                _diagnosticLog.Write(Event(request, provider.ProviderName, "enrichment.provider-identity-mismatch"));
                var failure = new LexicalResult(
                    LexicalLookupStatus.PermanentFailure,
                    request.NormalizedLemma,
                    request.Term,
                    request.TokenKind,
                    request.SourceLanguage,
                    request.ExplanationLanguage,
                    null,
                    [],
                    provider.ProviderName,
                    "unknown",
                    request.Term,
                    0,
                    string.Empty,
                    DateTime.UtcNow)
                {
                    ErrorCode = "provider-identity-mismatch"
                };
                return AddDiagnostics(failure, request, provider, "PermanentFailure: provider-identity-mismatch");
            }

            online = LexicalResultInvariantPolicy.Enforce(
                request,
                online);
            if (online.Status == LexicalLookupStatus.Success)
            {
                try
                {
                    await cache.SaveAsync(request, online, provider.ProviderSchemaVersion);
                }
                catch (Exception exception)
                {
                    _diagnosticLog.Write(
                        Event(request, provider.ProviderName, "enrichment.cache-write-failed", "write-failed"),
                        exception);
                }
            }

            var outcome = online.ErrorCode is null
                ? online.Status.ToString()
                : $"{online.Status}: {online.ErrorCode}";
            return AddDiagnostics(online, request, provider, outcome);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _diagnosticLog.Write(Event(request, provider.ProviderName, "enrichment.provider-crash"), exception);
            var failure = new LexicalResult(
                LexicalLookupStatus.PermanentFailure,
                request.NormalizedLemma,
                request.Term,
                request.TokenKind,
                request.SourceLanguage,
                request.ExplanationLanguage,
                null,
                [],
                provider.ProviderName,
                "unknown",
                request.Term,
                0,
                string.Empty,
                DateTime.UtcNow)
            {
                ErrorCode = "provider-crash"
            };
            return AddDiagnostics(failure, request, provider, "PermanentFailure: provider-crash");
        }
    }

    private LexicalResult RankAndApplyExpansion(
        LexicalResult result,
        string? importedExpansion,
        string? context)
    {
        var expansion = importedExpansion ?? result.AcronymExpansion;
        var rankingTokenKind = !string.IsNullOrWhiteSpace(expansion)
            && AcronymExpansionDetector.IsAcronymCandidate(result.DisplayTerm)
                ? KnownFirst.Core.Text.TokenKind.Acronym
                : result.TokenKind;
        return result with
        {
            AcronymExpansion = expansion,
            Meanings = meaningRanker.Rank(result.Meanings, rankingTokenKind, context)
        };
    }

    private static LexicalResult ApplyRelationMetadata(
        LexicalResult result,
        string originalTerm,
        ProviderFormRelation? relation,
        int redirectDepth) => relation is null
        ? result
        : result with
        {
            EncounteredSurfaceForm = originalTerm,
            GrammaticalRelationship = $"{relation.Relationship} {relation.BaseLemma}",
            RedirectDepth = redirectDepth
        };

    private static LexicalResult CreateRedirectFailure(
        LexicalResult result,
        string originalTerm,
        ProviderFormRelation relation,
        int redirectDepth,
        string errorCode) => ApplyRelationMetadata(
            result with
            {
                Status = LexicalLookupStatus.PermanentFailure,
                Meanings = [],
                ErrorCode = errorCode
            },
            originalTerm,
            relation,
            redirectDepth);

    private static string NormalizeLemma(string value) =>
        value.Trim().Normalize(System.Text.NormalizationForm.FormC).ToLowerInvariant();

    private static LexicalDiagnosticEvent Event(
        LexicalLookupRequest request,
        string providerName,
        string phase,
        string cacheOutcome = "-") => new(
        phase,
        request.CanonicalLookupTerm,
        request.SourceLanguage,
        request.LookupMode,
        request.TargetLanguage,
        providerName,
        CacheOutcome: cacheOutcome);

    private static LexicalResult AddDiagnostics(
        LexicalResult result,
        LexicalLookupRequest request,
        ILexicalLookupProvider? provider,
        string outcome) => result with
        {
            Diagnostics = new LexicalLookupDiagnostics(
                request.DisplayedSurfaceForm,
                request.VocabularyCanonicalTerm,
                request.CanonicalLookupTerm,
                request.SourceLanguage,
                request.LookupMode,
                request.TargetLanguage,
                LexicalCacheRepository.CreateCacheKey(
                    request,
                    provider?.ProviderName ?? request.Provider,
                    provider?.ProviderSchemaVersion ?? 0),
                provider?.DescribeRequest(request) ?? "unresolved-provider",
                outcome),
            LookupMode = request.LookupMode,
            TargetLanguage = request.TargetLanguage
        };
}
