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
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.NotFound);
        var wiktionary = new TrackingProvider("Wiktionary", LexicalLookupStatus.NotFound);
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wiktionary", LexicalLookupMode.Translation, "de");
        var result = await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(LexicalLookupStatus.NotFound, result.Status);
        Assert.AreEqual(1, wiktionary.InvocationCount);
        Assert.AreEqual(0, wikipedia.InvocationCount);
    }

    [TestMethod]
    public async Task ExplicitWikipedia_RelationLikeResult_DoesNotCallWiktionary()
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.Success);
        // "plural of redirected" will cause a redirect attempt within the provider loop
        wikipedia.ReturnResult = CreateValidResult("Wikipedia", LexicalLookupStatus.Success, null, LexicalLookupMode.Definition, false, "test");
        wikipedia.ReturnResult = wikipedia.ReturnResult with { Meanings = [new LexicalMeaning("1", null, "plural of redirected", null, null, [])] };

        var wiktionary = new TrackingProvider("Wiktionary", LexicalLookupStatus.Success);
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wikipedia");
        var result = await service.EnrichAsync(request, "test", null);

        // It should have redirected internally in Wikipedia, failing because the second Wikipedia return is also "plural of redirected" (the mock always returns it) which triggers lemma-redirect-loop
        Assert.AreEqual(LexicalLookupStatus.PermanentFailure, result.Status);
        Assert.AreEqual("lemma-redirect-loop", result.ErrorCode);
        Assert.AreEqual(0, wiktionary.InvocationCount);
    }

    [DataTestMethod]
    [DataRow(LexicalLookupStatus.TransientFailure, "timeout")]
    [DataRow(LexicalLookupStatus.TransientFailure, "rate-limited")]
    [DataRow(LexicalLookupStatus.TransientFailure, "network-error")]
    [DataRow(LexicalLookupStatus.ParseFailure, "parse-error")]
    [DataRow(LexicalLookupStatus.PermanentFailure, "permanent-error")]
    public async Task Wiktionary_OperationalFailures_DoesNotCallWikipedia(LexicalLookupStatus status, string errorCode)
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.Success);
        var wiktionary = new TrackingProvider("Wiktionary", status);
        wiktionary.ReturnResult = CreateValidResult("Wiktionary", status) with { ErrorCode = errorCode };
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wiktionary");
        var result = await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(status, result.Status);
        Assert.AreEqual(errorCode, result.ErrorCode);
        Assert.AreEqual(1, wiktionary.InvocationCount);
        Assert.AreEqual(0, wikipedia.InvocationCount);
    }

    [DataTestMethod]
    [DataRow(LexicalLookupStatus.TransientFailure, "timeout")]
    [DataRow(LexicalLookupStatus.ParseFailure, "parse-error")]
    [DataRow(LexicalLookupStatus.PermanentFailure, "permanent-error")]
    [DataRow(LexicalLookupStatus.NotFound, "not-found")]
    public async Task Wiktionary_NotFound_Wikipedia_Failures_AreReturnedWithoutRetry(LexicalLookupStatus status, string errorCode)
    {
        var wikipedia = new TrackingProvider("Wikipedia", status);
        wikipedia.ReturnResult = CreateValidResult("Wikipedia", status) with { ErrorCode = errorCode, Meanings = [] };
        var wiktionary = new TrackingProvider("Wiktionary", LexicalLookupStatus.NotFound);
        wiktionary.ReturnResult = CreateValidResult("Wiktionary", LexicalLookupStatus.NotFound) with { ErrorCode = "not-found", Meanings = [] };
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wiktionary");
        var result = await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(status, result.Status);
        Assert.AreEqual(errorCode, result.ErrorCode);
        Assert.AreEqual("Wikipedia", result.ProviderName);
        Assert.AreEqual(1, wiktionary.InvocationCount);
        Assert.AreEqual(1, wikipedia.InvocationCount);
    }

    [TestMethod]
    public async Task Wiktionary_ProviderCrash_DoesNotCallWikipedia()
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.Success);
        var wiktionary = new CrashingProvider("Wiktionary");
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wiktionary");
        var result = await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(LexicalLookupStatus.PermanentFailure, result.Status);
        Assert.AreEqual("provider-crash", result.ErrorCode);
        Assert.AreEqual(0, wikipedia.InvocationCount);
    }

    [TestMethod]
    public async Task Wiktionary_ProviderIdentityMismatch_DoesNotCallWikipedia()
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.Success);
        var wiktionary = new TrackingProvider("Wiktionary", LexicalLookupStatus.Success);
        wiktionary.ReturnResult = CreateValidResult("WrongIdentity", LexicalLookupStatus.Success);
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wiktionary");
        var result = await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(LexicalLookupStatus.PermanentFailure, result.Status);
        Assert.AreEqual("provider-identity-mismatch", result.ErrorCode);
        Assert.AreEqual(0, wikipedia.InvocationCount);
    }

    [TestMethod]
    public async Task UnknownExplicitlyRequestedProvider_DoesNotCallWikipedia()
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.Success);
        var service = CreateService([wikipedia]);

        var request = CreateRequest("UnknownProvider");
        var result = await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(LexicalLookupStatus.PermanentFailure, result.Status);
        Assert.AreEqual("provider-not-registered", result.ErrorCode);
        Assert.AreEqual(0, wikipedia.InvocationCount);
    }

    private sealed class CancellingThenNotFoundProvider(CancellationTokenSource cts) : ILexicalLookupProvider
    {
        public string ProviderName => "Wiktionary";
        public int ProviderSchemaVersion => 1;

        public Task<LexicalResult> LookupAsync(LexicalLookupRequest request, CancellationToken cancellationToken = default)
        {
            cts.Cancel(); // Simulate caller cancelling exactly as primary finishes
            return Task.FromResult(new LexicalResult(
                LexicalLookupStatus.NotFound,
                request.CanonicalLookupTerm,
                request.DisplayedSurfaceForm,
                request.TokenKind,
                request.SourceLanguage,
                request.ExplanationLanguage,
                null, [], "Wiktionary", "", "", null, "", DateTime.UtcNow
            )
            {
                ErrorCode = "not-found",
                LookupMode = request.LookupMode,
                TargetLanguage = request.TargetLanguage
            });
        }
    }

    [TestMethod]
    public async Task CallerCancellation_BetweenPrimaryAndFallback_DoesNotCallWikipedia()
    {
        using var cts = new CancellationTokenSource();
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.Success);
        var wiktionary = new CancellingThenNotFoundProvider(cts);
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wiktionary");
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.EnrichAsync(request, "test", null, cts.Token));

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
    public async Task Fallback_Success_PreservesAllWikipediaMetadataAndRestrictsTranslation()
    {
        var wikipedia = new TrackingProvider("Wikipedia", LexicalLookupStatus.Success);
        wikipedia.ReturnResult = new LexicalResult(
            LexicalLookupStatus.Success,
            "test-lemma",
            "test-display",
            TokenKind.Word,
            "en",
            "de",
            null,
            [new LexicalMeaning("wiki-meaning-1", "noun", "wiki definition", null, null, [])], // Translation must remain null
            "Wikipedia",
            "en.wikipedia.org",
            "Test_Page",
            12345,
            "Wikipedia Contributors",
            DateTime.UtcNow
        )
        {
            LookupMode = LexicalLookupMode.DefinitionAndTranslation,
            TargetLanguage = "de"
        };
        var wiktionary = new TrackingProvider("Wiktionary", LexicalLookupStatus.NotFound);
        var service = CreateService([wikipedia, wiktionary]);

        var request = CreateRequest("Wiktionary", LexicalLookupMode.DefinitionAndTranslation, "de");
        var result = await service.EnrichAsync(request, "test", null);

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.AreEqual("Wikipedia", result.ProviderName);
        Assert.AreEqual("en.wikipedia.org", result.SourceProject);
        Assert.AreEqual("Test_Page", result.PageTitle);
        Assert.AreEqual(12345, result.RevisionId);
        Assert.AreEqual("Wikipedia Contributors", result.Attribution);
        Assert.AreEqual(LexicalLookupMode.DefinitionAndTranslation, result.LookupMode);
        Assert.AreEqual("de", result.TargetLanguage);
        Assert.AreEqual("en", result.SourceLanguage);

        Assert.AreEqual(1, result.Meanings.Count);
        var meaning = result.Meanings.Single();
        Assert.AreEqual("wiki-meaning-1", meaning.MeaningId);
        Assert.AreEqual("wiki definition", meaning.Definition);
        Assert.IsNull(meaning.Translation);
    }

    [TestMethod]
    public async Task Fallback_ResultsInWikipediaCache_NotWiktionaryCache()
    {
        var db = new TemporaryKnownFirstDatabase(Guid.NewGuid().ToString("N"));
        var cache = new LexicalCacheRepository(db);
        var resolver = new LexicalLookupProviderResolver([
            new TrackingProvider("Wikipedia", LexicalLookupStatus.Success) { ReturnResult = new LexicalResult(
                LexicalLookupStatus.Success,
                "test", "test", TokenKind.Word, "en", "en", null,
                [new LexicalMeaning("wiki-meaning-1", "noun", "wiki definition", null, null, [])],
                "Wikipedia", "en.wikipedia.org", "Test_Page", 12345, "Wikipedia Contributors",
                DateTime.UtcNow
            )
            {
                LookupMode = LexicalLookupMode.Definition,
                TargetLanguage = "en"
            } },
            new TrackingProvider("Wiktionary", LexicalLookupStatus.NotFound) { ReturnResult = CreateValidResult("Wiktionary", LexicalLookupStatus.NotFound) }
        ]);
        var service = new LexicalEnrichmentService(new AcronymExpansionDetector(), new MeaningRanker(), cache, resolver);

        var request = CreateRequest("Wiktionary", LexicalLookupMode.DefinitionAndTranslation, "de");
        
        // 1. Initial request triggers fallback and populates cache
        var result1 = await service.EnrichAsync(request, "test", null);
        Assert.AreEqual(LexicalLookupStatus.Success, result1.Status);
        Assert.AreEqual("Wikipedia", result1.ProviderName);
        Assert.IsFalse(result1.IsFromCache);

        // 2. Second request should hit Wikipedia cache
        var result2 = await service.EnrichAsync(request, "test", null);
        Assert.AreEqual(LexicalLookupStatus.Success, result2.Status);
        Assert.AreEqual("Wikipedia", result2.ProviderName);
        Assert.IsTrue(result2.IsFromCache);

        // Assert serialization survival
        Assert.AreEqual("en.wikipedia.org", result2.SourceProject);
        Assert.AreEqual("Test_Page", result2.PageTitle);
        Assert.AreEqual(12345, result2.RevisionId);
        Assert.AreEqual("Wikipedia Contributors", result2.Attribution);
        Assert.AreEqual(LexicalLookupMode.DefinitionAndTranslation, result2.LookupMode);
        Assert.AreEqual("de", result2.TargetLanguage);
        var meaning = result2.Meanings.Single();
        Assert.AreEqual("wiki-meaning-1", meaning.MeaningId);

        // Ensure tracking providers were only called once (second time hit cache)
        var wikipediaProvider = (TrackingProvider)resolver.TryResolve("Wikipedia")!;
        var wiktionaryProvider = (TrackingProvider)resolver.TryResolve("Wiktionary")!;
        Assert.AreEqual(1, wikipediaProvider.InvocationCount); // Hit cache instead
        Assert.AreEqual(2, wiktionaryProvider.InvocationCount); // Evaluated twice, missed cache both times because Wiktionary didn't succeed and Wikipedia was cached under Wikipedia!
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

    private sealed class CrashingProvider(string name) : ILexicalLookupProvider
    {
        public string ProviderName { get; } = name;
        public int ProviderSchemaVersion { get; } = 1;
        public Task<LexicalResult> LookupAsync(LexicalLookupRequest request, CancellationToken cancellationToken = default)
        {
            throw new Exception("Intentional crash");
        }
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
