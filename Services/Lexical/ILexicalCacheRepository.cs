using KnownFirst.Core.Preparation;

namespace KnownFirst.Services.Lexical;

public interface ILexicalCacheRepository
{
    Task<LexicalResult?> GetAsync(
        LexicalLookupRequest request,
        string provider,
        int providerSchemaVersion);

    Task SaveAsync(
        LexicalLookupRequest request,
        LexicalResult result,
        int providerSchemaVersion);
}
