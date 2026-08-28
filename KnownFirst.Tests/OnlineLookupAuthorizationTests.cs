using System.Net;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Core.Text;
using KnownFirst.Services;
using KnownFirst.Services.Lexical;
using KnownFirst.Services.Lexical.Wikipedia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Storage;

namespace KnownFirst.Tests;

[TestClass]
public sealed class OnlineLookupAuthorizationTests
{
    private sealed class InMemoryPreferences : IPreferences
    {
        private readonly Dictionary<string, object> _values = new();

        public bool ContainsKey(string key, string? sharedName = null) => _values.ContainsKey(key);
        public void Remove(string key, string? sharedName = null) => _values.Remove(key);
        public void Clear(string? sharedName = null) => _values.Clear();
        public void Set<T>(string key, T value, string? sharedName = null)
        {
            if (value is null) _values.Remove(key);
            else _values[key] = value;
        }
        public T Get<T>(string key, T defaultValue, string? sharedName = null)
        {
            return _values.TryGetValue(key, out var val) && val is T typedVal ? typedVal : defaultValue;
        }
    }

    private sealed class TrackingHandler : HttpMessageHandler
    {
        public int InvocationCount { get; private set; }
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? HandlerFunc { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            if (HandlerFunc is not null)
            {
                return await HandlerFunc(request, cancellationToken);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class TrackingLookupProvider(string name, int schemaVersion = 1) : ILexicalLookupProvider
    {
        public string ProviderName => name;
        public int ProviderSchemaVersion => schemaVersion;
        public int InvocationCount { get; private set; }
        public Func<LexicalLookupRequest, CancellationToken, Task<LexicalResult>>? Handler { get; set; }

        public async Task<LexicalResult> LookupAsync(LexicalLookupRequest request, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            if (Handler is not null)
            {
                return await Handler(request, cancellationToken);
            }
            return new LexicalResult(
                LexicalLookupStatus.Success,
                request.NormalizedLemma,
                request.Term,
                request.TokenKind,
                request.SourceLanguage,
                request.ExplanationLanguage,
                null,
                [new LexicalMeaning("1", "noun", "test definition", null, null, [])],
                ProviderName,
                "test",
                request.Term,
                1,
                "attribution",
                DateTime.UtcNow);
        }
    }

    [TestMethod]
    public async Task UnauthorizedSend_DoesNotInvokeInnerHandler()
    {
        var preferences = new InMemoryPreferences();
        var settings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        var gate = new OnlineLookupAuthorizationGate(settings);
        var trackingHandler = new TrackingHandler();
        var handler = new OnlineLookupAuthorizationHandler(gate, trackingHandler);
        var client = new HttpClient(handler);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            client.GetAsync("https://en.wiktionary.org/wiki/test"));

        Assert.AreEqual(0, trackingHandler.InvocationCount);
    }

    [TestMethod]
    public async Task GrantedConsent_PermitsOutboundSend()
    {
        var preferences = new InMemoryPreferences();
        var settings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        settings.GrantOnlineLookupConsent();

        var gate = new OnlineLookupAuthorizationGate(settings);
        var trackingHandler = new TrackingHandler();
        var handler = new OnlineLookupAuthorizationHandler(gate, trackingHandler);
        var client = new HttpClient(handler);

        var response = await client.GetAsync("https://en.wiktionary.org/wiki/test");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, trackingHandler.InvocationCount);
    }

    [TestMethod]
    public async Task Revocation_CancelsInFlightAuthorizedRequest()
    {
        var preferences = new InMemoryPreferences();
        var settings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        settings.GrantOnlineLookupConsent();

        var gate = new OnlineLookupAuthorizationGate(settings);
        var inFlightStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var trackingHandler = new TrackingHandler
        {
            HandlerFunc = async (req, ct) =>
            {
                inFlightStarted.SetResult(true);
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        };

        var handler = new OnlineLookupAuthorizationHandler(gate, trackingHandler);
        var client = new HttpClient(handler);

        var sendTask = client.GetAsync("https://en.wiktionary.org/wiki/test");
        await inFlightStarted.Task;

        // Revoke consent while request is in flight
        settings.RevokeOnlineLookupConsent();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await sendTask);
        Assert.AreEqual(1, trackingHandler.InvocationCount);
    }

    [TestMethod]
    public void ReGrant_CreatesFreshEpoch_OldEpochRemainsCancelled()
    {
        var preferences = new InMemoryPreferences();
        var settings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        settings.GrantOnlineLookupConsent();

        var gate = new OnlineLookupAuthorizationGate(settings);
        var epoch1Token = gate.CurrentEpochToken;
        Assert.IsFalse(epoch1Token.IsCancellationRequested);

        // Revoke: epoch 1 should be cancelled
        settings.RevokeOnlineLookupConsent();
        Assert.IsTrue(epoch1Token.IsCancellationRequested);
        Assert.IsFalse(gate.IsAuthorized);

        // Re-grant: epoch 2 must be fresh and not cancelled; epoch 1 remains cancelled
        settings.GrantOnlineLookupConsent();
        var epoch2Token = gate.CurrentEpochToken;

        Assert.IsTrue(gate.IsAuthorized);
        Assert.IsTrue(epoch1Token.IsCancellationRequested, "Epoch 1 token must remain permanently cancelled");
        Assert.IsFalse(epoch2Token.IsCancellationRequested, "Epoch 2 token must be valid and not cancelled");
        Assert.AreNotEqual(epoch1Token, epoch2Token);
    }

    [TestMethod]
    public async Task SendAttemptedAfterRevocation_BlockedBeforeInnerHandler()
    {
        var preferences = new InMemoryPreferences();
        var settings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        settings.GrantOnlineLookupConsent();

        var gate = new OnlineLookupAuthorizationGate(settings);
        var trackingHandler = new TrackingHandler();
        var handler = new OnlineLookupAuthorizationHandler(gate, trackingHandler);
        var client = new HttpClient(handler);

        // First send: authorized
        var firstResponse = await client.GetAsync("https://en.wiktionary.org/wiki/test");
        Assert.AreEqual(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.AreEqual(1, trackingHandler.InvocationCount);

        // Revoke consent
        settings.RevokeOnlineLookupConsent();

        // Second send: must be blocked before reaching inner handler
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            client.GetAsync("https://en.wiktionary.org/wiki/test2"));

        Assert.AreEqual(1, trackingHandler.InvocationCount, "Inner handler must not be invoked after revocation");
    }

    [TestMethod]
    public async Task ExistingCallerCancellation_StillWorks()
    {
        var preferences = new InMemoryPreferences();
        var settings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        settings.GrantOnlineLookupConsent();

        var gate = new OnlineLookupAuthorizationGate(settings);
        var trackingHandler = new TrackingHandler();
        var handler = new OnlineLookupAuthorizationHandler(gate, trackingHandler);
        var client = new HttpClient(handler);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await client.GetAsync("https://en.wiktionary.org/wiki/test", cts.Token));
    }

    [TestMethod]
    public async Task LocalCacheHit_RemainsUsableWithoutInvokingProviderOrNetwork()
    {
        await using var database = new TemporaryKnownFirstDatabase("cache_hit_unauthorized_db");
        await database.InitializeAsync();

        var preferences = new InMemoryPreferences();
        var settings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        // Unauthorized
        var gate = new OnlineLookupAuthorizationGate(settings);
        Assert.IsFalse(gate.IsAuthorized);

        var cache = new LexicalCacheRepository(database);
        var request = new LexicalLookupRequest("en", LexicalLookupMode.Definition, null, "test", TokenKind.Word, "Wiktionary");
        var cachedResult = new LexicalResult(
            LexicalLookupStatus.Success,
            "test",
            "test",
            TokenKind.Word,
            "en",
            "en",
            null,
            [new LexicalMeaning("1", "noun", "cached meaning", null, null, [])],
            "Wiktionary",
            "test",
            "test",
            1,
            "attribution",
            DateTime.UtcNow);

        // Seed cache
        await cache.SaveAsync(request, cachedResult, 1);

        var provider = new TrackingLookupProvider("Wiktionary", 1);
        var resolver = new LexicalLookupProviderResolver([provider]);
        var enrichmentService = new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            cache,
            resolver,
            authorizationGate: gate);

        var result = await enrichmentService.EnrichAsync(request, "test", null);

        Assert.AreEqual(LexicalLookupStatus.Success, result.Status);
        Assert.IsTrue(result.IsFromCache);
        Assert.AreEqual(0, provider.InvocationCount, "Provider must not be invoked on cache hit even when unauthorized");
    }

    [TestMethod]
    public async Task CacheMissWhileUnauthorized_DoesNotInvokeProviders()
    {
        await using var database = new TemporaryKnownFirstDatabase("cache_miss_unauthorized_db");
        await database.InitializeAsync();

        var preferences = new InMemoryPreferences();
        var settings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        // Unauthorized
        var gate = new OnlineLookupAuthorizationGate(settings);
        Assert.IsFalse(gate.IsAuthorized);

        var cache = new LexicalCacheRepository(database);
        var request = new LexicalLookupRequest("en", LexicalLookupMode.Definition, null, "test", TokenKind.Word, "Wiktionary");
        var provider = new TrackingLookupProvider("Wiktionary", 1);
        var resolver = new LexicalLookupProviderResolver([provider]);
        var enrichmentService = new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            cache,
            resolver,
            authorizationGate: gate);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            enrichmentService.EnrichAsync(request, "test", null));

        Assert.AreEqual(0, provider.InvocationCount, "Provider must not be invoked on cache miss when unauthorized");
    }

    [TestMethod]
    public async Task RevocationBeforeSubsequentFallbackOrRedirect_PreventsNewOutboundSend()
    {
        await using var database = new TemporaryKnownFirstDatabase("fallback_revocation_db");
        await database.InitializeAsync();

        var preferences = new InMemoryPreferences();
        var settings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        settings.GrantOnlineLookupConsent();

        var gate = new OnlineLookupAuthorizationGate(settings);
        var cache = new LexicalCacheRepository(database);

        var request = new LexicalLookupRequest("en", LexicalLookupMode.Definition, null, "test", TokenKind.Word, "Wiktionary");

        // Primary provider returns NotFound (eligible for fallback) and revokes consent upon completion
        var wiktionary = new TrackingLookupProvider("Wiktionary", 1)
        {
            Handler = (req, ct) =>
            {
                // Revoke consent right as primary completes
                settings.RevokeOnlineLookupConsent();
                return Task.FromResult(new LexicalResult(
                    LexicalLookupStatus.NotFound,
                    req.NormalizedLemma,
                    req.Term,
                    req.TokenKind,
                    req.SourceLanguage,
                    req.ExplanationLanguage,
                    null,
                    [],
                    "Wiktionary",
                    "test",
                    req.Term,
                    1,
                    "attr",
                    DateTime.UtcNow)
                {
                    ErrorCode = "not-found"
                });
            }
        };

        var wikipedia = new TrackingLookupProvider("Wikipedia", 1);
        var resolver = new LexicalLookupProviderResolver([wiktionary, wikipedia]);
        var enrichmentService = new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            cache,
            resolver,
            authorizationGate: gate);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            enrichmentService.EnrichAsync(request, "test", null));

        Assert.AreEqual(1, wiktionary.InvocationCount);
        Assert.AreEqual(0, wikipedia.InvocationCount, "Fallback provider must not be invoked after consent was revoked");
    }

    [TestMethod]
    public void ProductionDI_ResolvesLexicalProvidersThroughGatedHttpClient()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPreferences>(new InMemoryPreferences());
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<KnownFirst.Core.Learning.IClock, KnownFirst.Core.Learning.SystemClock>();
        services.AddSingleton<IAsyncDelay, SystemAsyncDelay>();
        services.AddSingleton<WiktionaryHtmlParser>();
        services.AddSingleton<ILexicalDiagnosticLog>(NullLexicalDiagnosticLog.Instance);
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<AppSettingsService>>(Microsoft.Extensions.Logging.Abstractions.NullLogger<AppSettingsService>.Instance);
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<WiktionaryLookupProvider>>(Microsoft.Extensions.Logging.Abstractions.NullLogger<WiktionaryLookupProvider>.Instance);
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<WikipediaLookupProvider>>(Microsoft.Extensions.Logging.Abstractions.NullLogger<WikipediaLookupProvider>.Instance);
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<WikipediaApiClient>>(Microsoft.Extensions.Logging.Abstractions.NullLogger<WikipediaApiClient>.Instance);

        services.AddSingleton<IOnlineLookupAuthorizationGate, OnlineLookupAuthorizationGate>();
        services.AddSingleton<OnlineLookupAuthorizationHandler>();
        services.AddSingleton<HttpClient>(sp =>
        {
            var gate = sp.GetRequiredService<IOnlineLookupAuthorizationGate>();
            var handler = new OnlineLookupAuthorizationHandler(gate);
            return new HttpClient(handler);
        });
        services.AddLexicalProviders();

        var provider = services.BuildServiceProvider();

        var httpClient = provider.GetRequiredService<HttpClient>();
        var wiktionary = provider.GetRequiredService<WiktionaryLookupProvider>();
        var wikipedia = provider.GetRequiredService<WikipediaApiClient>();

        Assert.IsNotNull(httpClient);
        Assert.IsNotNull(wiktionary);
        Assert.IsNotNull(wikipedia);

        // Verify the registered HttpClient has OnlineLookupAuthorizationHandler
        var field = typeof(HttpMessageInvoker).GetField("_handler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var handler = field?.GetValue(httpClient) as HttpMessageHandler;
        Assert.IsInstanceOfType(handler, typeof(OnlineLookupAuthorizationHandler), "HttpClient in DI must be gated by OnlineLookupAuthorizationHandler");
    }
}
