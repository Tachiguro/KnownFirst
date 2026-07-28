using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models;

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

        // Both Translation-sourced variants are assigned (usable) for TermToMeaning, both AcceptedOnly.
        // Exactly one is preferred (Focused invariant 2) — the representative Meaning's own "Bank", since
        // meaningA is the group's lowest-Id (representative) Meaning; "Kreditinstitut" (meaningB) is
        // accepted but not preferred.
        var termToMeaningAssignments = await fixture.Connection.QueryAsync<AssignmentTextRow>(
            """
            SELECT a.Requirement, a.IsPreferred, v.NormalizedText
            FROM SenseAnswerVariantAssignments a JOIN AnswerVariants v ON v.Id = a.AnswerVariantId
            WHERE a.SenseId = ? AND a.CardDirection = ?
            """,
            senseId, (int)CardDirection.TermToMeaning);
        Assert.HasCount(2, termToMeaningAssignments);
        Assert.IsTrue(termToMeaningAssignments.All(a => a.Requirement == (int)AnswerVariantRequirement.AcceptedOnly));
        Assert.HasCount(1, termToMeaningAssignments.Where(a => a.IsPreferred).ToList());
        Assert.AreEqual("Bank", termToMeaningAssignments.Single(a => a.IsPreferred).NormalizedText);
        Assert.AreEqual("Kreditinstitut", termToMeaningAssignments.Single(a => !a.IsPreferred).NormalizedText);

        // The single deduped term-side variant still has exactly one MeaningToTerm assignment, and it is
        // still the preferred one — the representative Meaning's existing preferred assignment is untouched.
        var meaningToTermAssignments = await fixture.Connection.QueryAsync<AssignmentFlagsRow>(
            "SELECT Requirement, IsPreferred FROM SenseAnswerVariantAssignments WHERE SenseId = ? AND CardDirection = ?",
            senseId, (int)CardDirection.MeaningToTerm);
        Assert.HasCount(1, meaningToTermAssignments);
        Assert.IsTrue(meaningToTermAssignments[0].IsPreferred);

        // Never invents Required (Decision 12), and never more than one preferred assignment per direction.
        Assert.AreEqual(
            0, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE SenseId = ? AND Requirement = ?", senseId, (int)AnswerVariantRequirement.Required));
        Assert.AreEqual(
            2, await Schema8MigrationAssertHelpers.CountAsync(fixture.Connection, "SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE SenseId = ? AND IsPreferred = 1", senseId));
    }

    private sealed class AssignmentTextRow
    {
        public int Requirement { get; set; }
        public bool IsPreferred { get; set; }
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

        var requiredCount = await Schema8MigrationAssertHelpers.CountAsync(
            fixture.Connection,
            "SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE SenseId = ? AND Requirement = ?",
            senseId, (int)AnswerVariantRequirement.Required);
        Assert.AreEqual(0, requiredCount); // never invented Required (Decision 12)
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

    [TestMethod]
    public async Task NormalInitializeAsync_PopulatedDatabase_StillReportsVersion7AndSchema7Shape()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync("populated");
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: "populated", translation: "x");
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm);

        await DatabaseSchema.InitializeAsync(fixture.Connection);

        Assert.AreEqual(7, await Schema8MigrationAssertHelpers.GetUserVersionAsync(fixture.Connection));
        Assert.IsFalse(await Schema8MigrationAssertHelpers.TableExistsAsync(fixture.Connection, "Senses"));
        Assert.IsFalse(await Schema8MigrationAssertHelpers.TableExistsAsync(fixture.Connection, "AnswerVariants"));
        Assert.IsFalse(await Schema8MigrationAssertHelpers.TableExistsAsync(fixture.Connection, "SenseAnswerVariantAssignments"));
        Assert.IsFalse(await Schema8MigrationAssertHelpers.TableExistsAsync(fixture.Connection, "AnswerVariantProgress"));
        Assert.IsTrue(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "MeaningId"));
        Assert.IsFalse(await Schema8MigrationAssertHelpers.ColumnExistsAsync(fixture.Connection, "LearningCards", "SenseId"));
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
            SELECT a.Requirement, a.IsPreferred, v.NormalizedText
            FROM SenseAnswerVariantAssignments a JOIN AnswerVariants v ON v.Id = a.AnswerVariantId
            WHERE a.SenseId = ? AND a.CardDirection = ?
            """,
            senseId, (int)CardDirection.TermToMeaning);

        Assert.HasCount(1, assignments);
        Assert.IsTrue(assignments[0].IsPreferred);
        Assert.AreEqual((int)AnswerVariantRequirement.AcceptedOnly, assignments[0].Requirement);
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
            SELECT a.Requirement, a.IsPreferred, v.NormalizedText
            FROM SenseAnswerVariantAssignments a JOIN AnswerVariants v ON v.Id = a.AnswerVariantId
            WHERE a.SenseId = ? AND a.CardDirection = ?
            """,
            senseId, (int)CardDirection.TermToMeaning);

        Assert.HasCount(2, assignments); // both translations are usable (AcceptedOnly)
        var preferred = assignments.Single(a => a.IsPreferred);
        Assert.AreEqual("Option-Eins", preferred.NormalizedText); // lower legacy Meaning.Id wins deterministically
        Assert.IsFalse(assignments.Single(a => a.NormalizedText == "Option-Zwei").IsPreferred);

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
            SELECT a.Requirement, a.IsPreferred, v.NormalizedText
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
            SELECT a.Requirement, a.IsPreferred, v.NormalizedText
            FROM SenseAnswerVariantAssignments a JOIN AnswerVariants v ON v.Id = a.AnswerVariantId
            WHERE a.SenseId = ? AND a.CardDirection = ?
            """,
            senseId, (int)CardDirection.MeaningToTerm);

        Assert.HasCount(2, assignments); // "swift" (preferred term) + "quick" (accepted-only alias)

        var aliasAssignment = assignments.Single(a => a.NormalizedText == "quick");
        Assert.AreEqual((int)AnswerVariantRequirement.AcceptedOnly, aliasAssignment.Requirement);
        Assert.IsFalse(aliasAssignment.IsPreferred);

        var termAssignment = assignments.Single(a => a.NormalizedText == "swift");
        Assert.IsTrue(termAssignment.IsPreferred);
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
