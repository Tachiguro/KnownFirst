using KnownFirst.Core.Preparation;

namespace KnownFirst.Services.Lexical;

public interface ILexicalEnrichmentService
{
    Task<LexicalResult> EnrichAsync(
        LexicalLookupRequest request,
        string originalDocumentContent,
        string? representativeContext,
        CancellationToken cancellationToken = default);
}
