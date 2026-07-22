using Microsoft.Extensions.DependencyInjection;
using KnownFirst.Services.Lexical.Wikipedia;

namespace KnownFirst.Services.Lexical;

public static class LexicalServiceCollectionExtensions
{
    public static IServiceCollection AddLexicalProviders(this IServiceCollection services)
    {
        services.AddSingleton<WiktionaryLookupProvider>();
        services.AddSingleton<IDictionaryLookupProvider>(provider => provider.GetRequiredService<WiktionaryLookupProvider>());
        services.AddSingleton<ILexicalLookupProvider>(provider => provider.GetRequiredService<WiktionaryLookupProvider>());
        
        services.AddSingleton<WikipediaApiClient>();
        services.AddSingleton<IWikipediaApiClient>(provider => provider.GetRequiredService<WikipediaApiClient>());
        services.AddSingleton<WikipediaLookupProvider>();
        services.AddSingleton<ILexicalLookupProvider>(provider => provider.GetRequiredService<WikipediaLookupProvider>());
        
        services.AddSingleton<ILexicalLookupProviderResolver, LexicalLookupProviderResolver>();
        
        return services;
    }
}
