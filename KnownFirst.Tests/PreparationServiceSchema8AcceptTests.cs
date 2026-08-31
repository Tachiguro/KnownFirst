using System.Collections.Concurrent;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Migrations.Schema13;
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
        // This class characterizes PreparationService.AcceptAsync's data-shape/rollback behavior, not
        // literal-PRAGMA-version behavior (Schema 9-11 share Schema 8's meaning-centric data model exactly,
        // and every capability family here routes them through the identical Schema-8-shape handlers). The
        // fixture upgrades immediately after construction so TextReviewService's review-selection/completion
        // setup methods, which now require the historical Schema-12 shape, keep working. The two tests below
        // that inspect PRAGMA user_version therefore assert the fixture's explicit Schema-12 version.
        await _database.UpgradeToHistoricalSchema12Async();
        _clock = new FakeClock(Now);
        _review = new TextReviewService(
            _database, new TextAnalyzer(), new DisabledEnhancedRecognitionSettings(), new FixtureGermanLexicon());
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
    public async Task Accept_ManualDefinitionWithoutLexicalResult_PersistsAndAdvances()
    {
        var request = new ImportTextRequest(
            $"Document {Guid.NewGuid():N}",
            "Die Waschmaschine laeuft.",
            "de",
            LexicalLookupMode.Definition,
            null);
        var result = await _review.ImportAsync(request);
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);

        var wordId = -1;
        while (await _review.GetCurrentCandidateAsync() is { } candidate)
        {
            if (string.Equals(candidate.Candidate, "Waschmaschine", StringComparison.OrdinalIgnoreCase))
            {
                wordId = candidate.WordId;
                await _review.DecideAsync(candidate.WordId, WordStatus.UnknownBacklog);
            }
            else
            {
                await _review.DecideAsync(candidate.WordId, WordStatus.Known);
            }
        }

        Assert.AreNotEqual(-1, wordId);
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.GetCurrentAsync();
        Assert.IsNotNull(item);
        Assert.IsNull(item.Result, "manual acceptance must not require a dictionary lookup");

        var input = ManualDefinitionInput("Ein Haushaltsgeraet zum Waschen von Kleidung.");
        await _preparation.AcceptAsync(item.CandidateId, input, CardDirectionPreference.Both);

        var meaning = await _database.ReadAsync(connection => connection.Table<KnownFirst.Data.Entities.MeaningEntity>()
            .Where(candidateMeaning => candidateMeaning.WordId == wordId)
            .ToListAsync());
        Assert.HasCount(1, meaning);
        Assert.AreEqual("Ein Haushaltsgeraet zum Waschen von Kleidung.", meaning[0].Definition);

        var candidateAfter = await _database.ReadAsync(connection => connection.FindAsync<KnownFirst.Data.Entities.PreparationCandidateEntity>(item.CandidateId));
        Assert.AreEqual(PreparationCandidateStatus.Prepared, candidateAfter!.Status);
        var sessionAfter = await _database.ReadAsync(connection => connection.FindAsync<KnownFirst.Data.Entities.PreparationSessionEntity>(item.SessionId));
        Assert.AreEqual(PreparationSessionStatus.Completed, sessionAfter!.Status);
        Assert.IsNull(await _preparation.GetCurrentAsync());

        var persistedEnvelope = PreparationCandidatePayloadCodec.Read(candidateAfter.ResultJson).Envelope!;
        Assert.IsNull(persistedEnvelope.Result);
        Assert.IsEmpty(persistedEnvelope.ResolvedProviderMeaningIndexes);
        Assert.IsNotEmpty(persistedEnvelope.FrozenEvidence);
        Assert.AreEqual(0, _provider.RequestCount);
        Assert.AreEqual(string.Empty, meaning[0].SelectedMeaningId);
        Assert.AreEqual(string.Empty, meaning[0].Source);
        Assert.AreEqual(string.Empty, meaning[0].SourceProject);
        Assert.AreEqual(string.Empty, meaning[0].SourcePageTitle);
        Assert.IsNull(meaning[0].SourceRevisionId);
    }

    [TestMethod]
    public async Task Accept_ManualTranslationWithoutLexicalResult_PersistsAndAdvances()
    {
        var wordId = await ImportWithOnlyThisWordUnknownAsync(
            "Die Maschine arbeitet.",
            "Maschine",
            "de",
            LexicalLookupMode.Translation,
            "en");
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.GetCurrentAsync();
        Assert.IsNotNull(item);
        Assert.AreEqual(LexicalLookupMode.Translation, item.LookupMode);
        Assert.AreEqual("en", item.TargetLanguage);
        Assert.IsNull(item.Result);

        await _preparation.AcceptAsync(
            item.CandidateId,
            ManualTranslationInput("machine"),
            CardDirectionPreference.Both);

        var meaning = await _database.ReadAsync(connection => connection.Table<KnownFirst.Data.Entities.MeaningEntity>()
            .Where(candidateMeaning => candidateMeaning.WordId == wordId)
            .FirstAsync());
        Assert.AreEqual("machine", meaning.Translation);
        Assert.AreEqual(string.Empty, meaning.Definition);
        Assert.AreEqual(string.Empty, meaning.Source);
        Assert.IsNull(await _preparation.GetCurrentAsync());
        Assert.AreEqual(0, _provider.RequestCount);
    }

    [TestMethod]
    public async Task Accept_ManualDefinitionContextOverridesConflictingCallerModeAndDropsTranslation()
    {
        var wordId = await ImportWithOnlyThisWordUnknownAsync("bank protects money.", "bank");
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.GetCurrentAsync();
        Assert.AreEqual(LexicalLookupMode.Definition, item!.LookupMode);

        await _preparation.AcceptAsync(
            item.CandidateId,
            ManualDefinitionInput("A financial institution.") with
            {
                Translation = "must not persist",
                ManualInputMode = LexicalLookupMode.Translation
            },
            CardDirectionPreference.Both);

        var meaning = await _database.ReadAsync(connection => connection.Table<KnownFirst.Data.Entities.MeaningEntity>()
            .Where(candidateMeaning => candidateMeaning.WordId == wordId)
            .FirstAsync());
        Assert.AreEqual("A financial institution.", meaning.Definition);
        Assert.AreEqual(string.Empty, meaning.Translation);
        Assert.AreEqual("A financial institution.", meaning.TranslationOrDefinition);
    }

    [TestMethod]
    public async Task Accept_ManualTranslationContextOverridesConflictingCallerModeAndDropsDefinition()
    {
        var wordId = await ImportWithOnlyThisWordUnknownAsync(
            "Die Maschine arbeitet.",
            "Maschine",
            "de",
            LexicalLookupMode.Translation,
            "en");
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.GetCurrentAsync();
        Assert.AreEqual(LexicalLookupMode.Translation, item!.LookupMode);

        await _preparation.AcceptAsync(
            item.CandidateId,
            ManualTranslationInput("machine") with
            {
                Definition = "must not persist",
                ManualInputMode = LexicalLookupMode.Definition
            },
            CardDirectionPreference.Both);

        var meaning = await _database.ReadAsync(connection => connection.Table<KnownFirst.Data.Entities.MeaningEntity>()
            .Where(candidateMeaning => candidateMeaning.WordId == wordId)
            .FirstAsync());
        Assert.AreEqual("machine", meaning.Translation);
        Assert.AreEqual(string.Empty, meaning.Definition);
        Assert.AreEqual("machine", meaning.TranslationOrDefinition);
    }

    [TestMethod]
    public async Task Accept_ManualLegacyCombinedContextRetainsDefinitionAndTranslation()
    {
        var wordId = await ImportWithOnlyThisWordUnknownAsync("Die Maschine arbeitet.", "Maschine", "de");
        await SetDocumentLookupSettingsAsync(wordId, LexicalLookupMode.DefinitionAndTranslation, "en");
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.GetCurrentAsync();
        Assert.AreEqual(LexicalLookupMode.DefinitionAndTranslation, item!.LookupMode);

        await _preparation.AcceptAsync(
            item.CandidateId,
            ManualCombinedInput("Ein technisches Geraet.", "machine") with
            {
                ManualInputMode = LexicalLookupMode.Definition
            },
            CardDirectionPreference.Both);

        var meaning = await _database.ReadAsync(connection => connection.Table<KnownFirst.Data.Entities.MeaningEntity>()
            .Where(candidateMeaning => candidateMeaning.WordId == wordId)
            .FirstAsync());
        Assert.AreEqual("Ein technisches Geraet.", meaning.Definition);
        Assert.AreEqual("machine", meaning.Translation);
    }

    [TestMethod]
    public async Task Accept_RepeatedManualExactMeaning_ReusesSenseMeaningAndCards()
    {
        var wordId = await ImportWithOnlyThisWordUnknownAsync("bank fees apply here.", "bank");
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var first = await _preparation.GetCurrentAsync();
        await _preparation.AcceptAsync(
            first!.CandidateId,
            ManualDefinitionInput("A financial institution."),
            CardDirectionPreference.Both);

        var original = await ReadManualGraphCountsAsync(wordId);
        Assert.AreEqual(1, original.Senses);
        Assert.AreEqual(1, original.Meanings);
        Assert.AreEqual(2, original.Cards);
        Assert.AreEqual(1, original.Contexts);

        await ImportAdditionalEvidenceAsync(
            "A bank closed early. Nonce zzzmanualrepeat appeared.",
            "bank",
            wordId);
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var repeated = await _preparation.GetCurrentAsync();
        Assert.AreEqual(wordId, repeated!.WordId);

        await _preparation.AcceptAsync(
            repeated.CandidateId,
            ManualDefinitionInput("A financial institution."),
            CardDirectionPreference.Both);

        var after = await ReadManualGraphCountsAsync(wordId);
        Assert.AreEqual(original.Senses, after.Senses);
        Assert.AreEqual(original.Meanings, after.Meanings);
        Assert.AreEqual(original.Cards, after.Cards);
        Assert.AreEqual(2, after.Contexts, "genuinely new frozen evidence must link to the reused Meaning/Sense");
        Assert.AreEqual(PreparationCandidateStatus.Prepared, await ReadCandidateStatusAsync(repeated.CandidateId));
        var session = await _database.ReadAsync(connection => connection.FindAsync<KnownFirst.Data.Entities.PreparationSessionEntity>(repeated.SessionId));
        Assert.AreEqual(1, session!.CompletedItems);
        Assert.AreEqual(PreparationSessionStatus.Completed, session.Status);
    }

    [TestMethod]
    public async Task Accept_RepeatedManualDifferentMeaning_CreatesDistinctSenseAndMeaning()
    {
        var wordId = await ImportWithOnlyThisWordUnknownAsync("bank fees apply here.", "bank");
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var first = await _preparation.GetCurrentAsync();
        await _preparation.AcceptAsync(
            first!.CandidateId,
            ManualDefinitionInput("A financial institution."),
            CardDirectionPreference.Both);

        await ImportAdditionalEvidenceAsync(
            "A bank rose above the river. Nonce zzzmanualdifferent appeared.",
            "bank",
            wordId);
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var different = await _preparation.GetCurrentAsync();
        await _preparation.AcceptAsync(
            different!.CandidateId,
            ManualDefinitionInput("The land beside a river."),
            CardDirectionPreference.Both);

        var after = await ReadManualGraphCountsAsync(wordId);
        Assert.AreEqual(2, after.Senses);
        Assert.AreEqual(2, after.Meanings);
        Assert.AreEqual(4, after.Cards);
        Assert.AreEqual(2, after.Contexts);
    }

    [TestMethod]
    public async Task Accept_ManualFallbackAfterNoProviderMeaning_PersistsAndAdvances()
    {
        var wordId = await ImportWithOnlyThisWordUnknownAsync("bank protects money.", "bank");
        _provider.MeaningsFactory = _ => [];
        await _preparation.StartAsync(PreparationMethod.AutomaticOnline, 1);
        var item = await _preparation.LookupCurrentAsync();
        Assert.IsNotNull(item);
        Assert.IsNotNull(item.Result);
        Assert.IsEmpty(item.Result.Meanings);

        await _preparation.AcceptAsync(
            item.CandidateId,
            ManualDefinitionInput("A financial institution entered manually."),
            CardDirectionPreference.Both);

        var meaning = await _database.ReadAsync(connection => connection.Table<KnownFirst.Data.Entities.MeaningEntity>()
            .Where(candidateMeaning => candidateMeaning.WordId == wordId)
            .FirstAsync());
        Assert.AreEqual("A financial institution entered manually.", meaning.Definition);
        Assert.AreEqual(string.Empty, meaning.SelectedMeaningId);
        Assert.AreEqual(string.Empty, meaning.Source);
        Assert.AreEqual(string.Empty, meaning.SourceProject);
        Assert.AreEqual(string.Empty, meaning.SourcePageTitle);
        Assert.IsNull(meaning.SourceRevisionId);

        var candidate = await _database.ReadAsync(connection => connection.FindAsync<KnownFirst.Data.Entities.PreparationCandidateEntity>(item.CandidateId));
        var envelope = PreparationCandidatePayloadCodec.Read(candidate!.ResultJson).Envelope!;
        Assert.IsNotNull(envelope.Result, "the genuine provider result remains frozen in the candidate envelope");
        Assert.IsEmpty(envelope.Result.Meanings);
        Assert.IsEmpty(envelope.ResolvedProviderMeaningIndexes);
        Assert.IsNull(await _preparation.GetCurrentAsync());
        Assert.AreEqual(1, _provider.RequestCount);
    }

    [TestMethod]
    public async Task Accept_ManualEntryRejectsMissingDefinitionForDefinitionMode()
    {
        await ImportWithOnlyThisWordUnknownAsync("bank protects money.", "bank");
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.GetCurrentAsync();

        var exception = await Assert.ThrowsExactlyAsync<PreparationInputValidationException>(() =>
            _preparation.AcceptAsync(
                item!.CandidateId,
                ManualDefinitionInput(string.Empty) with { Translation = "must not satisfy definition mode" },
                CardDirectionPreference.Both));

        Assert.AreEqual(PreparationInputValidationReason.DefinitionRequired, exception.Reason);
        Assert.AreEqual(0, await _database.ReadAsync(connection => connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Meanings")));
        Assert.AreEqual(PreparationCandidateStatus.Pending, await ReadCandidateStatusAsync(item!.CandidateId));
    }

    [TestMethod]
    public async Task Accept_ManualEntryRejectsMissingTranslationForTranslationMode()
    {
        await ImportWithOnlyThisWordUnknownAsync(
            "Die Maschine arbeitet.",
            "Maschine",
            "de",
            LexicalLookupMode.Translation,
            "en");
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.GetCurrentAsync();

        var exception = await Assert.ThrowsExactlyAsync<PreparationInputValidationException>(() =>
            _preparation.AcceptAsync(
                item!.CandidateId,
                ManualTranslationInput(string.Empty) with { Definition = "must not satisfy translation mode" },
                CardDirectionPreference.Both));

        Assert.AreEqual(PreparationInputValidationReason.TranslationRequired, exception.Reason);
        Assert.AreEqual(0, await _database.ReadAsync(connection => connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Meanings")));
        Assert.AreEqual(PreparationCandidateStatus.Pending, await ReadCandidateStatusAsync(item!.CandidateId));
    }

    [TestMethod]
    public async Task Accept_ManualLegacyCombinedModeRequiresDefinitionOrTranslation()
    {
        var wordId = await ImportWithOnlyThisWordUnknownAsync("Die Maschine arbeitet.", "Maschine", "de");
        await SetDocumentLookupSettingsAsync(wordId, LexicalLookupMode.DefinitionAndTranslation, "en");
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.GetCurrentAsync();
        Assert.AreEqual(LexicalLookupMode.DefinitionAndTranslation, item!.LookupMode);

        var exception = await Assert.ThrowsExactlyAsync<PreparationInputValidationException>(() =>
            _preparation.AcceptAsync(
                item.CandidateId,
                ManualCombinedInput(string.Empty, string.Empty),
                CardDirectionPreference.Both));

        Assert.AreEqual(PreparationInputValidationReason.AnswerRequired, exception.Reason);
        Assert.AreEqual(PreparationCandidateStatus.Pending, await ReadCandidateStatusAsync(item.CandidateId));

        await _preparation.AcceptAsync(
            item.CandidateId,
            ManualCombinedInput(string.Empty, "machine"),
            CardDirectionPreference.Both);
        Assert.IsNull(await _preparation.GetCurrentAsync());
    }

    [TestMethod]
    public async Task Accept_ManualWithoutLexicalResult_FaultRollsBackAndLeavesCandidateRetryable()
    {
        var wordId = await ImportWithOnlyThisWordUnknownAsync("bank protects money.", "bank");
        var faultyPreparation = CreatePreparationService(
            _provider,
            new RecordingFaultInjector(PreparationSchema8Checkpoints.AfterMeaningInsert));
        await faultyPreparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await faultyPreparation.GetCurrentAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => faultyPreparation.AcceptAsync(
            item!.CandidateId,
            ManualDefinitionInput("A financial institution."),
            CardDirectionPreference.Both));

        Assert.AreEqual(0, await _database.ReadAsync(connection => connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Senses")));
        Assert.AreEqual(0, await _database.ReadAsync(connection => connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Meanings")));
        Assert.AreEqual(0, await _database.ReadAsync(connection => connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningCards")));
        Assert.AreEqual(PreparationCandidateStatus.Pending, await ReadCandidateStatusAsync(item!.CandidateId));
        var word = await _database.ReadAsync(connection => connection.FindAsync<KnownFirst.Data.Entities.WordEntity>(wordId));
        Assert.AreEqual(PreparationState.Preparing, word!.PreparationState);

        await faultyPreparation.CancelPrefetchAsync();
        await _preparation.AcceptAsync(
            item.CandidateId,
            ManualDefinitionInput("A financial institution."),
            CardDirectionPreference.Both);
        Assert.AreEqual(PreparationCandidateStatus.Prepared, await ReadCandidateStatusAsync(item.CandidateId));
    }

    [TestMethod]
    public async Task Accept_ManualWithoutLexicalResult_UpdatesProgressExactlyOnce()
    {
        var firstWordId = await ImportWithOnlyThisWordUnknownAsync("bank protects money.", "bank");
        var secondWordId = await ImportWithOnlyThisWordUnknownAsync("truck carries goods.", "truck");
        await _preparation.StartAsync(PreparationMethod.Manual, 2);
        var first = await _preparation.GetCurrentAsync();

        await _preparation.AcceptAsync(
            first!.CandidateId,
            ManualDefinitionInput("First manual definition."),
            CardDirectionPreference.Both);

        var afterFirst = await _database.ReadAsync(connection => connection.FindAsync<KnownFirst.Data.Entities.PreparationSessionEntity>(first.SessionId));
        Assert.AreEqual(1, afterFirst!.CompletedItems);
        Assert.AreEqual(PreparationSessionStatus.Active, afterFirst.Status);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _preparation.AcceptAsync(
            first.CandidateId,
            ManualDefinitionInput("First manual definition."),
            CardDirectionPreference.Both));
        var afterDuplicate = await _database.ReadAsync(connection => connection.FindAsync<KnownFirst.Data.Entities.PreparationSessionEntity>(first.SessionId));
        Assert.AreEqual(1, afterDuplicate!.CompletedItems);

        var next = await _preparation.GetCurrentAsync();
        Assert.IsNotNull(next);
        Assert.AreNotEqual(first.WordId, next.WordId);
        Assert.IsTrue(next.WordId == firstWordId || next.WordId == secondWordId);
        await _preparation.AcceptAsync(
            next.CandidateId,
            ManualDefinitionInput("Second manual definition."),
            CardDirectionPreference.Both);

        var completed = await _database.ReadAsync(connection => connection.FindAsync<KnownFirst.Data.Entities.PreparationSessionEntity>(first.SessionId));
        Assert.AreEqual(2, completed!.CompletedItems);
        Assert.AreEqual(PreparationSessionStatus.Completed, completed.Status);
        Assert.IsNull(await _preparation.GetCurrentAsync());
    }

    [TestMethod]
    public async Task AcceptProgressionCoordinator_SaveSucceedsLoadFailsRetryPreservesSingleAcceptedProgress()
    {
        await ImportWithOnlyThisWordUnknownAsync("bank protects money.", "bank");
        await ImportWithOnlyThisWordUnknownAsync("truck carries goods.", "truck");
        await _preparation.StartAsync(PreparationMethod.Manual, 2);
        var first = await _preparation.GetCurrentAsync();
        Assert.IsNotNull(first);

        var coordinator = new PreparationProgressionCoordinator();
        var acceptCalls = 0;
        var progressionCalls = 0;
        var failNextLoad = true;
        async Task CommitAsync()
        {
            acceptCalls++;
            await _preparation.AcceptAsync(
                first!.CandidateId,
                ManualDefinitionInput("First committed manual definition."),
                CardDirectionPreference.Both);
        }

        async Task LoadNextAsync(PreparationMethod method)
        {
            Assert.AreEqual(PreparationMethod.Manual, method);
            progressionCalls++;
            if (failNextLoad)
            {
                throw new InvalidOperationException("synthetic next-item load failure");
            }

            var next = await _preparation.GetCurrentAsync();
            Assert.IsNotNull(next);
            Assert.AreNotEqual(first!.CandidateId, next!.CandidateId);
        }

        var firstProgressionSucceeded = await coordinator.CommitAndProgressAsync(
            PreparationMethod.Manual,
            CommitAsync,
            LoadNextAsync);

        Assert.IsFalse(firstProgressionSucceeded);
        var afterFailure = await _database.ReadAsync(connection => connection.FindAsync<KnownFirst.Data.Entities.PreparationSessionEntity>(first!.SessionId));
        Assert.AreEqual(1, afterFailure!.CompletedItems);
        Assert.AreEqual(PreparationSessionStatus.Active, afterFailure.Status);
        Assert.AreEqual(1, acceptCalls);
        Assert.AreEqual(1, progressionCalls);

        failNextLoad = false;
        Assert.IsTrue(await coordinator.RetryProgressionAsync(LoadNextAsync));

        var afterRetry = await _database.ReadAsync(connection => connection.FindAsync<KnownFirst.Data.Entities.PreparationSessionEntity>(first!.SessionId));
        Assert.AreEqual(1, afterRetry!.CompletedItems);
        Assert.AreEqual(PreparationSessionStatus.Active, afterRetry.Status);
        Assert.AreEqual(1, acceptCalls, "retry must not accept the committed candidate again");
        Assert.AreEqual(2, progressionCalls);
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
    public async Task Accept_OneOfFourProviderMeanings_CompletesCandidateImmediately_OtherMeaningsNeverPersisted()
    {
        // KF-MEANING-002 regression coverage: accepting one explicitly selected meaning must complete the
        // candidate immediately. Before this fix, the candidate required every provider meaning to be
        // resolved and stayed active after a single accept.
        var wordId = await ImportSingleUnknownAsync("bank text here.", "bank");
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

        // Only the first provider meaning is ever explicitly selected and accepted.
        await _preparation.AcceptAsync(candidateId, InputFrom(item, 0), CardDirectionPreference.Both);

        var candidate = await _database.ReadAsync(c => c.Table<KnownFirst.Data.Entities.PreparationCandidateEntity>()
            .Where(x => x.Id == candidateId).FirstAsync());
        Assert.AreEqual(PreparationCandidateStatus.Prepared, candidate.Status, "the candidate must complete after one accepted meaning");
        var envelope = PreparationCandidatePayloadCodec.Read(candidate.ResultJson).Envelope!;
        CollectionAssert.AreEqual(new[] { 0 }, envelope.ResolvedProviderMeaningIndexes.ToArray());

        var word = await _database.ReadAsync(c => c.Table<KnownFirst.Data.Entities.WordEntity>().Where(w => w.Id == wordId).FirstAsync());
        Assert.AreEqual(PreparationState.Prepared, word.PreparationState);

        // The three unselected provider meanings were never inspected, matched, or persisted.
        var senseCount = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Senses WHERE WordId = ?", wordId));
        Assert.AreEqual(1, senseCount);
        var meaningCount = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Meanings WHERE WordId = ?", wordId));
        Assert.AreEqual(1, meaningCount);
        // MeaningRanker may reorder the provider's meanings, so the persisted meaning is whichever one
        // ended up at index 0 for this candidate — not necessarily the first one the provider returned.
        var selectedProviderMeaningId = item.Result!.Meanings[0].MeaningId;
        var persistedProviderMeaningId = await _database.ReadAsync(c =>
            c.ExecuteScalarAsync<string>("SELECT SelectedMeaningId FROM Meanings WHERE WordId = ?", wordId));
        Assert.AreEqual(selectedProviderMeaningId, persistedProviderMeaningId);
        foreach (var unselected in item.Result.Meanings.Skip(1))
        {
            Assert.AreNotEqual(unselected.MeaningId, persistedProviderMeaningId);
        }

        var senseId = await ReadSenseIdAsync(wordId);
        var cardCount = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningCards WHERE SenseId = ?", senseId));
        Assert.AreEqual(2, cardCount, "Both directions requested -> exactly two cards for the one selected Sense");
    }

    [TestMethod]
    public async Task Accept_MeaningExactAgainstExistingSense_ReusesSenseAndCompletesAfterOneAccept()
    {
        var wordId = await ImportSingleUnknownAsync("bank text here.", "bank");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution")];
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var firstItem = await _preparation.LookupCurrentAsync();
        await _preparation.AcceptAsync(firstItem!.CandidateId, InputFrom(firstItem), CardDirectionPreference.Both);

        // A second, independent candidate for the same already-prepared word (simulating new context
        // surfacing it again), whose provider now returns two meanings; only the first is ever selected
        // and accepted. KF-MEANING-002: the second, unselected meaning is never inspected or auto-linked —
        // it is simply never resolved, since resolving it is no longer required for completion.
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
        CollectionAssert.AreEqual(new[] { 0 }, envelope.ResolvedProviderMeaningIndexes.ToArray());

        // The selected meaning matched the existing Sense exactly (reused, not duplicated) — still
        // exactly one Sense for the Word.
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
        Assert.AreEqual(12, userVersion);

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

    [TestMethod]
    public async Task AcceptSchema13_CreatesCleanFsrsStateWithEachNewCard()
    {
        var wordId = await ImportSingleUnknownAsync("bank text here.", "bank");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution")];
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();
        await _database.ReadAsync(async connection =>
        {
            await Schema13DormantMigration.ApplyAsync(connection);
            return true;
        });

        await _preparation.AcceptAsync(item!.CandidateId, InputFrom(item), CardDirectionPreference.Both);

        var states = await _database.ReadAsync(connection => connection.QueryAsync<FsrsStateProbe>(
            """
            SELECT f.CardId, f.State, f.Stability, f.Difficulty, f.LastReviewedAtUtc, f.StepIndex, f.DueAtUtc
            FROM FsrsCardStates f
            JOIN LearningCards c ON c.Id = f.CardId
            WHERE c.WordId = ?
            ORDER BY f.CardId
            """,
            wordId));
        Assert.HasCount(2, states);
        Assert.IsTrue(states.All(state => state.State == 0
            && state.Stability is null
            && state.Difficulty is null
            && state.LastReviewedAtUtc is null
            && state.StepIndex is null
            && state.DueAtUtc is null));

        // Deliberate split-brain fixture: legacy columns claim both cards are overdue Review cards.
        // Preparation/workflow/learning read paths must continue to observe the clean FSRS New projection.
        await _database.ReadAsync(async connection =>
        {
            await connection.ExecuteAsync(
                "UPDATE LearningCards SET State = ?, DueAtUtc = ? WHERE WordId = ?",
                (int)CardState.Review,
                Now.AddDays(-1),
                wordId);
            return true;
        });

        var overview = await _preparation.GetOverviewAsync();
        Assert.AreEqual(0, overview.DueCardCount);
        Assert.AreEqual(1, overview.PreparedNewItemCount);

        var workflow = await new WorkflowStateService(_database, _clock).GetSnapshotAsync();
        Assert.AreEqual(0, workflow.DueCardCount);
        Assert.AreEqual(1, workflow.PreparedNewItemCount);
        Assert.AreEqual(WorkflowPrimaryAction.StartLearning, workflow.PrimaryAction);

        var learning = new LearningService(
            _database,
            new SimpleSpacedRepetitionScheduler(),
            new SpellingAnswerComparer(),
            _clock);
        var load = await learning.GetOrStartAsync();
        Assert.IsNotNull(load.Card);
        Assert.AreEqual(CardState.New, load.Card.State);
    }

    [TestMethod]
    public async Task AcceptSchema13_FaultAfterCardInsertion_RollsBackCardsAndFsrsStates()
    {
        await ImportSingleUnknownAsync("bank text here.", "bank");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution")];
        var faultyPreparation = CreatePreparationService(
            _provider,
            new RecordingFaultInjector(PreparationSchema8Checkpoints.AfterCardInsert));
        await faultyPreparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await faultyPreparation.LookupCurrentAsync();
        await _database.ReadAsync(async connection =>
        {
            await Schema13DormantMigration.ApplyAsync(connection);
            return true;
        });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => faultyPreparation.AcceptAsync(
            item!.CandidateId,
            InputFrom(item),
            CardDirectionPreference.Both));

        Assert.AreEqual(0, await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningCards")));
        Assert.AreEqual(0, await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM FsrsCardStates")));
    }

    private sealed class FsrsStateProbe
    {
        public int CardId { get; set; }
        public int State { get; set; }
        public double? Stability { get; set; }
        public double? Difficulty { get; set; }
        public string? LastReviewedAtUtc { get; set; }
        public int? StepIndex { get; set; }
        public string? DueAtUtc { get; set; }
    }

    // ========== KF-MEANING-001 Slice 4: answer-variant and assignment initialization on accept ==========

    private sealed class AssignmentProbeRow
    {
        public int Id { get; set; }
        public string StableId { get; set; } = string.Empty;
        public int SenseId { get; set; }
        public int CardDirection { get; set; }
        public int AnswerVariantId { get; set; }
        public int Requirement { get; set; }
        public bool IsPreferred { get; set; }
        public DateTime? RequiredSinceUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string NormalizedText { get; set; } = string.Empty;
        public string AnswerLanguage { get; set; } = string.Empty;
    }

    private Task<List<AssignmentProbeRow>> ReadAssignmentsAsync(int senseId) =>
        _database.ReadAsync(connection => connection.QueryAsync<AssignmentProbeRow>(
            """
            SELECT a.Id, a.StableId, a.SenseId, a.CardDirection, a.AnswerVariantId, a.Requirement,
                   a.IsPreferred, a.RequiredSinceUtc, a.CreatedAtUtc, v.NormalizedText, v.AnswerLanguage
            FROM SenseAnswerVariantAssignments a JOIN AnswerVariants v ON v.Id = a.AnswerVariantId
            WHERE a.SenseId = ?
            ORDER BY a.CardDirection, a.Id
            """,
            senseId));

    private Task<int> ReadSenseIdAsync(int wordId) =>
        _database.ReadAsync(connection =>
            connection.ExecuteScalarAsync<int>("SELECT Id FROM Senses WHERE WordId = ? ORDER BY Id LIMIT 1", wordId));

    [TestMethod]
    public async Task AcceptSchema8_CreatesOnePrimaryRequiredPreferredAssignmentPerCreatedDirection()
    {
        var wordId = await ImportSingleUnknownAsync("bank protects money.", "bank");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution")];

        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();
        await _preparation.AcceptAsync(item!.CandidateId, InputFrom(item), CardDirectionPreference.Both);

        Assert.AreEqual(12, await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("PRAGMA user_version")));

        var senseId = await ReadSenseIdAsync(wordId);
        var assignments = await ReadAssignmentsAsync(senseId);

        foreach (var direction in new[] { CardDirection.MeaningToTerm, CardDirection.TermToMeaning })
        {
            var forDirection = assignments.Where(a => a.CardDirection == (int)direction).ToList();
            var primaries = forDirection
                .Where(a => a.Requirement == (int)AnswerVariantRequirement.Required)
                .ToList();

            Assert.HasCount(1, primaries, $"{direction} must have exactly one Required primary");
            Assert.IsTrue(primaries[0].IsPreferred);
            Assert.IsNotNull(primaries[0].RequiredSinceUtc);
            Assert.HasCount(1, forDirection.Where(a => a.IsPreferred).ToList());
            Assert.MatchesRegex("^[0-9a-f]{32}$", primaries[0].StableId);
        }
    }

    [TestMethod]
    public async Task AcceptSchema8_PrimaryAssignment_RequiredSinceUtcEqualsCreatedAtUtc()
    {
        var wordId = await ImportSingleUnknownAsync("bank protects money.", "bank");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution")];

        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();
        await _preparation.AcceptAsync(item!.CandidateId, InputFrom(item), CardDirectionPreference.MeaningToTerm);

        var senseId = await ReadSenseIdAsync(wordId);
        var primary = (await ReadAssignmentsAsync(senseId))
            .Single(a => a.Requirement == (int)AnswerVariantRequirement.Required);

        Assert.IsNotNull(primary.RequiredSinceUtc);
        Assert.AreEqual(
            DateTime.SpecifyKind(primary.CreatedAtUtc, DateTimeKind.Utc).Ticks,
            DateTime.SpecifyKind(primary.RequiredSinceUtc!.Value, DateTimeKind.Utc).Ticks);
    }

    [TestMethod]
    public async Task AcceptSchema8_ProviderAlternatives_AreAcceptedOnlyWithNullBoundary()
    {
        var wordId = await ImportSingleUnknownAsync("bank protects money.", "bank");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution")];

        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();
        await _preparation.AcceptAsync(item!.CandidateId, InputFrom(item), CardDirectionPreference.Both);

        var senseId = await ReadSenseIdAsync(wordId);
        var accepted = (await ReadAssignmentsAsync(senseId))
            .Where(a => a.Requirement == (int)AnswerVariantRequirement.AcceptedOnly)
            .ToList();

        foreach (var row in accepted)
        {
            Assert.IsNull(row.RequiredSinceUtc);
            Assert.IsFalse(row.IsPreferred);
        }
    }

    [TestMethod]
    public async Task AcceptSchema8_Aliases_AreAcceptedOnlyMeaningToTermOnly()
    {
        // PreparationSelectionPolicy tie-breaks equal-occurrence candidates by ordinal CanonicalTerm, so the
        // fixture text must keep "color" ordinally first among its unknown words for this Sense to be the one
        // the session actually prepares.
        var wordId = await ImportSingleUnknownAsync("color is vivid.", "color");
        _provider.MeaningsFactory = _ => [Meaning("wikt-color")];

        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();
        Assert.AreEqual(wordId, item!.WordId, "the session must prepare the aliased word, not another candidate");
        var input = InputFrom(item) with { AcceptedAliases = ["colour"] };
        await _preparation.AcceptAsync(item.CandidateId, input, CardDirectionPreference.Both);

        var senseId = await ReadSenseIdAsync(wordId);
        var assignments = await ReadAssignmentsAsync(senseId);

        var aliasRows = assignments.Where(a => a.NormalizedText == "colour").ToList();
        Assert.IsNotEmpty(aliasRows);
        foreach (var row in aliasRows)
        {
            Assert.AreEqual((int)CardDirection.MeaningToTerm, row.CardDirection, "aliases are term-side only");
            Assert.AreEqual((int)AnswerVariantRequirement.AcceptedOnly, row.Requirement);
            Assert.IsFalse(row.IsPreferred);
            Assert.IsNull(row.RequiredSinceUtc);
        }

        Assert.IsFalse(
            assignments.Any(a => a.NormalizedText == "colour" && a.CardDirection == (int)CardDirection.TermToMeaning));
    }

    [TestMethod]
    public async Task AcceptSchema8_NoValidPrimaryExpression_CreatesNoAssignment()
    {
        var wordId = await ImportSingleUnknownAsync("silent word here.", "silent");
        // Definition-only provider meaning: no Translation, so TermToMeaning has no assignable expression.
        _provider.MeaningsFactory = _ => [new LexicalMeaning("wikt-silent", "noun", "A definition", null, null, [])];

        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();
        await _preparation.AcceptAsync(item!.CandidateId, InputFrom(item), CardDirectionPreference.TermToMeaning);

        var senseId = await ReadSenseIdAsync(wordId);
        var termToMeaning = (await ReadAssignmentsAsync(senseId))
            .Where(a => a.CardDirection == (int)CardDirection.TermToMeaning)
            .ToList();

        Assert.IsEmpty(termToMeaning); // no invented fallback variant and no primary assignment
    }

    [TestMethod]
    public async Task AcceptSchema8_ExistingDirectionCard_DoesNotAlterExistingAssignment()
    {
        var wordId = await ImportSingleUnknownAsync("bank protects money.", "bank");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution")];

        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var first = await _preparation.LookupCurrentAsync();
        await _preparation.AcceptAsync(first!.CandidateId, InputFrom(first), CardDirectionPreference.MeaningToTerm);

        var senseId = await ReadSenseIdAsync(wordId);
        var before = (await ReadAssignmentsAsync(senseId))
            .Where(a => a.CardDirection == (int)CardDirection.MeaningToTerm)
            .OrderBy(a => a.Id)
            .ToList();
        Assert.IsNotEmpty(before);

        // A second acceptance for the same Sense adds the missing direction only.
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var second = await _preparation.LookupCurrentAsync();
        if (second is not null)
        {
            await _preparation.AcceptAsync(second.CandidateId, InputFrom(second), CardDirectionPreference.Both);
        }

        var after = (await ReadAssignmentsAsync(senseId))
            .Where(a => a.CardDirection == (int)CardDirection.MeaningToTerm)
            .OrderBy(a => a.Id)
            .ToList();

        Assert.HasCount(before.Count, after);
        for (var index = 0; index < before.Count; index++)
        {
            Assert.AreEqual(before[index].Id, after[index].Id);
            Assert.AreEqual(before[index].StableId, after[index].StableId);
            Assert.AreEqual(before[index].Requirement, after[index].Requirement);
            Assert.AreEqual(before[index].IsPreferred, after[index].IsPreferred);
            Assert.AreEqual(before[index].RequiredSinceUtc, after[index].RequiredSinceUtc);
        }
    }

    [TestMethod]
    public async Task AcceptSchema8_AnswerVariantDedup_ByNormalizedTriple()
    {
        // Same ordinal-first fixture requirement as AcceptSchema8_Aliases_AreAcceptedOnlyMeaningToTermOnly:
        // "color" must be the prepared word for the alias to collide with the term-side primary at all.
        var wordId = await ImportSingleUnknownAsync("color is vivid.", "color");
        _provider.MeaningsFactory = _ => [Meaning("wikt-color")];

        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();
        Assert.AreEqual(wordId, item!.WordId, "the session must prepare the aliased word, not another candidate");
        // The alias normalizes to the same text as the term-side primary: it must dedupe onto one variant.
        var input = InputFrom(item) with { AcceptedAliases = ["color"] };
        await _preparation.AcceptAsync(item.CandidateId, input, CardDirectionPreference.Both);

        var senseId = await ReadSenseIdAsync(wordId);

        var duplicateVariants = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM (
                SELECT SenseId, AnswerLanguage, NormalizedText FROM AnswerVariants WHERE SenseId = ?
                GROUP BY SenseId, AnswerLanguage, NormalizedText HAVING COUNT(*) > 1)
            """,
            senseId));
        Assert.AreEqual(0, duplicateVariants);

        var duplicateTriples = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM (
                SELECT SenseId, CardDirection, AnswerVariantId FROM SenseAnswerVariantAssignments WHERE SenseId = ?
                GROUP BY SenseId, CardDirection, AnswerVariantId HAVING COUNT(*) > 1)
            """,
            senseId));
        Assert.AreEqual(0, duplicateTriples);
    }

    // ========== KF-MEANING-002: selected-meaning-only completion regression coverage ==========

    [TestMethod]
    public async Task Accept_TermToMeaningDirectionOnly_CreatesExactlyOneCard()
    {
        var wordId = await ImportSingleUnknownAsync("bank protects money.", "bank");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution")];
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();
        await _preparation.AcceptAsync(item!.CandidateId, InputFrom(item), CardDirectionPreference.TermToMeaning);

        var senseId = await ReadSenseIdAsync(wordId);
        var cardCount = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningCards WHERE SenseId = ?", senseId));
        Assert.AreEqual(1, cardCount);
        var direction = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT Direction FROM LearningCards WHERE SenseId = ?", senseId));
        Assert.AreEqual((int)CardDirection.TermToMeaning, direction);
    }

    [TestMethod]
    public async Task Accept_MeaningToTermDirectionOnly_CreatesExactlyOneCard()
    {
        var wordId = await ImportSingleUnknownAsync("bank protects money.", "bank");
        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution")];
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();
        await _preparation.AcceptAsync(item!.CandidateId, InputFrom(item), CardDirectionPreference.MeaningToTerm);

        var senseId = await ReadSenseIdAsync(wordId);
        var cardCount = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningCards WHERE SenseId = ?", senseId));
        Assert.AreEqual(1, cardCount);
        var direction = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT Direction FROM LearningCards WHERE SenseId = ?", senseId));
        Assert.AreEqual((int)CardDirection.MeaningToTerm, direction);
    }

    [TestMethod]
    public async Task Accept_MultipleAliases_RemainAnswerVariantsOfSameSense_WithoutAdditionalCards()
    {
        // Same ordinal-first fixture requirement as the other "color" tests in this file.
        var wordId = await ImportSingleUnknownAsync("color is vivid.", "color");
        _provider.MeaningsFactory = _ => [Meaning("wikt-color")];
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();
        Assert.AreEqual(wordId, item!.WordId, "the session must prepare the aliased word, not another candidate");
        var input = InputFrom(item) with { AcceptedAliases = ["colour", "hue", "shade"] };
        await _preparation.AcceptAsync(item.CandidateId, input, CardDirectionPreference.Both);

        var senseId = await ReadSenseIdAsync(wordId);
        var assignments = await ReadAssignmentsAsync(senseId);
        foreach (var alias in new[] { "colour", "hue", "shade" })
        {
            Assert.IsTrue(assignments.Any(a => a.NormalizedText == alias), $"expected an assignment for alias '{alias}'");
        }

        var cardCount = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningCards WHERE SenseId = ?", senseId));
        Assert.AreEqual(2, cardCount, "aliases must not create additional cards");
    }

    [TestMethod]
    public async Task Accept_DuplicateSubmittedAliases_DeduplicateDeterministically()
    {
        var wordId = await ImportSingleUnknownAsync("color is vivid.", "color");
        _provider.MeaningsFactory = _ => [Meaning("wikt-color")];
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();
        Assert.AreEqual(wordId, item!.WordId, "the session must prepare the aliased word, not another candidate");
        var input = InputFrom(item) with { AcceptedAliases = ["colour", "colour", "colour"] };
        await _preparation.AcceptAsync(item.CandidateId, input, CardDirectionPreference.Both);

        var senseId = await ReadSenseIdAsync(wordId);
        var variantCount = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM AnswerVariants WHERE SenseId = ? AND NormalizedText = ?", senseId, "colour"));
        Assert.AreEqual(1, variantCount);
        var assignmentCount = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM SenseAnswerVariantAssignments a
            JOIN AnswerVariants v ON v.Id = a.AnswerVariantId
            WHERE a.SenseId = ? AND v.NormalizedText = ?
            """,
            senseId, "colour"));
        Assert.AreEqual(1, assignmentCount);
    }

    [TestMethod]
    public async Task Accept_CandidateWhoseOnlyOccurrenceIsMisattributed_CompletesWithoutContextSnapshot()
    {
        // KF-MEANING-002 context-integrity fail-safe: when every occurrence available to a candidate is
        // excluded by the WordForms attribution check, the candidate must still be acceptable — just
        // without a context snapshot, never blocked and never crashed.
        var bankWordId = await ImportSingleUnknownAsync("bank text here.", "bank");
        var otherWordId = await ImportSingleUnknownAsync("urgent notice today.", "urgent");

        var bankOccurrence = await _database.ReadAsync(c => c.Table<KnownFirst.Data.Entities.WordOccurrenceEntity>()
            .Where(o => o.WordId == bankWordId).FirstAsync());
        var urgentOccurrence = await _database.ReadAsync(c => c.Table<KnownFirst.Data.Entities.WordOccurrenceEntity>()
            .Where(o => o.WordId == otherWordId).FirstAsync());
        await _database.ReadAsync(async connection =>
        {
            // Swap: "bank" keeps only the misattributed "urgent" occurrence — its own genuine occurrence
            // moves away, so every context available to the "bank" candidate is invalid.
            await connection.ExecuteAsync("UPDATE WordOccurrences SET WordId = ? WHERE Id = ?", otherWordId, bankOccurrence.Id);
            await connection.ExecuteAsync("UPDATE WordOccurrences SET WordId = ? WHERE Id = ?", bankWordId, urgentOccurrence.Id);
            return true;
        });

        _provider.MeaningsFactory = _ => [Meaning("wikt-financial-institution")];
        await _preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await _preparation.LookupCurrentAsync();
        Assert.AreEqual(bankWordId, item!.WordId);
        Assert.IsEmpty(item.Contexts, "every available occurrence is misattributed, so no context is displayed");

        await _preparation.AcceptAsync(item.CandidateId, InputFrom(item), CardDirectionPreference.Both);

        var candidate = await _database.ReadAsync(c => c.Table<KnownFirst.Data.Entities.PreparationCandidateEntity>()
            .Where(x => x.Id == item.CandidateId).FirstAsync());
        Assert.AreEqual(PreparationCandidateStatus.Prepared, candidate.Status, "acceptance must still succeed without any valid context");

        var senseId = await ReadSenseIdAsync(bankWordId);
        var snapshotCount = await _database.ReadAsync(c => c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ContextSnapshots WHERE SenseId = ?", senseId));
        Assert.AreEqual(0, snapshotCount);
    }

    [TestMethod]
    public async Task Accept_ManualPreparation_AdvancesToNextCandidateImmediately()
    {
        var firstWordId = await ImportWithOnlyThisWordUnknownAsync("bank protects money.", "bank");
        var secondWordId = await ImportWithOnlyThisWordUnknownAsync("truck carries goods.", "truck");
        _provider.MeaningsFactory = _ => [Meaning("wikt-generic")];

        await _preparation.StartAsync(PreparationMethod.Manual, 2);
        var first = await _preparation.LookupCurrentAsync();
        Assert.IsTrue(first!.WordId == firstWordId || first.WordId == secondWordId);
        await _preparation.AcceptAsync(first.CandidateId, InputFrom(first), CardDirectionPreference.Both);

        var next = await _preparation.GetCurrentAsync();
        Assert.IsNotNull(next);
        Assert.AreNotEqual(first.WordId, next!.WordId);
        Assert.IsTrue(next.WordId == firstWordId || next.WordId == secondWordId);
    }

    [TestMethod]
    public async Task Accept_AutomaticPreparation_AdvancesToNextCandidateImmediately()
    {
        var firstWordId = await ImportWithOnlyThisWordUnknownAsync("bank protects money.", "bank");
        var secondWordId = await ImportWithOnlyThisWordUnknownAsync("truck carries goods.", "truck");
        _provider.MeaningsFactory = _ => [Meaning("wikt-generic")];

        await _preparation.StartAsync(PreparationMethod.AutomaticOnline, 2);
        var first = await _preparation.LookupCurrentAsync();
        Assert.IsTrue(first!.WordId == firstWordId || first.WordId == secondWordId);
        await _preparation.AcceptAsync(first.CandidateId, InputFrom(first), CardDirectionPreference.Both);

        var next = await _preparation.LookupCurrentAsync();
        Assert.IsNotNull(next);
        Assert.AreNotEqual(first.WordId, next!.WordId);
        Assert.IsTrue(next.WordId == firstWordId || next.WordId == secondWordId);
    }

    [TestMethod]
    public async Task AcceptSchema7_AssignmentInitialization_IsNotReached()
    {
        await using var schema7 = new TemporaryKnownFirstDatabase("knownfirst-schema7-accept-slice4");
        await schema7.InitializeAsync();

        Assert.AreEqual(7, await schema7.ReadAsync(c => c.ExecuteScalarAsync<int>("PRAGMA user_version")));

        // The Schema-8-only tables do not exist at all, so no assignment initialization can have run.
        foreach (var table in new[] { "Senses", "AnswerVariants", "SenseAnswerVariantAssignments", "AnswerVariantProgress" })
        {
            var exists = await schema7.ReadAsync(c => c.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = ?", table));
            Assert.AreEqual(0, exists, $"{table} must not exist at Schema 7");
        }

        Assert.AreEqual(
            1,
            await schema7.ReadAsync(c => c.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM pragma_table_info('LearningCards') WHERE name = 'MeaningId'")));
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

    /// <summary>Like <see cref="ImportSingleUnknownAsync"/>, but every discovered candidate other than
    /// <paramref name="unknownTerm"/> is explicitly marked Known — so a multi-candidate batch test gets
    /// exactly the intended unknown words regardless of what else the fixture text happens to contain.</summary>
    private async Task<int> ImportWithOnlyThisWordUnknownAsync(
        string content,
        string unknownTerm,
        string sourceLanguage = "en",
        LexicalLookupMode lookupMode = LexicalLookupMode.Definition,
        string? targetLanguage = null)
    {
        var request = new ImportTextRequest(
            $"Document {Guid.NewGuid():N}",
            content,
            sourceLanguage,
            lookupMode,
            targetLanguage);
        var result = await _review.ImportAsync(request);
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);
        var wordId = -1;
        while (await _review.GetCurrentCandidateAsync() is { } candidate)
        {
            if (string.Equals(candidate.Candidate, unknownTerm, StringComparison.OrdinalIgnoreCase))
            {
                wordId = candidate.WordId;
                await _review.DecideAsync(candidate.WordId, WordStatus.UnknownBacklog);
            }
            else
            {
                await _review.DecideAsync(candidate.WordId, WordStatus.Known);
            }
        }

        Assert.AreNotEqual(-1, wordId);
        return wordId;
    }

    private async Task SetDocumentLookupSettingsAsync(
        int wordId,
        LexicalLookupMode lookupMode,
        string? targetLanguage)
    {
        await _database.ReadAsync(async connection =>
        {
            var documentId = await connection.ExecuteScalarAsync<int>(
                "SELECT DocumentId FROM WordOccurrences WHERE WordId = ? ORDER BY Id LIMIT 1",
                wordId);
            await connection.ExecuteAsync(
                "UPDATE Documents SET LookupMode = ?, TargetLanguage = ?, ExplanationLanguage = ? WHERE Id = ?",
                (int)lookupMode,
                targetLanguage ?? string.Empty,
                targetLanguage ?? "de",
                documentId);
            return true;
        });
    }

    private async Task ImportAdditionalEvidenceAsync(string content, string targetTerm, int expectedWordId)
    {
        var request = new ImportTextRequest(
            $"Document {Guid.NewGuid():N}",
            content,
            "en",
            LexicalLookupMode.Definition,
            null);
        var result = await _review.ImportAsync(request);
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);

        while (await _review.GetCurrentCandidateAsync() is { } candidate)
        {
            await _review.DecideAsync(
                candidate.WordId,
                string.Equals(candidate.Candidate, targetTerm, StringComparison.OrdinalIgnoreCase)
                    ? WordStatus.UnknownBacklog
                    : WordStatus.Known);
        }

        var word = await _database.ReadAsync(connection => connection.FindAsync<KnownFirst.Data.Entities.WordEntity>(expectedWordId));
        Assert.IsNotNull(word);
        Assert.AreEqual(targetTerm, word!.CanonicalTerm, ignoreCase: true);
    }

    private Task<(int Senses, int Meanings, int Cards, int Contexts)> ReadManualGraphCountsAsync(int wordId) =>
        _database.ReadAsync(async connection =>
        {
            var senses = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Senses WHERE WordId = ?", wordId);
            var meanings = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Meanings WHERE WordId = ?", wordId);
            var cards = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningCards WHERE WordId = ?", wordId);
            var contexts = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ContextSnapshots WHERE WordId = ?", wordId);
            return (senses, meanings, cards, contexts);
        });

    private Task<PreparationCandidateStatus> ReadCandidateStatusAsync(int candidateId) =>
        _database.ReadAsync(async connection =>
        {
            var candidate = await connection.FindAsync<KnownFirst.Data.Entities.PreparationCandidateEntity>(candidateId);
            return candidate!.Status;
        });

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

    private static PreparedMeaningInput ManualDefinitionInput(string definition) => new(
        null, null, null, definition, null, null, [],
        string.Empty, string.Empty, string.Empty, null, string.Empty,
        ManualInputMode: LexicalLookupMode.Definition);

    private static PreparedMeaningInput ManualTranslationInput(string translation) => new(
        null, null, translation, string.Empty, null, null, [],
        string.Empty, string.Empty, string.Empty, null, string.Empty,
        ManualInputMode: LexicalLookupMode.Translation);

    private static PreparedMeaningInput ManualCombinedInput(string definition, string translation) => new(
        null, null, translation, definition, null, null, [],
        string.Empty, string.Empty, string.Empty, null, string.Empty,
        ManualInputMode: LexicalLookupMode.DefinitionAndTranslation);

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

        public int RequestCount => _requests.Count;

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
