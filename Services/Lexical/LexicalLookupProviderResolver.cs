using KnownFirst.Core.Preparation;

namespace KnownFirst.Services.Lexical;

public sealed class LexicalLookupProviderResolver : ILexicalLookupProviderResolver
{
    private readonly Dictionary<string, ILexicalLookupProvider> _providers;

    public LexicalLookupProviderResolver(IEnumerable<ILexicalLookupProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = new Dictionary<string, ILexicalLookupProvider>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in providers)
        {
            var name = provider.ProviderName;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A lexical provider must have a valid non-empty name.");
            }

            if (!_providers.TryAdd(name, provider))
            {
                throw new ArgumentException($"A lexical provider with the name '{name}' is already registered.");
            }
        }
    }

    public ILexicalLookupProvider? TryResolve(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return null;
        }

        return _providers.TryGetValue(providerName, out var provider) ? provider : null;
    }

    public ILexicalLookupProvider Resolve(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException("The requested provider name cannot be empty.", nameof(providerName));
        }

        if (!_providers.TryGetValue(providerName, out var provider))
        {
            throw new InvalidOperationException($"The lexical provider '{providerName}' is not registered.");
        }

        return provider;
    }
}
