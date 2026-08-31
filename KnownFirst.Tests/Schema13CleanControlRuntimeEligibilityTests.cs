using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Core.Preparation;
using KnownFirst.Data.Migrations.Schema13;
using KnownFirst.Data.Schema8;
using KnownFirst.Data.Schema13;
using KnownFirst.Models;
using KnownFirst.Services;
using KnownFirst.Services.Lexical;
using KnownFirst.Services.Study;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
[DoNotParallelize]
public sealed class Schema13CleanControlRuntimeEligibilityTests
{
    private static readonly DateTime NowUtc =
        new(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset NowOffset = new(NowUtc, TimeSpan.Zero);

    [TestMethod]
    public async Task WordAlreadyKnown_ExcludesAllWordCardsFromSchedulingAndDueNewCounts()
    {
        await using var fixture = await CreateFixtureAsync(senseCount: 2);
        await SetDueAsync(fixture, senseIndex: 0);
        await fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
            WordLearningControlRepository.Save(
                connection,
                fixture.WordId,
                WordLearningControl.Default.MarkAlreadyKnown(NowUtc.AddHours(-1))));

        var load = await CreateLearningService(fixture).GetOrStartAsync();
        var queuedCardIds = await LoadIncompleteQueueCardIdsAsync(fixture);

        Assert.IsNull(load.Card);
        Assert.IsEmpty(queuedCardIds);
        await fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
        {
            Assert.HasCount(2, Schema13LearningRepository.LoadAllCards(connection),
                "Raw diagnostics must retain controlled cards.");
            Assert.AreEqual(0, Schema13LearningRepository.CountDueCards(connection, NowOffset));
            Assert.AreEqual(0, Schema13LearningRepository.CountNewWords(connection));
            Assert.IsNull(Schema13LearningRepository.SelectNextDueAtUtc(connection));
        });
    }

    [TestMethod]
    public async Task SenseStopLearning_ExcludesOnlyStoppedSenseFromDueQueueAndCount()
    {
        await using var fixture = await CreateFixtureAsync(senseCount: 2);
        await SetDueAsync(fixture, senseIndex: 0);
        await SetDueAsync(fixture, senseIndex: 1);
        await StopSenseAsync(fixture, senseIndex: 0);

        var load = await CreateLearningService(fixture).GetOrStartAsync();
        var queuedCardIds = await LoadIncompleteQueueCardIdsAsync(fixture);

        Assert.IsNotNull(load.Card);
        Assert.AreEqual(fixture.CardIds[1], load.Card.CardId);
        CollectionAssert.AreEqual(new[] { fixture.CardIds[1] }, queuedCardIds);
        await fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
        {
            Assert.AreEqual(1, Schema13LearningRepository.CountDueCards(connection, NowOffset));
            Assert.AreEqual(fixture.DueAtUtc[1], Schema13LearningRepository.SelectNextDueAtUtc(connection));
        });
    }

    [TestMethod]
    public async Task SenseStopLearning_NewWorkCountsWordWhenSiblingSenseRemainsEligible()
    {
        await using var fixture = await CreateFixtureAsync(senseCount: 2);
        await StopSenseAsync(fixture, senseIndex: 0);

        var load = await CreateLearningService(fixture).GetOrStartAsync();
        var queuedCardIds = await LoadIncompleteQueueCardIdsAsync(fixture);

        Assert.IsNotNull(load.Card);
        Assert.AreEqual(fixture.CardIds[1], load.Card.CardId);
        CollectionAssert.AreEqual(new[] { fixture.CardIds[1] }, queuedCardIds);
        await fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
            Assert.AreEqual(1, Schema13LearningRepository.CountNewWords(connection)));
    }

    [TestMethod]
    public async Task MissingControls_LeaveCardsQueueableAndCounted()
    {
        await using var fixture = await CreateFixtureAsync(senseCount: 2);
        await SetDueAsync(fixture, senseIndex: 0);

        var load = await CreateLearningService(fixture).GetOrStartAsync();
        var queuedCardIds = await LoadIncompleteQueueCardIdsAsync(fixture);

        Assert.IsNotNull(load.Card);
        CollectionAssert.AreEquivalent(fixture.CardIds, queuedCardIds);
        await fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
        {
            Assert.AreEqual(1, Schema13LearningRepository.CountDueCards(connection, NowOffset));
            Assert.AreEqual(1, Schema13LearningRepository.CountNewWords(connection));
            Assert.AreEqual(fixture.DueAtUtc[0], Schema13LearningRepository.SelectNextDueAtUtc(connection));
        });
    }

    [TestMethod]
    public async Task ControlledOnlyWork_DoesNotCreateWorkflowOrPreparationPhantomWork()
    {
        await using var fixture = await CreateFixtureAsync(senseCount: 2);
        await SetDueAsync(fixture, senseIndex: 0);
        await StopSenseAsync(fixture, senseIndex: 0);
        await StopSenseAsync(fixture, senseIndex: 1);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture.DatabaseFixture);

        var workflow = await new WorkflowStateService(database, new FakeClock(NowUtc)).GetSnapshotAsync();
        var preparation = await new PreparationService(
            database,
            new NoOpLexicalEnrichmentService(),
            new FakeClock(NowUtc)).GetOverviewAsync();

        Assert.AreEqual(0, workflow.DueCardCount);
        Assert.AreEqual(0, workflow.PreparedNewItemCount);
        Assert.AreEqual(WorkflowPrimaryAction.ImportText, workflow.PrimaryAction);
        Assert.AreEqual(0, preparation.DueCardCount);
        Assert.AreEqual(0, preparation.PreparedNewItemCount);
    }

    [TestMethod]
    public async Task AnswerVariants_DoNotBecomeIndependentControlOwners()
    {
        await using var fixture = await CreateFixtureAsync(senseCount: 1, variantsPerSense: 3);
        await SetDueAsync(fixture, senseIndex: 0);
        await StopSenseAsync(fixture, senseIndex: 0);

        var load = await CreateLearningService(fixture).GetOrStartAsync();
        var assignmentCount = await fixture.DatabaseFixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE SenseId = ?",
            fixture.SenseIds[0]);

        Assert.AreEqual(3, assignmentCount);
        Assert.IsNull(load.Card);
        Assert.IsEmpty(await LoadIncompleteQueueCardIdsAsync(fixture));
        await fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
            Assert.AreEqual(0, Schema13LearningRepository.CountDueCards(connection, NowOffset)));
    }

    [TestMethod]
    public async Task PreexistingQueueForStoppedSense_IsNotPresentableRevealableOrRateable()
    {
        await using var fixture = await CreateFixtureAsync(senseCount: 1);
        await SetDueAsync(fixture, senseIndex: 0);
        var queueItemId = 0;
        await fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
        {
            var sessionId = Schema8LearningRepository.InsertSession(connection, NowUtc.AddMinutes(-5), 1);
            Schema8LearningRepository.InsertQueueRow(
                connection,
                sessionId,
                fixture.CardIds[0],
                queueOrder: 0,
                isDueCard: true,
                fixture.PreferredVariantIds[0]);
            queueItemId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
            SenseLearningControlRepository.Save(
                connection,
                fixture.SenseIds[0],
                SenseLearningControl.Default.Stop(NowUtc.AddMinutes(-1)));
        });
        var service = CreateLearningService(fixture);

        var load = await service.GetOrStartAsync();

        Assert.IsNull(load.Card);
        var reveal = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => service.RevealAnswerAsync(queueItemId));
        Assert.AreEqual(Schema8LearningDataErrorCode.CardNotFound, reveal.Code);
        var rate = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => service.RateAsync(queueItemId, ReviewRating.Good));
        Assert.AreEqual(Schema8LearningDataErrorCode.CardNotFound, rate.Code);
        Assert.HasCount(1, await LoadIncompleteQueueCardIdsAsync(fixture),
            "Eligibility reads must not invent a Sense-control queue-deletion workflow.");
    }

    private static LearningService CreateLearningService(Fixture fixture) => new(
        new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture.DatabaseFixture),
        new SimpleSpacedRepetitionScheduler(),
        new SpellingAnswerComparer(),
        new FakeClock(NowUtc));

    private static Task SetDueAsync(Fixture fixture, int senseIndex) =>
        fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
        {
            var reviewEvent = new Fsrs6ReviewEvent(NowOffset.AddDays(-30), ReviewRating.Good);
            var scheduled = new Fsrs6Replayer().Replay(Fsrs6Card.New(), [reviewEvent]);
            FsrsReviewPersistenceCoordinator.PersistReview(
                connection,
                fixture.CardIds[senseIndex],
                $"eligibility-review-{senseIndex}",
                reviewEvent,
                scheduled);
            fixture.DueAtUtc[senseIndex] = scheduled.DueAtUtc;
        });

    private static Task StopSenseAsync(Fixture fixture, int senseIndex) =>
        fixture.DatabaseFixture.Connection.RunInTransactionAsync(connection =>
            SenseLearningControlRepository.Save(
                connection,
                fixture.SenseIds[senseIndex],
                SenseLearningControl.Default.Stop(NowUtc.AddHours(-1))));

    private static async Task<int[]> LoadIncompleteQueueCardIdsAsync(Fixture fixture) =>
        (await fixture.DatabaseFixture.Connection.QueryAsync<CardIdRow>(
            "SELECT CardId FROM LearningSessionCards WHERE IsCompleted = 0 ORDER BY QueueOrder, Id"))
        .Select(row => row.CardId)
        .ToArray();

    private static async Task<Fixture> CreateFixtureAsync(int senseCount, int variantsPerSense = 1)
    {
        var databaseFixture = await Schema7Fixture.CreateAsync();
        await HistoricalMigrationFixture.UpgradeToSchema12Async(databaseFixture.Connection);
        var wordId = 0;
        var senseIds = new int[senseCount];
        var cardIds = new int[senseCount];
        var preferredVariantIds = new int[senseCount];
        await databaseFixture.Connection.RunInTransactionAsync(connection =>
        {
            connection.Execute(
                """
                INSERT INTO Words (
                    Language, CanonicalTerm, NormalizedTerm, Status, TokenKind, PreparationState,
                    TotalOccurrenceCount, DocumentCount, AutomaticInteractionMode,
                    ConsecutiveRecallSuccessCount, ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount,
                    MasteryReviewExtensionScheduled, CreatedAt, UpdatedAt)
                VALUES ('en', 'eligible', 'eligible', ?, 0, ?, 1, 1, 0, 0, 0, 0, 0, ?, ?)
                """,
                (int)WordStatus.UnknownBacklog,
                (int)PreparationState.Prepared,
                NowUtc,
                NowUtc);
            wordId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

            for (var senseIndex = 0; senseIndex < senseCount; senseIndex++)
            {
                connection.Execute(
                    """
                    INSERT INTO Senses (
                        StableId, WordId, SourceLanguage, ExplanationLanguage, Status, CreatedAtUtc, UpdatedAtUtc)
                    VALUES (?, ?, 'en', 'en', 0, ?, ?)
                    """,
                    $"eligibility-sense-{senseIndex}",
                    wordId,
                    NowUtc,
                    NowUtc);
                var senseId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
                senseIds[senseIndex] = senseId;

                connection.Execute(
                    """
                    INSERT INTO Meanings (
                        WordId, SenseId, ExplanationLanguage, SourceLanguage, DisplayTerm, EncounteredSurfaceForm,
                        GrammaticalRelationship, TokenKind, Translation, Definition, DictionaryExample, AdditionalNote,
                        AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle, Attribution,
                        ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt, StableId)
                    VALUES (?, ?, 'en', 'en', ?, ?, '', 0, ?, ?, '', '', '[]', ?,
                            'test', 'test', ?, 'test', 1, ?, ?, ?, ?)
                    """,
                    wordId,
                    senseId,
                    $"eligible-{senseIndex}",
                    $"eligible-{senseIndex}",
                    $"meaning-{senseIndex}",
                    $"meaning-{senseIndex}",
                    $"meaning-{senseIndex}",
                    $"eligible-{senseIndex}",
                    NowUtc,
                    NowUtc,
                    NowUtc,
                    $"eligibility-meaning-{senseIndex}");
                var meaningId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
                connection.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", meaningId, senseId);

                connection.Execute(
                    """
                    INSERT INTO LearningCards (
                        WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays,
                        EaseFactor, SuccessfulReviewCount, LapseCount, CreatedAtUtc, UpdatedAtUtc)
                    VALUES (?, ?, ?, 0, 0, ?, 0, 2.5, 0, 0, ?, ?)
                    """,
                    wordId,
                    senseId,
                    meaningId,
                    NowUtc,
                    NowUtc,
                    NowUtc);
                cardIds[senseIndex] = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

                for (var variantIndex = 0; variantIndex < variantsPerSense; variantIndex++)
                {
                    connection.Execute(
                        """
                        INSERT INTO AnswerVariants (
                            StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText, SourceMeaningId,
                            CreatedAtUtc, UpdatedAtUtc)
                        VALUES (?, ?, 'en', ?, ?, ?, ?, ?)
                        """,
                        $"eligibility-variant-{senseIndex}-{variantIndex}",
                        senseId,
                        $"answer-{senseIndex}-{variantIndex}",
                        $"answer-{senseIndex}-{variantIndex}",
                        meaningId,
                        NowUtc,
                        NowUtc);
                    var variantId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
                    if (variantIndex == 0)
                    {
                        preferredVariantIds[senseIndex] = variantId;
                    }

                    connection.Execute(
                        """
                        INSERT INTO SenseAnswerVariantAssignments (
                            StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred,
                            RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                        VALUES (?, ?, 0, ?, 0, ?, ?, ?, ?)
                        """,
                        $"eligibility-assignment-{senseIndex}-{variantIndex}",
                        senseId,
                        variantId,
                        variantIndex == 0 ? 1 : 0,
                        NowUtc,
                        NowUtc,
                        NowUtc);
                }
            }
        });

        await Schema13DormantMigration.ApplyAsync(databaseFixture.Connection);
        return new Fixture(
            databaseFixture,
            wordId,
            senseIds,
            cardIds,
            preferredVariantIds,
            new DateTimeOffset?[senseCount]);
    }

    private sealed record Fixture(
        Schema7Fixture DatabaseFixture,
        int WordId,
        int[] SenseIds,
        int[] CardIds,
        int[] PreferredVariantIds,
        DateTimeOffset?[] DueAtUtc) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => DatabaseFixture.DisposeAsync();
    }

    private sealed class NoOpLexicalEnrichmentService : ILexicalEnrichmentService
    {
        public Task<LexicalResult> EnrichAsync(
            LexicalLookupRequest request,
            string originalDocumentContent,
            string? representativeContext,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("Overview reads must not invoke lexical enrichment.");
    }

    private sealed class CardIdRow
    {
        public int CardId { get; set; }
    }
}
