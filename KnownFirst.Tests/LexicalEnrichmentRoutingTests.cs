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
        public LexicalResult? ReturnResult { get; set; }
        public List<LexicalLookupRequest> ReceivedRequests { get; } = [];

        public Task<LexicalResult> LookupAsync(LexicalLookupRequest request, CancellationToken cancellationToken = default)
        {
            if (ThrowCancellation)
            {
                throw new OperationCanceledException();
            }

            InvocationCount++;
            ReceivedRequests.Add(request);
            
            if (ReturnResult != null) return Task.FromResult(ReturnResult);

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
                ErrorCode: mockStatus != LexicalLookupStatus.Success ? "mock-error" : null,
                LookupMode: request.LookupMode,
                TargetLanguage: request.TargetLanguage
            ));
        }
    }

    private sealed class RedirectingThenNotFoundProvider : ILexicalLookupProvider
    {
        public string ProviderName => "Wiktionary";
        public int ProviderSchemaVersion => 1;
        public int InvocationCount { get; private set; }
        public List<LexicalLookupRequest> ReceivedRequests { get; } = [];

        public Task<LexicalResult> LookupAsync(LexicalLookupRequest request, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            ReceivedRequests.Add(request);

            if (InvocationCount == 1)
            {
                return Task.FromResult(new LexicalResult(
                    Status: LexicalLookupStatus.Success,
                    QueriedLemma: request.CanonicalLookupTerm,
                    DisplayTerm: request.DisplayedSurfaceForm,
                    TokenKind: request.TokenKind,
                    SourceLanguage: request.SourceLanguage,
                    ExplanationLanguage: request.ExplanationLanguage,
                    AcronymExpansion: null,
                    Meanings: [new LexicalMeaning("1", null, "plural of redirected", null, null, [])],
                    ProviderName: ProviderName,
                    SourceProject: "",
                    PageTitle: "",
                    RevisionId: null,
                    Attribution: "",
                    LookupAtUtc: DateTime.UtcNow,
                    IsFromCache: false,
                    ErrorCode: null,
                    LookupMode: request.LookupMode,
                    TargetLanguage: request.TargetLanguage
                ));
            }
            
            return Task.FromResult(new LexicalResult(
                Status: LexicalLookupStatus.NotFound,
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
                ErrorCode: "not-found",
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
    public async Task RequestProvider_Wiktionary_Success_CallsOnlyWiktionary()
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.Success);
        var wiktionary = new TrackingProvider("Wiktionary", LexicalLookupStatus.Success);
        wiktionary.ReturnResult = CreateValidResult("Wiktionary", LexicalLookupStatus.Success);
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wiktionary");
        await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(0, wikipedia.InvocationCount);
        Assert.AreEqual(1, wiktionary.InvocationCount);
    }

    [TestMethod]
    public async Task Wiktionary_NotFound_Definition_CallsWikipedia()
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.Success);
        wikipedia.ReturnResult = CreateValidResult("Wikipedia", LexicalLookupStatus.Success);
        var wiktionary = new TrackingProvider("Wiktionary", LexicalLookupStatus.NotFound);
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wiktionary", LexicalLookupMode.Definition, null);
        var result = await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.AreEqual(1, wiktionary.InvocationCount);
        Assert.AreEqual(1, wikipedia.InvocationCount);
        Assert.AreEqual("Wikipedia", result.ProviderName);
    }

    [TestMethod]
    public async Task Wiktionary_NotFound_DefinitionAndTranslation_CallsWikipedia()
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.Success);
        wikipedia.ReturnResult = CreateValidResult("Wikipedia", LexicalLookupStatus.Success, "de", LexicalLookupMode.DefinitionAndTranslation);
        var wiktionary = new TrackingProvider("Wiktionary", LexicalLookupStatus.NotFound);
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wiktionary", LexicalLookupMode.DefinitionAndTranslation, "de");
        var result = await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.AreEqual(1, wiktionary.InvocationCount);
        Assert.AreEqual(1, wikipedia.InvocationCount);
    }

    [TestMethod]
    public async Task Wikipedia_DefinitionAndTranslation_WithDefinitionOnly_RemainsSuccess()
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.Success);
        wikipedia.ReturnResult = CreateValidResult("Wikipedia", LexicalLookupStatus.Success, "de", LexicalLookupMode.DefinitionAndTranslation, false);
        var wiktionary = new TrackingProvider("Wiktionary", LexicalLookupStatus.NotFound);
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wiktionary", LexicalLookupMode.DefinitionAndTranslation, "de");
        var result = await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
    }

    [TestMethod]
    public async Task Wiktionary_NotFound_Translation_DoesNotCallWikipedia()
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.Success);
        var wiktionary = new TrackingProvider("Wiktionary", LexicalLookupStatus.NotFound);
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wiktionary", LexicalLookupMode.Translation, "de");
        var result = await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(LexicalLookupStatus.NotFound, result.Status);
        Assert.AreEqual(1, wiktionary.InvocationCount);
        Assert.AreEqual(0, wikipedia.InvocationCount);
    }

    [DataTestMethod]
    [DataRow(LexicalLookupStatus.TransientFailure)]
    [DataRow(LexicalLookupStatus.PermanentFailure)]
    [DataRow(LexicalLookupStatus.ParseFailure)]
    public async Task Wiktionary_OtherFailures_DoesNotCallWikipedia(LexicalLookupStatus status)
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.Success);
        var wiktionary = new TrackingProvider("Wiktionary", status);
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wiktionary");
        var result = await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(status, result.Status);
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

    [TestMethod]
    public async Task MissingWikipediaRegistration_ProducesDeterministicError()
    {
        var wiktionary = new TrackingProvider("Wiktionary", LexicalLookupStatus.NotFound);
        var service = CreateService([wiktionary]); 

        var request = CreateRequest("Wiktionary");
        var result = await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(LexicalLookupStatus.PermanentFailure, result.Status);
        Assert.AreEqual("provider-not-registered", result.ErrorCode);
    }

    [TestMethod]
    public async Task Wiktionary_NotFound_Wikipedia_NotFound_ReturnsWikipediaNotFound()
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.NotFound);
        var wiktionary = new TrackingProvider("Wiktionary", LexicalLookupStatus.NotFound);
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wiktionary");
        var result = await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(LexicalLookupStatus.NotFound, result.Status);
        Assert.AreEqual("Wikipedia", result.ProviderName);
        Assert.AreEqual(1, wiktionary.InvocationCount);
        Assert.AreEqual(1, wikipedia.InvocationCount);
    }

    [TestMethod]
    public async Task FallbackRequest_PreservesOriginalParameters()
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.NotFound);
        var wiktionary = new TrackingProvider("Wiktionary", LexicalLookupStatus.NotFound);
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wiktionary", LexicalLookupMode.DefinitionAndTranslation, "de", "testform", "testcanonical");
        await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(1, wikipedia.ReceivedRequests.Count);
        var fbRequest = wikipedia.ReceivedRequests[0];
        Assert.AreEqual("en", fbRequest.SourceLanguage);
        Assert.AreEqual(LexicalLookupMode.DefinitionAndTranslation, fbRequest.LookupMode);
        Assert.AreEqual("de", fbRequest.TargetLanguage);
        Assert.AreEqual("testform", fbRequest.DisplayedSurfaceForm);
        Assert.AreEqual("testcanonical", fbRequest.VocabularyCanonicalTerm);
        Assert.AreEqual("Wikipedia", fbRequest.Provider);
    }

    [TestMethod]
    public async Task Fallback_AfterWiktionaryRedirect_UsesFinalEffectiveLemma_AndPreservesRelation()
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.Success);
        wikipedia.ReturnResult = CreateValidResult("Wikipedia", LexicalLookupStatus.Success, null, LexicalLookupMode.Definition, true, "redirected");
        
        var wiktionary = new RedirectingThenNotFoundProvider();
        
        var service = CreateService([wikipedia, wiktionary]);
        var request = CreateRequest("Wiktionary", LexicalLookupMode.Definition, null, "test", "test");
        
        var result = await service.EnrichAsync(request, "test", null);
        
        Assert.AreEqual(1, wikipedia.InvocationCount);
        Assert.AreEqual("redirected", wikipedia.ReceivedRequests[0].CanonicalLookupTerm);
        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.AreEqual("test", result.EncounteredSurfaceForm);
        Assert.AreEqual(1, result.RedirectDepth);
        Assert.AreEqual("plural of redirected", result.GrammaticalRelationship);
    }

    private static LexicalEnrichmentService CreateService(ILexicalLookupProvider[] providers)
    {
        var resolver = new LexicalLookupProviderResolver(providers);
        var cache = new LexicalCacheRepository(new TemporaryKnownFirstDatabase(Guid.NewGuid().ToString("N")));
        return new LexicalEnrichmentService(new AcronymExpansionDetector(), new MeaningRanker(), cache, resolver);
    }

    private static LexicalLookupRequest CreateRequest(
        string provider, 
        LexicalLookupMode mode = LexicalLookupMode.Definition, 
        string? targetLang = null,
        string displayForm = "test",
        string canonicalForm = "test")
    {
        return new LexicalLookupRequest("en", mode, targetLang, canonicalForm, TokenKind.Word, provider, displayForm, canonicalForm);
    }

    private static LexicalResult CreateValidResult(
        string provider, 
        LexicalLookupStatus status, 
        string? targetLang = null, 
        LexicalLookupMode mode = LexicalLookupMode.Definition, 
        bool withTranslation = true,
        string lemma = "test")
    {
        return new LexicalResult(
            Status: status,
            QueriedLemma: lemma,
            DisplayTerm: lemma,
            TokenKind: TokenKind.Word,
            SourceLanguage: "en",
            ExplanationLanguage: targetLang ?? "en",
            AcronymExpansion: null,
            Meanings: [new LexicalMeaning("1", null, "A definition", withTranslation ? "A translation" : null, null, [])],
            ProviderName: provider,
            SourceProject: "",
            PageTitle: "",
            RevisionId: null,
            Attribution: "",
            LookupAtUtc: DateTime.UtcNow,
            IsFromCache: false,
            ErrorCode: null,
            LookupMode: mode,
            TargetLanguage: targetLang
        );
    }
}
