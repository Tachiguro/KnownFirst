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
        var detectionKind = request.TokenKind == KnownFirst.Core.Text.TokenKind.Word
            && AcronymExpansionDetector.IsAcronymCandidate(request.Term)
                ? KnownFirst.Core.Text.TokenKind.Acronym
                : request.TokenKind;
        var importedExpansion = acronymDetector.FindExpansion(
            originalDocumentContent,
            request.Term,
            detectionKind);

        var originalTerm = request.Term;
        var currentRequest = request;
        var visitedLemmas = new HashSet<string>(StringComparer.Ordinal)
        {
            NormalizeLemma(request.NormalizedLemma)
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

            var relation = ProviderFormRelationPolicy.Resolve(result.Meanings);
            if (relation is null)
            {
                var ranked = RankAndApplyExpansion(
                    result with
                    {
                        QueriedLemma = currentRequest.NormalizedLemma,
                        DisplayTerm = currentRequest.Term
                    },
                    importedExpansion,
                    representativeContext);
                return ApplyRelationMetadata(ranked, originalTerm, initialRelation, redirectDepth);
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
                relation.BaseLemma,
                normalizedLemma,
                KnownFirst.Core.Text.TokenKind.Word,
                request.SourceLanguage,
                request.ExplanationLanguage);
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
            return cached;
        }

        var online = await provider.LookupAsync(request, cancellationToken);
        if (online.Status == LexicalLookupStatus.Success)
        {
            await cache.SaveAsync(request, online, provider.ProviderSchemaVersion);
        }

        return online;
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
}
