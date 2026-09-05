using System.Collections.Concurrent;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Review;
using KnownFirst.Core.Settings;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema13;
using KnownFirst.Data.Targets;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.DataSafety.Merge;
using KnownFirst.Services.Lexical;
using KnownFirst.Services.Study;
using static KnownFirst.Tests.DatabaseSchema13ProductionCutoverTests;

namespace KnownFirst.Tests;

[TestClass]
public sealed class PreparationTargetCutoverTests
{
    private static readonly DateTime Now = new(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task Accept_DefinitionTarget_CreatesLearningTargetAndAnswerVariantOnSchema14()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(Now);
        var analyzer = new TextAnalyzer();
        var review = new TextReviewService(
            database,
            analyzer,
            new DisabledEnhancedRecognitionSettings(),
            new FixtureGermanLexicon());

        var provider = new MutableProvider(clock);
        var preparation = CreatePreparationService(database, provider, clock);
        var targetRepo = new LearningTargetRepository(database);

        var wordId = await ImportWithOnlyThisWordUnknownAsync(
            review, "Ein Haus ist gross.", "Haus", "de", LexicalLookupMode.Definition, null);

        var sessionId = await preparation.StartAsync(PreparationMethod.Manual, 1);
        Assert.IsTrue(sessionId > 0);

        var item = await preparation.GetCurrentAsync();
        Assert.IsNotNull(item);
        Assert.AreEqual("Haus", item.Term);

        var input = new PreparedMeaningInput(
            SelectedMeaningId: null,
            AcronymExpansion: null,
            Translation: null,
            Definition: "Gebäude zum Wohnen",
            DictionaryExample: null,
            AdditionalNote: null,
            AcceptedAliases: [],
            ProviderName: string.Empty,
            SourceProject: string.Empty,
            SourcePageTitle: string.Empty,
            SourceRevisionId: null,
            Attribution: string.Empty,
            ManualInputMode: LexicalLookupMode.Definition);

        await preparation.AcceptAsync(item.CandidateId, input, CardDirectionPreference.Both);

        var senses = await database.RunInTransactionAsync(conn =>
            conn.Query<SenseIdRow>("SELECT Id FROM Senses WHERE WordId = ?", item.WordId));
        Assert.AreEqual(1, senses.Count);
        var senseId = senses[0].Id;

        var targets = await targetRepo.GetTargetsForSenseAsync(senseId);
        Assert.AreEqual(1, targets.Count);
        var target = targets[0];
        Assert.AreEqual(LearningTargetKind.Definition, target.TargetKind);
        Assert.AreEqual("de", target.SourceLanguage);
        Assert.AreEqual("de", target.TargetLanguage);
        Assert.IsFalse(target.TypingOptOut);

        var variants = await targetRepo.GetAnswerVariantsAsync(target.Id);
        Assert.AreEqual(1, variants.Count);
        Assert.AreEqual("Gebäude zum Wohnen", variants[0].DisplayText);
        Assert.AreEqual("de", variants[0].AnswerLanguage);
        Assert.IsTrue(variants[0].IsPreferred);
        Assert.AreEqual(AnswerVariantRequirement.Required, variants[0].Requirement);
    }

    [TestMethod]
    public async Task PreparationCreatedTarget_FailsClosedForV3ExportAndMergeSafetyCopy()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();
        var clock = new FakeClock(Now);
        var review = new TextReviewService(
            database,
            new TextAnalyzer(),
            new DisabledEnhancedRecognitionSettings(),
            new FixtureGermanLexicon());
        var preparation = CreatePreparationService(database, new MutableProvider(clock), clock);

        await ImportWithOnlyThisWordUnknownAsync(
            review, "Ein Haus ist gross.", "Haus", "de", LexicalLookupMode.Definition, null);
        await preparation.StartAsync(PreparationMethod.Manual, 1);
        var item = await preparation.GetCurrentAsync();
        Assert.IsNotNull(item);
        await preparation.AcceptAsync(
            item.CandidateId,
            CreateManualDefinitionInput("Gebäude zum Wohnen", typingOptOut: null),
            CardDirectionPreference.Both);

        var targetCount = await database.RunInTransactionAsync(conn =>
            conn.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningTargets"));
        Assert.AreEqual(1, targetCount, "The guard must run after the actual preparation path created target data.");

        using var archive = new MemoryStream();
        var exportException = await Assert.ThrowsExactlyAsync<BackupSchemaCapabilityException>(() =>
            new BackupService(database, new FakeBackupPlatformInfo())
                .CreatePortableArchiveAsync(archive, CancellationToken.None));
        Assert.AreEqual(BackupErrorCodes.Schema14TargetDataIncompatibleWithV3Transport, exportException.ErrorCode);

        var safetyCopyException = await Assert.ThrowsExactlyAsync<BackupSchemaCapabilityException>(() =>
            database.ExecuteSnapshotAsync(BackupMergeSafetyCopySnapshotCapture.CaptureForMergeSafetyCopy));
        Assert.AreEqual(BackupErrorCodes.Schema14TargetDataIncompatibleWithV3Transport, safetyCopyException.ErrorCode);
    }

    [TestMethod]
    public async Task ReacceptExistingTarget_WithoutTypingOverride_PreservesPersistedTypingOptOut()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(Now);
        var review = new TextReviewService(
            database,
            new TextAnalyzer(),
            new DisabledEnhancedRecognitionSettings(),
            new FixtureGermanLexicon());
        var provider = new MutableProvider(clock);
        var preparation = CreatePreparationService(database, provider, clock);
        var targetRepo = new LearningTargetRepository(database);

        var wordId = await ImportWithOnlyThisWordUnknownAsync(
            review, "Ein Haus ist gross.", "Haus", "de", LexicalLookupMode.Definition, null);

        await preparation.StartAsync(PreparationMethod.Manual, 1);
        var firstItem = await preparation.GetCurrentAsync();
        Assert.IsNotNull(firstItem);
        await preparation.AcceptAsync(
            firstItem.CandidateId,
            CreateManualDefinitionInput("Gebäude zum Wohnen", typingOptOut: true),
            CardDirectionPreference.Both);

        var senseId = await database.RunInTransactionAsync(conn =>
            conn.ExecuteScalar<int>("SELECT Id FROM Senses WHERE WordId = ?", wordId));
        var initialTargets = await targetRepo.GetTargetsForSenseAsync(senseId);
        Assert.AreEqual(1, initialTargets.Count);
        var target = initialTargets[0];
        Assert.IsTrue(target.TypingOptOut);

        await preparation.StartAsync(
            PreparationMethod.Manual,
            1,
            new PreparationTargetAdditionRequest(wordId, senseId, LearningTargetKind.Definition, "de"));
        var repeatedItem = await preparation.GetCurrentAsync();
        Assert.IsNotNull(repeatedItem);
        await preparation.AcceptAsync(
            repeatedItem.CandidateId,
            CreateManualDefinitionInput("Gebäude zum Wohnen", typingOptOut: null),
            CardDirectionPreference.Both);

        var repeatedTargets = await targetRepo.GetTargetsForSenseAsync(senseId);
        Assert.AreEqual(1, repeatedTargets.Count);
        var preserved = repeatedTargets[0];
        Assert.IsTrue(preserved.TypingOptOut);
    }

    [TestMethod]
    public async Task ProviderSense_AddDefinitionEnglishAndFrenchTargets_ReusesWordAndSenseWithoutResettingLegacySchedules()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();
        var clock = new FakeClock(Now);
        var review = new TextReviewService(
            database,
            new TextAnalyzer(),
            new DisabledEnhancedRecognitionSettings(),
            new FixtureGermanLexicon());
        var provider = new MutableProvider(clock)
        {
            MeaningsFactory = request => request.TargetLanguage switch
            {
                "en" => [new LexicalMeaning("haus-building", "Substantiv", string.Empty, "house", null, ["common"])],
                "fr" => [new LexicalMeaning("haus-building", "Substantiv", string.Empty, "maison", null, ["common"])],
                _ => [new LexicalMeaning("haus-building", "Substantiv", "Gebäude zum Wohnen", null, null, ["common"])]
            }
        };
        var preparation = CreatePreparationService(database, provider, clock);
        var targetRepo = new LearningTargetRepository(database);
        var wordId = await ImportWithOnlyThisWordUnknownAsync(
            review, "Ein Haus ist gross.", "Haus", "de", LexicalLookupMode.Definition, null);

        await PrepareProviderTargetAsync(preparation, null, typingOptOut: null, aliases: ["Wohngebäude"]);
        var initialSchedule = await LoadLegacyScheduleAsync(database, wordId);
        var senseId = await database.RunInTransactionAsync(conn =>
            conn.ExecuteScalar<int>("SELECT Id FROM Senses WHERE WordId = ?", wordId));

        await PrepareProviderTargetAsync(
            preparation,
            new PreparationTargetAdditionRequest(wordId, senseId, LearningTargetKind.Translation, "en"),
            typingOptOut: null,
            aliases: ["dwelling", " HOUSE "]);
        await PrepareManualTranslationTargetAsync(
            preparation,
            new PreparationTargetAdditionRequest(wordId, senseId, LearningTargetKind.Translation, "fr"),
            "maison",
            typingOptOut: null,
            aliases: []);

        var senses = await database.RunInTransactionAsync(conn =>
            conn.Query<SenseIdRow>("SELECT Id FROM Senses WHERE WordId = ? ORDER BY Id", wordId));
        Assert.AreEqual(1, senses.Count, "The same persisted provider sense must ignore target-language differences.");
        var targets = await targetRepo.GetTargetsForSenseAsync(senses[0].Id);
        Assert.AreEqual(3, targets.Count);
        CollectionAssert.AreEquivalent(
            new[]
            {
                "Definition:de:de",
                "Translation:de:en",
                "Translation:de:fr"
            },
            targets.Select(target => $"{target.TargetKind}:{target.SourceLanguage}:{target.TargetLanguage}").ToArray());
        Assert.IsFalse(targets.Any(target =>
            target.TargetKind == LearningTargetKind.Translation
            && target.SourceLanguage != "de"), "No reverse target may be created implicitly.");
        Assert.IsTrue(targets.All(target => !target.TypingOptOut), "A new target defaults to typing check enabled.");

        var englishTarget = targets.Single(target => target.TargetLanguage == "en");
        var englishVariants = await targetRepo.GetAnswerVariantsAsync(englishTarget.Id);
        Assert.AreEqual(2, englishVariants.Count, "The normalized alias matching the primary answer must not duplicate it.");
        Assert.AreEqual(1, englishVariants.Count(variant => variant.IsPreferred));
        Assert.AreEqual(AnswerVariantRequirement.Required, englishVariants.Single(variant => variant.IsPreferred).Requirement);
        Assert.AreEqual(AnswerVariantRequirement.AcceptedOnly, englishVariants.Single(variant => variant.DisplayText == "dwelling").Requirement);
        Assert.IsTrue(englishVariants.All(variant => variant.SourceMeaningId.HasValue));

        await PrepareProviderTargetAsync(
            preparation,
            new PreparationTargetAdditionRequest(wordId, senseId, LearningTargetKind.Translation, "en"),
            typingOptOut: true,
            aliases: ["dwelling"]);
        var repeatedTargets = await targetRepo.GetTargetsForSenseAsync(senses[0].Id);
        Assert.AreEqual(3, repeatedTargets.Count, "Exact target identity must be idempotent.");
        Assert.IsTrue(repeatedTargets.Single(target => target.TargetLanguage == "en").TypingOptOut,
            "An explicit Automatic-mode preference change must persist.");
        Assert.AreEqual(2, (await targetRepo.GetAnswerVariantsAsync(englishTarget.Id)).Count);

        var finalSchedule = await LoadLegacyScheduleAsync(database, wordId);
        CollectionAssert.AreEqual(initialSchedule, finalSchedule,
            "Adding targets must not create, repoint, or reset transitional LearningCards/FSRS state.");
    }

    [TestMethod]
    public async Task AddTarget_ExplicitSenseId_IsAuthoritativeAndDoesNotCreateNewSense()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();
        var clock = new FakeClock(Now);
        var review = new TextReviewService(database, new TextAnalyzer(), new DisabledEnhancedRecognitionSettings(), new FixtureGermanLexicon());
        var provider = new MutableProvider(clock);
        var preparation = CreatePreparationService(database, provider, clock);
        var wordId = await ImportWithOnlyThisWordUnknownAsync(
            review, "Eine Bank steht dort.", "Bank", "de", LexicalLookupMode.Definition, null);

        provider.MeaningsFactory = _ => [new LexicalMeaning("bank-seat", "Substantiv", "Sitzmöbel", null, null, [])];
        await PrepareProviderTargetAsync(preparation, null, null, []);
        var originalSenseId = await database.RunInTransactionAsync(conn =>
            conn.ExecuteScalar<int>("SELECT Id FROM Senses WHERE WordId = ?", wordId));
        provider.MeaningsFactory = _ => [new LexicalMeaning("bank-finance", "Substantiv", string.Empty, "bank", null, [])];
        await PrepareProviderTargetAsync(
            preparation,
            new PreparationTargetAdditionRequest(wordId, originalSenseId, LearningTargetKind.Translation, "en"),
            null,
            []);

        var senseCount = await database.RunInTransactionAsync(conn =>
            conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Senses WHERE WordId = ?", wordId));
        Assert.AreEqual(1, senseCount, "Under Option A, explicit TargetAddition.SenseId is authoritative and attaches to the existing sense.");

        var targetRepo = new LearningTargetRepository(database);
        var targets = await targetRepo.GetTargetsForSenseAsync(originalSenseId);
        Assert.AreEqual(2, targets.Count);
    }

    [TestMethod]
    public async Task TargetAddition_KnownUnpreparedWord_IsRejectedWithoutCreatingWorkflowState()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();
        var clock = new FakeClock(Now);
        var review = new TextReviewService(database, new TextAnalyzer(), new DisabledEnhancedRecognitionSettings(), new FixtureGermanLexicon());
        var preparation = CreatePreparationService(database, new MutableProvider(clock), clock);
        await ImportAllAsKnownAsync(review, "Haus", "de");
        var wordId = await database.RunInTransactionAsync(conn =>
            conn.ExecuteScalar<int>("SELECT Id FROM Words WHERE CanonicalTerm = 'Haus'"));

        var sessionId = await preparation.StartAsync(
            PreparationMethod.Manual,
            1,
            new PreparationTargetAdditionRequest(wordId, int.MaxValue, LearningTargetKind.Translation, "en"));

        Assert.AreEqual(0, sessionId);
        Assert.IsNull(await preparation.GetCurrentAsync());
        var counts = await database.RunInTransactionAsync(conn => new[]
        {
            conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Senses WHERE WordId = ?", wordId),
            conn.ExecuteScalar<int>("SELECT COUNT(*) FROM PreparationSessions")
        });
        CollectionAssert.AreEqual(new[] { 0, 0 }, counts);
    }

    [TestMethod]
    public void MetadataPresentation_PreservesGroundedFieldsAndDoesNotFabricateMissingProviderData()
    {
        var grounded = MeaningPreviewPolicy.GetSelectableMeanings(
            [new LexicalMeaning("sense", "noun", "a building", null, null, ["formal"])],
            LexicalLookupMode.Definition,
            grammaticalRelationship: "plural of house",
            topicOrDomain: "architecture");
        Assert.AreEqual(1, grounded.Count);
        Assert.AreEqual("noun · architecture · plural of house · formal", grounded[0].SecondaryText);

        var absent = MeaningPreviewPolicy.GetSelectableMeanings(
            [new LexicalMeaning("wp", null, "an encyclopedia summary", null, null, [])],
            LexicalLookupMode.Definition);
        Assert.AreEqual(1, absent.Count);
        Assert.IsNull(absent[0].SecondaryText);
    }

    [TestMethod]
    public void ExplicitGlobalTyping_RemainsAuthoritativeWhenTargetOptsOut()
    {
        var state = TargetAutomaticProgressionState.Initial with { TypingOptOut = true };
        Assert.AreEqual(
            LearningInteractionMode.Typing,
            TargetAutomaticProgressionPolicy.ResolveInteraction(LearningMode.Typing, state));
    }

    [TestMethod]
    public async Task AddTarget_Skip_PreservesExistingPreparedState()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(Now);
        var analyzer = new TextAnalyzer();
        var review = new TextReviewService(
            database,
            analyzer,
            new DisabledEnhancedRecognitionSettings(),
            new FixtureGermanLexicon());

        var provider = new MutableProvider(clock);
        var preparation = CreatePreparationService(database, provider, clock);

        var wordId = await ImportWithOnlyThisWordUnknownAsync(
            review, "Ein Haus ist gross.", "Haus", "de", LexicalLookupMode.Definition, null);

        var firstSessionId = await preparation.StartAsync(PreparationMethod.Manual, 1);
        Assert.IsTrue(firstSessionId > 0);
        var firstItem = await preparation.GetCurrentAsync();
        Assert.IsNotNull(firstItem);

        await preparation.AcceptAsync(
            firstItem.CandidateId,
            CreateManualDefinitionInput("Gebäude zum Wohnen", typingOptOut: null),
            CardDirectionPreference.Both);

        var preState = await database.RunInTransactionAsync(conn =>
        {
            var word = conn.Find<WordEntity>(wordId)!;
            var sense = conn.Query<SenseIdRow>("SELECT Id FROM Senses WHERE WordId = ?", wordId).Single();
            return (word.PreparationState, word.Status, sense.Id);
        });

        Assert.AreEqual(PreparationState.Prepared, preState.PreparationState);
        Assert.AreEqual(WordStatus.UnknownBacklog, preState.Status);

        var targetAddition = new PreparationTargetAdditionRequest(
            wordId, preState.Id, LearningTargetKind.Translation, "en");

        var addSessionId = await preparation.StartAsync(PreparationMethod.Manual, 1, targetAddition);
        Assert.IsTrue(addSessionId > 0);

        var addItem = await preparation.GetCurrentAsync();
        Assert.IsNotNull(addItem);

        await preparation.SkipAsync(addItem.CandidateId);

        var postWord = await database.RunInTransactionAsync(conn => conn.Find<WordEntity>(wordId)!);
        Assert.AreEqual(PreparationState.Prepared, postWord.PreparationState);
        Assert.AreEqual(preState.Status, postWord.Status);
    }

    [TestMethod]
    public async Task AddTarget_Cancel_PreservesExistingPreparedState()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(Now);
        var analyzer = new TextAnalyzer();
        var review = new TextReviewService(
            database,
            analyzer,
            new DisabledEnhancedRecognitionSettings(),
            new FixtureGermanLexicon());

        var provider = new MutableProvider(clock);
        var preparation = CreatePreparationService(database, provider, clock);

        var wordId = await ImportWithOnlyThisWordUnknownAsync(
            review, "Ein Haus ist gross.", "Haus", "de", LexicalLookupMode.Definition, null);

        var firstSessionId = await preparation.StartAsync(PreparationMethod.Manual, 1);
        Assert.IsTrue(firstSessionId > 0);
        var firstItem = await preparation.GetCurrentAsync();
        Assert.IsNotNull(firstItem);

        await preparation.AcceptAsync(
            firstItem.CandidateId,
            CreateManualDefinitionInput("Gebäude zum Wohnen", typingOptOut: null),
            CardDirectionPreference.Both);

        var preState = await database.RunInTransactionAsync(conn =>
        {
            var word = conn.Find<WordEntity>(wordId)!;
            var sense = conn.Query<SenseIdRow>("SELECT Id FROM Senses WHERE WordId = ?", wordId).Single();
            return (word.PreparationState, word.Status, sense.Id);
        });

        Assert.AreEqual(PreparationState.Prepared, preState.PreparationState);

        var targetAddition = new PreparationTargetAdditionRequest(
            wordId, preState.Id, LearningTargetKind.Translation, "en");

        var addSessionId = await preparation.StartAsync(PreparationMethod.Manual, 1, targetAddition);
        Assert.IsTrue(addSessionId > 0);

        await preparation.CancelActiveSessionAsync();

        var postWord = await database.RunInTransactionAsync(conn => conn.Find<WordEntity>(wordId)!);
        Assert.AreEqual(PreparationState.Prepared, postWord.PreparationState);
        Assert.AreEqual(preState.Status, postWord.Status);
    }

    [TestMethod]
    public async Task AddTarget_LookupFailure_PreservesExistingPreparedState()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(Now);
        var analyzer = new TextAnalyzer();
        var review = new TextReviewService(
            database,
            analyzer,
            new DisabledEnhancedRecognitionSettings(),
            new FixtureGermanLexicon());

        var provider = new MutableProvider(clock);
        var preparation = CreatePreparationService(database, provider, clock);

        var wordId = await ImportWithOnlyThisWordUnknownAsync(
            review, "Ein Haus ist gross.", "Haus", "de", LexicalLookupMode.Definition, null);

        var firstSessionId = await preparation.StartAsync(PreparationMethod.Manual, 1);
        var firstItem = await preparation.GetCurrentAsync();
        await preparation.AcceptAsync(
            firstItem!.CandidateId,
            CreateManualDefinitionInput("Gebäude zum Wohnen", typingOptOut: null),
            CardDirectionPreference.Both);

        var preState = await database.RunInTransactionAsync(conn =>
        {
            var word = conn.Find<WordEntity>(wordId)!;
            var sense = conn.Query<SenseIdRow>("SELECT Id FROM Senses WHERE WordId = ?", wordId).Single();
            return (word.PreparationState, word.Status, sense.Id);
        });

        provider.MeaningsFactory = _ => throw new InvalidOperationException("Simulated provider crash.");

        var targetAddition = new PreparationTargetAdditionRequest(
            wordId, preState.Id, LearningTargetKind.Translation, "en");

        var addSessionId = await preparation.StartAsync(PreparationMethod.AutomaticOnline, 1, targetAddition);
        Assert.IsTrue(addSessionId > 0);

        var candidate = await preparation.LookupCurrentAsync();
        Assert.IsNotNull(candidate);
        Assert.AreEqual(PreparationCandidateStatus.Failed, candidate.Status);

        var postWord = await database.RunInTransactionAsync(conn => conn.Find<WordEntity>(wordId)!);
        Assert.AreEqual(PreparationState.Prepared, postWord.PreparationState);
        Assert.AreEqual(preState.Status, postWord.Status);
    }

    [TestMethod]
    public async Task AddTarget_ActiveSessionConflict_ThrowsInvalidOperationException()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(Now);
        var analyzer = new TextAnalyzer();
        var review = new TextReviewService(
            database,
            analyzer,
            new DisabledEnhancedRecognitionSettings(),
            new FixtureGermanLexicon());

        var provider = new MutableProvider(clock);
        var preparation = CreatePreparationService(database, provider, clock);

        var wordId = await ImportWithOnlyThisWordUnknownAsync(
            review, "Ein Haus ist gross.", "Haus", "de", LexicalLookupMode.Definition, null);

        var firstSessionId = await preparation.StartAsync(PreparationMethod.Manual, 1);
        var firstItem = await preparation.GetCurrentAsync();
        await preparation.AcceptAsync(
            firstItem!.CandidateId,
            CreateManualDefinitionInput("Gebäude zum Wohnen", typingOptOut: null),
            CardDirectionPreference.Both);

        var senseId = await database.RunInTransactionAsync(conn =>
            conn.ExecuteScalar<int>("SELECT Id FROM Senses WHERE WordId = ?", wordId));

        var targetAddition1 = new PreparationTargetAdditionRequest(
            wordId, senseId, LearningTargetKind.Translation, "en");

        var session1 = await preparation.StartAsync(PreparationMethod.Manual, 1, targetAddition1);
        Assert.IsTrue(session1 > 0);

        // Conflict: Start ordinary session while add-target session is active
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            preparation.StartAsync(PreparationMethod.Manual, 1));

        // Conflict: Start different add-target session (different target language) while active
        var targetAddition2 = new PreparationTargetAdditionRequest(
            wordId, senseId, LearningTargetKind.Translation, "fr");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            preparation.StartAsync(PreparationMethod.Manual, 1, targetAddition2));
    }

    [TestMethod]
    public async Task AddTarget_ActiveSessionSameContext_ResumesSession()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(Now);
        var analyzer = new TextAnalyzer();
        var review = new TextReviewService(
            database,
            analyzer,
            new DisabledEnhancedRecognitionSettings(),
            new FixtureGermanLexicon());

        var provider = new MutableProvider(clock);
        var preparation = CreatePreparationService(database, provider, clock);

        var wordId = await ImportWithOnlyThisWordUnknownAsync(
            review, "Ein Haus ist gross.", "Haus", "de", LexicalLookupMode.Definition, null);

        var firstSessionId = await preparation.StartAsync(PreparationMethod.Manual, 1);
        var firstItem = await preparation.GetCurrentAsync();
        await preparation.AcceptAsync(
            firstItem!.CandidateId,
            CreateManualDefinitionInput("Gebäude zum Wohnen", typingOptOut: null),
            CardDirectionPreference.Both);

        var senseId = await database.RunInTransactionAsync(conn =>
            conn.ExecuteScalar<int>("SELECT Id FROM Senses WHERE WordId = ?", wordId));

        var targetAddition = new PreparationTargetAdditionRequest(
            wordId, senseId, LearningTargetKind.Translation, "en");

        var session1 = await preparation.StartAsync(PreparationMethod.Manual, 1, targetAddition);
        Assert.IsTrue(session1 > 0);

        // Resume: same exact targetAddition context
        var session2 = await preparation.StartAsync(PreparationMethod.Manual, 1, targetAddition);
        Assert.AreEqual(session1, session2);
    }

    [TestMethod]
    public async Task AddTarget_VariantNormalization_DeduplicatesDuplicatesAndUnicodeVariants()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(Now);
        var analyzer = new TextAnalyzer();
        var review = new TextReviewService(
            database,
            analyzer,
            new DisabledEnhancedRecognitionSettings(),
            new FixtureGermanLexicon());

        var provider = new MutableProvider(clock);
        var preparation = CreatePreparationService(database, provider, clock);
        var targetRepo = new LearningTargetRepository(database);

        var wordId = await ImportWithOnlyThisWordUnknownAsync(
            review, "Ein Café ist schön.", "Café", "de", LexicalLookupMode.Definition, null);

        var nfdVariant = "Cafe\u0301";
        var nfcVariant = "Caf\u00e9";

        var firstSessionId = await preparation.StartAsync(PreparationMethod.Manual, 1);
        var firstItem = await preparation.GetCurrentAsync();
        await preparation.AcceptAsync(
            firstItem!.CandidateId,
            new PreparedMeaningInput(
                SelectedMeaningId: null,
                AcronymExpansion: null,
                Translation: null,
                Definition: nfdVariant,
                DictionaryExample: null,
                AdditionalNote: null,
                AcceptedAliases: [nfcVariant, "  " + nfdVariant + "  ", "CAFÉ"],
                ProviderName: string.Empty,
                SourceProject: string.Empty,
                SourcePageTitle: string.Empty,
                SourceRevisionId: null,
                Attribution: string.Empty,
                ManualInputMode: LexicalLookupMode.Definition),
            CardDirectionPreference.Both);

        var senseId = await database.RunInTransactionAsync(conn =>
            conn.ExecuteScalar<int>("SELECT Id FROM Senses WHERE WordId = ?", wordId));
        var targets = await targetRepo.GetTargetsForSenseAsync(senseId);
        Assert.AreEqual(1, targets.Count);

        var variants = await targetRepo.GetAnswerVariantsAsync(targets[0].Id);
        Assert.AreEqual(1, variants.Count);
        Assert.IsTrue(variants[0].IsPreferred);
        Assert.AreEqual(nfdVariant.Normalize(System.Text.NormalizationForm.FormC), variants[0].DisplayText);
    }

    [TestMethod]
    public async Task AddTarget_MismatchedWordId_ThrowsInvalidOperationException()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(Now);
        var analyzer = new TextAnalyzer();
        var review = new TextReviewService(
            database,
            analyzer,
            new DisabledEnhancedRecognitionSettings(),
            new FixtureGermanLexicon());

        var provider = new MutableProvider(clock);
        var preparation = CreatePreparationService(database, provider, clock);

        var wordId1 = await ImportWithOnlyThisWordUnknownAsync(
            review, "Ein Haus ist gross.", "Haus", "de", LexicalLookupMode.Definition, null);

        var firstSessionId = await preparation.StartAsync(PreparationMethod.Manual, 1);
        var firstItem = await preparation.GetCurrentAsync();
        await preparation.AcceptAsync(
            firstItem!.CandidateId,
            CreateManualDefinitionInput("Gebäude zum Wohnen", typingOptOut: null),
            CardDirectionPreference.Both);

        var senseId1 = await database.RunInTransactionAsync(conn =>
            conn.ExecuteScalar<int>("SELECT Id FROM Senses WHERE WordId = ?", wordId1));

        var wordId2 = await ImportWithOnlyThisWordUnknownAsync(
            review, "Ein Baum ist grün.", "Baum", "de", LexicalLookupMode.Definition, null);

        var invalidAddition = new PreparationTargetAdditionRequest(
            wordId2, senseId1, LearningTargetKind.Translation, "en");

        var sessionId = await preparation.StartAsync(PreparationMethod.Manual, 1, invalidAddition);
        Assert.AreEqual(0, sessionId, "Starting target addition with mismatched sense ownership must fail to start session.");
    }

    [TestMethod]
    public async Task AddTarget_GenuinelyReviewedCard_PreservesAllFsrsStateAndHistory()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var clock = new FakeClock(Now);
        var analyzer = new TextAnalyzer();
        var review = new TextReviewService(
            database,
            analyzer,
            new DisabledEnhancedRecognitionSettings(),
            new FixtureGermanLexicon());

        var provider = new MutableProvider(clock);
        var preparation = CreatePreparationService(database, provider, clock);
        var targetRepo = new LearningTargetRepository(database);

        var wordId = await ImportWithOnlyThisWordUnknownAsync(
            review, "Ein Haus ist gross.", "Haus", "de", LexicalLookupMode.Definition, null);

        var firstSessionId = await preparation.StartAsync(PreparationMethod.Manual, 1);
        var firstItem = await preparation.GetCurrentAsync();
        await preparation.AcceptAsync(
            firstItem!.CandidateId,
            CreateManualDefinitionInput("Gebäude zum Wohnen", typingOptOut: null),
            CardDirectionPreference.Both);

        var senseId = await database.RunInTransactionAsync(conn =>
            conn.ExecuteScalar<int>("SELECT Id FROM Senses WHERE WordId = ?", wordId));

        var cardIds = await database.RunInTransactionAsync(conn =>
            conn.Query<SenseIdRow>("SELECT Id FROM LearningCards WHERE WordId = ?", wordId).Select(r => r.Id).ToArray());
        Assert.IsTrue(cardIds.Length > 0);

        var reviewTime = Now.AddDays(-1);
        var fsrsScheduler = new KnownFirst.Application.Learning.Fsrs6SchedulingService(clock);
        var projection = fsrsScheduler.Schedule(
            KnownFirst.Application.Learning.Fsrs6ScheduleProjection.New(),
            ReviewRating.Good,
            new DateTimeOffset(reviewTime, TimeSpan.Zero));

        await database.RunInTransactionAsync(conn =>
        {
            foreach (var cardId in cardIds)
            {
                var lastReviewedStr = Schema13TimestampCodec.FormatUtc(reviewTime);
                var dueStr = Schema13TimestampCodec.FormatUtc(projection.DueAtUtc!.Value.UtcDateTime);

                conn.Execute(
                    """
                    UPDATE FsrsCardStates
                    SET State = ?, Stability = ?, Difficulty = ?, LastReviewedAtUtc = ?, StepIndex = ?, DueAtUtc = ?
                    WHERE CardId = ?
                    """,
                    (int)projection.State,
                    projection.Stability,
                    projection.Difficulty,
                    lastReviewedStr,
                    projection.StepIndex,
                    dueStr,
                    cardId);

                conn.Execute(
                    """
                    UPDATE LearningCards
                    SET State = ?, DueAtUtc = ?
                    WHERE Id = ?
                    """,
                    (int)projection.State,
                    dueStr,
                    cardId);

                conn.Execute(
                    """
                    INSERT INTO FsrsReviewHistoryEntries (StableId, CardId, SequenceNumber, Rating, ReviewedAtUtc)
                    VALUES (?, ?, 1, ?, ?)
                    """,
                    Guid.NewGuid().ToString("N"),
                    cardId,
                    (int)ReviewRating.Good,
                    lastReviewedStr);
            }

            return true;
        });

        var preCards = await database.RunInTransactionAsync(conn =>
            conn.Query<StringRow>("SELECT Id || '|' || SenseId || '|' || PreferredMeaningId || '|' || Direction || '|' || State || '|' || DueAtUtc AS Value FROM LearningCards WHERE WordId = ? ORDER BY Id", wordId).Select(r => r.Value).ToArray());
        var preFsrs = await database.RunInTransactionAsync(conn =>
            conn.Query<StringRow>("SELECT CardId || '|' || State || '|' || quote(Stability) || '|' || quote(Difficulty) || '|' || quote(LastReviewedAtUtc) || '|' || quote(DueAtUtc) AS Value FROM FsrsCardStates WHERE CardId IN (SELECT Id FROM LearningCards WHERE WordId = ?) ORDER BY CardId", wordId).Select(r => r.Value).ToArray());
        var preLogs = await database.RunInTransactionAsync(conn =>
            conn.Query<StringRow>("SELECT CardId || '|' || SequenceNumber || '|' || Rating || '|' || ReviewedAtUtc AS Value FROM FsrsReviewHistoryEntries WHERE CardId IN (SELECT Id FROM LearningCards WHERE WordId = ?) ORDER BY Id", wordId).Select(r => r.Value).ToArray());

        Assert.IsTrue(preFsrs.Length > 0);
        Assert.IsTrue(preLogs.Length > 0);

        var targetAddition = new PreparationTargetAdditionRequest(
            wordId, senseId, LearningTargetKind.Translation, "en");

        provider.MeaningsFactory = _ => [new LexicalMeaning("meaning-trans", "Substantiv", string.Empty, "house", null, [])];
        await PrepareProviderTargetAsync(preparation, targetAddition, typingOptOut: null, aliases: ["home"]);

        var postCards = await database.RunInTransactionAsync(conn =>
            conn.Query<StringRow>("SELECT Id || '|' || SenseId || '|' || PreferredMeaningId || '|' || Direction || '|' || State || '|' || DueAtUtc AS Value FROM LearningCards WHERE WordId = ? ORDER BY Id", wordId).Select(r => r.Value).ToArray());
        var postFsrs = await database.RunInTransactionAsync(conn =>
            conn.Query<StringRow>("SELECT CardId || '|' || State || '|' || quote(Stability) || '|' || quote(Difficulty) || '|' || quote(LastReviewedAtUtc) || '|' || quote(DueAtUtc) AS Value FROM FsrsCardStates WHERE CardId IN (SELECT Id FROM LearningCards WHERE WordId = ?) ORDER BY CardId", wordId).Select(r => r.Value).ToArray());
        var postLogs = await database.RunInTransactionAsync(conn =>
            conn.Query<StringRow>("SELECT CardId || '|' || SequenceNumber || '|' || Rating || '|' || ReviewedAtUtc AS Value FROM FsrsReviewHistoryEntries WHERE CardId IN (SELECT Id FROM LearningCards WHERE WordId = ?) ORDER BY Id", wordId).Select(r => r.Value).ToArray());

        CollectionAssert.AreEqual(preCards, postCards, "LearningCards records must be unchanged.");
        CollectionAssert.AreEqual(preFsrs, postFsrs, "FsrsCardStates records must be unchanged.");
        CollectionAssert.AreEqual(preLogs, postLogs, "FsrsReviewHistoryEntries records must be unchanged.");

        var targets = await targetRepo.GetTargetsForSenseAsync(senseId);
        Assert.AreEqual(2, targets.Count, "The new translation target must be created alongside definition target.");
    }

    private static PreparedMeaningInput CreateManualDefinitionInput(string definition, bool? typingOptOut) => new(
        SelectedMeaningId: null,
        AcronymExpansion: null,
        Translation: null,
        Definition: definition,
        DictionaryExample: null,
        AdditionalNote: null,
        AcceptedAliases: [],
        ProviderName: string.Empty,
        SourceProject: string.Empty,
        SourcePageTitle: string.Empty,
        SourceRevisionId: null,
        Attribution: string.Empty,
        ManualInputMode: LexicalLookupMode.Definition,
        TypingOptOut: typingOptOut);

    private static async Task PrepareProviderTargetAsync(
        PreparationService preparation,
        PreparationTargetAdditionRequest? targetAddition,
        bool? typingOptOut,
        IReadOnlyList<string> aliases)
    {
        var sessionId = await preparation.StartAsync(PreparationMethod.AutomaticOnline, 1, targetAddition);
        Assert.IsTrue(sessionId > 0);
        var item = await preparation.LookupCurrentAsync();
        Assert.IsNotNull(item);
        Assert.IsNotNull(item.Result);
        var meaning = item.Result.Meanings[item.SelectedMeaningIndex];
        await preparation.AcceptAsync(
            item.CandidateId,
            new PreparedMeaningInput(
                meaning.MeaningId,
                item.Result.AcronymExpansion,
                meaning.Translation,
                meaning.Definition,
                meaning.Example,
                null,
                aliases,
                item.Result.ProviderName,
                item.Result.SourceProject,
                item.Result.PageTitle,
                item.Result.RevisionId,
                item.Result.Attribution,
                item.EncounteredSurfaceForm,
                item.Result.GrammaticalRelationship,
                item.Result.DisplayTerm,
                PartOfSpeech: meaning.PartOfSpeech,
                TypingOptOut: typingOptOut),
            CardDirectionPreference.Both);
    }

    private static async Task PrepareManualTranslationTargetAsync(
        PreparationService preparation,
        PreparationTargetAdditionRequest targetAddition,
        string translation,
        bool? typingOptOut,
        IReadOnlyList<string> aliases)
    {
        var sessionId = await preparation.StartAsync(PreparationMethod.Manual, 1, targetAddition);
        Assert.IsTrue(sessionId > 0);
        var item = await preparation.GetCurrentAsync();
        Assert.IsNotNull(item);
        await preparation.AcceptAsync(
            item.CandidateId,
            new PreparedMeaningInput(
                SelectedMeaningId: null,
                AcronymExpansion: null,
                Translation: translation,
                Definition: string.Empty,
                DictionaryExample: null,
                AdditionalNote: null,
                AcceptedAliases: aliases,
                ProviderName: string.Empty,
                SourceProject: string.Empty,
                SourcePageTitle: string.Empty,
                SourceRevisionId: null,
                Attribution: string.Empty,
                ManualInputMode: LexicalLookupMode.Translation,
                TypingOptOut: typingOptOut),
            CardDirectionPreference.Both);
    }

    private static Task<string[]> LoadLegacyScheduleAsync(IKnownFirstDatabase database, int wordId) =>
        database.RunInTransactionAsync(conn => conn.Query<LegacyScheduleRow>(
                """
                SELECT lc.Id, lc.SenseId, lc.PreferredMeaningId, lc.Direction, lc.State, lc.DueAtUtc,
                       fs.Stability, fs.Difficulty, fs.LastReviewedAtUtc
                FROM LearningCards lc
                LEFT JOIN FsrsCardStates fs ON fs.CardId = lc.Id
                WHERE lc.WordId = ?
                ORDER BY lc.Id
                """,
                wordId)
            .Select(row => $"{row.Id}|{row.SenseId}|{row.PreferredMeaningId}|{row.Direction}|{row.State}|{row.DueAtUtc:O}|{row.Stability}|{row.Difficulty}|{row.LastReviewedAtUtc:O}")
            .ToArray());

    private static async Task ImportAllAsKnownAsync(TextReviewService review, string content, string sourceLanguage)
    {
        var result = await review.ImportAsync(new ImportTextRequest(
            $"Document {Guid.NewGuid():N}", content, sourceLanguage, LexicalLookupMode.Definition, null));
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);
        while (await review.GetCurrentCandidateAsync() is { } candidate)
        {
            await review.DecideAsync(candidate.WordId, WordStatus.Known);
        }
    }

    private static async Task<int> ImportWithOnlyThisWordUnknownAsync(
        TextReviewService review,
        string content,
        string unknownTerm,
        string sourceLanguage = "de",
        LexicalLookupMode lookupMode = LexicalLookupMode.Definition,
        string? targetLanguage = null)
    {
        var request = new ImportTextRequest(
            $"Document {Guid.NewGuid():N}",
            content,
            sourceLanguage,
            lookupMode,
            targetLanguage);
        var result = await review.ImportAsync(request);
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);
        var wordId = -1;
        while (await review.GetCurrentCandidateAsync() is { } candidate)
        {
            if (string.Equals(candidate.Candidate, unknownTerm, StringComparison.OrdinalIgnoreCase))
            {
                wordId = candidate.WordId;
                await review.DecideAsync(candidate.WordId, WordStatus.UnknownBacklog);
            }
            else
            {
                await review.DecideAsync(candidate.WordId, WordStatus.Known);
            }
        }

        Assert.AreNotEqual(-1, wordId);
        return wordId;
    }

    private static PreparationService CreatePreparationService(
        IKnownFirstDatabase database,
        MutableProvider provider,
        FakeClock clock,
        IPreparationFaultInjector? faultInjector = null) => new(
        database,
        new LexicalEnrichmentService(
            new AcronymExpansionDetector(),
            new MeaningRanker(),
            new LexicalCacheRepository(database),
            new LexicalLookupProviderResolver([provider])),
        clock,
        diagnosticLog: null,
        faultInjector: faultInjector);

    private sealed class SenseIdRow
    {
        public int Id { get; set; }
    }

    private sealed class StringRow
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class LegacyScheduleRow
    {
        public int Id { get; set; }
        public int SenseId { get; set; }
        public int PreferredMeaningId { get; set; }
        public int Direction { get; set; }
        public int State { get; set; }
        public DateTime DueAtUtc { get; set; }
        public double? Stability { get; set; }
        public double? Difficulty { get; set; }
        public DateTime? LastReviewedAtUtc { get; set; }
    }

    private sealed class FakeBackupPlatformInfo : IBackupPlatformInfo
    {
        public BackupSourcePlatform SourcePlatform => BackupSourcePlatform.Windows;

        public string SourceAppVersion => "1.0.0-test";
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
