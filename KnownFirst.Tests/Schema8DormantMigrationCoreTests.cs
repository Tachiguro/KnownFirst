using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;

namespace KnownFirst.Tests;

/// <summary>
/// KF-MEANING-001 Slice 1 — the eight core required test scenarios for the dormant Schema 7 -&gt; 8
/// migration engine (<see cref="Schema8DormantMigration"/>). Every fixture is a synthetic, isolated,
/// temporary SQLite file built directly in test code via <see cref="Schema7Fixture"/> — never a real user
/// database, and the migration is invoked only via its explicit entry point, never through
/// <see cref="DatabaseSchema.InitializeAsync"/>.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class Schema8DormantMigrationCoreTests
{
    [TestMethod]
    public async Task Core1_PopulatedSchema7_MigratesAndPreservesRowIdsAndRelationships()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("network");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "network", translation: "Netzwerk");
        var meaningToTermCardId = await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm, intervalDays: 3);
        var termToMeaningCardId = await fixture.InsertCardAsync(wordId, meaningId, CardDirection.TermToMeaning, intervalDays: 5);
        var contextId = await fixture.InsertContextAsync(meaningId, wordId);
        var review1Id = await fixture.InsertReviewAsync(meaningToTermCardId);
        var review2Id = await fixture.InsertReviewAsync(termToMeaningCardId);
        var queue1Id = await fixture.InsertQueueItemAsync(sessionId: 1, cardId: meaningToTermCardId, queueOrder: 1);
        var queue2Id = await fixture.InsertQueueItemAsync(sessionId: 1, cardId: termToMeaningCardId, queueOrder: 2);

        var result = await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        Assert.AreEqual(Schema8MigrationOutcome.Migrated, result.Outcome);
        Assert.AreEqual(8, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));

        // Row IDs preserved verbatim.
        Assert.AreEqual(1, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM Words WHERE Id = ?", wordId));
        Assert.AreEqual(1, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM Meanings WHERE Id = ?", meaningId));
        Assert.AreEqual(1, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM LearningCards WHERE Id = ?", meaningToTermCardId));
        Assert.AreEqual(1, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM LearningCards WHERE Id = ?", termToMeaningCardId));
        Assert.AreEqual(1, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM ContextSnapshots WHERE Id = ?", contextId));

        // Relationships preserved: review.CardId and queue.CardId untouched.
        Assert.AreEqual(meaningToTermCardId, await fixture.Connection.ExecuteScalarAsync<int>("SELECT CardId FROM LearningReviews WHERE Id = ?", review1Id));
        Assert.AreEqual(termToMeaningCardId, await fixture.Connection.ExecuteScalarAsync<int>("SELECT CardId FROM LearningReviews WHERE Id = ?", review2Id));
        Assert.AreEqual(meaningToTermCardId, await fixture.Connection.ExecuteScalarAsync<int>("SELECT CardId FROM LearningSessionCards WHERE Id = ?", queue1Id));
        Assert.AreEqual(termToMeaningCardId, await fixture.Connection.ExecuteScalarAsync<int>("SELECT CardId FROM LearningSessionCards WHERE Id = ?", queue2Id));

        // Every migrated card resolves to a Sense of the same Word, and PreferredMeaningId == legacy MeaningId.
        var card1 = await fixture.Connection.QueryAsync<MigratedCardRow>("SELECT Id, WordId, SenseId, PreferredMeaningId, Direction FROM LearningCards WHERE Id = ?", meaningToTermCardId);
        Assert.AreEqual(meaningId, card1[0].PreferredMeaningId);
        Assert.IsNotNull(card1[0].SenseId);

        var senseWordId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT WordId FROM Senses WHERE Id = ?", card1[0].SenseId!.Value);
        Assert.AreEqual(wordId, senseWordId);

        var meaningSenseId = await fixture.Connection.ExecuteScalarAsync<int?>("SELECT SenseId FROM Meanings WHERE Id = ?", meaningId);
        Assert.AreEqual(card1[0].SenseId, meaningSenseId);
    }

    [TestMethod]
    public async Task Core2_AmbiguousMeanings_SplitIntoSeparateSenses()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("bank");
        // Neither meaning carries any reliable sense discriminator (no ProviderMeaningId, no
        // GrammaticalRelationship, no AcronymExpansion) — differing Definition/Translation wording alone
        // must never merge them (architecture doc §1 "HasReliableSenseDiscriminator").
        var financialMeaningId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "bank", translation: "Bank", definition: "financial institution");
        var riverMeaningId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "bank", translation: "Ufer", definition: "edge of a river");

        var result = await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        Assert.AreEqual(Schema8MigrationOutcome.Migrated, result.Outcome);

        var senseCount = await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM Senses WHERE WordId = ?", wordId);
        Assert.AreEqual(2, senseCount);

        var financialSenseId = await fixture.Connection.ExecuteScalarAsync<int?>("SELECT SenseId FROM Meanings WHERE Id = ?", financialMeaningId);
        var riverSenseId = await fixture.Connection.ExecuteScalarAsync<int?>("SELECT SenseId FROM Meanings WHERE Id = ?", riverMeaningId);
        Assert.IsNotNull(financialSenseId);
        Assert.IsNotNull(riverSenseId);
        Assert.AreNotEqual(financialSenseId, riverSenseId);
    }

    [TestMethod]
    public async Task Core3_ReliablyIdenticalMeanings_GroupIntoOneSense()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("run");
        // Both meanings share the same provider sense id (a reliable discriminator) and the same
        // languages, so SemanticMeaningIdentityPolicy computes an equal identity for both — strong
        // evidence of the same Sense, per the architecture doc's grouping rule.
        var meaning1Id = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "run", translation: "laufen", selectedMeaningId: "verb-move-fast-1",
            definition: "to move fast on foot");
        var meaning2Id = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "run", translation: "rennen", selectedMeaningId: "verb-move-fast-1",
            definition: "to sprint");

        var result = await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        Assert.AreEqual(Schema8MigrationOutcome.Migrated, result.Outcome);

        var senseCount = await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM Senses WHERE WordId = ?", wordId);
        Assert.AreEqual(1, senseCount);

        var sense1 = await fixture.Connection.ExecuteScalarAsync<int?>("SELECT SenseId FROM Meanings WHERE Id = ?", meaning1Id);
        var sense2 = await fixture.Connection.ExecuteScalarAsync<int?>("SELECT SenseId FROM Meanings WHERE Id = ?", meaning2Id);
        Assert.IsNotNull(sense1);
        Assert.AreEqual(sense1, sense2);
    }

    [TestMethod]
    public async Task Core4_ReferentialCorruption_FailsClosedAndLeavesSchema7Unchanged()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("broken");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "broken", translation: "kaputt");
        // Dangling reference: the card's MeaningId points to a Meaning row that does not exist.
        var danglingCardId = await fixture.InsertCardAsync(wordId, meaningId + 1000, CardDirection.MeaningToTerm);

        var beforeTableNames = await Schema8MigrationAssertHelpers.GetTableNamesAsync(fixture.Connection);
        var beforeCardCount = await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM LearningCards");

        var exception = await Assert.ThrowsExactlyAsync<Schema8MigrationException>(
            () => Schema8DormantMigration.ApplyAsync(fixture.Connection));

        Assert.AreEqual("schema8-migration-referential-corruption", exception.ErrorCode);

        Assert.AreEqual(7, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));
        Assert.IsFalse(await Schema8MigrationAssertHelpers.TableExistsAsync(fixture.Connection, "Senses"));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "MeaningId"));
        Assert.IsFalse(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "PreferredMeaningId"));

        var afterTableNames = await Schema8MigrationAssertHelpers.GetTableNamesAsync(fixture.Connection);
        CollectionAssert.AreEqual(beforeTableNames, afterTableNames);

        var afterCardCount = await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM LearningCards");
        Assert.AreEqual(beforeCardCount, afterCardCount);
        Assert.AreEqual(
            wordId,
            await fixture.Connection.ExecuteScalarAsync<int>("SELECT WordId FROM LearningCards WHERE Id = ?", danglingCardId));
    }

    [TestMethod]
    public async Task Core5_SecondInvocation_IsIdempotentAndCreatesNoDuplicateRows()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("idempotent");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "idempotent", translation: "idempotent");
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);

        var firstResult = await Schema8DormantMigration.ApplyAsync(fixture.Connection);
        Assert.AreEqual(Schema8MigrationOutcome.Migrated, firstResult.Outcome);

        var sensesAfterFirst = await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM Senses");
        var variantsAfterFirst = await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM AnswerVariants");
        var assignmentsAfterFirst = await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM SenseAnswerVariantAssignments");
        var cardsAfterFirst = await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM LearningCards");

        var secondResult = await Schema8DormantMigration.ApplyAsync(fixture.Connection);
        Assert.AreEqual(Schema8MigrationOutcome.AlreadyApplied, secondResult.Outcome);

        Assert.AreEqual(sensesAfterFirst, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM Senses"));
        Assert.AreEqual(variantsAfterFirst, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM AnswerVariants"));
        Assert.AreEqual(assignmentsAfterFirst, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM SenseAnswerVariantAssignments"));
        Assert.AreEqual(cardsAfterFirst, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM LearningCards"));
    }

    [TestMethod]
    public async Task Core6_FutureSchemaVersions_AreRejectedSafely()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await fixture.Connection.ExecuteAsync("PRAGMA user_version = 9");

        var exception = await Assert.ThrowsExactlyAsync<Schema8MigrationException>(
            () => Schema8DormantMigration.ApplyAsync(fixture.Connection));

        Assert.AreEqual("schema8-migration-future-version", exception.ErrorCode);
        Assert.AreEqual(9, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));
        Assert.IsFalse(await Schema8MigrationAssertHelpers.TableExistsAsync(fixture.Connection, "Senses"));
    }

    [TestMethod]
    public async Task Core7_OldAndNewCardIndexesAndPreferredMeaningIdTransition_AreCorrect()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("index-test");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "index-test", translation: "Index-Test");
        var cardId = await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);

        await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        Assert.IsFalse(await Schema8MigrationAssertHelpers.IndexExistsAsync(fixture.Connection, "IX_LearningCards_Word_Direction"));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.IndexExistsAsync(fixture.Connection, "IX_LearningCards_Sense_Direction"));
        Assert.IsFalse(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "MeaningId"));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "PreferredMeaningId"));

        var preferredMeaningId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT PreferredMeaningId FROM LearningCards WHERE Id = ?", cardId);
        Assert.AreEqual(meaningId, preferredMeaningId);
    }

    [TestMethod]
    public async Task Core8_OrdinaryInitializeAsync_ProducesNoSchema8Change()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await fixture.InsertWordAsync("dormant");

        var tablesBefore = await Schema8MigrationAssertHelpers.GetTableNamesAsync(fixture.Connection);
        var indexesBefore = await Schema8MigrationAssertHelpers.GetIndexNamesAsync(fixture.Connection);
        var versionBefore = await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection);

        // Ordinary re-initialization (what every normal app startup does) — the migration is never invoked.
        await DatabaseSchema.InitializeAsync(fixture.Connection);

        var tablesAfter = await Schema8MigrationAssertHelpers.GetTableNamesAsync(fixture.Connection);
        var indexesAfter = await Schema8MigrationAssertHelpers.GetIndexNamesAsync(fixture.Connection);
        var versionAfter = await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection);

        CollectionAssert.AreEqual(tablesBefore, tablesAfter);
        CollectionAssert.AreEqual(indexesBefore, indexesAfter);
        Assert.AreEqual(versionBefore, versionAfter);
        Assert.AreEqual(7, versionAfter);
        Assert.IsTrue(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "MeaningId"));
        Assert.IsFalse(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "PreferredMeaningId"));
    }
}
