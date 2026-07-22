using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Services.Lexical;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LexicalEnrichmentProviderTests
{
    private sealed class TrackingProvider(string name, int version) : ILexicalLookupProvider
    {
        public string ProviderName { get; } = name;
        public int ProviderSchemaVersion { get; } = version;
        public int InvocationCount { get; private set; }
        public LexicalLookupRequest? LastRequest { get; private set; }
        public LexicalResult? MockResult { get; set; }

        public Task<LexicalResult> LookupAsync(LexicalLookupRequest request, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            LastRequest = request;
            return Task.FromResult(MockResult ?? throw new InvalidOperationException("MockResult not set"));
        }
    }

    [TestMethod]
    public async Task EnrichAsync_UnknownProvider_YieldsPermanentFailure_NotRegistered()
    {
        var registered = new TrackingProvider("Wiktionary", 1);
        var resolver = new LexicalLookupProviderResolver([registered]);
        var cache = new LexicalCacheRepository(new TemporaryKnownFirstDatabase("unknown_provider_db"));
        var service = new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            cache,
            resolver);

        var request = new LexicalLookupRequest(
            "en",
            LexicalLookupMode.Definition,
            null,
            "test",
            TokenKind.Word,
            "Wikipedia"); // Wikipedia is NOT registered

        var result = await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(LexicalLookupStatus.PermanentFailure, result.Status);
        Assert.AreEqual("provider-not-registered", result.ErrorCode);
        Assert.AreEqual("Wikipedia", result.ProviderName);
        Assert.AreEqual(0, registered.InvocationCount);
    }

    [TestMethod]
    public async Task EnrichAsync_UsesResolvedProviderForCacheAndNetwork()
    {
        await using var database = new TemporaryKnownFirstDatabase("provider_cache_db");
        await database.InitializeAsync();
        
        var wikipedia = new TrackingProvider("Wikipedia", 2);
        var request = new LexicalLookupRequest(
            "en",
            LexicalLookupMode.Definition,
            null,
            "test",
            TokenKind.Word,
            "Wikipedia");
            
        wikipedia.MockResult = new LexicalResult(
            LexicalLookupStatus.Success,
            "test",
            "test",
            TokenKind.Word,
            "en",
            "en",
            null,
            [new LexicalMeaning("1", "noun", "A test", null, null, [])],
            "Wikipedia",
            "en.wikipedia.org",
            "test",
            123,
            "Wikipedia contributors",
            DateTime.UtcNow,
            false,
            null,
            null,
            null,
            0,
            [],
            null,
            LexicalLookupMode.Definition,
            null);

        var resolver = new LexicalLookupProviderResolver([wikipedia]);
        var cache = new LexicalCacheRepository(database);
        var service = new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            cache,
            resolver);

        // First call: goes to network, saves to cache
        var result1 = await service.EnrichAsync(request, "test", null);
        Assert.AreEqual(1, wikipedia.InvocationCount);
        Assert.AreEqual("Wikipedia", result1.ProviderName);
        Assert.IsFalse(result1.IsFromCache);
        
        // Second call: comes from cache
        var result2 = await service.EnrichAsync(request, "test", null);
        Assert.AreEqual(1, wikipedia.InvocationCount); // network not called again
        Assert.AreEqual("Wikipedia", result2.ProviderName);
        Assert.IsTrue(result2.IsFromCache);
    }

    [TestMethod]
    public void CacheKeys_AreDifferentForDifferentProviders()
    {
        var requestWiki = new LexicalLookupRequest("en", LexicalLookupMode.Definition, null, "test", TokenKind.Word, "Wikipedia");
        var requestWikt = new LexicalLookupRequest("en", LexicalLookupMode.Definition, null, "test", TokenKind.Word, "Wiktionary");
        
        var keyWiki = LexicalCacheRepository.CreateCacheKey(requestWiki, "Wikipedia", 1);
        var keyWikt = LexicalCacheRepository.CreateCacheKey(requestWikt, "Wiktionary", 1);
        
        Assert.AreNotEqual(keyWiki, keyWikt);
        Assert.IsTrue(keyWiki.Contains("wikipedia", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(keyWikt.Contains("wiktionary", StringComparison.OrdinalIgnoreCase));
    }
    
    [TestMethod]
    public async Task EnrichAsync_LemmaRedirect_KeepsOriginalProvider()
    {
        await using var database = new TemporaryKnownFirstDatabase("redirect_provider_db");
        await database.InitializeAsync();
        
        var wikipedia = new TrackingProvider("Wikipedia", 1);
        var request = new LexicalLookupRequest(
            "en",
            LexicalLookupMode.Definition,
            null,
            "test-alias",
            TokenKind.Word,
            "Wikipedia");
            
        // First request returns a redirect
        var redirectResult = new LexicalResult(
            LexicalLookupStatus.Success,
            "test-alias",
            "test-alias",
            TokenKind.Word,
            "en",
            "en",
            null,
            [],
            "Wikipedia",
            "en.wikipedia.org",
            "test-alias",
            123,
            "Wikipedia",
            DateTime.UtcNow,
            FormRelations: [new ProviderFormRelation((GrammaticalRelationKind)0, "test", "redirect")]);
            
        // Second request returns actual data
        var finalResult = new LexicalResult(
            LexicalLookupStatus.Success,
            "test",
            "test",
            TokenKind.Word,
            "en",
            "en",
            null,
            [new LexicalMeaning("1", "noun", "A test", null, null, [])],
            "Wikipedia",
            "en.wikipedia.org",
            "test",
            123,
            "Wikipedia",
            DateTime.UtcNow);

        var requestCount = 0;
        wikipedia.MockResult = null; // Controlled manually
        var resolver = new LexicalLookupProviderResolver([
            new DelegatingProvider("Wikipedia", 1, req => 
            {
                requestCount++;
                Assert.AreEqual("Wikipedia", req.Provider);
                return Task.FromResult(requestCount == 1 ? redirectResult : finalResult);
            })
        ]);
        
        var cache = new LexicalCacheRepository(database);
        var service = new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            cache,
            resolver);

        var result = await service.EnrichAsync(request, "test-alias", null);
        
        Assert.AreEqual(2, requestCount);
        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.AreEqual("test", result.QueriedLemma);
        Assert.AreEqual("Wikipedia", result.ProviderName);
    }

    private sealed class DelegatingProvider(string name, int version, Func<LexicalLookupRequest, Task<LexicalResult>> func) : ILexicalLookupProvider
    {
        public string ProviderName => name;
        public int ProviderSchemaVersion => version;
        public Task<LexicalResult> LookupAsync(LexicalLookupRequest request, CancellationToken cancellationToken = default) => func(request);
    }
}
