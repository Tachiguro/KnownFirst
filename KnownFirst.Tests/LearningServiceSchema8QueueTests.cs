using KnownFirst.Core.Learning;
using KnownFirst.Core.Text;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models;
using KnownFirst.Services.Study;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LearningServiceSchema8QueueTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task GetOrStart_Schema8_DueCardsPrecedeNewCardsAndTargetsAreFrozen()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var dueLater = await SeedSchema7CardAsync(fixture, 41, "due-later", 1, CardState.Review, Now.AddHours(-1));
        var newLow = await SeedSchema7CardAsync(fixture, 43, "new-low", 2, CardState.New, Now, frequency: 2);
        var dueEarlier = await SeedSchema7CardAsync(fixture, 40, "due-earlier", 3, CardState.Learning, Now.AddHours(-2));
        var newHigh = await SeedSchema7CardAsync(fixture, 42, "new-high", 4, CardState.New, Now, frequency: 20);
        await fixture.MigrateToSchema8Async();

        var service = CreateService(fixture);
        var result = await service.GetOrStartAsync();
        var first = await ReadQueueAsync(fixture);
        var assignmentsBeforeResume = await ReadAssignmentsFingerprintAsync(fixture);
        var progressBeforeResume = await ReadProgressFingerprintAsync(fixture);

        Assert.IsNotNull(result.Card);
        Assert.AreEqual(first[0].SessionId, result.Card.SessionId);
        Assert.AreEqual(first[0].Id, result.Card.QueueItemId);
        Assert.AreEqual(dueEarlier.CardId, result.Card.CardId);
        Assert.AreEqual(dueEarlier.WordId, result.Card.WordId);
        Assert.AreEqual(CardDirection.MeaningToTerm, result.Card.Direction);
        Assert.AreEqual(LearningInteractionMode.Reading, result.Card.InteractionMode);
        Assert.IsNull(result.CompletedSummary);
        Assert.HasCount(4, first);
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessions"));
        CollectionAssert.AreEqual(
            new[] { dueEarlier.CardId, dueLater.CardId, newHigh.CardId, newLow.CardId },
            first.Select(row => row.CardId).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, first.Select(row => row.QueueOrder).ToArray());
        CollectionAssert.AreEqual(new[] { true, true, false, false }, first.Select(row => row.IsDueCard).ToArray());
        Assert.IsTrue(first.All(row => row.TargetAnswerVariantId.HasValue));
        foreach (var row in first)
        {
            Assert.AreEqual(
                1,
                await fixture.Connection.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*)
                    FROM LearningCards c
                    JOIN SenseAnswerVariantAssignments a
                      ON a.SenseId = c.SenseId AND a.CardDirection = c.Direction
                    WHERE c.Id = ? AND a.AnswerVariantId = ? AND a.Requirement = ?
                    """,
                    row.CardId, row.TargetAnswerVariantId, (int)AnswerVariantRequirement.Required));
        }

        await service.GetOrStartAsync();
        var resumed = await ReadQueueAsync(fixture);
        CollectionAssert.AreEqual(first, resumed);
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessions"));
        Assert.AreEqual(assignmentsBeforeResume, await ReadAssignmentsFingerprintAsync(fixture));
        Assert.AreEqual(progressBeforeResume, await ReadProgressFingerprintAsync(fixture));
    }

    [TestMethod]
    public async Task GetOrStart_Schema8_MultipleSensesOfOneWordQueueIndependently()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var first = await SeedSchema7CardAsync(fixture, 40, "bank", 1, CardState.New, Now, frequency: 7);
        await fixture.MigrateToSchema8Async();
        var firstSenseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", first.CardId);
        var secondSenseId = await fixture.InsertSenseAsync(first.WordId, id: 200, createdAtUtc: Now, updatedAtUtc: Now);
        var secondMeaningId = await CloneMeaningForSenseAsync(fixture, first.MeaningId, secondSenseId, "river bank");
        const int secondCardId = 41;
        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO LearningCards
                (Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor,
                 SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, 0, 2.5, 0, 0, NULL, NULL, ?, ?)
            """,
            secondCardId, first.WordId, secondSenseId, secondMeaningId,
            (int)CardDirection.MeaningToTerm, (int)CardState.New, Now, Now, Now);
        const int secondTargetId = 700;
        await fixture.InsertAnswerVariantAsync(secondSenseId, "river bank", id: secondTargetId, createdAtUtc: Now);
        await fixture.InsertAssignmentAsync(
            secondSenseId, CardDirection.MeaningToTerm, secondTargetId,
            AnswerVariantRequirement.Required, true, Now, Now, "second-sense-required");

        await CreateService(fixture).GetOrStartAsync();
        var queue = await ReadQueueAsync(fixture);

        Assert.HasCount(2, queue);
        CollectionAssert.AreEquivalent(new[] { first.CardId, secondCardId }, queue.Select(row => row.CardId).ToArray());
        Assert.AreNotEqual(firstSenseId, secondSenseId);
        Assert.AreEqual(secondTargetId, queue.Single(row => row.CardId == secondCardId).TargetAnswerVariantId);
        Assert.AreNotEqual(
            secondTargetId,
            queue.Single(row => row.CardId == first.CardId).TargetAnswerVariantId);
    }

    [TestMethod]
    public async Task GetOrStart_Schema8_SelectsNextUnmasteredRequiredTargetDeterministically()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var seeded = await SeedSchema7CardAsync(fixture, 40, "target-order", 1, CardState.New, Now);
        await fixture.MigrateToSchema8Async();
        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", seeded.CardId);
        var primary = (await fixture.ReadAssignmentsAsync(senseId, CardDirection.MeaningToTerm)).Single(row => row.IsPreferred);
        await fixture.Connection.ExecuteAsync(
            "UPDATE SenseAnswerVariantAssignments SET IsPreferred = 0 WHERE Id = ?", primary.Id);
        await fixture.InsertProgressAsync(
            seeded.CardId, primary.AnswerVariantId, primary.RequiredSinceUtc!.Value, isMastered: true);

        const int oldestId = 710;
        const int tieFirstId = 711;
        const int tieSecondId = 712;
        const int preferredId = 713;
        foreach (var id in new[] { oldestId, tieFirstId, tieSecondId, preferredId })
        {
            await fixture.InsertAnswerVariantAsync(senseId, $"variant-{id}", id: id, createdAtUtc: Now);
        }

        var oldestBoundary = Now.AddDays(-3);
        var tiedBoundary = Now.AddDays(-2);
        await fixture.InsertAssignmentAsync(senseId, CardDirection.MeaningToTerm, oldestId, AnswerVariantRequirement.Required, false, oldestBoundary, oldestBoundary, "oldest");
        var tieFirstAssignmentId = await fixture.InsertAssignmentAsync(senseId, CardDirection.MeaningToTerm, tieFirstId, AnswerVariantRequirement.Required, false, tiedBoundary, tiedBoundary, "tie-first");
        var tieSecondAssignmentId = await fixture.InsertAssignmentAsync(senseId, CardDirection.MeaningToTerm, tieSecondId, AnswerVariantRequirement.Required, false, tiedBoundary, tiedBoundary, "tie-second");
        await fixture.InsertAssignmentAsync(senseId, CardDirection.MeaningToTerm, preferredId, AnswerVariantRequirement.Required, true, Now, Now, "preferred");
        Assert.IsTrue(tieSecondAssignmentId < int.MaxValue);
        Assert.IsTrue(tieFirstAssignmentId < tieSecondAssignmentId);

        var assignmentsBefore = await ReadAssignmentsFingerprintAsync(fixture);
        var progressBefore = await ReadProgressFingerprintAsync(fixture);
        await CreateService(fixture).GetOrStartAsync();
        Assert.AreEqual(preferredId, (await ReadQueueAsync(fixture)).Single().TargetAnswerVariantId);
        Assert.AreEqual(assignmentsBefore, await ReadAssignmentsFingerprintAsync(fixture));
        Assert.AreEqual(progressBefore, await ReadProgressFingerprintAsync(fixture));

        await DeleteCreatedSessionAsync(fixture);
        await fixture.InsertProgressAsync(seeded.CardId, preferredId, Now, isMastered: true);
        await CreateService(fixture).GetOrStartAsync();
        Assert.AreEqual(oldestId, (await ReadQueueAsync(fixture)).Single().TargetAnswerVariantId);

        await DeleteCreatedSessionAsync(fixture);
        await fixture.InsertProgressAsync(seeded.CardId, oldestId, oldestBoundary, isMastered: true);
        await CreateService(fixture).GetOrStartAsync();
        Assert.AreEqual(tieFirstId, (await ReadQueueAsync(fixture)).Single().TargetAnswerVariantId);
    }

    [TestMethod]
    public async Task GetOrStart_Schema8_InvalidEligibleCardGraphRollsBackSessionCreation()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var seeded = await SeedSchema7CardAsync(fixture, 40, "invalid-graph", 1, CardState.New, Now);
        await fixture.MigrateToSchema8Async();
        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", seeded.CardId);
        await fixture.Connection.ExecuteAsync("UPDATE Senses SET WordId = ? WHERE Id = ?", seeded.WordId + 999, senseId);
        var before = await CaptureStateAsync(fixture);

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => CreateService(fixture).GetOrStartAsync());
        var after = await CaptureStateAsync(fixture);

        Assert.AreEqual(Schema8LearningDataErrorCode.InvalidCardGraph, exception.Code);
        Assert.AreEqual(before, after);
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessions"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessionCards"));
    }

    [TestMethod]
    public async Task GetOrStart_Schema8_NoEligibleCardsDoesNotCreateEmptySession()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await SeedSchema7CardAsync(fixture, 40, "retired", 1, CardState.Retired, Now.AddDays(-1));
        await fixture.MigrateToSchema8Async();
        var before = await CaptureStateAsync(fixture);

        var result = await CreateService(fixture).GetOrStartAsync();
        var after = await CaptureStateAsync(fixture);

        Assert.IsNull(result.Card);
        Assert.IsNull(result.CompletedSummary);
        Assert.AreEqual(before, after);
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessions"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessionCards"));
    }

    [TestMethod]
    public async Task GetOrStart_Schema8_UndefinedCardStateFailsClosedWithoutSessionMutation()
    {
        const int undefinedCardState = 99;
        Assert.IsFalse(Enum.IsDefined((CardState)undefinedCardState));

        await using var fixture = await Schema7Fixture.CreateAsync();
        var seeded = await SeedSchema7CardAsync(
            fixture, 40, "undefined-state", 1, CardState.Review, Now.AddMinutes(-1));
        await fixture.MigrateToSchema8Async();
        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningCards SET State = ? WHERE Id = ?", undefinedCardState, seeded.CardId);
        var before = await CaptureStateAsync(fixture);

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => CreateService(fixture).GetOrStartAsync());

        var after = await CaptureStateAsync(fixture);
        Assert.AreEqual(Schema8LearningDataErrorCode.InvalidCardGraph, exception.Code);
        Assert.AreEqual(before, after);
        Assert.AreEqual(
            undefinedCardState,
            await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT State FROM LearningCards WHERE Id = ?", seeded.CardId));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessions"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessionCards"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningReviews"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AnswerVariantProgress"));
    }

    [TestMethod]
    public async Task GetOrStart_Schema8_InvalidLaterPreferredMeaningRollsBackSessionCreation()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var first = await SeedSchema7CardAsync(
            fixture, 40, "first-valid", 1, CardState.New, Now, frequency: 20);
        var later = await SeedSchema7CardAsync(
            fixture, 41, "later-invalid", 2, CardState.New, Now, frequency: 1);
        await fixture.MigrateToSchema8Async();
        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningCards SET PreferredMeaningId = ? WHERE Id = ?",
            first.MeaningId, later.CardId);

        Assert.AreEqual(
            2,
            await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE Requirement = ?",
                (int)AnswerVariantRequirement.Required));
        Assert.AreEqual(
            first.WordId,
            await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT WordId FROM Meanings WHERE Id = ?", first.MeaningId));
        Assert.AreNotEqual(first.WordId, later.WordId);

        var before = await CaptureStateAsync(fixture);
        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => CreateService(fixture).GetOrStartAsync());
        var after = await CaptureStateAsync(fixture);

        Assert.AreEqual(Schema8LearningDataErrorCode.InvalidCardGraph, exception.Code);
        Assert.AreEqual(before, after);
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessions"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessionCards"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningReviews"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AnswerVariantProgress"));
    }

    private static LearningService CreateService(Schema7Fixture fixture) => new(
        new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture),
        new SimpleSpacedRepetitionScheduler(),
        new SpellingAnswerComparer(),
        new FakeClock(Now));

    private static async Task<SeededCard> SeedSchema7CardAsync(
        Schema7Fixture fixture, int cardId, string term, int ordinal, CardState state, DateTime dueAtUtc,
        int frequency = 1)
    {
        var createdAt = Now.AddMinutes(ordinal);
        var wordId = await fixture.InsertWordAsync(
            term, totalOccurrenceCount: frequency, createdAt: createdAt, updatedAt: createdAt);
        var meaningId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: term, translation: $"answer-{term}", createdAt: createdAt, updatedAt: createdAt);
        await fixture.InsertCardAsync(
            wordId, meaningId, CardDirection.MeaningToTerm, state, dueAtUtc,
            createdAtUtc: createdAt, updatedAtUtc: createdAt, id: cardId);
        return new SeededCard(wordId, meaningId, cardId);
    }

    private static async Task<int> CloneMeaningForSenseAsync(
        Schema7Fixture fixture, int sourceMeaningId, int senseId, string displayTerm)
    {
        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO Meanings
                (WordId, SenseId, StableId, ExplanationLanguage, SourceLanguage, DisplayTerm,
                 EncounteredSurfaceForm, GrammaticalRelationship, TokenKind, SelectedMeaningId,
                 AcronymExpansion, Translation, Definition, DictionaryExample, AdditionalNote,
                 AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle,
                 SourceRevisionId, Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt)
            SELECT WordId, ?, ?, ExplanationLanguage, SourceLanguage, ?, EncounteredSurfaceForm,
                   GrammaticalRelationship, TokenKind, SelectedMeaningId, AcronymExpansion, Translation,
                   Definition, DictionaryExample, AdditionalNote, AcceptedAliasesJson,
                   TranslationOrDefinition, Source, SourceProject, SourcePageTitle, SourceRevisionId,
                   Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt
            FROM Meanings WHERE Id = ?
            """,
            senseId, Guid.NewGuid().ToString("N"), displayTerm, sourceMeaningId);
        return await fixture.Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
    }

    private static Task<List<QueueReadRow>> ReadQueueAsync(Schema7Fixture fixture) =>
        fixture.Connection.QueryAsync<QueueReadRow>(
            "SELECT Id, SessionId, CardId, QueueOrder, IsDueCard, TargetAnswerVariantId FROM LearningSessionCards ORDER BY QueueOrder, Id");

    private static async Task DeleteCreatedSessionAsync(Schema7Fixture fixture)
    {
        await fixture.Connection.ExecuteAsync("DELETE FROM LearningSessionCards");
        await fixture.Connection.ExecuteAsync("DELETE FROM LearningSessions");
    }

    private static async Task<string> ReadAssignmentsFingerprintAsync(Schema7Fixture fixture) =>
        string.Join(";", (await fixture.Connection.QueryAsync<ValueRow>(
            "SELECT quote(Id)||'|'||quote(SenseId)||'|'||quote(CardDirection)||'|'||quote(AnswerVariantId)||'|'||quote(Requirement)||'|'||quote(IsPreferred)||'|'||quote(RequiredSinceUtc) AS Value FROM SenseAnswerVariantAssignments ORDER BY Id"))
            .Select(row => row.Value));

    private static async Task<string> ReadProgressFingerprintAsync(Schema7Fixture fixture) =>
        string.Join(";", (await fixture.Connection.QueryAsync<ValueRow>(
            "SELECT quote(Id)||'|'||quote(CardId)||'|'||quote(AnswerVariantId)||'|'||quote(IsMastered)||'|'||quote(ReplayVersion)||'|'||quote(CreatedAtUtc)||'|'||quote(UpdatedAtUtc) AS Value FROM AnswerVariantProgress ORDER BY Id"))
            .Select(row => row.Value));

    private static async Task<string> CaptureStateAsync(Schema7Fixture fixture)
    {
        var queries = new[]
        {
            "SELECT quote(Id)||'|'||quote(Status)||'|'||quote(PreparationState)||'|'||quote(TotalOccurrenceCount)||'|'||quote(DocumentCount)||'|'||quote(AutomaticInteractionMode)||'|'||quote(ConsecutiveRecallSuccessCount)||'|'||quote(ConsecutiveTypingSuccessCount)||'|'||quote(ConsecutiveTypingFailureCount)||'|'||quote(MasteryReviewExtensionScheduled)||'|'||quote(UpdatedAt) AS Value FROM Words ORDER BY Id",
            "SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(Status)||'|'||quote(DefaultMeaningId)||'|'||quote(UpdatedAtUtc) AS Value FROM Senses ORDER BY Id",
            "SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(SenseId)||'|'||quote(StableId)||'|'||quote(DisplayTerm)||'|'||quote(UpdatedAt) AS Value FROM Meanings ORDER BY Id",
            "SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(SenseId)||'|'||quote(PreferredMeaningId)||'|'||quote(Direction)||'|'||quote(State)||'|'||quote(DueAtUtc)||'|'||quote(IntervalDays)||'|'||quote(EaseFactor)||'|'||quote(SuccessfulReviewCount)||'|'||quote(LapseCount)||'|'||quote(LastReviewedAtUtc)||'|'||quote(LastRating)||'|'||quote(UpdatedAtUtc) AS Value FROM LearningCards ORDER BY Id",
            "SELECT quote(Id)||'|'||quote(SenseId)||'|'||quote(DisplayText)||'|'||quote(NormalizedText)||'|'||quote(UpdatedAtUtc) AS Value FROM AnswerVariants ORDER BY Id",
            "SELECT quote(Id)||'|'||quote(SenseId)||'|'||quote(CardDirection)||'|'||quote(AnswerVariantId)||'|'||quote(Requirement)||'|'||quote(IsPreferred)||'|'||quote(RequiredSinceUtc)||'|'||quote(UpdatedAtUtc) AS Value FROM SenseAnswerVariantAssignments ORDER BY Id",
            "SELECT quote(Id)||'|'||quote(CardId)||'|'||quote(AnswerVariantId)||'|'||quote(InteractionMode)||'|'||quote(ConsecutiveReadingSuccessCount)||'|'||quote(ConsecutiveTypingSuccessCount)||'|'||quote(ConsecutiveTypingFailureCount)||'|'||quote(IsMastered)||'|'||quote(ReplayVersion)||'|'||quote(CreatedAtUtc)||'|'||quote(UpdatedAtUtc) AS Value FROM AnswerVariantProgress ORDER BY Id",
            "SELECT quote(Id)||'|'||quote(CardId)||'|'||quote(SessionId)||'|'||quote(Rating)||'|'||quote(WasTypedAnswer)||'|'||quote(WasCorrect)||'|'||quote(ReviewedAtUtc)||'|'||quote(DueAtUtc)||'|'||quote(IntervalDays)||'|'||quote(EaseFactor)||'|'||quote(TargetAnswerVariantId)||'|'||quote(MatchedAnswerVariantId) AS Value FROM LearningReviews ORDER BY Id",
            "SELECT quote(Id)||'|'||quote(Status)||'|'||quote(TotalCards)||'|'||quote(CompletedCards)||'|'||quote(AgainCount)||'|'||quote(HardCount)||'|'||quote(GoodCount)||'|'||quote(EasyCount)||'|'||quote(StartedAtUtc)||'|'||quote(UpdatedAtUtc)||'|'||quote(CompletedAtUtc) AS Value FROM LearningSessions ORDER BY Id",
            "SELECT quote(Id)||'|'||quote(SessionId)||'|'||quote(CardId)||'|'||quote(QueueOrder)||'|'||quote(IsDueCard)||'|'||quote(IsAgainRepeat)||'|'||quote(AnswerRevealed)||'|'||quote(SpellingChecked)||'|'||quote(SpellingCorrect)||'|'||quote(IsCompleted)||'|'||quote(Rating)||'|'||quote(CompletedAtUtc)||'|'||quote(TargetAnswerVariantId) AS Value FROM LearningSessionCards ORDER BY Id",
            "SELECT quote(Id)||'|'||quote(MeaningId)||'|'||quote(SenseId)||'|'||quote(WordId)||'|'||quote(SourceDocumentId)||'|'||quote(Text)||'|'||quote(TargetStart)||'|'||quote(TargetLength)||'|'||quote(CreatedAtUtc) AS Value FROM ContextSnapshots ORDER BY Id"
        };
        var parts = new List<string>();
        foreach (var query in queries)
        {
            var rows = await fixture.Connection.QueryAsync<ValueRow>(query);
            parts.Add(string.Join(';', rows.Select(row => row.Value)));
        }

        return string.Join("\n", parts);
    }

    private sealed record SeededCard(int WordId, int MeaningId, int CardId);

    private sealed record QueueReadRow
    {
        public int Id { get; set; }
        public int SessionId { get; set; }
        public int CardId { get; set; }
        public int QueueOrder { get; set; }
        public bool IsDueCard { get; set; }
        public int? TargetAnswerVariantId { get; set; }
    }

    private sealed class ValueRow
    {
        public string Value { get; set; } = string.Empty;
    }
}
