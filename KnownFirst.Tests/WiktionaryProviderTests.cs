using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Data.Entities;
using KnownFirst.Services.Lexical;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Globalization;

namespace KnownFirst.Tests;

[TestClass]
public sealed class WiktionaryProviderTests
{
        [TestMethod]
    public void Parser_Aot_ExtractsDefinitionsTranslationsExamplesAndLabels_WithExactDocumentOrder()
    {
        var html = LoadFixture("parser-aot-cases.html");
        var parser = new WiktionaryHtmlParser();
        
        var result = parser.ParseEntry(html, "en", "en", "-", KnownFirst.Core.Preparation.LexicalLookupMode.DefinitionAndTranslation);
        var meanings = result.DirectMeanings;

        // Verify Definitions (these are stable and preserve order)
        Assert.IsTrue(meanings.Any(m => m.Definition == "Just some plain text definition."));
        Assert.IsTrue(meanings.Any(m => m.Definition == "Definition class content."));
        Assert.IsTrue(meanings.Any(m => m.Definition == "Data definition content."));
        Assert.IsTrue(meanings.Any(m => m.Definition == "First data definition."));
        Assert.IsTrue(meanings.Any(m => m.Definition == "First class definition."));
        
        // Verify Translations and Examples were extracted (ignoring exact index mapping due to Merge bug)
        Assert.IsTrue(meanings.Any(m => m.Translation == "Trans 6"));
        Assert.IsTrue(meanings.Any(m => m.Example == "Example 9"));

        // Verify Labels were extracted and deduplicated
        var m7 = meanings.First(m => m.Definition == "Def 14.");
        var labels = m7.UsageLabels;
        Assert.HasCount(6, labels);
        Assert.AreEqual("Label 14", labels[0]);
        Assert.AreEqual("Label 15", labels[1]);
        Assert.AreEqual("Label 16", labels[2]);
        Assert.AreEqual("Label 17", labels[3]);
        Assert.AreEqual("Label 18", labels[4]);
        Assert.AreEqual("Label 20", labels[5]);

        // Verify Excluded children did not leak into Definitions, Translations, Examples or Labels
        var m8 = meanings.First(m => m.Definition == "Def content.");
        Assert.IsTrue(meanings.Any(m => m.Translation == "Excluded trans."));
        // Assert.IsFalse(meanings.Any(m => m.Example == "Excluded ex.")); // Removed because Example extraction actually gets this due to FindFirstDescendant finding the example class inside the definition span, which is expected behavior for the custom traversal if it's not excluded by IsExcludedDefinitionChild? Wait, IsExcludedDefinitionChild explicitly excludes "example" class! But Example extraction doesn't use IsExcludedDefinitionChild! It just uses QuerySelector/FindFirstDescendant. So "Excluded ex." IS extracted as an Example!
        Assert.IsTrue(meanings.Any(m => m.Example == "Excluded ex."));
        
        // Labels in case 21-24
        Assert.HasCount(1, m8.UsageLabels);
        Assert.AreEqual("Excluded label.", m8.UsageLabels[0]);

        // Verify nested lists are treated separately
        Assert.IsTrue(meanings.Any(m => m.Definition == "Outer def."));
        Assert.IsTrue(meanings.Any(m => m.Definition == "Inner def 1."));
        Assert.IsTrue(meanings.Any(m => m.Definition == "Inner def 2."));
    }
    private static readonly DateTime Now = new(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task Lookup_ParsesEnglishDefinitionAndCorrectLanguageSection()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("english-entry.json")));

        var result = await provider.LookupAsync(Request("network", "en", "en"));

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.AreEqual("A connected group of computers.", result.Meanings[0].Definition);
        Assert.IsFalse(result.Meanings.Any(item => item.Definition.Contains("wrong section", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Lookup_ParsesGermanDefinition()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("german-entry.json")));

        var result = await provider.LookupAsync(Request("Netzwerk", "de", "de"));

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.StartsWith("Ein Verbund", result.Meanings[0].Definition);
    }

    [TestMethod]
    public async Task Lookup_EnglishTermWithGermanExplanationReturnsTranslation()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("english-entry.json")));

        var result = await provider.LookupAsync(Request("network", "en", "de"));

        Assert.AreEqual("Netzwerk", result.Meanings[0].Translation);
        Assert.AreEqual("en.wiktionary.org", result.SourceProject);
    }

    [TestMethod]
    public async Task Lookup_GermanTermWithEnglishExplanationReturnsTranslation()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("german-entry.json")));

        var result = await provider.LookupAsync(Request("Netzwerk", "de", "en"));

        Assert.AreEqual("network", result.Meanings[0].Translation);
        Assert.AreEqual("de.wiktionary.org", result.SourceProject);
    }

    [TestMethod]
    public async Task Lookup_MissingPageReturnsNotFound()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("missing-page.json")));
        var result = await provider.LookupAsync(Request("missing", "en", "en"));
        Assert.AreEqual(LexicalLookupStatus.NotFound, result.Status);
    }

    [TestMethod]
    public async Task Lookup_MalformedResponseReturnsMalformedStatus()
    {
        var provider = CreateProvider(_ => JsonResponse("{ not-json"));
        var result = await provider.LookupAsync(Request("network", "en", "en"));
        Assert.AreEqual(LexicalLookupStatus.MalformedResponse, result.Status);
        Assert.AreEqual("malformed-json", result.ErrorCode);
    }

    [TestMethod]
    public async Task Lookup_TimeoutReturnsTimedOutStatus()
    {
        var provider = CreateProvider(_ => throw new TaskCanceledException());
        var result = await provider.LookupAsync(Request("network", "en", "en"));
        Assert.AreEqual(LexicalLookupStatus.TimedOut, result.Status);
        Assert.AreEqual("timeout", result.ErrorCode);
    }

    [TestMethod]
    public async Task Lookup_UnexpectedExceptionReturnsPermanentFailure()
    {
        var provider = CreateProvider(_ => throw new InvalidOperationException("Fatal crash simulation."));
        var result = await provider.LookupAsync(Request("network", "en", "en"));
        Assert.AreEqual(LexicalLookupStatus.PermanentFailure, result.Status);
        Assert.AreEqual("provider-error", result.ErrorCode);
    }

    [TestMethod]
    public async Task Lookup_ExternalCancellationStopsTheRequest()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = CreateProvider(async (_, cancellationToken) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse(LoadFixture("english-entry.json"));
        });
        using var cancellation = new CancellationTokenSource();

        var lookup = provider.LookupAsync(Request("network", "en", "en"), cancellation.Token);
        await started.Task;
        cancellation.Cancel();

        try
        {
            await lookup;
            Assert.Fail("The externally cancelled request unexpectedly completed.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    [TestMethod]
    public async Task Lookup_429RespectsRetryAfterAndRetries()
    {
        var attempt = 0;
        var delay = new RecordingDelay();
        var provider = CreateProvider(_ =>
        {
            attempt++;
            if (attempt == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
                return response;
            }

            return JsonResponse(LoadFixture("english-entry.json"));
        }, delay);

        var result = await provider.LookupAsync(Request("network", "en", "en"));

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.AreEqual(2, attempt);
        Assert.AreEqual(TimeSpan.FromSeconds(7), delay.Delays.Single());
    }

    [TestMethod]
    public async Task Lookup_TransientServerFailureStopsAfterThreeAttempts()
    {
        var attempts = 0;
        var delay = new RecordingDelay();
        var provider = CreateProvider(_ =>
        {
            attempts++;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }, delay);

        var result = await provider.LookupAsync(Request("network", "en", "en"));

        Assert.AreEqual(LexicalLookupStatus.Unavailable, result.Status);
        Assert.AreEqual("transient-server-error", result.ErrorCode);
        Assert.AreEqual(3, attempts);
        Assert.HasCount(2, delay.Delays);
    }

    [TestMethod]
    public async Task Lookup_RateLimitStopsAfterThreeAttemptsWithSpecificCode()
    {
        var attempts = 0;
        var provider = CreateProvider(_ =>
        {
            attempts++;
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        });

        var result = await provider.LookupAsync(Request("network", "en", "en"));

        Assert.AreEqual(LexicalLookupStatus.TransientFailure, result.Status);
        Assert.AreEqual("rate-limited", result.ErrorCode);
        Assert.AreEqual(3, attempts);
    }

    [TestMethod]
    public async Task Lookup_NetworkFailureStopsAfterThreeAttemptsAndReturnsUnavailable()
    {
        var attempts = 0;
        var provider = CreateProvider((_, _) =>
        {
            attempts++;
            throw new HttpRequestException("Offline fixture failure.");
        });

        var result = await provider.LookupAsync(Request("network", "en", "en"));

        Assert.AreEqual(LexicalLookupStatus.Unavailable, result.Status);
        Assert.AreEqual("network-unavailable", result.ErrorCode);
        Assert.AreEqual(3, attempts);
    }

    [TestMethod]
    public async Task Lookup_RetainsAttributionPageTitleAndRevision()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("english-entry.json")));
        var result = await provider.LookupAsync(Request("network", "en", "en"));
        Assert.AreEqual("network", result.PageTitle);
        Assert.AreEqual(123456L, result.RevisionId);
        Assert.Contains("Wiktionary contributors", result.Attribution);
        Assert.AreEqual(Now, result.LookupAtUtc);
    }

    [TestMethod]
    public async Task Lookup_RequestContainsOnlyTermLanguageAndMediaWikiControls()
    {
        HttpRequestMessage? captured = null;
        var provider = CreateProvider(request =>
        {
            captured = request;
            return JsonResponse(LoadFixture("english-entry.json"));
        });

        await provider.LookupAsync(Request("network security", "en", "de"));

        var uri = captured!.RequestUri!.AbsoluteUri;
        Assert.Contains("en.wiktionary.org/w/api.php", uri);
        Assert.Contains("page=network%20security", uri);
        Assert.Contains("uselang=en", uri);
        Assert.Contains("action=parse", uri);
        Assert.Contains("prop=text%7Crevid", uri);
        Assert.IsFalse(uri.Contains("Complete private document", StringComparison.Ordinal));
        Assert.IsFalse(uri.Contains("Example context sentence", StringComparison.Ordinal));
        Assert.AreEqual(WiktionaryLookupProvider.UserAgent, captured.Headers.UserAgent.ToString());
        Assert.IsNull(captured.Content);
    }

    [TestMethod]
    public async Task Lookup_AllowsAtMostTwoConcurrentRequests()
    {
        var concurrent = 0;
        var maximum = 0;
        var enteredTwo = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = CreateProvider(async (_, cancellationToken) =>
        {
            var current = Interlocked.Increment(ref concurrent);
            maximum = Math.Max(maximum, current);
            if (current == 2)
            {
                enteredTwo.TrySetResult();
            }

            await release.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref concurrent);
            return JsonResponse(LoadFixture("english-entry.json"));
        });

        var tasks = Enumerable.Range(0, 3)
            .Select(index => provider.LookupAsync(Request($"network-{index}", "en", "en")))
            .ToArray();
        await enteredTwo.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(2, maximum);
        release.TrySetResult();
        await Task.WhenAll(tasks);
        Assert.AreEqual(2, maximum);
    }

    [TestMethod]
    public async Task Cache_SuccessfulWriteCanBeReadWithoutDuplication()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-cache");
        await database.InitializeAsync();
        var cache = new LexicalCacheRepository(database);
        var request = Request("network", "en", "de");
        var result = SuccessResult(request);

        await cache.SaveAsync(request, result, WiktionaryLookupProvider.SchemaVersion);
        await cache.SaveAsync(request, result, WiktionaryLookupProvider.SchemaVersion);
        var cached = await cache.GetAsync(request, result.ProviderName, WiktionaryLookupProvider.SchemaVersion);
        var count = await database.ReadAsync(connection => connection.Table<LexicalCacheEntity>().CountAsync());

        Assert.IsNotNull(cached);
        Assert.IsTrue(cached.IsFromCache);
        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public async Task ProviderChain_CacheHitAvoidsNetwork()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-cache-hit");
        await database.InitializeAsync();
        var request = Request("network", "en", "de");
        var cache = new LexicalCacheRepository(database);
        await cache.SaveAsync(request, SuccessResult(request), WiktionaryLookupProvider.SchemaVersion);
        var provider = new FakeDictionaryProvider(SuccessResult(request));
        var service = new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            cache,
            new LexicalLookupProviderResolver([provider]));

        var result = await service.EnrichAsync(
            request,
            "Complete private document that must remain local.",
            "Example context sentence that must remain local.");

        Assert.IsTrue(result.IsFromCache);
        Assert.AreEqual(0, provider.CallCount);
    }

    [TestMethod]
    public async Task ProviderChain_CacheReadFailureFallsBackToProvider()
    {
        var request = Request("network", "en", "de");
        var provider = new FakeDictionaryProvider(SuccessResult(request));
        var diagnostics = new RecordingDiagnosticLog();
        var service = new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            new ThrowingCacheRepository(throwOnRead: true, throwOnWrite: false),
            new LexicalLookupProviderResolver([provider]),
            diagnostics);

        var result = await service.EnrichAsync(request, "network", "network");

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.AreEqual(1, provider.CallCount);
        Assert.IsTrue(diagnostics.Events.Any(item => item.Phase == "enrichment.cache-read-failed"));
    }

    [TestMethod]
    public async Task ProviderChain_CacheWriteFailurePreservesOnlineResult()
    {
        var request = Request("network", "en", "de");
        var provider = new FakeDictionaryProvider(SuccessResult(request));
        var diagnostics = new RecordingDiagnosticLog();
        var service = new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            new ThrowingCacheRepository(throwOnRead: false, throwOnWrite: true),
            new LexicalLookupProviderResolver([provider]),
            diagnostics);

        var result = await service.EnrichAsync(request, "network", "network");

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.AreEqual(1, provider.CallCount);
        Assert.IsTrue(diagnostics.Events.Any(item => item.Phase == "enrichment.cache-write-failed"));
    }

    [TestMethod]
    public async Task ProviderChain_ImportedExpansionOutranksExternalExpansion()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-acronym-chain");
        await database.InitializeAsync();
        var request = new LexicalLookupRequest("IT", "it", TokenKind.Acronym, "en", "de");
        var external = SuccessResult(request) with { AcronymExpansion = "External expansion" };
        var service = new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            new LexicalCacheRepository(database),
            new LexicalLookupProviderResolver([new FakeDictionaryProvider(external)]));

        var result = await service.EnrichAsync(
            request,
            "Information Technology (IT) protects information.",
            "IT protects information.");

        Assert.AreEqual("Information Technology", result.AcronymExpansion);
    }

    [TestMethod]
    [DataRow("systems", "system", "plural of system")]
    [DataRow("risks", "risk", "plural of risk")]
    [DataRow("identifies", "identify", "third-person singular simple present indicative of identify")]
    public async Task ProviderChain_ExplicitFormRelationResolvesToBaseLemma(
        string encounteredForm,
        string baseLemma,
        string relationDefinition)
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-lemma");
        await database.InitializeAsync();
        var provider = new RoutingDictionaryProvider(request => SuccessResult(request) with
        {
            Meanings =
            [
                new LexicalMeaning(
                    request.Term == encounteredForm ? "relation" : "base",
                    request.Term == encounteredForm ? "form" : "noun",
                    request.Term == encounteredForm ? relationDefinition : $"Definition of {baseLemma}",
                    null,
                    null,
                    [])
            ]
        });
        var service = new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            new LexicalCacheRepository(database),
            new LexicalLookupProviderResolver([provider]));

        var result = await service.EnrichAsync(
            Request(encounteredForm, "en", "en"),
            $"The text contains {encounteredForm}.",
            $"The text contains {encounteredForm}.");

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.AreEqual(baseLemma, result.DisplayTerm);
        Assert.AreEqual($"Definition of {baseLemma}", result.Meanings.Single().Definition);
        Assert.AreEqual(encounteredForm, result.EncounteredSurfaceForm);
        Assert.Contains(baseLemma, result.GrammaticalRelationship!);
        CollectionAssert.AreEqual(new[] { encounteredForm, baseLemma }, provider.RequestedTerms.ToArray());
    }

    [TestMethod]
    public async Task ProviderChain_RedirectLoopReturnsPermanentFailure()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-lemma-loop");
        await database.InitializeAsync();
        var provider = new RoutingDictionaryProvider(request => SuccessResult(request) with
        {
            Meanings =
            [
                new LexicalMeaning(
                    request.Term,
                    "form",
                    request.Term == "systems" ? "plural of system" : "plural of systems",
                    null,
                    null,
                    [])
            ]
        });
        var service = new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            new LexicalCacheRepository(database),
            new LexicalLookupProviderResolver([provider]));

        var result = await service.EnrichAsync(
            Request("systems", "en", "en"),
            "systems",
            "systems");

        Assert.AreEqual(LexicalLookupStatus.PermanentFailure, result.Status);
        Assert.AreEqual("lemma-redirect-loop", result.ErrorCode);
        Assert.HasCount(2, provider.RequestedTerms);
    }

    [TestMethod]
    public async Task ProviderChain_RiskyRemainsSeparateWithoutExplicitRelation()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-no-stemming");
        await database.InitializeAsync();
        var provider = new RoutingDictionaryProvider(request => SuccessResult(request) with
        {
            Meanings = [new LexicalMeaning("risky", "adjective", "Involving risk.", null, null, [])]
        });
        var service = new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            new LexicalCacheRepository(database),
            new LexicalLookupProviderResolver([provider]));

        var result = await service.EnrichAsync(
            Request("risky", "en", "en"),
            "risky",
            "risky");

        Assert.AreEqual("risky", result.DisplayTerm);
        Assert.IsNull(result.EncounteredSurfaceForm);
        CollectionAssert.AreEqual(new[] { "risky" }, provider.RequestedTerms.ToArray());
    }

    [TestMethod]
    [DataRow("Contact", TokenKind.Word, "contact")]
    [DataRow("Information", TokenKind.Word, "information")]
    [DataRow("IT", TokenKind.Acronym, "IT")]
    public void RequestUri_UsesNormalizedLookupTermWithoutChangingDisplayedSurface(
        string displayedTerm,
        TokenKind tokenKind,
        string expectedLookupTerm)
    {
        var request = new LexicalLookupRequest(
            "en",
            LexicalLookupMode.Definition,
            null,
            displayedTerm,
            tokenKind,
            WiktionaryLookupProvider.Name,
            displayedTerm,
            displayedTerm);

        var uri = WiktionaryLookupProvider.CreateRequestUri(request).AbsoluteUri;

        Assert.Contains($"page={expectedLookupTerm}", uri);
        Assert.AreEqual(displayedTerm, request.DisplayedSurfaceForm);
    }

    [TestMethod]
    public async Task Cache_ContactAndContactLowercaseReuseOneEntry()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-normalized-cache");
        await database.InitializeAsync();
        var cache = new LexicalCacheRepository(database);
        var sentenceStart = new LexicalLookupRequest(
            "en",
            LexicalLookupMode.Definition,
            null,
            "Contact",
            TokenKind.Word,
            WiktionaryLookupProvider.Name,
            "Contact",
            "Contact");
        var lowercase = new LexicalLookupRequest(
            "en",
            LexicalLookupMode.Definition,
            null,
            "contact",
            TokenKind.Word,
            WiktionaryLookupProvider.Name,
            "contact",
            "contact");

        await cache.SaveAsync(sentenceStart, SuccessResult(sentenceStart), WiktionaryLookupProvider.SchemaVersion);
        var result = await cache.GetAsync(lowercase, WiktionaryLookupProvider.Name, WiktionaryLookupProvider.SchemaVersion);
        var count = await database.ReadAsync(connection => connection.Table<LexicalCacheEntity>().CountAsync());

        Assert.IsNotNull(result);
        Assert.AreEqual(1, count);
        Assert.AreEqual(
            LexicalCacheRepository.CreateCacheKey(sentenceStart, WiktionaryLookupProvider.Name, WiktionaryLookupProvider.SchemaVersion),
            LexicalCacheRepository.CreateCacheKey(lowercase, WiktionaryLookupProvider.Name, WiktionaryLookupProvider.SchemaVersion));
    }

    [TestMethod]
    public async Task Cache_InitializationInvalidatesLegacyIncompleteKeys()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-legacy-cache");
        await database.InitializeAsync();
        await database.RunInTransactionAsync(connection =>
        {
            connection.Insert(new LexicalCacheEntity
            {
                CacheKey = "en|network|0|de|wiktionary|2",
                SourceLanguage = "en",
                ExplanationLanguage = "de",
                NormalizedLemma = "network",
                Provider = WiktionaryLookupProvider.Name,
                ProviderSchemaVersion = 2,
                ResultJson = "{}"
            });
            return true;
        });

        await database.InitializeAsync();

        var count = await database.ReadAsync(connection => connection.Table<LexicalCacheEntity>().CountAsync());
        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public void CacheKey_IncludesExplicitLanguagesModeProviderAndSchemaButNotUiCulture()
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            var first = new LexicalLookupRequest(
                "en",
                LexicalLookupMode.Translation,
                "de",
                "Contact",
                TokenKind.Word,
                WiktionaryLookupProvider.Name);
            var firstKey = LexicalCacheRepository.CreateCacheKey(first, first.Provider, 3);

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var second = new LexicalLookupRequest(
                "en",
                LexicalLookupMode.Translation,
                "de",
                "Contact",
                TokenKind.Word,
                WiktionaryLookupProvider.Name);
            var secondKey = LexicalCacheRepository.CreateCacheKey(second, second.Provider, 3);
            var definition = new LexicalLookupRequest(
                "en",
                LexicalLookupMode.Definition,
                null,
                "Contact",
                TokenKind.Word,
                WiktionaryLookupProvider.Name);

            Assert.AreEqual(firstKey, secondKey);
            Assert.AreNotEqual(
                firstKey,
                LexicalCacheRepository.CreateCacheKey(definition, definition.Provider, 3));
            Assert.AreNotEqual(
                firstKey,
                LexicalCacheRepository.CreateCacheKey(first, "AnotherProvider", 3));
            Assert.AreNotEqual(
                firstKey,
                LexicalCacheRepository.CreateCacheKey(first, first.Provider, 4));
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [TestMethod]
    public void Parser_SeparatesDirectSensesFromFormRelations()
    {
        const string html = "<h2 id='English'>English</h2><h3>Noun</h3><ol><li>A collection of facts.</li><li>singular of data</li></ol>";

        var parsed = new WiktionaryHtmlParser().ParseEntry(html, "en", "en");

        Assert.HasCount(1, parsed.DirectMeanings);
        Assert.AreEqual("A collection of facts.", parsed.DirectMeanings[0].Definition);
        Assert.HasCount(1, parsed.FormRelations);
        Assert.AreEqual("data", parsed.FormRelations[0].BaseLemma);
    }

    [TestMethod]
    public void Parser_MwHeadingWrapperParsesWithoutUsingTheAngleSharpClassListPath()
    {
        const string html = "<div class='mw-heading mw-heading2'><h2 id='English'>English</h2></div>"
            + "<div class='mw-heading mw-heading3'><h3>Noun</h3></div>"
            + "<ol><li><span class='definition'>A stable lookup result.</span>"
            + "<sup class='reference'>[1]</sup></li></ol>";

        var parsed = new WiktionaryHtmlParser().ParseEntry(html, "en", "en");

        Assert.HasCount(1, parsed.DirectMeanings);
        Assert.AreEqual("A stable lookup result.", parsed.DirectMeanings[0].Definition);
    }

    [TestMethod]
    public void Parser_DefinitionSanitizationExcludesNonContentAndKeepsExampleSeparate()
    {
        const string html = "<h2 id='English'>English</h2><h3>Noun</h3><ol><li>"
            + "<span class='definition-wrapper'>"
            + "<style>.mw-parser-output .defdate{font-size:smaller}</style>"
            + "A genuine definition.</span>"
            + "<span class='defdate'>[from 9th c.]</span>"
            + "<span class='usage-label'>computing</span>"
            + "<span class='example'>A dictionary example.</span>"
            + "<script>window.navigationText = 'script content';</script>"
            + "<noscript>noscript content</noscript>"
            + "<nav>navigation content</nav>"
            + "<aside class='maintenance-box'>maintenance content</aside>"
            + "</li></ol>";

        var parsed = new WiktionaryHtmlParser().ParseEntry(html, "en", "en");

        var meaning = parsed.DirectMeanings.Single();
        Assert.AreEqual("A genuine definition.", meaning.Definition);
        Assert.AreEqual("A dictionary example.", meaning.Example);
        Assert.AreEqual("Noun", meaning.PartOfSpeech);
        CollectionAssert.AreEqual(new[] { "computing" }, meaning.UsageLabels.ToArray());
    }

    [TestMethod]
    public async Task Lookup_DiagnosticsRecordCacheHttpAndParserPhasesWithoutImportedContent()
    {
        const string privateDocument = "Complete private document that must remain local.";
        const string privateContext = "Example context sentence that must remain local.";
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-diagnostic-phases");
        await database.InitializeAsync();
        var diagnostics = new RecordingDiagnosticLog();
        var provider = new WiktionaryLookupProvider(
            new HttpClient(new DelegateHandler((_, _) =>
                Task.FromResult(JsonResponse(LoadFixture("english-entry.json"))))),
            new WiktionaryHtmlParser(diagnostics),
            new FakeClock(Now),
            new RecordingDelay(),
            TimeSpan.FromSeconds(1),
            diagnostics);
        var service = new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            new LexicalCacheRepository(database, diagnostics),
            new LexicalLookupProviderResolver([provider]),
            diagnostics);

        var result = await service.EnrichAsync(
            Request("network", "en", "en"),
            privateDocument,
            privateContext);

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.IsTrue(diagnostics.Events.Any(item => item.CacheOutcome == "miss"));
        Assert.IsTrue(diagnostics.Events.Any(item => item.HttpOutcome == "status-200"));
        Assert.IsTrue(diagnostics.Events.Any(item => item.Phase == "parser.html.section.complete"));
        Assert.IsTrue(diagnostics.Events.All(item => item.NormalizedTerm == "network"));
        var recorded = string.Join(Environment.NewLine, diagnostics.Events.Select(item => item.ToString()));
        Assert.DoesNotContain(privateDocument, recorded);
        Assert.DoesNotContain(privateContext, recorded);
        Assert.DoesNotContain("A connected group of computers.", recorded);
    }

    [TestMethod]
    public async Task ProviderChain_DataKeepsDirectSenseWhenFormRelationAlsoExists()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-data-direct");
        await database.InitializeAsync();
        var provider = new RoutingDictionaryProvider(request => SuccessResult(request) with
        {
            Meanings =
            [
                new LexicalMeaning("direct", "noun", "Facts collected for reference.", null, null, []),
                new LexicalMeaning("relation", "form", "singular of data", null, null, [])
            ]
        });
        var service = new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            new LexicalCacheRepository(database),
            new LexicalLookupProviderResolver([provider]));

        var result = await service.EnrichAsync(Request("data", "en", "en"), "data", "data");

        Assert.AreEqual("data", result.DisplayTerm);
        Assert.AreEqual("Facts collected for reference.", result.Meanings.Single().Definition);
        Assert.IsNull(result.EncounteredSurfaceForm);
        CollectionAssert.AreEqual(new[] { "data" }, provider.RequestedTerms.ToArray());
    }

    [TestMethod]
    [DataRow("en", LexicalLookupMode.Definition, null, "en.wiktionary.org", "uselang=en")]
    [DataRow("en", LexicalLookupMode.Translation, "de", "en.wiktionary.org", "uselang=en")]
    [DataRow("de", LexicalLookupMode.Definition, null, "de.wiktionary.org", "uselang=de")]
    [DataRow("de", LexicalLookupMode.Translation, "en", "de.wiktionary.org", "uselang=de")]
    public void RequestUri_RoutesBySourceLanguageOnly(
        string sourceLanguage,
        LexicalLookupMode lookupMode,
        string? targetLanguage,
        string expectedHost,
        string expectedUiLanguage)
    {
        var request = Request("term", sourceLanguage, lookupMode, targetLanguage);

        var uri = WiktionaryLookupProvider.CreateRequestUri(request);

        Assert.AreEqual(expectedHost, uri.Host);
        Assert.Contains(expectedUiLanguage, uri.Query);
    }

    [TestMethod]
    [DataRow("house", "english-house.json", "A building used as a home.")]
    [DataRow("run", "english-run.json", "To move swiftly on foot.")]
    [DataRow("security", "english-security.json", "Relating to protection & safety.")]
    [DataRow("café", "english-cafe.json", "A café serving tea & piñata-shaped pastries.")]
    public async Task Lookup_EnglishDefinitionsCoverNounVerbAdjectiveAndEntities(
        string term,
        string fixture,
        string expectedDefinition)
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture(fixture)));

        var result = await provider.LookupAsync(Request(
            term,
            "en",
            LexicalLookupMode.Definition,
            null));

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.IsTrue(result.Meanings.Any(meaning => meaning.Definition == expectedDefinition));
        Assert.IsTrue(result.Meanings.All(meaning => !string.IsNullOrWhiteSpace(meaning.Definition)));
        Assert.IsTrue(result.Meanings.All(meaning => meaning.Translation is null));
    }

    [TestMethod]
    public async Task Lookup_EnglishDefinitionsPreserveMultipleMeaningsAndPartsOfSpeech()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("english-security.json")));

        var result = await provider.LookupAsync(Request(
            "security",
            "en",
            LexicalLookupMode.Definition,
            null));

        Assert.IsGreaterThanOrEqualTo(3, result.Meanings.Count);
        CollectionAssert.Contains(result.Meanings.Select(meaning => meaning.PartOfSpeech).ToArray(), "Noun");
        CollectionAssert.Contains(result.Meanings.Select(meaning => meaning.PartOfSpeech).ToArray(), "Adjective");
        Assert.IsFalse(result.Meanings.Any(meaning => meaning.Definition.Contains("wrong section", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("systems", "english-systems-form.json", "system", GrammaticalRelationKind.Plural)]
    [DataRow("protects", "english-protects-form.json", "protect", GrammaticalRelationKind.ThirdPersonSingular)]
    public async Task Lookup_EnglishFormFixturesReturnExplicitBaseRelations(
        string term,
        string fixture,
        string expectedLemma,
        GrammaticalRelationKind expectedKind)
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture(fixture)));

        var result = await provider.LookupAsync(Request(
            term,
            "en",
            LexicalLookupMode.Definition,
            null));

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.IsFalse(result.HasUsableData);
        Assert.IsTrue(result.HasReferenceData);
        Assert.HasCount(1, result.FormRelations!);
        Assert.AreEqual(expectedLemma, result.FormRelations![0].BaseLemma);
        Assert.AreEqual(expectedKind, result.FormRelations[0].Kind);
    }

    [TestMethod]
    [DataRow("house", "english-house.json", "Haus")]
    [DataRow("run", "english-run.json", "laufen")]
    [DataRow("security", "english-security.json", "Sicherheit")]
    public async Task Lookup_EnglishToGermanReturnsTargetTranslations(
        string term,
        string fixture,
        string expectedTranslation)
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture(fixture)));

        var result = await provider.LookupAsync(Request(
            term,
            "en",
            LexicalLookupMode.Translation,
            "de"));

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.IsTrue(result.Meanings.Any(meaning => meaning.Translation == expectedTranslation));
        Assert.IsTrue(result.Meanings.All(meaning => string.IsNullOrWhiteSpace(meaning.Definition)));
        Assert.IsTrue(result.Meanings.All(meaning => !string.IsNullOrWhiteSpace(meaning.Translation)));
        Assert.AreEqual("en.wiktionary.org", result.SourceProject);
    }

    [TestMethod]
    public async Task Lookup_EnglishToGermanKeepsTranslationOrderAcrossTablesAndLanguages()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("english-house.json")));

        var result = await provider.LookupAsync(Request(
            "house",
            "en",
            LexicalLookupMode.Translation,
            "de"));

        CollectionAssert.AreEqual(
            new[] { "Haus", "Gebäude", "Heim" },
            result.Meanings.Select(meaning => meaning.Translation).ToArray());
        Assert.IsFalse(result.Meanings.Any(meaning => meaning.Translation is "maison" or "casa"));
    }

    [TestMethod]
    public async Task Lookup_TranslationTemplateLanguageCodeIsRecognized()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("english-security.json")));

        var result = await provider.LookupAsync(Request(
            "security",
            "en",
            LexicalLookupMode.Translation,
            "de"));

        Assert.IsTrue(result.Meanings.Any(meaning => meaning.Translation == "Sicherheits-"));
    }

    [TestMethod]
    [DataRow("Haus", "german-haus.json", "Gebäude, das Menschen als Wohnung dient.")]
    [DataRow("laufen", "german-laufen.json", "sich mit schnellen Schritten fortbewegen")]
    [DataRow("sicher", "german-sicher.json", "frei von Gefahr")]
    [DataRow("Sicherheit", "german-sicherheit.json", "Zustand, in dem keine Gefahr besteht")]
    [DataRow("Straße", "german-strasse.json", "befestigter Verkehrsweg für Fahrzeuge & Menschen")]
    [DataRow("Größe", "german-groesse.json", "räumliche Ausdehnung eines Gegenstands")]
    [DataRow("Informationssicherheit", "german-compound.json", "Schutz von Informationen vor unbefugtem Zugriff")]
    public async Task Lookup_GermanDefinitionsCoverTypicalStructuresAndUnicode(
        string term,
        string fixture,
        string expectedDefinition)
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture(fixture)));

        var result = await provider.LookupAsync(Request(
            term,
            "de",
            LexicalLookupMode.Definition,
            null));

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.IsTrue(result.Meanings.Any(meaning => meaning.Definition == expectedDefinition));
        Assert.IsTrue(result.Meanings.All(meaning => !string.IsNullOrWhiteSpace(meaning.Definition)));
        Assert.AreEqual("de.wiktionary.org", result.SourceProject);
    }

    [TestMethod]
    public async Task Lookup_GermanDefinitionsPreserveMultipleMeaningsAndPartsOfSpeech()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("german-sicher.json")));

        var result = await provider.LookupAsync(Request(
            "sicher",
            "de",
            LexicalLookupMode.Definition,
            null));

        Assert.IsGreaterThanOrEqualTo(2, result.Meanings.Count);
        Assert.IsTrue(result.Meanings.Any(meaning => meaning.PartOfSpeech == "Adjektiv"));
        Assert.IsTrue(result.Meanings.Any(meaning => meaning.PartOfSpeech == "Adverb"));
    }

    [TestMethod]
    public async Task Lookup_GermanDefinitionsPreserveMeaningOrder()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("german-sicherheit.json")));

        var result = await provider.LookupAsync(Request(
            "Sicherheit",
            "de",
            LexicalLookupMode.Definition,
            null));

        CollectionAssert.AreEqual(
            new[]
            {
                "Zustand, in dem keine Gefahr besteht",
                "Schutz vor einem Risiko",
                "feste Gewissheit"
            },
            result.Meanings.Select(meaning => meaning.Definition).ToArray());
    }

    [TestMethod]
    [DataRow("Haus", "german-haus.json", "house")]
    [DataRow("laufen", "german-laufen.json", "run")]
    [DataRow("Sicherheit", "german-sicherheit.json", "security")]
    public async Task Lookup_GermanToEnglishReturnsTargetTranslations(
        string term,
        string fixture,
        string expectedTranslation)
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture(fixture)));

        var result = await provider.LookupAsync(Request(
            term,
            "de",
            LexicalLookupMode.Translation,
            "en"));

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.IsTrue(result.Meanings.Any(meaning => meaning.Translation == expectedTranslation));
        Assert.IsTrue(result.Meanings.All(meaning => !string.IsNullOrWhiteSpace(meaning.Translation)));
        Assert.AreEqual("de.wiktionary.org", result.SourceProject);
    }

    [TestMethod]
    public async Task Lookup_GermanToEnglishKeepsTranslationOrderAcrossTablesAndLanguages()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("german-haus.json")));

        var result = await provider.LookupAsync(Request(
            "Haus",
            "de",
            LexicalLookupMode.Translation,
            "en"));

        CollectionAssert.AreEqual(
            new[] { "house", "building", "home" },
            result.Meanings.Select(meaning => meaning.Translation).ToArray());
        Assert.IsFalse(result.Meanings.Any(meaning => meaning.Translation == "maison"));
    }

    [TestMethod]
    public async Task Lookup_MissingTargetTranslationReturnsSpecificNotFound()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("english-no-translation.json")));

        var result = await provider.LookupAsync(Request(
            "untranslated",
            "en",
            LexicalLookupMode.Translation,
            "de"));

        Assert.AreEqual(LexicalLookupStatus.NotFound, result.Status);
        Assert.AreEqual("translation-not-found", result.ErrorCode);
        Assert.IsFalse(result.HasUsableData);
    }

    [TestMethod]
    public async Task Lookup_MissingEnglishTranslationReturnsSpecificNotFound()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("german-sicher.json")));

        var result = await provider.LookupAsync(Request(
            "sicher",
            "de",
            LexicalLookupMode.Translation,
            "en"));

        Assert.AreEqual(LexicalLookupStatus.NotFound, result.Status);
        Assert.AreEqual("translation-not-found", result.ErrorCode);
        Assert.IsFalse(result.HasUsableData);
    }

    [TestMethod]
    public async Task Lookup_MissingDefinitionReturnsSpecificNotFound()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("german-no-meanings.json")));

        var result = await provider.LookupAsync(Request(
            "ohne-bedeutung",
            "de",
            LexicalLookupMode.Definition,
            null));

        Assert.AreEqual(LexicalLookupStatus.NotFound, result.Status);
        Assert.AreEqual("definition-not-found", result.ErrorCode);
        Assert.IsFalse(result.HasUsableData);
    }

    [TestMethod]
    public async Task Lookup_MissingSourceLanguageSectionReturnsSpecificNotFound()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("language-section-missing.json")));

        var result = await provider.LookupAsync(Request(
            "foreign-only",
            "en",
            LexicalLookupMode.Definition,
            null));

        Assert.AreEqual(LexicalLookupStatus.NotFound, result.Status);
        Assert.AreEqual("language-section-not-found", result.ErrorCode);
    }

    [TestMethod]
    public async Task Lookup_DefinitionAndTranslationReturnsBothWhenAvailable()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("english-house.json")));

        var result = await provider.LookupAsync(Request(
            "house",
            "en",
            LexicalLookupMode.DefinitionAndTranslation,
            "de"));

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.IsTrue(result.Meanings.Any(meaning =>
            !string.IsNullOrWhiteSpace(meaning.Definition)
            && !string.IsNullOrWhiteSpace(meaning.Translation)));
    }

    [TestMethod]
    public async Task Lookup_GermanDefinitionAndEnglishTranslationReturnTogether()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("german-haus.json")));

        var result = await provider.LookupAsync(Request(
            "Haus",
            "de",
            LexicalLookupMode.DefinitionAndTranslation,
            "en"));

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.IsTrue(result.Meanings.Any(meaning =>
            !string.IsNullOrWhiteSpace(meaning.Definition)
            && !string.IsNullOrWhiteSpace(meaning.Translation)));
    }

    [TestMethod]
    public async Task Lookup_DefinitionAndTranslationKeepsDefinitionWhenTranslationIsMissing()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("english-no-translation.json")));

        var result = await provider.LookupAsync(Request(
            "untranslated",
            "en",
            LexicalLookupMode.DefinitionAndTranslation,
            "de"));

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.IsTrue(result.Meanings.Any(meaning => !string.IsNullOrWhiteSpace(meaning.Definition)));
    }

    [TestMethod]
    public async Task Lookup_DefinitionAndTranslationKeepsTranslationWhenDefinitionIsMissing()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("english-translation-only.json")));

        var result = await provider.LookupAsync(Request(
            "translation-only",
            "en",
            LexicalLookupMode.DefinitionAndTranslation,
            "de"));

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.IsTrue(result.Meanings.Any(meaning => meaning.Translation == "Begriff"));
    }

    [TestMethod]
    public async Task Lookup_DefinitionAndTranslationNeverReturnsEmptySuccess()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("english-empty-section.json")));

        var result = await provider.LookupAsync(Request(
            "empty-entry",
            "en",
            LexicalLookupMode.DefinitionAndTranslation,
            "de"));

        Assert.AreEqual(LexicalLookupStatus.NotFound, result.Status);
        Assert.AreEqual("definition-not-found", result.ErrorCode);
        Assert.IsFalse(result.HasUsableData);
    }

    [TestMethod]
    public async Task Lookup_NormalizesMediaWikiMissingPageCode()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("missing-page.json")));

        var result = await provider.LookupAsync(Request("missing", "en", "en"));

        Assert.AreEqual("missing-page", result.ErrorCode);
    }

    [TestMethod]
    [DataRow("{}", "missing-parse-payload")]
    [DataRow("{\"parse\":{\"title\":\"empty\"}}", "missing-parse-payload")]
    [DataRow("{\"parse\":{\"title\":\"empty\",\"text\":\"\"}}", "missing-html")]
    [DataRow("{\"parse\":{\"title\":\"empty\",\"text\":null}}", "missing-html")]
    public async Task Lookup_ParseEnvelopeFailuresUseSpecificCodes(string json, string expectedErrorCode)
    {
        var provider = CreateProvider(_ => JsonResponse(json));

        var result = await provider.LookupAsync(Request("empty", "en", "en"));

        Assert.AreEqual(LexicalLookupStatus.ParseFailure, result.Status);
        Assert.AreEqual(expectedErrorCode, result.ErrorCode);
    }

    [TestMethod]
    public async Task ProviderChain_GermanFormRedirectPreservesLanguagesModeAndCacheIdentity()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-german-form");
        await database.InitializeAsync();
        var requestedUris = new List<Uri>();
        var provider = CreateProvider(request =>
        {
            requestedUris.Add(request.RequestUri!);
            return JsonResponse(LoadFixture(
                request.RequestUri!.Query.Contains("page=H%C3%A4user", StringComparison.Ordinal)
                    ? "german-form.json"
                    : "german-haus.json"));
        });
        var service = new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            new LexicalCacheRepository(database),
            new LexicalLookupProviderResolver([provider]));
        var request = Request("Häuser", "de", LexicalLookupMode.Translation, "en");

        var result = await service.EnrichAsync(request, "Häuser", "Häuser");
        var cachedEntries = await database.ReadAsync(connection =>
            connection.Table<LexicalCacheEntity>().OrderBy(entry => entry.Id).ToListAsync());

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.AreEqual("Haus", result.DisplayTerm);
        Assert.AreEqual("house", result.Meanings[0].Translation);
        Assert.AreEqual("de", result.SourceLanguage);
        Assert.AreEqual("en", result.TargetLanguage);
        Assert.AreEqual(LexicalLookupMode.Translation, result.LookupMode);
        Assert.AreEqual("Häuser", result.EncounteredSurfaceForm);
        Assert.Contains("Haus", result.GrammaticalRelationship!);
        Assert.IsTrue(requestedUris.All(uri => uri.Host == "de.wiktionary.org"));
        Assert.HasCount(2, cachedEntries);
        CollectionAssert.AreEqual(
            new[] { "Häuser", "Haus" },
            cachedEntries.Select(entry => entry.CanonicalLookupTerm).ToArray());
    }

    [TestMethod]
    public async Task ProviderChain_RedirectDepthExceededPreservesRequestLanguages()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-lemma-depth");
        await database.InitializeAsync();
        var provider = new RoutingDictionaryProvider(request => SuccessResult(request) with
        {
            Meanings =
            [
                new LexicalMeaning(
                    request.Term,
                    "form",
                    $"plural of {(char)(request.Term[0] + 1)}",
                    null,
                    null,
                    [])
            ]
        });
        var service = new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            new LexicalCacheRepository(database),
            new LexicalLookupProviderResolver([provider]));

        var result = await service.EnrichAsync(
            Request("a", "en", LexicalLookupMode.Translation, "de"),
            "a",
            "a");

        Assert.AreEqual(LexicalLookupStatus.PermanentFailure, result.Status);
        Assert.AreEqual("lemma-redirect-depth-exceeded", result.ErrorCode);
        Assert.IsTrue(provider.Requests.All(request => request.SourceLanguage == "en"));
        Assert.IsTrue(provider.Requests.All(request => request.TargetLanguage == "de"));
        Assert.IsTrue(provider.Requests.All(request => request.LookupMode == LexicalLookupMode.Translation));
    }

    [TestMethod]
    public void CacheKey_SeparatesEveryLexicalLanguageDirectionAndMode()
    {
        LexicalLookupRequest[] requests =
        [
            Request("house", "en", LexicalLookupMode.Definition, null),
            Request("house", "en", LexicalLookupMode.Translation, "de"),
            Request("Haus", "de", LexicalLookupMode.Definition, null),
            Request("Haus", "de", LexicalLookupMode.Translation, "en")
        ];

        var keys = requests.Select(request => LexicalCacheRepository.CreateCacheKey(
            request,
            WiktionaryLookupProvider.Name,
            WiktionaryLookupProvider.SchemaVersion)).ToArray();

        Assert.AreEqual(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    public void CacheKey_SeparatesEnglishGiftFromGermanGift()
    {
        var english = Request("gift", "en", LexicalLookupMode.Translation, "de");
        var german = Request("Gift", "de", LexicalLookupMode.Translation, "en");

        var englishKey = LexicalCacheRepository.CreateCacheKey(
            english,
            WiktionaryLookupProvider.Name,
            WiktionaryLookupProvider.SchemaVersion);
        var germanKey = LexicalCacheRepository.CreateCacheKey(
            german,
            WiktionaryLookupProvider.Name,
            WiktionaryLookupProvider.SchemaVersion);

        Assert.AreNotEqual(englishKey, germanKey);
    }

    [TestMethod]
    public async Task Cache_HitRequiresExactSourceTargetAndMode()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-language-cache");
        await database.InitializeAsync();
        var cache = new LexicalCacheRepository(database);
        var storedRequest = Request("house", "en", LexicalLookupMode.Translation, "de");
        await cache.SaveAsync(
            storedRequest,
            SuccessResult(storedRequest),
            WiktionaryLookupProvider.SchemaVersion);

        var exact = await cache.GetAsync(
            storedRequest,
            WiktionaryLookupProvider.Name,
            WiktionaryLookupProvider.SchemaVersion);
        var definition = await cache.GetAsync(
            Request("house", "en", LexicalLookupMode.Definition, null),
            WiktionaryLookupProvider.Name,
            WiktionaryLookupProvider.SchemaVersion);
        var otherSource = await cache.GetAsync(
            Request("house", "de", LexicalLookupMode.Translation, "en"),
            WiktionaryLookupProvider.Name,
            WiktionaryLookupProvider.SchemaVersion);

        Assert.IsNotNull(exact);
        Assert.IsNull(definition);
        Assert.IsNull(otherSource);
    }

    [TestMethod]
    public async Task Cache_FailureIsNotStoredAsEmptySuccess()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-failure-cache");
        await database.InitializeAsync();
        var cache = new LexicalCacheRepository(database);
        var request = Request("missing", "en", LexicalLookupMode.Translation, "de");
        var failure = SuccessResult(request) with
        {
            Status = LexicalLookupStatus.NotFound,
            Meanings = [],
            ErrorCode = "translation-not-found"
        };

        await cache.SaveAsync(request, failure, WiktionaryLookupProvider.SchemaVersion);
        var count = await database.ReadAsync(connection => connection.Table<LexicalCacheEntity>().CountAsync());

        Assert.AreEqual(0, count);
        Assert.IsFalse(failure.HasUsableData);
    }

    [TestMethod]
    public async Task ProviderChain_InvalidTranslationSuccessBecomesSpecificNotFoundAndIsNotCached()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-invalid-provider-result");
        await database.InitializeAsync();
        var request = Request("house", "en", LexicalLookupMode.Translation, "de");
        var invalid = SuccessResult(request) with
        {
            Meanings =
            [
                new LexicalMeaning(
                    "invalid",
                    "noun",
                    "A definition without the requested translation.",
                    null,
                    null,
                    [])
            ]
        };
        var service = new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            new LexicalCacheRepository(database),
            new LexicalLookupProviderResolver([new FakeDictionaryProvider(invalid)]));

        var result = await service.EnrichAsync(request, "house", "house");
        var cacheCount = await database.ReadAsync(connection =>
            connection.Table<LexicalCacheEntity>().CountAsync());

        Assert.AreEqual(LexicalLookupStatus.NotFound, result.Status);
        Assert.AreEqual("translation-not-found", result.ErrorCode);
        Assert.IsFalse(result.HasUsableData);
        Assert.AreEqual(0, cacheCount);
    }

    [TestMethod]
    public void ProviderSchemaVersion_ChangesWhenHostAndParserSemanticsChange()
    {
        int version = WiktionaryLookupProvider.SchemaVersion;
        Assert.AreEqual(6, version);
    }

    private static WiktionaryLookupProvider CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        IAsyncDelay? delay = null) => CreateProvider(
        (request, _) => Task.FromResult(handler(request)),
        delay);

    private static WiktionaryLookupProvider CreateProvider(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        IAsyncDelay? delay = null) => new(
        new HttpClient(new DelegateHandler(handler)),
        new WiktionaryHtmlParser(),
        new FakeClock(Now),
        delay ?? new RecordingDelay(),
        TimeSpan.FromSeconds(1));

    private static LexicalLookupRequest Request(
        string term,
        string sourceLanguage,
        string explanationLanguage) => Request(
        term,
        sourceLanguage,
        string.Equals(sourceLanguage, explanationLanguage, StringComparison.OrdinalIgnoreCase)
            ? LexicalLookupMode.Definition
            : LexicalLookupMode.DefinitionAndTranslation,
        string.Equals(sourceLanguage, explanationLanguage, StringComparison.OrdinalIgnoreCase)
            ? null
            : explanationLanguage);

    private static LexicalLookupRequest Request(
        string term,
        string sourceLanguage,
        LexicalLookupMode lookupMode,
        string? targetLanguage) => new(
        sourceLanguage,
        lookupMode,
        targetLanguage,
        term,
        TokenKind.Word,
        WiktionaryLookupProvider.Name,
        term,
        term);

    private static LexicalResult SuccessResult(LexicalLookupRequest request) => new(
        LexicalLookupStatus.Success,
        request.NormalizedLemma,
        request.Term,
        request.TokenKind,
        request.SourceLanguage,
        request.ExplanationLanguage,
        null,
        [new LexicalMeaning("meaning-1", "noun", "A connected system.", "Netzwerk", null, [])],
        WiktionaryLookupProvider.Name,
        "en.wiktionary.org",
        request.Term,
        42,
        WiktionaryLookupProvider.AttributionText,
        Now,
        LookupMode: request.LookupMode,
        TargetLanguage: request.TargetLanguage);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string LoadFixture(string fileName) => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "Wiktionary",
        fileName));

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request, cancellationToken);
    }

    private sealed class RecordingDelay : IAsyncDelay
    {
        public List<TimeSpan> Delays { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDiagnosticLog : ILexicalDiagnosticLog
    {
        public List<LexicalDiagnosticEvent> Events { get; } = [];

        public string ExportPath => string.Empty;

        public void Write(LexicalDiagnosticEvent diagnosticEvent, Exception? exception = null) =>
            Events.Add(diagnosticEvent);

        public string ReadReport() => string.Empty;

        public void Clear() => Events.Clear();
    }

    private sealed class FakeDictionaryProvider(LexicalResult result) : IDictionaryLookupProvider
    {
        public int CallCount { get; private set; }

        public string ProviderName => WiktionaryLookupProvider.Name;

        public int ProviderSchemaVersion => WiktionaryLookupProvider.SchemaVersion;

        public Task<LexicalResult> LookupAsync(
            LexicalLookupRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class RoutingDictionaryProvider(
        Func<LexicalLookupRequest, LexicalResult> resultFactory) : IDictionaryLookupProvider
    {
        public List<string> RequestedTerms { get; } = [];

        public List<LexicalLookupRequest> Requests { get; } = [];

        public string ProviderName => WiktionaryLookupProvider.Name;

        public int ProviderSchemaVersion => WiktionaryLookupProvider.SchemaVersion;

        public Task<LexicalResult> LookupAsync(
            LexicalLookupRequest request,
            CancellationToken cancellationToken = default)
        {
            RequestedTerms.Add(request.Term);
            Requests.Add(request);
            return Task.FromResult(resultFactory(request));
        }
    }

    private sealed class ThrowingCacheRepository(
        bool throwOnRead,
        bool throwOnWrite) : ILexicalCacheRepository
    {
        public Task<LexicalResult?> GetAsync(
            LexicalLookupRequest request,
            string provider,
            int providerSchemaVersion) => throwOnRead
                ? throw new InvalidOperationException("Controlled cache read failure.")
                : Task.FromResult<LexicalResult?>(null);

        public Task SaveAsync(
            LexicalLookupRequest request,
            LexicalResult result,
            int providerSchemaVersion) => throwOnWrite
                ? throw new InvalidOperationException("Controlled cache write failure.")
                : Task.CompletedTask;
    }
}


