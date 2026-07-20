using KnownFirst.Core.Preparation;

namespace KnownFirst.Services.Lexical;

public sealed class LexicalEnrichmentService(
    AcronymExpansionDetector acronymDetector,
    MeaningRanker meaningRanker,
    ILexicalCacheRepository cache,
    IDictionaryLookupProvider provider,
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
        _diagnosticLog.Write(Event(request, "enrichment.start"));
        var displayedSurfaceForm = request.DisplayedSurfaceForm;
        _diagnosticLog.Write(Event(request, "enrichment.acronym-detection.start"));
        var detectionKind = request.TokenKind == KnownFirst.Core.Text.TokenKind.Word
            && AcronymExpansionDetector.IsAcronymCandidate(displayedSurfaceForm)
                ? KnownFirst.Core.Text.TokenKind.Acronym
                : request.TokenKind;
        var importedExpansion = acronymDetector.FindExpansion(
            originalDocumentContent,
            displayedSurfaceForm,
            detectionKind);
        _diagnosticLog.Write(Event(request, "enrichment.acronym-detection.complete"));

        var originalTerm = displayedSurfaceForm;
        var currentRequest = request;
        _diagnosticLog.Write(Event(request, "enrichment.lemma-normalization.start"));
        var visitedLemmas = new HashSet<string>(StringComparer.Ordinal)
        {
            NormalizeLemma(request.CanonicalLookupTerm)
        };
        _diagnosticLog.Write(Event(request, "enrichment.lemma-normalization.complete"));
        ProviderFormRelation? initialRelation = null;
        var redirectDepth = 0;

        while (true)
        {
            _diagnosticLog.Write(Event(currentRequest, "enrichment.lookup.start"));
            var result = await LookupOneAsync(currentRequest, cancellationToken);
            _diagnosticLog.Write(Event(currentRequest, "enrichment.lookup.complete"));
            if (result.Status != LexicalLookupStatus.Success)
            {
                return ApplyRelationMetadata(result, originalTerm, initialRelation, redirectDepth);
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
                _diagnosticLog.Write(Event(currentRequest, "enrichment.ranking.start"));
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
                _diagnosticLog.Write(Event(currentRequest, "enrichment.ranking.complete"));
                return ApplyRelationMetadata(ranked, originalTerm, initialRelation, redirectDepth);
            }

            var relation = relations.FirstOrDefault();
            if (relation is null)
            {
                return ApplyRelationMetadata(
                    result with
                    {
                        Status = LexicalLookupStatus.NotFound,
                        ErrorCode = "no-suitable-direct-sense"
                    },
                    originalTerm,
                    initialRelation,
                    redirectDepth);
            }

            initialRelation ??= relation;
            if (redirectDepth >= MaximumLemmaRedirectDepth)
            {
                return CreateRedirectFailure(
                    result,
                    originalTerm,
                    initialRelation,
                    redirectDepth,
                    "lemma-redirect-depth-exceeded");
            }

            var normalizedLemma = NormalizeLemma(relation.BaseLemma);
            if (!visitedLemmas.Add(normalizedLemma))
            {
                return CreateRedirectFailure(
                    result,
                    originalTerm,
                    initialRelation,
                    redirectDepth,
                    "lemma-redirect-loop");
            }

            redirectDepth++;
            _diagnosticLog.Write(Event(currentRequest, "enrichment.redirect-request.start"));
            currentRequest = new LexicalLookupRequest(
                request.SourceLanguage,
                request.LookupMode,
                request.TargetLanguage,
                relation.BaseLemma,
                KnownFirst.Core.Text.TokenKind.Word,
                request.Provider,
                request.DisplayedSurfaceForm,
                request.VocabularyCanonicalTerm);
            _diagnosticLog.Write(Event(currentRequest, "enrichment.redirect-request.complete"));
        }
    }

    private async Task<LexicalResult> LookupOneAsync(
        LexicalLookupRequest request,
        CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync(
            request,
            provider.ProviderName,
            provider.ProviderSchemaVersion);
        if (cached is not null)
        {
            return AddDiagnostics(cached, request, "cache-hit");
        }

        try
        {
            var online = await provider.LookupAsync(request, cancellationToken);
            if (online.Status == LexicalLookupStatus.Success)
            {
                await cache.SaveAsync(request, online, provider.ProviderSchemaVersion);
            }

            var outcome = online.ErrorCode is null
                ? online.Status.ToString()
                : $"{online.Status}: {online.ErrorCode}";
            return AddDiagnostics(online, request, outcome);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _diagnosticLog.Write(Event(request, "enrichment.provider-crash"), exception);
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
            return AddDiagnostics(failure, request, "PermanentFailure: provider-crash");
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

    private LexicalDiagnosticEvent Event(
        LexicalLookupRequest request,
        string phase,
        string cacheOutcome = "-") => new(
        phase,
        request.CanonicalLookupTerm,
        request.SourceLanguage,
        request.LookupMode,
        request.TargetLanguage,
        provider.ProviderName,
        CacheOutcome: cacheOutcome);

    private LexicalResult AddDiagnostics(
        LexicalResult result,
        LexicalLookupRequest request,
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
                    provider.ProviderName,
                    provider.ProviderSchemaVersion),
                provider.DescribeRequest(request),
                outcome),
            LookupMode = request.LookupMode,
            TargetLanguage = request.TargetLanguage
        };
}
