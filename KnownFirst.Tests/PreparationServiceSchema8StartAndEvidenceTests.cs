using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Core.Text;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Services;
using KnownFirst.Services.Lexical;
using KnownFirst.Services.Study;

namespace KnownFirst.Tests;

/// <summary>
/// KF-MEANING-001 Slice 3: the real Schema-8 <see cref="PreparationService.StartAsync"/> selection/evidence-
/// freezing path (§2), the shared context-evidence policy (§3), the effective-processed-evidence ledger
/// (§5), and frozen-candidate-evidence completeness (§4). Every candidate here is produced by the real
/// service methods (StartAsync/LookupCurrentAsync/AcceptAsync) — never hand-inserted.
/// </summary>
[TestClass]
public sealed class PreparationServiceSchema8StartAndEvidenceTests
{
    private static readonly DateTime Now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

    private TemporarySchema8Database _database = null!;
    private FakeClock _clock = null!;
    private TextReviewService _review = null!;
    private MutableProvider _provider = null!;
    private PreparationService _preparation = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _database = new TemporarySchema8Database("knownfirst-schema8-start");
        await _database.InitializeAsync();
        _clock = new FakeClock(Now);
        _review = new TextReviewService(_database, new TextAnalyzer());
        _provider = new MutableProvider(_clock);
        _preparation = new PreparationService(
            _database,
            new LexicalEnrichmentService(
                new AcronymExpansionDetector(),
                new MeaningRanker(),
                new LexicalCacheRepository(_database),
                new LexicalLookupProviderResolver([_provider])),
            _clock);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _preparation.CancelPrefetchAsync();
        await _database.DisposeAsync();
    }

    [TestMethod]
    public async Task StartAsync_FreshWord_FreezesEvidenceIntoPendingEnvelope()
    {
        var wordId = await ImportAndEstablishAsync(
            "Bank fees apply here. The bank reopened today. Visit the bank tomorrow.", "bank", "n1");

        var sessionId = await _preparation.StartAsync(PreparationMethod.Manual, 5);
        Assert.AreNotEqual(0, sessionId);

        var candidate = await _database.ReadAsync(c => c.Table<PreparationCandidateEntity>()
            .Where(item => item.WordId == wordId).FirstAsync());
        Assert.AreEqual(PreparationCandidateStatus.Pending, candidate.Status);

        var read = PreparationCandidatePayloadCodec.Read(candidate.ResultJson);
        Assert.AreEqual(PreparationCandidatePayloadKind.EnvelopeV1, read.Kind);
        Assert.IsNull(read.Envelope!.Result);
        Assert.AreEqual(3, read.Envelope.FrozenEvidence.Count);
    }

    [TestMethod]
    public async Task StartAsync_PreparedWordWithGenuinelyNewEvidence_BecomesEligibleAgain()
    {
        var wordId = await ImportAndEstablishAsync("Bank fees apply here.", "bank", "n1");
        await PrepareWordCompletelyAsync(wordId, "bank");

        var wordAfterFirstPrepare = await _database.ReadAsync(c => c.Table<WordEntity>().Where(w => w.Id == wordId).FirstAsync());
        Assert.AreEqual(PreparationState.Prepared, wordAfterFirstPrepare.PreparationState);

        // Genuinely new evidence: a second document, same term, with a fresh unrelated word so the import
        // isn't short-circuited as NoNewVocabulary.
        await ImportAndEstablishAsync("The bank closed early. Nonce zzzalpha appeared.", "bank", "n2");

        var sessionId = await _preparation.StartAsync(PreparationMethod.Manual, 5);
        Assert.AreNotEqual(0, sessionId);

        var candidates = await _database.ReadAsync(c => c.Table<PreparationCandidateEntity>()
            .Where(item => item.SessionId == sessionId && item.WordId == wordId).ToListAsync());
        Assert.HasCount(1, candidates);
        var read = PreparationCandidatePayloadCodec.Read(candidates[0].ResultJson);
        Assert.AreEqual(PreparationCandidatePayloadKind.EnvelopeV1, read.Kind);
        Assert.HasCount(1, read.Envelope!.FrozenEvidence);
    }

    [TestMethod]
    public async Task StartAsync_PreparedWordWithNoNewEvidence_StaysIneligible()
    {
        var wordId = await ImportAndEstablishAsync("Bank fees apply here.", "bank", "n1");
        await PrepareWordCompletelyAsync(wordId, "bank");

        var sessionId = await _preparation.StartAsync(PreparationMethod.Manual, 5);
        Assert.AreEqual(0, sessionId);
    }

    [TestMethod]
    public async Task StartAsync_ThreeProcessedOccurrencesThenAFourthNew_ScannerFindsTheFourth()
    {
        var wordId = await ImportAndEstablishAsync(
            "Bank fees apply here. The bank reopened today. Visit the bank tomorrow.", "bank", "n1");
        await PrepareWordCompletelyAsync(wordId, "bank"); // consumes exactly the first 3 occurrences

        await ImportAndEstablishAsync("A fourth bank mention appears. Nonce zzzbeta here.", "bank", "n2");

        var sessionId = await _preparation.StartAsync(PreparationMethod.Manual, 5);
        Assert.AreNotEqual(0, sessionId);
        var candidate = await _database.ReadAsync(c => c.Table<PreparationCandidateEntity>()
            .Where(item => item.SessionId == sessionId && item.WordId == wordId).FirstAsync());
        var evidence = PreparationCandidatePayloadCodec.Read(candidate.ResultJson).Envelope!.FrozenEvidence;
        Assert.HasCount(1, evidence);
        // The fourth occurrence's document must be the newly imported one, never one of the first three.
        var firstDocumentId = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>(
            "SELECT MIN(Id) FROM Documents"));
        Assert.AreNotEqual(firstDocumentId, evidence[0].SourceDocumentId);
    }

    [TestMethod]
    public async Task LookupCurrentAsync_SendsFrozenFirstContext_NotALaterRecomputedContext()
    {
        var wordId = await ImportAndEstablishAsync("Bank fees apply in spring.", "bank", "n1");
        await _preparation.StartAsync(PreparationMethod.Manual, 5);

        // Import a second occurrence before the lookup runs; the frozen evidence recorded at StartAsync
        // must still win.
        await ImportAndEstablishAsync("Winter bank news arrives. Nonce zzzgamma appears.", "bank", "n2");

        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution")];
        var item = await _preparation.LookupCurrentAsync();

        Assert.IsNotNull(item);
        Assert.IsTrue(item!.Contexts.Count >= 1);
        StringAssert.Contains(item.Contexts[0].Text, "spring");
        Assert.IsFalse(item.Contexts.Any(context => context.Text.Contains("Winter")));
    }

    [TestMethod]
    public async Task Accept_EvidenceRemainsByteIdenticalAfterFirstMeaningAccepted()
    {
        var wordId = await ImportAndEstablishAsync(
            "Bank fees apply here. The bank reopened today.", "bank", "n1");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution"), Meaning("wikt-river-edge")];
        await _preparation.StartAsync(PreparationMethod.Manual, 5);
        var item = await _preparation.LookupCurrentAsync();
        var candidateId = item!.CandidateId;

        var beforeAccept = await _database.ReadAsync(c => c.Table<PreparationCandidateEntity>()
            .Where(x => x.Id == candidateId).FirstAsync());
        var evidenceBefore = PreparationCandidatePayloadCodec.Read(beforeAccept.ResultJson).Envelope!.FrozenEvidence;

        await _preparation.AcceptAsync(candidateId, InputFrom(item, 0), CardDirectionPreference.Both);

        var afterAccept = await _database.ReadAsync(c => c.Table<PreparationCandidateEntity>()
            .Where(x => x.Id == candidateId).FirstAsync());
        var evidenceAfter = PreparationCandidatePayloadCodec.Read(afterAccept.ResultJson).Envelope!.FrozenEvidence;

        CollectionAssert.AreEqual(evidenceBefore.ToArray(), evidenceAfter.ToArray());
    }

    [TestMethod]
    public async Task Ledger_MalformedCandidateHistory_ThrowsBeforeStartMutatesAnything()
    {
        var wordId = await ImportAndEstablishAsync("Bank fees apply here.", "bank", "n1");
        await PrepareWordCompletelyAsync(wordId, "bank");

        // Corrupt the historical candidate's ResultJson so its envelope carries an unsupported version.
        await _database.ReadAsync(async connection =>
        {
            await connection.ExecuteAsync(
                "UPDATE PreparationCandidates SET ResultJson = '{\"payloadVersion\":2,\"Result\":null,\"ResolvedProviderMeaningIndexes\":[],\"FrozenEvidence\":[]}' WHERE WordId = ?",
                wordId);
            return true;
        });
        await ImportAndEstablishAsync("A new bank sentence. Nonce zzzdelta here.", "bank", "n2");

        await Assert.ThrowsExactlyAsync<PreparationCandidateStateException>(
            () => _preparation.StartAsync(PreparationMethod.Manual, 5));

        var activeSessionCount = await _database.ReadAsync(c => c.Table<PreparationSessionEntity>()
            .Where(s => s.Status == PreparationSessionStatus.Active).CountAsync());
        Assert.AreEqual(0, activeSessionCount);
    }

    [TestMethod]
    public async Task LookupCurrentAsync_AllProviderMeaningsExactMatchExistingSense_AutoCompletesWithoutExplicitAccept()
    {
        var wordId = await ImportAndEstablishAsync("Bank fees apply here.", "bank", "n1");
        await PrepareWordCompletelyAsync(wordId, "bank");
        await ImportAndEstablishAsync("A new bank sentence appears. Nonce zzzepsilon here.", "bank", "n2");

        // The lexical cache is keyed by term/language/mode/provider, not by meaning content — clear it so
        // the re-offered candidate's lookup genuinely reaches the provider again instead of replaying the
        // first lookup's single-meaning cached result.
        await _database.ReadAsync(c => c.ExecuteAsync("DELETE FROM LexicalCache"));

        // The re-offered candidate's provider now returns two meanings, both exact matches for the Sense
        // already created above — lookup persistence alone must auto-resolve and auto-complete.
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution"), Meaning("wikt-financial-institution")];
        var sessionId = await _preparation.StartAsync(PreparationMethod.Manual, 5);
        Assert.AreNotEqual(0, sessionId);

        var item = await _preparation.LookupCurrentAsync();

        // The lookup call itself reports the now-Prepared outcome; a follow-up GetCurrentAsync no longer
        // finds this candidate "current" (only Pending/ResultReady/Failed candidates qualify).
        Assert.IsNotNull(item);
        Assert.AreEqual(PreparationCandidateStatus.Prepared, item!.Status);
        Assert.IsNull(await _preparation.GetCurrentAsync());

        var candidate = await _database.ReadAsync(c => c.Table<PreparationCandidateEntity>()
            .Where(x => x.SessionId == sessionId && x.WordId == wordId).FirstAsync());
        Assert.AreEqual(PreparationCandidateStatus.Prepared, candidate.Status);
        var envelope = PreparationCandidatePayloadCodec.Read(candidate.ResultJson).Envelope!;
        var senseCount = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Senses WHERE WordId = ?", wordId));
        CollectionAssert.AreEqual(
            new[] { 0, 1 },
            envelope.ResolvedProviderMeaningIndexes.ToArray(),
            $"resolved=[{string.Join(",", envelope.ResolvedProviderMeaningIndexes)}] senseCount={senseCount} status={candidate.Status}");
        Assert.AreEqual(1, senseCount);

        var word = await _database.ReadAsync(c => c.Table<WordEntity>().Where(w => w.Id == wordId).FirstAsync());
        Assert.AreEqual(PreparationState.Prepared, word.PreparationState);
    }

    [TestMethod]
    public async Task LookupCurrentAsync_CalledTwiceOnAnAlreadyAutoCompletedCandidate_DoesNotDoubleCompleteSession()
    {
        var wordId = await ImportAndEstablishAsync("Bank fees apply here.", "bank", "n1");
        await PrepareWordCompletelyAsync(wordId, "bank");
        await ImportAndEstablishAsync("A new bank sentence appears. Nonce zzzzeta here.", "bank", "n2");

        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution")];
        var sessionId = await _preparation.StartAsync(PreparationMethod.Manual, 5);
        await _preparation.LookupCurrentAsync();
        var completedItemsAfterFirst = await _database.ReadAsync(c => c.Table<PreparationSessionEntity>()
            .Where(s => s.Id == sessionId).FirstAsync());

        // A second call must not find the same candidate "current" again (it is Prepared, not Pending/
        // ResultReady/Failed) and therefore must not increment session counters a second time.
        await _preparation.LookupCurrentAsync();
        var completedItemsAfterSecond = await _database.ReadAsync(c => c.Table<PreparationSessionEntity>()
            .Where(s => s.Id == sessionId).FirstAsync());

        Assert.AreEqual(completedItemsAfterFirst.CompletedItems, completedItemsAfterSecond.CompletedItems);
    }

    private async Task<int> ImportAndEstablishAsync(string content, string targetTerm, string suffix)
    {
        var request = new ImportTextRequest($"Doc-{suffix}-{Guid.NewGuid():N}", content, "en", LexicalLookupMode.Definition, null);
        await _review.ImportAsync(request);

        var wordId = -1;
        while (await _review.GetCurrentCandidateAsync() is { } candidate)
        {
            if (string.Equals(candidate.Candidate, targetTerm, StringComparison.OrdinalIgnoreCase))
            {
                wordId = candidate.WordId;
                await _review.DecideAsync(candidate.WordId, WordStatus.UnknownBacklog);
            }
            else
            {
                await _review.DecideAsync(candidate.WordId, WordStatus.Known);
            }
        }

        if (wordId == -1)
        {
            var words = await _database.ReadAsync(c => c.Table<WordEntity>().ToListAsync());
            var word = words.First(w =>
                string.Equals(w.CanonicalTerm, targetTerm, StringComparison.OrdinalIgnoreCase)
                || string.Equals(w.NormalizedTerm, targetTerm, StringComparison.OrdinalIgnoreCase));
            wordId = word.Id;
        }

        return wordId;
    }

    private async Task PrepareWordCompletelyAsync(int wordId, string term)
    {
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution")];
        await _preparation.StartAsync(PreparationMethod.Manual, 5);
        var item = await _preparation.LookupCurrentAsync();
        if (item is null || item.WordId != wordId)
        {
            // Word wasn't (re)selected this round — nothing to prepare.
            return;
        }

        await _preparation.AcceptAsync(item.CandidateId, InputFrom(item, 0), CardDirectionPreference.Both);
    }

    private static LexicalMeaning Meaning(string providerMeaningId, string partOfSpeech = "noun") =>
        new(providerMeaningId, partOfSpeech, $"Definition for {providerMeaningId}", $"Translation {providerMeaningId}", null, []);

    private static PreparedMeaningInput InputFrom(PreparationItem item, int meaningIndex)
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

    private sealed class MutableProvider(FakeClock clock) : IDictionaryLookupProvider
    {
        public Func<LexicalLookupRequest, IReadOnlyList<LexicalMeaning>> MeaningsFactory { get; set; } =
            _ => [new LexicalMeaning("primary", "noun", "Definition", null, null, [])];

        public string ProviderName => "Wiktionary";

        public int ProviderSchemaVersion => 1;

        public Task<LexicalResult> LookupAsync(LexicalLookupRequest request, CancellationToken cancellationToken = default)
        {
            var result = new LexicalResult(
                LexicalLookupStatus.Success,
                request.NormalizedLemma,
                request.Term,
                request.TokenKind,
                request.SourceLanguage,
                request.ExplanationLanguage,
                null,
                MeaningsFactory(request),
                ProviderName,
                "en.wiktionary.org",
                request.Term,
                1,
                "Wiktionary contributors",
                clock.UtcNow,
                LookupMode: request.LookupMode,
                TargetLanguage: request.TargetLanguage);
            return Task.FromResult(result);
        }
    }
}
