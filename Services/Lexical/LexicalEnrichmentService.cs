using KnownFirst.Core.Preparation;

namespace KnownFirst.Services.Lexical;

public sealed class LexicalEnrichmentService(
    AcronymExpansionDetector acronymDetector,
    MeaningRanker meaningRanker,
    ILexicalCacheRepository cache,
    IDictionaryLookupProvider provider) : ILexicalEnrichmentService
{
    public const int MaximumLemmaRedirectDepth = 3;

    public async Task<LexicalResult> EnrichAsync(
        LexicalLookupRequest request,
        string originalDocumentContent,
        string? representativeContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var displayedSurfaceForm = request.DisplayedSurfaceForm;
        var detectionKind = request.TokenKind == KnownFirst.Core.Text.TokenKind.Word
            && AcronymExpansionDetector.IsAcronymCandidate(displayedSurfaceForm)
                ? KnownFirst.Core.Text.TokenKind.Acronym
                : request.TokenKind;
        var importedExpansion = acronymDetector.FindExpansion(
            originalDocumentContent,
            displayedSurfaceForm,
            detectionKind);

        var originalTerm = displayedSurfaceForm;
        var currentRequest = request;
        var visitedLemmas = new HashSet<string>(StringComparer.Ordinal)
        {
            NormalizeLemma(request.CanonicalLookupTerm)
        };
        ProviderFormRelation? initialRelation = null;
        var redirectDepth = 0;

        while (true)
        {
            var result = await LookupOneAsync(currentRequest, cancellationToken);
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
            currentRequest = new LexicalLookupRequest(
                request.SourceLanguage,
                request.LookupMode,
                request.TargetLanguage,
                relation.BaseLemma,
                KnownFirst.Core.Text.TokenKind.Word,
                request.Provider,
                request.DisplayedSurfaceForm,
                request.VocabularyCanonicalTerm);
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
