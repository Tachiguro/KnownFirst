using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Core.Text;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Services;
using KnownFirst.Services.Lexical;
using KnownFirst.Services.Study;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Storage;

namespace KnownFirst.Tests;

[TestClass]
public sealed class PreparationAuthorizationOrchestrationTests
{
    private static readonly DateTime Now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

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

    private sealed class TrackingLookupProvider(FakeClock clock) : ILexicalLookupProvider
    {
        public string ProviderName => "Wiktionary";
        public int ProviderSchemaVersion => 1;
        public int InvocationCount => _invocationCount;
        private int _invocationCount;

        public Func<LexicalLookupRequest, CancellationToken, Task<LexicalResult>>? Handler { get; set; }

        public async Task<LexicalResult> LookupAsync(LexicalLookupRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _invocationCount);
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
                [new LexicalMeaning("m1", "noun", $"Definition for {request.Term}", null, null, [])],
                ProviderName,
                "en.wiktionary.org",
                request.Term,
                1,
                "Wiktionary contributors",
                clock.UtcNow,
                LookupMode: request.LookupMode,
                TargetLanguage: request.TargetLanguage);
        }
    }

    private TemporarySchema8Database _database = null!;
    private FakeClock _clock = null!;
    private AppSettingsService _settings = null!;
    private OnlineLookupAuthorizationGate _gate = null!;
    private TrackingLookupProvider _provider = null!;
    private TextReviewService _review = null!;
    private PreparationService _preparation = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _database = new TemporarySchema8Database("knownfirst-prep-auth");
        await _database.InitializeAsync();
        await _database.UpgradeToCurrentSchemaAsync();
        _clock = new FakeClock(Now);
        var preferences = new InMemoryPreferences();
        _settings = new AppSettingsService(preferences, NullLogger<AppSettingsService>.Instance);
        _gate = new OnlineLookupAuthorizationGate(_settings);
        _provider = new TrackingLookupProvider(_clock);
        _review = new TextReviewService(
            _database, new TextAnalyzer(), new DisabledEnhancedRecognitionSettings(), new FixtureGermanLexicon());

        var cache = new LexicalCacheRepository(_database);
        var resolver = new LexicalLookupProviderResolver([_provider]);
        var enrichment = new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            cache,
            resolver,
            authorizationGate: _gate);

        _preparation = new PreparationService(
            _database,
            enrichment,
            _clock,
            authorizationGate: _gate);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _preparation.CancelPrefetchAsync();
        _gate.Dispose();
        await _database.DisposeAsync();
    }

    private async Task<int> ImportWordAsync(string content, string term)
    {
        var request = new ImportTextRequest($"Doc {Guid.NewGuid():N}", content, "en", LexicalLookupMode.Definition, null);
        var result = await _review.ImportAsync(request);
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);
        var wordId = -1;
        while (await _review.GetCurrentCandidateAsync() is { } candidate)
        {
            if (string.Equals(candidate.Candidate, term, StringComparison.OrdinalIgnoreCase))
            {
                wordId = candidate.WordId;
            }

            await _review.DecideAsync(candidate.WordId, WordStatus.UnknownBacklog);
        }

        Assert.AreNotEqual(-1, wordId);
        return wordId;
    }

    private static PreparedMeaningInput InputFrom(PreparationItem item, int meaningIndex = 0)
    {
        var result = item.Result ?? throw new InvalidOperationException("The item has no result.");
        var meaning = result.Meanings[meaningIndex];
        return new PreparedMeaningInput(
            meaning.MeaningId,
            result.AcronymExpansion,
            meaning.Translation,
            meaning.Definition,
            meaning.Example,
            null,
            [],
            result.ProviderName,
            result.SourceProject,
            result.PageTitle,
            result.RevisionId,
            result.Attribution,
            item.EncounteredSurfaceForm,
            result.GrammaticalRelationship);
    }

    // Scope A: New automatic session while disabled
    [TestMethod]
    public async Task StartAsync_AutomaticOnline_WhenUnauthorized_ThrowsAndMutatesNoState()
    {
        await ImportWordAsync("apple grows on trees.", "apple");
        Assert.IsFalse(_settings.HasOnlineLookupConsent);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => _preparation.StartAsync(PreparationMethod.AutomaticOnline, 5));

        var sessionCount = await _database.ReadAsync(c => c.Table<PreparationSessionEntity>().CountAsync());
        Assert.AreEqual(0, sessionCount);

        var candidateCount = await _database.ReadAsync(c => c.Table<PreparationCandidateEntity>().CountAsync());
        Assert.AreEqual(0, candidateCount);

        var word = await _database.ReadAsync(c => c.Table<WordEntity>().FirstAsync());
        Assert.AreEqual(PreparationState.Unprepared, word.PreparationState);

        Assert.AreEqual(0, _provider.InvocationCount);
    }

    [TestMethod]
    public async Task StartAsync_Manual_WhenUnauthorized_Succeeds()
    {
        await ImportWordAsync("apple grows on trees.", "apple");
        Assert.IsFalse(_settings.HasOnlineLookupConsent);

        var sessionId = await _preparation.StartAsync(PreparationMethod.Manual, 5);
        Assert.AreNotEqual(0, sessionId);

        var session = await _database.ReadAsync(c => c.Table<PreparationSessionEntity>().FirstOrDefaultAsync());
        Assert.IsNotNull(session);
        Assert.AreEqual(PreparationMethod.Manual, session.Method);
        Assert.AreEqual(0, _provider.InvocationCount);
    }

    // Scope B: Existing automatic session resumed while disabled
    [TestMethod]
    public async Task ResumedAutomaticSession_WhenUnauthorized_CanBeLoadedWithoutTriggeringLookup()
    {
        await ImportWordAsync("apple grows on trees.", "apple");
        _settings.GrantOnlineLookupConsent();

        var sessionId = await _preparation.StartAsync(PreparationMethod.AutomaticOnline, 5);
        Assert.AreNotEqual(0, sessionId);

        _settings.RevokeOnlineLookupConsent();

        var overview = await _preparation.GetOverviewAsync();
        Assert.AreEqual(sessionId, overview.ActiveSessionId);
        Assert.AreEqual(PreparationMethod.AutomaticOnline, overview.ActiveMethod);

        var item = await _preparation.GetCurrentAsync();
        Assert.IsNotNull(item);
        Assert.AreEqual(PreparationCandidateStatus.Pending, item.Status);
        Assert.AreEqual(0, _provider.InvocationCount);

        var sessionInDb = await _database.ReadAsync(c => c.Table<PreparationSessionEntity>().FirstAsync());
        Assert.AreEqual(PreparationSessionStatus.Active, sessionInDb.Status);
    }

    // Scope C: Foreground lookup
    [TestMethod]
    public async Task LookupCurrentAsync_WhenUnauthorized_ThrowsAndDoesNotPersistFailure()
    {
        await ImportWordAsync("apple grows on trees.", "apple");
        _settings.GrantOnlineLookupConsent();
        await _preparation.StartAsync(PreparationMethod.AutomaticOnline, 5);

        _settings.RevokeOnlineLookupConsent();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => _preparation.LookupCurrentAsync());

        Assert.AreEqual(0, _provider.InvocationCount);

        var candidate = await _database.ReadAsync(c => c.Table<PreparationCandidateEntity>().FirstAsync());
        Assert.AreEqual(PreparationCandidateStatus.Pending, candidate.Status);
        Assert.AreEqual(0, candidate.LookupAttemptCount);
        Assert.AreEqual(string.Empty, candidate.LastErrorCode);
    }

    [TestMethod]
    public async Task LookupCurrentAsync_WhenRevokedInFlight_CancelsAndDoesNotPersistFailure()
    {
        await ImportWordAsync("apple grows on trees.", "apple");
        _settings.GrantOnlineLookupConsent();
        await _preparation.StartAsync(PreparationMethod.AutomaticOnline, 5);

        var inFlightTcs = new TaskCompletionSource<bool>();
        _provider.Handler = async (req, ct) =>
        {
            inFlightTcs.SetResult(true);
            await Task.Delay(1500, ct);
            return new LexicalResult(
                LexicalLookupStatus.Success, req.NormalizedLemma, req.Term, req.TokenKind,
                req.SourceLanguage, req.ExplanationLanguage, null, [], "Wiktionary",
                "en.wiktionary.org", req.Term, 1, "contrib", _clock.UtcNow);
        };

        var lookupTask = _preparation.LookupCurrentAsync();
        await inFlightTcs.Task;

        _settings.RevokeOnlineLookupConsent();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await lookupTask);

        var candidate = await _database.ReadAsync(c => c.Table<PreparationCandidateEntity>().FirstAsync());
        Assert.AreNotEqual(PreparationCandidateStatus.Failed, candidate.Status);
        Assert.AreEqual(string.Empty, candidate.LastErrorCode);

        // Verify resumability once re-granted
        _settings.GrantOnlineLookupConsent();
        _provider.Handler = null; // Default success
        var itemAfterRegrant = await _preparation.LookupCurrentAsync();
        Assert.IsNotNull(itemAfterRegrant);
        Assert.AreEqual(PreparationCandidateStatus.ResultReady, itemAfterRegrant.Status);
    }

    // Scope D: Retry
    [TestMethod]
    public async Task Retry_WhenUnauthorized_ThrowsAndPreservesFailureData()
    {
        await ImportWordAsync("apple grows on trees.", "apple");
        _settings.GrantOnlineLookupConsent();
        await _preparation.StartAsync(PreparationMethod.AutomaticOnline, 5);

        // Fail first lookup with network error
        _provider.Handler = (req, ct) => Task.FromResult(new LexicalResult(
            LexicalLookupStatus.TransientFailure, req.NormalizedLemma, req.Term, req.TokenKind,
            req.SourceLanguage, req.ExplanationLanguage, null, [], "Wiktionary",
            "en.wiktionary.org", req.Term, 1, "contrib", _clock.UtcNow)
        {
            ErrorCode = "network-timeout"
        });

        var failedItem = await _preparation.LookupCurrentAsync();
        Assert.IsNotNull(failedItem);
        Assert.AreEqual(PreparationCandidateStatus.Failed, failedItem.Status);

        var candidateBeforeRevoke = await _database.ReadAsync(c => c.Table<PreparationCandidateEntity>().FirstAsync());
        Assert.AreEqual(PreparationCandidateStatus.Failed, candidateBeforeRevoke.Status);
        Assert.AreEqual("network-timeout", candidateBeforeRevoke.LastErrorCode);
        var attemptsBeforeRevoke = candidateBeforeRevoke.LookupAttemptCount;

        // Revoke consent
        _settings.RevokeOnlineLookupConsent();
        _provider.Handler = null;

        // Calling retry (LookupCurrentAsync) when unauthorized
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => _preparation.LookupCurrentAsync());

        // Status, error code, and attempt count must remain intact and uncorrupted
        var candidateAfterUnauthorizedRetry = await _database.ReadAsync(c => c.Table<PreparationCandidateEntity>().FirstAsync());
        Assert.AreEqual(PreparationCandidateStatus.Failed, candidateAfterUnauthorizedRetry.Status);
        Assert.AreEqual("network-timeout", candidateAfterUnauthorizedRetry.LastErrorCode);
        Assert.AreEqual(attemptsBeforeRevoke, candidateAfterUnauthorizedRetry.LookupAttemptCount);

        // Re-grant consent: retry now succeeds on the same candidate
        _settings.GrantOnlineLookupConsent();
        var retriedItem = await _preparation.LookupCurrentAsync();
        Assert.IsNotNull(retriedItem);
        Assert.AreEqual(PreparationCandidateStatus.ResultReady, retriedItem.Status);
    }

    // Scope E: Prefetch
    [TestMethod]
    public async Task Prefetch_ActivePrefetchCancelledOnRevocation_AndTransientResultNotConsumed()
    {
        await ImportWordAsync("apple.", "apple");
        await ImportWordAsync("banana.", "banana");
        _settings.GrantOnlineLookupConsent();
        await _preparation.StartAsync(PreparationMethod.AutomaticOnline, 5);

        var prefetchStartedTcs = new TaskCompletionSource<bool>();
        _provider.Handler = async (req, ct) =>
        {
            if (string.Equals(req.Term, "banana", StringComparison.OrdinalIgnoreCase))
            {
                prefetchStartedTcs.TrySetResult(true);
                await Task.Delay(1500, ct);
            }

            return new LexicalResult(
                LexicalLookupStatus.Success, req.NormalizedLemma, req.Term, req.TokenKind,
                req.SourceLanguage, req.ExplanationLanguage, null,
                [new LexicalMeaning("m1", "noun", $"Def for {req.Term}", null, null, [])],
                "Wiktionary", "en.wiktionary.org", req.Term, 1, "contrib", _clock.UtcNow);
        };

        // Lookup first item (apple); this triggers BeginPrefetch for banana
        var item1 = await _preparation.LookupCurrentAsync();
        Assert.IsNotNull(item1);
        Assert.AreEqual("apple", item1.Term);

        // Wait for prefetch for banana to start
        await prefetchStartedTcs.Task;

        // Revoke consent while prefetch is in flight
        _settings.RevokeOnlineLookupConsent();

        // Accept item 1 locally (should succeed because item 1 already has ready result)
        await _preparation.AcceptAsync(item1.CandidateId, InputFrom(item1), CardDirectionPreference.Both);

        // Candidate 2 (banana) is now current
        var item2 = await _preparation.GetCurrentAsync();
        Assert.IsNotNull(item2);
        Assert.AreEqual("banana", item2.Term);
        Assert.AreEqual(PreparationCandidateStatus.Pending, item2.Status);

        // LookupCurrentAsync on banana must fail fast because consent is revoked
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => _preparation.LookupCurrentAsync());

        // Stale prefetch was not consumed into candidate 2
        var candidate2Db = await _database.ReadAsync(c => c.Table<PreparationCandidateEntity>().Where(item => item.WordId == item2.WordId).FirstAsync());
        Assert.AreEqual(PreparationCandidateStatus.Pending, candidate2Db.Status);
    }

    [TestMethod]
    public async Task Prefetch_CompletedUnderOldEpoch_IsNotConsumedAfterRevocationAndRegrant()
    {
        await ImportWordAsync("apple.", "apple");
        await ImportWordAsync("banana.", "banana");
        _settings.GrantOnlineLookupConsent();
        await _preparation.StartAsync(PreparationMethod.AutomaticOnline, 5);

        // Lookup apple; prefetch for banana runs and completes
        var item1 = await _preparation.LookupCurrentAsync();
        Assert.IsNotNull(item1);

        // Wait briefly for prefetch of banana to finish
        await Task.Delay(100);

        // Revoke consent, then re-grant (new epoch!)
        _settings.RevokeOnlineLookupConsent();
        _settings.GrantOnlineLookupConsent();

        // Clear local cache so that if prefetch was discarded, foreground lookup must invoke provider
        await _database.RunInTransactionAsync(c => { c.Execute("DELETE FROM LexicalCache"); return true; });

        // Accept apple
        await _preparation.AcceptAsync(item1.CandidateId, InputFrom(item1), CardDirectionPreference.Both);

        var invocationsBeforeWord2 = _provider.InvocationCount;

        // Now lookup banana: the old prefetch was from the revoked epoch, so it MUST NOT be consumed.
        // It must perform a fresh lookup under the new epoch.
        var item2 = await _preparation.LookupCurrentAsync();
        Assert.IsNotNull(item2);
        Assert.AreEqual("banana", item2.Term);
        Assert.AreEqual(PreparationCandidateStatus.ResultReady, item2.Status);

        // Provider MUST have been invoked again for banana because old prefetch was discarded
        Assert.IsTrue(_provider.InvocationCount > invocationsBeforeWord2,
            "Fresh lookup must occur under new epoch instead of consuming old-epoch prefetch");
    }

    // Scope F: Progression and Data Integrity
    [TestMethod]
    public async Task Progression_UsablePersistedResult_CanBeAcceptedLocallyAfterRevocation()
    {
        var appleWordId = await ImportWordAsync("apple.", "apple");
        var bananaWordId = await ImportWordAsync("banana.", "banana");
        _settings.GrantOnlineLookupConsent();
        await _preparation.StartAsync(PreparationMethod.AutomaticOnline, 5);

        // Lookup apple while authorized
        var item1 = await _preparation.LookupCurrentAsync();
        Assert.IsNotNull(item1);
        Assert.AreEqual(PreparationCandidateStatus.ResultReady, item1.Status);

        // Revoke consent
        _settings.RevokeOnlineLookupConsent();

        // Accept apple locally without network
        await _preparation.AcceptAsync(item1.CandidateId, InputFrom(item1), CardDirectionPreference.Both);

        // Verify apple in DB is Prepared with Sense and LearningCard created
        var appleWord = await _database.ReadAsync(c => c.Table<WordEntity>().Where(w => w.Id == appleWordId).FirstAsync());
        Assert.AreEqual(PreparationState.Prepared, appleWord.PreparationState);

        var cards = await _database.ReadAsync(c => c.Table<LearningCardEntity>().Where(card => card.WordId == appleWordId).ToListAsync());
        Assert.IsTrue(cards.Count > 0);

        // Move to next candidate (banana)
        var item2 = await _preparation.GetCurrentAsync();
        Assert.IsNotNull(item2);
        Assert.AreEqual(bananaWordId, item2.WordId);
        Assert.AreEqual(PreparationCandidateStatus.Pending, item2.Status);

        // Progressing does NOT automatically run lookup while unauthorized
        Assert.AreEqual(1, _provider.InvocationCount); // Only apple was looked up

        // Banana can be skipped or marked known locally even while unauthorized!
        await _preparation.SkipAsync(item2.CandidateId);
        var bananaCandidate = await _database.ReadAsync(c => c.Table<PreparationCandidateEntity>().Where(c => c.WordId == bananaWordId).FirstAsync());
        Assert.AreEqual(PreparationCandidateStatus.Skipped, bananaCandidate.Status);

        var session = await _database.ReadAsync(c => c.Table<PreparationSessionEntity>().FirstAsync());
        Assert.AreEqual(PreparationSessionStatus.Completed, session.Status);
        Assert.AreEqual(2, session.CompletedItems);
    }
}
