using KnownFirst.Core.Preparation;

namespace KnownFirst.Services.Lexical;

public sealed class LexicalEnrichmentService(
    AcronymExpansionDetector acronymDetector,
    MeaningRanker meaningRanker,
    ILexicalCacheRepository cache,
    IDictionaryLookupProvider provider) : ILexicalEnrichmentService
{
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
        var cached = await cache.GetAsync(
            request,
            provider.ProviderName,
            provider.ProviderSchemaVersion);
        if (cached is not null)
        {
            return RankAndApplyExpansion(cached, importedExpansion, representativeContext);
        }

        var online = await provider.LookupAsync(request, cancellationToken);
        if (online.Status == LexicalLookupStatus.Success)
        {
            await cache.SaveAsync(request, online, provider.ProviderSchemaVersion);
        }

        return RankAndApplyExpansion(online, importedExpansion, representativeContext);
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
}
