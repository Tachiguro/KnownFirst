using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Migrations.Schema9;
using KnownFirst.Models;
using KnownFirst.Services.Study;

namespace KnownFirst.Tests;

/// <summary>KF-MEANING-001 Slice 1 — focused tests beyond the eight core scenarios.</summary>
[TestClass]
[DoNotParallelize]
public sealed class Schema8DormantMigrationFocusedTests
{
    [TestMethod]
    public async Task EmptyDatabase_MigratesSuccessfully()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var result = await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        Assert.AreEqual(Schema8MigrationOutcome.Migrated, result.Outcome);
        Assert.AreEqual(8, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));
        Assert.AreEqual(0, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM Senses"));
        Assert.AreEqual(0, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM AnswerVariants"));
    }

    [TestMethod]
    public async Task MultipleMeaningsForOneWord_MixedGroupingIsCorrect()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("light");
        var m1 = await fixture.InsertMeaningAsync(wordId, displayTerm: "light", translation: "Licht", selectedMeaningId: "sense-illumination");
        var m2 = await fixture.InsertMeaningAsync(wordId, displayTerm: "light", translation: "hell", selectedMeaningId: "sense-illumination");
        var m3 = await fixture.InsertMeaningAsync(wordId, displayTerm: "light", translation: "leicht");

        await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        Assert.AreEqual(2, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM Senses WHERE WordId = ?", wordId));

        var sense1 = await fixture.Connection.ExecuteScalarAsync<int?>("SELECT SenseId FROM Meanings WHERE Id = ?", m1);
        var sense2 = await fixture.Connection.ExecuteScalarAsync<int?>("SELECT SenseId FROM Meanings WHERE Id = ?", m2);
        var sense3 = await fixture.Connection.ExecuteScalarAsync<int?>("SELECT SenseId FROM Meanings WHERE Id = ?", m3);

        Assert.AreEqual(sense1, sense2);
        Assert.AreNotEqual(sense1, sense3);
    }

    [TestMethod]
    public async Task GroupedSense_EveryMeaningsDisplayTermAndTranslation_BecomeUsableAnswerVariants()
    {
        // Regression for the confirmed losslessness defect: when multiple Meaning rows group into one
        // Sense, every distinct answer expression (not just the representative Meaning's) must become a
        // real AnswerVariant with an AcceptedOnly assignment, never remain inert Meaning-row text.
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("bank");
        var meaningAId = await fixture.InsertMeaningAsync(
            wordId, sourceLanguage: "en", explanationLanguage: "de", displayTerm: "bank", translation: "Bank",
            selectedMeaningId: "fin-institution");
        var meaningBId = await fixture.InsertMeaningAsync(
            wordId, sourceLanguage: "en", explanationLanguage: "de", displayTerm: "bank", translation: "Kreditinstitut",
            selectedMeaningId: "fin-institution");
        await fixture.InsertCardAsync(wordId, meaningAId, CardDirection.MeaningToTerm);
        await fixture.InsertCardAsync(wordId, meaningAId, CardDirection.TermToMeaning);

        await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        // Grouping itself is unaffected: both Meanings share the same reliable discriminator.
        var senseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", meaningAId);
        Assert.AreEqual(senseId, await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", meaningBId));
        Assert.AreEqual(1, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM Senses WHERE WordId = ?", wordId));

        // "bank" (DisplayTerm) dedupes to one term-side variant even though both Meanings share it.
        var termVariantCount = await Schema8MigrationAssertHelpers.CountAsync(
            fixture.Connection, "SELECT COUNT(*) FROM AnswerVariants WHERE SenseId = ? AND NormalizedText = 'bank'", senseId);
        Assert.AreEqual(1, termVariantCount);

        // Both distinct Translation values ("Bank" and "Kreditinstitut") exist as real AnswerVariants.
        Assert.AreEqual(
            1, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM AnswerVariants WHERE SenseId = ? AND NormalizedText = 'Bank'", senseId));
        Assert.AreEqual(
            1, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM AnswerVariants WHERE SenseId = ? AND NormalizedText = 'Kreditinstitut'", senseId));
        Assert.AreEqual(3, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM AnswerVariants WHERE SenseId = ?", senseId));

        // Both Translation-sourced variants are assigned (usable) for TermToMeaning. Exactly one is preferred
        // (Focused invariant 2) — the representative Meaning's own "Bank", since meaningA is the group's
        // lowest-Id (representative) Meaning. KF-MEANING-001 Slice 4: that single primary assignment is now
        // Required with a boundary; "Kreditinstitut" (meaningB) stays AcceptedOnly with a null boundary.
        var termToMeaningAssignments = await fixture.Connection.QueryAsync<AssignmentTextRow>(
            """
            SELECT a.Requirement, a.IsPreferred, a.RequiredSinceUtc, v.NormalizedText
            FROM SenseAnswerVariantAssignments a JOIN AnswerVariants v ON v.Id = a.AnswerVariantId
            WHERE a.SenseId = ? AND a.CardDirection = ?
            """,
            senseId, (int)CardDirection.TermToMeaning);
        Assert.HasCount(2, termToMeaningAssignments);
        Assert.HasCount(1, termToMeaningAssignments.Where(a => a.IsPreferred).ToList());

        var preferredTermToMeaning = termToMeaningAssignments.Single(a => a.IsPreferred);
        Assert.AreEqual("Bank", preferredTermToMeaning.NormalizedText);
        Assert.AreEqual((int)AnswerVariantRequirement.Required, preferredTermToMeaning.Requirement);
        Assert.IsNotNull(preferredTermToMeaning.RequiredSinceUtc);

        var acceptedTermToMeaning = termToMeaningAssignments.Single(a => !a.IsPreferred);
        Assert.AreEqual("Kreditinstitut", acceptedTermToMeaning.NormalizedText);
        Assert.AreEqual((int)AnswerVariantRequirement.AcceptedOnly, acceptedTermToMeaning.Requirement);
        Assert.IsNull(acceptedTermToMeaning.RequiredSinceUtc);

        // The single deduped term-side variant still has exactly one MeaningToTerm assignment, and it is
        // still the preferred one — the representative Meaning's existing preferred assignment is untouched.
        var meaningToTermAssignments = await fixture.Connection.QueryAsync<AssignmentFlagsRow>(
            "SELECT Requirement, IsPreferred FROM SenseAnswerVariantAssignments WHERE SenseId = ? AND CardDirection = ?",
            senseId, (int)CardDirection.MeaningToTerm);
        Assert.HasCount(1, meaningToTermAssignments);
        Assert.IsTrue(meaningToTermAssignments[0].IsPreferred);

        // Exactly one Required assignment per existing card direction (Slice 4) — never more, never invented
        // for a direction without a card — and never more than one preferred assignment per direction.
        Assert.AreEqual(
            2, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE SenseId = ? AND Requirement = ?", senseId, (int)AnswerVariantRequirement.Required));
        Assert.AreEqual(
            2, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE SenseId = ? AND IsPreferred = 1", senseId));
    }

    private sealed class AssignmentTextRow
    {
        public int Requirement { get; set; }
        public bool IsPreferred { get; set; }
        public DateTime? RequiredSinceUtc { get; set; }
        public string NormalizedText { get; set; } = string.Empty;
    }

    private sealed class AssignmentFlagsRow
    {
        public int Requirement { get; set; }
        public bool IsPreferred { get; set; }
    }

    [TestMethod]
    public async Task MultipleWordsAndBothCardDirections_AllMigrateIndependently()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var word1Id = await fixture.InsertWordAsync("alpha");
        var meaning1Id = await fixture.InsertMeaningAsync(word1Id, displayTerm: "alpha", translation: "Alpha");
        var card1MtT = await fixture.InsertCardAsync(word1Id, meaning1Id, CardDirection.MeaningToTerm);
        var card1TtM = await fixture.InsertCardAsync(word1Id, meaning1Id, CardDirection.TermToMeaning);

        var word2Id = await fixture.InsertWordAsync("beta");
        var meaning2Id = await fixture.InsertMeaningAsync(word2Id, displayTerm: "beta", translation: "Beta");
        var card2MtT = await fixture.InsertCardAsync(word2Id, meaning2Id, CardDirection.MeaningToTerm);

        var result = await Schema8DormantMigration.ApplyAsync(fixture.Connection);
        Assert.AreEqual(Schema8MigrationOutcome.Migrated, result.Outcome);

        var sense1 = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM LearningCards WHERE Id = ?", card1MtT);
        var sense1B = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM LearningCards WHERE Id = ?", card1TtM);
        var sense2 = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM LearningCards WHERE Id = ?", card2MtT);

        Assert.AreEqual(sense1, sense1B);
        Assert.AreNotEqual(sense1, sense2);

        // word1's Sense has both directions assigned; word2's Sense has only MeaningToTerm.
        var word1Assignments = await Schema8MigrationAssertHelpers.CountAsync(
            fixture.Connection, "SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE SenseId = ?", sense1);
        var word2Assignments = await Schema8MigrationAssertHelpers.CountAsync(
            fixture.Connection, "SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE SenseId = ?", sense2);
        Assert.AreEqual(2, word1Assignments); // term-side (MeaningToTerm) + explanation-side (TermToMeaning)
        Assert.AreEqual(1, word2Assignments); // term-side (MeaningToTerm) only
    }

    [TestMethod]
    public async Task StatusAndPreparationStateCompatibility_TranslatesAndResetsCorrectly()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var learningWordId = await fixture.InsertWordAsync("learning-word", status: WordStatus.Learning, preparationState: KnownFirst.Core.Preparation.PreparationState.Prepared);
        var learningMeaningId = await fixture.InsertMeaningAsync(learningWordId, displayTerm: "learning-word", translation: "x");

        var masteredWordId = await fixture.InsertWordAsync("mastered-word", status: WordStatus.Mastered);
        var masteredMeaningId = await fixture.InsertMeaningAsync(masteredWordId, displayTerm: "mastered-word", translation: "y");

        // Edge case: a Word with a confirmed Meaning but a Status that was never Prepared/Learning/Mastered.
        var backlogWordId = await fixture.InsertWordAsync("backlog-word", status: WordStatus.UnknownBacklog);
        var backlogMeaningId = await fixture.InsertMeaningAsync(backlogWordId, displayTerm: "backlog-word", translation: "z");

        await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        var learningSenseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", learningMeaningId);
        var learningSenseStatus = await fixture.Connection.ExecuteScalarAsync<int>("SELECT Status FROM Senses WHERE Id = ?", learningSenseId);
        Assert.AreEqual((int)SenseStatus.Learning, learningSenseStatus);
        Assert.AreEqual((int)WordStatus.UnknownBacklog, await fixture.Connection.ExecuteScalarAsync<int>("SELECT Status FROM Words WHERE Id = ?", learningWordId));
        Assert.AreEqual((int)KnownFirst.Core.Preparation.PreparationState.Prepared, await fixture.Connection.ExecuteScalarAsync<int>("SELECT PreparationState FROM Words WHERE Id = ?", learningWordId));

        var masteredSenseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", masteredMeaningId);
        var masteredSenseStatus = await fixture.Connection.ExecuteScalarAsync<int>("SELECT Status FROM Senses WHERE Id = ?", masteredSenseId);
        Assert.AreEqual((int)SenseStatus.Mastered, masteredSenseStatus);
        Assert.AreEqual((int)WordStatus.UnknownBacklog, await fixture.Connection.ExecuteScalarAsync<int>("SELECT Status FROM Words WHERE Id = ?", masteredWordId));

        var backlogSenseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", backlogMeaningId);
        var backlogSenseStatus = await fixture.Connection.ExecuteScalarAsync<int>("SELECT Status FROM Senses WHERE Id = ?", backlogSenseId);
        Assert.AreEqual((int)SenseStatus.Prepared, backlogSenseStatus); // safe fallback default
        Assert.AreEqual((int)WordStatus.UnknownBacklog, await fixture.Connection.ExecuteScalarAsync<int>("SELECT Status FROM Words WHERE Id = ?", backlogWordId)); // unchanged, never touched
    }

    [TestMethod]
    public async Task AliasDeduplicationAndLanguage_IsCorrect()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("color");
        var meaningId = await fixture.InsertMeaningAsync(
            wordId, sourceLanguage: "en", explanationLanguage: "de", displayTerm: "color", translation: "Farbe",
            acceptedAliasesJson: """["colour","color"]""");
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);

        await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", meaningId);

        // "color" (the alias) normalizes to the same text as the term-side variant already created from
        // DisplayTerm — it must dedupe onto that same AnswerVariant, not create a second row.
        var variantCount = await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM AnswerVariants WHERE SenseId = ?", senseId);
        Assert.AreEqual(2, variantCount); // "color" (term/alias, deduped) + "colour" (alias)

        var aliasLanguages = await fixture.Connection.QueryAsync<AnswerLanguageRow>(
            "SELECT AnswerLanguage FROM AnswerVariants WHERE SenseId = ?", senseId);
        Assert.IsTrue(aliasLanguages.All(a => a.AnswerLanguage == "en")); // always term-side (SourceLanguage)

        var assignmentCount = await Schema8MigrationAssertHelpers.CountAsync(
            fixture.Connection, "SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE SenseId = ? AND CardDirection = ?", senseId, (int)CardDirection.MeaningToTerm);
        Assert.AreEqual(2, assignmentCount); // preferred "color" + accepted-only "colour"

        // KF-MEANING-001 Slice 4: exactly one Required assignment — the deterministic primary of the single
        // existing MeaningToTerm card direction. The alias stays AcceptedOnly (Decision 12).
        var requiredCount = await Schema8MigrationAssertHelpers.CountAsync(
            fixture.Connection,
            "SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE SenseId = ? AND Requirement = ?",
            senseId, (int)AnswerVariantRequirement.Required);
        Assert.AreEqual(1, requiredCount);

        var aliasRequiredCount = await Schema8MigrationAssertHelpers.CountAsync(
            fixture.Connection,
            """
            SELECT COUNT(*) FROM SenseAnswerVariantAssignments a
            JOIN AnswerVariants v ON v.Id = a.AnswerVariantId
            WHERE a.SenseId = ? AND v.NormalizedText = 'colour' AND a.Requirement = ?
            """,
            senseId, (int)AnswerVariantRequirement.Required);
        Assert.AreEqual(0, aliasRequiredCount);
    }

    [TestMethod]
    public async Task StableIds_AreUniqueAndNonEmptyAcrossAllSchema8Tables()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var word1Id = await fixture.InsertWordAsync("one");
        var meaning1Id = await fixture.InsertMeaningAsync(word1Id, displayTerm: "one", translation: "Eins");
        await fixture.InsertCardAsync(word1Id, meaning1Id, CardDirection.MeaningToTerm);
        await fixture.InsertCardAsync(word1Id, meaning1Id, CardDirection.TermToMeaning);

        var word2Id = await fixture.InsertWordAsync("two");
        var meaning2Id = await fixture.InsertMeaningAsync(word2Id, displayTerm: "two", translation: "Zwei");
        await fixture.InsertCardAsync(word2Id, meaning2Id, CardDirection.MeaningToTerm);

        await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        foreach (var table in new[] { "Senses", "AnswerVariants", "SenseAnswerVariantAssignments", "Meanings" })
        {
            var emptyCount = await Schema8MigrationAssertHelpers.CountAsync(
                fixture.Connection, $"SELECT COUNT(*) FROM {table} WHERE StableId IS NULL OR TRIM(StableId) = ''");
            Assert.AreEqual(0, emptyCount, $"{table} has empty StableId rows");

            var duplicateCount = await Schema8MigrationAssertHelpers.CountAsync(
                fixture.Connection,
                $"SELECT COUNT(*) FROM (SELECT StableId FROM {table} GROUP BY StableId HAVING COUNT(*) > 1)");
            Assert.AreEqual(0, duplicateCount, $"{table} has duplicate StableId rows");
        }
    }

    [TestMethod]
    public async Task AutomaticCounterMigration_CreatesExactlyOneProgressRowPerCard_NoDoubleCredit()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var activeWordId = await fixture.InsertWordAsync(
            "active", automaticInteractionMode: LearningInteractionMode.Typing,
            consecutiveRecallSuccessCount: 3, consecutiveTypingSuccessCount: 2, consecutiveTypingFailureCount: 1,
            masteryReviewExtensionScheduled: true);
        var activeMeaningId = await fixture.InsertMeaningAsync(activeWordId, displayTerm: "active", translation: "aktiv");
        var activeCardId = await fixture.InsertCardAsync(activeWordId, activeMeaningId, CardDirection.MeaningToTerm);

        var idleWordId = await fixture.InsertWordAsync("idle"); // all-default automatic counters
        var idleMeaningId = await fixture.InsertMeaningAsync(idleWordId, displayTerm: "idle", translation: "untätig");
        await fixture.InsertCardAsync(idleWordId, idleMeaningId, CardDirection.MeaningToTerm);

        await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        var totalProgressRows = await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM AnswerVariantProgress");
        Assert.AreEqual(1, totalProgressRows); // idle word gets none; active word gets exactly one (one card)

        var progress = (await fixture.Connection.QueryAsync<AnswerVariantProgressRow>(
            "SELECT * FROM AnswerVariantProgress WHERE CardId = ?", activeCardId)).Single();
        Assert.AreEqual(3, progress.ConsecutiveReadingSuccessCount);
        Assert.AreEqual(2, progress.ConsecutiveTypingSuccessCount);
        Assert.AreEqual(1, progress.ConsecutiveTypingFailureCount);
        Assert.IsTrue(progress.MasteryReviewExtensionScheduled);
        Assert.AreEqual(LearningInteractionMode.Typing, progress.InteractionMode);
        Assert.AreEqual(1, progress.ReplayVersion);
        Assert.IsFalse(progress.IsMastered);
    }

    private sealed class AnswerLanguageRow
    {
        public string AnswerLanguage { get; set; } = string.Empty;
    }

    // ---- Missing-reference (referential corruption) coverage ----

    [TestMethod]
    public async Task MissingMeaningReference_CardPointsToNonexistentMeaning_FailsClosed()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync("w");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "w", translation: "x");
        await fixture.InsertCardAsync(wordId, meaningId + 999, CardDirection.MeaningToTerm);

        await AssertFailsClosedAsync(fixture);
    }

    [TestMethod]
    public async Task MissingWordReference_MeaningPointsToNonexistentWord_FailsClosed()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync("w");
        await fixture.InsertMeaningAsync(wordId + 999, displayTerm: "w", translation: "x");

        await AssertFailsClosedAsync(fixture);
    }

    [TestMethod]
    public async Task MissingCardReference_ReviewPointsToNonexistentCard_FailsClosed()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync("w");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "w", translation: "x");
        var cardId = await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);
        await fixture.InsertReviewAsync(cardId + 999);

        await AssertFailsClosedAsync(fixture);
    }

    [TestMethod]
    public async Task MissingCardReference_QueueItemPointsToNonexistentCard_FailsClosed()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync("w");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "w", translation: "x");
        var cardId = await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);
        await fixture.InsertQueueItemAsync(sessionId: 1, cardId: cardId + 999, queueOrder: 1);

        await AssertFailsClosedAsync(fixture);
    }

    [TestMethod]
    public async Task MissingContextReference_PointsToNonexistentMeaning_FailsClosed()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync("w");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "w", translation: "x");
        await fixture.InsertContextAsync(meaningId + 999, wordId);

        await AssertFailsClosedAsync(fixture);
    }

    /// <summary>
    /// Explicit historical advancement against a populated database: the Schema-8 semantic upgrade
    /// (Senses/AnswerVariants/assignments/progress) happens correctly, then advances to Schema 9.
    /// </summary>
    [TestMethod]
    public async Task HistoricalAdvancement_PopulatedDatabase_ReportsSchema9AndValidShape()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync("populated");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "populated", translation: "x");
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);

        await HistoricalMigrationFixture.UpgradeToSchema9Async(fixture.Connection);

        Assert.AreEqual(9, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.TableExistsAsync(fixture.Connection, "Senses"));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.TableExistsAsync(fixture.Connection, "AnswerVariants"));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.TableExistsAsync(fixture.Connection, "SenseAnswerVariantAssignments"));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.TableExistsAsync(fixture.Connection, "AnswerVariantProgress"));
        Assert.IsFalse(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "MeaningId"));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "SenseId"));

        var validShape = false;
        string? shapeFailureDetail = null;
        await fixture.Connection.RunInTransactionAsync(connection =>
            validShape = Schema9ShapeValidator.IsValidDatabase(connection, out shapeFailureDetail));
        Assert.IsTrue(validShape, shapeFailureDetail);
    }

    // ---- Focused invariant 1: deterministic SourceMeaningId ----

    [TestMethod]
    public async Task DeterministicSourceMeaningId_RepresentativeWinsWhenBothContributeSameExpression()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("shared-term");
        var representativeId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "shared-term", translation: "", selectedMeaningId: "sense-x");
        var otherId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "shared-term", translation: "", selectedMeaningId: "sense-x");
        await fixture.InsertCardAsync(wordId, representativeId, CardDirection.MeaningToTerm);

        await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", representativeId);
        var variants = await fixture.Connection.QueryAsync<AnswerVariantProvenanceRow>(
            "SELECT SourceMeaningId FROM AnswerVariants WHERE SenseId = ? AND NormalizedText = 'shared-term'", senseId);

        Assert.HasCount(1, variants); // exactly one AnswerVariant row after deduplication
        Assert.AreEqual(representativeId, variants[0].SourceMeaningId); // representative (lowest Id) wins
        Assert.IsTrue(representativeId < otherId);
    }

    [TestMethod]
    public async Task DeterministicSourceMeaningId_LowestIdWinsWhenRepresentativeDidNotContribute()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("fallback-alias");
        var representativeId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "fallback-alias", translation: "", selectedMeaningId: "sense-y",
            acceptedAliasesJson: "[]"); // representative never contributes "synonym-x"
        var lowerContributorId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "fallback-alias", translation: "", selectedMeaningId: "sense-y",
            acceptedAliasesJson: """["synonym-x"]""");
        var higherContributorId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "fallback-alias", translation: "", selectedMeaningId: "sense-y",
            acceptedAliasesJson: """["synonym-x"]""");
        await fixture.InsertCardAsync(wordId, representativeId, CardDirection.MeaningToTerm);

        Assert.IsTrue(lowerContributorId < higherContributorId);

        await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", representativeId);
        var variants = await fixture.Connection.QueryAsync<AnswerVariantProvenanceRow>(
            "SELECT SourceMeaningId FROM AnswerVariants WHERE SenseId = ? AND NormalizedText = 'synonym-x'", senseId);

        Assert.HasCount(1, variants); // exactly one AnswerVariant row after deduplication
        Assert.AreEqual(lowerContributorId, variants[0].SourceMeaningId); // lowest-Id contributor wins, never the higher one
    }

    [TestMethod]
    public async Task DeterministicSourceMeaningId_ThreeWayDuplicate_RepresentativeStillWinsAndDedupesToOneRow()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("triple-shared");
        var representativeId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "triple-shared", translation: "", selectedMeaningId: "sense-z");
        await fixture.InsertMeaningAsync(wordId, displayTerm: "triple-shared", translation: "", selectedMeaningId: "sense-z");
        await fixture.InsertMeaningAsync(wordId, displayTerm: "triple-shared", translation: "", selectedMeaningId: "sense-z");
        await fixture.InsertCardAsync(wordId, representativeId, CardDirection.MeaningToTerm);

        await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", representativeId);
        var variants = await fixture.Connection.QueryAsync<AnswerVariantProvenanceRow>(
            "SELECT SourceMeaningId FROM AnswerVariants WHERE SenseId = ? AND NormalizedText = 'triple-shared'", senseId);

        Assert.HasCount(1, variants);
        Assert.AreEqual(representativeId, variants[0].SourceMeaningId);
    }

    private sealed class AnswerVariantProvenanceRow
    {
        public int SourceMeaningId { get; set; }
    }

    // ---- Focused invariant 2: exactly one preferred assignment, deterministic fallback ----

    [TestMethod]
    public async Task PreferredAssignmentFallback_RepresentativeMissingTranslation_NonRepresentativeWins()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("credit");
        var representativeId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "credit", translation: "", selectedMeaningId: "fin-institution"); // no Translation
        var otherId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "credit", translation: "Kreditinstitut", selectedMeaningId: "fin-institution");
        await fixture.InsertCardAsync(wordId, representativeId, CardDirection.TermToMeaning);

        await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", representativeId);
        var assignments = await fixture.Connection.QueryAsync<AssignmentTextRow>(
            """
            SELECT a.Requirement, a.IsPreferred, a.RequiredSinceUtc, v.NormalizedText
            FROM SenseAnswerVariantAssignments a JOIN AnswerVariants v ON v.Id = a.AnswerVariantId
            WHERE a.SenseId = ? AND a.CardDirection = ?
            """,
            senseId, (int)CardDirection.TermToMeaning);

        Assert.HasCount(1, assignments);
        Assert.IsTrue(assignments[0].IsPreferred);
        // Slice 4: the deterministic primary of an existing card direction is Required with a boundary.
        Assert.AreEqual((int)AnswerVariantRequirement.Required, assignments[0].Requirement);
        Assert.IsNotNull(assignments[0].RequiredSinceUtc);
        Assert.AreEqual("Kreditinstitut", assignments[0].NormalizedText);

        var variantSourceMeaningId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SourceMeaningId FROM AnswerVariants WHERE SenseId = ? AND NormalizedText = 'Kreditinstitut'", senseId);
        Assert.AreEqual(otherId, variantSourceMeaningId);
    }

    [TestMethod]
    public async Task PreferredAssignmentFallback_MultipleNonRepresentativeCandidates_SelectsLowestIdDeterministically()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("option");
        var representativeId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "option", translation: "", selectedMeaningId: "sense-opt"); // no Translation
        var lowerId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "option", translation: "Option-Eins", selectedMeaningId: "sense-opt");
        var higherId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "option", translation: "Option-Zwei", selectedMeaningId: "sense-opt");
        await fixture.InsertCardAsync(wordId, representativeId, CardDirection.TermToMeaning);

        Assert.IsTrue(lowerId < higherId);

        await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", representativeId);
        var assignments = await fixture.Connection.QueryAsync<AssignmentTextRow>(
            """
            SELECT a.Requirement, a.IsPreferred, a.RequiredSinceUtc, v.NormalizedText
            FROM SenseAnswerVariantAssignments a JOIN AnswerVariants v ON v.Id = a.AnswerVariantId
            WHERE a.SenseId = ? AND a.CardDirection = ?
            """,
            senseId, (int)CardDirection.TermToMeaning);

        Assert.HasCount(2, assignments); // both translations are usable
        var preferred = assignments.Single(a => a.IsPreferred);
        Assert.AreEqual("Option-Eins", preferred.NormalizedText); // lower legacy Meaning.Id wins deterministically
        Assert.AreEqual((int)AnswerVariantRequirement.Required, preferred.Requirement); // Slice 4 primary
        Assert.IsNotNull(preferred.RequiredSinceUtc);

        var runnerUp = assignments.Single(a => a.NormalizedText == "Option-Zwei");
        Assert.IsFalse(runnerUp.IsPreferred);
        Assert.AreEqual((int)AnswerVariantRequirement.AcceptedOnly, runnerUp.Requirement);
        Assert.IsNull(runnerUp.RequiredSinceUtc);

        // The partial unique index still rejects a second preferred assignment for this same
        // (SenseId, CardDirection), even for the code path this fallback selection newly introduces.
        var losingVariantId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT Id FROM AnswerVariants WHERE SenseId = ? AND NormalizedText = 'Option-Zwei'", senseId);
        var exception = await Assert.ThrowsExactlyAsync<SQLite.SQLiteException>(() => fixture.Connection.RunInTransactionAsync(connection =>
        {
            connection.Execute(
                "INSERT INTO SenseAnswerVariantAssignments (StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, 1, ?, ?)",
                Guid.NewGuid().ToString("N"), senseId, (int)CardDirection.TermToMeaning, losingVariantId, (int)AnswerVariantRequirement.AcceptedOnly, DateTime.UtcNow, DateTime.UtcNow);
        }));
        Assert.IsNotNull(exception);
    }

    [TestMethod]
    public async Task PreferredAssignmentFallback_ExistingRepresentativePreferred_IsNotDisplaced()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("keep");
        var representativeId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "keep", translation: "behalten", selectedMeaningId: "sense-keep");
        await fixture.InsertMeaningAsync(
            wordId, displayTerm: "keep", translation: "bewahren", selectedMeaningId: "sense-keep");
        await fixture.InsertCardAsync(wordId, representativeId, CardDirection.TermToMeaning);

        await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", representativeId);
        var preferred = await fixture.Connection.QueryAsync<AssignmentTextRow>(
            """
            SELECT a.Requirement, a.IsPreferred, a.RequiredSinceUtc, v.NormalizedText
            FROM SenseAnswerVariantAssignments a JOIN AnswerVariants v ON v.Id = a.AnswerVariantId
            WHERE a.SenseId = ? AND a.CardDirection = ? AND a.IsPreferred = 1
            """,
            senseId, (int)CardDirection.TermToMeaning);

        Assert.HasCount(1, preferred);
        Assert.AreEqual("behalten", preferred[0].NormalizedText); // the representative's own Translation, untouched
    }

    [TestMethod]
    public async Task PreferredAssignmentFallback_NoAssignableExpressionForDirection_CreatesNoPreferredAssignment()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("no-translation");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "no-translation", translation: "");
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.TermToMeaning);

        await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", meaningId);
        var termToMeaningAssignments = await Schema8MigrationAssertHelpers.CountAsync(
            fixture.Connection, "SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE SenseId = ? AND CardDirection = ?",
            senseId, (int)CardDirection.TermToMeaning);

        Assert.AreEqual(0, termToMeaningAssignments); // never invented
    }

    // ---- Focused invariant 3: non-representative aliases ----

    [TestMethod]
    public async Task NonRepresentativeAlias_BecomesAcceptedOnlyAssignment_NeverDisplacesPreferredTerm()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("swift");
        var representativeId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "swift", translation: "", selectedMeaningId: "sense-swift", acceptedAliasesJson: "[]");
        await fixture.InsertMeaningAsync(
            wordId, displayTerm: "swift", translation: "", selectedMeaningId: "sense-swift",
            acceptedAliasesJson: """["quick"]""");
        await fixture.InsertCardAsync(wordId, representativeId, CardDirection.MeaningToTerm);

        await Schema8DormantMigration.ApplyAsync(fixture.Connection);

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", representativeId);
        var assignments = await fixture.Connection.QueryAsync<AssignmentTextRow>(
            """
            SELECT a.Requirement, a.IsPreferred, a.RequiredSinceUtc, v.NormalizedText
            FROM SenseAnswerVariantAssignments a JOIN AnswerVariants v ON v.Id = a.AnswerVariantId
            WHERE a.SenseId = ? AND a.CardDirection = ?
            """,
            senseId, (int)CardDirection.MeaningToTerm);

        Assert.HasCount(2, assignments); // "swift" (preferred term) + "quick" (accepted-only alias)

        var aliasAssignment = assignments.Single(a => a.NormalizedText == "quick");
        Assert.AreEqual((int)AnswerVariantRequirement.AcceptedOnly, aliasAssignment.Requirement);
        Assert.IsNull(aliasAssignment.RequiredSinceUtc);
        Assert.IsFalse(aliasAssignment.IsPreferred);

        var termAssignment = assignments.Single(a => a.NormalizedText == "swift");
        Assert.IsTrue(termAssignment.IsPreferred);
        // Slice 4: the preferred primary of the existing MeaningToTerm card is Required with a boundary.
        Assert.AreEqual((int)AnswerVariantRequirement.Required, termAssignment.Requirement);
        Assert.IsNotNull(termAssignment.RequiredSinceUtc);
    }

    // ================= KF-MEANING-001 Slice 4: RequiredSinceUtc, epochs and compatibility progress =========
    // Fixed fixture values: T0 = 2026-01-01Z, T1 = 2026-01-02Z, T2 = 2027-01-02Z; Word 10, Sense A 20,
    // Sense B 21, Meaning 30, MeaningToTerm card 40, TermToMeaning card 41, other-Sense card 42.

    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = new(2027, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime Utc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static void AssertSameInstant(DateTime? expected, DateTime? actual, string because)
    {
        if (expected is null || actual is null)
        {
            Assert.AreEqual(expected is null, actual is null, because);
            return;
        }

        Assert.AreEqual(Utc(expected.Value).Ticks, Utc(actual.Value).Ticks, because);
    }

    [TestMethod]
    public async Task PrimaryAssignment_IsRequiredAndPreferred()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("bank");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "bank", translation: "Bank");
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm, createdAtUtc: T0, id: 40);

        await fixture.MigrateToSchema8Async();
        Assert.AreEqual(8, await fixture.ReadUserVersionAsync());

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", meaningId);
        var assignments = await fixture.ReadAssignmentsAsync(senseId, CardDirection.MeaningToTerm);

        var primary = assignments.Single(a => a.IsPreferred);
        Assert.AreEqual(AnswerVariantRequirement.Required, primary.Requirement);
        Assert.IsTrue(primary.IsPreferred);
        Assert.IsNotNull(primary.RequiredSinceUtc);
        Assert.IsFalse(string.IsNullOrWhiteSpace(primary.StableId));

        var duplicateStableIds = await Schema8MigrationAssertHelpers.CountAsync(
            fixture.Connection,
            "SELECT COUNT(*) FROM (SELECT StableId FROM SenseAnswerVariantAssignments GROUP BY StableId HAVING COUNT(*) > 1)");
        Assert.AreEqual(0, duplicateStableIds);
    }

    [TestMethod]
    public async Task PrimaryAssignment_RequiredSinceUtcEqualsCardCreatedAtUtc()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("bank");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "bank", translation: "Bank");
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm, createdAtUtc: T0, id: 40);

        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", meaningId);
        var primary = (await fixture.ReadAssignmentsAsync(senseId, CardDirection.MeaningToTerm)).Single(a => a.IsPreferred);

        AssertSameInstant(T0, primary.RequiredSinceUtc, "the boundary is the affected card's own CreatedAtUtc");
    }

    [TestMethod]
    public async Task PrimaryAssignment_PerDirection_UsesOwnCardCreatedAtUtc()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("bank");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "bank", translation: "Bank");
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm, createdAtUtc: T0, id: 40);
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.TermToMeaning, createdAtUtc: T1, id: 41);

        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", meaningId);

        var termSide = (await fixture.ReadAssignmentsAsync(senseId, CardDirection.MeaningToTerm)).Single(a => a.IsPreferred);
        var meaningSide = (await fixture.ReadAssignmentsAsync(senseId, CardDirection.TermToMeaning)).Single(a => a.IsPreferred);

        AssertSameInstant(T0, termSide.RequiredSinceUtc, "MeaningToTerm uses card 40's CreatedAtUtc");
        AssertSameInstant(T1, meaningSide.RequiredSinceUtc, "TermToMeaning uses card 41's own CreatedAtUtc");
        Assert.AreEqual(AnswerVariantRequirement.Required, termSide.Requirement);
        Assert.AreEqual(AnswerVariantRequirement.Required, meaningSide.Requirement);
    }

    [TestMethod]
    public async Task Aliases_RemainAcceptedOnlyWithNullBoundary()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("color");
        var meaningId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "color", translation: "Farbe", acceptedAliasesJson: """["colour","hue"]""");
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm, createdAtUtc: T0, id: 40);

        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", meaningId);
        var assignments = await fixture.ReadAssignmentsAsync(senseId, CardDirection.MeaningToTerm);

        foreach (var alias in new[] { "colour", "hue" })
        {
            var row = assignments.Single(a => a.NormalizedText == alias);
            Assert.AreEqual(AnswerVariantRequirement.AcceptedOnly, row.Requirement);
            Assert.IsFalse(row.IsPreferred);
            Assert.IsNull(row.RequiredSinceUtc);
            Assert.AreEqual("en", row.AnswerLanguage); // aliases are always term-side
        }
    }

    [TestMethod]
    public async Task AlternativeVariants_RemainAcceptedOnlyWithNullBoundary()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("bank");
        var first = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "bank", translation: "Bank", selectedMeaningId: "fin");
        await fixture.InsertMeaningAsync(
            wordId, displayTerm: "bank", translation: "Kreditinstitut", selectedMeaningId: "fin");
        await fixture.InsertCardAsync(wordId, first, CardDirection.TermToMeaning, createdAtUtc: T0, id: 41);

        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", first);
        var alternative = (await fixture.ReadAssignmentsAsync(senseId, CardDirection.TermToMeaning))
            .Single(a => a.NormalizedText == "Kreditinstitut");

        Assert.AreEqual(AnswerVariantRequirement.AcceptedOnly, alternative.Requirement);
        Assert.IsFalse(alternative.IsPreferred);
        Assert.IsNull(alternative.RequiredSinceUtc);
    }

    [TestMethod]
    public async Task NoValidPrimaryExpression_CreatesNoAssignment()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("empty");
        // No Translation at all: the TermToMeaning direction has no assignable expression.
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "empty", translation: "");
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.TermToMeaning, createdAtUtc: T0, id: 41);

        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", meaningId);
        var assignments = await fixture.ReadAssignmentsAsync(senseId, CardDirection.TermToMeaning);

        Assert.IsEmpty(assignments); // nothing invented, no fallback text
    }

    [TestMethod]
    public async Task RetiredCardWithNonDefaultCounters_ProgressIsMastered()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync(
            "mastered", consecutiveTypingSuccessCount: 2, automaticInteractionMode: LearningInteractionMode.Typing);
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "mastered", translation: "gemeistert");
        await fixture.InsertCardAsync(
            wordId, meaningId, CardDirection.MeaningToTerm, state: CardState.Retired,
            createdAtUtc: T0, lastReviewedAtUtc: T1, id: 40);

        await fixture.MigrateToSchema8Async();

        var progress = (await fixture.ReadProgressAsync()).Single(p => p.CardId == 40);
        Assert.IsTrue(progress.IsMastered);
        Assert.AreEqual(1, progress.ReplayVersion);
        Assert.AreEqual(2, progress.ConsecutiveTypingSuccessCount);
    }

    [TestMethod]
    public async Task RetiredCardWithZeroCounters_ReceivesMasteredCompatibilityRow()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("legacy"); // all-default automatic counters
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "legacy", translation: "alt");
        await fixture.InsertCardAsync(
            wordId, meaningId, CardDirection.MeaningToTerm, state: CardState.Retired,
            createdAtUtc: T0, lastReviewedAtUtc: T1, id: 40);

        await fixture.MigrateToSchema8Async();

        var progress = (await fixture.ReadProgressAsync()).Single(p => p.CardId == 40);
        Assert.IsTrue(progress.IsMastered);
        Assert.AreEqual(1, progress.ReplayVersion);
        Assert.AreEqual(LearningInteractionMode.Typing, progress.InteractionMode);
        Assert.AreEqual(
            KnownFirst.Core.Learning.AutomaticLearningPolicy.RequiredConsecutiveAssessments,
            progress.ConsecutiveTypingSuccessCount);
        Assert.AreEqual(0, progress.ConsecutiveReadingSuccessCount);
        Assert.AreEqual(0, progress.ConsecutiveTypingFailureCount);
        Assert.IsFalse(progress.MasteryReviewExtensionScheduled);
    }

    [TestMethod]
    public async Task NonRetiredSiblingCard_RemainsUnmastered()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("pair");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "pair", translation: "Paar");
        await fixture.InsertCardAsync(
            wordId, meaningId, CardDirection.MeaningToTerm, state: CardState.Retired, createdAtUtc: T0, id: 40);
        await fixture.InsertCardAsync(
            wordId, meaningId, CardDirection.TermToMeaning, state: CardState.Review, createdAtUtc: T0,
            intervalDays: 5, id: 41);

        await fixture.MigrateToSchema8Async();

        var cards = await fixture.ReadCardsAsync();
        Assert.AreEqual(CardState.Retired, cards.Single(c => c.Id == 40).State);
        Assert.AreEqual(CardState.Review, cards.Single(c => c.Id == 41).State);

        var progress = await fixture.ReadProgressAsync();
        Assert.IsTrue(progress.Single(p => p.CardId == 40).IsMastered);
        Assert.IsFalse(progress.Any(p => p.CardId == 41 && p.IsMastered));
    }

    [TestMethod]
    public async Task WordStatusAlone_DoesNotMasterEveryCard()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("done", status: WordStatus.Mastered);
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "done", translation: "fertig");
        await fixture.InsertCardAsync(
            wordId, meaningId, CardDirection.MeaningToTerm, state: CardState.Retired, createdAtUtc: T0, id: 40);
        await fixture.InsertCardAsync(
            wordId, meaningId, CardDirection.TermToMeaning, state: CardState.Review, createdAtUtc: T0, id: 41);

        await fixture.MigrateToSchema8Async();

        var cards = await fixture.ReadCardsAsync();
        Assert.AreEqual(CardState.Review, cards.Single(c => c.Id == 41).State);
        Assert.IsFalse((await fixture.ReadProgressAsync()).Any(p => p.CardId == 41 && p.IsMastered));

        // WordStatus itself is reset out of the frozen learning tiers.
        Assert.AreEqual(
            (int)WordStatus.UnknownBacklog,
            await fixture.Connection.ExecuteScalarAsync<int>("SELECT Status FROM Words WHERE Id = ?", wordId));
    }

    [TestMethod]
    public async Task CardIdsStatesAndSchedules_ArePreserved()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("keep");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "keep", translation: "behalten");
        await fixture.InsertCardAsync(
            wordId, meaningId, CardDirection.MeaningToTerm, state: CardState.Review, dueAtUtc: T2,
            intervalDays: 365, easeFactor: 2.35, successfulReviewCount: 7, lapseCount: 2,
            lastReviewedAtUtc: T1, lastRating: ReviewRating.Good, createdAtUtc: T0, updatedAtUtc: T1, id: 40);

        await fixture.MigrateToSchema8Async();

        var card = (await fixture.ReadCardsAsync()).Single();
        Assert.AreEqual(40, card.Id);
        Assert.AreEqual(CardState.Review, card.State);
        AssertSameInstant(T2, card.DueAtUtc, "DueAtUtc preserved");
        Assert.AreEqual(365, card.IntervalDays);
        Assert.AreEqual(2.35, card.EaseFactor);
        Assert.AreEqual(7, card.SuccessfulReviewCount);
        Assert.AreEqual(2, card.LapseCount);
        AssertSameInstant(T1, card.LastReviewedAtUtc, "LastReviewedAtUtc preserved");
        Assert.AreEqual(ReviewRating.Good, card.LastRating);
        AssertSameInstant(T0, card.CreatedAtUtc, "CreatedAtUtc preserved");
        AssertSameInstant(T1, card.UpdatedAtUtc, "UpdatedAtUtc preserved");
    }

    [TestMethod]
    public async Task MigrationIsDeterministic_TwoRunsOnIdenticalFixturesAgree()
    {
        static async Task<List<string>> ProjectAsync(Schema7Fixture fixture)
        {
            var wordId = await fixture.InsertWordAsync("bank");
            var meaningId = await fixture.InsertMeaningAsync(
                wordId, displayTerm: "bank", translation: "Bank", acceptedAliasesJson: """["banc"]""");
            await fixture.InsertMeaningAsync(
                wordId, displayTerm: "bank", translation: "Kreditinstitut", selectedMeaningId: "");
            await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm, createdAtUtc: T0, id: 40);
            await fixture.InsertCardAsync(wordId, meaningId, CardDirection.TermToMeaning, createdAtUtc: T1, id: 41);

            await fixture.MigrateToSchema8Async();

            var rows = await fixture.Connection.QueryAsync<DeterminismRow>(
                """
                SELECT a.CardDirection, a.Requirement, a.IsPreferred, a.RequiredSinceUtc, v.NormalizedText, v.AnswerLanguage
                FROM SenseAnswerVariantAssignments a JOIN AnswerVariants v ON v.Id = a.AnswerVariantId
                ORDER BY a.CardDirection, v.AnswerLanguage, v.NormalizedText
                """);
            return rows
                .Select(r => $"{r.CardDirection}|{r.Requirement}|{r.IsPreferred}|{r.RequiredSinceUtc?.Ticks}|{r.AnswerLanguage}|{r.NormalizedText}")
                .ToList();
        }

        await using var first = await Schema7Fixture.CreateAsync();
        await using var second = await Schema7Fixture.CreateAsync();

        var firstProjection = await ProjectAsync(first);
        var secondProjection = await ProjectAsync(second);

        Assert.IsNotEmpty(firstProjection);
        CollectionAssert.AreEqual(firstProjection, secondProjection);
    }

    [TestMethod]
    public async Task MigrationIsIdempotent_SecondApplyReportsAlreadyApplied()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("idem");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "idem", translation: "gleich");
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm, createdAtUtc: T0, id: 40);

        var firstResult = await fixture.MigrateToSchema8Async();
        Assert.AreEqual(Schema8MigrationOutcome.Migrated, firstResult.Outcome);

        var assignmentsBefore = await Schema8MigrationAssertHelpers.CountAsync(
            fixture.Connection, "SELECT COUNT(*) FROM SenseAnswerVariantAssignments");

        var secondResult = await fixture.MigrateToSchema8Async();
        Assert.AreEqual(Schema8MigrationOutcome.AlreadyApplied, secondResult.Outcome);
        Assert.AreEqual(8, await fixture.ReadUserVersionAsync());
        Assert.AreEqual(
            assignmentsBefore,
            await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM SenseAnswerVariantAssignments"));
    }

    [TestMethod]
    public async Task InvariantFailure_RequiredWithNullBoundary_FailsClosed()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("bad");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "bad", translation: "schlecht");
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm, createdAtUtc: T0, id: 40);

        var options = new Schema8MigrationOptions
        {
            FaultInjectionHook = checkpoint =>
            {
                if (checkpoint != "after-backfill")
                {
                    return;
                }

                // Break invariant I1 in the direction "Required without a boundary" before the final gate.
                fixture.Connection.GetConnection().Execute(
                    "UPDATE SenseAnswerVariantAssignments SET RequiredSinceUtc = NULL WHERE Requirement = ?",
                    (int)AnswerVariantRequirement.Required);
            }
        };

        await Assert.ThrowsExactlyAsync<Schema8MigrationException>(() => fixture.MigrateToSchema8Async(options));
        Assert.AreEqual(7, await fixture.ReadUserVersionAsync());
        Assert.IsFalse(await Schema8MigrationAssertHelpers.TableExistsAsync(fixture.Connection, "SenseAnswerVariantAssignments"));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "MeaningId"));
    }

    [TestMethod]
    public async Task InvariantFailure_AcceptedOnlyWithNonNullBoundary_FailsClosed()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("bad2");
        var meaningId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "bad2", translation: "schlecht", acceptedAliasesJson: """["schlimm"]""");
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm, createdAtUtc: T0, id: 40);

        var options = new Schema8MigrationOptions
        {
            FaultInjectionHook = checkpoint =>
            {
                if (checkpoint != "after-backfill")
                {
                    return;
                }

                // Break invariant I1 in the opposite direction: AcceptedOnly carrying a boundary.
                fixture.Connection.GetConnection().Execute(
                    "UPDATE SenseAnswerVariantAssignments SET RequiredSinceUtc = ? WHERE Requirement = ?",
                    T1, (int)AnswerVariantRequirement.AcceptedOnly);
            }
        };

        await Assert.ThrowsExactlyAsync<Schema8MigrationException>(() => fixture.MigrateToSchema8Async(options));
        Assert.AreEqual(7, await fixture.ReadUserVersionAsync());
        Assert.IsFalse(await Schema8MigrationAssertHelpers.TableExistsAsync(fixture.Connection, "SenseAnswerVariantAssignments"));
    }

    [TestMethod]
    public async Task CompatibilityRow_TimestampsComeFromCardNotMigrationTime()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("timestamps");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "timestamps", translation: "Zeitstempel");
        await fixture.InsertCardAsync(
            wordId, meaningId, CardDirection.MeaningToTerm, state: CardState.Retired,
            createdAtUtc: T0, lastReviewedAtUtc: T1, id: 40);

        await fixture.MigrateToSchema8Async();

        var progress = (await fixture.ReadProgressAsync()).Single(p => p.CardId == 40);
        AssertSameInstant(T0, progress.CreatedAtUtc, "CreatedAtUtc = card.CreatedAtUtc");
        AssertSameInstant(T1, progress.LastAssessedAtUtc, "LastAssessedAtUtc = card.LastReviewedAtUtc");
        AssertSameInstant(T1, progress.UpdatedAtUtc, "UpdatedAtUtc = card.LastReviewedAtUtc");

        // Nothing is derived from migration execution time.
        Assert.IsTrue(Utc(progress.CreatedAtUtc) < DateTime.UtcNow.AddDays(-1));
    }

    [TestMethod]
    public async Task CompatibilityRow_CreatedAtUtcEqualsAssignmentRequiredSinceUtc()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("epoch");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "epoch", translation: "Epoche");
        await fixture.InsertCardAsync(
            wordId, meaningId, CardDirection.MeaningToTerm, state: CardState.Retired, createdAtUtc: T0, id: 40);

        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT SenseId FROM Meanings WHERE Id = ?", meaningId);
        var primary = (await fixture.ReadAssignmentsAsync(senseId, CardDirection.MeaningToTerm)).Single(a => a.IsPreferred);
        var progress = (await fixture.ReadProgressAsync()).Single(p => p.CardId == 40);

        Assert.AreEqual(primary.AnswerVariantId, progress.AnswerVariantId);
        AssertSameInstant(
            primary.RequiredSinceUtc, progress.CreatedAtUtc,
            "the seeded row must be current-Required-epoch progress, otherwise replay would reset it");
    }

    [TestMethod]
    public async Task ShapeValidator_MissingRequiredSinceUtcColumn_IsInvalidShape()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("shape");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "shape", translation: "Form");
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm, createdAtUtc: T0, id: 40);

        await fixture.MigrateToSchema8Async();
        var meaningsBefore = await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM Meanings");
        var cardsBefore = await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM LearningCards");
        var assignmentsBefore = await Schema8MigrationAssertHelpers.CountAsync(
            fixture.Connection, "SELECT COUNT(*) FROM SenseAnswerVariantAssignments");

        // Portable rebuild — never ALTER TABLE ... DROP COLUMN.
        await fixture.RebuildAssignmentsTableWithoutRequiredSinceUtcAsync();
        Assert.IsFalse(await Schema8MigrationAssertHelpers.ColumnExistsAsync(
            fixture.Connection, "SenseAnswerVariantAssignments", "RequiredSinceUtc"));
        Assert.AreEqual(8, await fixture.ReadUserVersionAsync());

        // Schema8ShapeValidator is internal to the production assembly and the test project has no
        // InternalsVisibleTo, so the shared shape verdict is proven through the two public surfaces that
        // delegate to it. Both must reject, and both must name the missing column.
        var capabilityException = await Assert.ThrowsExactlyAsync<LearningSchemaCapabilityException>(
            () => fixture.Connection.RunInTransactionAsync(
                connection => LearningSchemaCapability.Resolve(connection)));
        Assert.IsTrue(capabilityException.ShapeMismatch);
        Assert.AreEqual("learning-schema-capability-shape-mismatch", capabilityException.ErrorCode);
        Assert.Contains("RequiredSinceUtc", capabilityException.ShapeDetail ?? string.Empty);

        var migrationException = await Assert.ThrowsExactlyAsync<Schema8MigrationException>(
            () => fixture.MigrateToSchema8Async());
        Assert.AreEqual("schema8-migration-already-applied-shape-invalid", migrationException.ErrorCode);
        Assert.Contains("RequiredSinceUtc", migrationException.Message);

        // No unrelated data changed.
        Assert.AreEqual(meaningsBefore, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM Meanings"));
        Assert.AreEqual(cardsBefore, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM LearningCards"));
        Assert.AreEqual(
            assignmentsBefore,
            await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM SenseAnswerVariantAssignments"));
    }

    private sealed class DeterminismRow
    {
        public int CardDirection { get; set; }
        public int Requirement { get; set; }
        public bool IsPreferred { get; set; }
        public DateTime? RequiredSinceUtc { get; set; }
        public string NormalizedText { get; set; } = string.Empty;
        public string AnswerLanguage { get; set; } = string.Empty;
    }

    private static async Task AssertFailsClosedAsync(Schema7Fixture fixture)
    {
        var tablesBefore = await Schema8MigrationAssertHelpers.GetTableNamesAsync(fixture.Connection);

        await Assert.ThrowsExactlyAsync<Schema8MigrationException>(() => Schema8DormantMigration.ApplyAsync(fixture.Connection));

        Assert.AreEqual(7, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));
        var tablesAfter = await Schema8MigrationAssertHelpers.GetTableNamesAsync(fixture.Connection);
        CollectionAssert.AreEqual(tablesBefore, tablesAfter);
        Assert.IsTrue(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "MeaningId"));
    }
}
