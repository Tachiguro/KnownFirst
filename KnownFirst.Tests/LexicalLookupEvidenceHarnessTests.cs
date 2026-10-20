using System.Net;
using System.Net.Http.Headers;
using System.Text;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Services.Lexical;
using KnownFirst.Services.Lexical.Wikipedia;
using KnownFirst.Tests.LexicalEvidence;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LexicalLookupEvidenceHarnessTests
{
    [TestMethod]
    public async Task CaptureEnrichment_EnglishDirectLookup_PreservesRequestedVsCanonicalTerms_AndNullPreparationAttemptCount()
    {
        await using var database = new TemporaryKnownFirstDatabase("kf-lex-005-en-direct");
        await database.InitializeAsync();
        var cache = new LexicalCacheRepository(database);

        var request = new LexicalLookupRequest(
            sourceLanguage: "en",
            lookupMode: LexicalLookupMode.Definition,
            targetLanguage: null,
            canonicalLookupTerm: "Network",
            tokenKind: TokenKind.Word,
            provider: WiktionaryLookupProvider.Name,
            displayedSurfaceForm: "Network",
            vocabularyCanonicalTerm: "Network");

        var evidence = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "LEX-EN-001",
                Description: "English direct definition lookup with uppercase surface form normalized to lowercase canonical lookup term.",
                Request: request,
                OriginalDocumentContent: "Network security is critical.",
                RepresentativeContext: "Network security is critical.",
                TransportRealism: LexicalEvidenceTransportRealism.MeasuredOfflineHttpTransport,
                OfflineHttpHandler: (_, _) => Task.FromResult(JsonResponse(LoadWiktionaryFixture("english-entry.json"))),
                CacheRepository: cache));

        Assert.AreEqual("LEX-EN-001", evidence.CaseId);
        Assert.AreEqual(LexicalEvidenceExecutionMode.DirectEnrichmentService, evidence.ExecutionMode);
        Assert.AreEqual(LexicalEvidenceTransportRealism.MeasuredOfflineHttpTransport, evidence.TransportRealism);
        Assert.IsNull(evidence.PreparationLookupAttemptCount, "Direct enrichment must leave PreparationLookupAttemptCount null.");
        Assert.AreEqual(1, evidence.ProviderInvocationCount);
        Assert.AreEqual(0, evidence.WikipediaApiClientInvocationCount);
        Assert.AreEqual(1, evidence.HttpAttemptCount);
        Assert.AreEqual(0, evidence.HttpRetryCount);

        Assert.AreEqual("Network", evidence.DisplayedSurfaceForm);
        Assert.AreEqual("Network", evidence.VocabularyCanonicalTerm);
        Assert.AreEqual("network", evidence.CanonicalLookupTerm);
        Assert.AreEqual("network", evidence.FinalQueriedLemma);
        Assert.AreEqual("network", evidence.FinalDisplayTerm);

        Assert.AreEqual(LexicalLookupStatus.Success, evidence.FinalStatus);
        Assert.IsNull(evidence.FinalErrorCode);
        Assert.AreEqual(LexicalOutcomeClassification.DirectSuccess, evidence.OutcomeClassification);
        Assert.IsTrue(evidence.HasUsableData);
        Assert.IsTrue(evidence.HasReferenceData);
        Assert.IsFalse(evidence.FinalResultFromCache);
        Assert.AreEqual(LexicalCacheObservationOutcome.Miss, evidence.PrimaryCacheOutcome);
        Assert.AreEqual(1, evidence.CacheReadAttemptCount);
        Assert.AreEqual(1, evidence.CacheMissCount);
        Assert.AreEqual(1, evidence.CacheWriteAttemptCount);
        Assert.AreEqual(1, evidence.CacheWriteStoredCount);

        CollectionAssert.AreEqual(new[] { "Wiktionary" }, evidence.OrderedProviderChain.ToArray());
        CollectionAssert.AreEqual(new[] { "network" }, evidence.OrderedQueryCandidates.ToArray());
    }

    [TestMethod]
    public async Task CapturePreparationLookup_GermanWordViaPreparationService_MeasuresActualPreparationLookupAttemptCount()
    {
        await using var database = new TemporaryKnownFirstDatabase("kf-lex-005-prep");
        await database.InitializeAsync();
        await SeedSingleGermanPreparationCandidateAsync(database, "Netzwerk", "Das Netzwerk ist geschützt.");

        var evidence = await LexicalEvidenceCaptureHarness.CapturePreparationLookupAsync(
            new LexicalEvidencePreparationCaptureOptions(
                CaseId: "LEX-DE-PREP-001",
                Description: "Real PreparationService.LookupCurrentAsync execution measuring persisted LookupAttemptCount.",
                Database: database,
                OfflineHttpHandler: (_, _) => Task.FromResult(JsonResponse(LoadWiktionaryFixture("german-entry.json")))));

        Assert.AreEqual(LexicalEvidenceExecutionMode.PreparationServiceLookup, evidence.ExecutionMode);
        Assert.AreEqual(LexicalEvidenceTransportRealism.MeasuredOfflineHttpTransport, evidence.TransportRealism);
        Assert.AreEqual(1, evidence.PreparationLookupAttemptCount, "PreparationService lookup must record actual persisted LookupAttemptCount.");
        Assert.AreEqual(1, evidence.ProviderInvocationCount);
        Assert.AreEqual(0, evidence.WikipediaApiClientInvocationCount);
        Assert.AreEqual(1, evidence.HttpAttemptCount);
        Assert.AreEqual(0, evidence.HttpRetryCount);
        Assert.AreEqual("de", evidence.SourceLanguage);
        Assert.AreEqual(LexicalLookupMode.Definition, evidence.LookupMode);
        Assert.AreEqual("Netzwerk", evidence.CanonicalLookupTerm);
        Assert.AreEqual(LexicalLookupStatus.Success, evidence.FinalStatus);
        Assert.AreEqual(LexicalOutcomeClassification.DirectSuccess, evidence.OutcomeClassification);
        Assert.IsTrue(evidence.HasUsableData);
    }

    [TestMethod]
    public async Task CaptureEnrichment_GermanCaseFallback_DistinguishesSingleProviderInvocationFromTwoHttpAttempts()
    {
        var request = new LexicalLookupRequest(
            sourceLanguage: "de",
            lookupMode: LexicalLookupMode.Definition,
            targetLanguage: null,
            canonicalLookupTerm: "Geht",
            tokenKind: TokenKind.Word,
            provider: WiktionaryLookupProvider.Name,
            displayedSurfaceForm: "Geht",
            vocabularyCanonicalTerm: "Geht");

        var evidence = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "LEX-DE-002",
                Description: "German capitalized word falls back to lowercase initial rune within single Wiktionary provider invocation.",
                Request: request,
                OriginalDocumentContent: "Geht das heute?",
                RepresentativeContext: "Geht das heute?",
                OfflineHttpHandler: (httpRequest, _) =>
                {
                    var query = httpRequest.RequestUri?.Query ?? string.Empty;
                    if (query.Contains("page=Geht", StringComparison.Ordinal))
                    {
                        return Task.FromResult(JsonResponse(LoadWiktionaryFixture("missing-page.json")));
                    }

                    if (query.Contains("page=geht", StringComparison.Ordinal))
                    {
                        return Task.FromResult(JsonResponse(CreateGermanWiktionaryEntryJson("geht", "sich zu Fuß fortbewegen", 98765)));
                    }

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                }));

        Assert.IsNull(evidence.PreparationLookupAttemptCount);
        Assert.AreEqual(1, evidence.ProviderInvocationCount, "German case fallback occurs inside a single WiktionaryLookupProvider.LookupAsync invocation.");
        Assert.AreEqual(2, evidence.HttpAttemptCount, "Both primary 'Geht' and fallback 'geht' HTTP sends must be counted.");
        Assert.AreEqual(0, evidence.HttpRetryCount, "Case fallback to a distinct query candidate is not an HTTP retry of the same URI.");
        Assert.IsTrue(evidence.CaseFallbackAttempted);
        Assert.IsTrue(evidence.CaseFallbackSucceeded);
        Assert.AreEqual(LexicalLookupStatus.Success, evidence.FinalStatus);
        Assert.AreEqual(LexicalOutcomeClassification.GermanCaseFallbackSuccess, evidence.OutcomeClassification);
        Assert.AreEqual("Geht", evidence.FinalDisplayTerm);
        Assert.AreEqual("geht", evidence.FinalPageTitle);

        CollectionAssert.AreEqual(new[] { "Wiktionary" }, evidence.OrderedProviderChain.ToArray());
        CollectionAssert.AreEqual(new[] { "Geht", "geht" }, evidence.OrderedQueryCandidates.ToArray());
        Assert.HasCount(1, evidence.LookupSteps);
        CollectionAssert.AreEqual(new[] { "Geht", "geht" }, evidence.LookupSteps[0].QueryCandidates.ToArray());
        Assert.IsTrue(evidence.LookupSteps[0].CaseFallbackTriggered);
        Assert.HasCount(2, evidence.LookupSteps[0].HttpAttempts);
    }

    [TestMethod]
    public async Task CaptureEnrichment_TranslationModeAndCacheHit_RecordsObservableCacheOutcomesWithoutUnverifiedInference()
    {
        await using var database = new TemporaryKnownFirstDatabase("kf-lex-005-cache");
        await database.InitializeAsync();
        var cache = new LexicalCacheRepository(database);

        var translationRequest = new LexicalLookupRequest(
            sourceLanguage: "en",
            lookupMode: LexicalLookupMode.Translation,
            targetLanguage: "de",
            canonicalLookupTerm: "network",
            tokenKind: TokenKind.Word,
            provider: WiktionaryLookupProvider.Name);

        var firstLookup = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "LEX-CACHE-001",
                Description: "First translation lookup populates cache after observed miss.",
                Request: translationRequest,
                OfflineHttpHandler: (_, _) => Task.FromResult(JsonResponse(LoadWiktionaryFixture("english-entry.json"))),
                CacheRepository: cache));

        Assert.AreEqual(LexicalCacheObservationOutcome.Miss, firstLookup.PrimaryCacheOutcome);
        Assert.AreEqual(LexicalCacheObservationOutcome.Miss, firstLookup.EffectiveCacheOutcome);
        Assert.IsFalse(firstLookup.FinalResultFromCache);
        Assert.AreEqual(1, firstLookup.CacheReadAttemptCount);
        Assert.AreEqual(1, firstLookup.CacheMissCount);
        Assert.AreEqual(1, firstLookup.CacheWriteAttemptCount);
        Assert.AreEqual(1, firstLookup.CacheWriteStoredCount);
        Assert.AreEqual(1, firstLookup.ProviderInvocationCount);
        Assert.AreEqual(1, firstLookup.HttpAttemptCount);
        Assert.AreEqual(LexicalOutcomeClassification.DirectSuccess, firstLookup.OutcomeClassification);

        var secondLookup = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "LEX-CACHE-002",
                Description: "Second identical translation lookup hits cache without invoking provider or HTTP handler.",
                Request: translationRequest,
                OfflineHttpHandler: (_, _) => throw new InvalidOperationException("HTTP transport must not be called on cache hit."),
                CacheRepository: cache));

        Assert.AreEqual(LexicalCacheObservationOutcome.Hit, secondLookup.PrimaryCacheOutcome);
        Assert.AreEqual(LexicalCacheObservationOutcome.Hit, secondLookup.EffectiveCacheOutcome);
        Assert.IsTrue(secondLookup.FinalResultFromCache);
        Assert.AreEqual(1, secondLookup.CacheReadAttemptCount);
        Assert.AreEqual(1, secondLookup.CacheHitCount);
        Assert.AreEqual(0, secondLookup.CacheMissCount);
        Assert.AreEqual(0, secondLookup.CacheWriteAttemptCount);
        Assert.AreEqual(0, secondLookup.ProviderInvocationCount);
        Assert.AreEqual(0, secondLookup.HttpAttemptCount);
        Assert.AreEqual(LexicalOutcomeClassification.CacheHitSuccess, secondLookup.OutcomeClassification);

        var unregisteredRequest = new LexicalLookupRequest(
            sourceLanguage: "en",
            lookupMode: LexicalLookupMode.Definition,
            targetLanguage: null,
            canonicalLookupTerm: "network",
            tokenKind: TokenKind.Word,
            provider: "UnregisteredProvider");

        var unregisteredEvidence = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "LEX-CACHE-003",
                Description: "Unregistered provider fails before consulting cache; cache miss must not be inferred.",
                Request: unregisteredRequest,
                CacheRepository: cache));

        Assert.AreEqual(0, unregisteredEvidence.CacheReadAttemptCount);
        Assert.AreEqual(0, unregisteredEvidence.CacheMissCount);
        Assert.AreEqual(LexicalCacheObservationOutcome.NotConsulted, unregisteredEvidence.PrimaryCacheOutcome);
        Assert.AreEqual(LexicalCacheObservationOutcome.NotConsulted, unregisteredEvidence.EffectiveCacheOutcome);
        Assert.AreEqual(LexicalLookupStatus.PermanentFailure, unregisteredEvidence.FinalStatus);
        Assert.AreEqual("provider-not-registered", unregisteredEvidence.FinalErrorCode);
        Assert.AreEqual(LexicalOutcomeClassification.ProviderNotRegistered, unregisteredEvidence.OutcomeClassification);

        var throwingCacheEvidence = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "LEX-CACHE-004",
                Description: "Throwing cache repository records observed ReadFailed and WriteFailed outcomes.",
                Request: translationRequest,
                OfflineHttpHandler: (_, _) => Task.FromResult(JsonResponse(LoadWiktionaryFixture("english-entry.json"))),
                CacheRepository: new FaultInjectingCacheRepository(throwOnRead: true, throwOnWrite: true)));

        Assert.AreEqual(LexicalCacheObservationOutcome.ReadFailed, throwingCacheEvidence.PrimaryCacheOutcome);
        Assert.AreEqual(1, throwingCacheEvidence.CacheReadAttemptCount);
        Assert.AreEqual(1, throwingCacheEvidence.CacheReadFailureCount);
        Assert.AreEqual(1, throwingCacheEvidence.CacheWriteAttemptCount);
        Assert.AreEqual(1, throwingCacheEvidence.CacheWriteFailureCount);
        Assert.AreEqual(LexicalLookupStatus.Success, throwingCacheEvidence.FinalStatus);
    }

    [TestMethod]
    public async Task CaptureEnrichment_LemmaRedirectAndBounds_CapturesIntermediateStepsLoopAndMaxDepth()
    {
        // 1. Successful form-relation redirect: systems -> system
        var redirectRequest = new LexicalLookupRequest(
            sourceLanguage: "en",
            lookupMode: LexicalLookupMode.Definition,
            targetLanguage: null,
            canonicalLookupTerm: "systems",
            tokenKind: TokenKind.Word,
            provider: WiktionaryLookupProvider.Name);

        var redirectSuccess = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "LEX-RED-001",
                Description: "English plural form 'systems' redirects to base lemma 'system'.",
                Request: redirectRequest,
                OfflineHttpHandler: (httpRequest, _) =>
                {
                    var query = httpRequest.RequestUri?.Query ?? string.Empty;
                    if (query.Contains("page=systems", StringComparison.Ordinal))
                    {
                        return Task.FromResult(JsonResponse(LoadWiktionaryFixture("english-systems-form.json")));
                    }

                    if (query.Contains("page=system", StringComparison.Ordinal))
                    {
                        return Task.FromResult(JsonResponse(CreateEnglishWiktionaryEntryJson("system", "Noun", "A set of interacting components.")));
                    }

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                }));

        Assert.AreEqual(2, redirectSuccess.ProviderInvocationCount);
        Assert.AreEqual(2, redirectSuccess.HttpAttemptCount);
        Assert.IsTrue(redirectSuccess.LemmaRedirectAttempted);
        Assert.IsTrue(redirectSuccess.LemmaRedirectSucceeded);
        Assert.AreEqual(1, redirectSuccess.FinalRedirectDepth);
        Assert.AreEqual("systems", redirectSuccess.EncounteredSurfaceForm);
        Assert.AreEqual("plural of system", redirectSuccess.GrammaticalRelationship);
        Assert.AreEqual("system", redirectSuccess.FinalDisplayTerm);
        Assert.AreEqual(LexicalLookupStatus.Success, redirectSuccess.FinalStatus);
        Assert.AreEqual(LexicalOutcomeClassification.LemmaRedirectSuccess, redirectSuccess.OutcomeClassification);
        CollectionAssert.AreEqual(new[] { "systems", "system" }, redirectSuccess.OrderedQueryCandidates.ToArray());
        Assert.HasCount(2, redirectSuccess.LookupSteps);
        Assert.AreEqual(0, redirectSuccess.LookupSteps[0].ExtractedDirectMeaningCount);
        Assert.HasCount(1, redirectSuccess.LookupSteps[0].ExtractedFormRelations);
        Assert.AreEqual(1, redirectSuccess.LookupSteps[1].ExtractedDirectMeaningCount);

        // 2. Redirect loop: systems -> system -> systems
        var redirectLoop = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "LEX-RED-002",
                Description: "Cyclic form relation 'systems' <-> 'system' fails closed with lemma-redirect-loop.",
                Request: redirectRequest,
                OfflineHttpHandler: (httpRequest, _) =>
                {
                    var query = httpRequest.RequestUri?.Query ?? string.Empty;
                    var def = query.Contains("page=systems", StringComparison.Ordinal)
                        ? "plural of system"
                        : "plural of systems";
                    return Task.FromResult(JsonResponse(CreateEnglishWiktionaryEntryJson("loop", "Noun", def)));
                }));

        Assert.AreEqual(2, redirectLoop.ProviderInvocationCount);
        Assert.AreEqual(2, redirectLoop.HttpAttemptCount);
        Assert.IsTrue(redirectLoop.LemmaRedirectAttempted);
        Assert.IsFalse(redirectLoop.LemmaRedirectSucceeded);
        Assert.AreEqual(LexicalLookupStatus.PermanentFailure, redirectLoop.FinalStatus);
        Assert.AreEqual("lemma-redirect-loop", redirectLoop.FinalErrorCode);
        Assert.AreEqual(LexicalOutcomeClassification.LemmaRedirectLoop, redirectLoop.OutcomeClassification);
        CollectionAssert.AreEqual(new[] { "systems", "system" }, redirectLoop.OrderedQueryCandidates.ToArray());

        // 3. Redirect depth exceeded (> MaximumLemmaRedirectDepth = 3)
        var depthRequest = new LexicalLookupRequest(
            sourceLanguage: "en",
            lookupMode: LexicalLookupMode.Definition,
            targetLanguage: null,
            canonicalLookupTerm: "stepzero",
            tokenKind: TokenKind.Word,
            provider: WiktionaryLookupProvider.Name);

        var depthExceeded = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "LEX-RED-003",
                Description: "Form relation chain exceeding MaximumLemmaRedirectDepth (3) fails closed.",
                Request: depthRequest,
                OfflineHttpHandler: (httpRequest, _) =>
                {
                    var query = httpRequest.RequestUri?.Query ?? string.Empty;
                    var next = query switch
                    {
                        _ when query.Contains("page=stepzero", StringComparison.Ordinal) => "stepone",
                        _ when query.Contains("page=stepone", StringComparison.Ordinal) => "steptwo",
                        _ when query.Contains("page=steptwo", StringComparison.Ordinal) => "stepthree",
                        _ => "stepfour"
                    };
                    return Task.FromResult(JsonResponse(CreateEnglishWiktionaryEntryJson("chain", "Noun", $"plural of {next}")));
                }));

        Assert.AreEqual(4, depthExceeded.ProviderInvocationCount);
        Assert.AreEqual(4, depthExceeded.HttpAttemptCount);
        Assert.AreEqual(3, depthExceeded.FinalRedirectDepth);
        Assert.IsTrue(depthExceeded.LemmaRedirectAttempted);
        Assert.IsFalse(depthExceeded.LemmaRedirectSucceeded);
        Assert.AreEqual(LexicalLookupStatus.PermanentFailure, depthExceeded.FinalStatus);
        Assert.AreEqual("lemma-redirect-depth-exceeded", depthExceeded.FinalErrorCode);
        Assert.AreEqual(LexicalOutcomeClassification.LemmaRedirectDepthExceeded, depthExceeded.OutcomeClassification);
        CollectionAssert.AreEqual(new[] { "stepzero", "stepone", "steptwo", "stepthree" }, depthExceeded.OrderedQueryCandidates.ToArray());
    }

    [TestMethod]
    public async Task CaptureEnrichment_WikipediaFallback_DistinguishesSimulatedApiClientFromHttpTransport_AndExcludesTranslationOnly()
    {
        var definitionRequest = new LexicalLookupRequest(
            sourceLanguage: "en",
            lookupMode: LexicalLookupMode.Definition,
            targetLanguage: null,
            canonicalLookupTerm: "cybernetics",
            tokenKind: TokenKind.Word,
            provider: WiktionaryLookupProvider.Name);

        // 1. Simulated IWikipediaApiClient: must record WikipediaApiClientInvocationCount = 1, but Wikipedia HttpAttemptCount = 0
        var simulatedWikiClient = new StubWikipediaApiClient(req => new WikipediaArticleResult(
            Status: WikipediaArticleStatus.Success,
            RequestedTitle: req.RequestedTitle,
            CanonicalTitle: "Cybernetics",
            Extract: "Cybernetics is the transdisciplinary study of circular causal systems.",
            SourceLanguage: "en",
            SourceProject: "en.wikipedia.org",
            PageId: 4200,
            RevisionId: 9001,
            CanonicalUrl: "https://en.wikipedia.org/wiki/Cybernetics",
            IsRedirect: false,
            RedirectedFrom: null,
            TargetLanguage: null,
            TargetTitleCandidate: null,
            TargetUrlCandidate: null,
            Attribution: "Wikipedia contributors (CC BY-SA 4.0).",
            ErrorCode: null,
            RetryAfter: null));

        var simulatedFallback = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "LEX-WIKI-001",
                Description: "Wiktionary NotFound in Definition mode triggers Wikipedia fallback via simulated IWikipediaApiClient.",
                Request: definitionRequest,
                TransportRealism: LexicalEvidenceTransportRealism.SimulatedWikipediaApiClient,
                OfflineHttpHandler: (_, _) => Task.FromResult(JsonResponse(LoadWiktionaryFixture("missing-page.json"))),
                SimulatedWikipediaApiClient: simulatedWikiClient));

        Assert.AreEqual(LexicalEvidenceTransportRealism.SimulatedWikipediaApiClient, simulatedFallback.TransportRealism);
        Assert.AreEqual(2, simulatedFallback.ProviderInvocationCount, "Both Wiktionary and Wikipedia providers are invoked.");
        Assert.AreEqual(1, simulatedFallback.WikipediaApiClientInvocationCount, "IWikipediaApiClient was invoked once.");
        Assert.AreEqual(1, simulatedFallback.HttpAttemptCount, "Only Wiktionary used the HTTP handler; simulated IWikipediaApiClient must not count as an HTTP attempt.");
        Assert.IsTrue(simulatedFallback.WikipediaFallbackEligible);
        Assert.IsTrue(simulatedFallback.WikipediaFallbackTriggered);
        Assert.IsTrue(simulatedFallback.WikipediaFallbackSucceeded);
        Assert.AreEqual("eligible-and-triggered", simulatedFallback.WikipediaFallbackDisposition);
        Assert.AreEqual(LexicalLookupStatus.Success, simulatedFallback.FinalStatus);
        Assert.AreEqual("Wikipedia", simulatedFallback.FinalProviderName);
        Assert.AreEqual(LexicalOutcomeClassification.WikipediaFallbackSuccess, simulatedFallback.OutcomeClassification);
        CollectionAssert.AreEqual(new[] { "Wiktionary", "Wikipedia" }, simulatedFallback.OrderedProviderChain.ToArray());
        Assert.HasCount(2, simulatedFallback.LookupSteps);
        Assert.AreEqual(LexicalLookupStatus.NotFound, simulatedFallback.LookupSteps[0].StepStatus);
        Assert.AreEqual("missing-page", simulatedFallback.LookupSteps[0].StepErrorCode);
        Assert.AreEqual(1, simulatedFallback.LookupSteps[0].HttpAttemptCount);
        Assert.IsTrue(simulatedFallback.LookupSteps[1].IsFallbackStep);
        Assert.IsTrue(simulatedFallback.LookupSteps[1].WikipediaApiClientInvoked);
        Assert.AreEqual(0, simulatedFallback.LookupSteps[1].HttpAttemptCount);
        Assert.AreEqual(LexicalLookupStatus.Success, simulatedFallback.LookupSteps[1].StepStatus);

        // 2. Measured HTTP-backed WikipediaApiClient: both Wiktionary and Wikipedia go through HTTP handler
        var httpFallback = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "LEX-WIKI-002",
                Description: "Wiktionary NotFound in Definition mode triggers HTTP-backed WikipediaApiClient fallback.",
                Request: new LexicalLookupRequest(
                    sourceLanguage: "en",
                    lookupMode: LexicalLookupMode.Definition,
                    targetLanguage: null,
                    canonicalLookupTerm: "Exact Title",
                    tokenKind: TokenKind.TechnicalTerm,
                    provider: WiktionaryLookupProvider.Name),
                TransportRealism: LexicalEvidenceTransportRealism.MeasuredOfflineHttpTransport,
                OfflineHttpHandler: (httpRequest, _) =>
                {
                    var host = httpRequest.RequestUri?.Host ?? string.Empty;
                    if (host.Contains("wiktionary.org", StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(JsonResponse(LoadWiktionaryFixture("missing-page.json")));
                    }

                    return Task.FromResult(JsonResponse(LoadWikipediaFixture("exact-success.json")));
                }));

        Assert.AreEqual(2, httpFallback.ProviderInvocationCount);
        Assert.AreEqual(1, httpFallback.WikipediaApiClientInvocationCount);
        Assert.AreEqual(2, httpFallback.HttpAttemptCount, "Both Wiktionary and HTTP-backed WikipediaApiClient sends are counted.");
        Assert.AreEqual(LexicalLookupStatus.Success, httpFallback.FinalStatus);
        Assert.AreEqual(LexicalOutcomeClassification.WikipediaFallbackSuccess, httpFallback.OutcomeClassification);

        // 3. Translation-only mode must NOT trigger Wikipedia fallback
        var translationOnlyRequest = new LexicalLookupRequest(
            sourceLanguage: "de",
            lookupMode: LexicalLookupMode.Translation,
            targetLanguage: "en",
            canonicalLookupTerm: "unbekannt",
            tokenKind: TokenKind.Word,
            provider: WiktionaryLookupProvider.Name);

        var translationExcluded = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "LEX-WIKI-003",
                Description: "Wiktionary NotFound in Translation-only mode does not trigger Wikipedia fallback.",
                Request: translationOnlyRequest,
                OfflineHttpHandler: (_, _) => Task.FromResult(JsonResponse(LoadWiktionaryFixture("missing-page.json")))));

        Assert.AreEqual(1, translationExcluded.ProviderInvocationCount);
        Assert.AreEqual(0, translationExcluded.WikipediaApiClientInvocationCount);
        Assert.AreEqual(1, translationExcluded.HttpAttemptCount);
        Assert.IsFalse(translationExcluded.WikipediaFallbackEligible);
        Assert.IsFalse(translationExcluded.WikipediaFallbackTriggered);
        Assert.IsFalse(translationExcluded.WikipediaFallbackSucceeded);
        Assert.AreEqual("excluded-translation-only-mode", translationExcluded.WikipediaFallbackDisposition);
        Assert.AreEqual(LexicalLookupStatus.NotFound, translationExcluded.FinalStatus);
        Assert.AreEqual("missing-page", translationExcluded.FinalErrorCode);
        Assert.AreEqual(LexicalOutcomeClassification.TranslationModeFallbackExcluded, translationExcluded.OutcomeClassification);
        CollectionAssert.AreEqual(new[] { "Wiktionary" }, translationExcluded.OrderedProviderChain.ToArray());
    }

    [TestMethod]
    public async Task CaptureEnrichment_MeaningRelevanceRanking_CapturesPreRankingAndRankedOrderWithoutFabricatingScores()
    {
        const string html = """
            <div class="mw-heading mw-heading2"><h2 id="English">English</h2></div>
            <div class="mw-heading mw-heading3"><h3>Noun</h3></div>
            <ol>
              <li><span class="usage-label">archaic</span><span class="definition">An archaic medieval watchtower router.</span></li>
              <li><span class="definition">A general domestic building.</span></li>
              <li><span class="usage-label">computing</span><span class="definition">A hardware packet router forwarding encrypted network traffic.</span></li>
            </ol>
            """;

        var jsonPayload = $$"""
            {
              "parse": {
                "title": "router",
                "revid": 777,
                "text": {{System.Text.Json.JsonSerializer.Serialize(html, KnownFirst.Core.Preparation.LexicalJsonSerializerContext.Default.String)}}
              }
            }
            """;

        var request = new LexicalLookupRequest(
            sourceLanguage: "en",
            lookupMode: LexicalLookupMode.Definition,
            targetLanguage: null,
            canonicalLookupTerm: "router",
            tokenKind: TokenKind.Word,
            provider: WiktionaryLookupProvider.Name);

        var evidence = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "LEX-RANK-001",
                Description: "Pre-ranking snapshot preserves provider order while final ranked order demotes archaic sense and promotes context-matching sense.",
                Request: request,
                OriginalDocumentContent: "The hardware packet router forwards encrypted network traffic.",
                RepresentativeContext: "The hardware packet router forwards encrypted network traffic.",
                OfflineHttpHandler: (_, _) => Task.FromResult(JsonResponse(jsonPayload))));

        Assert.AreEqual(LexicalLookupStatus.Success, evidence.FinalStatus);
        Assert.HasCount(3, evidence.PreRankingMeanings);
        Assert.HasCount(3, evidence.RankedMeanings);
        Assert.IsTrue(evidence.MeaningOrderChangedByRanking);

        // Pre-ranking order matches exact Wiktionary HTML document order (0, 1, 2)
        Assert.AreEqual(0, evidence.PreRankingMeanings[0].OrderIndex);
        Assert.AreEqual(0, evidence.PreRankingMeanings[0].OriginalProviderIndex);
        Assert.AreEqual(MeaningRelevanceTier.Demoted, evidence.PreRankingMeanings[0].RelevanceTier);
        Assert.AreEqual("archaic", evidence.PreRankingMeanings[0].TriggeringDemotionLabel);
        StringAssert.Contains(evidence.PreRankingMeanings[0].Definition, "archaic medieval watchtower");

        Assert.AreEqual(1, evidence.PreRankingMeanings[1].OrderIndex);
        Assert.AreEqual(MeaningRelevanceTier.Standard, evidence.PreRankingMeanings[1].RelevanceTier);
        StringAssert.Contains(evidence.PreRankingMeanings[1].Definition, "general domestic building");

        Assert.AreEqual(2, evidence.PreRankingMeanings[2].OrderIndex);
        Assert.AreEqual(MeaningRelevanceTier.Standard, evidence.PreRankingMeanings[2].RelevanceTier);
        StringAssert.Contains(evidence.PreRankingMeanings[2].Definition, "hardware packet router");

        // Ranked order: computing context-overlap standard sense (orig 2) -> general standard sense (orig 1) -> archaic demoted sense (orig 0)
        Assert.AreEqual(0, evidence.RankedMeanings[0].OrderIndex);
        Assert.AreEqual(2, evidence.RankedMeanings[0].OriginalProviderIndex);
        Assert.AreEqual(MeaningRelevanceTier.Standard, evidence.RankedMeanings[0].RelevanceTier);

        Assert.AreEqual(1, evidence.RankedMeanings[1].OrderIndex);
        Assert.AreEqual(1, evidence.RankedMeanings[1].OriginalProviderIndex);
        Assert.AreEqual(MeaningRelevanceTier.Standard, evidence.RankedMeanings[1].RelevanceTier);

        Assert.AreEqual(2, evidence.RankedMeanings[2].OrderIndex);
        Assert.AreEqual(0, evidence.RankedMeanings[2].OriginalProviderIndex);
        Assert.AreEqual(MeaningRelevanceTier.Demoted, evidence.RankedMeanings[2].RelevanceTier);
        Assert.AreEqual("archaic", evidence.RankedMeanings[2].TriggeringDemotionLabel);
    }

    [TestMethod]
    public async Task CaptureEnrichment_ErrorTaxonomyAndHttpRetries_ClassifiesParseTransientAndPermanentFailures()
    {
        var baseRequest = new LexicalLookupRequest(
            sourceLanguage: "en",
            lookupMode: LexicalLookupMode.Definition,
            targetLanguage: null,
            canonicalLookupTerm: "network",
            tokenKind: TokenKind.Word,
            provider: WiktionaryLookupProvider.Name);

        // 1. ParseFailure (malformed JSON)
        var parseFailure = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "LEX-ERR-PARSE",
                Description: "Malformed JSON payload returns ParseFailure without retry or Wikipedia fallback.",
                Request: baseRequest,
                OfflineHttpHandler: (_, _) => Task.FromResult(JsonResponse("{ malformed-json"))));

        Assert.AreEqual(LexicalLookupStatus.ParseFailure, parseFailure.FinalStatus);
        Assert.AreEqual("malformed-json", parseFailure.FinalErrorCode);
        Assert.AreEqual(LexicalOutcomeClassification.MalformedJson, parseFailure.OutcomeClassification);
        Assert.AreEqual(1, parseFailure.ProviderInvocationCount);
        Assert.AreEqual(1, parseFailure.HttpAttemptCount);
        Assert.AreEqual(0, parseFailure.HttpRetryCount);
        Assert.IsFalse(parseFailure.WikipediaFallbackTriggered);
        Assert.IsFalse(parseFailure.CanRetry);

        // 2. TransientFailure after 3 HTTP 503 attempts
        var transientFailure = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "LEX-ERR-TRANSIENT",
                Description: "Three HTTP 503 responses exhaust retry budget and return TransientFailure.",
                Request: baseRequest,
                OfflineHttpHandler: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));

        Assert.AreEqual(LexicalLookupStatus.TransientFailure, transientFailure.FinalStatus);
        Assert.AreEqual("transient-server-error", transientFailure.FinalErrorCode);
        Assert.AreEqual(LexicalOutcomeClassification.TransientServerError, transientFailure.OutcomeClassification);
        Assert.AreEqual(1, transientFailure.ProviderInvocationCount);
        Assert.AreEqual(3, transientFailure.HttpAttemptCount);
        Assert.AreEqual(2, transientFailure.HttpRetryCount);
        CollectionAssert.AreEqual(new[] { 250.0, 500.0 }, transientFailure.RecordedRetryDelayMilliseconds.ToArray());
        Assert.IsTrue(transientFailure.CanRetry);

        // 3. Recovery after HTTP 429 retry
        var retryAttempt = 0;
        var retryRecovered = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "LEX-RETRY-OK",
                Description: "HTTP 429 on first send followed by HTTP 200 succeeds with 1 retry recorded.",
                Request: baseRequest,
                OfflineHttpHandler: (_, _) =>
                {
                    retryAttempt++;
                    if (retryAttempt == 1)
                    {
                        var tooMany = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                        tooMany.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
                        return Task.FromResult(tooMany);
                    }

                    return Task.FromResult(JsonResponse(LoadWiktionaryFixture("english-entry.json")));
                }));

        Assert.AreEqual(LexicalLookupStatus.Success, retryRecovered.FinalStatus);
        Assert.AreEqual(1, retryRecovered.ProviderInvocationCount);
        Assert.AreEqual(2, retryRecovered.HttpAttemptCount);
        Assert.AreEqual(1, retryRecovered.HttpRetryCount);
        CollectionAssert.AreEqual(new[] { 3000.0 }, retryRecovered.RecordedRetryDelayMilliseconds.ToArray());
        Assert.IsFalse(retryRecovered.LookupSteps[0].HttpAttempts[0].IsRetry);
        Assert.IsTrue(retryRecovered.LookupSteps[0].HttpAttempts[1].IsRetry);

        // 4. PermanentFailure on HTTP 400
        var permanentFailure = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "LEX-ERR-PERM",
                Description: "HTTP 400 client error returns PermanentFailure without retry.",
                Request: baseRequest,
                OfflineHttpHandler: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest))));

        Assert.AreEqual(LexicalLookupStatus.PermanentFailure, permanentFailure.FinalStatus);
        Assert.AreEqual("http-400", permanentFailure.FinalErrorCode);
        Assert.AreEqual(LexicalOutcomeClassification.HttpClientError, permanentFailure.OutcomeClassification);
        Assert.AreEqual(1, permanentFailure.ProviderInvocationCount);
        Assert.AreEqual(1, permanentFailure.HttpAttemptCount);
        Assert.AreEqual(0, permanentFailure.HttpRetryCount);
        Assert.IsFalse(permanentFailure.CanRetry);
    }

    [TestMethod]
    public async Task ReportFormatter_ProducesDeterministicJsonAndText_WithExplicitDenominatorsAndProblematicTermListings()
    {
        await using var prepDb = new TemporaryKnownFirstDatabase("kf-lex-005-suite-prep");
        await prepDb.InitializeAsync();
        await SeedSingleGermanPreparationCandidateAsync(prepDb, "Netzwerk", "Das Netzwerk ist sicher.");

        // 1. German PreparationService direct success
        var c1 = await LexicalEvidenceCaptureHarness.CapturePreparationLookupAsync(
            new LexicalEvidencePreparationCaptureOptions(
                CaseId: "CASE-01-DE-PREP",
                Description: "German PreparationService lookup.",
                Database: prepDb,
                OfflineHttpHandler: (_, _) => Task.FromResult(JsonResponse(LoadWiktionaryFixture("german-entry.json")))));

        // 2. German case fallback success
        var c2 = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "CASE-02-DE-CASE-FALLBACK",
                Description: "German capitalized verb fallback.",
                Request: new LexicalLookupRequest("de", LexicalLookupMode.Definition, null, "Geht", TokenKind.Word, WiktionaryLookupProvider.Name),
                OfflineHttpHandler: (req, _) =>
                {
                    var q = req.RequestUri?.Query ?? string.Empty;
                    return Task.FromResult(q.Contains("page=Geht", StringComparison.Ordinal)
                        ? JsonResponse(LoadWiktionaryFixture("missing-page.json"))
                        : JsonResponse(CreateGermanWiktionaryEntryJson("geht", "sich bewegen", 101)));
                }));

        // 3. English Wikipedia fallback via simulated API client
        var c3 = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "CASE-03-EN-WIKI-FALLBACK",
                Description: "English Wikipedia fallback with simulated client.",
                Request: new LexicalLookupRequest("en", LexicalLookupMode.Definition, null, "cybernetics", TokenKind.Word, WiktionaryLookupProvider.Name),
                TransportRealism: LexicalEvidenceTransportRealism.SimulatedWikipediaApiClient,
                OfflineHttpHandler: (_, _) => Task.FromResult(JsonResponse(LoadWiktionaryFixture("missing-page.json"))),
                SimulatedWikipediaApiClient: new StubWikipediaApiClient(r => new WikipediaArticleResult(
                    WikipediaArticleStatus.Success, r.RequestedTitle, "Cybernetics", "Control and communication theory.", "en", "en.wikipedia.org", 12, 34, "https://en.wikipedia.org/wiki/Cybernetics", false, null, null, null, null, "CC BY-SA 4.0", null, null))));

        // 4. German Translation-only NotFound (fallback excluded)
        var c4 = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "CASE-04-DE-TRANS-NOTFOUND",
                Description: "German translation lookup missing page; Wikipedia fallback excluded.",
                Request: new LexicalLookupRequest("de", LexicalLookupMode.Translation, "en", "unbekannt", TokenKind.Word, WiktionaryLookupProvider.Name),
                OfflineHttpHandler: (_, _) => Task.FromResult(JsonResponse(LoadWiktionaryFixture("missing-page.json")))));

        // 5. English ParseFailure
        var c5 = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "CASE-05-EN-PARSE-FAILURE",
                Description: "English malformed JSON response.",
                Request: new LexicalLookupRequest("en", LexicalLookupMode.Definition, null, "broken", TokenKind.Word, WiktionaryLookupProvider.Name),
                OfflineHttpHandler: (_, _) => Task.FromResult(JsonResponse("{ not-json"))));

        // 6. English TransientFailure with retries
        var c6 = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "CASE-06-EN-TRANSIENT-FAILURE",
                Description: "English transient 503 failure with retries.",
                Request: new LexicalLookupRequest("en", LexicalLookupMode.Definition, null, "unstable", TokenKind.Word, WiktionaryLookupProvider.Name),
                OfflineHttpHandler: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));

        // 7. English PermanentFailure (redirect loop)
        var c7 = await LexicalEvidenceCaptureHarness.CaptureEnrichmentAsync(
            new LexicalEvidenceEnrichmentCaptureOptions(
                CaseId: "CASE-07-EN-PERM-LOOP",
                Description: "English lemma redirect loop failure.",
                Request: new LexicalLookupRequest("en", LexicalLookupMode.Definition, null, "systems", TokenKind.Word, WiktionaryLookupProvider.Name),
                OfflineHttpHandler: (req, _) =>
                {
                    var q = req.RequestUri?.Query ?? string.Empty;
                    var def = q.Contains("page=systems", StringComparison.Ordinal) ? "plural of system" : "plural of systems";
                    return Task.FromResult(JsonResponse(CreateEnglishWiktionaryEntryJson("loop", "Noun", def)));
                }));

        // Pass intentionally out-of-order to verify stable sorting by CaseId in BuildReportDocument
        var report = LexicalEvidenceCaptureHarness.BuildReportDocument(
            suiteId: "KF-LEX-005-SLICE1-MATRIX",
            description: "Slice 1 deterministic lexical lookup evidence matrix.",
            candidates: [c7, c2, c5, c1, c6, c3, c4]);

        var summary = report.Summary;
        Assert.AreEqual(7, summary.TotalCandidateCount);
        Assert.AreEqual(3, summary.GermanCandidateCount);
        Assert.AreEqual(4, summary.EnglishCandidateCount);
        Assert.AreEqual(6, summary.DefinitionModeCandidateCount);
        Assert.AreEqual(1, summary.TranslationModeCandidateCount);
        Assert.AreEqual(0, summary.DefinitionAndTranslationModeCandidateCount);

        Assert.AreEqual(1, summary.PreparationServiceCandidateCount);
        Assert.AreEqual(6, summary.DirectEnrichmentCandidateCount);
        Assert.AreEqual(6, summary.MeasuredHttpTransportCandidateCount);
        Assert.AreEqual(1, summary.SimulatedWikipediaApiClientCandidateCount);
        Assert.AreEqual(0, summary.SimulatedProviderBoundaryCandidateCount);

        Assert.AreEqual(1, summary.TotalPreparationLookupAttemptCount);
        Assert.AreEqual(9, summary.TotalProviderInvocationCount); // c1:1, c2:1, c3:2, c4:1, c5:1, c6:1, c7:2 = 9
        Assert.AreEqual(1, summary.TotalWikipediaApiClientInvocationCount);
        Assert.AreEqual(11, summary.TotalHttpAttemptCount); // c1:1, c2:2, c3:1, c4:1, c5:1, c6:3, c7:2 = 11
        Assert.AreEqual(2, summary.TotalHttpRetryCount); // c6:2

        Assert.AreEqual(3, summary.SuccessCount);
        Assert.AreEqual(1, summary.NotFoundCount);
        Assert.AreEqual(1, summary.ParseFailureCount);
        Assert.AreEqual(1, summary.TransientFailureCount);
        Assert.AreEqual(1, summary.PermanentFailureCount);
        Assert.AreEqual(7, summary.StatusBreakdown.Sum(s => s.Count));
        Assert.AreEqual(7, summary.ClassificationBreakdown.Sum(c => c.Count));

        // Verify stable candidate ordering by CaseId
        var orderedCaseIds = report.Candidates.Select(c => c.CaseId).ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                "CASE-01-DE-PREP",
                "CASE-02-DE-CASE-FALLBACK",
                "CASE-03-EN-WIKI-FALLBACK",
                "CASE-04-DE-TRANS-NOTFOUND",
                "CASE-05-EN-PARSE-FAILURE",
                "CASE-06-EN-TRANSIENT-FAILURE",
                "CASE-07-EN-PERM-LOOP"
            },
            orderedCaseIds);

        // Verify FailedTerms (4 failed candidates)
        Assert.HasCount(4, summary.FailedTerms);
        CollectionAssert.AreEqual(
            new[]
            {
                "CASE-04-DE-TRANS-NOTFOUND",
                "CASE-05-EN-PARSE-FAILURE",
                "CASE-06-EN-TRANSIENT-FAILURE",
                "CASE-07-EN-PERM-LOOP"
            },
            summary.FailedTerms.Select(f => f.CaseId).ToArray());

        // Verify ProblematicTerms (includes c2 German case fallback, c3 Wiki fallback, and the 4 failed terms)
        Assert.IsTrue(summary.ProblematicTerms.Any(p => p.CaseId == "CASE-02-DE-CASE-FALLBACK" && p.ProblemKind == LexicalProblemKind.RecoveredViaGermanCaseFallback));
        Assert.IsTrue(summary.ProblematicTerms.Any(p => p.CaseId == "CASE-03-EN-WIKI-FALLBACK" && p.ProblemKind == LexicalProblemKind.RecoveredViaWikipediaFallback));
        Assert.IsTrue(summary.ProblematicTerms.Any(p => p.CaseId == "CASE-06-EN-TRANSIENT-FAILURE" && p.ProblemKind == LexicalProblemKind.LookupFailed));

        // Verify JSON & TXT determinism and round-trip
        var json1 = LexicalEvidenceReportFormatter.FormatJson(report);
        var json2 = LexicalEvidenceReportFormatter.FormatJson(report);
        Assert.AreEqual(json1, json2);
        Assert.IsFalse(json1.Contains("\r\n", StringComparison.Ordinal));

        var txt1 = LexicalEvidenceReportFormatter.FormatText(report);
        var txt2 = LexicalEvidenceReportFormatter.FormatText(report);
        Assert.AreEqual(txt1, txt2);
        Assert.IsFalse(txt1.Contains("\r\n", StringComparison.Ordinal));
        StringAssert.Contains(txt1, "=== LEXICAL LOOKUP EVIDENCE REPORT ===");
        StringAssert.Contains(txt1, "--- QUANTITATIVE SUMMARY & EXPLICIT DENOMINATORS ---");
        StringAssert.Contains(txt1, "--- FAILED TERMS (4) ---");
        StringAssert.Contains(txt1, "--- PROBLEMATIC & RECOVERY TERMS");
        StringAssert.Contains(txt1, "--- PER-CANDIDATE EVIDENCE TRACES (7) ---");

        var roundTrip = LexicalEvidenceReportFormatter.DeserializeJson(json1);
        Assert.AreEqual(report.SuiteId, roundTrip.SuiteId);
        Assert.HasCount(report.Candidates.Count, roundTrip.Candidates);
        Assert.AreEqual(report.Summary.TotalCandidateCount, roundTrip.Summary.TotalCandidateCount);
        Assert.AreEqual(report.Summary.TotalHttpAttemptCount, roundTrip.Summary.TotalHttpAttemptCount);
        Assert.HasCount(report.Summary.FailedTerms.Count, roundTrip.Summary.FailedTerms);

        // Verify isolated temp directory file writing
        var tempDir = Path.Combine(Path.GetTempPath(), $"kf-lex-005-report-{Guid.NewGuid():N}");
        try
        {
            var (txtPath, jsonPath) = LexicalEvidenceReportFormatter.WriteReportFiles(report, tempDir, "slice1-matrix");
            Assert.IsTrue(File.Exists(txtPath));
            Assert.IsTrue(File.Exists(jsonPath));
            Assert.AreEqual(txt1, await File.ReadAllTextAsync(txtPath, Encoding.UTF8));
            Assert.AreEqual(json1, await File.ReadAllTextAsync(jsonPath, Encoding.UTF8));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static async Task SeedSingleGermanPreparationCandidateAsync(
        TemporaryKnownFirstDatabase database,
        string canonicalTerm,
        string sentenceText)
    {
        var now = LexicalEvidenceCaptureHarness.DefaultUtcNow;
        await database.RunInTransactionAsync(connection =>
        {
            var document = new DocumentEntity
            {
                Title = "German Test Document",
                Content = sentenceText,
                ContentFingerprint = "hash-de-001",
                TextLanguage = "de",
                ExplanationLanguage = "de",
                LookupMode = LexicalLookupMode.Definition,
                TargetLanguage = string.Empty,
                ImportedAt = now,
                WordCount = 4
            };
            connection.Insert(document);

            var sentence = new SentenceSpanEntity
            {
                DocumentId = document.Id,
                Order = 0,
                StartPosition = 0,
                Length = sentenceText.Length
            };
            connection.Insert(sentence);

            var word = new WordEntity
            {
                NormalizedTerm = canonicalTerm,
                CanonicalTerm = canonicalTerm,
                Language = "de",
                TokenKind = TokenKind.Word,
                Status = WordStatus.UnknownBacklog,
                PreparationState = PreparationState.Unprepared,
                TotalOccurrenceCount = 1,
                DocumentCount = 1,
                CreatedAt = now,
                UpdatedAt = now
            };
            connection.Insert(word);

            connection.Insert(new WordFormEntity
            {
                WordId = word.Id,
                SurfaceForm = canonicalTerm,
                OccurrenceCount = 1
            });

            var startIndex = sentenceText.IndexOf(canonicalTerm, StringComparison.Ordinal);
            connection.Insert(new WordOccurrenceEntity
            {
                DocumentId = document.Id,
                SentenceSpanId = sentence.Id,
                WordId = word.Id,
                SurfaceForm = canonicalTerm,
                Order = 0,
                StartPosition = startIndex >= 0 ? startIndex : 0,
                Length = canonicalTerm.Length
            });

            return true;
        });
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string LoadWiktionaryFixture(string fileName) => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "Wiktionary",
        fileName));

    private static string LoadWikipediaFixture(string fileName) => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "Wikipedia",
        fileName));

    private static string CreateGermanWiktionaryEntryJson(string title, string definition, long revId) =>
        $$"""
        {
          "parse": {
            "title": "{{title}}",
            "revid": {{revId}},
            "text": "<div class=\"mw-heading mw-heading2\"><h2 id=\"{{title}}_(Deutsch)\">{{title}} (Deutsch)</h2></div><div class=\"mw-heading mw-heading3\"><h3>Verb</h3></div><p style=\"font-weight:bold;\">Bedeutungen:</p><dl><dd>[1] {{definition}}</dd></dl>"
          }
        }
        """;

    private static string CreateEnglishWiktionaryEntryJson(string title, string pos, string definition, long revId = 555) =>
        $$"""
        {
          "parse": {
            "title": "{{title}}",
            "revid": {{revId}},
            "text": "<div class=\"mw-heading mw-heading2\"><h2 id=\"English\">English</h2></div><div class=\"mw-heading mw-heading3\"><h3>{{pos}}</h3></div><ol><li><span class=\"definition\">{{definition}}</span></li></ol>"
          }
        }
        """;

    private sealed class StubWikipediaApiClient(Func<WikipediaArticleRequest, WikipediaArticleResult> handler) : IWikipediaApiClient
    {
        public Task<WikipediaArticleResult> GetArticleAsync(
            WikipediaArticleRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(handler(request));
    }

    private sealed class FaultInjectingCacheRepository(bool throwOnRead, bool throwOnWrite) : ILexicalCacheRepository
    {
        public Task<LexicalResult?> GetAsync(
            LexicalLookupRequest request,
            string provider,
            int providerSchemaVersion) =>
            throwOnRead
                ? throw new InvalidOperationException("Simulated cache read failure.")
                : Task.FromResult<LexicalResult?>(null);

        public Task SaveAsync(
            LexicalLookupRequest request,
            LexicalResult result,
            int providerSchemaVersion) =>
            throwOnWrite
                ? throw new InvalidOperationException("Simulated cache write failure.")
                : Task.CompletedTask;
    }
}
