using KnownFirst.Services.Lexical;
using KnownFirst.Services.Lexical.Wikipedia;
using Microsoft.Extensions.DependencyInjection;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LexicalDIRegistrationTests
{
    private IServiceProvider _provider = null!;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();
        // Add required dependencies for Wiktionary and Wikipedia
        services.AddSingleton(new HttpClient());
        services.AddSingleton<WiktionaryHtmlParser>();
        services.AddSingleton<KnownFirst.Core.Learning.IClock, KnownFirst.Core.Learning.SystemClock>();
        services.AddSingleton<KnownFirst.Services.Lexical.IAsyncDelay, KnownFirst.Services.Lexical.SystemAsyncDelay>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<KnownFirst.Services.Lexical.WiktionaryLookupProvider>>(Microsoft.Extensions.Logging.Abstractions.NullLogger<KnownFirst.Services.Lexical.WiktionaryLookupProvider>.Instance);
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<KnownFirst.Services.Lexical.Wikipedia.WikipediaLookupProvider>>(Microsoft.Extensions.Logging.Abstractions.NullLogger<KnownFirst.Services.Lexical.Wikipedia.WikipediaLookupProvider>.Instance);
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<KnownFirst.Services.Lexical.Wikipedia.WikipediaApiClient>>(Microsoft.Extensions.Logging.Abstractions.NullLogger<KnownFirst.Services.Lexical.Wikipedia.WikipediaApiClient>.Instance);
        
        // Add our actual lexical registrations
        services.AddLexicalProviders();
        _provider = services.BuildServiceProvider();
    }

    [TestMethod]
    public void WikipediaApiClient_IsRegisteredConcretely()
    {
        var client = _provider.GetRequiredService<WikipediaApiClient>();
        Assert.IsNotNull(client);
    }

    [TestMethod]
    public void IWikipediaApiClient_ResolvesToSameSingletonInstance()
    {
        var concrete = _provider.GetRequiredService<WikipediaApiClient>();
        var iface = _provider.GetRequiredService<IWikipediaApiClient>();
        
        Assert.AreSame(concrete, iface);
    }

    [TestMethod]
    public void WikipediaLookupProvider_IsRegisteredConcretely()
    {
        var provider = _provider.GetRequiredService<WikipediaLookupProvider>();
        Assert.IsNotNull(provider);
    }

    [TestMethod]
    public void ILexicalLookupProvider_ResolvesWikipediaToSameSingletonInstance()
    {
        var concrete = _provider.GetRequiredService<WikipediaLookupProvider>();
        var providers = _provider.GetServices<ILexicalLookupProvider>().ToList();
        
        var resolved = providers.OfType<WikipediaLookupProvider>().Single();
        Assert.AreSame(concrete, resolved);
    }

    [TestMethod]
    public void IEnumerable_ILexicalLookupProvider_ContainsExactlyOneWikipediaProvider()
    {
        var providers = _provider.GetServices<ILexicalLookupProvider>().ToList();
        
        Assert.AreEqual(1, providers.Count(p => p is WikipediaLookupProvider));
    }

    [TestMethod]
    public void IEnumerable_IDictionaryLookupProvider_DoesNotContainWikipediaProvider()
    {
        var dictionaryProviders = _provider.GetServices<IDictionaryLookupProvider>().ToList();
        
        Assert.IsFalse(dictionaryProviders.Any(p => p is WikipediaLookupProvider));
    }

    [TestMethod]
    public void Wiktionary_RemainsRegisteredUnderAllContracts()
    {
        var concrete = _provider.GetRequiredService<WiktionaryLookupProvider>();
        var dictionaryProviders = _provider.GetServices<IDictionaryLookupProvider>().ToList();
        var lexicalProviders = _provider.GetServices<ILexicalLookupProvider>().ToList();
        
        Assert.AreSame(concrete, dictionaryProviders.OfType<WiktionaryLookupProvider>().Single());
        Assert.AreSame(concrete, lexicalProviders.OfType<WiktionaryLookupProvider>().Single());
    }

    [TestMethod]
    public void Resolver_ResolvesBothProvidersCaseInsensitively()
    {
        var resolver = _provider.GetRequiredService<ILexicalLookupProviderResolver>();
        
        var wiktionary1 = resolver.Resolve("Wiktionary");
        var wiktionary2 = resolver.Resolve("wiktionary");
        
        var wikipedia1 = resolver.Resolve("Wikipedia");
        var wikipedia2 = resolver.Resolve("wikipedia");
        
        Assert.IsInstanceOfType(wiktionary1, typeof(WiktionaryLookupProvider));
        Assert.AreSame(wiktionary1, wiktionary2);
        
        Assert.IsInstanceOfType(wikipedia1, typeof(WikipediaLookupProvider));
        Assert.AreSame(wikipedia1, wikipedia2);
    }

    [TestMethod]
    public void Resolver_ForbidsDuplicateProviderNames()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILexicalLookupProvider>(new FakeProvider("Duplicate"));
        services.AddSingleton<ILexicalLookupProvider>(new FakeProvider("duplicate")); // case-insensitive duplicate

        var provider = services.BuildServiceProvider();
        
        var exception = Assert.ThrowsExactly<ArgumentException>(() => 
            new LexicalLookupProviderResolver(provider.GetServices<ILexicalLookupProvider>()));
            
        Assert.IsTrue(exception.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
    }
    
    private sealed class FakeProvider(string name) : ILexicalLookupProvider
    {
        public string ProviderName => name;
        public int ProviderSchemaVersion => 1;
        public Task<KnownFirst.Core.Preparation.LexicalResult> LookupAsync(KnownFirst.Core.Preparation.LexicalLookupRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
