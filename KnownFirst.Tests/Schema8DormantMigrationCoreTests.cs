using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Migrations.Schema9;
using KnownFirst.Models;

namespace KnownFirst.Tests;

/// <summary>
/// KF-MEANING-001 Slice 1 — the eight core required test scenarios for the Schema 7 -&gt; 8
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

        const int sourceDocumentId = 1;
        const string documentTitle = "Doc";
        const string documentContent = "some context text";
        const string documentFingerprint = "core1-context-document";
        var documentImportedAt = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await fixture.Connection.ExecuteAsync(
            "INSERT INTO Documents (Id, Title, TextLanguage, ExplanationLanguage, LookupMode, TargetLanguage, Content, ContentFingerprint, ImportedAt, WordCount) VALUES (?, ?, 'en', 'de', 0, '', ?, ?, ?, 3)",
            sourceDocumentId,
            documentTitle,
            documentContent,
            documentFingerprint,
            documentImportedAt);
        const int learningSessionId = 1;
        var sessionTimestamp = new DateTime(2030, 1, 2, 4, 0, 0, DateTimeKind.Utc);
        await fixture.Connection.ExecuteAsync(
            "INSERT INTO LearningSessions (Id, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc) VALUES (?, 1, 2, 0, 0, 0, 0, 0, ?, ?, ?)",
            learningSessionId,
            sessionTimestamp,
            sessionTimestamp,
            sessionTimestamp);

        var wordId = await fixture.InsertWordAsync("network");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "network", translation: "Netzwerk");
        var meaningToTermCardId = await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm, intervalDays: 3);
        var termToMeaningCardId = await fixture.InsertCardAsync(wordId, meaningId, CardDirection.TermToMeaning, intervalDays: 5);

        // Every pre-existing ContextSnapshot field carries a distinct deterministic value, and the target
        // range addresses the real "context" slice of the source document, so a silently rewritten column
        // cannot hide behind a default.
        const string contextFingerprint = "core1-context-fingerprint";
        const int contextTargetStart = 5;
        const int contextTargetLength = 7;
        var contextCreatedAt = new DateTime(2030, 1, 2, 5, 6, 7, DateTimeKind.Utc);
        var contextId = await fixture.InsertContextAsync(
            meaningId,
            wordId,
            sourceDocumentId: sourceDocumentId,
            sourceDocumentTitle: documentTitle,
            text: documentContent,
            targetStart: contextTargetStart,
            targetLength: contextTargetLength,
            normalizedFingerprint: contextFingerprint,
            createdAtUtc: contextCreatedAt);
        var review1Id = await fixture.InsertReviewAsync(meaningToTermCardId);
        var review2Id = await fixture.InsertReviewAsync(termToMeaningCardId);
        var queue1Id = await fixture.InsertQueueItemAsync(sessionId: 1, cardId: meaningToTermCardId, queueOrder: 1);
        var queue2Id = await fixture.InsertQueueItemAsync(sessionId: 1, cardId: termToMeaningCardId, queueOrder: 2);

        var result = await Schema8DormantMigration.ApplyAsync(fixture.Connection);
        await fixture.ReopenAsync();

        Assert.AreEqual(Schema8MigrationOutcome.Migrated, result.Outcome);
        Assert.AreEqual(8, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));

        // Row IDs preserved verbatim.
        Assert.AreEqual(1, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM Words WHERE Id = ?", wordId));
        Assert.AreEqual(1, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM Meanings WHERE Id = ?", meaningId));
        Assert.AreEqual(1, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM LearningCards WHERE Id = ?", meaningToTermCardId));
        Assert.AreEqual(1, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM LearningCards WHERE Id = ?", termToMeaningCardId));
        Assert.AreEqual(1, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM ContextSnapshots WHERE Id = ?", contextId));
        Assert.AreEqual(1, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM Documents WHERE Id = ?", sourceDocumentId));
        Assert.AreEqual(1, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM LearningSessions WHERE Id = ?", learningSessionId));
        Assert.AreEqual(
            sourceDocumentId,
            await fixture.Connection.ExecuteScalarAsync<int>("SELECT SourceDocumentId FROM ContextSnapshots WHERE Id = ?", contextId));
        Assert.AreEqual(
            1,
            await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM ContextSnapshots x JOIN Meanings m ON m.Id = x.MeaningId JOIN Senses s ON s.Id = x.SenseId JOIN Words w ON w.Id = x.WordId WHERE x.Id = ? AND x.SourceDocumentId = ? AND m.WordId = x.WordId AND s.WordId = x.WordId AND m.SenseId = x.SenseId AND w.Id = x.WordId",
                contextId,
                sourceDocumentId));
        Assert.AreEqual(2, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessionCards WHERE SessionId = ?", learningSessionId));
        Assert.AreEqual(2, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningReviews WHERE SessionId = ?", learningSessionId));

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

        // Every pre-existing ContextSnapshot field survives the migration and a reopen byte for byte.
        var snapshots = await fixture.Connection.QueryAsync<Core1ContextSnapshotRow>(
            "SELECT Id, MeaningId, WordId, SourceDocumentId, SourceDocumentTitle, Text, TargetStart, TargetLength, NormalizedFingerprint, CreatedAtUtc, SenseId FROM ContextSnapshots ORDER BY Id");
        Assert.AreEqual(1, snapshots.Count);
        var snapshot = snapshots[0];
        Assert.AreEqual(contextId, snapshot.Id);
        Assert.AreEqual(meaningId, snapshot.MeaningId);
        Assert.AreEqual(wordId, snapshot.WordId);
        Assert.AreEqual(sourceDocumentId, snapshot.SourceDocumentId);
        Assert.AreEqual(documentTitle, snapshot.SourceDocumentTitle);
        Assert.AreEqual(documentContent, snapshot.Text);
        Assert.AreEqual(contextTargetStart, snapshot.TargetStart);
        Assert.AreEqual(contextTargetLength, snapshot.TargetLength);
        Assert.AreEqual(contextFingerprint, snapshot.NormalizedFingerprint);
        Assert.AreEqual(contextCreatedAt, snapshot.CreatedAtUtc);
        Assert.AreEqual(meaningSenseId, snapshot.SenseId);

        // Source Document identity and metadata.
        var documents = await fixture.Connection.QueryAsync<Core1DocumentRow>(
            "SELECT Id, Title, TextLanguage, ExplanationLanguage, LookupMode, TargetLanguage, Content, ContentFingerprint, ImportedAt, WordCount FROM Documents ORDER BY Id");
        Assert.AreEqual(1, documents.Count);
        var sourceDocument = documents[0];
        Assert.AreEqual(sourceDocumentId, sourceDocument.Id);
        Assert.AreEqual(documentTitle, sourceDocument.Title);
        Assert.AreEqual("en", sourceDocument.TextLanguage);
        Assert.AreEqual("de", sourceDocument.ExplanationLanguage);
        Assert.AreEqual(0, sourceDocument.LookupMode);
        Assert.AreEqual(string.Empty, sourceDocument.TargetLanguage);
        Assert.AreEqual(documentContent, sourceDocument.Content);
        Assert.AreEqual(documentFingerprint, sourceDocument.ContentFingerprint);
        Assert.AreEqual(documentImportedAt, sourceDocument.ImportedAt);
        Assert.AreEqual(3, sourceDocument.WordCount);

        // ContextSnapshot -> Document ownership, and the snapshot's target range still addresses the exact
        // slice of the source document it was created from.
        Assert.AreEqual(sourceDocument.Id, snapshot.SourceDocumentId);
        Assert.AreEqual(sourceDocument.Title, snapshot.SourceDocumentTitle);
        Assert.IsTrue(snapshot.TargetStart >= 0);
        Assert.IsTrue(snapshot.TargetStart + snapshot.TargetLength <= sourceDocument.Content.Length);
        Assert.AreEqual("context", sourceDocument.Content.Substring(snapshot.TargetStart, snapshot.TargetLength));

        // ContextSnapshot -> Meaning and Meaning -> Sense -> Word ownership.
        Assert.AreEqual(
            1,
            await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM ContextSnapshots x JOIN Meanings m ON m.Id = x.MeaningId JOIN Senses s ON s.Id = m.SenseId JOIN Words w ON w.Id = s.WordId JOIN Documents d ON d.Id = x.SourceDocumentId WHERE x.Id = ? AND m.WordId = x.WordId AND s.Id = x.SenseId AND s.WordId = x.WordId AND w.Id = ? AND d.Id = ?",
                contextId,
                wordId,
                sourceDocumentId));

        // LearningSession identity, ownership and timestamps.
        var sessions = await fixture.Connection.QueryAsync<Core1LearningSessionRow>(
            "SELECT Id, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc FROM LearningSessions ORDER BY Id");
        Assert.AreEqual(1, sessions.Count);
        var session = sessions[0];
        Assert.AreEqual(learningSessionId, session.Id);
        Assert.AreEqual(1, session.Status);
        Assert.AreEqual(2, session.TotalCards);
        Assert.AreEqual(0, session.CompletedCards);
        Assert.AreEqual(0, session.AgainCount);
        Assert.AreEqual(0, session.HardCount);
        Assert.AreEqual(0, session.GoodCount);
        Assert.AreEqual(0, session.EasyCount);
        Assert.AreEqual(sessionTimestamp, session.StartedAtUtc);
        Assert.AreEqual(sessionTimestamp, session.UpdatedAtUtc);
        Assert.AreEqual(sessionTimestamp, session.CompletedAtUtc);

        // Review and queue rows still belong to that session and to their original cards.
        Assert.AreEqual(learningSessionId, await fixture.Connection.ExecuteScalarAsync<int>("SELECT SessionId FROM LearningReviews WHERE Id = ?", review1Id));
        Assert.AreEqual(learningSessionId, await fixture.Connection.ExecuteScalarAsync<int>("SELECT SessionId FROM LearningReviews WHERE Id = ?", review2Id));
        Assert.AreEqual(learningSessionId, await fixture.Connection.ExecuteScalarAsync<int>("SELECT SessionId FROM LearningSessionCards WHERE Id = ?", queue1Id));
        Assert.AreEqual(learningSessionId, await fixture.Connection.ExecuteScalarAsync<int>("SELECT SessionId FROM LearningSessionCards WHERE Id = ?", queue2Id));
        Assert.AreEqual(
            4,
            await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT (SELECT COUNT(*) FROM LearningReviews r JOIN LearningSessions s ON s.Id = r.SessionId JOIN LearningCards c ON c.Id = r.CardId WHERE c.WordId = ?) + (SELECT COUNT(*) FROM LearningSessionCards q JOIN LearningSessions s ON s.Id = q.SessionId JOIN LearningCards c ON c.Id = q.CardId WHERE c.WordId = ?)",
                wordId,
                wordId));
    }

    private sealed class Core1ContextSnapshotRow
    {
        public int Id { get; set; }
        public int MeaningId { get; set; }
        public int WordId { get; set; }
        public int SourceDocumentId { get; set; }
        public string SourceDocumentTitle { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public int TargetStart { get; set; }
        public int TargetLength { get; set; }
        public string NormalizedFingerprint { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public int? SenseId { get; set; }
    }

    private sealed class Core1DocumentRow
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string TextLanguage { get; set; } = string.Empty;
        public string ExplanationLanguage { get; set; } = string.Empty;
        public int LookupMode { get; set; }
        public string TargetLanguage { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string ContentFingerprint { get; set; } = string.Empty;
        public DateTime ImportedAt { get; set; }
        public int WordCount { get; set; }
    }

    private sealed class Core1LearningSessionRow
    {
        public int Id { get; set; }
        public int Status { get; set; }
        public int TotalCards { get; set; }
        public int CompletedCards { get; set; }
        public int AgainCount { get; set; }
        public int HardCount { get; set; }
        public int GoodCount { get; set; }
        public int EasyCount { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
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

    /// <summary>
    /// Ordinary current-schema initialization runs the Schema-8 semantic migration as an internal step,
    /// then continues on to <see cref="DatabaseSchema.CurrentVersion"/> (Schema 9) — never stopping at 8.
    /// </summary>
    [TestMethod]
    public async Task Core8_OrdinaryInitializeAsync_ActivatesCurrentSchemaAfterSchema8Step()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await fixture.InsertWordAsync("dormant", status: WordStatus.Unreviewed);

        await DatabaseSchema.InitializeAsync(fixture.Connection);

        var versionAfter = await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection);

        Assert.AreEqual(DatabaseSchema.CurrentVersion, versionAfter);
        Assert.IsTrue(await Schema8MigrationAssertHelpers.TableExistsAsync(fixture.Connection, "Senses"));
        Assert.IsFalse(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "MeaningId"));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "PreferredMeaningId"));

        var validShape = false;
        string? shapeFailureDetail = null;
        await fixture.Connection.RunInTransactionAsync(connection =>
            validShape = Schema9ShapeValidator.IsValidDatabase(connection, out shapeFailureDetail));
        Assert.IsTrue(validShape, shapeFailureDetail);
    }

    [TestMethod]
    public Task PreparedWordWithoutMeaning_RollsBackPersistentState() =>
        AssertLegacyLearningStatusWithoutMeaningRollsBackAsync(WordStatus.Prepared);

    [TestMethod]
    public Task LearningWordWithoutMeaning_RollsBackPersistentState() =>
        AssertLegacyLearningStatusWithoutMeaningRollsBackAsync(WordStatus.Learning);

    [TestMethod]
    public Task MasteredWordWithoutMeaning_RollsBackPersistentState() =>
        AssertLegacyLearningStatusWithoutMeaningRollsBackAsync(WordStatus.Mastered);

    private static async Task AssertLegacyLearningStatusWithoutMeaningRollsBackAsync(WordStatus status)
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var createdAt = new DateTime(2032, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var wordId = await fixture.InsertWordAsync(
            $"missing-{status}",
            status: status,
            totalOccurrenceCount: 19,
            documentCount: 4,
            consecutiveRecallSuccessCount: 3,
            consecutiveTypingSuccessCount: 2,
            consecutiveTypingFailureCount: 1,
            masteryReviewExtensionScheduled: true,
            createdAt: createdAt,
            updatedAt: createdAt.AddMinutes(1));
        var before = await fixture.CapturePersistentStateAsync();

        var exception = await Assert.ThrowsExactlyAsync<Schema8MigrationException>(
            () => Schema8DormantMigration.ApplyAsync(fixture.Connection));
        Assert.AreEqual("schema8-migration-referential-corruption", exception.ErrorCode);

        await fixture.ReopenAsync();
        var after = await fixture.CapturePersistentStateAsync();
        CollectionAssert.AreEqual(before, after);
        Assert.AreEqual(7, await fixture.ReadUserVersionAsync());
        Assert.AreEqual((int)status, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT Status FROM Words WHERE Id = ?", wordId));
        Assert.AreEqual(19, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT TotalOccurrenceCount FROM Words WHERE Id = ?", wordId));
        Assert.AreEqual(0, await fixture.GetTableCountAsync("Senses"));
    }
}
