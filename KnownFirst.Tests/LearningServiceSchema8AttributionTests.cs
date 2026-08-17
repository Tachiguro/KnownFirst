using System.Security.Cryptography;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Core.Text;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema8;
using KnownFirst.Models;
using KnownFirst.Services;
using KnownFirst.Services.Study;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LearningServiceSchema8AttributionTests
{
    private static readonly DateTime Now = new(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task Capability_Schema8ValidShape_UsesSchema8LearningPath()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(fixture, "capability-term", "capability-answer");
        await fixture.MigrateToSchema8Async();

        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var before = await CaptureLearningStateAsync(fixture, graph.QueueItemId, graph.CardId, graph.SessionId);
        var capability = await database.RunInTransactionAsync(LearningSchemaCapability.Resolve);
        var loadResult = await CreateLearningService(database).GetOrStartAsync();

        // Reveal is the closest public operation that loads and validates the complete Schema-8 graph.
        // The legacy path would attempt to read the removed LearningCards.MeaningId column.
        await CreateLearningService(database).RevealAnswerAsync(graph.QueueItemId);
        var after = await CaptureLearningStateAsync(fixture, graph.QueueItemId, graph.CardId, graph.SessionId);

        Assert.AreEqual(8, before.UserVersion);
        Assert.IsInstanceOfType<LearningSchema8CapabilityResult>(capability);
        Assert.AreEqual(ValidatedLearningSchema8Capability.SchemaVersion, before.UserVersion);
        Assert.AreEqual(4, before.Schema8TableCount);
        Assert.AreEqual(0, before.LegacyMeaningIdColumnCount);
        Assert.AreEqual(1, before.PreferredMeaningIdColumnCount);
        Assert.AreEqual(1, before.RequiredSinceUtcColumnCount);
        Assert.AreEqual(1, before.SenseDirectionIndexCount);
        Assert.IsNotNull(loadResult.Card);
        Assert.AreEqual(graph.SessionId, loadResult.Card.SessionId);
        Assert.AreEqual(graph.QueueItemId, loadResult.Card.QueueItemId);
        Assert.AreEqual(graph.CardId, loadResult.Card.CardId);
        Assert.AreEqual(graph.WordId, loadResult.Card.WordId);
        Assert.AreEqual(CardDirection.MeaningToTerm, loadResult.Card.Direction);
        Assert.AreEqual(LearningInteractionMode.Reading, loadResult.Card.InteractionMode);
        Assert.IsNull(loadResult.CompletedSummary);
        Assert.AreEqual(graph.QueueItemId, before.QueueItemId);
        Assert.AreEqual(graph.CardId, before.QueueCardId);
        Assert.IsNotNull(before.TargetAnswerVariantId);
        Assert.IsFalse(before.AnswerRevealed);
        Assert.IsTrue(after.AnswerRevealed);
        Assert.AreEqual(before.UserVersion, after.UserVersion);
        Assert.AreEqual(before.SchemaVersion, after.SchemaVersion);
        Assert.AreEqual(before.TargetAnswerVariantId, after.TargetAnswerVariantId);
        Assert.AreEqual(before.AnswerVariantCount, after.AnswerVariantCount);
        Assert.AreEqual(before.AssignmentCount, after.AssignmentCount);
        Assert.AreEqual(before.ReviewCount, after.ReviewCount);
        Assert.AreEqual(before.ProgressFingerprint, after.ProgressFingerprint);
        Assert.AreEqual(before.CardScheduleFingerprint, after.CardScheduleFingerprint);
        Assert.AreEqual(before.QueueCompletionFingerprint, after.QueueCompletionFingerprint);
        Assert.AreEqual(before.SessionCounterFingerprint, after.SessionCounterFingerprint);
    }

    [TestMethod]
    public async Task Capability_UserVersion8WithInvalidShape_FailsClosed()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(fixture, "malformed-term", "malformed-answer");
        await fixture.MigrateToSchema8Async();
        await fixture.RebuildAssignmentsTableWithoutRequiredSinceUtcAsync();

        var beforeMetadata = await CaptureShapeMetadataAsync(fixture);
        await fixture.ReopenAsync();
        var beforeDatabaseHash = ComputeSha256(fixture.DatabasePath);

        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var exception = await Assert.ThrowsExactlyAsync<LearningSchemaCapabilityException>(
            () => CreateLearningService(database).GetOrStartAsync());

        await fixture.ReopenAsync();
        var afterDatabaseHash = ComputeSha256(fixture.DatabasePath);
        var afterMetadata = await CaptureShapeMetadataAsync(fixture);

        Assert.IsTrue(exception.ShapeMismatch);
        Assert.AreEqual("learning-schema-capability-shape-mismatch", exception.ErrorCode);
        Assert.AreEqual(8, exception.FoundVersion);
        Assert.Contains("RequiredSinceUtc", exception.ShapeDetail ?? string.Empty);
        Assert.AreEqual(8, beforeMetadata.UserVersion);
        Assert.AreEqual(0, beforeMetadata.RequiredSinceUtcColumnCount);
        Assert.AreEqual(beforeMetadata, afterMetadata);
        Assert.AreEqual(beforeDatabaseHash, afterDatabaseHash);
        Assert.AreEqual(graph.CardId, afterMetadata.CardId);
        Assert.AreEqual(graph.SessionId, afterMetadata.SessionId);
        Assert.AreEqual(graph.QueueItemId, afterMetadata.QueueItemId);
        Assert.AreEqual(0, afterMetadata.ReviewCount);
        Assert.AreEqual(0, afterMetadata.ProgressCount);
    }

    [TestMethod]
    public async Task LoadAsync_Schema8_ReadsTargetAnswerVariantIdFromRawQueueColumn()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(fixture, "legacy-preferred-term", "legacy-preferred-answer");
        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        const int nonTargetVariantId = 701;
        const int rawTargetVariantId = 703;
        await fixture.InsertAnswerVariantAsync(
            senseId, "alphabetically-first-answer", answerLanguage: "en", id: nonTargetVariantId,
            normalizedText: "alphabetically-first-answer");
        await fixture.InsertAnswerVariantAsync(
            senseId, "raw-queue-target-answer", answerLanguage: "en", id: rawTargetVariantId,
            normalizedText: "raw-queue-target-answer");
        await fixture.InsertAssignmentAsync(
            senseId, CardDirection.MeaningToTerm, nonTargetVariantId,
            AnswerVariantRequirement.AcceptedOnly, isPreferred: false, requiredSinceUtc: null,
            createdAtUtc: Now, stableId: "assignment-non-target");
        await fixture.InsertAssignmentAsync(
            senseId, CardDirection.MeaningToTerm, rawTargetVariantId,
            AnswerVariantRequirement.Required, isPreferred: false, requiredSinceUtc: Now,
            createdAtUtc: Now, stableId: "assignment-raw-target");
        await fixture.InsertProgressAsync(
            graph.CardId, rawTargetVariantId, Now,
            interactionMode: LearningInteractionMode.Typing);
        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningSessionCards SET TargetAnswerVariantId = ? WHERE Id = ?",
            rawTargetVariantId, graph.QueueItemId);

        var before = await CaptureLearningStateAsync(fixture, graph.QueueItemId, graph.CardId, graph.SessionId);
        var assignmentsBefore = await fixture.ReadAssignmentsAsync(senseId, CardDirection.MeaningToTerm);
        var preferredVariantId = assignmentsBefore.Single(row => row.IsPreferred).AnswerVariantId;
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);

        // A correct check returns the variant identity consumed from the raw queue target. It does not rate.
        var result = await CreateLearningService(database, LearningMode.Typing).CheckSpellingAsync(
            graph.QueueItemId, "raw-queue-target-answer");
        var after = await CaptureLearningStateAsync(fixture, graph.QueueItemId, graph.CardId, graph.SessionId);

        Assert.AreEqual(8, before.UserVersion);
        Assert.AreEqual(rawTargetVariantId, before.TargetAnswerVariantId);
        Assert.AreNotEqual(rawTargetVariantId, preferredVariantId);
        Assert.AreNotEqual(rawTargetVariantId, assignmentsBefore.Min(row => row.AnswerVariantId));
        Assert.IsGreaterThan(1, assignmentsBefore.Count(row => row.Requirement == AnswerVariantRequirement.Required));
        Assert.IsTrue(result.IsCorrect);
        Assert.IsFalse(result.RatingWasPersisted);
        Assert.AreEqual(rawTargetVariantId, result.MatchedAnswerVariantId);
        Assert.AreEqual("raw-queue-target-answer", result.CorrectAnswer);
        Assert.AreEqual(graph.QueueItemId, after.QueueItemId);
        Assert.AreEqual(graph.CardId, after.QueueCardId);
        Assert.AreEqual(before.TargetAnswerVariantId, after.TargetAnswerVariantId);
        Assert.AreEqual(before.AnswerVariantCount, after.AnswerVariantCount);
        Assert.AreEqual(before.AssignmentCount, after.AssignmentCount);
        Assert.AreEqual(before.ReviewCount, after.ReviewCount);
        Assert.AreEqual(before.ProgressFingerprint, after.ProgressFingerprint);
        Assert.AreEqual(before.CardScheduleFingerprint, after.CardScheduleFingerprint);
        Assert.AreEqual(before.QueueCompletionFingerprint, after.QueueCompletionFingerprint);
        Assert.AreEqual(before.SessionCounterFingerprint, after.SessionCounterFingerprint);
    }

    [TestMethod]
    public async Task LoadAsync_Schema8_MissingTargetAnswerVariantIdFailsClosedWithoutMutation()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(fixture, "missing-target-term", "missing-target-answer");
        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        const int plausibleVariantId = 711;
        await fixture.InsertAnswerVariantAsync(
            senseId, "plausible-required-answer", id: plausibleVariantId,
            normalizedText: "plausible-required-answer", createdAtUtc: Now);
        await fixture.InsertAssignmentAsync(
            senseId, CardDirection.MeaningToTerm, plausibleVariantId,
            AnswerVariantRequirement.Required, isPreferred: false, requiredSinceUtc: Now,
            createdAtUtc: Now, stableId: "assignment-plausible-required");
        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningSessionCards SET TargetAnswerVariantId = NULL WHERE Id = ?", graph.QueueItemId);

        var before = await CapturePersistedStateAsync(fixture);
        var beforeQueue = await ReadQueueRowsAsync(fixture);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => CreateLearningService(database).RevealAnswerAsync(graph.QueueItemId));

        var after = await CapturePersistedStateAsync(fixture);
        var afterQueue = await ReadQueueRowsAsync(fixture);

        Assert.AreEqual(8, before.UserVersion);
        Assert.AreEqual(Schema8LearningDataErrorCode.MissingTarget, exception.Code);
        Assert.IsTrue(exception.IsNonRetryable);
        Assert.HasCount(1, beforeQueue);
        Assert.IsNull(beforeQueue[0].TargetAnswerVariantId);
        Assert.IsNull(afterQueue[0].TargetAnswerVariantId);
        Assert.IsGreaterThan(1, before.AnswerVariantCount);
        Assert.IsGreaterThan(1, before.AssignmentCount);
        Assert.AreEqual(before, after);
        CollectionAssert.AreEqual(beforeQueue, afterQueue);
    }

    [TestMethod]
    public async Task LoadAsync_Schema8_TargetSelectionIsNotInventedByLearningService()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(fixture, "invalid-target-term", "valid-preferred-answer");
        await fixture.MigrateToSchema8Async();

        var currentSenseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        const int plausibleVariantId = 721;
        await fixture.InsertAnswerVariantAsync(
            currentSenseId, "another-valid-current-answer", id: plausibleVariantId,
            normalizedText: "another-valid-current-answer", createdAtUtc: Now);
        await fixture.InsertAssignmentAsync(
            currentSenseId, CardDirection.MeaningToTerm, plausibleVariantId,
            AnswerVariantRequirement.AcceptedOnly, isPreferred: false, requiredSinceUtc: null,
            createdAtUtc: Now, stableId: "assignment-valid-current");

        const int otherSenseId = 820;
        const int outOfScopeTargetId = 821;
        await fixture.InsertSenseAsync(graph.WordId, id: otherSenseId, createdAtUtc: Now, updatedAtUtc: Now);
        await fixture.InsertAnswerVariantAsync(
            otherSenseId, "out-of-scope-answer", id: outOfScopeTargetId,
            normalizedText: "out-of-scope-answer", createdAtUtc: Now);
        await fixture.InsertAssignmentAsync(
            otherSenseId, CardDirection.MeaningToTerm, outOfScopeTargetId,
            AnswerVariantRequirement.Required, isPreferred: true, requiredSinceUtc: Now,
            createdAtUtc: Now, stableId: "assignment-out-of-scope");
        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningSessionCards SET TargetAnswerVariantId = ? WHERE Id = ?",
            outOfScopeTargetId, graph.QueueItemId);

        var before = await CapturePersistedStateAsync(fixture);
        var beforeQueue = await ReadQueueRowsAsync(fixture);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => CreateLearningService(database).RevealAnswerAsync(graph.QueueItemId));

        var after = await CapturePersistedStateAsync(fixture);
        var afterQueue = await ReadQueueRowsAsync(fixture);

        Assert.AreEqual(8, before.UserVersion);
        Assert.AreEqual(Schema8LearningDataErrorCode.InvalidTarget, exception.Code);
        Assert.HasCount(1, beforeQueue);
        Assert.AreEqual(outOfScopeTargetId, beforeQueue[0].TargetAnswerVariantId);
        Assert.AreEqual(outOfScopeTargetId, afterQueue[0].TargetAnswerVariantId);
        Assert.AreNotEqual(
            outOfScopeTargetId,
            (await fixture.ReadAssignmentsAsync(currentSenseId, CardDirection.MeaningToTerm))
                .Single(row => row.IsPreferred).AnswerVariantId);
        Assert.AreEqual(before, after);
        CollectionAssert.AreEqual(beforeQueue, afterQueue);
    }

    [TestMethod]
    public async Task RevealAnswer_AcceptedOnlyTarget_FailsClosedWithoutMutation()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "accepted-only-reveal-term", "accepted-only-reveal-answer");
        await fixture.MigrateToSchema8Async();

        var queueBefore = (await ReadQueueRowsAsync(fixture)).Single();
        var targetVariantId = queueBefore.TargetAnswerVariantId
            ?? throw new AssertFailedException("The migrated queue target is missing.");
        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        await fixture.Connection.ExecuteAsync(
            """
            UPDATE SenseAnswerVariantAssignments
            SET Requirement = ?, RequiredSinceUtc = NULL
            WHERE SenseId = ? AND CardDirection = ? AND AnswerVariantId = ?
            """,
            (int)AnswerVariantRequirement.AcceptedOnly, senseId,
            (int)CardDirection.MeaningToTerm, targetVariantId);

        var targetAssignment = (await fixture.ReadAssignmentsAsync(
            senseId, CardDirection.MeaningToTerm))
            .Single(row => row.AnswerVariantId == targetVariantId);
        var before = await CapturePersistedStateAsync(fixture);
        var beforeDetails = await CapturePersistenceDetailsAsync(fixture);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => CreateLearningService(database, LearningMode.Reading)
                .RevealAnswerAsync(graph.QueueItemId));

        var after = await CapturePersistedStateAsync(fixture);
        var afterDetails = await CapturePersistenceDetailsAsync(fixture);
        var queueAfter = (await ReadQueueRowsAsync(fixture)).Single();

        Assert.AreEqual(Schema8LearningDataErrorCode.InvalidTarget, exception.Code);
        Assert.AreEqual(AnswerVariantRequirement.AcceptedOnly, targetAssignment.Requirement);
        Assert.IsNull(targetAssignment.RequiredSinceUtc);
        Assert.AreEqual(targetVariantId, queueBefore.TargetAnswerVariantId);
        Assert.IsFalse(queueBefore.AnswerRevealed);
        Assert.IsFalse(queueAfter.AnswerRevealed);
        Assert.IsEmpty(beforeDetails.Reviews);
        Assert.IsEmpty(afterDetails.Reviews);
        Assert.AreEqual(before, after);
        CollectionAssert.AreEqual(beforeDetails.Queues, afterDetails.Queues);
        CollectionAssert.AreEqual(beforeDetails.Reviews, afterDetails.Reviews);
        CollectionAssert.AreEqual(beforeDetails.Progress, afterDetails.Progress);
        CollectionAssert.AreEqual(beforeDetails.Cards, afterDetails.Cards);
        CollectionAssert.AreEqual(beforeDetails.Sessions, afterDetails.Sessions);
        CollectionAssert.AreEqual(beforeDetails.Senses, afterDetails.Senses);
        CollectionAssert.AreEqual(beforeDetails.Words, afterDetails.Words);
        CollectionAssert.AreEqual(beforeDetails.Variants, afterDetails.Variants);
        CollectionAssert.AreEqual(beforeDetails.Assignments, afterDetails.Assignments);
    }

    [TestMethod]
    public async Task RevealAnswer_TypingMode_FailsClosedWithoutMutation()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "typing-reveal-term", "typing-reveal-answer");
        await fixture.MigrateToSchema8Async();

        var queueBefore = (await ReadQueueRowsAsync(fixture)).Single();
        var targetVariantId = queueBefore.TargetAnswerVariantId
            ?? throw new AssertFailedException("The migrated queue target is missing.");
        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        var targetAssignment = (await fixture.ReadAssignmentsAsync(
            senseId, CardDirection.MeaningToTerm))
            .Single(row => row.AnswerVariantId == targetVariantId);
        var before = await CapturePersistedStateAsync(fixture);
        var beforeDetails = await CapturePersistenceDetailsAsync(fixture);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var service = CreateLearningService(database, LearningMode.Typing);

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => service.RevealAnswerAsync(graph.QueueItemId));

        var after = await CapturePersistedStateAsync(fixture);
        var afterDetails = await CapturePersistenceDetailsAsync(fixture);
        var queueAfter = (await ReadQueueRowsAsync(fixture)).Single();

        Assert.AreEqual(Schema8LearningDataErrorCode.InvalidQueueState, exception.Code);
        Assert.AreEqual(AnswerVariantRequirement.Required, targetAssignment.Requirement);
        Assert.IsNotNull(targetAssignment.RequiredSinceUtc);
        Assert.AreEqual(CardDirection.MeaningToTerm, targetAssignment.CardDirection);
        Assert.AreEqual(targetVariantId, queueBefore.TargetAnswerVariantId);
        Assert.IsFalse(queueBefore.AnswerRevealed);
        Assert.IsFalse(queueAfter.AnswerRevealed);
        Assert.IsEmpty(beforeDetails.Reviews);
        Assert.IsEmpty(afterDetails.Reviews);
        Assert.AreEqual(before, after);
        CollectionAssert.AreEqual(beforeDetails.Queues, afterDetails.Queues);
        CollectionAssert.AreEqual(beforeDetails.Reviews, afterDetails.Reviews);
        CollectionAssert.AreEqual(beforeDetails.Progress, afterDetails.Progress);
        CollectionAssert.AreEqual(beforeDetails.Cards, afterDetails.Cards);
        CollectionAssert.AreEqual(beforeDetails.Sessions, afterDetails.Sessions);
        CollectionAssert.AreEqual(beforeDetails.Senses, afterDetails.Senses);
        CollectionAssert.AreEqual(beforeDetails.Words, afterDetails.Words);
        CollectionAssert.AreEqual(beforeDetails.Variants, afterDetails.Variants);
        CollectionAssert.AreEqual(beforeDetails.Assignments, afterDetails.Assignments);

        // Make the durable typing flags independently valid, then prove the failed Reveal did not create the
        // instance-local pending evidence that a correct typed rating requires.
        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningSessionCards SET AnswerRevealed = 1, SpellingChecked = 1, SpellingCorrect = 1 WHERE Id = ?",
            graph.QueueItemId);
        var beforeEvidenceProbe = await CapturePersistedStateAsync(fixture);
        var evidenceException = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => service.RateAsync(graph.QueueItemId, ReviewRating.Good));
        var afterEvidenceProbe = await CapturePersistedStateAsync(fixture);

        Assert.AreEqual(Schema8LearningDataErrorCode.MissingMatchEvidence, evidenceException.Code);
        Assert.AreEqual(beforeEvidenceProbe, afterEvidenceProbe);
    }

    [TestMethod]
    public async Task ResumeActiveSession_Schema8_PreservesExistingTargetAnswerVariantId()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var session = await SeedTwoCardSessionAsync(fixture);
        await fixture.MigrateToSchema8Async();

        var initialQueues = await ReadQueueRowsAsync(fixture);
        var targetTexts = await ReadTargetTextsAsync(fixture);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var originalService = CreateLearningService(database, LearningMode.Typing);
        var initialLoad = await originalService.GetOrStartAsync();

        Assert.IsNotNull(initialLoad.Card);
        Assert.AreEqual(session.SessionId, initialLoad.Card.SessionId);
        Assert.AreEqual(initialQueues[0].Id, initialLoad.Card.QueueItemId);
        Assert.AreEqual(initialQueues[0].CardId, initialLoad.Card.CardId);
        Assert.AreEqual(CardDirection.MeaningToTerm, initialLoad.Card.Direction);
        Assert.AreEqual(LearningInteractionMode.Typing, initialLoad.Card.InteractionMode);
        Assert.IsNull(initialLoad.CompletedSummary);
        Assert.HasCount(2, initialQueues);
        Assert.AreEqual(0, initialQueues[0].QueueOrder);
        Assert.AreEqual(1, initialQueues[1].QueueOrder);
        Assert.AreNotEqual(initialQueues[0].TargetAnswerVariantId, initialQueues[1].TargetAnswerVariantId);
        Assert.IsNotNull(initialQueues[0].TargetAnswerVariantId);
        Assert.IsNotNull(initialQueues[1].TargetAnswerVariantId);

        var firstCheck = await originalService.CheckSpellingAsync(
            initialQueues[0].Id, targetTexts[initialQueues[0].Id]);
        Assert.AreEqual(initialQueues[0].TargetAnswerVariantId, firstCheck.MatchedAnswerVariantId);
        await originalService.RateAsync(initialQueues[0].Id, ReviewRating.Good);

        var established = await CapturePersistedStateAsync(fixture);
        var establishedQueues = await ReadQueueRowsAsync(fixture);
        var reconstructedService = CreateLearningService(database, LearningMode.Typing);
        var resumedLoad = await reconstructedService.GetOrStartAsync();
        var afterResume = await CapturePersistedStateAsync(fixture);
        var resumedQueues = await ReadQueueRowsAsync(fixture);

        Assert.IsNotNull(resumedLoad.Card);
        Assert.AreEqual(session.SessionId, resumedLoad.Card.SessionId);
        Assert.AreEqual(establishedQueues[1].Id, resumedLoad.Card.QueueItemId);
        Assert.AreEqual(establishedQueues[1].CardId, resumedLoad.Card.CardId);
        Assert.AreEqual(CardDirection.MeaningToTerm, resumedLoad.Card.Direction);
        Assert.AreEqual(LearningInteractionMode.Typing, resumedLoad.Card.InteractionMode);
        Assert.IsNull(resumedLoad.CompletedSummary);
        Assert.AreEqual(8, afterResume.UserVersion);
        Assert.AreEqual(session.SessionId, established.SessionId);
        Assert.AreEqual(1, established.SessionCount);
        Assert.AreEqual(2, established.QueueCount);
        Assert.IsTrue(establishedQueues[0].IsCompleted);
        Assert.IsFalse(establishedQueues[1].IsCompleted);
        Assert.AreEqual(established, afterResume);
        CollectionAssert.AreEqual(establishedQueues, resumedQueues);

        var completedException = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => reconstructedService.CheckSpellingAsync(
                resumedQueues[0].Id, targetTexts[resumedQueues[0].Id]));
        Assert.AreEqual(Schema8LearningDataErrorCode.DuplicateSubmission, completedException.Code);

        var secondCheck = await reconstructedService.CheckSpellingAsync(
            resumedQueues[1].Id, targetTexts[resumedQueues[1].Id]);
        var afterConsumption = await CapturePersistedStateAsync(fixture);
        var finalQueues = await ReadQueueRowsAsync(fixture);

        Assert.IsTrue(secondCheck.IsCorrect);
        Assert.IsFalse(secondCheck.RatingWasPersisted);
        Assert.AreEqual(resumedQueues[1].TargetAnswerVariantId, secondCheck.MatchedAnswerVariantId);
        Assert.AreEqual(established.ReviewFingerprint, afterConsumption.ReviewFingerprint);
        Assert.AreEqual(established.ProgressFingerprint, afterConsumption.ProgressFingerprint);
        Assert.AreEqual(established.CardFingerprint, afterConsumption.CardFingerprint);
        Assert.AreEqual(established.SessionFingerprint, afterConsumption.SessionFingerprint);
        Assert.AreEqual(established.AssignmentFingerprint, afterConsumption.AssignmentFingerprint);
        Assert.AreEqual(established.AnswerVariantFingerprint, afterConsumption.AnswerVariantFingerprint);
        Assert.AreEqual(established.QueueStructureFingerprint, afterConsumption.QueueStructureFingerprint);
        Assert.AreEqual(established.QueueTargetFingerprint, afterConsumption.QueueTargetFingerprint);
        Assert.AreEqual(initialQueues[0].TargetAnswerVariantId, finalQueues[0].TargetAnswerVariantId);
        Assert.AreEqual(initialQueues[1].TargetAnswerVariantId, finalQueues[1].TargetAnswerVariantId);
        Assert.IsTrue(finalQueues[0].IsCompleted);
        Assert.IsFalse(finalQueues[1].IsCompleted);
        Assert.IsTrue(finalQueues[1].SpellingChecked);
        Assert.IsTrue(finalQueues[1].SpellingCorrect);
    }

    [TestMethod]
    public async Task CheckSpelling_TargetRequiredMatch_ReturnsTargetVariant()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "target-required-term", "migration-required-answer");
        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        const int rawTargetVariantId = 731;
        await fixture.InsertAnswerVariantAsync(
            senseId, "raw-target-required-answer", answerLanguage: "en", id: rawTargetVariantId,
            normalizedText: "raw-target-required-answer", createdAtUtc: Now);
        await fixture.InsertAssignmentAsync(
            senseId, CardDirection.MeaningToTerm, rawTargetVariantId,
            AnswerVariantRequirement.Required, isPreferred: false, requiredSinceUtc: Now,
            createdAtUtc: Now, stableId: "assignment-target-required");
        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningSessionCards SET TargetAnswerVariantId = ? WHERE Id = ?",
            rawTargetVariantId, graph.QueueItemId);

        var assignmentsBefore = await fixture.ReadAssignmentsAsync(
            senseId, CardDirection.MeaningToTerm);
        var before = await CapturePersistedStateAsync(fixture);
        var beforeQueue = await ReadQueueRowsAsync(fixture);
        var beforeProgress = await fixture.ReadProgressAsync();
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);

        var result = await CreateLearningService(database, LearningMode.Typing)
            .CheckSpellingAsync(graph.QueueItemId, "  raw-target-required-answer  ");

        var after = await CapturePersistedStateAsync(fixture);
        var afterQueue = await ReadQueueRowsAsync(fixture);
        var assignmentsAfter = await fixture.ReadAssignmentsAsync(
            senseId, CardDirection.MeaningToTerm);
        var afterProgress = await fixture.ReadProgressAsync();

        Assert.AreEqual(8, before.UserVersion);
        Assert.HasCount(1, beforeQueue);
        Assert.AreEqual(rawTargetVariantId, beforeQueue[0].TargetAnswerVariantId);
        Assert.AreNotEqual(
            rawTargetVariantId, assignmentsBefore.Single(row => row.IsPreferred).AnswerVariantId);
        Assert.AreNotEqual(rawTargetVariantId, assignmentsBefore.Min(row => row.AnswerVariantId));
        Assert.AreEqual(
            AnswerVariantRequirement.Required,
            assignmentsBefore.Single(row => row.AnswerVariantId == rawTargetVariantId).Requirement);
        Assert.IsTrue(result.IsCorrect);
        Assert.IsFalse(result.RatingWasPersisted);
        Assert.AreEqual(rawTargetVariantId, result.MatchedAnswerVariantId);
        Assert.AreEqual("raw-target-required-answer", result.CorrectAnswer);
        Assert.AreEqual(rawTargetVariantId, afterQueue[0].TargetAnswerVariantId);
        Assert.IsFalse(beforeQueue[0].IsCompleted);
        Assert.IsFalse(afterQueue[0].IsCompleted);
        Assert.IsTrue(afterQueue[0].SpellingChecked);
        Assert.IsTrue(afterQueue[0].SpellingCorrect);
        Assert.AreEqual(before.UserVersion, after.UserVersion);
        Assert.AreEqual(before.SchemaVersion, after.SchemaVersion);
        Assert.AreEqual(before.SessionCount, after.SessionCount);
        Assert.AreEqual(before.QueueCount, after.QueueCount);
        Assert.AreEqual(before.AnswerVariantCount, after.AnswerVariantCount);
        Assert.AreEqual(before.AssignmentCount, after.AssignmentCount);
        Assert.AreEqual(before.QueueStructureFingerprint, after.QueueStructureFingerprint);
        Assert.AreEqual(before.QueueTargetFingerprint, after.QueueTargetFingerprint);
        Assert.AreEqual(before.SessionFingerprint, after.SessionFingerprint);
        Assert.AreEqual(before.CardFingerprint, after.CardFingerprint);
        Assert.AreEqual(before.ReviewFingerprint, after.ReviewFingerprint);
        Assert.AreEqual(before.ProgressFingerprint, after.ProgressFingerprint);
        Assert.AreEqual(before.AssignmentFingerprint, after.AssignmentFingerprint);
        Assert.AreEqual(before.AnswerVariantFingerprint, after.AnswerVariantFingerprint);
        Assert.AreEqual(before.SenseFingerprint, after.SenseFingerprint);
        Assert.AreEqual(before.WordFingerprint, after.WordFingerprint);
        Assert.AreEqual(before.SchemaFingerprint, after.SchemaFingerprint);
        Assert.HasCount(assignmentsBefore.Count, assignmentsAfter);
        CollectionAssert.AreEqual(beforeProgress, afterProgress);
    }

    [TestMethod]
    public async Task CheckSpelling_DifferentRequiredMatch_ReturnsMatchedRequiredVariant()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "different-required-term", "queue-target-required-answer");
        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        var targetVariantId = (await ReadQueueRowsAsync(fixture)).Single().TargetAnswerVariantId;
        const int differentRequiredVariantId = 741;
        await fixture.InsertAnswerVariantAsync(
            senseId, "different-required-answer", answerLanguage: "en", id: differentRequiredVariantId,
            normalizedText: "different-required-answer", createdAtUtc: Now);
        await fixture.InsertAssignmentAsync(
            senseId, CardDirection.MeaningToTerm, differentRequiredVariantId,
            AnswerVariantRequirement.Required, isPreferred: false, requiredSinceUtc: Now,
            createdAtUtc: Now, stableId: "assignment-different-required");

        var assignmentsBefore = await fixture.ReadAssignmentsAsync(
            senseId, CardDirection.MeaningToTerm);
        var before = await CapturePersistedStateAsync(fixture);
        var beforeQueue = await ReadQueueRowsAsync(fixture);
        var beforeProgress = await fixture.ReadProgressAsync();
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);

        var result = await CreateLearningService(database, LearningMode.Typing)
            .CheckSpellingAsync(graph.QueueItemId, "different-required-answer");

        var after = await CapturePersistedStateAsync(fixture);
        var afterQueue = await ReadQueueRowsAsync(fixture);
        var assignmentsAfter = await fixture.ReadAssignmentsAsync(
            senseId, CardDirection.MeaningToTerm);
        var afterProgress = await fixture.ReadProgressAsync();

        Assert.AreEqual(8, before.UserVersion);
        Assert.IsNotNull(targetVariantId);
        Assert.AreEqual(targetVariantId, beforeQueue[0].TargetAnswerVariantId);
        Assert.AreNotEqual(differentRequiredVariantId, targetVariantId);
        Assert.AreEqual(
            AnswerVariantRequirement.Required,
            assignmentsBefore.Single(row => row.AnswerVariantId == differentRequiredVariantId).Requirement);
        Assert.IsFalse(
            assignmentsBefore.Single(row => row.AnswerVariantId == differentRequiredVariantId).IsPreferred);
        Assert.IsTrue(result.IsCorrect);
        Assert.IsFalse(result.RatingWasPersisted);
        Assert.AreEqual(differentRequiredVariantId, result.MatchedAnswerVariantId);
        Assert.AreEqual("queue-target-required-answer", result.CorrectAnswer);
        Assert.AreEqual(targetVariantId, afterQueue[0].TargetAnswerVariantId);
        Assert.IsFalse(beforeQueue[0].IsCompleted);
        Assert.IsFalse(afterQueue[0].IsCompleted);
        Assert.IsTrue(afterQueue[0].SpellingChecked);
        Assert.IsTrue(afterQueue[0].SpellingCorrect);
        Assert.AreEqual(before.UserVersion, after.UserVersion);
        Assert.AreEqual(before.SchemaVersion, after.SchemaVersion);
        Assert.AreEqual(before.SessionCount, after.SessionCount);
        Assert.AreEqual(before.QueueCount, after.QueueCount);
        Assert.AreEqual(before.AnswerVariantCount, after.AnswerVariantCount);
        Assert.AreEqual(before.AssignmentCount, after.AssignmentCount);
        Assert.AreEqual(before.QueueStructureFingerprint, after.QueueStructureFingerprint);
        Assert.AreEqual(before.QueueTargetFingerprint, after.QueueTargetFingerprint);
        Assert.AreEqual(before.SessionFingerprint, after.SessionFingerprint);
        Assert.AreEqual(before.CardFingerprint, after.CardFingerprint);
        Assert.AreEqual(before.ReviewFingerprint, after.ReviewFingerprint);
        Assert.AreEqual(before.ProgressFingerprint, after.ProgressFingerprint);
        Assert.AreEqual(before.AssignmentFingerprint, after.AssignmentFingerprint);
        Assert.AreEqual(before.AnswerVariantFingerprint, after.AnswerVariantFingerprint);
        Assert.AreEqual(before.SenseFingerprint, after.SenseFingerprint);
        Assert.AreEqual(before.WordFingerprint, after.WordFingerprint);
        Assert.AreEqual(before.SchemaFingerprint, after.SchemaFingerprint);
        Assert.HasCount(assignmentsBefore.Count, assignmentsAfter);
        CollectionAssert.AreEqual(beforeProgress, afterProgress);
    }

    [TestMethod]
    public async Task CheckSpelling_AcceptedOnlyMatch_IsSemanticallyCorrect()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "accepted-only-term", "queue-target-required-answer");
        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        var targetVariantId = (await ReadQueueRowsAsync(fixture)).Single().TargetAnswerVariantId;
        await fixture.Connection.ExecuteAsync(
            "UPDATE SenseAnswerVariantAssignments SET IsPreferred = 0 WHERE SenseId = ? AND CardDirection = ?",
            senseId, (int)CardDirection.MeaningToTerm);
        const int acceptedOnlyVariantId = 751;
        await fixture.InsertAnswerVariantAsync(
            senseId, "accepted-only-answer", answerLanguage: "en", id: acceptedOnlyVariantId,
            normalizedText: "accepted-only-answer", createdAtUtc: Now);
        await fixture.InsertAssignmentAsync(
            senseId, CardDirection.MeaningToTerm, acceptedOnlyVariantId,
            AnswerVariantRequirement.AcceptedOnly, isPreferred: true, requiredSinceUtc: null,
            createdAtUtc: Now, stableId: "assignment-accepted-only");

        var assignmentsBefore = await fixture.ReadAssignmentsAsync(
            senseId, CardDirection.MeaningToTerm);
        var before = await CapturePersistedStateAsync(fixture);
        var beforeQueue = await ReadQueueRowsAsync(fixture);
        var beforeProgress = await fixture.ReadProgressAsync();
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);

        var result = await CreateLearningService(database, LearningMode.Typing)
            .CheckSpellingAsync(graph.QueueItemId, "accepted-only-answer");

        var after = await CapturePersistedStateAsync(fixture);
        var afterQueue = await ReadQueueRowsAsync(fixture);
        var assignmentsAfter = await fixture.ReadAssignmentsAsync(
            senseId, CardDirection.MeaningToTerm);
        var afterProgress = await fixture.ReadProgressAsync();
        var targetAssignment = assignmentsBefore.Single(row => row.AnswerVariantId == targetVariantId);
        var acceptedAssignment = assignmentsBefore.Single(row => row.AnswerVariantId == acceptedOnlyVariantId);

        Assert.AreEqual(8, before.UserVersion);
        Assert.IsNotNull(targetVariantId);
        Assert.AreEqual(targetVariantId, beforeQueue[0].TargetAnswerVariantId);
        Assert.AreNotEqual(acceptedOnlyVariantId, targetVariantId);
        Assert.AreEqual(AnswerVariantRequirement.Required, targetAssignment.Requirement);
        Assert.IsFalse(targetAssignment.IsPreferred);
        Assert.AreEqual(AnswerVariantRequirement.AcceptedOnly, acceptedAssignment.Requirement);
        Assert.IsTrue(acceptedAssignment.IsPreferred);
        Assert.IsNull(acceptedAssignment.RequiredSinceUtc);
        Assert.IsTrue(result.IsCorrect);
        Assert.IsFalse(result.RatingWasPersisted);
        Assert.AreEqual(acceptedOnlyVariantId, result.MatchedAnswerVariantId);
        Assert.AreEqual("queue-target-required-answer", result.CorrectAnswer);
        Assert.AreEqual(targetVariantId, afterQueue[0].TargetAnswerVariantId);
        Assert.IsFalse(beforeQueue[0].IsCompleted);
        Assert.IsFalse(afterQueue[0].IsCompleted);
        Assert.IsTrue(afterQueue[0].SpellingChecked);
        Assert.IsTrue(afterQueue[0].SpellingCorrect);
        Assert.IsEmpty(beforeProgress);
        Assert.IsEmpty(afterProgress);
        Assert.AreEqual(before.UserVersion, after.UserVersion);
        Assert.AreEqual(before.SchemaVersion, after.SchemaVersion);
        Assert.AreEqual(before.SessionCount, after.SessionCount);
        Assert.AreEqual(before.QueueCount, after.QueueCount);
        Assert.AreEqual(before.AnswerVariantCount, after.AnswerVariantCount);
        Assert.AreEqual(before.AssignmentCount, after.AssignmentCount);
        Assert.AreEqual(before.QueueStructureFingerprint, after.QueueStructureFingerprint);
        Assert.AreEqual(before.QueueTargetFingerprint, after.QueueTargetFingerprint);
        Assert.AreEqual(before.SessionFingerprint, after.SessionFingerprint);
        Assert.AreEqual(before.CardFingerprint, after.CardFingerprint);
        Assert.AreEqual(before.ReviewFingerprint, after.ReviewFingerprint);
        Assert.AreEqual(before.ProgressFingerprint, after.ProgressFingerprint);
        Assert.AreEqual(before.AssignmentFingerprint, after.AssignmentFingerprint);
        Assert.AreEqual(before.AnswerVariantFingerprint, after.AnswerVariantFingerprint);
        Assert.AreEqual(before.SenseFingerprint, after.SenseFingerprint);
        Assert.AreEqual(before.WordFingerprint, after.WordFingerprint);
        Assert.AreEqual(before.SchemaFingerprint, after.SchemaFingerprint);
        Assert.HasCount(assignmentsBefore.Count, assignmentsAfter);
    }

    [TestMethod]
    public async Task CheckSpelling_GloballyExistingUnassignedVariant_IsOrdinaryIncorrect()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "global-unassigned-term", "persisted-required-target");
        await fixture.MigrateToSchema8Async();

        var currentSenseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        const int unrelatedSenseId = 801;
        const int unassignedVariantId = 803;
        await fixture.InsertSenseAsync(
            graph.WordId, id: unrelatedSenseId, sourceLanguage: "en", explanationLanguage: "de",
            createdAtUtc: Now, updatedAtUtc: Now);
        await fixture.InsertAnswerVariantAsync(
            unrelatedSenseId, "global-unassigned-answer", answerLanguage: "en", id: unassignedVariantId,
            normalizedText: "global-unassigned-answer", createdAtUtc: Now);

        var beforeFingerprint = await CapturePersistedStateAsync(fixture);
        var before = await CapturePersistenceDetailsAsync(fixture);
        var targetVariantId = before.Queues.Single().TargetAnswerVariantId;
        var targetAssignment = before.Assignments.Single(row =>
            row.SenseId == currentSenseId
            && row.CardDirection == CardDirection.MeaningToTerm
            && row.AnswerVariantId == targetVariantId);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);

        var result = await CreateLearningService(database, LearningMode.Typing)
            .CheckSpellingAsync(graph.QueueItemId, "  GLOBAL-UNASSIGNED-ANSWER  ");

        var afterFingerprint = await CapturePersistedStateAsync(fixture);
        var after = await CapturePersistenceDetailsAsync(fixture);
        var review = after.Reviews.Single();
        var targetProgress = after.Progress.Single();
        var originalQueue = after.Queues.Single(row => row.Id == graph.QueueItemId);
        var repeatQueue = after.Queues.Single(row => row.Id != graph.QueueItemId);
        var card = after.Cards.Single(row => row.Id == graph.CardId);
        var session = after.Sessions.Single(row => row.Id == graph.SessionId);

        Assert.AreEqual(8, before.UserVersion);
        Assert.AreEqual(8, after.UserVersion);
        Assert.IsNotNull(targetVariantId);
        Assert.AreEqual(AnswerVariantRequirement.Required, targetAssignment.Requirement);
        Assert.AreEqual(
            unrelatedSenseId,
            before.Variants.Single(row => row.Id == unassignedVariantId).SenseId);
        Assert.IsFalse(before.Assignments.Any(row => row.AnswerVariantId == unassignedVariantId));
        Assert.IsFalse(result.IsCorrect);
        Assert.IsNull(result.MatchedAnswerVariantId);
        Assert.IsTrue(result.RatingWasPersisted);
        Assert.AreEqual("persisted-required-target", result.CorrectAnswer);
        Assert.IsEmpty(before.Reviews);
        Assert.HasCount(1, after.Reviews);
        Assert.AreEqual(graph.CardId, review.CardId);
        Assert.AreEqual(graph.SessionId, review.SessionId);
        Assert.AreEqual(ReviewRating.Again, review.Rating);
        Assert.IsTrue(review.WasTypedAnswer);
        Assert.IsFalse(review.WasCorrect);
        Assert.AreEqual(targetVariantId, review.TargetAnswerVariantId);
        Assert.IsNull(review.MatchedAnswerVariantId);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, review.ReviewedAtUtc));
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now.AddMinutes(10), review.DueAtUtc));
        Assert.AreEqual(0, review.IntervalDays);
        Assert.AreEqual(SimpleSpacedRepetitionScheduler.DefaultEaseFactor, review.EaseFactor);
        Assert.IsEmpty(before.Progress);
        Assert.HasCount(1, after.Progress);
        Assert.AreEqual(graph.CardId, targetProgress.CardId);
        Assert.AreEqual(targetVariantId, targetProgress.AnswerVariantId);
        Assert.AreEqual(LearningInteractionMode.Reading, targetProgress.InteractionMode);
        Assert.AreEqual(0, targetProgress.ConsecutiveReadingSuccessCount);
        Assert.AreEqual(0, targetProgress.ConsecutiveTypingSuccessCount);
        Assert.AreEqual(1, targetProgress.ConsecutiveTypingFailureCount);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, targetProgress.LastAssessedAtUtc));
        Assert.IsFalse(targetProgress.MasteryReviewExtensionScheduled);
        Assert.IsFalse(targetProgress.IsMastered);
        Assert.AreEqual(Schema8LearningReviewReplayPolicy.ReplayVersion, targetProgress.ReplayVersion);
        Assert.IsTrue(Schema8Utc.AreSameInstant(targetAssignment.RequiredSinceUtc, targetProgress.CreatedAtUtc));
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, targetProgress.UpdatedAtUtc));
        Assert.IsFalse(after.Progress.Any(row => row.AnswerVariantId == unassignedVariantId));
        Assert.HasCount(1, before.Queues);
        Assert.HasCount(2, after.Queues);
        Assert.AreEqual(targetVariantId, originalQueue.TargetAnswerVariantId);
        Assert.IsTrue(originalQueue.AnswerRevealed);
        Assert.IsTrue(originalQueue.SpellingChecked);
        Assert.IsFalse(originalQueue.SpellingCorrect);
        Assert.IsTrue(originalQueue.IsCompleted);
        Assert.AreEqual(ReviewRating.Again, originalQueue.Rating);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, originalQueue.CompletedAtUtc));
        Assert.AreEqual(targetVariantId, repeatQueue.TargetAnswerVariantId);
        Assert.AreEqual(1, repeatQueue.QueueOrder);
        Assert.IsFalse(repeatQueue.IsDueCard);
        Assert.IsTrue(repeatQueue.IsAgainRepeat);
        Assert.IsFalse(repeatQueue.AnswerRevealed);
        Assert.IsFalse(repeatQueue.SpellingChecked);
        Assert.IsFalse(repeatQueue.SpellingCorrect);
        Assert.IsFalse(repeatQueue.IsCompleted);
        Assert.IsNull(repeatQueue.Rating);
        Assert.IsNull(repeatQueue.CompletedAtUtc);
        Assert.AreEqual(CardState.Learning, card.State);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now.AddMinutes(10), card.DueAtUtc));
        Assert.AreEqual(0, card.IntervalDays);
        Assert.AreEqual(SimpleSpacedRepetitionScheduler.DefaultEaseFactor, card.EaseFactor);
        Assert.AreEqual(0, card.SuccessfulReviewCount);
        Assert.AreEqual(0, card.LapseCount);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, card.LastReviewedAtUtc));
        Assert.AreEqual(ReviewRating.Again, card.LastRating);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, card.UpdatedAtUtc));
        Assert.AreEqual(LearningSessionStatus.Active, session.Status);
        Assert.AreEqual(2, session.TotalCards);
        Assert.AreEqual(1, session.CompletedCards);
        Assert.AreEqual(1, session.AgainCount);
        Assert.AreEqual(0, session.HardCount);
        Assert.AreEqual(0, session.GoodCount);
        Assert.AreEqual(0, session.EasyCount);
        Assert.IsNull(session.CompletedAtUtc);
        Assert.AreEqual(beforeFingerprint.AssignmentFingerprint, afterFingerprint.AssignmentFingerprint);
        Assert.AreEqual(beforeFingerprint.AnswerVariantFingerprint, afterFingerprint.AnswerVariantFingerprint);
        Assert.AreEqual(beforeFingerprint.SenseFingerprint, afterFingerprint.SenseFingerprint);
        Assert.AreEqual(beforeFingerprint.WordFingerprint, afterFingerprint.WordFingerprint);
        Assert.AreEqual(beforeFingerprint.SchemaFingerprint, afterFingerprint.SchemaFingerprint);
        CollectionAssert.AreEqual(before.Assignments, after.Assignments);
        CollectionAssert.AreEqual(before.Variants, after.Variants);
        CollectionAssert.AreEqual(before.Senses, after.Senses);
        CollectionAssert.AreEqual(before.Words, after.Words);
    }

    [TestMethod]
    public async Task CheckSpelling_OutOfScopeAssignedVariant_IsOrdinaryIncorrect()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "out-of-scope-term", "current-direction-target");
        await fixture.MigrateToSchema8Async();

        var currentSenseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        const int otherDirectionCardId = 41;
        const int outOfScopeVariantId = 813;
        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO LearningCards
                (Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor,
                 SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, 0, ?, 0, 0, NULL, NULL, ?, ?)
            """,
            otherDirectionCardId, graph.WordId, currentSenseId, graph.MeaningId,
            (int)CardDirection.TermToMeaning, (int)CardState.New, Now,
            SimpleSpacedRepetitionScheduler.DefaultEaseFactor, Now, Now);
        await fixture.InsertAnswerVariantAsync(
            currentSenseId, "other-direction-answer", answerLanguage: "de", id: outOfScopeVariantId,
            normalizedText: "other-direction-answer", createdAtUtc: Now);
        await fixture.InsertAssignmentAsync(
            currentSenseId, CardDirection.TermToMeaning, outOfScopeVariantId,
            AnswerVariantRequirement.Required, isPreferred: true, requiredSinceUtc: Now,
            createdAtUtc: Now, stableId: "assignment-other-direction");

        var beforeFingerprint = await CapturePersistedStateAsync(fixture);
        var before = await CapturePersistenceDetailsAsync(fixture);
        var targetVariantId = before.Queues.Single().TargetAnswerVariantId;
        var targetAssignment = before.Assignments.Single(row =>
            row.SenseId == currentSenseId
            && row.CardDirection == CardDirection.MeaningToTerm
            && row.AnswerVariantId == targetVariantId);
        var outOfScopeAssignment = before.Assignments.Single(row =>
            row.AnswerVariantId == outOfScopeVariantId);
        var otherCardBefore = before.Cards.Single(row => row.Id == otherDirectionCardId);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);

        var result = await CreateLearningService(database, LearningMode.Typing)
            .CheckSpellingAsync(graph.QueueItemId, "other-direction-answer");

        var afterFingerprint = await CapturePersistedStateAsync(fixture);
        var after = await CapturePersistenceDetailsAsync(fixture);
        var review = after.Reviews.Single();
        var targetProgress = after.Progress.Single();
        var originalQueue = after.Queues.Single(row => row.Id == graph.QueueItemId);
        var repeatQueue = after.Queues.Single(row => row.Id != graph.QueueItemId);
        var currentCard = after.Cards.Single(row => row.Id == graph.CardId);
        var session = after.Sessions.Single(row => row.Id == graph.SessionId);

        Assert.AreEqual(8, before.UserVersion);
        Assert.AreEqual(8, after.UserVersion);
        Assert.IsNotNull(targetVariantId);
        Assert.AreEqual(AnswerVariantRequirement.Required, targetAssignment.Requirement);
        Assert.AreEqual(currentSenseId, outOfScopeAssignment.SenseId);
        Assert.AreEqual(CardDirection.TermToMeaning, outOfScopeAssignment.CardDirection);
        Assert.AreEqual(AnswerVariantRequirement.Required, outOfScopeAssignment.Requirement);
        Assert.IsFalse(before.Assignments.Any(row =>
            row.AnswerVariantId == outOfScopeVariantId
            && row.CardDirection == CardDirection.MeaningToTerm));
        Assert.IsFalse(result.IsCorrect);
        Assert.IsNull(result.MatchedAnswerVariantId);
        Assert.IsTrue(result.RatingWasPersisted);
        Assert.AreEqual("current-direction-target", result.CorrectAnswer);
        Assert.IsEmpty(before.Reviews);
        Assert.HasCount(1, after.Reviews);
        Assert.AreEqual(graph.CardId, review.CardId);
        Assert.AreEqual(graph.SessionId, review.SessionId);
        Assert.AreEqual(ReviewRating.Again, review.Rating);
        Assert.IsTrue(review.WasTypedAnswer);
        Assert.IsFalse(review.WasCorrect);
        Assert.AreEqual(targetVariantId, review.TargetAnswerVariantId);
        Assert.IsNull(review.MatchedAnswerVariantId);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, review.ReviewedAtUtc));
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now.AddMinutes(10), review.DueAtUtc));
        Assert.AreEqual(0, review.IntervalDays);
        Assert.AreEqual(SimpleSpacedRepetitionScheduler.DefaultEaseFactor, review.EaseFactor);
        Assert.IsEmpty(before.Progress);
        Assert.HasCount(1, after.Progress);
        Assert.AreEqual(graph.CardId, targetProgress.CardId);
        Assert.AreEqual(targetVariantId, targetProgress.AnswerVariantId);
        Assert.AreEqual(LearningInteractionMode.Reading, targetProgress.InteractionMode);
        Assert.AreEqual(0, targetProgress.ConsecutiveReadingSuccessCount);
        Assert.AreEqual(0, targetProgress.ConsecutiveTypingSuccessCount);
        Assert.AreEqual(1, targetProgress.ConsecutiveTypingFailureCount);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, targetProgress.LastAssessedAtUtc));
        Assert.IsFalse(targetProgress.MasteryReviewExtensionScheduled);
        Assert.IsFalse(targetProgress.IsMastered);
        Assert.AreEqual(Schema8LearningReviewReplayPolicy.ReplayVersion, targetProgress.ReplayVersion);
        Assert.IsTrue(Schema8Utc.AreSameInstant(targetAssignment.RequiredSinceUtc, targetProgress.CreatedAtUtc));
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, targetProgress.UpdatedAtUtc));
        Assert.IsFalse(after.Progress.Any(row => row.AnswerVariantId == outOfScopeVariantId));
        Assert.HasCount(1, before.Queues);
        Assert.HasCount(2, after.Queues);
        Assert.AreEqual(targetVariantId, originalQueue.TargetAnswerVariantId);
        Assert.IsTrue(originalQueue.AnswerRevealed);
        Assert.IsTrue(originalQueue.SpellingChecked);
        Assert.IsFalse(originalQueue.SpellingCorrect);
        Assert.IsTrue(originalQueue.IsCompleted);
        Assert.AreEqual(ReviewRating.Again, originalQueue.Rating);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, originalQueue.CompletedAtUtc));
        Assert.AreEqual(targetVariantId, repeatQueue.TargetAnswerVariantId);
        Assert.AreEqual(1, repeatQueue.QueueOrder);
        Assert.IsFalse(repeatQueue.IsDueCard);
        Assert.IsTrue(repeatQueue.IsAgainRepeat);
        Assert.IsFalse(repeatQueue.AnswerRevealed);
        Assert.IsFalse(repeatQueue.SpellingChecked);
        Assert.IsFalse(repeatQueue.SpellingCorrect);
        Assert.IsFalse(repeatQueue.IsCompleted);
        Assert.IsNull(repeatQueue.Rating);
        Assert.IsNull(repeatQueue.CompletedAtUtc);
        Assert.AreEqual(CardState.Learning, currentCard.State);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now.AddMinutes(10), currentCard.DueAtUtc));
        Assert.AreEqual(0, currentCard.IntervalDays);
        Assert.AreEqual(SimpleSpacedRepetitionScheduler.DefaultEaseFactor, currentCard.EaseFactor);
        Assert.AreEqual(0, currentCard.SuccessfulReviewCount);
        Assert.AreEqual(0, currentCard.LapseCount);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, currentCard.LastReviewedAtUtc));
        Assert.AreEqual(ReviewRating.Again, currentCard.LastRating);
        Assert.AreEqual(otherCardBefore, after.Cards.Single(row => row.Id == otherDirectionCardId));
        Assert.AreEqual(LearningSessionStatus.Active, session.Status);
        Assert.AreEqual(2, session.TotalCards);
        Assert.AreEqual(1, session.CompletedCards);
        Assert.AreEqual(1, session.AgainCount);
        Assert.AreEqual(0, session.HardCount);
        Assert.AreEqual(0, session.GoodCount);
        Assert.AreEqual(0, session.EasyCount);
        Assert.IsNull(session.CompletedAtUtc);
        Assert.AreEqual(beforeFingerprint.AssignmentFingerprint, afterFingerprint.AssignmentFingerprint);
        Assert.AreEqual(beforeFingerprint.AnswerVariantFingerprint, afterFingerprint.AnswerVariantFingerprint);
        Assert.AreEqual(beforeFingerprint.SenseFingerprint, afterFingerprint.SenseFingerprint);
        Assert.AreEqual(beforeFingerprint.WordFingerprint, afterFingerprint.WordFingerprint);
        Assert.AreEqual(beforeFingerprint.SchemaFingerprint, afterFingerprint.SchemaFingerprint);
        CollectionAssert.AreEqual(before.Assignments, after.Assignments);
        CollectionAssert.AreEqual(before.Variants, after.Variants);
        CollectionAssert.AreEqual(before.Senses, after.Senses);
        CollectionAssert.AreEqual(before.Words, after.Words);
    }

    [TestMethod]
    public async Task CheckSpelling_AmbiguousCurrentAssignments_FailsClosedWithoutMutation()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "ambiguous-term", "caf\u00e9");
        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        const int collidingVariantId = 823;
        await fixture.InsertAnswerVariantAsync(
            senseId, "cafe\u0301", answerLanguage: "en", id: collidingVariantId,
            normalizedText: "cafe\u0301", createdAtUtc: Now);
        await fixture.InsertAssignmentAsync(
            senseId, CardDirection.MeaningToTerm, collidingVariantId,
            AnswerVariantRequirement.AcceptedOnly, isPreferred: false, requiredSinceUtc: null,
            createdAtUtc: Now, stableId: "assignment-normalization-collision");

        var beforeFingerprint = await CapturePersistedStateAsync(fixture);
        var before = await CapturePersistenceDetailsAsync(fixture);
        var targetVariantId = before.Queues.Single().TargetAnswerVariantId;
        var targetAssignment = before.Assignments.Single(row => row.AnswerVariantId == targetVariantId);
        var collidingAssignment = before.Assignments.Single(row => row.AnswerVariantId == collidingVariantId);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => CreateLearningService(database, LearningMode.Typing)
                .CheckSpellingAsync(graph.QueueItemId, "  CAF\u00c9  "));

        var afterFingerprint = await CapturePersistedStateAsync(fixture);
        var after = await CapturePersistenceDetailsAsync(fixture);

        Assert.AreEqual(8, before.UserVersion);
        Assert.AreEqual(8, after.UserVersion);
        Assert.AreEqual(Schema8LearningDataErrorCode.InvalidMatchEvidence, exception.Code);
        Assert.IsTrue(exception.IsNonRetryable);
        Assert.IsNotNull(targetVariantId);
        Assert.AreNotEqual(collidingVariantId, targetVariantId);
        Assert.AreEqual(targetVariantId, before.Assignments.Min(row => row.AnswerVariantId));
        Assert.AreEqual(AnswerVariantRequirement.Required, targetAssignment.Requirement);
        Assert.IsTrue(targetAssignment.IsPreferred);
        Assert.AreEqual(AnswerVariantRequirement.AcceptedOnly, collidingAssignment.Requirement);
        Assert.IsFalse(collidingAssignment.IsPreferred);
        Assert.AreEqual("caf\u00e9", before.Variants.Single(row => row.Id == targetVariantId).DisplayText);
        Assert.AreEqual("cafe\u0301", before.Variants.Single(row => row.Id == collidingVariantId).DisplayText);
        Assert.IsEmpty(after.Reviews);
        Assert.IsEmpty(after.Progress);
        Assert.AreEqual(targetVariantId, after.Queues.Single().TargetAnswerVariantId);
        Assert.AreEqual(beforeFingerprint, afterFingerprint);
        CollectionAssert.AreEqual(before.Queues, after.Queues);
        CollectionAssert.AreEqual(before.Reviews, after.Reviews);
        CollectionAssert.AreEqual(before.Progress, after.Progress);
        CollectionAssert.AreEqual(before.Cards, after.Cards);
        CollectionAssert.AreEqual(before.Sessions, after.Sessions);
        CollectionAssert.AreEqual(before.Senses, after.Senses);
        CollectionAssert.AreEqual(before.Words, after.Words);
        CollectionAssert.AreEqual(before.Variants, after.Variants);
        CollectionAssert.AreEqual(before.Assignments, after.Assignments);
    }

    [TestMethod]
    public async Task CheckSpelling_UndefinedPersistedRequirement_FailsClosedWithoutMutation()
    {
        const int undefinedRequirementValue = 2;
        Assert.IsFalse(Enum.IsDefined((AnswerVariantRequirement)undefinedRequirementValue));

        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "undefined-requirement-term", "undefined-requirement-answer");
        await fixture.MigrateToSchema8Async();

        var queueBeforeCorruption = (await ReadQueueRowsAsync(fixture)).Single();
        var targetVariantId = queueBeforeCorruption.TargetAnswerVariantId
            ?? throw new AssertFailedException("The migrated queue target is missing.");
        await fixture.Connection.ExecuteAsync(
            "UPDATE SenseAnswerVariantAssignments SET Requirement = ?, RequiredSinceUtc = NULL WHERE AnswerVariantId = ?",
            undefinedRequirementValue, targetVariantId);

        var beforeFingerprint = await CapturePersistedStateAsync(fixture);
        var before = await CapturePersistenceDetailsAsync(fixture);
        var malformedAssignment = before.Assignments.Single(row => row.AnswerVariantId == targetVariantId);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var service = CreateLearningService(database, LearningMode.Typing);

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(() =>
            service.CheckSpellingAsync(graph.QueueItemId, "undefined-requirement-answer"));

        var afterFingerprint = await CapturePersistedStateAsync(fixture);
        var after = await CapturePersistenceDetailsAsync(fixture);
        var queueAfter = after.Queues.Single();

        Assert.AreEqual(Schema8LearningDataErrorCode.InvalidAssignmentGraph, exception.Code);
        Assert.AreEqual(undefinedRequirementValue, (int)malformedAssignment.Requirement);
        Assert.IsNull(malformedAssignment.RequiredSinceUtc);
        Assert.IsFalse(queueAfter.AnswerRevealed);
        Assert.IsFalse(queueAfter.SpellingChecked);
        Assert.IsFalse(queueAfter.SpellingCorrect);
        Assert.IsFalse(queueAfter.IsCompleted);
        Assert.IsNull(queueAfter.Rating);
        Assert.IsNull(queueAfter.CompletedAtUtc);
        Assert.IsEmpty(after.Reviews);
        Assert.IsEmpty(after.Progress);
        Assert.AreEqual(beforeFingerprint, afterFingerprint);
        CollectionAssert.AreEqual(before.Queues, after.Queues);
        CollectionAssert.AreEqual(before.Reviews, after.Reviews);
        CollectionAssert.AreEqual(before.Progress, after.Progress);
        CollectionAssert.AreEqual(before.Cards, after.Cards);
        CollectionAssert.AreEqual(before.Sessions, after.Sessions);
        CollectionAssert.AreEqual(before.Senses, after.Senses);
        CollectionAssert.AreEqual(before.Words, after.Words);
        CollectionAssert.AreEqual(before.Variants, after.Variants);
        CollectionAssert.AreEqual(before.Assignments, after.Assignments);

        await fixture.Connection.ExecuteAsync(
            "UPDATE SenseAnswerVariantAssignments SET Requirement = ?, RequiredSinceUtc = ? WHERE AnswerVariantId = ?",
            (int)AnswerVariantRequirement.Required, Now, targetVariantId);
        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningSessionCards SET AnswerRevealed = 1, SpellingChecked = 1, SpellingCorrect = 1 WHERE Id = ?",
            graph.QueueItemId);
        var beforeEvidenceProbe = await CapturePersistedStateAsync(fixture);

        var evidenceException = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(() =>
            service.RateAsync(graph.QueueItemId, ReviewRating.Good));

        var afterEvidenceProbe = await CapturePersistedStateAsync(fixture);
        Assert.AreEqual(Schema8LearningDataErrorCode.MissingMatchEvidence, evidenceException.Code);
        Assert.AreEqual(beforeEvidenceProbe, afterEvidenceProbe);
    }

    [TestMethod]
    public async Task CheckSpelling_WrongAnswerWithoutExistingVariant_ReturnsIncorrectAndPersistsAgainImmediately()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "unknown-answer-term", "persisted-unknown-test-target");
        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        const int additionalVariantId = 831;
        const string enteredAnswer = "never-persisted-unknown-answer";
        await fixture.InsertAnswerVariantAsync(
            senseId, "additional-current-answer", answerLanguage: "en", id: additionalVariantId,
            normalizedText: "additional-current-answer", createdAtUtc: Now);
        await fixture.InsertAssignmentAsync(
            senseId, CardDirection.MeaningToTerm, additionalVariantId,
            AnswerVariantRequirement.AcceptedOnly, isPreferred: false, requiredSinceUtc: null,
            createdAtUtc: Now, stableId: "assignment-additional-current-unknown-test");

        var globalEnteredTextCount = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM AnswerVariants WHERE DisplayText = ? OR NormalizedText = ?",
            enteredAnswer, enteredAnswer);
        var beforeFingerprint = await CapturePersistedStateAsync(fixture);
        var before = await CapturePersistenceDetailsAsync(fixture);
        var targetVariantId = before.Queues.Single().TargetAnswerVariantId;
        var targetAssignment = before.Assignments.Single(row =>
            row.SenseId == senseId
            && row.CardDirection == CardDirection.MeaningToTerm
            && row.AnswerVariantId == targetVariantId);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);

        var result = await CreateLearningService(database, LearningMode.Typing)
            .CheckSpellingAsync(graph.QueueItemId, enteredAnswer);

        var afterFingerprint = await CapturePersistedStateAsync(fixture);
        var after = await CapturePersistenceDetailsAsync(fixture);
        var review = after.Reviews.Single();
        var targetProgress = after.Progress.Single();
        var originalQueue = after.Queues.Single(row => row.Id == graph.QueueItemId);
        var repeatQueue = after.Queues.Single(row => row.Id != graph.QueueItemId);
        var card = after.Cards.Single(row => row.Id == graph.CardId);
        var session = after.Sessions.Single(row => row.Id == graph.SessionId);

        Assert.AreEqual(8, before.UserVersion);
        Assert.AreEqual(8, after.UserVersion);
        Assert.AreEqual(0, globalEnteredTextCount);
        Assert.IsNotNull(targetVariantId);
        Assert.AreEqual(AnswerVariantRequirement.Required, targetAssignment.Requirement);
        Assert.IsTrue(before.Assignments.Any(row =>
            row.SenseId == senseId
            && row.CardDirection == CardDirection.MeaningToTerm
            && row.AnswerVariantId == additionalVariantId));
        Assert.IsFalse(result.IsCorrect);
        Assert.IsNull(result.MatchedAnswerVariantId);
        Assert.IsTrue(result.RatingWasPersisted);
        Assert.AreEqual(enteredAnswer, result.EnteredAnswer);
        Assert.AreEqual("persisted-unknown-test-target", result.CorrectAnswer);
        Assert.IsEmpty(before.Reviews);
        Assert.HasCount(1, after.Reviews);
        Assert.AreEqual(graph.CardId, review.CardId);
        Assert.AreEqual(graph.SessionId, review.SessionId);
        Assert.AreEqual(ReviewRating.Again, review.Rating);
        Assert.IsTrue(review.WasTypedAnswer);
        Assert.IsFalse(review.WasCorrect);
        Assert.AreEqual(targetVariantId, review.TargetAnswerVariantId);
        Assert.IsNull(review.MatchedAnswerVariantId);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, review.ReviewedAtUtc));
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now.AddMinutes(10), review.DueAtUtc));
        Assert.AreEqual(0, review.IntervalDays);
        Assert.AreEqual(SimpleSpacedRepetitionScheduler.DefaultEaseFactor, review.EaseFactor);
        Assert.IsEmpty(before.Progress);
        Assert.HasCount(1, after.Progress);
        Assert.AreEqual(graph.CardId, targetProgress.CardId);
        Assert.AreEqual(targetVariantId, targetProgress.AnswerVariantId);
        Assert.AreEqual(LearningInteractionMode.Reading, targetProgress.InteractionMode);
        Assert.AreEqual(0, targetProgress.ConsecutiveReadingSuccessCount);
        Assert.AreEqual(0, targetProgress.ConsecutiveTypingSuccessCount);
        Assert.AreEqual(1, targetProgress.ConsecutiveTypingFailureCount);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, targetProgress.LastAssessedAtUtc));
        Assert.IsFalse(targetProgress.MasteryReviewExtensionScheduled);
        Assert.IsFalse(targetProgress.IsMastered);
        Assert.AreEqual(Schema8LearningReviewReplayPolicy.ReplayVersion, targetProgress.ReplayVersion);
        Assert.IsTrue(Schema8Utc.AreSameInstant(targetAssignment.RequiredSinceUtc, targetProgress.CreatedAtUtc));
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, targetProgress.UpdatedAtUtc));
        Assert.IsFalse(after.Progress.Any(row => row.AnswerVariantId == additionalVariantId));
        Assert.HasCount(1, before.Queues);
        Assert.HasCount(2, after.Queues);
        Assert.AreEqual(targetVariantId, originalQueue.TargetAnswerVariantId);
        Assert.IsTrue(originalQueue.AnswerRevealed);
        Assert.IsTrue(originalQueue.SpellingChecked);
        Assert.IsFalse(originalQueue.SpellingCorrect);
        Assert.IsTrue(originalQueue.IsCompleted);
        Assert.AreEqual(ReviewRating.Again, originalQueue.Rating);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, originalQueue.CompletedAtUtc));
        Assert.AreEqual(targetVariantId, repeatQueue.TargetAnswerVariantId);
        Assert.AreEqual(1, repeatQueue.QueueOrder);
        Assert.IsFalse(repeatQueue.IsDueCard);
        Assert.IsTrue(repeatQueue.IsAgainRepeat);
        Assert.IsFalse(repeatQueue.AnswerRevealed);
        Assert.IsFalse(repeatQueue.SpellingChecked);
        Assert.IsFalse(repeatQueue.SpellingCorrect);
        Assert.IsFalse(repeatQueue.IsCompleted);
        Assert.IsNull(repeatQueue.Rating);
        Assert.IsNull(repeatQueue.CompletedAtUtc);
        Assert.AreEqual(CardState.Learning, card.State);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now.AddMinutes(10), card.DueAtUtc));
        Assert.AreEqual(0, card.IntervalDays);
        Assert.AreEqual(SimpleSpacedRepetitionScheduler.DefaultEaseFactor, card.EaseFactor);
        Assert.AreEqual(0, card.SuccessfulReviewCount);
        Assert.AreEqual(0, card.LapseCount);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, card.LastReviewedAtUtc));
        Assert.AreEqual(ReviewRating.Again, card.LastRating);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, card.UpdatedAtUtc));
        Assert.AreEqual(LearningSessionStatus.Active, session.Status);
        Assert.AreEqual(2, session.TotalCards);
        Assert.AreEqual(1, session.CompletedCards);
        Assert.AreEqual(1, session.AgainCount);
        Assert.AreEqual(0, session.HardCount);
        Assert.AreEqual(0, session.GoodCount);
        Assert.AreEqual(0, session.EasyCount);
        Assert.IsNull(session.CompletedAtUtc);
        Assert.AreEqual(beforeFingerprint.AssignmentFingerprint, afterFingerprint.AssignmentFingerprint);
        Assert.AreEqual(beforeFingerprint.AnswerVariantFingerprint, afterFingerprint.AnswerVariantFingerprint);
        Assert.AreEqual(beforeFingerprint.SenseFingerprint, afterFingerprint.SenseFingerprint);
        Assert.AreEqual(beforeFingerprint.WordFingerprint, afterFingerprint.WordFingerprint);
        Assert.AreEqual(beforeFingerprint.SchemaFingerprint, afterFingerprint.SchemaFingerprint);
        CollectionAssert.AreEqual(before.Assignments, after.Assignments);
        CollectionAssert.AreEqual(before.Variants, after.Variants);
        CollectionAssert.AreEqual(before.Senses, after.Senses);
        CollectionAssert.AreEqual(before.Words, after.Words);
    }

    [TestMethod]
    public async Task CheckSpelling_CorrectAnswer_DoesNotPersistReviewBeforeRating()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "correct-no-review-term", "exact-current-target-answer");
        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        const int additionalVariantId = 841;
        await fixture.InsertAnswerVariantAsync(
            senseId, "distinguishable-additional-answer", answerLanguage: "en", id: additionalVariantId,
            normalizedText: "distinguishable-additional-answer", createdAtUtc: Now);
        await fixture.InsertAssignmentAsync(
            senseId, CardDirection.MeaningToTerm, additionalVariantId,
            AnswerVariantRequirement.AcceptedOnly, isPreferred: false, requiredSinceUtc: null,
            createdAtUtc: Now, stableId: "assignment-additional-correct-no-review");

        var beforeFingerprint = await CapturePersistedStateAsync(fixture);
        var before = await CapturePersistenceDetailsAsync(fixture);
        var targetVariantId = before.Queues.Single().TargetAnswerVariantId;
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);

        var result = await CreateLearningService(database, LearningMode.Typing)
            .CheckSpellingAsync(graph.QueueItemId, "exact-current-target-answer");

        var afterFingerprint = await CapturePersistedStateAsync(fixture);
        var after = await CapturePersistenceDetailsAsync(fixture);
        var queue = after.Queues.Single();

        Assert.AreEqual(8, before.UserVersion);
        Assert.AreEqual(8, after.UserVersion);
        Assert.IsNotNull(targetVariantId);
        Assert.AreNotEqual(additionalVariantId, targetVariantId);
        Assert.IsTrue(result.IsCorrect);
        Assert.AreEqual(targetVariantId, result.MatchedAnswerVariantId);
        Assert.IsFalse(result.RatingWasPersisted);
        Assert.AreEqual("exact-current-target-answer", result.EnteredAnswer);
        Assert.AreEqual("exact-current-target-answer", result.CorrectAnswer);
        Assert.IsEmpty(before.Reviews);
        Assert.IsEmpty(after.Reviews);
        Assert.IsEmpty(before.Progress);
        Assert.IsEmpty(after.Progress);
        Assert.HasCount(1, before.Queues);
        Assert.HasCount(1, after.Queues);
        Assert.AreEqual(targetVariantId, queue.TargetAnswerVariantId);
        Assert.IsTrue(queue.AnswerRevealed);
        Assert.IsTrue(queue.SpellingChecked);
        Assert.IsTrue(queue.SpellingCorrect);
        Assert.IsFalse(queue.IsCompleted);
        Assert.IsNull(queue.Rating);
        Assert.IsNull(queue.CompletedAtUtc);
        Assert.AreEqual(beforeFingerprint.QueueStructureFingerprint, afterFingerprint.QueueStructureFingerprint);
        Assert.AreEqual(beforeFingerprint.QueueTargetFingerprint, afterFingerprint.QueueTargetFingerprint);
        Assert.AreEqual(beforeFingerprint.SessionFingerprint, afterFingerprint.SessionFingerprint);
        Assert.AreEqual(beforeFingerprint.CardFingerprint, afterFingerprint.CardFingerprint);
        Assert.AreEqual(beforeFingerprint.ReviewFingerprint, afterFingerprint.ReviewFingerprint);
        Assert.AreEqual(beforeFingerprint.ProgressFingerprint, afterFingerprint.ProgressFingerprint);
        Assert.AreEqual(beforeFingerprint.AssignmentFingerprint, afterFingerprint.AssignmentFingerprint);
        Assert.AreEqual(beforeFingerprint.AnswerVariantFingerprint, afterFingerprint.AnswerVariantFingerprint);
        Assert.AreEqual(beforeFingerprint.SenseFingerprint, afterFingerprint.SenseFingerprint);
        Assert.AreEqual(beforeFingerprint.WordFingerprint, afterFingerprint.WordFingerprint);
        Assert.AreEqual(beforeFingerprint.SchemaFingerprint, afterFingerprint.SchemaFingerprint);
        CollectionAssert.AreEqual(before.Reviews, after.Reviews);
        CollectionAssert.AreEqual(before.Progress, after.Progress);
        CollectionAssert.AreEqual(before.Cards, after.Cards);
        CollectionAssert.AreEqual(before.Sessions, after.Sessions);
        CollectionAssert.AreEqual(before.Senses, after.Senses);
        CollectionAssert.AreEqual(before.Words, after.Words);
        CollectionAssert.AreEqual(before.Variants, after.Variants);
        CollectionAssert.AreEqual(before.Assignments, after.Assignments);
    }

    [TestMethod]
    public async Task CheckSpelling_TargetVariantOutsideSenseOrDirection_FailsClosedWithoutMutation()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "post-load-invalid-target-term", "post-load-persisted-target");
        await fixture.MigrateToSchema8Async();

        var currentSenseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        var targetVariantId = (await ReadQueueRowsAsync(fixture)).Single().TargetAnswerVariantId;
        const int additionalVariantId = 851;
        const int otherSenseId = 853;
        await fixture.InsertAnswerVariantAsync(
            currentSenseId, "remaining-current-answer", answerLanguage: "en", id: additionalVariantId,
            normalizedText: "remaining-current-answer", createdAtUtc: Now);
        await fixture.InsertAssignmentAsync(
            currentSenseId, CardDirection.MeaningToTerm, additionalVariantId,
            AnswerVariantRequirement.AcceptedOnly, isPreferred: false, requiredSinceUtc: null,
            createdAtUtc: Now, stableId: "assignment-remaining-current-after-target-move");
        await fixture.InsertSenseAsync(
            graph.WordId, id: otherSenseId, sourceLanguage: "en", explanationLanguage: "de",
            createdAtUtc: Now, updatedAtUtc: Now);

        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var service = CreateLearningService(database, LearningMode.Typing);
        var legitimateLoad = await service.GetOrStartAsync();

        Assert.IsNotNull(legitimateLoad.Card);
        Assert.AreEqual(graph.SessionId, legitimateLoad.Card.SessionId);
        Assert.AreEqual(graph.QueueItemId, legitimateLoad.Card.QueueItemId);
        Assert.AreEqual(graph.CardId, legitimateLoad.Card.CardId);
        Assert.AreEqual(CardDirection.MeaningToTerm, legitimateLoad.Card.Direction);
        Assert.AreEqual(LearningInteractionMode.Typing, legitimateLoad.Card.InteractionMode);
        Assert.IsNull(legitimateLoad.CompletedSummary);
        Assert.IsNotNull(targetVariantId);

        await fixture.Connection.ExecuteAsync(
            "UPDATE AnswerVariants SET SenseId = ?, UpdatedAtUtc = ? WHERE Id = ?",
            otherSenseId, Now, targetVariantId);
        await fixture.Connection.ExecuteAsync(
            "UPDATE SenseAnswerVariantAssignments SET SenseId = ?, CardDirection = ?, UpdatedAtUtc = ? WHERE SenseId = ? AND CardDirection = ? AND AnswerVariantId = ?",
            otherSenseId, (int)CardDirection.TermToMeaning, Now,
            currentSenseId, (int)CardDirection.MeaningToTerm, targetVariantId);

        var beforeFingerprint = await CapturePersistedStateAsync(fixture);
        var before = await CapturePersistenceDetailsAsync(fixture);
        var beforeQueue = before.Queues.Single();
        var movedAssignment = before.Assignments.Single(row => row.AnswerVariantId == targetVariantId);

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => service.CheckSpellingAsync(graph.QueueItemId, "post-load-persisted-target"));

        var afterFingerprint = await CapturePersistedStateAsync(fixture);
        var after = await CapturePersistenceDetailsAsync(fixture);
        var afterQueue = after.Queues.Single();

        Assert.AreEqual(8, before.UserVersion);
        Assert.AreEqual(8, after.UserVersion);
        Assert.AreEqual(Schema8LearningDataErrorCode.InvalidTarget, exception.Code);
        Assert.IsTrue(exception.IsNonRetryable);
        Assert.AreEqual(targetVariantId, beforeQueue.TargetAnswerVariantId);
        Assert.AreEqual(targetVariantId, afterQueue.TargetAnswerVariantId);
        Assert.AreEqual(otherSenseId, before.Variants.Single(row => row.Id == targetVariantId).SenseId);
        Assert.AreEqual(otherSenseId, movedAssignment.SenseId);
        Assert.AreEqual(CardDirection.TermToMeaning, movedAssignment.CardDirection);
        Assert.IsFalse(before.Assignments.Any(row =>
            row.SenseId == currentSenseId
            && row.CardDirection == CardDirection.MeaningToTerm
            && row.AnswerVariantId == targetVariantId));
        Assert.IsTrue(before.Assignments.Any(row =>
            row.SenseId == currentSenseId
            && row.CardDirection == CardDirection.MeaningToTerm
            && row.AnswerVariantId == additionalVariantId));
        Assert.IsFalse(afterQueue.AnswerRevealed);
        Assert.IsFalse(afterQueue.SpellingChecked);
        Assert.IsFalse(afterQueue.SpellingCorrect);
        Assert.IsFalse(afterQueue.IsCompleted);
        Assert.IsNull(afterQueue.Rating);
        Assert.IsNull(afterQueue.CompletedAtUtc);
        Assert.IsEmpty(after.Reviews);
        Assert.IsEmpty(after.Progress);
        Assert.AreEqual(beforeFingerprint, afterFingerprint);
        CollectionAssert.AreEqual(before.Queues, after.Queues);
        CollectionAssert.AreEqual(before.Reviews, after.Reviews);
        CollectionAssert.AreEqual(before.Progress, after.Progress);
        CollectionAssert.AreEqual(before.Cards, after.Cards);
        CollectionAssert.AreEqual(before.Sessions, after.Sessions);
        CollectionAssert.AreEqual(before.Senses, after.Senses);
        CollectionAssert.AreEqual(before.Words, after.Words);
        CollectionAssert.AreEqual(before.Variants, after.Variants);
        CollectionAssert.AreEqual(before.Assignments, after.Assignments);
    }

    [TestMethod]
    public async Task PendingMatch_IsBoundToExactQueueItemEnteredAnswerAndMatchedVariant()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "pending-tuple-term", "original-persisted-target");
        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        const int alternateVariantId = 861;
        await fixture.InsertAnswerVariantAsync(
            senseId, "alternate-required-answer", answerLanguage: "en", id: alternateVariantId,
            normalizedText: "alternate-required-answer", createdAtUtc: Now);
        await fixture.InsertAssignmentAsync(
            senseId, CardDirection.MeaningToTerm, alternateVariantId,
            AnswerVariantRequirement.Required, isPreferred: false, requiredSinceUtc: Now,
            createdAtUtc: Now, stableId: "assignment-pending-tuple-alternate");

        var beforeCheck = await CapturePersistenceDetailsAsync(fixture);
        var targetVariantId = beforeCheck.Queues.Single().TargetAnswerVariantId;
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var service = CreateLearningService(database, LearningMode.Typing);

        var checkResult = await service.CheckSpellingAsync(
            graph.QueueItemId, "alternate-required-answer");

        var afterCheck = await CapturePersistenceDetailsAsync(fixture);
        var checkedQueue = afterCheck.Queues.Single();

        Assert.AreEqual(8, beforeCheck.UserVersion);
        Assert.AreEqual(8, afterCheck.UserVersion);
        Assert.IsNotNull(targetVariantId);
        Assert.AreNotEqual(targetVariantId, alternateVariantId);
        Assert.AreEqual(
            AnswerVariantRequirement.Required,
            beforeCheck.Assignments.Single(row => row.AnswerVariantId == alternateVariantId).Requirement);
        Assert.IsTrue(checkResult.IsCorrect);
        Assert.AreEqual(alternateVariantId, checkResult.MatchedAnswerVariantId);
        Assert.IsFalse(checkResult.RatingWasPersisted);
        Assert.AreEqual("alternate-required-answer", checkResult.EnteredAnswer);
        Assert.AreEqual("original-persisted-target", checkResult.CorrectAnswer);
        Assert.AreEqual(targetVariantId, checkedQueue.TargetAnswerVariantId);
        Assert.IsTrue(checkedQueue.AnswerRevealed);
        Assert.IsTrue(checkedQueue.SpellingChecked);
        Assert.IsTrue(checkedQueue.SpellingCorrect);
        Assert.IsFalse(checkedQueue.IsCompleted);
        Assert.IsNull(checkedQueue.Rating);
        Assert.IsNull(checkedQueue.CompletedAtUtc);
        Assert.IsEmpty(afterCheck.Reviews);
        Assert.IsEmpty(afterCheck.Progress);
        CollectionAssert.AreEqual(beforeCheck.Cards, afterCheck.Cards);
        CollectionAssert.AreEqual(beforeCheck.Sessions, afterCheck.Sessions);
        CollectionAssert.AreEqual(beforeCheck.Senses, afterCheck.Senses);
        CollectionAssert.AreEqual(beforeCheck.Words, afterCheck.Words);
        CollectionAssert.AreEqual(beforeCheck.Variants, afterCheck.Variants);
        CollectionAssert.AreEqual(beforeCheck.Assignments, afterCheck.Assignments);

        var ratingResult = await service.RateAsync(graph.QueueItemId, ReviewRating.Good);

        var afterRating = await CapturePersistenceDetailsAsync(fixture);
        var review = afterRating.Reviews.Single();
        var targetProgress = afterRating.Progress.Single(row => row.AnswerVariantId == targetVariantId);
        var alternateProgress = afterRating.Progress.Single(row => row.AnswerVariantId == alternateVariantId);
        var completedQueue = afterRating.Queues.Single();
        var card = afterRating.Cards.Single(row => row.Id == graph.CardId);
        var session = afterRating.Sessions.Single(row => row.Id == graph.SessionId);

        Assert.IsNull(ratingResult.Card);
        Assert.IsNotNull(ratingResult.CompletedSummary);
        Assert.AreEqual(graph.SessionId, ratingResult.CompletedSummary!.SessionId);
        Assert.HasCount(1, afterRating.Reviews);
        Assert.AreEqual(graph.CardId, review.CardId);
        Assert.AreEqual(graph.SessionId, review.SessionId);
        Assert.AreEqual(ReviewRating.Good, review.Rating);
        Assert.IsTrue(review.WasTypedAnswer);
        Assert.IsTrue(review.WasCorrect);
        Assert.AreEqual(targetVariantId, review.TargetAnswerVariantId);
        Assert.AreEqual(alternateVariantId, review.MatchedAnswerVariantId);
        Assert.HasCount(2, afterRating.Progress);
        Assert.AreEqual(0, targetProgress.ConsecutiveReadingSuccessCount);
        Assert.AreEqual(0, targetProgress.ConsecutiveTypingSuccessCount);
        Assert.AreEqual(0, targetProgress.ConsecutiveTypingFailureCount);
        Assert.IsNull(targetProgress.LastAssessedAtUtc);
        Assert.IsFalse(targetProgress.IsMastered);
        Assert.AreEqual(1, alternateProgress.ConsecutiveTypingSuccessCount);
        Assert.AreEqual(0, alternateProgress.ConsecutiveTypingFailureCount);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, alternateProgress.LastAssessedAtUtc));
        Assert.IsFalse(alternateProgress.IsMastered);
        Assert.AreEqual(targetVariantId, completedQueue.TargetAnswerVariantId);
        Assert.IsTrue(completedQueue.IsCompleted);
        Assert.AreEqual(ReviewRating.Good, completedQueue.Rating);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, completedQueue.CompletedAtUtc));
        Assert.AreEqual(CardState.Review, card.State);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now.AddDays(3), card.DueAtUtc));
        Assert.AreEqual(3, card.IntervalDays);
        Assert.AreEqual(1, card.SuccessfulReviewCount);
        Assert.AreEqual(0, card.LapseCount);
        Assert.AreEqual(ReviewRating.Good, card.LastRating);
        Assert.AreEqual(LearningSessionStatus.Completed, session.Status);
        Assert.AreEqual(1, session.TotalCards);
        Assert.AreEqual(1, session.CompletedCards);
        Assert.AreEqual(0, session.AgainCount);
        Assert.AreEqual(0, session.HardCount);
        Assert.AreEqual(1, session.GoodCount);
        Assert.AreEqual(0, session.EasyCount);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, session.CompletedAtUtc));
        CollectionAssert.AreEqual(beforeCheck.Words, afterRating.Words);
        CollectionAssert.AreEqual(beforeCheck.Variants, afterRating.Variants);
        CollectionAssert.AreEqual(beforeCheck.Assignments, afterRating.Assignments);
    }

    [TestMethod]
    public async Task PendingMatch_DoesNotCrossQueueItems()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var seededSession = await SeedTwoCardSessionAsync(fixture);
        await fixture.MigrateToSchema8Async();

        var queues = await ReadQueueRowsAsync(fixture);
        var queueA = queues.Single(row => row.Id == seededSession.FirstQueueItemId);
        var queueB = queues.Single(row => row.Id == seededSession.SecondQueueItemId);
        var targetTexts = await ReadTargetTextsAsync(fixture);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var service = CreateLearningService(database, LearningMode.Typing);

        var checkA = await service.CheckSpellingAsync(queueA.Id, targetTexts[queueA.Id]);
        Assert.IsTrue(checkA.IsCorrect);
        Assert.AreEqual(queueA.TargetAnswerVariantId, checkA.MatchedAnswerVariantId);
        Assert.IsFalse(checkA.RatingWasPersisted);

        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningSessionCards SET AnswerRevealed = 1, SpellingChecked = 1, SpellingCorrect = 1 WHERE Id = ?",
            queueB.Id);
        var beforeRejectFingerprint = await CapturePersistedStateAsync(fixture);
        var beforeReject = await CapturePersistenceDetailsAsync(fixture);
        var queueBBeforeReject = beforeReject.Queues.Single(row => row.Id == queueB.Id);

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => service.RateAsync(queueB.Id, ReviewRating.Good));

        var afterRejectFingerprint = await CapturePersistedStateAsync(fixture);
        var afterReject = await CapturePersistenceDetailsAsync(fixture);

        Assert.AreEqual(8, beforeReject.UserVersion);
        Assert.AreEqual(Schema8LearningDataErrorCode.MissingMatchEvidence, exception.Code);
        Assert.IsTrue(exception.IsNonRetryable);
        Assert.AreEqual(beforeRejectFingerprint, afterRejectFingerprint);
        CollectionAssert.AreEqual(beforeReject.Queues, afterReject.Queues);
        CollectionAssert.AreEqual(beforeReject.Reviews, afterReject.Reviews);
        CollectionAssert.AreEqual(beforeReject.Progress, afterReject.Progress);
        CollectionAssert.AreEqual(beforeReject.Cards, afterReject.Cards);
        CollectionAssert.AreEqual(beforeReject.Sessions, afterReject.Sessions);
        CollectionAssert.AreEqual(beforeReject.Senses, afterReject.Senses);
        CollectionAssert.AreEqual(beforeReject.Words, afterReject.Words);
        CollectionAssert.AreEqual(beforeReject.Variants, afterReject.Variants);
        CollectionAssert.AreEqual(beforeReject.Assignments, afterReject.Assignments);
        Assert.IsEmpty(afterReject.Reviews);
        Assert.IsEmpty(afterReject.Progress);

        var ratingA = await service.RateAsync(queueA.Id, ReviewRating.Good);

        var final = await CapturePersistenceDetailsAsync(fixture);
        var review = final.Reviews.Single();
        var queueAFinal = final.Queues.Single(row => row.Id == queueA.Id);
        var queueBFinal = final.Queues.Single(row => row.Id == queueB.Id);
        var session = final.Sessions.Single(row => row.Id == seededSession.SessionId);

        Assert.IsNotNull(ratingA.Card);
        Assert.AreEqual(seededSession.SessionId, ratingA.Card.SessionId);
        Assert.AreEqual(queueB.Id, ratingA.Card.QueueItemId);
        Assert.AreEqual(queueB.CardId, ratingA.Card.CardId);
        Assert.AreEqual(CardDirection.MeaningToTerm, ratingA.Card.Direction);
        Assert.AreEqual(LearningInteractionMode.Typing, ratingA.Card.InteractionMode);
        Assert.IsNull(ratingA.CompletedSummary);
        Assert.HasCount(1, final.Reviews);
        Assert.AreEqual(queueA.CardId, review.CardId);
        Assert.AreEqual(seededSession.SessionId, review.SessionId);
        Assert.AreEqual(ReviewRating.Good, review.Rating);
        Assert.AreEqual(queueA.TargetAnswerVariantId, review.TargetAnswerVariantId);
        Assert.AreEqual(queueA.TargetAnswerVariantId, review.MatchedAnswerVariantId);
        Assert.IsTrue(queueAFinal.IsCompleted);
        Assert.AreEqual(ReviewRating.Good, queueAFinal.Rating);
        Assert.AreEqual(queueBBeforeReject, queueBFinal);
        Assert.IsFalse(queueBFinal.IsCompleted);
        Assert.IsNull(queueBFinal.Rating);
        Assert.IsNull(queueBFinal.CompletedAtUtc);
        Assert.AreEqual(LearningSessionStatus.Active, session.Status);
        Assert.AreEqual(2, session.TotalCards);
        Assert.AreEqual(1, session.CompletedCards);
        Assert.AreEqual(1, session.GoodCount);
        Assert.AreEqual(0, session.AgainCount);
        Assert.HasCount(1, final.Progress);
        Assert.AreEqual(queueA.CardId, final.Progress.Single().CardId);
        Assert.AreEqual(queueA.TargetAnswerVariantId, final.Progress.Single().AnswerVariantId);
        CollectionAssert.AreEqual(beforeReject.Words, final.Words);
        CollectionAssert.AreEqual(beforeReject.Variants, final.Variants);
        CollectionAssert.AreEqual(beforeReject.Assignments, final.Assignments);
        Assert.AreEqual(
            beforeReject.Cards.Single(row => row.Id == queueB.CardId),
            final.Cards.Single(row => row.Id == queueB.CardId));
    }

    [TestMethod]
    public async Task PendingMatch_DifferentEnteredAnswerInvalidatesPriorEvidence()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "pending-replacement-term", "first-required-answer");
        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        const int secondVariantId = 871;
        await fixture.InsertAnswerVariantAsync(
            senseId, "second-required-answer", answerLanguage: "en", id: secondVariantId,
            normalizedText: "second-required-answer", createdAtUtc: Now);
        await fixture.InsertAssignmentAsync(
            senseId, CardDirection.MeaningToTerm, secondVariantId,
            AnswerVariantRequirement.Required, isPreferred: false, requiredSinceUtc: Now,
            createdAtUtc: Now, stableId: "assignment-pending-replacement-second");

        var before = await CapturePersistenceDetailsAsync(fixture);
        var targetVariantId = before.Queues.Single().TargetAnswerVariantId;
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var service = CreateLearningService(database, LearningMode.Typing);

        var firstCheck = await service.CheckSpellingAsync(
            graph.QueueItemId, "first-required-answer");
        var afterFirstCheck = await CapturePersistenceDetailsAsync(fixture);

        Assert.AreEqual(8, before.UserVersion);
        Assert.IsNotNull(targetVariantId);
        Assert.IsTrue(firstCheck.IsCorrect);
        Assert.AreEqual(targetVariantId, firstCheck.MatchedAnswerVariantId);
        Assert.IsFalse(firstCheck.RatingWasPersisted);
        Assert.AreEqual("first-required-answer", firstCheck.EnteredAnswer);
        Assert.IsEmpty(afterFirstCheck.Reviews);
        Assert.IsEmpty(afterFirstCheck.Progress);

        var secondCheck = await service.CheckSpellingAsync(
            graph.QueueItemId, "second-required-answer");
        var afterSecondCheck = await CapturePersistenceDetailsAsync(fixture);
        var checkedQueue = afterSecondCheck.Queues.Single();

        Assert.IsTrue(secondCheck.IsCorrect);
        Assert.AreEqual(secondVariantId, secondCheck.MatchedAnswerVariantId);
        Assert.AreNotEqual(firstCheck.MatchedAnswerVariantId, secondCheck.MatchedAnswerVariantId);
        Assert.IsFalse(secondCheck.RatingWasPersisted);
        Assert.AreEqual("second-required-answer", secondCheck.EnteredAnswer);
        Assert.AreEqual(targetVariantId, checkedQueue.TargetAnswerVariantId);
        Assert.IsTrue(checkedQueue.AnswerRevealed);
        Assert.IsTrue(checkedQueue.SpellingChecked);
        Assert.IsTrue(checkedQueue.SpellingCorrect);
        Assert.IsFalse(checkedQueue.IsCompleted);
        Assert.IsEmpty(afterSecondCheck.Reviews);
        Assert.IsEmpty(afterSecondCheck.Progress);
        CollectionAssert.AreEqual(afterFirstCheck.Queues, afterSecondCheck.Queues);
        CollectionAssert.AreEqual(afterFirstCheck.Cards, afterSecondCheck.Cards);
        CollectionAssert.AreEqual(afterFirstCheck.Sessions, afterSecondCheck.Sessions);
        CollectionAssert.AreEqual(afterFirstCheck.Senses, afterSecondCheck.Senses);
        CollectionAssert.AreEqual(afterFirstCheck.Words, afterSecondCheck.Words);
        CollectionAssert.AreEqual(afterFirstCheck.Variants, afterSecondCheck.Variants);
        CollectionAssert.AreEqual(afterFirstCheck.Assignments, afterSecondCheck.Assignments);

        await service.RateAsync(graph.QueueItemId, ReviewRating.Good);

        var afterRating = await CapturePersistenceDetailsAsync(fixture);
        var review = afterRating.Reviews.Single();
        var targetProgress = afterRating.Progress.Single(row => row.AnswerVariantId == targetVariantId);
        var secondProgress = afterRating.Progress.Single(row => row.AnswerVariantId == secondVariantId);

        Assert.HasCount(1, afterRating.Reviews);
        Assert.AreEqual(targetVariantId, review.TargetAnswerVariantId);
        Assert.AreEqual(secondVariantId, review.MatchedAnswerVariantId);
        Assert.AreNotEqual(firstCheck.MatchedAnswerVariantId, review.MatchedAnswerVariantId);
        Assert.HasCount(2, afterRating.Progress);
        Assert.AreEqual(0, targetProgress.ConsecutiveReadingSuccessCount);
        Assert.AreEqual(0, targetProgress.ConsecutiveTypingSuccessCount);
        Assert.AreEqual(0, targetProgress.ConsecutiveTypingFailureCount);
        Assert.IsNull(targetProgress.LastAssessedAtUtc);
        Assert.AreEqual(1, secondProgress.ConsecutiveTypingSuccessCount);
        Assert.AreEqual(0, secondProgress.ConsecutiveTypingFailureCount);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, secondProgress.LastAssessedAtUtc));
        Assert.IsTrue(afterRating.Queues.Single().IsCompleted);
        Assert.AreEqual(ReviewRating.Good, afterRating.Queues.Single().Rating);
        CollectionAssert.AreEqual(before.Words, afterRating.Words);
        CollectionAssert.AreEqual(before.Variants, afterRating.Variants);
        CollectionAssert.AreEqual(before.Assignments, afterRating.Assignments);
    }

    [TestMethod]
    public async Task PendingMatch_IsNotRestoredAcrossLearningServiceReconstruction()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "reconstruction-term", "reconstruction-required-answer");
        await fixture.MigrateToSchema8Async();

        var beforeCheck = await CapturePersistenceDetailsAsync(fixture);
        var targetVariantId = beforeCheck.Queues.Single().TargetAnswerVariantId;
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var serviceA = CreateLearningService(database, LearningMode.Typing);

        var check = await serviceA.CheckSpellingAsync(
            graph.QueueItemId, "reconstruction-required-answer");

        var afterCheck = await CapturePersistenceDetailsAsync(fixture);
        var checkedQueue = afterCheck.Queues.Single();

        Assert.AreEqual(8, beforeCheck.UserVersion);
        Assert.IsNotNull(targetVariantId);
        Assert.IsTrue(check.IsCorrect);
        Assert.AreEqual(targetVariantId, check.MatchedAnswerVariantId);
        Assert.IsFalse(check.RatingWasPersisted);
        Assert.AreEqual("reconstruction-required-answer", check.EnteredAnswer);
        Assert.IsTrue(checkedQueue.AnswerRevealed);
        Assert.IsTrue(checkedQueue.SpellingChecked);
        Assert.IsTrue(checkedQueue.SpellingCorrect);
        Assert.IsFalse(checkedQueue.IsCompleted);
        Assert.AreEqual(targetVariantId, checkedQueue.TargetAnswerVariantId);
        Assert.IsEmpty(afterCheck.Reviews);
        Assert.IsEmpty(afterCheck.Progress);
        CollectionAssert.AreEqual(beforeCheck.Cards, afterCheck.Cards);
        CollectionAssert.AreEqual(beforeCheck.Sessions, afterCheck.Sessions);
        CollectionAssert.AreEqual(beforeCheck.Senses, afterCheck.Senses);
        CollectionAssert.AreEqual(beforeCheck.Words, afterCheck.Words);
        CollectionAssert.AreEqual(beforeCheck.Variants, afterCheck.Variants);
        CollectionAssert.AreEqual(beforeCheck.Assignments, afterCheck.Assignments);

        var beforeRejectFingerprint = await CapturePersistedStateAsync(fixture);
        var beforeReject = await CapturePersistenceDetailsAsync(fixture);
        var serviceB = CreateLearningService(database, LearningMode.Typing);

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => serviceB.RateAsync(graph.QueueItemId, ReviewRating.Good));

        var afterRejectFingerprint = await CapturePersistedStateAsync(fixture);
        var afterReject = await CapturePersistenceDetailsAsync(fixture);

        Assert.AreEqual(Schema8LearningDataErrorCode.MissingMatchEvidence, exception.Code);
        Assert.IsTrue(exception.IsNonRetryable);
        Assert.AreEqual(beforeRejectFingerprint, afterRejectFingerprint);
        CollectionAssert.AreEqual(beforeReject.Queues, afterReject.Queues);
        CollectionAssert.AreEqual(beforeReject.Reviews, afterReject.Reviews);
        CollectionAssert.AreEqual(beforeReject.Progress, afterReject.Progress);
        CollectionAssert.AreEqual(beforeReject.Cards, afterReject.Cards);
        CollectionAssert.AreEqual(beforeReject.Sessions, afterReject.Sessions);
        CollectionAssert.AreEqual(beforeReject.Senses, afterReject.Senses);
        CollectionAssert.AreEqual(beforeReject.Words, afterReject.Words);
        CollectionAssert.AreEqual(beforeReject.Variants, afterReject.Variants);
        CollectionAssert.AreEqual(beforeReject.Assignments, afterReject.Assignments);

        var rating = await serviceA.RateAsync(graph.QueueItemId, ReviewRating.Good);

        var final = await CapturePersistenceDetailsAsync(fixture);
        var review = final.Reviews.Single();

        Assert.IsNull(rating.Card);
        Assert.IsNotNull(rating.CompletedSummary);
        Assert.HasCount(1, final.Reviews);
        Assert.AreEqual(graph.CardId, review.CardId);
        Assert.AreEqual(graph.SessionId, review.SessionId);
        Assert.AreEqual(ReviewRating.Good, review.Rating);
        Assert.AreEqual(targetVariantId, review.TargetAnswerVariantId);
        Assert.AreEqual(targetVariantId, review.MatchedAnswerVariantId);
        Assert.HasCount(1, final.Progress);
        Assert.AreEqual(targetVariantId, final.Progress.Single().AnswerVariantId);
        Assert.IsTrue(final.Queues.Single().IsCompleted);
        Assert.AreEqual(ReviewRating.Good, final.Queues.Single().Rating);
        CollectionAssert.AreEqual(beforeCheck.Words, final.Words);
        CollectionAssert.AreEqual(beforeCheck.Variants, final.Variants);
        CollectionAssert.AreEqual(beforeCheck.Assignments, final.Assignments);
    }

    [TestMethod]
    public async Task PendingMatch_EnteredAnswerNoLongerResolves_FailsClosedWithoutMutation()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "stale-answer-term", "initial-matching-answer");
        await fixture.MigrateToSchema8Async();

        var initial = await CapturePersistenceDetailsAsync(fixture);
        var targetVariantId = initial.Queues.Single().TargetAnswerVariantId;
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var service = CreateLearningService(database, LearningMode.Typing);

        var check = await service.CheckSpellingAsync(
            graph.QueueItemId, "initial-matching-answer");
        var afterCheck = await CapturePersistenceDetailsAsync(fixture);

        Assert.AreEqual(8, initial.UserVersion);
        Assert.IsNotNull(targetVariantId);
        Assert.IsTrue(check.IsCorrect);
        Assert.AreEqual(targetVariantId, check.MatchedAnswerVariantId);
        Assert.IsFalse(check.RatingWasPersisted);
        Assert.IsEmpty(afterCheck.Reviews);
        Assert.IsEmpty(afterCheck.Progress);

        await fixture.Connection.ExecuteAsync(
            "UPDATE AnswerVariants SET DisplayText = ?, NormalizedText = ?, UpdatedAtUtc = ? WHERE Id = ?",
            "changed-nonmatching-answer", "changed-nonmatching-answer", Now.AddMinutes(1), targetVariantId);

        var beforeRejectFingerprint = await CapturePersistedStateAsync(fixture);
        var beforeReject = await CapturePersistenceDetailsAsync(fixture);
        var targetBeforeReject = beforeReject.Variants.Single(row => row.Id == targetVariantId);

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => service.RateAsync(graph.QueueItemId, ReviewRating.Good));

        var afterRejectFingerprint = await CapturePersistedStateAsync(fixture);
        var afterReject = await CapturePersistenceDetailsAsync(fixture);

        Assert.AreEqual(8, beforeReject.UserVersion);
        Assert.AreEqual(Schema8LearningDataErrorCode.InvalidMatchEvidence, exception.Code);
        Assert.IsTrue(exception.IsNonRetryable);
        Assert.AreEqual("changed-nonmatching-answer", targetBeforeReject.DisplayText);
        Assert.IsFalse(beforeReject.Variants.Any(row => row.DisplayText == "initial-matching-answer"));
        Assert.AreEqual(targetVariantId, beforeReject.Queues.Single().TargetAnswerVariantId);
        Assert.AreEqual(targetVariantId, afterReject.Queues.Single().TargetAnswerVariantId);
        Assert.AreEqual(beforeRejectFingerprint, afterRejectFingerprint);
        CollectionAssert.AreEqual(beforeReject.Queues, afterReject.Queues);
        CollectionAssert.AreEqual(beforeReject.Reviews, afterReject.Reviews);
        CollectionAssert.AreEqual(beforeReject.Progress, afterReject.Progress);
        CollectionAssert.AreEqual(beforeReject.Cards, afterReject.Cards);
        CollectionAssert.AreEqual(beforeReject.Sessions, afterReject.Sessions);
        CollectionAssert.AreEqual(beforeReject.Senses, afterReject.Senses);
        CollectionAssert.AreEqual(beforeReject.Words, afterReject.Words);
        CollectionAssert.AreEqual(beforeReject.Variants, afterReject.Variants);
        CollectionAssert.AreEqual(beforeReject.Assignments, afterReject.Assignments);
        Assert.IsEmpty(afterReject.Reviews);
        Assert.IsEmpty(afterReject.Progress);
        Assert.IsFalse(afterReject.Queues.Single().IsCompleted);
        Assert.IsTrue(afterReject.Queues.Single().SpellingChecked);
        Assert.IsTrue(afterReject.Queues.Single().SpellingCorrect);
    }

    [TestMethod]
    public async Task PendingMatch_ReResolvedVariantDiffersFromStoredMatch_FailsClosedWithoutMutation()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "different-reresolution-term", "original-answer-a");
        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        const int variantBId = 881;
        await fixture.InsertAnswerVariantAsync(
            senseId, "original-answer-b", answerLanguage: "en", id: variantBId,
            normalizedText: "original-answer-b", createdAtUtc: Now);
        await fixture.InsertAssignmentAsync(
            senseId, CardDirection.MeaningToTerm, variantBId,
            AnswerVariantRequirement.Required, isPreferred: false, requiredSinceUtc: Now,
            createdAtUtc: Now, stableId: "assignment-reresolution-variant-b");

        var initial = await CapturePersistenceDetailsAsync(fixture);
        var variantAId = initial.Queues.Single().TargetAnswerVariantId;
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var service = CreateLearningService(database, LearningMode.Typing);

        var check = await service.CheckSpellingAsync(graph.QueueItemId, "original-answer-a");
        var afterCheck = await CapturePersistenceDetailsAsync(fixture);

        Assert.AreEqual(8, initial.UserVersion);
        Assert.IsNotNull(variantAId);
        Assert.AreNotEqual(variantAId, variantBId);
        Assert.IsTrue(check.IsCorrect);
        Assert.AreEqual(variantAId, check.MatchedAnswerVariantId);
        Assert.IsFalse(check.RatingWasPersisted);
        Assert.IsEmpty(afterCheck.Reviews);
        Assert.IsEmpty(afterCheck.Progress);

        await fixture.Connection.ExecuteAsync(
            "UPDATE AnswerVariants SET DisplayText = ?, NormalizedText = ?, UpdatedAtUtc = ? WHERE Id = ?",
            "former-answer-a", "former-answer-a", Now.AddMinutes(1), variantAId);
        await fixture.Connection.ExecuteAsync(
            "UPDATE AnswerVariants SET DisplayText = ?, NormalizedText = ?, UpdatedAtUtc = ? WHERE Id = ?",
            "original-answer-a", "original-answer-a", Now.AddMinutes(1), variantBId);

        var beforeRejectFingerprint = await CapturePersistedStateAsync(fixture);
        var beforeReject = await CapturePersistenceDetailsAsync(fixture);
        var variantABeforeReject = beforeReject.Variants.Single(row => row.Id == variantAId);
        var variantBBeforeReject = beforeReject.Variants.Single(row => row.Id == variantBId);

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => service.RateAsync(graph.QueueItemId, ReviewRating.Good));

        var afterRejectFingerprint = await CapturePersistedStateAsync(fixture);
        var afterReject = await CapturePersistenceDetailsAsync(fixture);

        Assert.AreEqual(8, beforeReject.UserVersion);
        Assert.AreEqual(Schema8LearningDataErrorCode.InvalidMatchEvidence, exception.Code);
        Assert.IsTrue(exception.IsNonRetryable);
        Assert.AreEqual("former-answer-a", variantABeforeReject.DisplayText);
        Assert.AreEqual("original-answer-a", variantBBeforeReject.DisplayText);
        Assert.AreEqual(1, beforeReject.Variants.Count(row => row.DisplayText == "original-answer-a"));
        Assert.IsTrue(beforeReject.Assignments.Any(row =>
            row.SenseId == senseId
            && row.CardDirection == CardDirection.MeaningToTerm
            && row.AnswerVariantId == variantAId));
        Assert.IsTrue(beforeReject.Assignments.Any(row =>
            row.SenseId == senseId
            && row.CardDirection == CardDirection.MeaningToTerm
            && row.AnswerVariantId == variantBId));
        Assert.AreEqual(variantAId, beforeReject.Queues.Single().TargetAnswerVariantId);
        Assert.AreEqual(variantAId, afterReject.Queues.Single().TargetAnswerVariantId);
        Assert.AreEqual(beforeRejectFingerprint, afterRejectFingerprint);
        CollectionAssert.AreEqual(beforeReject.Queues, afterReject.Queues);
        CollectionAssert.AreEqual(beforeReject.Reviews, afterReject.Reviews);
        CollectionAssert.AreEqual(beforeReject.Progress, afterReject.Progress);
        CollectionAssert.AreEqual(beforeReject.Cards, afterReject.Cards);
        CollectionAssert.AreEqual(beforeReject.Sessions, afterReject.Sessions);
        CollectionAssert.AreEqual(beforeReject.Senses, afterReject.Senses);
        CollectionAssert.AreEqual(beforeReject.Words, afterReject.Words);
        CollectionAssert.AreEqual(beforeReject.Variants, afterReject.Variants);
        CollectionAssert.AreEqual(beforeReject.Assignments, afterReject.Assignments);
        Assert.IsEmpty(afterReject.Reviews);
        Assert.IsEmpty(afterReject.Progress);
        Assert.IsFalse(afterReject.Queues.Single().IsCompleted);
        Assert.IsTrue(afterReject.Queues.Single().SpellingChecked);
        Assert.IsTrue(afterReject.Queues.Single().SpellingCorrect);
    }

    [TestMethod]
    public async Task PendingMatch_DifferentMatchedVariantInvalidatesPriorEvidence()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "same-answer-replacement-term", "distinct-persisted-target");
        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        const int variantAId = 891;
        const int variantBId = 893;
        const string enteredAnswer = "same-literal-entered-answer";
        await fixture.InsertAnswerVariantAsync(
            senseId, enteredAnswer, answerLanguage: "en", id: variantAId,
            normalizedText: enteredAnswer, createdAtUtc: Now);
        await fixture.InsertAnswerVariantAsync(
            senseId, "future-variant-b-answer", answerLanguage: "en", id: variantBId,
            normalizedText: "future-variant-b-answer", createdAtUtc: Now);
        await fixture.InsertAssignmentAsync(
            senseId, CardDirection.MeaningToTerm, variantAId,
            AnswerVariantRequirement.Required, isPreferred: false, requiredSinceUtc: Now,
            createdAtUtc: Now, stableId: "assignment-same-answer-variant-a");
        await fixture.InsertAssignmentAsync(
            senseId, CardDirection.MeaningToTerm, variantBId,
            AnswerVariantRequirement.Required, isPreferred: false, requiredSinceUtc: Now,
            createdAtUtc: Now, stableId: "assignment-same-answer-variant-b");

        var initial = await CapturePersistenceDetailsAsync(fixture);
        var targetVariantId = initial.Queues.Single().TargetAnswerVariantId;
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var service = CreateLearningService(database, LearningMode.Typing);

        var firstCheck = await service.CheckSpellingAsync(graph.QueueItemId, enteredAnswer);
        var afterFirstCheck = await CapturePersistenceDetailsAsync(fixture);

        Assert.AreEqual(8, initial.UserVersion);
        Assert.IsNotNull(targetVariantId);
        Assert.AreNotEqual(targetVariantId, variantAId);
        Assert.AreNotEqual(targetVariantId, variantBId);
        Assert.IsTrue(firstCheck.IsCorrect);
        Assert.AreEqual(variantAId, firstCheck.MatchedAnswerVariantId);
        Assert.IsFalse(firstCheck.RatingWasPersisted);
        Assert.AreEqual(enteredAnswer, firstCheck.EnteredAnswer);
        Assert.IsEmpty(afterFirstCheck.Reviews);
        Assert.IsEmpty(afterFirstCheck.Progress);

        await fixture.Connection.ExecuteAsync(
            "UPDATE AnswerVariants SET DisplayText = ?, NormalizedText = ?, UpdatedAtUtc = ? WHERE Id = ?",
            "former-variant-a-answer", "former-variant-a-answer", Now.AddMinutes(1), variantAId);
        await fixture.Connection.ExecuteAsync(
            "UPDATE AnswerVariants SET DisplayText = ?, NormalizedText = ?, UpdatedAtUtc = ? WHERE Id = ?",
            enteredAnswer, enteredAnswer, Now.AddMinutes(1), variantBId);

        var beforeSecondCheck = await CapturePersistenceDetailsAsync(fixture);
        var secondCheck = await service.CheckSpellingAsync(graph.QueueItemId, enteredAnswer);
        var afterSecondCheck = await CapturePersistenceDetailsAsync(fixture);
        var checkedQueue = afterSecondCheck.Queues.Single();

        Assert.AreEqual(8, afterSecondCheck.UserVersion);
        Assert.AreEqual(1, beforeSecondCheck.Variants.Count(row => row.DisplayText == enteredAnswer));
        Assert.AreEqual("former-variant-a-answer", beforeSecondCheck.Variants.Single(row => row.Id == variantAId).DisplayText);
        Assert.AreEqual(enteredAnswer, beforeSecondCheck.Variants.Single(row => row.Id == variantBId).DisplayText);
        Assert.IsTrue(beforeSecondCheck.Assignments.Any(row =>
            row.SenseId == senseId
            && row.CardDirection == CardDirection.MeaningToTerm
            && row.AnswerVariantId == variantAId));
        Assert.IsTrue(beforeSecondCheck.Assignments.Any(row =>
            row.SenseId == senseId
            && row.CardDirection == CardDirection.MeaningToTerm
            && row.AnswerVariantId == variantBId));
        Assert.IsTrue(secondCheck.IsCorrect);
        Assert.AreEqual(variantBId, secondCheck.MatchedAnswerVariantId);
        Assert.AreNotEqual(firstCheck.MatchedAnswerVariantId, secondCheck.MatchedAnswerVariantId);
        Assert.IsFalse(secondCheck.RatingWasPersisted);
        Assert.AreEqual(enteredAnswer, secondCheck.EnteredAnswer);
        Assert.AreEqual(targetVariantId, checkedQueue.TargetAnswerVariantId);
        Assert.IsFalse(checkedQueue.IsCompleted);
        Assert.IsEmpty(afterSecondCheck.Reviews);
        Assert.IsEmpty(afterSecondCheck.Progress);
        CollectionAssert.AreEqual(beforeSecondCheck.Queues, afterSecondCheck.Queues);
        CollectionAssert.AreEqual(beforeSecondCheck.Cards, afterSecondCheck.Cards);
        CollectionAssert.AreEqual(beforeSecondCheck.Sessions, afterSecondCheck.Sessions);
        CollectionAssert.AreEqual(beforeSecondCheck.Senses, afterSecondCheck.Senses);
        CollectionAssert.AreEqual(beforeSecondCheck.Words, afterSecondCheck.Words);
        CollectionAssert.AreEqual(beforeSecondCheck.Variants, afterSecondCheck.Variants);
        CollectionAssert.AreEqual(beforeSecondCheck.Assignments, afterSecondCheck.Assignments);

        await service.RateAsync(graph.QueueItemId, ReviewRating.Good);

        var afterRating = await CapturePersistenceDetailsAsync(fixture);
        var review = afterRating.Reviews.Single();
        var targetProgress = afterRating.Progress.Single(row => row.AnswerVariantId == targetVariantId);
        var variantAProgress = afterRating.Progress.Single(row => row.AnswerVariantId == variantAId);
        var variantBProgress = afterRating.Progress.Single(row => row.AnswerVariantId == variantBId);
        var card = afterRating.Cards.Single(row => row.Id == graph.CardId);
        var session = afterRating.Sessions.Single(row => row.Id == graph.SessionId);

        Assert.HasCount(1, afterRating.Reviews);
        Assert.AreEqual(targetVariantId, review.TargetAnswerVariantId);
        Assert.AreEqual(variantBId, review.MatchedAnswerVariantId);
        Assert.AreNotEqual(variantAId, review.MatchedAnswerVariantId);
        Assert.HasCount(3, afterRating.Progress);
        Assert.AreEqual(0, targetProgress.ConsecutiveTypingSuccessCount);
        Assert.IsNull(targetProgress.LastAssessedAtUtc);
        Assert.AreEqual(0, variantAProgress.ConsecutiveTypingSuccessCount);
        Assert.AreEqual(0, variantAProgress.ConsecutiveTypingFailureCount);
        Assert.IsNull(variantAProgress.LastAssessedAtUtc);
        Assert.AreEqual(1, variantBProgress.ConsecutiveTypingSuccessCount);
        Assert.AreEqual(0, variantBProgress.ConsecutiveTypingFailureCount);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, variantBProgress.LastAssessedAtUtc));
        Assert.IsTrue(afterRating.Queues.Single().IsCompleted);
        Assert.AreEqual(ReviewRating.Good, afterRating.Queues.Single().Rating);
        Assert.AreEqual(CardState.Review, card.State);
        Assert.AreEqual(3, card.IntervalDays);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now.AddDays(3), card.DueAtUtc));
        Assert.AreEqual(LearningSessionStatus.Completed, session.Status);
        Assert.AreEqual(1, session.CompletedCards);
        Assert.AreEqual(1, session.GoodCount);
        CollectionAssert.AreEqual(beforeSecondCheck.Words, afterRating.Words);
        CollectionAssert.AreEqual(beforeSecondCheck.Variants, afterRating.Variants);
        CollectionAssert.AreEqual(beforeSecondCheck.Assignments, afterRating.Assignments);
    }

    [TestMethod]
    public async Task RateAsync_CorrectTypingWithoutPendingMatchFailsClosed()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "flags-only-term", "flags-only-required-answer");
        await fixture.MigrateToSchema8Async();

        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningSessionCards SET AnswerRevealed = 1, SpellingChecked = 1, SpellingCorrect = 1 WHERE Id = ?",
            graph.QueueItemId);

        var beforeFingerprint = await CapturePersistedStateAsync(fixture);
        var before = await CapturePersistenceDetailsAsync(fixture);
        var targetVariantId = before.Queues.Single().TargetAnswerVariantId;
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var service = CreateLearningService(database, LearningMode.Typing);

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => service.RateAsync(graph.QueueItemId, ReviewRating.Good));

        var afterFingerprint = await CapturePersistedStateAsync(fixture);
        var after = await CapturePersistenceDetailsAsync(fixture);
        var queue = after.Queues.Single();

        Assert.AreEqual(8, before.UserVersion);
        Assert.IsNotNull(targetVariantId);
        Assert.AreEqual(Schema8LearningDataErrorCode.MissingMatchEvidence, exception.Code);
        Assert.IsTrue(exception.IsNonRetryable);
        Assert.IsTrue(queue.AnswerRevealed);
        Assert.IsTrue(queue.SpellingChecked);
        Assert.IsTrue(queue.SpellingCorrect);
        Assert.IsFalse(queue.IsCompleted);
        Assert.IsNull(queue.Rating);
        Assert.IsNull(queue.CompletedAtUtc);
        Assert.AreEqual(targetVariantId, queue.TargetAnswerVariantId);
        Assert.AreEqual(beforeFingerprint, afterFingerprint);
        CollectionAssert.AreEqual(before.Queues, after.Queues);
        CollectionAssert.AreEqual(before.Reviews, after.Reviews);
        CollectionAssert.AreEqual(before.Progress, after.Progress);
        CollectionAssert.AreEqual(before.Cards, after.Cards);
        CollectionAssert.AreEqual(before.Sessions, after.Sessions);
        CollectionAssert.AreEqual(before.Senses, after.Senses);
        CollectionAssert.AreEqual(before.Words, after.Words);
        CollectionAssert.AreEqual(before.Variants, after.Variants);
        CollectionAssert.AreEqual(before.Assignments, after.Assignments);
        Assert.IsEmpty(after.Reviews);
        Assert.IsEmpty(after.Progress);
    }

    [TestMethod]
    public async Task RateAsync_MatchingPendingEvidenceIsAcceptedOnce()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "accepted-once-term", "accepted-once-persisted-target");
        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        const int matchedVariantId = 901;
        await fixture.InsertAnswerVariantAsync(
            senseId, "accepted-once-alternate-answer", answerLanguage: "en", id: matchedVariantId,
            normalizedText: "accepted-once-alternate-answer", createdAtUtc: Now);
        await fixture.InsertAssignmentAsync(
            senseId, CardDirection.MeaningToTerm, matchedVariantId,
            AnswerVariantRequirement.Required, isPreferred: false, requiredSinceUtc: Now,
            createdAtUtc: Now, stableId: "assignment-accepted-once-alternate");

        var beforeCheck = await CapturePersistenceDetailsAsync(fixture);
        var targetVariantId = beforeCheck.Queues.Single().TargetAnswerVariantId;
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var service = CreateLearningService(database, LearningMode.Typing);

        var check = await service.CheckSpellingAsync(
            graph.QueueItemId, "accepted-once-alternate-answer");
        var beforeRating = await CapturePersistenceDetailsAsync(fixture);
        var checkedQueue = beforeRating.Queues.Single();

        Assert.AreEqual(8, beforeRating.UserVersion);
        Assert.IsNotNull(targetVariantId);
        Assert.AreNotEqual(targetVariantId, matchedVariantId);
        Assert.IsTrue(check.IsCorrect);
        Assert.AreEqual(matchedVariantId, check.MatchedAnswerVariantId);
        Assert.IsFalse(check.RatingWasPersisted);
        Assert.AreEqual(targetVariantId, checkedQueue.TargetAnswerVariantId);
        Assert.IsFalse(checkedQueue.IsCompleted);
        Assert.IsEmpty(beforeRating.Reviews);
        Assert.IsEmpty(beforeRating.Progress);
        CollectionAssert.AreEqual(beforeCheck.Cards, beforeRating.Cards);
        CollectionAssert.AreEqual(beforeCheck.Sessions, beforeRating.Sessions);
        CollectionAssert.AreEqual(beforeCheck.Senses, beforeRating.Senses);
        CollectionAssert.AreEqual(beforeCheck.Words, beforeRating.Words);
        CollectionAssert.AreEqual(beforeCheck.Variants, beforeRating.Variants);
        CollectionAssert.AreEqual(beforeCheck.Assignments, beforeRating.Assignments);

        var rating = await service.RateAsync(graph.QueueItemId, ReviewRating.Hard);

        var afterRating = await CapturePersistenceDetailsAsync(fixture);
        var review = afterRating.Reviews.Single();
        var targetProgress = afterRating.Progress.Single(row => row.AnswerVariantId == targetVariantId);
        var matchedProgress = afterRating.Progress.Single(row => row.AnswerVariantId == matchedVariantId);
        var completedQueue = afterRating.Queues.Single();
        var card = afterRating.Cards.Single(row => row.Id == graph.CardId);
        var session = afterRating.Sessions.Single(row => row.Id == graph.SessionId);

        Assert.IsNull(rating.Card);
        Assert.IsNotNull(rating.CompletedSummary);
        Assert.HasCount(1, afterRating.Reviews);
        Assert.AreEqual(graph.CardId, review.CardId);
        Assert.AreEqual(graph.SessionId, review.SessionId);
        Assert.AreEqual(ReviewRating.Hard, review.Rating);
        Assert.IsTrue(review.WasTypedAnswer);
        Assert.IsTrue(review.WasCorrect);
        Assert.AreEqual(targetVariantId, review.TargetAnswerVariantId);
        Assert.AreEqual(matchedVariantId, review.MatchedAnswerVariantId);
        Assert.HasCount(2, afterRating.Progress);
        Assert.AreEqual(0, targetProgress.ConsecutiveTypingSuccessCount);
        Assert.AreEqual(0, targetProgress.ConsecutiveTypingFailureCount);
        Assert.IsNull(targetProgress.LastAssessedAtUtc);
        Assert.AreEqual(1, matchedProgress.ConsecutiveTypingSuccessCount);
        Assert.AreEqual(0, matchedProgress.ConsecutiveTypingFailureCount);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, matchedProgress.LastAssessedAtUtc));
        Assert.AreEqual(targetVariantId, completedQueue.TargetAnswerVariantId);
        Assert.IsTrue(completedQueue.IsCompleted);
        Assert.AreEqual(ReviewRating.Hard, completedQueue.Rating);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, completedQueue.CompletedAtUtc));
        Assert.AreEqual(CardState.Review, card.State);
        Assert.AreEqual(1, card.IntervalDays);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now.AddDays(1), card.DueAtUtc));
        Assert.AreEqual(1, card.SuccessfulReviewCount);
        Assert.AreEqual(LearningSessionStatus.Completed, session.Status);
        Assert.AreEqual(1, session.TotalCards);
        Assert.AreEqual(1, session.CompletedCards);
        Assert.AreEqual(1, session.HardCount);
        Assert.AreEqual(0, session.GoodCount);
        CollectionAssert.AreEqual(beforeCheck.Words, afterRating.Words);
        CollectionAssert.AreEqual(beforeCheck.Variants, afterRating.Variants);
        CollectionAssert.AreEqual(beforeCheck.Assignments, afterRating.Assignments);
    }

    [TestMethod]
    public async Task RateAsync_ReusedPendingEvidenceIsRejected()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "reused-evidence-term", "reused-evidence-target");
        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        const int matchedVariantId = 911;
        await fixture.InsertAnswerVariantAsync(
            senseId, "reused-evidence-alternate", answerLanguage: "en", id: matchedVariantId,
            normalizedText: "reused-evidence-alternate", createdAtUtc: Now);
        await fixture.InsertAssignmentAsync(
            senseId, CardDirection.MeaningToTerm, matchedVariantId,
            AnswerVariantRequirement.Required, isPreferred: false, requiredSinceUtc: Now,
            createdAtUtc: Now, stableId: "assignment-reused-evidence-alternate");

        var initial = await CapturePersistenceDetailsAsync(fixture);
        var targetVariantId = initial.Queues.Single().TargetAnswerVariantId;
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var service = CreateLearningService(database, LearningMode.Typing);

        var check = await service.CheckSpellingAsync(
            graph.QueueItemId, "reused-evidence-alternate");
        var firstRating = await service.RateAsync(graph.QueueItemId, ReviewRating.Good);

        var afterFirstFingerprint = await CapturePersistedStateAsync(fixture);
        var afterFirst = await CapturePersistenceDetailsAsync(fixture);
        var firstReview = afterFirst.Reviews.Single();

        Assert.AreEqual(8, afterFirst.UserVersion);
        Assert.IsNotNull(targetVariantId);
        Assert.IsTrue(check.IsCorrect);
        Assert.AreEqual(matchedVariantId, check.MatchedAnswerVariantId);
        Assert.IsFalse(check.RatingWasPersisted);
        Assert.IsNull(firstRating.Card);
        Assert.IsNotNull(firstRating.CompletedSummary);
        Assert.HasCount(1, afterFirst.Reviews);
        Assert.AreEqual(ReviewRating.Good, firstReview.Rating);
        Assert.AreEqual(targetVariantId, firstReview.TargetAnswerVariantId);
        Assert.AreEqual(matchedVariantId, firstReview.MatchedAnswerVariantId);
        Assert.IsTrue(afterFirst.Queues.Single().IsCompleted);
        Assert.AreEqual(LearningSessionStatus.Completed, afterFirst.Sessions.Single().Status);

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => service.RateAsync(graph.QueueItemId, ReviewRating.Hard));

        var afterRejectFingerprint = await CapturePersistedStateAsync(fixture);
        var afterReject = await CapturePersistenceDetailsAsync(fixture);

        Assert.AreEqual(Schema8LearningDataErrorCode.SessionNotActive, exception.Code);
        Assert.IsTrue(exception.IsNonRetryable);
        Assert.AreEqual(afterFirstFingerprint, afterRejectFingerprint);
        CollectionAssert.AreEqual(afterFirst.Queues, afterReject.Queues);
        CollectionAssert.AreEqual(afterFirst.Reviews, afterReject.Reviews);
        CollectionAssert.AreEqual(afterFirst.Progress, afterReject.Progress);
        CollectionAssert.AreEqual(afterFirst.Cards, afterReject.Cards);
        CollectionAssert.AreEqual(afterFirst.Sessions, afterReject.Sessions);
        CollectionAssert.AreEqual(afterFirst.Senses, afterReject.Senses);
        CollectionAssert.AreEqual(afterFirst.Words, afterReject.Words);
        CollectionAssert.AreEqual(afterFirst.Variants, afterReject.Variants);
        CollectionAssert.AreEqual(afterFirst.Assignments, afterReject.Assignments);
        Assert.HasCount(1, afterReject.Reviews);
    }

    [TestMethod]
    public async Task PendingMatch_SuccessfulRatingClearsEvidence()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "successful-clear-term", "successful-clear-answer");
        await fixture.MigrateToSchema8Async();

        var initial = await CapturePersistenceDetailsAsync(fixture);
        var targetVariantId = initial.Queues.Single().TargetAnswerVariantId;
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var service = CreateLearningService(database, LearningMode.Typing);

        var check = await service.CheckSpellingAsync(graph.QueueItemId, "successful-clear-answer");
        await service.RateAsync(graph.QueueItemId, ReviewRating.Good);
        var afterFirstRating = await CapturePersistenceDetailsAsync(fixture);

        Assert.AreEqual(8, afterFirstRating.UserVersion);
        Assert.IsNotNull(targetVariantId);
        Assert.IsTrue(check.IsCorrect);
        Assert.AreEqual(targetVariantId, check.MatchedAnswerVariantId);
        Assert.HasCount(1, afterFirstRating.Reviews);
        Assert.HasCount(1, afterFirstRating.Progress);
        Assert.IsTrue(afterFirstRating.Queues.Single().IsCompleted);
        Assert.AreEqual(ReviewRating.Good, afterFirstRating.Queues.Single().Rating);
        Assert.AreEqual(CardState.Review, afterFirstRating.Cards.Single().State);
        Assert.AreEqual(LearningSessionStatus.Completed, afterFirstRating.Sessions.Single().Status);

        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningSessionCards SET AnswerRevealed = 1, SpellingChecked = 1, SpellingCorrect = 1, IsCompleted = 0, Rating = NULL, CompletedAtUtc = NULL WHERE Id = ?",
            graph.QueueItemId);
        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningSessions SET Status = ?, CompletedCards = 0, CompletedAtUtc = NULL WHERE Id = ?",
            (int)LearningSessionStatus.Active, graph.SessionId);

        var beforeRejectFingerprint = await CapturePersistedStateAsync(fixture);
        var beforeReject = await CapturePersistenceDetailsAsync(fixture);

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => service.RateAsync(graph.QueueItemId, ReviewRating.Hard));

        var afterRejectFingerprint = await CapturePersistedStateAsync(fixture);
        var afterReject = await CapturePersistenceDetailsAsync(fixture);

        Assert.AreEqual(8, beforeReject.UserVersion);
        Assert.AreEqual(Schema8LearningDataErrorCode.MissingMatchEvidence, exception.Code);
        Assert.IsTrue(exception.IsNonRetryable);
        Assert.AreEqual(targetVariantId, beforeReject.Queues.Single().TargetAnswerVariantId);
        Assert.IsFalse(beforeReject.Queues.Single().IsCompleted);
        Assert.IsTrue(beforeReject.Queues.Single().SpellingChecked);
        Assert.IsTrue(beforeReject.Queues.Single().SpellingCorrect);
        Assert.AreEqual(LearningSessionStatus.Active, beforeReject.Sessions.Single().Status);
        Assert.HasCount(1, beforeReject.Reviews);
        Assert.HasCount(1, beforeReject.Progress);
        Assert.AreEqual(beforeRejectFingerprint, afterRejectFingerprint);
        CollectionAssert.AreEqual(beforeReject.Queues, afterReject.Queues);
        CollectionAssert.AreEqual(beforeReject.Reviews, afterReject.Reviews);
        CollectionAssert.AreEqual(beforeReject.Progress, afterReject.Progress);
        CollectionAssert.AreEqual(beforeReject.Cards, afterReject.Cards);
        CollectionAssert.AreEqual(beforeReject.Sessions, afterReject.Sessions);
        CollectionAssert.AreEqual(beforeReject.Senses, afterReject.Senses);
        CollectionAssert.AreEqual(beforeReject.Words, afterReject.Words);
        CollectionAssert.AreEqual(beforeReject.Variants, afterReject.Variants);
        CollectionAssert.AreEqual(beforeReject.Assignments, afterReject.Assignments);
        Assert.HasCount(1, afterReject.Reviews);
    }

    [TestMethod]
    public async Task PendingMatch_WrongAnswerClearsPriorCorrectEvidence()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "wrong-clear-term", "wrong-clear-target");
        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        const int priorMatchedVariantId = 921;
        const string wrongAnswer = "globally-absent-wrong-clear-answer";
        await fixture.InsertAnswerVariantAsync(
            senseId, "prior-correct-alternate", answerLanguage: "en", id: priorMatchedVariantId,
            normalizedText: "prior-correct-alternate", createdAtUtc: Now);
        await fixture.InsertAssignmentAsync(
            senseId, CardDirection.MeaningToTerm, priorMatchedVariantId,
            AnswerVariantRequirement.Required, isPreferred: false, requiredSinceUtc: Now,
            createdAtUtc: Now, stableId: "assignment-prior-correct-alternate");

        var initial = await CapturePersistenceDetailsAsync(fixture);
        var targetVariantId = initial.Queues.Single().TargetAnswerVariantId;
        var wrongAnswerVariantCount = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM AnswerVariants WHERE DisplayText = ? OR NormalizedText = ?",
            wrongAnswer, wrongAnswer);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var service = CreateLearningService(database, LearningMode.Typing);

        var correct = await service.CheckSpellingAsync(
            graph.QueueItemId, "prior-correct-alternate");
        var afterCorrect = await CapturePersistenceDetailsAsync(fixture);

        Assert.AreEqual(0, wrongAnswerVariantCount);
        Assert.IsNotNull(targetVariantId);
        Assert.IsTrue(correct.IsCorrect);
        Assert.AreEqual(priorMatchedVariantId, correct.MatchedAnswerVariantId);
        Assert.IsFalse(correct.RatingWasPersisted);
        Assert.IsEmpty(afterCorrect.Reviews);
        Assert.IsEmpty(afterCorrect.Progress);

        var incorrect = await service.CheckSpellingAsync(graph.QueueItemId, wrongAnswer);
        var afterIncorrect = await CapturePersistenceDetailsAsync(fixture);
        var review = afterIncorrect.Reviews.Single();
        var targetProgress = afterIncorrect.Progress.Single(row => row.AnswerVariantId == targetVariantId);
        var priorMatchedProgress = afterIncorrect.Progress.Single(row => row.AnswerVariantId == priorMatchedVariantId);
        var completedQueue = afterIncorrect.Queues.Single(row => row.Id == graph.QueueItemId);
        var repeatQueue = afterIncorrect.Queues.Single(row => row.Id != graph.QueueItemId);

        Assert.IsFalse(incorrect.IsCorrect);
        Assert.IsTrue(incorrect.RatingWasPersisted);
        Assert.IsNull(incorrect.MatchedAnswerVariantId);
        Assert.HasCount(1, afterIncorrect.Reviews);
        Assert.AreEqual(ReviewRating.Again, review.Rating);
        Assert.IsTrue(review.WasTypedAnswer);
        Assert.IsFalse(review.WasCorrect);
        Assert.AreEqual(targetVariantId, review.TargetAnswerVariantId);
        Assert.IsNull(review.MatchedAnswerVariantId);
        Assert.AreEqual(1, targetProgress.ConsecutiveTypingFailureCount);
        Assert.IsTrue(Schema8Utc.AreSameInstant(Now, targetProgress.LastAssessedAtUtc));
        Assert.AreEqual(0, priorMatchedProgress.ConsecutiveTypingSuccessCount);
        Assert.AreEqual(0, priorMatchedProgress.ConsecutiveTypingFailureCount);
        Assert.IsNull(priorMatchedProgress.LastAssessedAtUtc);
        Assert.IsTrue(completedQueue.IsCompleted);
        Assert.AreEqual(ReviewRating.Again, completedQueue.Rating);
        Assert.IsTrue(repeatQueue.IsAgainRepeat);
        Assert.IsFalse(repeatQueue.IsCompleted);
        Assert.AreEqual(targetVariantId, repeatQueue.TargetAnswerVariantId);
        Assert.AreEqual(CardState.Learning, afterIncorrect.Cards.Single().State);
        Assert.AreEqual(2, afterIncorrect.Sessions.Single().TotalCards);
        Assert.AreEqual(1, afterIncorrect.Sessions.Single().CompletedCards);
        Assert.AreEqual(1, afterIncorrect.Sessions.Single().AgainCount);

        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningSessionCards SET AnswerRevealed = 1, SpellingChecked = 1, SpellingCorrect = 1, IsCompleted = 0, Rating = NULL, CompletedAtUtc = NULL WHERE Id = ?",
            graph.QueueItemId);
        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningSessions SET Status = ?, CompletedCards = 0, CompletedAtUtc = NULL WHERE Id = ?",
            (int)LearningSessionStatus.Active, graph.SessionId);

        var beforeRejectFingerprint = await CapturePersistedStateAsync(fixture);
        var beforeReject = await CapturePersistenceDetailsAsync(fixture);

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => service.RateAsync(graph.QueueItemId, ReviewRating.Good));

        var afterRejectFingerprint = await CapturePersistedStateAsync(fixture);
        var afterReject = await CapturePersistenceDetailsAsync(fixture);

        Assert.AreEqual(8, beforeReject.UserVersion);
        Assert.AreEqual(Schema8LearningDataErrorCode.MissingMatchEvidence, exception.Code);
        Assert.IsTrue(exception.IsNonRetryable);
        Assert.AreEqual(targetVariantId, beforeReject.Queues.Single(row => row.Id == graph.QueueItemId).TargetAnswerVariantId);
        Assert.HasCount(2, beforeReject.Queues);
        Assert.HasCount(1, beforeReject.Reviews);
        Assert.HasCount(2, beforeReject.Progress);
        Assert.AreEqual(beforeRejectFingerprint, afterRejectFingerprint);
        CollectionAssert.AreEqual(beforeReject.Queues, afterReject.Queues);
        CollectionAssert.AreEqual(beforeReject.Reviews, afterReject.Reviews);
        CollectionAssert.AreEqual(beforeReject.Progress, afterReject.Progress);
        CollectionAssert.AreEqual(beforeReject.Cards, afterReject.Cards);
        CollectionAssert.AreEqual(beforeReject.Sessions, afterReject.Sessions);
        CollectionAssert.AreEqual(beforeReject.Senses, afterReject.Senses);
        CollectionAssert.AreEqual(beforeReject.Words, afterReject.Words);
        CollectionAssert.AreEqual(beforeReject.Variants, afterReject.Variants);
        CollectionAssert.AreEqual(beforeReject.Assignments, afterReject.Assignments);
        Assert.HasCount(1, afterReject.Reviews);
    }

    [TestMethod]
    public async Task CheckSpelling_RequiresMeaningToTermDirection()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "direction-gate-term", "direction-gate-answer");
        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningCards SET Direction = ? WHERE Id = ?",
            (int)CardDirection.TermToMeaning, graph.CardId);
        await fixture.Connection.ExecuteAsync(
            "UPDATE SenseAnswerVariantAssignments SET CardDirection = ? WHERE SenseId = ? AND CardDirection = ?",
            (int)CardDirection.TermToMeaning, senseId, (int)CardDirection.MeaningToTerm);

        var beforeFingerprint = await CapturePersistedStateAsync(fixture);
        var before = await CapturePersistenceDetailsAsync(fixture);
        var queueBefore = before.Queues.Single();
        var cardBefore = before.Cards.Single();
        var targetVariantId = queueBefore.TargetAnswerVariantId;
        var targetVariant = before.Variants.Single(row => row.Id == targetVariantId);
        var targetAssignment = before.Assignments.Single(row => row.AnswerVariantId == targetVariantId);
        var database = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture);
        var service = CreateLearningService(database, LearningMode.Typing);

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => service.CheckSpellingAsync(graph.QueueItemId, "direction-gate-answer"));

        var afterFingerprint = await CapturePersistedStateAsync(fixture);
        var after = await CapturePersistenceDetailsAsync(fixture);

        Assert.AreEqual(8, before.UserVersion);
        Assert.AreEqual(Schema8LearningDataErrorCode.InvalidQueueState, exception.Code);
        Assert.IsTrue(exception.IsNonRetryable);
        Assert.AreEqual(CardDirection.TermToMeaning, cardBefore.Direction);
        Assert.IsNotNull(targetVariantId);
        Assert.AreEqual("direction-gate-answer", targetVariant.DisplayText);
        Assert.AreEqual(senseId, targetAssignment.SenseId);
        Assert.AreEqual(CardDirection.TermToMeaning, targetAssignment.CardDirection);
        Assert.AreEqual(AnswerVariantRequirement.Required, targetAssignment.Requirement);
        Assert.IsTrue(targetAssignment.IsPreferred);
        Assert.IsFalse(queueBefore.AnswerRevealed);
        Assert.IsFalse(queueBefore.SpellingChecked);
        Assert.IsFalse(queueBefore.SpellingCorrect);
        Assert.IsFalse(queueBefore.IsCompleted);
        Assert.IsNull(queueBefore.Rating);
        Assert.IsEmpty(before.Reviews);
        Assert.IsEmpty(before.Progress);
        Assert.AreEqual(beforeFingerprint, afterFingerprint);
        CollectionAssert.AreEqual(before.Queues, after.Queues);
        CollectionAssert.AreEqual(before.Reviews, after.Reviews);
        CollectionAssert.AreEqual(before.Progress, after.Progress);
        CollectionAssert.AreEqual(before.Cards, after.Cards);
        CollectionAssert.AreEqual(before.Sessions, after.Sessions);
        CollectionAssert.AreEqual(before.Senses, after.Senses);
        CollectionAssert.AreEqual(before.Words, after.Words);
        CollectionAssert.AreEqual(before.Variants, after.Variants);
        CollectionAssert.AreEqual(before.Assignments, after.Assignments);
        Assert.HasCount(1, after.Queues);
        Assert.IsEmpty(after.Reviews);
        Assert.IsEmpty(after.Progress);
    }

    [TestMethod]
    public async Task ResumeActiveSession_Schema8_CrossSenseAssignmentFailsClosedWithoutMutation()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "cross-sense-assignment-term", "valid-frozen-target");
        await fixture.MigrateToSchema8Async();

        var currentSenseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        const int otherSenseId = 951;
        const int crossSenseVariantId = 953;
        await fixture.InsertSenseAsync(
            graph.WordId, id: otherSenseId, createdAtUtc: Now, updatedAtUtc: Now);
        await fixture.InsertAnswerVariantAsync(
            otherSenseId, "cross-sense-variant", id: crossSenseVariantId, createdAtUtc: Now);
        await fixture.InsertAssignmentAsync(
            currentSenseId, CardDirection.MeaningToTerm, crossSenseVariantId,
            AnswerVariantRequirement.AcceptedOnly, isPreferred: false, requiredSinceUtc: null,
            createdAtUtc: Now, stableId: "cross-sense-raw-assignment");

        var queue = (await ReadQueueRowsAsync(fixture)).Single();
        var frozenTarget = queue.TargetAnswerVariantId
            ?? throw new AssertFailedException("The valid frozen queue target is missing.");
        Assert.AreNotEqual(crossSenseVariantId, frozenTarget);
        Assert.AreEqual(
            currentSenseId,
            await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT SenseId FROM AnswerVariants WHERE Id = ?", frozenTarget));

        var before = await CapturePersistedStateAsync(fixture);
        var beforeDetails = await CapturePersistenceDetailsAsync(fixture);
        var service = CreateLearningService(
            new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture));

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => service.GetOrStartAsync());

        var after = await CapturePersistedStateAsync(fixture);
        var afterDetails = await CapturePersistenceDetailsAsync(fixture);

        Assert.AreEqual(Schema8LearningDataErrorCode.InvalidAssignmentGraph, exception.Code);
        Assert.AreEqual(before, after);
        CollectionAssert.AreEqual(beforeDetails.Queues, afterDetails.Queues);
        CollectionAssert.AreEqual(beforeDetails.Reviews, afterDetails.Reviews);
        CollectionAssert.AreEqual(beforeDetails.Progress, afterDetails.Progress);
        CollectionAssert.AreEqual(beforeDetails.Cards, afterDetails.Cards);
        CollectionAssert.AreEqual(beforeDetails.Sessions, afterDetails.Sessions);
        CollectionAssert.AreEqual(beforeDetails.Senses, afterDetails.Senses);
        CollectionAssert.AreEqual(beforeDetails.Words, afterDetails.Words);
        CollectionAssert.AreEqual(beforeDetails.Variants, afterDetails.Variants);
        CollectionAssert.AreEqual(beforeDetails.Assignments, afterDetails.Assignments);
        Assert.IsEmpty(afterDetails.Reviews);
        Assert.IsEmpty(afterDetails.Progress);
        Assert.AreEqual(frozenTarget, afterDetails.Queues.Single().TargetAnswerVariantId);
    }

    [TestMethod]
    public async Task ResumeActiveSession_Schema8_CrossWordSenseFailsClosedWithoutMutation()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var graph = await SeedSchema7GraphAsync(
            fixture, "card-word", "cross-word-sense-target");
        await fixture.MigrateToSchema8Async();

        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", graph.CardId);
        var otherWordId = await fixture.InsertWordAsync(
            "sense-word", status: WordStatus.Prepared, createdAt: Now, updatedAt: Now);
        await fixture.Connection.ExecuteAsync(
            "UPDATE Senses SET WordId = ? WHERE Id = ?", otherWordId, senseId);

        Assert.AreEqual(
            graph.WordId,
            await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT WordId FROM LearningCards WHERE Id = ?", graph.CardId));
        Assert.AreEqual(
            otherWordId,
            await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT WordId FROM Senses WHERE Id = ?", senseId));

        var before = await CapturePersistedStateAsync(fixture);
        var beforeDetails = await CapturePersistenceDetailsAsync(fixture);
        var service = CreateLearningService(
            new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture));

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => service.GetOrStartAsync());

        var after = await CapturePersistedStateAsync(fixture);
        var afterDetails = await CapturePersistenceDetailsAsync(fixture);

        Assert.AreEqual(Schema8LearningDataErrorCode.InvalidCardGraph, exception.Code);
        Assert.AreEqual(before, after);
        CollectionAssert.AreEqual(beforeDetails.Queues, afterDetails.Queues);
        CollectionAssert.AreEqual(beforeDetails.Reviews, afterDetails.Reviews);
        CollectionAssert.AreEqual(beforeDetails.Progress, afterDetails.Progress);
        CollectionAssert.AreEqual(beforeDetails.Cards, afterDetails.Cards);
        CollectionAssert.AreEqual(beforeDetails.Sessions, afterDetails.Sessions);
        CollectionAssert.AreEqual(beforeDetails.Senses, afterDetails.Senses);
        CollectionAssert.AreEqual(beforeDetails.Words, afterDetails.Words);
        CollectionAssert.AreEqual(beforeDetails.Variants, afterDetails.Variants);
        CollectionAssert.AreEqual(beforeDetails.Assignments, afterDetails.Assignments);
        Assert.IsEmpty(afterDetails.Reviews);
        Assert.IsEmpty(afterDetails.Progress);
    }

    private static LearningService CreateLearningService(
        Schema8BackupFixtureBuilders.Schema8DatabaseAdapter database,
        LearningMode? learningMode = null) => learningMode.HasValue
            ? new LearningService(
                database,
                new SimpleSpacedRepetitionScheduler(),
                new SpellingAnswerComparer(),
                new FakeClock(Now),
                new FixedAppSettings(learningMode.Value))
            : new LearningService(
                database,
                new SimpleSpacedRepetitionScheduler(),
                new SpellingAnswerComparer(),
                new FakeClock(Now));

    private static async Task<SeededGraph> SeedSchema7GraphAsync(
        Schema7Fixture fixture,
        string canonicalTerm,
        string displayTerm)
    {
        var wordId = await fixture.InsertWordAsync(
            canonicalTerm,
            status: WordStatus.Prepared,
            tokenKind: TokenKind.Word,
            automaticInteractionMode: LearningInteractionMode.Reading,
            createdAt: Now,
            updatedAt: Now);
        var meaningId = await fixture.InsertMeaningAsync(
            wordId,
            displayTerm: displayTerm,
            translation: "deterministic-explanation",
            createdAt: Now,
            updatedAt: Now);
        var cardId = await fixture.InsertCardAsync(
            wordId,
            meaningId,
            CardDirection.MeaningToTerm,
            state: CardState.New,
            dueAtUtc: Now,
            createdAtUtc: Now,
            updatedAtUtc: Now,
            id: 40);
        var sessionId = await fixture.InsertLearningSessionAsync(
            LearningSessionStatus.Active,
            totalCards: 1,
            completedCards: 0,
            startedAtUtc: Now,
            updatedAtUtc: Now);
        var queueItemId = await fixture.InsertQueueItemAsync(sessionId, cardId, queueOrder: 0);
        return new SeededGraph(wordId, meaningId, cardId, sessionId, queueItemId);
    }

    private static async Task<SeededSession> SeedTwoCardSessionAsync(Schema7Fixture fixture)
    {
        async Task<int> SeedCardAsync(int cardId, string term, string answer)
        {
            var wordId = await fixture.InsertWordAsync(
                term, status: WordStatus.Prepared, tokenKind: TokenKind.Word,
                createdAt: Now, updatedAt: Now);
            var meaningId = await fixture.InsertMeaningAsync(
                wordId, displayTerm: answer, translation: $"explanation-{cardId}",
                createdAt: Now, updatedAt: Now);
            return await fixture.InsertCardAsync(
                wordId, meaningId, CardDirection.MeaningToTerm,
                state: CardState.New, dueAtUtc: Now,
                createdAtUtc: Now, updatedAtUtc: Now, id: cardId);
        }

        var firstCardId = await SeedCardAsync(40, "first-term", "first-target-answer");
        var secondCardId = await SeedCardAsync(41, "second-term", "second-target-answer");
        var sessionId = await fixture.InsertLearningSessionAsync(
            LearningSessionStatus.Active, totalCards: 2, completedCards: 0,
            startedAtUtc: Now, updatedAtUtc: Now);
        var firstQueueId = await fixture.InsertQueueItemAsync(sessionId, firstCardId, queueOrder: 0);
        var secondQueueId = await fixture.InsertQueueItemAsync(sessionId, secondCardId, queueOrder: 1);
        return new SeededSession(sessionId, firstQueueId, secondQueueId);
    }

    private static async Task<List<QueueState>> ReadQueueRowsAsync(Schema7Fixture fixture)
    {
        List<Schema8QueueTargetRow>? rows = null;
        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            rows = connection.Query<Schema8QueueTargetRow>(
                "SELECT Id, SessionId, CardId, QueueOrder, IsDueCard, IsAgainRepeat, AnswerRevealed, SpellingChecked, SpellingCorrect, IsCompleted, Rating, CompletedAtUtc, TargetAnswerVariantId FROM LearningSessionCards ORDER BY QueueOrder, Id");
        });
        return rows!.Select(row => new QueueState(
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
            row.CompletedAtUtc,
            row.TargetAnswerVariantId)).ToList();
    }

    private static async Task<IReadOnlyDictionary<int, string>> ReadTargetTextsAsync(Schema7Fixture fixture)
    {
        List<QueueTargetTextRow>? rows = null;
        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            rows = connection.Query<QueueTargetTextRow>(
                "SELECT q.Id AS QueueItemId, v.DisplayText AS DisplayText FROM LearningSessionCards q JOIN AnswerVariants v ON v.Id = q.TargetAnswerVariantId ORDER BY q.QueueOrder, q.Id");
        });
        return rows!.ToDictionary(row => row.QueueItemId, row => row.DisplayText);
    }

    private static async Task<PersistenceDetails> CapturePersistenceDetailsAsync(Schema7Fixture fixture)
    {
        PersistenceDetails? details = null;
        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            var queues = connection.Query<Schema8QueueTargetRow>(
                "SELECT Id, SessionId, CardId, QueueOrder, IsDueCard, IsAgainRepeat, AnswerRevealed, SpellingChecked, SpellingCorrect, IsCompleted, Rating, CompletedAtUtc, TargetAnswerVariantId FROM LearningSessionCards ORDER BY QueueOrder, Id")
                .Select(row => new QueueState(
                    row.Id, row.SessionId, row.CardId, row.QueueOrder, row.IsDueCard, row.IsAgainRepeat,
                    row.AnswerRevealed, row.SpellingChecked, row.SpellingCorrect, row.IsCompleted,
                    row.Rating, row.CompletedAtUtc, row.TargetAnswerVariantId))
                .ToList();
            var reviews = connection.Query<Schema8ReviewRow>(
                "SELECT Id, CardId, SessionId, Rating, WasTypedAnswer, WasCorrect, ReviewedAtUtc, DueAtUtc, IntervalDays, EaseFactor, TargetAnswerVariantId, MatchedAnswerVariantId FROM LearningReviews ORDER BY Id")
                .Select(row => new ReviewState(
                    row.Id, row.CardId, row.SessionId, row.Rating, row.WasTypedAnswer, row.WasCorrect,
                    row.ReviewedAtUtc, row.DueAtUtc, row.IntervalDays, row.EaseFactor,
                    row.TargetAnswerVariantId, row.MatchedAnswerVariantId))
                .ToList();
            var progress = connection.Query<AnswerVariantProgressRow>(
                "SELECT Id, CardId, AnswerVariantId, InteractionMode, ConsecutiveReadingSuccessCount, ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, LastAssessedAtUtc, MasteryReviewExtensionScheduled, IsMastered, ReplayVersion, CreatedAtUtc, UpdatedAtUtc FROM AnswerVariantProgress ORDER BY CardId, AnswerVariantId")
                .Select(row => new ProgressState(
                    row.Id, row.CardId, row.AnswerVariantId, row.InteractionMode,
                    row.ConsecutiveReadingSuccessCount, row.ConsecutiveTypingSuccessCount,
                    row.ConsecutiveTypingFailureCount, row.LastAssessedAtUtc,
                    row.MasteryReviewExtensionScheduled, row.IsMastered, row.ReplayVersion,
                    row.CreatedAtUtc, row.UpdatedAtUtc))
                .ToList();
            var cards = connection.Query<Schema8CardRow>(
                "SELECT Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc FROM LearningCards ORDER BY Id")
                .Select(row => new CardStateSnapshot(
                    row.Id, row.WordId, row.SenseId, row.PreferredMeaningId, row.Direction, row.State,
                    row.DueAtUtc, row.IntervalDays, row.EaseFactor, row.SuccessfulReviewCount,
                    row.LapseCount, row.LastReviewedAtUtc, row.LastRating, row.CreatedAtUtc, row.UpdatedAtUtc))
                .ToList();
            var sessions = connection.Query<Schema8SessionCounterRow>(
                "SELECT Id, Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc FROM LearningSessions ORDER BY Id")
                .Select(row => new SessionState(
                    row.Id, row.Status, row.TotalCards, row.CompletedCards, row.AgainCount,
                    row.HardCount, row.GoodCount, row.EasyCount, row.StartedAtUtc,
                    row.UpdatedAtUtc, row.CompletedAtUtc))
                .ToList();
            var senses = connection.Query<SenseRow>(
                "SELECT Id, StableId, WordId, SourceLanguage, ExplanationLanguage, ProviderSenseId, TopicOrDomain, PartOfSpeech, GrammaticalRelationship, AcronymExpansion, DefaultMeaningId, Status, CreatedAtUtc, UpdatedAtUtc FROM Senses ORDER BY Id")
                .Select(row => new SenseStateSnapshot(
                    row.Id, row.StableId, row.WordId, row.SourceLanguage, row.ExplanationLanguage,
                    row.ProviderSenseId, row.TopicOrDomain, row.PartOfSpeech,
                    row.GrammaticalRelationship, row.AcronymExpansion, row.DefaultMeaningId,
                    row.Status, row.CreatedAtUtc, row.UpdatedAtUtc))
                .ToList();
            var words = connection.Query<WordStateRow>(
                "SELECT Id, Status, AutomaticInteractionMode, ConsecutiveRecallSuccessCount, ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, MasteryReviewExtensionScheduled, UpdatedAt FROM Words ORDER BY Id")
                .Select(row => new WordStateSnapshot(
                    row.Id, row.Status, row.AutomaticInteractionMode,
                    row.ConsecutiveRecallSuccessCount, row.ConsecutiveTypingSuccessCount,
                    row.ConsecutiveTypingFailureCount, row.MasteryReviewExtensionScheduled, row.UpdatedAt))
                .ToList();
            var variants = connection.Query<AnswerVariantRow>(
                "SELECT Id, StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText, SourceMeaningId, CreatedAtUtc, UpdatedAtUtc FROM AnswerVariants ORDER BY Id")
                .Select(row => new VariantState(
                    row.Id, row.StableId, row.SenseId, row.AnswerLanguage, row.DisplayText,
                    row.NormalizedText, row.SourceMeaningId, row.CreatedAtUtc, row.UpdatedAtUtc))
                .ToList();
            var assignments = connection.Query<SenseAnswerVariantAssignmentRow>(
                "SELECT Id, StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred, RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc FROM SenseAnswerVariantAssignments ORDER BY Id")
                .Select(row => new AssignmentState(
                    row.Id, row.StableId, row.SenseId, row.CardDirection, row.AnswerVariantId,
                    row.Requirement, row.IsPreferred, row.RequiredSinceUtc,
                    row.CreatedAtUtc, row.UpdatedAtUtc))
                .ToList();

            details = new PersistenceDetails(
                connection.ExecuteScalar<int>("PRAGMA user_version"), queues, reviews, progress,
                cards, sessions, senses, words, variants, assignments);
        });
        return details!;
    }

    private static async Task<PersistedState> CapturePersistedStateAsync(Schema7Fixture fixture)
    {
        await fixture.ReopenAsync();
        var databaseHash = ComputeSha256(fixture.DatabasePath);
        PersistedState? state = null;
        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            static string Fingerprint(SQLite.SQLiteConnection connection, string select) =>
                connection.ExecuteScalar<string?>($"SELECT group_concat(Value, char(10)) FROM ({select})") ?? string.Empty;

            var sessionId = connection.ExecuteScalar<int>(
                "SELECT Id FROM LearningSessions ORDER BY Id LIMIT 1");
            state = new PersistedState(
                connection.ExecuteScalar<int>("PRAGMA user_version"),
                connection.ExecuteScalar<int>("PRAGMA schema_version"),
                databaseHash,
                sessionId,
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningSessions"),
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningSessionCards"),
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM AnswerVariants"),
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM SenseAnswerVariantAssignments"),
                Fingerprint(connection, "SELECT quote(Id)||'|'||quote(SessionId)||'|'||quote(CardId)||'|'||quote(QueueOrder)||'|'||quote(IsDueCard)||'|'||quote(IsAgainRepeat)||'|'||quote(AnswerRevealed)||'|'||quote(SpellingChecked)||'|'||quote(SpellingCorrect)||'|'||quote(IsCompleted)||'|'||quote(Rating)||'|'||quote(CompletedAtUtc)||'|'||quote(TargetAnswerVariantId) AS Value FROM LearningSessionCards ORDER BY QueueOrder, Id"),
                Fingerprint(connection, "SELECT quote(Id)||'|'||quote(SessionId)||'|'||quote(CardId)||'|'||quote(QueueOrder)||'|'||quote(IsDueCard)||'|'||quote(IsAgainRepeat)||'|'||quote(IsCompleted)||'|'||quote(Rating)||'|'||quote(CompletedAtUtc) AS Value FROM LearningSessionCards ORDER BY QueueOrder, Id"),
                Fingerprint(connection, "SELECT quote(Id)||'|'||quote(TargetAnswerVariantId) AS Value FROM LearningSessionCards ORDER BY QueueOrder, Id"),
                Fingerprint(connection, "SELECT quote(Id)||'|'||quote(Status)||'|'||quote(TotalCards)||'|'||quote(CompletedCards)||'|'||quote(AgainCount)||'|'||quote(HardCount)||'|'||quote(GoodCount)||'|'||quote(EasyCount)||'|'||quote(StartedAtUtc)||'|'||quote(UpdatedAtUtc)||'|'||quote(CompletedAtUtc) AS Value FROM LearningSessions ORDER BY Id"),
                Fingerprint(connection, "SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(SenseId)||'|'||quote(PreferredMeaningId)||'|'||quote(Direction)||'|'||quote(State)||'|'||quote(DueAtUtc)||'|'||quote(IntervalDays)||'|'||quote(EaseFactor)||'|'||quote(SuccessfulReviewCount)||'|'||quote(LapseCount)||'|'||quote(LastReviewedAtUtc)||'|'||quote(LastRating)||'|'||quote(CreatedAtUtc)||'|'||quote(UpdatedAtUtc) AS Value FROM LearningCards ORDER BY Id"),
                Fingerprint(connection, "SELECT quote(Id)||'|'||quote(CardId)||'|'||quote(SessionId)||'|'||quote(Rating)||'|'||quote(WasTypedAnswer)||'|'||quote(WasCorrect)||'|'||quote(ReviewedAtUtc)||'|'||quote(DueAtUtc)||'|'||quote(IntervalDays)||'|'||quote(EaseFactor)||'|'||quote(TargetAnswerVariantId)||'|'||quote(MatchedAnswerVariantId) AS Value FROM LearningReviews ORDER BY Id"),
                Fingerprint(connection, "SELECT quote(Id)||'|'||quote(CardId)||'|'||quote(AnswerVariantId)||'|'||quote(InteractionMode)||'|'||quote(ConsecutiveReadingSuccessCount)||'|'||quote(ConsecutiveTypingSuccessCount)||'|'||quote(ConsecutiveTypingFailureCount)||'|'||quote(LastAssessedAtUtc)||'|'||quote(MasteryReviewExtensionScheduled)||'|'||quote(IsMastered)||'|'||quote(ReplayVersion)||'|'||quote(CreatedAtUtc)||'|'||quote(UpdatedAtUtc) AS Value FROM AnswerVariantProgress ORDER BY Id"),
                Fingerprint(connection, "SELECT quote(Id)||'|'||quote(StableId)||'|'||quote(SenseId)||'|'||quote(CardDirection)||'|'||quote(AnswerVariantId)||'|'||quote(Requirement)||'|'||quote(IsPreferred)||'|'||quote(RequiredSinceUtc)||'|'||quote(CreatedAtUtc)||'|'||quote(UpdatedAtUtc) AS Value FROM SenseAnswerVariantAssignments ORDER BY Id"),
                Fingerprint(connection, "SELECT quote(Id)||'|'||quote(StableId)||'|'||quote(SenseId)||'|'||quote(AnswerLanguage)||'|'||quote(DisplayText)||'|'||quote(NormalizedText)||'|'||quote(SourceMeaningId)||'|'||quote(CreatedAtUtc)||'|'||quote(UpdatedAtUtc) AS Value FROM AnswerVariants ORDER BY Id"),
                Fingerprint(connection, "SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(Status)||'|'||quote(UpdatedAtUtc) AS Value FROM Senses ORDER BY Id"),
                Fingerprint(connection, "SELECT quote(Id)||'|'||quote(Status)||'|'||quote(AutomaticInteractionMode)||'|'||quote(ConsecutiveRecallSuccessCount)||'|'||quote(ConsecutiveTypingSuccessCount)||'|'||quote(ConsecutiveTypingFailureCount)||'|'||quote(MasteryReviewExtensionScheduled)||'|'||quote(UpdatedAt) AS Value FROM Words ORDER BY Id"),
                Fingerprint(connection, "SELECT quote(type)||'|'||quote(name)||'|'||quote(tbl_name)||'|'||quote(sql) AS Value FROM sqlite_master ORDER BY type, name"));
        });
        return state!;
    }

    private static async Task<LearningState> CaptureLearningStateAsync(
        Schema7Fixture fixture,
        int queueItemId,
        int cardId,
        int sessionId)
    {
        LearningState? state = null;
        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            var queue = Schema8LearningRepository.LoadQueueRow(connection, queueItemId)
                ?? throw new AssertFailedException("The Schema-8 queue row was not found.");
            var card = Schema8LearningRepository.LoadCard(connection, cardId)
                ?? throw new AssertFailedException("The Schema-8 card row was not found.");
            var session = Schema8LearningRepository.LoadSession(connection, sessionId)
                ?? throw new AssertFailedException("The Schema-8 session row was not found.");
            state = new LearningState(
                connection.ExecuteScalar<int>("PRAGMA user_version"),
                connection.ExecuteScalar<int>("PRAGMA schema_version"),
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('Senses', 'AnswerVariants', 'SenseAnswerVariantAssignments', 'AnswerVariantProgress')"),
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('LearningCards') WHERE name = 'MeaningId'"),
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('LearningCards') WHERE name = 'PreferredMeaningId'"),
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('SenseAnswerVariantAssignments') WHERE name = 'RequiredSinceUtc'"),
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_LearningCards_Sense_Direction'"),
                queue.Id,
                queue.CardId,
                queue.TargetAnswerVariantId,
                queue.AnswerRevealed,
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM AnswerVariants"),
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM SenseAnswerVariantAssignments"),
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningReviews"),
                string.Join("\n", Schema8LearningRepository.LoadProgressForCard(connection, cardId).Select(row =>
                    $"{row.Id}|{row.CardId}|{row.AnswerVariantId}|{(int)row.InteractionMode}|{row.ConsecutiveReadingSuccessCount}|{row.ConsecutiveTypingSuccessCount}|{row.ConsecutiveTypingFailureCount}|{row.LastAssessedAtUtc?.Ticks}|{row.MasteryReviewExtensionScheduled}|{row.IsMastered}|{row.ReplayVersion}|{row.CreatedAtUtc.Ticks}|{row.UpdatedAtUtc.Ticks}")),
                $"{card.Id}|{(int)card.State}|{card.DueAtUtc.Ticks}|{card.IntervalDays}|{card.EaseFactor:R}|{card.SuccessfulReviewCount}|{card.LapseCount}|{card.LastReviewedAtUtc?.Ticks}|{(int?)card.LastRating}|{card.UpdatedAtUtc.Ticks}",
                $"{queue.Id}|{queue.SessionId}|{queue.CardId}|{queue.QueueOrder}|{queue.IsDueCard}|{queue.IsAgainRepeat}|{queue.IsCompleted}|{(int?)queue.Rating}|{queue.CompletedAtUtc?.Ticks}",
                $"{session.Id}|{(int)session.Status}|{session.TotalCards}|{session.CompletedCards}|{session.AgainCount}|{session.HardCount}|{session.GoodCount}|{session.EasyCount}|{session.StartedAtUtc.Ticks}|{session.UpdatedAtUtc.Ticks}|{session.CompletedAtUtc?.Ticks}");
        });
        return state!;
    }

    private static async Task<ShapeMetadata> CaptureShapeMetadataAsync(Schema7Fixture fixture)
    {
        ShapeMetadata? metadata = null;
        await fixture.Connection.RunInTransactionAsync(connection =>
        {
            metadata = new ShapeMetadata(
                connection.ExecuteScalar<int>("PRAGMA user_version"),
                connection.ExecuteScalar<int>("PRAGMA schema_version"),
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('SenseAnswerVariantAssignments') WHERE name = 'RequiredSinceUtc'"),
                connection.ExecuteScalar<int>("SELECT Id FROM LearningCards ORDER BY Id LIMIT 1"),
                connection.ExecuteScalar<int>("SELECT Id FROM LearningSessions ORDER BY Id LIMIT 1"),
                connection.ExecuteScalar<int>("SELECT Id FROM LearningSessionCards ORDER BY Id LIMIT 1"),
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LearningReviews"),
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM AnswerVariantProgress"),
                string.Join("\n", connection.Query<SqliteMasterRow>(
                    "SELECT type AS Type, name AS Name, tbl_name AS TableName, sql AS Sql FROM sqlite_master ORDER BY type, name")
                    .Select(row => $"{row.Type}|{row.Name}|{row.TableName}|{row.Sql}")));
        });
        return metadata!;
    }

    private static string ComputeSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed record SeededGraph(int WordId, int MeaningId, int CardId, int SessionId, int QueueItemId);

    private sealed record SeededSession(int SessionId, int FirstQueueItemId, int SecondQueueItemId);

    private sealed record QueueState(
        int Id,
        int SessionId,
        int CardId,
        int QueueOrder,
        bool IsDueCard,
        bool IsAgainRepeat,
        bool AnswerRevealed,
        bool SpellingChecked,
        bool SpellingCorrect,
        bool IsCompleted,
        ReviewRating? Rating,
        DateTime? CompletedAtUtc,
        int? TargetAnswerVariantId);

    private sealed record ReviewState(
        int Id,
        int CardId,
        int SessionId,
        ReviewRating Rating,
        bool WasTypedAnswer,
        bool WasCorrect,
        DateTime ReviewedAtUtc,
        DateTime DueAtUtc,
        int IntervalDays,
        double EaseFactor,
        int? TargetAnswerVariantId,
        int? MatchedAnswerVariantId);

    private sealed record ProgressState(
        int Id,
        int CardId,
        int AnswerVariantId,
        LearningInteractionMode InteractionMode,
        int ConsecutiveReadingSuccessCount,
        int ConsecutiveTypingSuccessCount,
        int ConsecutiveTypingFailureCount,
        DateTime? LastAssessedAtUtc,
        bool MasteryReviewExtensionScheduled,
        bool IsMastered,
        int ReplayVersion,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    private sealed record CardStateSnapshot(
        int Id,
        int WordId,
        int? SenseId,
        int PreferredMeaningId,
        CardDirection Direction,
        CardState State,
        DateTime DueAtUtc,
        int IntervalDays,
        double EaseFactor,
        int SuccessfulReviewCount,
        int LapseCount,
        DateTime? LastReviewedAtUtc,
        ReviewRating? LastRating,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    private sealed record SessionState(
        int Id,
        LearningSessionStatus Status,
        int TotalCards,
        int CompletedCards,
        int AgainCount,
        int HardCount,
        int GoodCount,
        int EasyCount,
        DateTime StartedAtUtc,
        DateTime UpdatedAtUtc,
        DateTime? CompletedAtUtc);

    private sealed record SenseStateSnapshot(
        int Id,
        string StableId,
        int WordId,
        string SourceLanguage,
        string ExplanationLanguage,
        string ProviderSenseId,
        string TopicOrDomain,
        string PartOfSpeech,
        string GrammaticalRelationship,
        string AcronymExpansion,
        int? DefaultMeaningId,
        SenseStatus Status,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    private sealed record WordStateSnapshot(
        int Id,
        WordStatus Status,
        LearningInteractionMode AutomaticInteractionMode,
        int ConsecutiveRecallSuccessCount,
        int ConsecutiveTypingSuccessCount,
        int ConsecutiveTypingFailureCount,
        bool MasteryReviewExtensionScheduled,
        DateTime UpdatedAt);

    private sealed record VariantState(
        int Id,
        string StableId,
        int SenseId,
        string AnswerLanguage,
        string DisplayText,
        string NormalizedText,
        int? SourceMeaningId,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    private sealed record AssignmentState(
        int Id,
        string StableId,
        int SenseId,
        CardDirection CardDirection,
        int AnswerVariantId,
        AnswerVariantRequirement Requirement,
        bool IsPreferred,
        DateTime? RequiredSinceUtc,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    private sealed record PersistenceDetails(
        int UserVersion,
        List<QueueState> Queues,
        List<ReviewState> Reviews,
        List<ProgressState> Progress,
        List<CardStateSnapshot> Cards,
        List<SessionState> Sessions,
        List<SenseStateSnapshot> Senses,
        List<WordStateSnapshot> Words,
        List<VariantState> Variants,
        List<AssignmentState> Assignments);

    private sealed record PersistedState(
        int UserVersion,
        int SchemaVersion,
        string DatabaseHash,
        int SessionId,
        int SessionCount,
        int QueueCount,
        int AnswerVariantCount,
        int AssignmentCount,
        string QueueFingerprint,
        string QueueStructureFingerprint,
        string QueueTargetFingerprint,
        string SessionFingerprint,
        string CardFingerprint,
        string ReviewFingerprint,
        string ProgressFingerprint,
        string AssignmentFingerprint,
        string AnswerVariantFingerprint,
        string SenseFingerprint,
        string WordFingerprint,
        string SchemaFingerprint);

    private sealed record LearningState(
        int UserVersion,
        int SchemaVersion,
        int Schema8TableCount,
        int LegacyMeaningIdColumnCount,
        int PreferredMeaningIdColumnCount,
        int RequiredSinceUtcColumnCount,
        int SenseDirectionIndexCount,
        int QueueItemId,
        int QueueCardId,
        int? TargetAnswerVariantId,
        bool AnswerRevealed,
        int AnswerVariantCount,
        int AssignmentCount,
        int ReviewCount,
        string ProgressFingerprint,
        string CardScheduleFingerprint,
        string QueueCompletionFingerprint,
        string SessionCounterFingerprint);

    private sealed record ShapeMetadata(
        int UserVersion,
        int SchemaVersion,
        int RequiredSinceUtcColumnCount,
        int CardId,
        int SessionId,
        int QueueItemId,
        int ReviewCount,
        int ProgressCount,
        string SchemaFingerprint);

    private sealed class SqliteMasterRow
    {
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string? Sql { get; set; }
    }

    private sealed class QueueTargetTextRow
    {
        public int QueueItemId { get; set; }
        public string DisplayText { get; set; } = string.Empty;
    }

    private sealed class WordStateRow
    {
        public int Id { get; set; }
        public WordStatus Status { get; set; }
        public LearningInteractionMode AutomaticInteractionMode { get; set; }
        public int ConsecutiveRecallSuccessCount { get; set; }
        public int ConsecutiveTypingSuccessCount { get; set; }
        public int ConsecutiveTypingFailureCount { get; set; }
        public bool MasteryReviewExtensionScheduled { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private sealed class FixedAppSettings(LearningMode learningMode) : IAppSettingsService
    {
        public int PreparationLimit => 20;
        public IReadOnlyList<int> SupportedPreparationLimits => [20];
        public CardDirectionPreference CardDirection => CardDirectionPreference.Both;
        public LearningMode LearningMode => learningMode;
        public bool HasOnlineLookupConsent => false;
        public bool EnhancedTermRecognitionEnabled => false;

        public void SetPreparationLimit(int preparationLimit) => throw new NotSupportedException();
        public void SetCardDirection(CardDirectionPreference preference) => throw new NotSupportedException();
        public void SetLearningMode(LearningMode mode) => throw new NotSupportedException();
        public void GrantOnlineLookupConsent() => throw new NotSupportedException();
        public void RevokeOnlineLookupConsent() => throw new NotSupportedException();
        public void SetEnhancedTermRecognitionEnabled(bool enabled) => throw new NotSupportedException();
        public void Reset() => throw new NotSupportedException();
    }
}
