using System.Text;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema13;
using KnownFirst.Data.Schema14;
using KnownFirst.Data.Targets;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services;
using KnownFirst.Services.DataSafety;
using SQLite;
using static KnownFirst.Tests.DatabaseSchema13ProductionCutoverTests;

namespace KnownFirst.Tests;

[TestClass]
[DoNotParallelize]
public sealed class VocabularyCorrectionServiceTests
{
    private static readonly DateTime BaseTimeUtc = new(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset BaseOffsetUtc = new(BaseTimeUtc, TimeSpan.Zero);

    [TestMethod]
    public async Task ValidEquivalentAcceptedOnlyInsertion_SucceedsAndPersistsExpectedValues()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var (wordId, senseId, targetId, _) = await SeedWordSenseAndTargetAsync(database);

        var service = new VocabularyCorrectionService(database);
        var request = new AddAcceptedAnswerVariantRequest(
            wordId: wordId,
            senseId: senseId,
            targetId: targetId,
            answerText: "  Geschäftsinstitut  ",
            expectedLanguage: "de");

        var result = await service.AddAcceptedAnswerVariantAsync(request);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.Success, result.Status);
        Assert.IsNull(result.ErrorMessage);
        Assert.IsNotNull(result.Variant);

        var variant = result.Variant;
        Assert.IsTrue(variant.Id > 0);
        Assert.IsFalse(string.IsNullOrWhiteSpace(variant.StableId));
        Assert.AreEqual(targetId, variant.TargetId);
        Assert.AreEqual("de", variant.AnswerLanguage);
        Assert.AreEqual("Geschäftsinstitut", variant.DisplayText);
        Assert.AreEqual("geschäftsinstitut", variant.NormalizedText);
        Assert.AreEqual(AnswerVariantRequirement.AcceptedOnly, variant.Requirement);
        Assert.IsFalse(variant.IsPreferred);
        Assert.IsNull(variant.RequiredSinceUtc);
        Assert.IsNull(variant.SourceMeaningId);
        Assert.AreEqual(DateTimeKind.Utc, variant.CreatedAtUtc.Kind);
        Assert.AreEqual(DateTimeKind.Utc, variant.UpdatedAtUtc.Kind);

        // Verify directly against database
        await database.RunInTransactionAsync(conn =>
        {
            var dbVariants = LearningTargetRepository.GetAnswerVariants(conn, targetId);
            Assert.AreEqual(2, dbVariants.Count);

            var dbAdded = dbVariants.FirstOrDefault(v => v.Id == variant.Id);
            Assert.IsNotNull(dbAdded);
            Assert.AreEqual(variant.StableId, dbAdded.StableId);
            Assert.AreEqual("de", dbAdded.AnswerLanguage);
            Assert.AreEqual("Geschäftsinstitut", dbAdded.DisplayText);
            Assert.AreEqual("geschäftsinstitut", dbAdded.NormalizedText);
            Assert.AreEqual(AnswerVariantRequirement.AcceptedOnly, dbAdded.Requirement);
            Assert.IsFalse(dbAdded.IsPreferred);
            Assert.IsNull(dbAdded.RequiredSinceUtc);
            Assert.IsNull(dbAdded.SourceMeaningId);
            return true;
        });
    }

    [TestMethod]
    public async Task AddedAnswer_IsVisibleThroughExistingVocabularyDetailService()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var (wordId, senseId, targetId, preferredVariantId) = await SeedWordSenseAndTargetAsync(database);

        var correctionService = new VocabularyCorrectionService(database);
        var detailService = new VocabularyDetailService(database);

        var addResult = await correctionService.AddAcceptedAnswerVariantAsync(new AddAcceptedAnswerVariantRequest(
            wordId: wordId,
            senseId: senseId,
            targetId: targetId,
            answerText: "Geldinstitut"));

        Assert.IsTrue(addResult.IsSuccess);

        var detailResult = await detailService.GetVocabularyDetailAsync(wordId);
        Assert.IsTrue(detailResult.IsFound);
        Assert.IsNotNull(detailResult.Detail);

        var detail = detailResult.Detail;
        Assert.AreEqual(1, detail.Senses.Count);
        var sense = detail.Senses[0];
        Assert.AreEqual(1, sense.LearningTargets.Count);
        var target = sense.LearningTargets[0];

        Assert.AreEqual(2, target.AnswerVariants.Count);
        Assert.IsFalse(target.HasMoreAnswerVariants);

        // Preferred variant remains first
        Assert.AreEqual(preferredVariantId, target.AnswerVariants[0].VariantId);
        Assert.IsTrue(target.AnswerVariants[0].IsPreferred);
        Assert.AreEqual(AnswerVariantRequirement.Required, target.AnswerVariants[0].Requirement);

        // AcceptedOnly variant is present
        var acceptedDetail = target.AnswerVariants[1];
        Assert.AreEqual(addResult.Variant!.Id, acceptedDetail.VariantId);
        Assert.IsFalse(acceptedDetail.IsPreferred);
        Assert.AreEqual(AnswerVariantRequirement.AcceptedOnly, acceptedDetail.Requirement);
        Assert.AreEqual("Geldinstitut", acceptedDetail.DisplayText);
        Assert.AreEqual("geldinstitut", acceptedDetail.NormalizedText);
        Assert.IsNull(acceptedDetail.RequiredSinceUtc);
        Assert.IsNull(acceptedDetail.SourceMeaningId);
    }

    [TestMethod]
    public async Task InvalidAndMissingTargetIds_ReturnsControlledErrorsWithoutMutation()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var (wordId, senseId, targetId, _) = await SeedWordSenseAndTargetAsync(database);
        var service = new VocabularyCorrectionService(database);

        // Invalid WordId (<= 0)
        var zeroWord = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(0, senseId, targetId, "Geldinstitut"));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.InvalidWordId, zeroWord.Status);
        Assert.IsTrue(zeroWord.IsInvalidId);

        // Missing WordId
        var missingWord = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(999_999, senseId, targetId, "Geldinstitut"));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.WordNotFound, missingWord.Status);
        Assert.IsTrue(missingWord.IsNotFound);

        // Invalid SenseId (<= 0)
        var zeroSense = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, 0, targetId, "Geldinstitut"));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.InvalidSenseId, zeroSense.Status);
        Assert.IsTrue(zeroSense.IsInvalidId);

        // Missing SenseId
        var missingSense = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, 888_888, targetId, "Geldinstitut"));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.SenseNotFound, missingSense.Status);
        Assert.IsTrue(missingSense.IsNotFound);

        // Invalid TargetId (<= 0)
        var zeroTarget = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, senseId, 0, "Geldinstitut"));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.InvalidTargetId, zeroTarget.Status);
        Assert.IsTrue(zeroTarget.IsInvalidId);

        // Missing TargetId
        var missingTarget = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, senseId, 777_777, "Geldinstitut"));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.TargetNotFound, missingTarget.Status);
        Assert.IsTrue(missingTarget.IsNotFound);

        // Verify no variant was added
        await database.RunInTransactionAsync(conn =>
        {
            var variants = LearningTargetRepository.GetAnswerVariants(conn, targetId);
            Assert.AreEqual(1, variants.Count);
            return true;
        });
    }

    [TestMethod]
    public async Task CrossWordAndCrossSenseTargetIds_ReturnsOwnershipMismatchWithoutPartialWrites()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var (word1Id, sense1Id, target1Id, _) = await SeedWordSenseAndTargetAsync(database, "bank");
        var (word2Id, sense2Id, target2Id, _) = await SeedWordSenseAndTargetAsync(database, "haus");

        var service = new VocabularyCorrectionService(database);

        // Sense 2 does not belong to Word 1
        var senseMismatch = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(word1Id, sense2Id, target2Id, "Gebäude"));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.OwnershipMismatch, senseMismatch.Status);
        Assert.IsTrue(senseMismatch.IsOwnershipMismatch);

        // Target 2 does not belong to Sense 1
        var targetMismatch = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(word1Id, sense1Id, target2Id, "Gebäude"));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.OwnershipMismatch, targetMismatch.Status);
        Assert.IsTrue(targetMismatch.IsOwnershipMismatch);

        // Target 1 under Word 2 (with Sense 2)
        var crossTarget = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(word2Id, sense2Id, target1Id, "Geldinstitut"));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.OwnershipMismatch, crossTarget.Status);

        // Both targets still have only 1 variant
        await database.RunInTransactionAsync(conn =>
        {
            Assert.AreEqual(1, LearningTargetRepository.GetAnswerVariants(conn, target1Id).Count);
            Assert.AreEqual(1, LearningTargetRepository.GetAnswerVariants(conn, target2Id).Count);
            return true;
        });
    }

    [TestMethod]
    public async Task EmptyAndOverlongAnswers_ReturnValidationErrors()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var (wordId, senseId, targetId, _) = await SeedWordSenseAndTargetAsync(database);
        var service = new VocabularyCorrectionService(database);

        // Null/empty/whitespace
        var emptyResult = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, senseId, targetId, ""));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.EmptyAnswerText, emptyResult.Status);
        Assert.IsTrue(emptyResult.IsValidationFailure);

        var wsResult = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, senseId, targetId, "   \t \r\n "));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.EmptyAnswerText, wsResult.Status);

        // Overlong text (> MaxAnswerTextLength = 200)
        var overlong = new string('x', IVocabularyCorrectionService.MaxAnswerTextLength + 1);
        var overlongResult = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, senseId, targetId, overlong));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.AnswerTextTooLong, overlongResult.Status);
        Assert.IsTrue(overlongResult.IsValidationFailure);

        // Exact boundary (200 chars) succeeds
        var exactBoundary = new string('a', IVocabularyCorrectionService.MaxAnswerTextLength);
        var boundaryResult = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, senseId, targetId, exactBoundary));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.Success, boundaryResult.Status);

        // Verify count is 2 (original 1 + boundary 1)
        await database.RunInTransactionAsync(conn =>
        {
            var variants = LearningTargetRepository.GetAnswerVariants(conn, targetId);
            Assert.AreEqual(2, variants.Count);
            return true;
        });
    }

    [TestMethod]
    public async Task DuplicateNormalizedAnswer_RejectsAndDoesNotDuplicate()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var (wordId, senseId, targetId, _) = await SeedWordSenseAndTargetAsync(database);
        var service = new VocabularyCorrectionService(database);

        // Existing variant has DisplayText "Bank", NormalizedText "bank"
        // Try to add "  bank  "
        var dupExisting = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, senseId, targetId, "  bank  "));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.DuplicateAnswer, dupExisting.Status);
        Assert.IsTrue(dupExisting.IsDuplicate);

        // Try to add "BANK"
        var dupCase = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, senseId, targetId, "BANK"));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.DuplicateAnswer, dupCase.Status);

        // Add a new distinct alias
        var distinctResult = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, senseId, targetId, "Geldinstitut"));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.Success, distinctResult.Status);

        // Try to add the same newly added alias again
        var dupNew = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, senseId, targetId, "  geldinstitut  "));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.DuplicateAnswer, dupNew.Status);

        // Verify exactly 2 variants exist
        await database.RunInTransactionAsync(conn =>
        {
            var variants = LearningTargetRepository.GetAnswerVariants(conn, targetId);
            Assert.AreEqual(2, variants.Count);
            return true;
        });
    }

    [TestMethod]
    public async Task InvalidAnswerLanguageAssociation_ReturnsLanguageMismatch()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        // Target has TargetLanguage = "de"
        var (wordId, senseId, targetId, _) = await SeedWordSenseAndTargetAsync(database);
        var service = new VocabularyCorrectionService(database);

        // Mismatched expected language "en" != "de"
        var mismatchResult = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, senseId, targetId, "Geldinstitut", expectedLanguage: "en"));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.InvalidAnswerLanguage, mismatchResult.Status);
        Assert.IsTrue(mismatchResult.IsValidationFailure);

        // Matching language "de" succeeds
        var matchResult = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, senseId, targetId, "Geldinstitut", expectedLanguage: "de"));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.Success, matchResult.Status);

        // Case-insensitive match "DE" succeeds
        var caseMatchResult = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, senseId, targetId, "Kreditinstitut", expectedLanguage: "DE"));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.Success, caseMatchResult.Status);
    }

    [TestMethod]
    public async Task ActiveWorkflowConflict_FailsClosedWithoutMutation()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var (wordId, senseId, targetId, _) = await SeedWordSenseAndTargetAsync(database);
        var service = new VocabularyCorrectionService(database);

        // 1. Test active ReviewSession blocks
        await database.RunInTransactionAsync(conn =>
        {
            conn.Execute(
                """
                INSERT INTO Documents (Id, Title, TextLanguage, ExplanationLanguage, LookupMode, TargetLanguage, Content, ContentFingerprint, ImportedAt, WordCount)
                VALUES (1, 'Test Doc', 'en', 'de', 0, '', 'Test content', 'fp-test', ?, 2)
                """,
                Schema13TimestampCodec.FormatUtc(BaseTimeUtc));

            conn.Insert(new ReviewSessionEntity
            {
                DocumentId = 1,
                Status = ReviewSessionStatus.Active,
                StartedAt = BaseTimeUtc,
                TotalCandidates = 10
            });
            return true;
        });

        var reviewBlocked = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, senseId, targetId, "Geldinstitut"));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.BlockedByActiveWorkflow, reviewBlocked.Status);
        Assert.IsTrue(reviewBlocked.IsBlockedByActiveWorkflow);

        // Clear review session
        await database.RunInTransactionAsync(conn =>
        {
            conn.Execute("DELETE FROM ReviewSessions");
            return true;
        });

        // 2. Test active PreparationSession blocks
        await database.RunInTransactionAsync(conn =>
        {
            conn.Insert(new PreparationSessionEntity
            {
                Status = PreparationSessionStatus.Active,
                StartedAtUtc = BaseTimeUtc,
                UpdatedAtUtc = BaseTimeUtc,
                TotalItems = 10,
                CompletedItems = 0
            });
            return true;
        });

        var prepBlocked = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, senseId, targetId, "Geldinstitut"));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.BlockedByActiveWorkflow, prepBlocked.Status);

        // Clear preparation session
        await database.RunInTransactionAsync(conn =>
        {
            conn.Execute("DELETE FROM PreparationSessions");
            return true;
        });

        // 3. Test active LearningSession blocks
        await database.RunInTransactionAsync(conn =>
        {
            conn.Execute(
                """
                INSERT INTO LearningSessions (StableId, Status, StartedAtUtc, UpdatedAtUtc, TotalCards, CompletedCards)
                VALUES (?, ?, ?, ?, 5, 0)
                """,
                Guid.NewGuid().ToString("N"),
                (int)LearningSessionStatus.Active,
                Schema13TimestampCodec.FormatUtc(BaseTimeUtc),
                Schema13TimestampCodec.FormatUtc(BaseTimeUtc));
            return true;
        });

        var learnBlocked = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, senseId, targetId, "Geldinstitut"));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.BlockedByActiveWorkflow, learnBlocked.Status);

        // Clear learning session and verify success
        await database.RunInTransactionAsync(conn =>
        {
            conn.Execute("DELETE FROM LearningSessions");
            return true;
        });

        var clearedResult = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, senseId, targetId, "Geldinstitut"));
        Assert.AreEqual(AddAcceptedAnswerVariantStatus.Success, clearedResult.Status);
    }

    [TestMethod]
    public async Task PreserveExistingStateWithoutModification_FsrsReviewsControlsMeaningsUnchanged()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var (wordId, senseId, targetId, preferredVariantId) = await SeedWordSenseAndTargetAsync(database);

        // Seed explicit FSRS state, review history, learning control, and meaning
        var originalFsrsState = new FsrsStateSnapshot(
            State: Fsrs6CardState.Review,
            Stability: 4.5,
            Difficulty: 2.3,
            StepIndex: null,
            DueAtUtc: BaseTimeUtc.AddDays(3),
            LastReviewedAtUtc: BaseTimeUtc.AddDays(-1));

        await database.RunInTransactionAsync(conn =>
        {
            conn.Execute(
                """
                UPDATE TargetFsrsStates
                SET State = ?, Stability = ?, Difficulty = ?, StepIndex = ?, DueAtUtc = ?, LastReviewedAtUtc = ?
                WHERE TargetId = ?
                """,
                (int)originalFsrsState.State,
                originalFsrsState.Stability,
                originalFsrsState.Difficulty,
                originalFsrsState.StepIndex,
                Schema13TimestampCodec.FormatUtc(originalFsrsState.DueAtUtc!.Value),
                Schema13TimestampCodec.FormatUtc(originalFsrsState.LastReviewedAtUtc!.Value),
                targetId);

            var sessionStableId = Guid.NewGuid().ToString("N");
            conn.Execute(
                """
                INSERT INTO LearningSessions (StableId, Status, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc, TotalCards, CompletedCards)
                VALUES (?, ?, ?, ?, ?, 1, 1)
                """,
                sessionStableId,
                (int)LearningSessionStatus.Completed,
                Schema13TimestampCodec.FormatUtc(BaseTimeUtc.AddDays(-2)),
                Schema13TimestampCodec.FormatUtc(BaseTimeUtc.AddDays(-1)),
                Schema13TimestampCodec.FormatUtc(BaseTimeUtc.AddDays(-1)));
            var sessionId = conn.ExecuteScalar<int>("SELECT last_insert_rowid()");

            conn.Execute(
                """
                INSERT INTO TargetReviews
                    (StableId, TargetId, SessionId, Rating, WasTypedAnswer, WasCorrect, IsSessionRepeat,
                     TargetAnswerVariantId, MatchedAnswerVariantId, ReviewedAtUtc, DueAtUtc)
                VALUES ('trv-test-1', ?, ?, 2, 1, 1, 0, ?, ?, ?, ?)
                """,
                targetId,
                sessionId,
                preferredVariantId,
                preferredVariantId,
                Schema13TimestampCodec.FormatUtc(BaseTimeUtc.AddDays(-1)),
                Schema13TimestampCodec.FormatUtc(originalFsrsState.DueAtUtc!.Value));

            conn.Execute(
                """
                INSERT INTO TargetFsrsReviewHistoryEntries (StableId, TargetId, SequenceNumber, Rating, ReviewedAtUtc)
                VALUES ('rh-test-1', ?, 1, 2, ?)
                """,
                targetId,
                Schema13TimestampCodec.FormatUtc(BaseTimeUtc.AddDays(-1)));

            conn.Execute(
                """
                INSERT INTO WordLearningControls (WordId, DecidedAtUtc)
                VALUES (?, ?)
                """,
                wordId,
                Schema13TimestampCodec.FormatUtc(BaseTimeUtc));

            return true;
        });

        var service = new VocabularyCorrectionService(database);
        var result = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, senseId, targetId, "Geldinstitut"));

        Assert.IsTrue(result.IsSuccess);

        // Verify FSRS state unchanged
        await database.RunInTransactionAsync(conn =>
        {
            var fsrsRow = conn.Query<TargetFsrsStateRow>(
                "SELECT State, Stability, Difficulty, StepIndex, DueAtUtc, LastReviewedAtUtc FROM TargetFsrsStates WHERE TargetId = ?",
                targetId).Single();

            Assert.AreEqual((int)originalFsrsState.State, fsrsRow.State);
            Assert.AreEqual(originalFsrsState.Stability, fsrsRow.Stability, 0.001);
            Assert.AreEqual(originalFsrsState.Difficulty, fsrsRow.Difficulty, 0.001);
            Assert.AreEqual(originalFsrsState.StepIndex, fsrsRow.StepIndex);
            Assert.AreEqual(Schema13TimestampCodec.FormatUtc(originalFsrsState.DueAtUtc!.Value), fsrsRow.DueAtUtc);
            Assert.AreEqual(Schema13TimestampCodec.FormatUtc(originalFsrsState.LastReviewedAtUtc!.Value), fsrsRow.LastReviewedAtUtc);

            // Review counts unchanged
            var reviewCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM TargetReviews WHERE TargetId = ?", targetId);
            Assert.AreEqual(1, reviewCount);

            var historyCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM TargetFsrsReviewHistoryEntries WHERE TargetId = ?", targetId);
            Assert.AreEqual(1, historyCount);

            // Word learning control unchanged
            var wordControlCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM WordLearningControls WHERE WordId = ?", wordId);
            Assert.AreEqual(1, wordControlCount);

            // Original preferred variant unchanged
            var preferredVariant = conn.Query<VariantRow>(
                "SELECT DisplayText, Requirement, IsPreferred, RequiredSinceUtc, SourceMeaningId FROM TargetAnswerVariants WHERE Id = ?",
                preferredVariantId).Single();
            Assert.AreEqual("Bank", preferredVariant.DisplayText);
            Assert.AreEqual((int)AnswerVariantRequirement.Required, preferredVariant.Requirement);
            Assert.AreEqual(1, preferredVariant.IsPreferred);
            Assert.IsNotNull(preferredVariant.RequiredSinceUtc);
            Assert.IsNotNull(preferredVariant.SourceMeaningId);

            // Meaning row unchanged
            var meaningCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Meanings WHERE SenseId = ?", senseId);
            Assert.AreEqual(1, meaningCount);

            return true;
        });
    }

    [TestMethod]
    public async Task LearningEligibilityAndRequiredAnswerSemantics_RemainUnchanged()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var (wordId, senseId, targetId, preferredVariantId) = await SeedWordSenseAndTargetAsync(database);
        var service = new VocabularyCorrectionService(database);

        var result = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, senseId, targetId, "Geldinstitut"));
        Assert.IsTrue(result.IsSuccess);

        await database.RunInTransactionAsync(conn =>
        {
            // Only the original variant is Required
            var requiredVariants = LearningTargetRepository.GetRequiredVariants(conn, targetId);
            Assert.AreEqual(1, requiredVariants.Count);
            Assert.AreEqual(preferredVariantId, requiredVariants[0].Id);

            // Only the original variant is Preferred
            var preferred = LearningTargetRepository.GetPreferredVariant(conn, targetId);
            Assert.IsNotNull(preferred);
            Assert.AreEqual(preferredVariantId, preferred.Id);

            // The new variant is AcceptedOnly
            var allVariants = LearningTargetRepository.GetAnswerVariants(conn, targetId);
            var accepted = allVariants.First(v => v.Id == result.Variant!.Id);
            Assert.AreEqual(AnswerVariantRequirement.AcceptedOnly, accepted.Requirement);
            Assert.IsFalse(accepted.IsPreferred);
            Assert.IsNull(accepted.RequiredSinceUtc);

            return true;
        });
    }

    [TestMethod]
    public async Task ArchiveV4SnapshotAndExportRestore_IntegrityRemainsValid()
    {
        await using var database = new ProductionInitializedDatabase();
        await database.InitializeAsync();

        var (wordId, senseId, targetId, _) = await SeedWordSenseAndTargetAsync(database);
        var service = new VocabularyCorrectionService(database);

        var addResult = await service.AddAcceptedAnswerVariantAsync(
            new AddAcceptedAnswerVariantRequest(wordId, senseId, targetId, "Geldinstitut"));
        Assert.IsTrue(addResult.IsSuccess);

        // Capture Schema-14 portable snapshot
        Schema14BackupSnapshot? snapshot = null;
        await database.RunInTransactionAsync(conn =>
        {
            snapshot = Schema14BackupSnapshotRepository.CapturePortableSnapshot(conn);
            return true;
        });

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(1, snapshot.BaseSnapshot.Words.Count);
        Assert.AreEqual(1, snapshot.BaseSnapshot.Senses.Count);
        Assert.AreEqual(1, snapshot.LearningTargets.Count);
        Assert.AreEqual(2, snapshot.TargetAnswerVariants.Count);

        var addedVariantSnapshot = snapshot.TargetAnswerVariants.FirstOrDefault(v => v.DisplayText == "Geldinstitut");
        Assert.IsNotNull(addedVariantSnapshot);
        Assert.AreEqual(AnswerVariantRequirement.AcceptedOnly, addedVariantSnapshot.Requirement);
        Assert.IsFalse(addedVariantSnapshot.IsPreferred);
        Assert.IsNull(addedVariantSnapshot.RequiredSinceUtc);
        Assert.IsNull(addedVariantSnapshot.SourceMeaningId);

        // Roundtrip through Archive V4 writer and reader
        var payloadV4 = BackupModelMapperV4.MapToExternal(snapshot);
        using var stream = new MemoryStream();
        await BackupArchiveWriterV4.WriteArchiveAsync(
            payloadV4, new FakePlatformInfo(), DateTime.UtcNow, stream, CancellationToken.None);

        stream.Position = 0;
        var envelope = await BackupArchiveReader.ValidateVersionedAsync(stream, CancellationToken.None);
        Assert.IsNotNull(envelope);
        Assert.AreEqual(4, envelope.FormatVersion);
        Assert.IsNotNull(envelope.V4);

        var readVariants = envelope.V4.Payload.TargetAnswerVariants;
        Assert.AreEqual(2, readVariants.Count);
        var readAdded = readVariants.FirstOrDefault(v => v.DisplayText == "Geldinstitut");
        Assert.IsNotNull(readAdded);
        Assert.AreEqual(BackupAnswerVariantRequirement.AcceptedOnly, readAdded.Requirement);
        Assert.IsFalse(readAdded.IsPreferred);
        Assert.IsNull(readAdded.RequiredSinceUtc);
        Assert.IsNull(readAdded.SourceMeaningId);
    }

    private static async Task<(int WordId, int SenseId, int TargetId, int PreferredVariantId)> SeedWordSenseAndTargetAsync(
        IKnownFirstDatabase database,
        string term = "bank")
    {
        return await database.RunInTransactionAsync(conn =>
        {
            var wordId = InsertWord(
                conn,
                canonicalTerm: term,
                normalizedTerm: "w:" + term.ToLowerInvariant(),
                language: "en",
                tokenKind: TokenKind.Word,
                status: WordStatus.UnknownBacklog,
                totalOccurrences: 5,
                documentCount: 1,
                createdAt: BaseTimeUtc,
                updatedAt: BaseTimeUtc);

            var senseId = InsertSense(
                conn,
                wordId: wordId,
                stableId: "sense-" + term,
                sourceLanguage: "en",
                explanationLanguage: "de",
                providerSenseId: "wikt-en-" + term + "-1",
                topicOrDomain: "Finance",
                partOfSpeech: "Noun",
                grammaticalRelationship: "",
                acronymExpansion: "",
                defaultMeaningId: null,
                status: SenseStatus.Learning,
                createdAtUtc: BaseOffsetUtc,
                updatedAtUtc: BaseOffsetUtc);

            var meaningId = InsertMeaning(
                conn,
                wordId: wordId,
                senseId: senseId,
                stableId: "meaning-" + term + "-primary",
                sourceLanguage: "en",
                explanationLanguage: "de",
                displayTerm: term,
                encounteredSurfaceForm: term,
                grammaticalRelationship: "",
                tokenKind: TokenKind.Word,
                selectedMeaningId: "wikt-en-" + term + "-1",
                acronymExpansion: "",
                definition: "A financial institution that accepts deposits.",
                translation: "Bank",
                dictionaryExample: "Money in the bank.",
                additionalNote: "",
                acceptedAliasesJson: "[]",
                source: "Wiktionary",
                sourceProject: "en.wiktionary.org",
                sourcePageTitle: term,
                sourceRevisionId: 100L,
                attribution: "Wiktionary contributors",
                confirmedByUser: true,
                createdAt: BaseTimeUtc,
                updatedAt: BaseTimeUtc,
                preparedAt: BaseTimeUtc);

            conn.Execute("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", meaningId, senseId);

            var targetId = InsertLearningTarget(
                conn,
                senseId: senseId,
                stableId: "target-" + term + "-trn",
                targetKind: LearningTargetKind.Translation,
                sourceLanguage: "en",
                targetLanguage: "de",
                typingOptOut: false,
                createdAtUtc: BaseOffsetUtc,
                updatedAtUtc: BaseOffsetUtc);

            var preferredVariantId = InsertAnswerVariant(
                conn,
                targetId: targetId,
                stableId: "var-" + term + "-preferred",
                answerLanguage: "de",
                displayText: "Bank",
                normalizedText: "bank",
                requirement: AnswerVariantRequirement.Required,
                isPreferred: true,
                requiredSinceUtc: BaseOffsetUtc,
                sourceMeaningId: meaningId,
                createdAtUtc: BaseOffsetUtc,
                updatedAtUtc: BaseOffsetUtc);

            return (wordId, senseId, targetId, preferredVariantId);
        });
    }

    private static int InsertWord(
        SQLiteConnection connection,
        string canonicalTerm,
        string normalizedTerm,
        string language,
        TokenKind tokenKind,
        WordStatus status,
        int totalOccurrences,
        int documentCount,
        DateTime createdAt,
        DateTime updatedAt,
        PreparationState preparationState = PreparationState.Unprepared)
    {
        connection.Execute(
            """
            INSERT INTO Words
                (Language, CanonicalTerm, NormalizedTerm, Status, TokenKind, PreparationState,
                 TotalOccurrenceCount, DocumentCount, AutomaticInteractionMode, ConsecutiveRecallSuccessCount,
                 ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, MasteryReviewExtensionScheduled,
                 CreatedAt, UpdatedAt)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, 0, 0, 0, 0, 0, ?, ?)
            """,
            language,
            canonicalTerm,
            normalizedTerm,
            (int)status,
            (int)tokenKind,
            (int)preparationState,
            totalOccurrences,
            documentCount,
            Schema13TimestampCodec.FormatUtc(createdAt),
            Schema13TimestampCodec.FormatUtc(updatedAt));

        return connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }

    private static int InsertSense(
        SQLiteConnection connection,
        int wordId,
        string stableId,
        string sourceLanguage,
        string explanationLanguage,
        string providerSenseId,
        string topicOrDomain,
        string partOfSpeech,
        string grammaticalRelationship,
        string acronymExpansion,
        int? defaultMeaningId,
        SenseStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        connection.Execute(
            """
            INSERT INTO Senses
                (WordId, StableId, SourceLanguage, ExplanationLanguage, ProviderSenseId,
                 TopicOrDomain, PartOfSpeech, GrammaticalRelationship, AcronymExpansion,
                 DefaultMeaningId, Status, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            wordId,
            stableId,
            sourceLanguage,
            explanationLanguage,
            providerSenseId,
            topicOrDomain,
            partOfSpeech,
            grammaticalRelationship,
            acronymExpansion,
            defaultMeaningId,
            (int)status,
            Schema13TimestampCodec.FormatUtc(createdAtUtc),
            Schema13TimestampCodec.FormatUtc(updatedAtUtc));

        return connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }

    private static int InsertMeaning(
        SQLiteConnection connection,
        int wordId,
        int senseId,
        string stableId,
        string sourceLanguage,
        string explanationLanguage,
        string displayTerm,
        string encounteredSurfaceForm,
        string grammaticalRelationship,
        TokenKind tokenKind,
        string selectedMeaningId,
        string acronymExpansion,
        string definition,
        string translation,
        string dictionaryExample,
        string additionalNote,
        string acceptedAliasesJson,
        string source,
        string sourceProject,
        string sourcePageTitle,
        long? sourceRevisionId,
        string attribution,
        bool confirmedByUser,
        DateTime createdAt,
        DateTime updatedAt,
        DateTime preparedAt)
    {
        connection.Execute(
            """
            INSERT INTO Meanings
                (SenseId, WordId, StableId, SourceLanguage, ExplanationLanguage,
                 DisplayTerm, EncounteredSurfaceForm, GrammaticalRelationship, TokenKind,
                 SelectedMeaningId, AcronymExpansion, Definition, Translation,
                 DictionaryExample, AdditionalNote, AcceptedAliasesJson, Source,
                 SourceProject, SourcePageTitle, SourceRevisionId, Attribution,
                 ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            senseId,
            wordId,
            stableId,
            sourceLanguage,
            explanationLanguage,
            displayTerm,
            encounteredSurfaceForm,
            grammaticalRelationship,
            (int)tokenKind,
            selectedMeaningId,
            acronymExpansion,
            definition,
            translation,
            dictionaryExample,
            additionalNote,
            acceptedAliasesJson,
            source,
            sourceProject,
            sourcePageTitle,
            sourceRevisionId,
            attribution,
            confirmedByUser ? 1 : 0,
            Schema13TimestampCodec.FormatUtc(createdAt),
            Schema13TimestampCodec.FormatUtc(updatedAt),
            Schema13TimestampCodec.FormatUtc(preparedAt));

        return connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }

    private static int InsertLearningTarget(
        SQLiteConnection connection,
        int senseId,
        string stableId,
        LearningTargetKind targetKind,
        string sourceLanguage,
        string targetLanguage,
        bool typingOptOut,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        connection.Execute(
            """
            INSERT INTO LearningTargets
                (StableId, SenseId, TargetKind, SourceLanguage, TargetLanguage, TypingOptOut, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """,
            stableId,
            senseId,
            (int)targetKind,
            sourceLanguage,
            targetLanguage,
            typingOptOut ? 1 : 0,
            Schema13TimestampCodec.FormatUtc(createdAtUtc),
            Schema13TimestampCodec.FormatUtc(updatedAtUtc));

        var targetId = connection.ExecuteScalar<int>("SELECT last_insert_rowid()");

        if (TargetPersistenceShapeValidator.TableExists(connection, TargetPersistenceDdl.TargetFsrsStatesTableName))
        {
            TargetFsrsStateRepository.InsertCleanNewState(connection, targetId);
        }

        return targetId;
    }

    private static int InsertAnswerVariant(
        SQLiteConnection connection,
        int targetId,
        string stableId,
        string answerLanguage,
        string displayText,
        string normalizedText,
        AnswerVariantRequirement requirement,
        bool isPreferred,
        DateTimeOffset? requiredSinceUtc,
        int? sourceMeaningId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        connection.Execute(
            """
            INSERT INTO TargetAnswerVariants
                (StableId, TargetId, AnswerLanguage, DisplayText, NormalizedText, Requirement, IsPreferred,
                 RequiredSinceUtc, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            stableId,
            targetId,
            answerLanguage,
            displayText,
            normalizedText,
            (int)requirement,
            isPreferred ? 1 : 0,
            requiredSinceUtc.HasValue ? Schema13TimestampCodec.FormatUtc(requiredSinceUtc.Value) : null,
            sourceMeaningId,
            Schema13TimestampCodec.FormatUtc(createdAtUtc),
            Schema13TimestampCodec.FormatUtc(updatedAtUtc));

        return connection.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }

    private sealed record FsrsStateSnapshot(
        Fsrs6CardState State,
        double Stability,
        double Difficulty,
        int? StepIndex,
        DateTime? DueAtUtc,
        DateTime? LastReviewedAtUtc);

    private sealed class TargetFsrsStateRow
    {
        public int State { get; set; }
        public double Stability { get; set; }
        public double Difficulty { get; set; }
        public int? StepIndex { get; set; }
        public string? DueAtUtc { get; set; }
        public string? LastReviewedAtUtc { get; set; }
    }

    private sealed class VariantRow
    {
        public string DisplayText { get; set; } = string.Empty;
        public int Requirement { get; set; }
        public int IsPreferred { get; set; }
        public string? RequiredSinceUtc { get; set; }
        public int? SourceMeaningId { get; set; }
    }

    private sealed class FakePlatformInfo : IBackupPlatformInfo
    {
        public BackupSourcePlatform SourcePlatform => BackupSourcePlatform.Windows;
        public string SourceAppVersion => "1.0.0-test";
    }
}
