using System.Collections.Concurrent;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Core.Text;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models;
using KnownFirst.Services;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.Lexical;
using KnownFirst.Services.Study;

namespace KnownFirst.Tests;

/// <summary>
/// KF-MEANING-001 Slice 3: <see cref="PreparationService.AcceptAsync"/> against a synthetic, already
/// dormant-migrated Schema-8 fixture (<see cref="TemporarySchema8Database"/>) — never a real application
/// database. Covers multi-Sense candidate resolution, exact-variant dedup, the evidence ledger, the
/// TopicOrDomain/PartOfSpeech persistence path, all-exact automatic completion, capability fail-closed
/// behavior, and fault-injection rollback.
/// </summary>
[TestClass]
public sealed class PreparationServiceSchema8AcceptTests
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
        _database = new TemporarySchema8Database("knownfirst-schema8-accept");
        await _database.InitializeAsync();
        _clock = new FakeClock(Now);
        _review = new TextReviewService(_database, new TextAnalyzer());
        _provider = new MutableProvider(_clock);
        _preparation = CreatePreparationService(_provider);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _preparation.CancelPrefetchAsync();
        await _database.DisposeAsync();
    }

    [TestMethod]
    public async Task Accept_SingleMeaning_CreatesSenseMeaningAndCards_NeverWritesWordStatusPrepared()
    {
        var wordId = await ImportSingleUnknownAsync("bank protects money.", "bank");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution")];

        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();
        await _preparation.AcceptAsync(item!.CandidateId, InputFrom(item), CardDirectionPreference.Both);

        var senses = await _database.ReadAsync(c => c.QueryAsync<SenseRow>("SELECT * FROM Senses WHERE WordId = ?", wordId));
        Assert.HasCount(1, senses);
        Assert.AreEqual("wikt-financial-institution", senses[0].ProviderSenseId);
        Assert.IsNotNull(senses[0].DefaultMeaningId);

        var meaningCount = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Meanings WHERE SenseId = ?", senses[0].Id));
        Assert.AreEqual(1, meaningCount);

        var cardCount = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningCards WHERE SenseId = ?", senses[0].Id));
        Assert.AreEqual(2, cardCount);

        var word = await _database.ReadAsync(c => c.Table<KnownFirst.Data.Entities.WordEntity>().Where(w => w.Id == wordId).FirstAsync());
        Assert.AreNotEqual(WordStatus.Prepared, word.Status);
        Assert.AreEqual(PreparationState.Prepared, word.PreparationState);

        var candidate = await _database.ReadAsync(c => c.Table<KnownFirst.Data.Entities.PreparationCandidateEntity>().FirstAsync());
        Assert.AreEqual(PreparationCandidateStatus.Prepared, candidate.Status);
        var envelope = PreparationCandidatePayloadCodec.Read(candidate.ResultJson);
        Assert.AreEqual(PreparationCandidatePayloadKind.EnvelopeV1, envelope.Kind);
        CollectionAssert.AreEqual(new[] { 0 }, envelope.Envelope!.ResolvedProviderMeaningIndexes.ToArray());
    }

    [TestMethod]
    public async Task Accept_FourProviderMeanings_ResolvedOneAtATimeWithoutSilentLoss()
    {
        await ImportSingleUnknownAsync("bank text here.", "bank");
        _provider.MeaningsFactory = _ =>
        [
            Meaning("wikt-financial-institution"),
            Meaning("wikt-river-edge"),
            Meaning("wikt-blood-bank"),
            Meaning("wikt-data-bank")
        ];

        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();
        var candidateId = item!.CandidateId;

        for (var index = 0; index < 4; index++)
        {
            await _preparation.SelectMeaningAsync(candidateId, index);
            var current = await _preparation.GetCurrentAsync();
            var toAccept = current ?? await ReloadCompletedItemAsync(candidateId);
            await _preparation.AcceptAsync(candidateId, InputFrom(toAccept, index), CardDirectionPreference.Both);
        }

        var senseCount = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Senses"));
        Assert.AreEqual(4, senseCount);
        var meaningCount = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Meanings"));
        Assert.AreEqual(4, meaningCount);

        var candidate = await _database.ReadAsync(c => c.Table<KnownFirst.Data.Entities.PreparationCandidateEntity>().FirstAsync());
        Assert.AreEqual(PreparationCandidateStatus.Prepared, candidate.Status);
        var envelope = PreparationCandidatePayloadCodec.Read(candidate.ResultJson).Envelope!;
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, envelope.ResolvedProviderMeaningIndexes.ToArray());
    }

    [TestMethod]
    public async Task Accept_AllExactAgainstExistingSense_AutoCompletesAfterOneAccept()
    {
        var wordId = await ImportSingleUnknownAsync("bank text here.", "bank");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution")];
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var firstItem = await _preparation.LookupCurrentAsync();
        await _preparation.AcceptAsync(firstItem!.CandidateId, InputFrom(firstItem), CardDirectionPreference.Both);

        // A second, independent candidate for the same already-prepared word (simulating new context
        // surfacing it again), whose provider now returns two meanings that both classify Equal against
        // the already-known Sense.
        var sessionId = await InsertSessionAsync();
        var candidateId = await InsertCandidateAsync(sessionId, wordId, order: 0);
        var duplicateLookup = new LexicalResult(
            LexicalLookupStatus.Success, "bank", "bank", TokenKind.Word, "en", "de", null,
            [Meaning("wikt-financial-institution"), Meaning("wikt-financial-institution")],
            "Wiktionary", "en.wiktionary.org", "Bank", 1, "Wiktionary contributors", Now);
        await _database.ReadAsync(async connection =>
        {
            await connection.ExecuteAsync(
                "UPDATE PreparationCandidates SET ResultJson = ?, Status = 1 WHERE Id = ?",
                PreparationCandidatePayloadCodec.Write(PreparationCandidatePayloadV1.Create(duplicateLookup)),
                candidateId);
            return true;
        });

        await _preparation.SelectMeaningAsync(candidateId, 0);
        await _preparation.AcceptAsync(
            candidateId,
            ManualInput("bank", meaningId: "wikt-financial-institution"),
            CardDirectionPreference.Both);

        var candidate = await _database.ReadAsync(c => c.Table<KnownFirst.Data.Entities.PreparationCandidateEntity>()
            .Where(item => item.Id == candidateId).FirstAsync());
        Assert.AreEqual(PreparationCandidateStatus.Prepared, candidate.Status);
        var envelope = PreparationCandidatePayloadCodec.Read(candidate.ResultJson).Envelope!;
        CollectionAssert.AreEqual(new[] { 0, 1 }, envelope.ResolvedProviderMeaningIndexes.ToArray());

        // No new Sense/Meaning was created for the auto-linked duplicate — still exactly one Sense.
        var senseCount = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Senses WHERE WordId = ?", wordId));
        Assert.AreEqual(1, senseCount);
    }

    [TestMethod]
    public async Task Accept_DuplicateAcceptanceOfSameResolvedIndex_Throws()
    {
        await ImportSingleUnknownAsync("bank text here.", "bank");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution"), Meaning("wikt-river-edge")];
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();
        await _preparation.AcceptAsync(item!.CandidateId, InputFrom(item), CardDirectionPreference.Both);

        // SelectedMeaningIndex was never advanced past 0 — accepting again must fail rather than silently
        // re-creating content for an already-resolved index.
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => _preparation.AcceptAsync(item.CandidateId, InputFrom(item), CardDirectionPreference.Both));
    }

    [TestMethod]
    public async Task Accept_TopicOrDomainAndExplicitPartOfSpeech_ArePersistedOnSense()
    {
        var wordId = await ImportSingleUnknownAsync("bank text here.", "bank");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution", partOfSpeech: "noun")];
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();

        var input = InputFrom(item!) with { TopicOrDomain = "finance", PartOfSpeech = "proper noun" };
        await _preparation.AcceptAsync(item.CandidateId, input, CardDirectionPreference.Both);

        var sense = await _database.ReadAsync(c => c.Table<KnownFirst.Data.Entities.WordEntity>().Where(w => w.Id == wordId).FirstAsync())
            .ContinueWith(_ => _database.ReadAsync(c => c.QueryAsync<SenseRow>("SELECT * FROM Senses WHERE WordId = ?", wordId))).Unwrap();
        Assert.AreEqual("finance", sense[0].TopicOrDomain);
        Assert.AreEqual("proper noun", sense[0].PartOfSpeech);
    }

    [TestMethod]
    public async Task Accept_PartOfSpeechFallsBackToProviderMeaningWhenNotSuppliedByUser()
    {
        var wordId = await ImportSingleUnknownAsync("bank text here.", "bank");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution", partOfSpeech: "noun")];
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();

        var input = InputFrom(item!) with { PartOfSpeech = null };
        await _preparation.AcceptAsync(item.CandidateId, input, CardDirectionPreference.Both);

        var senses = await _database.ReadAsync(c => c.QueryAsync<SenseRow>("SELECT * FROM Senses WHERE WordId = ?", wordId));
        Assert.AreEqual("noun", senses[0].PartOfSpeech);
    }

    [TestMethod]
    public async Task Accept_ExistingCardPreferredMeaning_NeverRepointedWhenSecondMeaningJoinsSameSense()
    {
        var wordId = await ImportSingleUnknownAsync("bank text here.", "bank");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution")];
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var firstItem = await _preparation.LookupCurrentAsync();
        await _preparation.AcceptAsync(firstItem!.CandidateId, InputFrom(firstItem), CardDirectionPreference.Both);

        var senseId = (await _database.ReadAsync(c => c.QueryAsync<SenseRow>("SELECT * FROM Senses WHERE WordId = ?", wordId)))[0].Id;
        var originalPreferredMeaningId = await _database.ReadAsync(c =>
            c.ExecuteScalarAsync<int>("SELECT PreferredMeaningId FROM LearningCards WHERE SenseId = ? AND Direction = ?", senseId, (int)CardDirection.TermToMeaning));

        // A second candidate for the same word, same provider sense id (classifies Equal -> same Sense),
        // but different, non-duplicate content -> a distinct exact-variant Meaning is created under the
        // same Sense, and the existing card must keep pointing at the original Meaning.
        var sessionId = await InsertSessionAsync();
        var candidateId = await InsertCandidateAsync(sessionId, wordId, order: 0);
        var secondLookup = new LexicalResult(
            LexicalLookupStatus.Success, "bank", "bank", TokenKind.Word, "en", "de", null,
            [new LexicalMeaning("wikt-financial-institution", "noun", "A different worded definition.", "Sparkasse", null, [])],
            "Wiktionary", "en.wiktionary.org", "Bank", 1, "Wiktionary contributors", Now);
        await _database.ReadAsync(async connection =>
        {
            await connection.ExecuteAsync(
                "UPDATE PreparationCandidates SET ResultJson = ?, Status = 1 WHERE Id = ?",
                PreparationCandidatePayloadCodec.Write(PreparationCandidatePayloadV1.Create(secondLookup)),
                candidateId);
            return true;
        });
        await _preparation.SelectMeaningAsync(candidateId, 0);
        await _preparation.AcceptAsync(
            candidateId,
            ManualInputWithTranslation("bank", "wikt-financial-institution", "Sparkasse", "A different worded definition."),
            CardDirectionPreference.Both);

        var meaningCountForSense = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Meanings WHERE SenseId = ?", senseId));
        Assert.AreEqual(2, meaningCountForSense);
        var preferredAfter = await _database.ReadAsync(c =>
            c.ExecuteScalarAsync<int>("SELECT PreferredMeaningId FROM LearningCards WHERE SenseId = ? AND Direction = ?", senseId, (int)CardDirection.TermToMeaning));
        Assert.AreEqual(originalPreferredMeaningId, preferredAfter);
    }

    [TestMethod]
    public async Task Accept_CapabilityShapeMismatch_FailsBeforeAnyMutation()
    {
        await ImportSingleUnknownAsync("bank text here.", "bank");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution")];
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();

        await _database.ReadAsync(async connection =>
        {
            await connection.ExecuteAsync("DROP TABLE Senses");
            return true;
        });

        await Assert.ThrowsExactlyAsync<PreparationSchemaCapabilityException>(
            () => _preparation.AcceptAsync(item!.CandidateId, InputFrom(item), CardDirectionPreference.Both));

        var candidate = await _database.ReadAsync(c => c.Table<KnownFirst.Data.Entities.PreparationCandidateEntity>().FirstAsync());
        Assert.AreEqual(PreparationCandidateStatus.ResultReady, candidate.Status);
    }

    [TestMethod]
    [DataRow(PreparationSchema8Checkpoints.AfterEnvelopePersist)]
    [DataRow(PreparationSchema8Checkpoints.AfterSenseInsert)]
    [DataRow(PreparationSchema8Checkpoints.AfterMeaningInsert)]
    [DataRow(PreparationSchema8Checkpoints.AfterContextLink)]
    [DataRow(PreparationSchema8Checkpoints.AfterCardInsert)]
    [DataRow(PreparationSchema8Checkpoints.AfterResolvedIndexPersist)]
    [DataRow(PreparationSchema8Checkpoints.DuringAutoExactVariantLinking)]
    [DataRow(PreparationSchema8Checkpoints.BeforeCandidateCompletion)]
    [DataRow(PreparationSchema8Checkpoints.BeforeAutomaticCandidateCompletion)]
    public async Task Accept_FaultInjectedAtCheckpoint_RollsBackCompletelyAndPreservesUserVersion(string checkpoint)
    {
        var wordId = await ImportSingleUnknownAsync("bank text here.", "bank");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution")];
        var injector = new RecordingFaultInjector(checkpoint);
        var faultyPreparation = CreatePreparationService(_provider, injector);
        await faultyPreparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await faultyPreparation.LookupCurrentAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => faultyPreparation.AcceptAsync(item!.CandidateId, InputFrom(item), CardDirectionPreference.Both));

        var userVersion = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("PRAGMA user_version"));
        Assert.AreEqual(8, userVersion);

        var senseCount = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Senses"));
        Assert.AreEqual(0, senseCount);
        var meaningCount = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Meanings"));
        Assert.AreEqual(0, meaningCount);
        var cardCount = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningCards"));
        Assert.AreEqual(0, cardCount);

        var word = await _database.ReadAsync(c => c.Table<KnownFirst.Data.Entities.WordEntity>().Where(w => w.Id == wordId).FirstAsync());
        Assert.AreEqual(PreparationState.Preparing, word.PreparationState);
        var candidate = await _database.ReadAsync(c => c.Table<KnownFirst.Data.Entities.PreparationCandidateEntity>().FirstAsync());
        Assert.AreEqual(PreparationCandidateStatus.ResultReady, candidate.Status);

        await faultyPreparation.CancelPrefetchAsync();

        // Retry without the injected fault succeeds, proving rollback left a genuinely clean, retryable state.
        await _preparation.AcceptAsync(item.CandidateId, InputFrom(item), CardDirectionPreference.Both);
        var retriedCandidate = await _database.ReadAsync(c => c.Table<KnownFirst.Data.Entities.PreparationCandidateEntity>().FirstAsync());
        Assert.AreEqual(PreparationCandidateStatus.Prepared, retriedCandidate.Status);
    }

    private PreparationService CreatePreparationService(
        MutableProvider provider, IPreparationFaultInjector? faultInjector = null) => new(
        _database,
        new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            new LexicalCacheRepository(_database),
            new LexicalLookupProviderResolver([provider])),
        _clock,
        diagnosticLog: null,
        faultInjector: faultInjector);

    private async Task<int> ImportSingleUnknownAsync(string content, string unknownTerm)
    {
        var request = new ImportTextRequest($"Document {Guid.NewGuid():N}", content, "en", LexicalLookupMode.Definition, null);
        var result = await _review.ImportAsync(request);
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);
        var wordId = -1;
        while (await _review.GetCurrentCandidateAsync() is { } candidate)
        {
            if (string.Equals(candidate.Candidate, unknownTerm, StringComparison.OrdinalIgnoreCase))
            {
                wordId = candidate.WordId;
            }

            await _review.DecideAsync(candidate.WordId, WordStatus.UnknownBacklog);
        }

        Assert.AreNotEqual(-1, wordId);
        return wordId;
    }

    private async Task<int> InsertSessionAsync()
    {
        await _database.ReadAsync(async connection =>
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO PreparationSessions (Status, Method, TotalItems, CompletedItems, StartedAtUtc, UpdatedAtUtc)
                VALUES (0, 0, 1, 0, ?, ?)
                """,
                Now, Now);
            return true;
        });
        return await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT last_insert_rowid()"));
    }

    private async Task<int> InsertCandidateAsync(int sessionId, int wordId, int order)
    {
        await _database.ReadAsync(async connection =>
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO PreparationCandidates (SessionId, WordId, "Order", Status, ResultJson, SelectedMeaningIndex, LastErrorCode, LookupAttemptCount, UpdatedAtUtc)
                VALUES (?, ?, ?, 0, '', 0, '', 0, ?)
                """,
                sessionId, wordId, order, Now);
            return true;
        });
        return await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT last_insert_rowid()"));
    }

    private async Task<PreparationItem> ReloadCompletedItemAsync(int candidateId)
    {
        var candidate = await _database.ReadAsync(c => c.Table<KnownFirst.Data.Entities.PreparationCandidateEntity>()
            .Where(item => item.Id == candidateId).FirstAsync());
        var result = PreparationCandidatePayloadCodec.Read(candidate.ResultJson).AnyResult!;
        return new PreparationItem(
            candidate.SessionId, candidate.Id, candidate.WordId, "bank", TokenKind.Word, "en", "de",
            1, 1, 1, PreparationMethod.Manual, candidate.Status, [], result, candidate.SelectedMeaningIndex, null);
    }

    private static LexicalMeaning Meaning(string providerMeaningId, string partOfSpeech = "noun") =>
        new(providerMeaningId, partOfSpeech, $"Definition for {providerMeaningId}", $"Translation {providerMeaningId}", null, []);

    private static PreparedMeaningInput InputFrom(PreparationItem item) => InputFrom(item, item.SelectedMeaningIndex);

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

    private static PreparedMeaningInput ManualInput(string term, string meaningId) => new(
        meaningId, null, null, $"Definition for {term.ToLowerInvariant()}", null, null, [],
        "Manual", string.Empty, string.Empty, null, string.Empty);

    private static PreparedMeaningInput ManualInputWithTranslation(string term, string meaningId, string translation, string definition) => new(
        meaningId, null, translation, definition, null, null, [],
        "Manual", string.Empty, string.Empty, null, string.Empty);

    private sealed class RecordingFaultInjector(string checkpointToFail) : IPreparationFaultInjector
    {
        public void AtCheckpoint(string checkpointName)
        {
            if (string.Equals(checkpointName, checkpointToFail, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Injected fault at checkpoint '{checkpointName}'.");
            }
        }
    }

    private sealed class MutableProvider(FakeClock clock) : IDictionaryLookupProvider
    {
        private readonly ConcurrentQueue<LexicalLookupRequest> _requests = new();

        public Func<LexicalLookupRequest, IReadOnlyList<LexicalMeaning>> MeaningsFactory { get; set; } =
            _ => [new LexicalMeaning("primary", "noun", "Definition", null, null, [])];

        public string ProviderName => "Wiktionary";

        public int ProviderSchemaVersion => 1;

        public Task<LexicalResult> LookupAsync(LexicalLookupRequest request, CancellationToken cancellationToken = default)
        {
            _requests.Enqueue(request);
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
