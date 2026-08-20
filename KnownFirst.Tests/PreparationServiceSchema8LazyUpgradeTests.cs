using System.Text.Json;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Services;
using KnownFirst.Services.Lexical;
using KnownFirst.Services.Study;

namespace KnownFirst.Tests;

/// <summary>
/// KF-MEANING-001 Slice 3 §6: lazy envelope upgrade of an active Schema-8 candidate, exercised through the
/// real service entry points (<see cref="PreparationService.GetCurrentAsync"/>,
/// <see cref="PreparationService.SelectMeaningAsync"/>, <see cref="PreparationService.AcceptAsync"/>) against
/// candidates whose <c>ResultJson</c> is deliberately seeded (never a hand-inserted candidate row's
/// business outcome — only its ResultJson shape) into every input shape the policy must classify: a genuine
/// EnvelopeV1 (untouched), a raw pre-migration LegacyLexicalResult (Pending/ResultReady/Failed), Empty, an
/// unsupported envelope version, and malformed JSON.
/// </summary>
[TestClass]
public sealed class PreparationServiceSchema8LazyUpgradeTests
{
    private static readonly DateTime Now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

    private TemporarySchema8Database _database = null!;
    private FakeClock _clock = null!;
    private TextReviewService _review = null!;
    private PreparationService _preparation = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _database = new TemporarySchema8Database("knownfirst-schema8-lazy-upgrade");
        await _database.InitializeAsync();
        // This class characterizes the JSON PreparationCandidate ResultJson envelope lazy-upgrade policy,
        // not literal-PRAGMA-version behavior — ResultJson content is manually seeded per test regardless
        // of schema version. The fixture upgrades immediately after construction so TextReviewService's
        // review-selection/completion setup methods, which now require the current schema, keep working.
        await _database.UpgradeToCurrentSchemaAsync();
        _clock = new FakeClock(Now);
        _review = new TextReviewService(
            _database, new TextAnalyzer(), new DisabledEnhancedRecognitionSettings(), new FixtureGermanLexicon());
        _preparation = new PreparationService(
            _database,
            new LexicalEnrichmentService(
                new AcronymExpansionDetector(),
                new MeaningRanker(),
                new LexicalCacheRepository(_database),
                new LexicalLookupProviderResolver([new NoOpProvider()])),
            _clock);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _preparation.CancelPrefetchAsync();
        await _database.DisposeAsync();
    }

    [TestMethod]
    public async Task GetCurrentAsync_GenuineEnvelopeV1_IsNeverRewritten()
    {
        var (wordId, candidateId) = await CreateSingleCandidateAsync();
        var envelope = PreparationCandidatePayloadV1.Create(
            LegacyResult("wikt-a"),
            resolvedProviderMeaningIndexes: [],
            frozenEvidence: [new PreparationCandidateEvidence(1, "fp", 0, 4)]);
        var json = PreparationCandidatePayloadCodec.Write(envelope);
        await SetResultJsonAsync(candidateId, json, PreparationCandidateStatus.ResultReady);

        _ = await _preparation.GetCurrentAsync();

        var after = await ReadResultJsonAsync(candidateId);
        Assert.AreEqual(json, after, "A genuine EnvelopeV1 must stay byte-identical.");
    }

    [TestMethod]
    public async Task GetCurrentAsync_LegacyResultReady_UpgradesToEnvelopeWithFrozenEvidenceAndPreservesValidIndex()
    {
        var (wordId, candidateId) = await CreateSingleCandidateAsync();
        var legacy = LegacyResult("wikt-a", "wikt-b");
        var legacyJson = JsonSerializer.Serialize(legacy, LexicalJsonSerializerContext.Default.LexicalResult);
        await SetResultJsonAsync(candidateId, legacyJson, PreparationCandidateStatus.ResultReady, selectedMeaningIndex: 1);

        var item = await _preparation.GetCurrentAsync();
        Assert.IsNotNull(item);

        var read = PreparationCandidatePayloadCodec.Read(await ReadResultJsonAsync(candidateId));
        Assert.AreEqual(PreparationCandidatePayloadKind.EnvelopeV1, read.Kind);
        Assert.AreEqual(2, read.Envelope!.Result!.Meanings.Count);
        Assert.IsTrue(read.Envelope.FrozenEvidence.Count > 0, "Upgrade must freeze evidence.");
        var candidate = await _database.ReadAsync(c => c.Table<PreparationCandidateEntity>().Where(x => x.Id == candidateId).FirstAsync());
        Assert.AreEqual(1, candidate.SelectedMeaningIndex, "A valid index must be preserved.");
    }

    [TestMethod]
    public async Task GetCurrentAsync_LegacyWithOutOfRangeIndex_IsCorrectedDeterministically()
    {
        var (wordId, candidateId) = await CreateSingleCandidateAsync();
        var legacy = LegacyResult("wikt-a");
        var legacyJson = JsonSerializer.Serialize(legacy, LexicalJsonSerializerContext.Default.LexicalResult);
        await SetResultJsonAsync(candidateId, legacyJson, PreparationCandidateStatus.ResultReady, selectedMeaningIndex: 99);

        _ = await _preparation.GetCurrentAsync();

        var candidate = await _database.ReadAsync(c => c.Table<PreparationCandidateEntity>().Where(x => x.Id == candidateId).FirstAsync());
        Assert.AreEqual(0, candidate.SelectedMeaningIndex);
    }

    [TestMethod]
    public async Task GetCurrentAsync_EmptyResultJson_UpgradesToEnvelopeWithNullResultAndFrozenEvidence()
    {
        var (wordId, candidateId) = await CreateSingleCandidateAsync();
        await SetResultJsonAsync(candidateId, string.Empty, PreparationCandidateStatus.Pending);

        _ = await _preparation.GetCurrentAsync();

        var read = PreparationCandidatePayloadCodec.Read(await ReadResultJsonAsync(candidateId));
        Assert.AreEqual(PreparationCandidatePayloadKind.EnvelopeV1, read.Kind);
        Assert.IsNull(read.Envelope!.Result);
    }

    [TestMethod]
    public async Task GetCurrentAsync_UnsupportedEnvelopeVersion_ThrowsBeforeMutation()
    {
        var (wordId, candidateId) = await CreateSingleCandidateAsync();
        var unsupportedJson = """{"payloadVersion":2,"Result":null,"ResolvedProviderMeaningIndexes":[],"FrozenEvidence":[]}""";
        await SetResultJsonAsync(candidateId, unsupportedJson, PreparationCandidateStatus.ResultReady);

        await Assert.ThrowsExactlyAsync<PreparationCandidateStateException>(() => _preparation.GetCurrentAsync());

        var after = await ReadResultJsonAsync(candidateId);
        Assert.AreEqual(unsupportedJson, after, "A rejected upgrade must not mutate the row.");
    }

    [TestMethod]
    public async Task GetCurrentAsync_MalformedResultJson_ThrowsBeforeMutation()
    {
        var (wordId, candidateId) = await CreateSingleCandidateAsync();
        var malformedJson = "{ not valid json";
        await SetResultJsonAsync(candidateId, malformedJson, PreparationCandidateStatus.ResultReady);

        await Assert.ThrowsExactlyAsync<PreparationCandidateStateException>(() => _preparation.GetCurrentAsync());

        var after = await ReadResultJsonAsync(candidateId);
        Assert.AreEqual(malformedJson, after);
    }

    [TestMethod]
    public async Task SelectMeaningAsync_LegacyResultReady_UpgradesBeforeSelecting()
    {
        var (wordId, candidateId) = await CreateSingleCandidateAsync();
        var legacy = LegacyResult("wikt-a", "wikt-b");
        var legacyJson = JsonSerializer.Serialize(legacy, LexicalJsonSerializerContext.Default.LexicalResult);
        await SetResultJsonAsync(candidateId, legacyJson, PreparationCandidateStatus.ResultReady);

        await _preparation.SelectMeaningAsync(candidateId, 1);

        var read = PreparationCandidatePayloadCodec.Read(await ReadResultJsonAsync(candidateId));
        Assert.AreEqual(PreparationCandidatePayloadKind.EnvelopeV1, read.Kind);
        var candidate = await _database.ReadAsync(c => c.Table<PreparationCandidateEntity>().Where(x => x.Id == candidateId).FirstAsync());
        Assert.AreEqual(1, candidate.SelectedMeaningIndex);
    }

    private async Task<(int WordId, int CandidateId)> CreateSingleCandidateAsync()
    {
        var request = new ImportTextRequest($"Doc-{Guid.NewGuid():N}", "bank text here.", "en", LexicalLookupMode.Definition, null);
        await _review.ImportAsync(request);
        var wordId = -1;
        while (await _review.GetCurrentCandidateAsync() is { } candidate)
        {
            wordId = candidate.WordId;
            await _review.DecideAsync(candidate.WordId, WordStatus.UnknownBacklog);
        }

        var sessionId = await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var candidateId = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>(
            "SELECT Id FROM PreparationCandidates WHERE SessionId = ?", sessionId));
        return (wordId, candidateId);
    }

    private async Task SetResultJsonAsync(
        int candidateId, string resultJson, PreparationCandidateStatus status, int selectedMeaningIndex = 0) =>
        await _database.ReadAsync(async connection =>
        {
            await connection.ExecuteAsync(
                "UPDATE PreparationCandidates SET ResultJson = ?, Status = ?, SelectedMeaningIndex = ? WHERE Id = ?",
                resultJson, (int)status, selectedMeaningIndex, candidateId);
            return true;
        });

    private Task<string> ReadResultJsonAsync(int candidateId) => _database.ReadAsync(
        c => c.ExecuteScalarAsync<string>("SELECT ResultJson FROM PreparationCandidates WHERE Id = ?", candidateId));

    private static LexicalResult LegacyResult(params string[] meaningIds) => new(
        LexicalLookupStatus.Success, "bank", "bank", TokenKind.Word, "en", "de", null,
        meaningIds.Select(id => new LexicalMeaning(id, "noun", $"Definition {id}", $"Translation {id}", null, [])).ToArray(),
        "Wiktionary", "en.wiktionary.org", "Bank", 1, "Wiktionary contributors", Now);

    private sealed class NoOpProvider : IDictionaryLookupProvider
    {
        public string ProviderName => "Wiktionary";
        public int ProviderSchemaVersion => 1;
        public Task<LexicalResult> LookupAsync(LexicalLookupRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("GetCurrentAsync/SelectMeaningAsync must never perform a network lookup.");
    }
}
