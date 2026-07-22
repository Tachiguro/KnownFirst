using KnownFirst.Core.Preparation;

namespace KnownFirst.Services.Lexical;

public interface ILexicalLookupProvider
{
    string ProviderName { get; }

    int ProviderSchemaVersion { get; }

    string DescribeRequest(LexicalLookupRequest request) => ProviderName;

    Task<LexicalResult> LookupAsync(
        LexicalLookupRequest request,
        CancellationToken cancellationToken = default);
}
