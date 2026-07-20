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
        Assert.AreEqual("de.wiktionary.org", result.SourceProject);
    }

    [TestMethod]
    public async Task Lookup_GermanTermWithEnglishExplanationReturnsTranslation()
    {
        var provider = CreateProvider(_ => JsonResponse(LoadFixture("german-entry.json")));

        var result = await provider.LookupAsync(Request("Netzwerk", "de", "en"));

        Assert.AreEqual("network", result.Meanings[0].Translation);
        Assert.AreEqual("en.wiktionary.org", result.SourceProject);
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
    }

    [TestMethod]
    public async Task Lookup_TimeoutReturnsTimedOutStatus()
    {
        var provider = CreateProvider(_ => throw new TaskCanceledException());
        var result = await provider.LookupAsync(Request("network", "en", "en"));
        Assert.AreEqual(LexicalLookupStatus.TimedOut, result.Status);
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
        Assert.AreEqual(3, attempts);
        Assert.HasCount(2, delay.Delays);
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
        Assert.Contains("de.wiktionary.org/w/api.php", uri);
        Assert.Contains("page=network%20security", uri);
        Assert.Contains("uselang=de", uri);
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
            provider);

        var result = await service.EnrichAsync(
            request,
            "Complete private document that must remain local.",
            "Example context sentence that must remain local.");

        Assert.IsTrue(result.IsFromCache);
        Assert.AreEqual(0, provider.CallCount);
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
            new FakeDictionaryProvider(external));

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
            provider);

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
            provider);

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
            provider);

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
            provider,
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
            provider);

        var result = await service.EnrichAsync(Request("data", "en", "en"), "data", "data");

        Assert.AreEqual("data", result.DisplayTerm);
        Assert.AreEqual("Facts collected for reference.", result.Meanings.Single().Definition);
        Assert.IsNull(result.EncounteredSurfaceForm);
        CollectionAssert.AreEqual(new[] { "data" }, provider.RequestedTerms.ToArray());
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
        string explanationLanguage) => new(
        term,
        term.ToLowerInvariant(),
        TokenKind.Word,
        sourceLanguage,
        explanationLanguage);

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
        Now);

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

        public string ProviderName => WiktionaryLookupProvider.Name;

        public int ProviderSchemaVersion => WiktionaryLookupProvider.SchemaVersion;

        public Task<LexicalResult> LookupAsync(
            LexicalLookupRequest request,
            CancellationToken cancellationToken = default)
        {
            RequestedTerms.Add(request.Term);
            return Task.FromResult(resultFactory(request));
        }
    }
}
