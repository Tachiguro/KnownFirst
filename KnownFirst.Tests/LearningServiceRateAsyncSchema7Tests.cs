using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Services.Study;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LearningServiceRateAsyncSchema7Tests
{
    private static readonly DateTime Now = new(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task Capability_Schema7_UsesLegacyLearningPath()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-learning-schema7-capability");
        await database.InitializeAsync();

        var before = await database.ReadAsync(async connection => (
            UserVersion: await connection.ExecuteScalarAsync<int>("PRAGMA user_version"),
            SchemaVersion: await connection.ExecuteScalarAsync<int>("PRAGMA schema_version"),
            TableCount: await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'"),
            CardCount: await connection.Table<LearningCardEntity>().CountAsync(),
            SessionCount: await connection.Table<LearningSessionEntity>().CountAsync(),
            QueueCount: await connection.Table<LearningSessionCardEntity>().CountAsync(),
            ReviewCount: await connection.Table<LearningReviewEntity>().CountAsync(),
            TotalChanges: await connection.ExecuteScalarAsync<int>("SELECT total_changes()")));

        var capability = await database.RunInTransactionAsync(LearningSchemaCapability.Resolve);
        var loaded = await CreateLearningService(database).GetOrStartAsync();

        var after = await database.ReadAsync(async connection => (
            UserVersion: await connection.ExecuteScalarAsync<int>("PRAGMA user_version"),
            SchemaVersion: await connection.ExecuteScalarAsync<int>("PRAGMA schema_version"),
            TableCount: await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'"),
            CardCount: await connection.Table<LearningCardEntity>().CountAsync(),
            SessionCount: await connection.Table<LearningSessionEntity>().CountAsync(),
            QueueCount: await connection.Table<LearningSessionCardEntity>().CountAsync(),
            ReviewCount: await connection.Table<LearningReviewEntity>().CountAsync(),
            TotalChanges: await connection.ExecuteScalarAsync<int>("SELECT total_changes()"),
            Schema8TableCount: await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type = 'table'
                  AND name IN ('Senses', 'AnswerVariants', 'SenseAnswerVariantAssignments', 'AnswerVariantProgress')
                """),
            MeaningIdColumnCount: await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM pragma_table_info('LearningCards') WHERE name = 'MeaningId'"),
            PreferredMeaningIdColumnCount: await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM pragma_table_info('LearningCards') WHERE name = 'PreferredMeaningId'")));

        Assert.AreEqual(7, before.UserVersion);
        Assert.IsInstanceOfType<LearningSchema7CapabilityResult>(capability);
        Assert.AreEqual(ValidatedLearningSchema7Capability.SchemaVersion, before.UserVersion);
        Assert.IsNull(loaded.Card);
        Assert.IsNull(loaded.CompletedSummary);
        Assert.AreEqual(0, after.Schema8TableCount);
        Assert.AreEqual(1, after.MeaningIdColumnCount);
        Assert.AreEqual(0, after.PreferredMeaningIdColumnCount);
        Assert.AreEqual(before.UserVersion, after.UserVersion);
        Assert.AreEqual(before.SchemaVersion, after.SchemaVersion);
        Assert.AreEqual(before.TableCount, after.TableCount);
        Assert.AreEqual(before.CardCount, after.CardCount);
        Assert.AreEqual(before.SessionCount, after.SessionCount);
        Assert.AreEqual(before.QueueCount, after.QueueCount);
        Assert.AreEqual(before.ReviewCount, after.ReviewCount);
        Assert.AreEqual(before.TotalChanges, after.TotalChanges);
    }

    [TestMethod]
    public async Task LoadAsync_Schema7_DoesNotRequireSchema8PhysicalShape()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-learning-schema7-load");
        await database.InitializeAsync();
        var seeded = await SeedMeaningToTermCardAsync(
            database,
            canonicalTerm: "legacy-word",
            displayTerm: "legacy-display-term");

        var loaded = await CreateLearningService(database).GetOrStartAsync();
        var physical = await database.ReadAsync(async connection => new
        {
            UserVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version"),
            Schema8TableCount = await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type = 'table'
                  AND name IN ('Senses', 'AnswerVariants', 'SenseAnswerVariantAssignments', 'AnswerVariantProgress')
                """),
            MeaningIdColumnCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM pragma_table_info('LearningCards') WHERE name = 'MeaningId'"),
            PreferredMeaningIdColumnCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM pragma_table_info('LearningCards') WHERE name = 'PreferredMeaningId'"),
            TargetAnswerVariantIdColumnCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM pragma_table_info('LearningSessionCards') WHERE name = 'TargetAnswerVariantId'"),
            ReviewCount = await connection.Table<LearningReviewEntity>().CountAsync(),
            StoredCard = await connection.FindAsync<LearningCardEntity>(seeded.CardId)
        });

        Assert.AreEqual(7, physical.UserVersion);
        Assert.IsNotNull(loaded.Card);
        Assert.IsNull(loaded.CompletedSummary);
        Assert.AreEqual(seeded.CardId, loaded.Card.CardId);
        Assert.AreEqual(seeded.WordId, loaded.Card.WordId);
        Assert.AreEqual(CardDirection.MeaningToTerm, loaded.Card.Direction);
        Assert.AreEqual(LearningInteractionMode.Typing, loaded.Card.InteractionMode);
        Assert.AreEqual("legacy-display-term", loaded.Card.Term);
        Assert.IsNotNull(physical.StoredCard);
        Assert.AreEqual(seeded.MeaningId, physical.StoredCard.MeaningId);
        Assert.AreEqual(1, physical.MeaningIdColumnCount);
        Assert.AreEqual(0, physical.PreferredMeaningIdColumnCount);
        Assert.AreEqual(0, physical.TargetAnswerVariantIdColumnCount);
        Assert.AreEqual(0, physical.ReviewCount);
        Assert.AreEqual(0, physical.Schema8TableCount);
    }

    [TestMethod]
    public async Task CheckSpelling_Schema7_CanonicalAnswerRemainsAccepted()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-learning-schema7-spelling");
        await database.InitializeAsync();
        const string canonicalDisplayTerm = "schema-seven-answer";
        await SeedMeaningToTermCardAsync(
            database,
            canonicalTerm: "different-word-term",
            displayTerm: canonicalDisplayTerm);
        var service = CreateLearningService(database);
        var card = (await service.GetOrStartAsync()).Card
            ?? throw new AssertFailedException("The genuine Schema-7 card was not loaded.");

        var result = await service.CheckSpellingAsync(card.QueueItemId, canonicalDisplayTerm);
        var persisted = await database.ReadAsync(async connection => new
        {
            Queue = await connection.FindAsync<LearningSessionCardEntity>(card.QueueItemId),
            ReviewCount = await connection.Table<LearningReviewEntity>().CountAsync(),
            AssignmentTableCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SenseAnswerVariantAssignments'"),
            ProgressTableCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'AnswerVariantProgress'"),
            TargetAnswerVariantIdColumnCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM pragma_table_info('LearningSessionCards') WHERE name = 'TargetAnswerVariantId'")
        });

        Assert.AreEqual(canonicalDisplayTerm, card.Term);
        Assert.IsTrue(result.IsCorrect);
        Assert.AreEqual(canonicalDisplayTerm, result.EnteredAnswer);
        Assert.AreEqual(canonicalDisplayTerm, result.CorrectAnswer);
        Assert.AreEqual(string.Empty, result.Difference);
        Assert.IsNull(result.MatchedAlias);
        Assert.IsFalse(result.RatingWasPersisted);
        Assert.IsNull(result.MatchedAnswerVariantId);
        Assert.IsNotNull(persisted.Queue);
        Assert.IsTrue(persisted.Queue.SpellingChecked);
        Assert.IsTrue(persisted.Queue.SpellingCorrect);
        Assert.IsTrue(persisted.Queue.AnswerRevealed);
        Assert.IsFalse(persisted.Queue.IsCompleted);
        Assert.IsNull(persisted.Queue.Rating);
        Assert.IsNull(persisted.Queue.CompletedAtUtc);
        Assert.AreEqual(0, persisted.ReviewCount);
        Assert.AreEqual(0, persisted.AssignmentTableCount);
        Assert.AreEqual(0, persisted.ProgressTableCount);
        Assert.AreEqual(0, persisted.TargetAnswerVariantIdColumnCount);
    }

    [TestMethod]
    public async Task CheckSpelling_Schema7_AcceptedAliasRemainsAccepted()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-learning-schema7-alias");
        await database.InitializeAsync();
        await SeedMeaningToTermCardAsync(
            database,
            canonicalTerm: "different-word-term",
            displayTerm: "canonical-answer",
            acceptedAliasesJson: "[\"Caf\\u00E9 alias\"]");
        var service = CreateLearningService(database);
        var card = (await service.GetOrStartAsync()).Card
            ?? throw new AssertFailedException("The genuine Schema-7 alias card was not loaded.");

        var result = await service.CheckSpellingAsync(card.QueueItemId, " Cafe\u0301 alias ");
        var persisted = await database.ReadAsync(async connection => new
        {
            UserVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version"),
            Queue = await connection.FindAsync<LearningSessionCardEntity>(card.QueueItemId),
            ReviewCount = await connection.Table<LearningReviewEntity>().CountAsync(),
            Schema8TableCount = await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type = 'table'
                  AND name IN ('Senses', 'AnswerVariants', 'SenseAnswerVariantAssignments', 'AnswerVariantProgress')
                """),
            TargetAnswerVariantIdColumnCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM pragma_table_info('LearningSessionCards') WHERE name = 'TargetAnswerVariantId'")
        });

        Assert.AreEqual(7, persisted.UserVersion);
        Assert.IsTrue(result.IsCorrect);
        Assert.AreEqual(" Cafe\u0301 alias ", result.EnteredAnswer);
        Assert.AreEqual("canonical-answer", result.CorrectAnswer);
        Assert.AreEqual("Caf\u00E9 alias", result.MatchedAlias);
        Assert.IsFalse(result.RatingWasPersisted);
        Assert.IsNull(result.MatchedAnswerVariantId);
        Assert.IsNotNull(persisted.Queue);
        Assert.IsTrue(persisted.Queue.SpellingChecked);
        Assert.IsTrue(persisted.Queue.SpellingCorrect);
        Assert.IsFalse(persisted.Queue.IsCompleted);
        Assert.IsNull(persisted.Queue.Rating);
        Assert.AreEqual(0, persisted.ReviewCount);
        Assert.AreEqual(0, persisted.Schema8TableCount);
        Assert.AreEqual(0, persisted.TargetAnswerVariantIdColumnCount);
    }

    [TestMethod]
    public async Task CheckSpelling_Schema7_WrongAnswerPersistsAgainWithoutVariantMetadata()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-learning-schema7-wrong-answer");
        await database.InitializeAsync();
        var seeded = await SeedMeaningToTermCardAsync(
            database,
            canonicalTerm: "legacy-word",
            displayTerm: "canonical-answer",
            acceptedAliasesJson: "[\"accepted-alias\"]");
        var service = CreateLearningService(database);
        var card = (await service.GetOrStartAsync()).Card
            ?? throw new AssertFailedException("The genuine Schema-7 wrong-answer card was not loaded.");
        var beforeReviewCount = await database.ReadAsync(connection =>
            connection.Table<LearningReviewEntity>().CountAsync());

        var result = await service.CheckSpellingAsync(card.QueueItemId, "neither-canonical-nor-alias");
        var persisted = await database.ReadAsync(async connection => new
        {
            UserVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version"),
            Reviews = await connection.Table<LearningReviewEntity>().ToListAsync(),
            OriginalQueue = await connection.FindAsync<LearningSessionCardEntity>(card.QueueItemId),
            QueueRows = await connection.Table<LearningSessionCardEntity>().OrderBy(row => row.QueueOrder).ToListAsync(),
            StoredCard = await connection.FindAsync<LearningCardEntity>(seeded.CardId),
            Schema8TableCount = await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type = 'table'
                  AND name IN ('Senses', 'AnswerVariants', 'SenseAnswerVariantAssignments', 'AnswerVariantProgress')
                """),
            TargetAnswerVariantIdColumnCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM pragma_table_info('LearningSessionCards') WHERE name = 'TargetAnswerVariantId'"),
            ReviewVariantColumnCount = await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM pragma_table_info('LearningReviews')
                WHERE name IN ('TargetAnswerVariantId', 'MatchedAnswerVariantId')
                """)
        });

        Assert.AreEqual(7, persisted.UserVersion);
        Assert.AreEqual(0, beforeReviewCount);
        Assert.HasCount(1, persisted.Reviews);
        Assert.IsFalse(result.IsCorrect);
        Assert.IsTrue(result.RatingWasPersisted);
        Assert.IsNull(result.MatchedAlias);
        Assert.IsNull(result.MatchedAnswerVariantId);
        var review = persisted.Reviews.Single();
        Assert.AreEqual(ReviewRating.Again, review.Rating);
        Assert.IsTrue(review.WasTypedAnswer);
        Assert.IsFalse(review.WasCorrect);
        Assert.AreEqual(seeded.CardId, review.CardId);
        Assert.IsNotNull(persisted.OriginalQueue);
        Assert.IsTrue(persisted.OriginalQueue.IsCompleted);
        Assert.AreEqual(ReviewRating.Again, persisted.OriginalQueue.Rating);
        Assert.IsNotNull(persisted.OriginalQueue.CompletedAtUtc);
        Assert.HasCount(2, persisted.QueueRows);
        Assert.IsTrue(persisted.QueueRows[1].IsAgainRepeat);
        Assert.IsFalse(persisted.QueueRows[1].IsCompleted);
        Assert.IsNotNull(persisted.StoredCard);
        Assert.AreEqual(CardState.Learning, persisted.StoredCard.State);
        Assert.AreEqual(ReviewRating.Again, persisted.StoredCard.LastRating);
        Assert.AreEqual(0, persisted.Schema8TableCount);
        Assert.AreEqual(0, persisted.TargetAnswerVariantIdColumnCount);
        Assert.AreEqual(0, persisted.ReviewVariantColumnCount);
    }

    [TestMethod]
    public async Task RateAsync_Schema7_NullMatchedAnswerVariantIdRemainsValid()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-learning-schema7-rating");
        await database.InitializeAsync();
        var seeded = await SeedMeaningToTermCardAsync(
            database,
            canonicalTerm: "legacy-reading-word",
            displayTerm: "legacy-reading-answer",
            direction: CardDirection.TermToMeaning);
        var service = CreateLearningService(database);
        var card = (await service.GetOrStartAsync()).Card
            ?? throw new AssertFailedException("The genuine Schema-7 reading card was not loaded.");
        var beforeReviewCount = await database.ReadAsync(connection =>
            connection.Table<LearningReviewEntity>().CountAsync());
        await service.RevealAnswerAsync(card.QueueItemId);

        var result = await service.RateAsync(card.QueueItemId, ReviewRating.Good);
        var persisted = await database.ReadAsync(async connection => new
        {
            UserVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version"),
            Reviews = await connection.Table<LearningReviewEntity>().ToListAsync(),
            Queue = await connection.FindAsync<LearningSessionCardEntity>(card.QueueItemId),
            StoredCard = await connection.FindAsync<LearningCardEntity>(seeded.CardId),
            Schema8TableCount = await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type = 'table'
                  AND name IN ('Senses', 'AnswerVariants', 'SenseAnswerVariantAssignments', 'AnswerVariantProgress')
                """),
            TargetAnswerVariantIdColumnCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM pragma_table_info('LearningSessionCards') WHERE name = 'TargetAnswerVariantId'"),
            ReviewVariantColumnCount = await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM pragma_table_info('LearningReviews')
                WHERE name IN ('TargetAnswerVariantId', 'MatchedAnswerVariantId')
                """)
        });

        Assert.AreEqual(7, persisted.UserVersion);
        Assert.AreEqual(LearningInteractionMode.Reading, card.InteractionMode);
        Assert.AreEqual(0, beforeReviewCount);
        Assert.HasCount(1, persisted.Reviews);
        Assert.AreEqual(ReviewRating.Good, persisted.Reviews.Single().Rating);
        Assert.IsFalse(persisted.Reviews.Single().WasTypedAnswer);
        Assert.IsTrue(persisted.Reviews.Single().WasCorrect);
        Assert.IsNotNull(persisted.Queue);
        Assert.IsTrue(persisted.Queue.IsCompleted);
        Assert.AreEqual(ReviewRating.Good, persisted.Queue.Rating);
        Assert.IsNotNull(persisted.StoredCard);
        Assert.AreEqual(CardState.Review, persisted.StoredCard.State);
        Assert.AreEqual(ReviewRating.Good, persisted.StoredCard.LastRating);
        Assert.IsNull(result.Card);
        Assert.IsNotNull(result.CompletedSummary);
        Assert.AreEqual(1, result.CompletedSummary.CardsReviewed);
        Assert.AreEqual(1, result.CompletedSummary.GoodCount);
        Assert.AreEqual(0, persisted.Schema8TableCount);
        Assert.AreEqual(0, persisted.TargetAnswerVariantIdColumnCount);
        Assert.AreEqual(0, persisted.ReviewVariantColumnCount);
    }

    [TestMethod]
    public async Task RateAsync_Schema7_PersistsExactlyOneLegacyReview()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-learning-schema7-one-review");
        await database.InitializeAsync();
        var seeded = await SeedMeaningToTermCardAsync(
            database,
            canonicalTerm: "one-review-word",
            displayTerm: "one-review-answer",
            direction: CardDirection.TermToMeaning);
        var service = CreateLearningService(database);
        var card = (await service.GetOrStartAsync()).Card
            ?? throw new AssertFailedException("The genuine Schema-7 one-review card was not loaded.");
        var beforeReviewCount = await database.ReadAsync(connection =>
            connection.Table<LearningReviewEntity>().CountAsync());
        await service.RevealAnswerAsync(card.QueueItemId);

        _ = await service.RateAsync(card.QueueItemId, ReviewRating.Hard);
        var persisted = await database.ReadAsync(async connection => new
        {
            UserVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version"),
            AllReviews = await connection.Table<LearningReviewEntity>().ToListAsync(),
            RatedCardReviews = await connection.Table<LearningReviewEntity>()
                .Where(review => review.CardId == seeded.CardId)
                .ToListAsync(),
            Queue = await connection.FindAsync<LearningSessionCardEntity>(card.QueueItemId),
            Schema8TableCount = await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type = 'table'
                  AND name IN ('Senses', 'AnswerVariants', 'SenseAnswerVariantAssignments', 'AnswerVariantProgress')
                """),
            QueueTargetColumnCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM pragma_table_info('LearningSessionCards') WHERE name = 'TargetAnswerVariantId'"),
            ReviewVariantColumnCount = await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM pragma_table_info('LearningReviews')
                WHERE name IN ('TargetAnswerVariantId', 'MatchedAnswerVariantId')
                """)
        });

        Assert.AreEqual(7, persisted.UserVersion);
        Assert.AreEqual(0, beforeReviewCount);
        Assert.HasCount(1, persisted.AllReviews);
        Assert.HasCount(1, persisted.RatedCardReviews);
        var review = persisted.RatedCardReviews.Single();
        Assert.AreEqual(seeded.CardId, review.CardId);
        Assert.AreEqual(card.SessionId, review.SessionId);
        Assert.AreEqual(ReviewRating.Hard, review.Rating);
        Assert.IsFalse(review.WasTypedAnswer);
        Assert.IsTrue(review.WasCorrect);
        Assert.IsNotNull(persisted.Queue);
        Assert.IsTrue(persisted.Queue.IsCompleted);
        Assert.AreEqual(ReviewRating.Hard, persisted.Queue.Rating);
        Assert.IsNotNull(persisted.Queue.CompletedAtUtc);
        Assert.AreEqual(0, persisted.Schema8TableCount);
        Assert.AreEqual(0, persisted.QueueTargetColumnCount);
        Assert.AreEqual(0, persisted.ReviewVariantColumnCount);
    }

    [TestMethod]
    public async Task RateAsync_Schema7_UpdatesLegacyCardSchedule()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-learning-schema7-schedule");
        await database.InitializeAsync();
        var ratedSeed = await SeedMeaningToTermCardAsync(
            database,
            canonicalTerm: "alpha-rated-word",
            displayTerm: "rated-answer",
            direction: CardDirection.TermToMeaning);
        var untouchedSeed = await SeedMeaningToTermCardAsync(
            database,
            canonicalTerm: "zeta-untouched-word",
            displayTerm: "untouched-answer",
            direction: CardDirection.TermToMeaning);
        var service = CreateLearningService(database);
        var card = (await service.GetOrStartAsync()).Card
            ?? throw new AssertFailedException("The genuine Schema-7 schedule card was not loaded.");
        Assert.AreEqual(ratedSeed.CardId, card.CardId);
        var before = await database.ReadAsync(async connection => new
        {
            Rated = await connection.FindAsync<LearningCardEntity>(ratedSeed.CardId),
            Untouched = await connection.FindAsync<LearningCardEntity>(untouchedSeed.CardId),
            ReviewCount = await connection.Table<LearningReviewEntity>().CountAsync()
        });
        await service.RevealAnswerAsync(card.QueueItemId);

        _ = await service.RateAsync(card.QueueItemId, ReviewRating.Good);
        var after = await database.ReadAsync(async connection => new
        {
            UserVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version"),
            Rated = await connection.FindAsync<LearningCardEntity>(ratedSeed.CardId),
            Untouched = await connection.FindAsync<LearningCardEntity>(untouchedSeed.CardId),
            Reviews = await connection.Table<LearningReviewEntity>().ToListAsync(),
            ProgressTableCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'AnswerVariantProgress'"),
            ReviewVariantColumnCount = await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM pragma_table_info('LearningReviews')
                WHERE name IN ('TargetAnswerVariantId', 'MatchedAnswerVariantId')
                """)
        });

        Assert.AreEqual(7, after.UserVersion);
        Assert.IsNotNull(before.Rated);
        Assert.AreEqual(CardState.New, before.Rated.State);
        Assert.AreEqual(Now, before.Rated.DueAtUtc);
        Assert.AreEqual(0, before.Rated.IntervalDays);
        Assert.AreEqual(SimpleSpacedRepetitionScheduler.DefaultEaseFactor, before.Rated.EaseFactor, 0.0001);
        Assert.IsNull(before.Rated.LastReviewedAtUtc);
        Assert.IsNull(before.Rated.LastRating);
        Assert.AreEqual(0, before.ReviewCount);
        Assert.IsNotNull(after.Rated);
        Assert.AreEqual(CardState.Review, after.Rated.State);
        Assert.AreEqual(Now.AddDays(3), after.Rated.DueAtUtc);
        Assert.AreEqual(3, after.Rated.IntervalDays);
        Assert.AreEqual(SimpleSpacedRepetitionScheduler.DefaultEaseFactor, after.Rated.EaseFactor, 0.0001);
        Assert.AreEqual(Now, after.Rated.LastReviewedAtUtc);
        Assert.AreEqual(ReviewRating.Good, after.Rated.LastRating);
        Assert.HasCount(1, after.Reviews);
        var review = after.Reviews.Single();
        Assert.AreEqual(ratedSeed.CardId, review.CardId);
        Assert.AreEqual(ReviewRating.Good, review.Rating);
        Assert.AreEqual(Now, review.ReviewedAtUtc);
        Assert.AreEqual(Now.AddDays(3), review.DueAtUtc);
        Assert.AreEqual(3, review.IntervalDays);
        Assert.AreEqual(SimpleSpacedRepetitionScheduler.DefaultEaseFactor, review.EaseFactor, 0.0001);
        Assert.IsNotNull(before.Untouched);
        Assert.IsNotNull(after.Untouched);
        Assert.AreEqual(before.Untouched.State, after.Untouched.State);
        Assert.AreEqual(before.Untouched.DueAtUtc, after.Untouched.DueAtUtc);
        Assert.AreEqual(before.Untouched.IntervalDays, after.Untouched.IntervalDays);
        Assert.AreEqual(before.Untouched.EaseFactor, after.Untouched.EaseFactor, 0.0001);
        Assert.AreEqual(before.Untouched.LastReviewedAtUtc, after.Untouched.LastReviewedAtUtc);
        Assert.AreEqual(before.Untouched.LastRating, after.Untouched.LastRating);
        Assert.AreEqual(0, after.ProgressTableCount);
        Assert.AreEqual(0, after.ReviewVariantColumnCount);
    }

    [TestMethod]
    public async Task RateAsync_Schema7_CompletedQueueItemRejectsDuplicateWithoutMutation()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-learning-schema7-duplicate");
        await database.InitializeAsync();
        var ratedSeed = await SeedMeaningToTermCardAsync(
            database,
            canonicalTerm: "alpha-rated-word",
            displayTerm: "rated-answer",
            direction: CardDirection.TermToMeaning);
        _ = await SeedMeaningToTermCardAsync(
            database,
            canonicalTerm: "zeta-remaining-word",
            displayTerm: "remaining-answer",
            direction: CardDirection.TermToMeaning);
        var service = CreateLearningService(database);
        var first = (await service.GetOrStartAsync()).Card
            ?? throw new AssertFailedException("The first genuine Schema-7 queue item was not loaded.");
        Assert.AreEqual(ratedSeed.CardId, first.CardId);
        await service.RevealAnswerAsync(first.QueueItemId);

        var firstResult = await service.RateAsync(first.QueueItemId, ReviewRating.Good);
        Assert.IsNotNull(firstResult.Card);
        Assert.AreNotEqual(first.QueueItemId, firstResult.Card.QueueItemId);
        var beforeDuplicate = await database.ReadAsync(async connection =>
        {
            var reviews = await connection.Table<LearningReviewEntity>()
                .Where(review => review.CardId == ratedSeed.CardId)
                .ToListAsync();
            var card = await connection.FindAsync<LearningCardEntity>(ratedSeed.CardId)
                ?? throw new AssertFailedException("The rated legacy card disappeared.");
            var queue = await connection.FindAsync<LearningSessionCardEntity>(first.QueueItemId)
                ?? throw new AssertFailedException("The completed legacy queue row disappeared.");
            var session = await connection.FindAsync<LearningSessionEntity>(first.SessionId)
                ?? throw new AssertFailedException("The active legacy session disappeared.");
            var word = await connection.FindAsync<WordEntity>(first.WordId)
                ?? throw new AssertFailedException("The rated legacy word disappeared.");
            var queueRows = await connection.Table<LearningSessionCardEntity>()
                .Where(row => row.SessionId == first.SessionId)
                .OrderBy(row => row.QueueOrder)
                .ToListAsync();
            return new
            {
                UserVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version"),
                Reviews = reviews.Select(review => new
                {
                    review.Id,
                    review.CardId,
                    review.SessionId,
                    review.Rating,
                    review.WasTypedAnswer,
                    review.WasCorrect,
                    review.ReviewedAtUtc,
                    review.DueAtUtc,
                    review.IntervalDays,
                    review.EaseFactor
                }).ToArray(),
                Card = new
                {
                    card.Id,
                    card.WordId,
                    card.MeaningId,
                    card.Direction,
                    card.State,
                    card.DueAtUtc,
                    card.IntervalDays,
                    card.EaseFactor,
                    card.SuccessfulReviewCount,
                    card.LapseCount,
                    card.LastReviewedAtUtc,
                    card.LastRating,
                    card.CreatedAtUtc,
                    card.UpdatedAtUtc
                },
                Queue = new
                {
                    queue.Id,
                    queue.SessionId,
                    queue.CardId,
                    queue.QueueOrder,
                    queue.IsDueCard,
                    queue.IsAgainRepeat,
                    queue.AnswerRevealed,
                    queue.SpellingChecked,
                    queue.SpellingCorrect,
                    queue.IsCompleted,
                    queue.Rating,
                    queue.CompletedAtUtc
                },
                Session = new
                {
                    session.Id,
                    session.Status,
                    session.TotalCards,
                    session.CompletedCards,
                    session.AgainCount,
                    session.HardCount,
                    session.GoodCount,
                    session.EasyCount,
                    session.StartedAtUtc,
                    session.UpdatedAtUtc,
                    session.CompletedAtUtc
                },
                Word = new
                {
                    word.Id,
                    word.Status,
                    word.UpdatedAt,
                    word.AutomaticInteractionMode,
                    word.ConsecutiveRecallSuccessCount,
                    word.ConsecutiveTypingSuccessCount,
                    word.ConsecutiveTypingFailureCount,
                    word.MasteryReviewExtensionScheduled
                },
                QueueRows = queueRows.Select(row => new
                {
                    row.Id,
                    row.SessionId,
                    row.CardId,
                    row.QueueOrder,
                    row.IsAgainRepeat,
                    row.IsCompleted,
                    row.Rating,
                    row.CompletedAtUtc
                }).ToArray()
            };
        });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.RateAsync(first.QueueItemId, ReviewRating.Easy));

        var afterDuplicate = await database.ReadAsync(async connection =>
        {
            var reviews = await connection.Table<LearningReviewEntity>()
                .Where(review => review.CardId == ratedSeed.CardId)
                .ToListAsync();
            var card = await connection.FindAsync<LearningCardEntity>(ratedSeed.CardId)
                ?? throw new AssertFailedException("The rated legacy card disappeared after rejection.");
            var queue = await connection.FindAsync<LearningSessionCardEntity>(first.QueueItemId)
                ?? throw new AssertFailedException("The completed legacy queue row disappeared after rejection.");
            var session = await connection.FindAsync<LearningSessionEntity>(first.SessionId)
                ?? throw new AssertFailedException("The active legacy session disappeared after rejection.");
            var word = await connection.FindAsync<WordEntity>(first.WordId)
                ?? throw new AssertFailedException("The rated legacy word disappeared after rejection.");
            var queueRows = await connection.Table<LearningSessionCardEntity>()
                .Where(row => row.SessionId == first.SessionId)
                .OrderBy(row => row.QueueOrder)
                .ToListAsync();
            return new
            {
                UserVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version"),
                Reviews = reviews.Select(review => new
                {
                    review.Id,
                    review.CardId,
                    review.SessionId,
                    review.Rating,
                    review.WasTypedAnswer,
                    review.WasCorrect,
                    review.ReviewedAtUtc,
                    review.DueAtUtc,
                    review.IntervalDays,
                    review.EaseFactor
                }).ToArray(),
                Card = new
                {
                    card.Id,
                    card.WordId,
                    card.MeaningId,
                    card.Direction,
                    card.State,
                    card.DueAtUtc,
                    card.IntervalDays,
                    card.EaseFactor,
                    card.SuccessfulReviewCount,
                    card.LapseCount,
                    card.LastReviewedAtUtc,
                    card.LastRating,
                    card.CreatedAtUtc,
                    card.UpdatedAtUtc
                },
                Queue = new
                {
                    queue.Id,
                    queue.SessionId,
                    queue.CardId,
                    queue.QueueOrder,
                    queue.IsDueCard,
                    queue.IsAgainRepeat,
                    queue.AnswerRevealed,
                    queue.SpellingChecked,
                    queue.SpellingCorrect,
                    queue.IsCompleted,
                    queue.Rating,
                    queue.CompletedAtUtc
                },
                Session = new
                {
                    session.Id,
                    session.Status,
                    session.TotalCards,
                    session.CompletedCards,
                    session.AgainCount,
                    session.HardCount,
                    session.GoodCount,
                    session.EasyCount,
                    session.StartedAtUtc,
                    session.UpdatedAtUtc,
                    session.CompletedAtUtc
                },
                Word = new
                {
                    word.Id,
                    word.Status,
                    word.UpdatedAt,
                    word.AutomaticInteractionMode,
                    word.ConsecutiveRecallSuccessCount,
                    word.ConsecutiveTypingSuccessCount,
                    word.ConsecutiveTypingFailureCount,
                    word.MasteryReviewExtensionScheduled
                },
                QueueRows = queueRows.Select(row => new
                {
                    row.Id,
                    row.SessionId,
                    row.CardId,
                    row.QueueOrder,
                    row.IsAgainRepeat,
                    row.IsCompleted,
                    row.Rating,
                    row.CompletedAtUtc
                }).ToArray(),
                Schema8TableCount = await connection.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*) FROM sqlite_master
                    WHERE type = 'table'
                      AND name IN ('Senses', 'AnswerVariants', 'SenseAnswerVariantAssignments', 'AnswerVariantProgress')
                    """),
                QueueTargetColumnCount = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('LearningSessionCards') WHERE name = 'TargetAnswerVariantId'"),
                ReviewVariantColumnCount = await connection.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*) FROM pragma_table_info('LearningReviews')
                    WHERE name IN ('TargetAnswerVariantId', 'MatchedAnswerVariantId')
                    """)
            };
        });

        Assert.AreEqual(7, beforeDuplicate.UserVersion);
        Assert.IsTrue(beforeDuplicate.Queue.IsCompleted);
        Assert.AreEqual(ReviewRating.Good, beforeDuplicate.Queue.Rating);
        Assert.HasCount(1, beforeDuplicate.Reviews);
        Assert.HasCount(2, beforeDuplicate.QueueRows);
        Assert.IsFalse(beforeDuplicate.QueueRows.Any(row => row.IsAgainRepeat));
        Assert.AreEqual(beforeDuplicate.UserVersion, afterDuplicate.UserVersion);
        CollectionAssert.AreEqual(beforeDuplicate.Reviews, afterDuplicate.Reviews);
        Assert.AreEqual(beforeDuplicate.Card, afterDuplicate.Card);
        Assert.AreEqual(beforeDuplicate.Queue, afterDuplicate.Queue);
        Assert.AreEqual(beforeDuplicate.Session, afterDuplicate.Session);
        Assert.AreEqual(beforeDuplicate.Word, afterDuplicate.Word);
        CollectionAssert.AreEqual(beforeDuplicate.QueueRows, afterDuplicate.QueueRows);
        Assert.HasCount(1, afterDuplicate.Reviews);
        Assert.HasCount(2, afterDuplicate.QueueRows);
        Assert.IsFalse(afterDuplicate.QueueRows.Any(row => row.IsAgainRepeat));
        Assert.AreEqual(0, afterDuplicate.Schema8TableCount);
        Assert.AreEqual(0, afterDuplicate.QueueTargetColumnCount);
        Assert.AreEqual(0, afterDuplicate.ReviewVariantColumnCount);
    }

    [TestMethod]
    public async Task ResumeActiveSession_Schema7_RemainsFunctional()
    {
        await using var database = new TemporaryKnownFirstDatabase("knownfirst-learning-schema7-resume");
        await database.InitializeAsync();
        var firstSeed = await SeedMeaningToTermCardAsync(
            database,
            canonicalTerm: "alpha-first-word",
            displayTerm: "first-answer",
            direction: CardDirection.TermToMeaning);
        var secondSeed = await SeedMeaningToTermCardAsync(
            database,
            canonicalTerm: "zeta-second-word",
            displayTerm: "second-answer",
            direction: CardDirection.TermToMeaning);
        var originalService = CreateLearningService(database);
        var first = (await originalService.GetOrStartAsync()).Card
            ?? throw new AssertFailedException("The first genuine Schema-7 queue item was not loaded.");
        Assert.AreEqual(firstSeed.CardId, first.CardId);
        await originalService.RevealAnswerAsync(first.QueueItemId);
        var progress = await originalService.RateAsync(first.QueueItemId, ReviewRating.Good);
        var second = progress.Card
            ?? throw new AssertFailedException("The second genuine Schema-7 queue item was not loaded.");
        Assert.AreEqual(secondSeed.CardId, second.CardId);
        await originalService.RevealAnswerAsync(second.QueueItemId);

        var beforeResume = await database.ReadAsync(async connection =>
        {
            var sessions = await connection.Table<LearningSessionEntity>().OrderBy(session => session.Id).ToListAsync();
            var queueRows = await connection.Table<LearningSessionCardEntity>().OrderBy(row => row.QueueOrder).ToListAsync();
            var cards = await connection.Table<LearningCardEntity>().OrderBy(card => card.Id).ToListAsync();
            var reviews = await connection.Table<LearningReviewEntity>().OrderBy(review => review.Id).ToListAsync();
            return new
            {
                UserVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version"),
                Sessions = sessions.Select(session => new
                {
                    session.Id,
                    session.Status,
                    session.TotalCards,
                    session.CompletedCards,
                    session.AgainCount,
                    session.HardCount,
                    session.GoodCount,
                    session.EasyCount,
                    session.StartedAtUtc,
                    session.UpdatedAtUtc,
                    session.CompletedAtUtc
                }).ToArray(),
                QueueRows = queueRows.Select(row => new
                {
                    row.Id,
                    row.SessionId,
                    row.CardId,
                    row.QueueOrder,
                    row.IsDueCard,
                    row.IsAgainRepeat,
                    row.AnswerRevealed,
                    row.SpellingChecked,
                    row.SpellingCorrect,
                    row.IsCompleted,
                    row.Rating,
                    row.CompletedAtUtc
                }).ToArray(),
                Cards = cards.Select(card => new
                {
                    card.Id,
                    card.State,
                    card.DueAtUtc,
                    card.IntervalDays,
                    card.EaseFactor,
                    card.SuccessfulReviewCount,
                    card.LapseCount,
                    card.LastReviewedAtUtc,
                    card.LastRating,
                    card.UpdatedAtUtc
                }).ToArray(),
                Reviews = reviews.Select(review => new
                {
                    review.Id,
                    review.CardId,
                    review.SessionId,
                    review.Rating,
                    review.ReviewedAtUtc
                }).ToArray()
            };
        });

        var reconstructedService = CreateLearningService(database);
        var resumed = await reconstructedService.GetOrStartAsync();

        var afterResume = await database.ReadAsync(async connection =>
        {
            var sessions = await connection.Table<LearningSessionEntity>().OrderBy(session => session.Id).ToListAsync();
            var queueRows = await connection.Table<LearningSessionCardEntity>().OrderBy(row => row.QueueOrder).ToListAsync();
            var cards = await connection.Table<LearningCardEntity>().OrderBy(card => card.Id).ToListAsync();
            var reviews = await connection.Table<LearningReviewEntity>().OrderBy(review => review.Id).ToListAsync();
            return new
            {
                UserVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version"),
                Sessions = sessions.Select(session => new
                {
                    session.Id,
                    session.Status,
                    session.TotalCards,
                    session.CompletedCards,
                    session.AgainCount,
                    session.HardCount,
                    session.GoodCount,
                    session.EasyCount,
                    session.StartedAtUtc,
                    session.UpdatedAtUtc,
                    session.CompletedAtUtc
                }).ToArray(),
                QueueRows = queueRows.Select(row => new
                {
                    row.Id,
                    row.SessionId,
                    row.CardId,
                    row.QueueOrder,
                    row.IsDueCard,
                    row.IsAgainRepeat,
                    row.AnswerRevealed,
                    row.SpellingChecked,
                    row.SpellingCorrect,
                    row.IsCompleted,
                    row.Rating,
                    row.CompletedAtUtc
                }).ToArray(),
                Cards = cards.Select(card => new
                {
                    card.Id,
                    card.State,
                    card.DueAtUtc,
                    card.IntervalDays,
                    card.EaseFactor,
                    card.SuccessfulReviewCount,
                    card.LapseCount,
                    card.LastReviewedAtUtc,
                    card.LastRating,
                    card.UpdatedAtUtc
                }).ToArray(),
                Reviews = reviews.Select(review => new
                {
                    review.Id,
                    review.CardId,
                    review.SessionId,
                    review.Rating,
                    review.ReviewedAtUtc
                }).ToArray(),
                Schema8TableCount = await connection.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*) FROM sqlite_master
                    WHERE type = 'table'
                      AND name IN ('Senses', 'AnswerVariants', 'SenseAnswerVariantAssignments', 'AnswerVariantProgress')
                    """),
                QueueTargetColumnCount = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('LearningSessionCards') WHERE name = 'TargetAnswerVariantId'"),
                ReviewVariantColumnCount = await connection.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*) FROM pragma_table_info('LearningReviews')
                    WHERE name IN ('TargetAnswerVariantId', 'MatchedAnswerVariantId')
                    """)
            };
        });

        Assert.AreEqual(7, beforeResume.UserVersion);
        Assert.IsNotNull(resumed.Card);
        Assert.IsNull(resumed.CompletedSummary);
        Assert.AreEqual(first.SessionId, resumed.Card.SessionId);
        Assert.AreEqual(second.QueueItemId, resumed.Card.QueueItemId);
        Assert.AreEqual(secondSeed.CardId, resumed.Card.CardId);
        Assert.AreNotEqual(first.QueueItemId, resumed.Card.QueueItemId);
        Assert.AreEqual(CardDirection.TermToMeaning, resumed.Card.Direction);
        Assert.IsTrue(resumed.Card.AnswerRevealed);
        Assert.HasCount(1, beforeResume.Sessions);
        Assert.AreEqual(LearningSessionStatus.Active, beforeResume.Sessions[0].Status);
        Assert.HasCount(2, beforeResume.QueueRows);
        Assert.AreEqual(0, beforeResume.QueueRows[0].QueueOrder);
        Assert.IsTrue(beforeResume.QueueRows[0].IsCompleted);
        Assert.AreEqual(1, beforeResume.QueueRows[1].QueueOrder);
        Assert.IsFalse(beforeResume.QueueRows[1].IsCompleted);
        Assert.IsTrue(beforeResume.QueueRows[1].AnswerRevealed);
        Assert.AreEqual(beforeResume.UserVersion, afterResume.UserVersion);
        CollectionAssert.AreEqual(beforeResume.Sessions, afterResume.Sessions);
        CollectionAssert.AreEqual(beforeResume.QueueRows, afterResume.QueueRows);
        CollectionAssert.AreEqual(beforeResume.Cards, afterResume.Cards);
        CollectionAssert.AreEqual(beforeResume.Reviews, afterResume.Reviews);
        Assert.HasCount(1, afterResume.Sessions);
        Assert.HasCount(2, afterResume.QueueRows);
        Assert.HasCount(1, afterResume.Reviews);
        Assert.AreEqual(0, afterResume.Schema8TableCount);
        Assert.AreEqual(0, afterResume.QueueTargetColumnCount);
        Assert.AreEqual(0, afterResume.ReviewVariantColumnCount);

        var completed = await reconstructedService.RateAsync(resumed.Card.QueueItemId, ReviewRating.Easy);
        var finalReviewCount = await database.ReadAsync(connection =>
            connection.Table<LearningReviewEntity>().CountAsync());
        Assert.IsNull(completed.Card);
        Assert.IsNotNull(completed.CompletedSummary);
        Assert.AreEqual(first.SessionId, completed.CompletedSummary.SessionId);
        Assert.AreEqual(2, finalReviewCount);
    }

    private static LearningService CreateLearningService(TemporaryKnownFirstDatabase database) => new(
        database,
        new SimpleSpacedRepetitionScheduler(),
        new SpellingAnswerComparer(),
        new FakeClock(Now));

    private static Task<(int WordId, int MeaningId, int CardId)> SeedMeaningToTermCardAsync(
        TemporaryKnownFirstDatabase database,
        string canonicalTerm,
        string displayTerm,
        string acceptedAliasesJson = "[]",
        CardDirection direction = CardDirection.MeaningToTerm) => database.RunInTransactionAsync(connection =>
    {
        var word = new WordEntity
        {
            Language = "en",
            CanonicalTerm = canonicalTerm,
            NormalizedTerm = canonicalTerm,
            Status = WordStatus.Prepared,
            TokenKind = TokenKind.Word,
            PreparationState = PreparationState.Prepared,
            TotalOccurrenceCount = 1,
            DocumentCount = 1,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        connection.Insert(word);

        var meaning = new MeaningEntity
        {
            WordId = word.Id,
            SourceLanguage = "en",
            ExplanationLanguage = "de",
            DisplayTerm = displayTerm,
            Translation = "deterministic translation",
            Definition = "deterministic definition",
            TranslationOrDefinition = "deterministic translation",
            AcceptedAliasesJson = acceptedAliasesJson,
            Source = "Test fixture",
            ConfirmedByUser = true,
            CreatedAt = Now,
            UpdatedAt = Now,
            PreparedAt = Now
        };
        connection.Insert(meaning);

        var card = new LearningCardEntity
        {
            WordId = word.Id,
            MeaningId = meaning.Id,
            Direction = direction,
            State = CardState.New,
            DueAtUtc = Now,
            EaseFactor = SimpleSpacedRepetitionScheduler.DefaultEaseFactor,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        connection.Insert(card);

        return (word.Id, meaning.Id, card.Id);
    });
}
