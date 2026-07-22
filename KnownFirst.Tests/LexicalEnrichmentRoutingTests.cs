using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Services.Lexical;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LexicalEnrichmentRoutingTests
{
    private sealed class TrackingProvider(string name, LexicalLookupStatus mockStatus) : ILexicalLookupProvider
    {
        public string ProviderName { get; } = name;
        public int ProviderSchemaVersion { get; } = 1;
        public int InvocationCount { get; private set; }
        public bool ThrowCancellation { get; set; }

        public Task<LexicalResult> LookupAsync(LexicalLookupRequest request, CancellationToken cancellationToken = default)
        {
            if (ThrowCancellation)
            {
                throw new OperationCanceledException();
            }

            InvocationCount++;
            return Task.FromResult(new LexicalResult(
                Status: mockStatus,
                QueriedLemma: request.CanonicalLookupTerm,
                DisplayTerm: request.DisplayedSurfaceForm,
                TokenKind: request.TokenKind,
                SourceLanguage: request.SourceLanguage,
                ExplanationLanguage: request.ExplanationLanguage,
                AcronymExpansion: null,
                Meanings: [],
                ProviderName: ProviderName,
                SourceProject: "",
                PageTitle: "",
                RevisionId: null,
                Attribution: "",
                LookupAtUtc: DateTime.UtcNow,
                IsFromCache: false,
                ErrorCode: "mock-error",
                LookupMode: request.LookupMode,
                TargetLanguage: request.TargetLanguage
            ));
        }
    }

    [TestMethod]
    public async Task RequestProvider_Wikipedia_CallsOnlyWikipedia()
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.Success);
        var wiktionary = new TrackingProvider("Wiktionary", LexicalLookupStatus.Success);
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wikipedia");
        await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(1, wikipedia.InvocationCount);
        Assert.AreEqual(0, wiktionary.InvocationCount);
    }

    [TestMethod]
    public async Task RequestProvider_Wiktionary_CallsOnlyWiktionary()
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.Success);
        var wiktionary = new TrackingProvider("Wiktionary", LexicalLookupStatus.Success);
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wiktionary");
        await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(0, wikipedia.InvocationCount);
        Assert.AreEqual(1, wiktionary.InvocationCount);
    }

    [TestMethod]
    public async Task Wiktionary_NotFound_DoesNotFallbackToWikipedia()
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.Success);
        var wiktionary = new TrackingProvider("Wiktionary", LexicalLookupStatus.NotFound);
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wiktionary");
        var result = await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(LexicalLookupStatus.NotFound, result.Status);
        Assert.AreEqual(1, wiktionary.InvocationCount);
        Assert.AreEqual(0, wikipedia.InvocationCount);
    }

    [TestMethod]
    public async Task Wiktionary_TransientFailure_DoesNotFallbackToWikipedia()
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.Success);
        var wiktionary = new TrackingProvider("Wiktionary", LexicalLookupStatus.TransientFailure);
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wiktionary");
        var result = await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(LexicalLookupStatus.TransientFailure, result.Status);
        Assert.AreEqual(1, wiktionary.InvocationCount);
        Assert.AreEqual(0, wikipedia.InvocationCount);
    }

    [TestMethod]
    public async Task Wiktionary_PermanentFailure_DoesNotFallbackToWikipedia()
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.Success);
        var wiktionary = new TrackingProvider("Wiktionary", LexicalLookupStatus.PermanentFailure);
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wiktionary");
        var result = await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(LexicalLookupStatus.PermanentFailure, result.Status);
        Assert.AreEqual(1, wiktionary.InvocationCount);
        Assert.AreEqual(0, wikipedia.InvocationCount);
    }

    [TestMethod]
    public async Task Wiktionary_ParseFailure_DoesNotFallbackToWikipedia()
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.Success);
        var wiktionary = new TrackingProvider("Wiktionary", LexicalLookupStatus.ParseFailure);
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wiktionary");
        var result = await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(LexicalLookupStatus.ParseFailure, result.Status);
        Assert.AreEqual(1, wiktionary.InvocationCount);
        Assert.AreEqual(0, wikipedia.InvocationCount);
    }

    [TestMethod]
    public async Task CallerCancellation_DoesNotTriggerFallback()
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.Success);
        var wiktionary = new TrackingProvider("Wiktionary", LexicalLookupStatus.Success) { ThrowCancellation = true };
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wiktionary");
        
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.EnrichAsync(request, "test", null, new CancellationToken(true)));
        
        Assert.AreEqual(0, wikipedia.InvocationCount);
    }

    private static LexicalEnrichmentService CreateService(ILexicalLookupProvider[] providers)
    {
        var resolver = new LexicalLookupProviderResolver(providers);
        var cache = new LexicalCacheRepository(new TemporaryKnownFirstDatabase(Guid.NewGuid().ToString("N")));
        return new LexicalEnrichmentService(new AcronymExpansionDetector(), new MeaningRanker(), cache, resolver);
    }

    private static LexicalLookupRequest CreateRequest(string provider)
    {
        return new LexicalLookupRequest("en", LexicalLookupMode.Definition, null, "test", TokenKind.Word, provider);
    }
}
