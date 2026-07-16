using KnownFirst.Core.Preparation;

namespace KnownFirst.Services.Lexical;

public interface IDictionaryLookupProvider
{
    string ProviderName { get; }

    int ProviderSchemaVersion { get; }

    Task<LexicalResult> LookupAsync(
        LexicalLookupRequest request,
        CancellationToken cancellationToken = default);
}
