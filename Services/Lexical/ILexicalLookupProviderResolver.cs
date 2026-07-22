using KnownFirst.Core.Preparation;

namespace KnownFirst.Services.Lexical;

public interface ILexicalLookupProviderResolver
{
    ILexicalLookupProvider? TryResolve(string providerName);

    ILexicalLookupProvider Resolve(string providerName);
}
