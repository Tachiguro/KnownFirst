using KnownFirst.Core.Learning;
using KnownFirst.Core.Text;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models;
using KnownFirst.Services.Study;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LearningServiceSchema8ViewAndContinuationTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task ResumeActiveSession_Schema8_ReturnsCurrentCardWithoutTargetDrift()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var seeded = await SeedSchema7CardAsync(fixture, 40, "resume", "resume-target");
        await fixture.MigrateToSchema8Async();
        var service = CreateService(fixture);
        var created = await service.GetOrStartAsync();
        var queue = (await ReadQueueAsync(fixture)).Single();
        var frozenTarget = queue.TargetAnswerVariantId!.Value;
        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", seeded.CardId);

        const int laterPreferredId = 800;
        await fixture.InsertAnswerVariantAsync(senseId, "later-preferred", id: laterPreferredId, createdAtUtc: Now);
        await fixture.Connection.ExecuteAsync(
            "UPDATE SenseAnswerVariantAssignments SET IsPreferred = 0 WHERE SenseId = ? AND CardDirection = ?",
            senseId, (int)CardDirection.MeaningToTerm);
        await fixture.InsertAssignmentAsync(
            senseId, CardDirection.MeaningToTerm, laterPreferredId,
            AnswerVariantRequirement.Required, true, Now.AddDays(-1), Now, "later-preferred-assignment");
        var before = await CaptureStateAsync(fixture);

        var resumed = await service.GetOrStartAsync();
        var after = await CaptureStateAsync(fixture);
        var resumedQueue = (await ReadQueueAsync(fixture)).Single();

        Assert.IsNotNull(created.Card);
        Assert.IsNotNull(resumed.Card);
        Assert.AreEqual(queue.Id, resumed.Card.QueueItemId);
        Assert.AreEqual(seeded.CardId, resumed.Card.CardId);
        Assert.AreEqual(frozenTarget, resumedQueue.TargetAnswerVariantId);
        Assert.AreNotEqual(laterPreferredId, resumedQueue.TargetAnswerVariantId);
        Assert.AreEqual(before, after);
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessions"));
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessionCards"));
    }

    [TestMethod]
    public async Task CardView_Schema8_UsesDirectionSpecificPreferredMeaningAndContexts()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var wordId = await fixture.InsertWordAsync("bank", createdAt: Now, updatedAt: Now);
        var termMeaningId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "bank-term", translation: "bank-answer", definition: "bank-definition",
            selectedMeaningId: "shared-sense", createdAt: Now, updatedAt: Now);
        var explanationMeaningId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "bank-other-term", translation: "bank-other-answer", definition: "other-definition",
            selectedMeaningId: "shared-sense", createdAt: Now.AddSeconds(1), updatedAt: Now.AddSeconds(1));
        await fixture.InsertContextAsync(termMeaningId, wordId, sourceDocumentId: 1, sourceDocumentTitle: "Term doc", text: "term context", targetStart: 0, targetLength: 4);
        await fixture.InsertContextAsync(explanationMeaningId, wordId, sourceDocumentId: 2, sourceDocumentTitle: "Meaning doc", text: "meaning context", targetStart: 0, targetLength: 7);
        const int termToMeaningCardId = 40;
        const int meaningToTermCardId = 41;
        await fixture.InsertCardAsync(wordId, termMeaningId, CardDirection.TermToMeaning, id: termToMeaningCardId, createdAtUtc: Now, updatedAtUtc: Now);
        await fixture.InsertCardAsync(wordId, explanationMeaningId, CardDirection.MeaningToTerm, id: meaningToTermCardId, createdAtUtc: Now, updatedAtUtc: Now);
        await fixture.MigrateToSchema8Async();
        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", termToMeaningCardId);
        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningCards SET PreferredMeaningId = ? WHERE Id = ?", termMeaningId, termToMeaningCardId);
        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningCards SET PreferredMeaningId = ? WHERE Id = ?", explanationMeaningId, meaningToTermCardId);

        var termTarget = await RequiredTargetAsync(fixture, senseId, CardDirection.TermToMeaning);
        var meaningTarget = await RequiredTargetAsync(fixture, senseId, CardDirection.MeaningToTerm);
        var sessionId = await InsertSchema8SessionAsync(fixture, totalCards: 2);
        await InsertSchema8QueueAsync(fixture, sessionId, termToMeaningCardId, 0, termTarget, isAgainRepeat: false);
        await InsertSchema8QueueAsync(fixture, sessionId, meaningToTermCardId, 1, meaningTarget, isAgainRepeat: true);

        var service = CreateService(fixture);
        var first = (await service.GetOrStartAsync()).Card!;
        Assert.AreEqual(CardDirection.TermToMeaning, first.Direction);
        Assert.AreEqual("bank-term", first.Term);
        Assert.AreEqual("bank-answer", first.Translation);
        Assert.AreEqual("bank-definition", first.Definition);
        Assert.AreEqual("Term doc", first.Contexts.Single().DocumentTitle);
        Assert.IsFalse(first.IsAgainRepeat);

        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningSessionCards SET IsCompleted = 1 WHERE SessionId = ? AND QueueOrder = 0", sessionId);
        var second = (await service.GetOrStartAsync()).Card!;
        Assert.AreEqual(CardDirection.MeaningToTerm, second.Direction);
        Assert.AreEqual("bank-other-term", second.Term);
        Assert.AreEqual("bank-other-answer", second.Translation);
        Assert.AreEqual("other-definition", second.Definition);
        Assert.AreEqual("Meaning doc", second.Contexts.Single().DocumentTitle);
        Assert.IsTrue(second.IsAgainRepeat);
        Assert.AreEqual(meaningTarget, (await ReadQueueAsync(fixture)).Single(row => row.Id == second.QueueItemId).TargetAnswerVariantId);
    }

    [TestMethod]
    public async Task RateAsync_Schema8_AgainRepeatCopiesExactCurrentTarget()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var seeded = await SeedSchema7CardAsync(fixture, 40, "again", "original-target");
        await fixture.MigrateToSchema8Async();
        var service = CreateService(fixture);
        var original = (await service.GetOrStartAsync()).Card!;
        var originalQueue = (await ReadQueueAsync(fixture)).Single();
        var frozenTarget = originalQueue.TargetAnswerVariantId!.Value;
        var senseId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM LearningCards WHERE Id = ?", seeded.CardId);
        const int laterPreferredId = 810;
        await fixture.InsertAnswerVariantAsync(senseId, "preferred-after-freeze", id: laterPreferredId, createdAtUtc: Now);
        await fixture.Connection.ExecuteAsync(
            "UPDATE SenseAnswerVariantAssignments SET IsPreferred = 0 WHERE SenseId = ? AND CardDirection = ?",
            senseId, (int)CardDirection.MeaningToTerm);
        await fixture.InsertAssignmentAsync(
            senseId, CardDirection.MeaningToTerm, laterPreferredId,
            AnswerVariantRequirement.Required, true, Now.AddDays(-2), Now, "new-preferred");

        await service.RevealAnswerAsync(original.QueueItemId);
        var repeatResult = await service.RateAsync(original.QueueItemId, ReviewRating.Again);
        var queues = await ReadQueueAsync(fixture);
        var repeat = queues.Single(row => row.IsAgainRepeat);

        Assert.IsNotNull(repeatResult.Card);
        Assert.HasCount(2, queues);
        Assert.AreEqual(originalQueue.SessionId, repeat.SessionId);
        Assert.AreEqual(originalQueue.CardId, repeat.CardId);
        Assert.AreEqual(frozenTarget, repeat.TargetAnswerVariantId);
        Assert.AreNotEqual(laterPreferredId, repeat.TargetAnswerVariantId);
        Assert.AreEqual(1, repeat.QueueOrder);

        await service.RevealAnswerAsync(repeat.Id);
        await service.RateAsync(repeat.Id, ReviewRating.Again);
        Assert.HasCount(2, await ReadQueueAsync(fixture));
    }

    [TestMethod]
    public async Task RateAsync_Schema8_ContinuesNextItemAndFinalizesOwningSession()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        await SeedSchema7CardAsync(fixture, 40, "first", "first-target", frequency: 10);
        await SeedSchema7CardAsync(fixture, 41, "second", "second-target", frequency: 1);
        await fixture.MigrateToSchema8Async();
        var service = CreateService(fixture);
        var first = (await service.GetOrStartAsync()).Card!;
        var sessionId = first.SessionId;
        var queues = await ReadQueueAsync(fixture);

        await service.RevealAnswerAsync(first.QueueItemId);
        var continued = await service.RateAsync(first.QueueItemId, ReviewRating.Good);
        Assert.IsNotNull(continued.Card);
        Assert.AreEqual(sessionId, continued.Card.SessionId);
        Assert.AreEqual(queues[1].Id, continued.Card.QueueItemId);
        Assert.AreEqual(queues[1].TargetAnswerVariantId, (await ReadQueueAsync(fixture))[1].TargetAnswerVariantId);

        await service.RevealAnswerAsync(continued.Card.QueueItemId);
        var completed = await service.RateAsync(continued.Card.QueueItemId, ReviewRating.Good);
        Assert.IsNull(completed.Card);
        Assert.IsNotNull(completed.CompletedSummary);
        Assert.AreEqual(sessionId, completed.CompletedSummary.SessionId);
        Assert.AreEqual(2, completed.CompletedSummary.CardsReviewed);
        Assert.AreEqual(2, completed.CompletedSummary.GoodCount);
        Assert.IsNotNull(completed.CompletedSummary.NextDueAtUtc);
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessions"));
        Assert.AreEqual((int)LearningSessionStatus.Completed,
            await fixture.Connection.ExecuteScalarAsync<int>("SELECT Status FROM LearningSessions WHERE Id = ?", sessionId));
    }

    [TestMethod]
    public async Task MarkPermanentlyKnown_Schema8_RemovesAllSensesAndNormalizesSessions()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var seeded = await SeedSchema7CardAsync(fixture, 40, "polyseme", "first-sense");
        await fixture.MigrateToSchema8Async();
        await AddSecondSenseGraphAsync(fixture, seeded.WordId, seeded.MeaningId, 200, 41, 900, "second-sense");
        var service = CreateService(fixture);
        await service.GetOrStartAsync();
        var queues = await ReadQueueAsync(fixture);
        var sessionId = queues[0].SessionId;
        await fixture.InsertProgressAsync(queues[0].CardId, queues[0].TargetAnswerVariantId!.Value, Now);
        var reviewId = await fixture.InsertReviewAsync(queues[0].CardId, sessionId, reviewedAtUtc: Now);
        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningReviews SET TargetAnswerVariantId = ? WHERE Id = ?", queues[0].TargetAnswerVariantId, reviewId);

        var result = await service.MarkPermanentlyKnownAsync(seeded.WordId, confirmed: true);

        Assert.IsTrue(result);
        Assert.AreEqual((int)WordStatus.Known,
            await fixture.Connection.ExecuteScalarAsync<int>("SELECT Status FROM Words WHERE Id = ?", seeded.WordId));
        foreach (var table in new[] { "Senses", "Meanings", "ContextSnapshots", "LearningCards", "AnswerVariants", "SenseAnswerVariantAssignments", "AnswerVariantProgress", "LearningReviews", "LearningSessionCards" })
        {
            Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {table}"), table);
        }
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessions"));
    }

    [TestMethod]
    public async Task MarkPermanentlyKnown_Schema8_PreservesUnrelatedWordGraph()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var removed = await SeedSchema7CardAsync(fixture, 40, "removed", "removed-target", frequency: 10);
        var retained = await SeedSchema7CardAsync(fixture, 41, "retained", "retained-target", frequency: 1);
        await fixture.MigrateToSchema8Async();
        await AddSecondSenseGraphAsync(fixture, removed.WordId, removed.MeaningId, 200, 42, 901, "removed-second");
        var service = CreateService(fixture);
        await service.GetOrStartAsync();
        var retainedBefore = await CaptureWordGraphAsync(fixture, retained.WordId);
        var sessionId = (await ReadQueueAsync(fixture)).Single(row => row.CardId == retained.CardId).SessionId;

        Assert.IsTrue(await service.MarkPermanentlyKnownAsync(removed.WordId, confirmed: true));
        var retainedAfter = await CaptureWordGraphAsync(fixture, retained.WordId);
        var session = await fixture.Connection.QueryAsync<SessionRow>(
            "SELECT Id, Status, TotalCards, CompletedCards FROM LearningSessions WHERE Id = ?", sessionId);

        Assert.AreEqual(retainedBefore, retainedAfter);
        Assert.HasCount(1, session);
        Assert.AreEqual(LearningSessionStatus.Active, session[0].Status);
        Assert.AreEqual(1, session[0].TotalCards);
        Assert.AreEqual(0, session[0].CompletedCards);
        Assert.AreEqual((int)WordStatus.UnknownBacklog,
            await fixture.Connection.ExecuteScalarAsync<int>("SELECT Status FROM Words WHERE Id = ?", retained.WordId));
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessionCards WHERE SessionId = ? AND CardId = ?", sessionId, retained.CardId));
    }

    private static LearningService CreateService(Schema7Fixture fixture) => new(
        new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture),
        new SimpleSpacedRepetitionScheduler(), new SpellingAnswerComparer(), new FakeClock(Now));

    private static async Task<SeededCard> SeedSchema7CardAsync(
        Schema7Fixture fixture, int cardId, string term, string answer, int frequency = 1)
    {
        var wordId = await fixture.InsertWordAsync(term, totalOccurrenceCount: frequency, createdAt: Now, updatedAt: Now);
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: answer, translation: $"meaning-{answer}", definition: $"definition-{answer}", createdAt: Now, updatedAt: Now);
        await fixture.InsertContextAsync(meaningId, wordId, sourceDocumentId: cardId, sourceDocumentTitle: $"doc-{term}", text: $"{term} context", targetStart: 0, targetLength: term.Length);
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm, id: cardId, createdAtUtc: Now, updatedAtUtc: Now);
        return new SeededCard(wordId, meaningId, cardId);
    }

    private static async Task AddSecondSenseGraphAsync(
        Schema7Fixture fixture, int wordId, int sourceMeaningId, int senseId, int cardId, int targetId, string text)
    {
        await fixture.InsertSenseAsync(wordId, id: senseId, createdAtUtc: Now, updatedAtUtc: Now);
        var meaningId = await CloneMeaningForSenseAsync(fixture, sourceMeaningId, senseId, text);
        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO LearningCards
                (Id, WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor,
                 SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, 0, 2.5, 0, 0, NULL, NULL, ?, ?)
            """,
            cardId, wordId, senseId, meaningId, (int)CardDirection.MeaningToTerm,
            (int)CardState.New, Now, Now, Now);
        await fixture.InsertAnswerVariantAsync(senseId, text, id: targetId, sourceMeaningId: meaningId, createdAtUtc: Now);
        await fixture.InsertAssignmentAsync(senseId, CardDirection.MeaningToTerm, targetId,
            AnswerVariantRequirement.Required, true, Now, Now, $"assignment-{targetId}");
    }

    private static async Task<int> CloneMeaningForSenseAsync(Schema7Fixture fixture, int sourceMeaningId, int senseId, string displayTerm)
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
                   Definition, DictionaryExample, AdditionalNote, AcceptedAliasesJson, TranslationOrDefinition,
                   Source, SourceProject, SourcePageTitle, SourceRevisionId, Attribution, ConfirmedByUser,
                   CreatedAt, UpdatedAt, PreparedAt FROM Meanings WHERE Id = ?
            """, senseId, Guid.NewGuid().ToString("N"), displayTerm, sourceMeaningId);
        var meaningId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
        await fixture.Connection.ExecuteAsync(
            "UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", meaningId, senseId);
        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO ContextSnapshots
                (MeaningId, SenseId, WordId, SourceDocumentId, SourceDocumentTitle, Text, TargetStart,
                 TargetLength, NormalizedFingerprint, CreatedAtUtc)
            VALUES (?, ?, (SELECT WordId FROM Meanings WHERE Id = ?), ?, ?, ?, 0, ?, ?, ?)
            """, meaningId, senseId, meaningId, meaningId, $"doc-{displayTerm}", $"{displayTerm} context",
            displayTerm.Length, Guid.NewGuid().ToString("N"), Now);
        return meaningId;
    }

    private static async Task<int> RequiredTargetAsync(Schema7Fixture fixture, int senseId, CardDirection direction) =>
        await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT AnswerVariantId FROM SenseAnswerVariantAssignments WHERE SenseId = ? AND CardDirection = ? AND Requirement = ? ORDER BY IsPreferred DESC, Id LIMIT 1",
            senseId, (int)direction, (int)AnswerVariantRequirement.Required);

    private static async Task<int> InsertSchema8SessionAsync(Schema7Fixture fixture, int totalCards)
    {
        await fixture.Connection.ExecuteAsync(
            "INSERT INTO LearningSessions (Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc) VALUES (?, ?, 0, 0, 0, 0, 0, ?, ?, NULL)",
            (int)LearningSessionStatus.Active, totalCards, Now, Now);
        return await fixture.Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
    }

    private static Task InsertSchema8QueueAsync(
        Schema7Fixture fixture, int sessionId, int cardId, int order, int targetId, bool isAgainRepeat) =>
        fixture.Connection.ExecuteAsync(
            "INSERT INTO LearningSessionCards (SessionId, CardId, QueueOrder, IsDueCard, IsAgainRepeat, AnswerRevealed, SpellingChecked, SpellingCorrect, IsCompleted, Rating, CompletedAtUtc, TargetAnswerVariantId) VALUES (?, ?, ?, 0, ?, 0, 0, 0, 0, NULL, NULL, ?)",
            sessionId, cardId, order, isAgainRepeat, targetId);

    private static Task<List<QueueRow>> ReadQueueAsync(Schema7Fixture fixture) => fixture.Connection.QueryAsync<QueueRow>(
        "SELECT Id, SessionId, CardId, QueueOrder, IsAgainRepeat, IsCompleted, TargetAnswerVariantId FROM LearningSessionCards ORDER BY QueueOrder, Id");

    private static async Task<string> CaptureStateAsync(Schema7Fixture fixture) => string.Join("\n", new[]
    {
        await RowsAsync(fixture, "SELECT quote(Id)||'|'||quote(Status)||'|'||quote(UpdatedAt) AS Value FROM Words ORDER BY Id"),
        await RowsAsync(fixture, "SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(Status)||'|'||quote(UpdatedAtUtc) AS Value FROM Senses ORDER BY Id"),
        await RowsAsync(fixture, "SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(SenseId)||'|'||quote(UpdatedAt) AS Value FROM Meanings ORDER BY Id"),
        await RowsAsync(fixture, "SELECT quote(Id)||'|'||quote(State)||'|'||quote(DueAtUtc)||'|'||quote(UpdatedAtUtc) AS Value FROM LearningCards ORDER BY Id"),
        await RowsAsync(fixture, "SELECT quote(Id)||'|'||quote(SenseId)||'|'||quote(Requirement)||'|'||quote(IsPreferred)||'|'||quote(UpdatedAtUtc) AS Value FROM SenseAnswerVariantAssignments ORDER BY Id"),
        await RowsAsync(fixture, "SELECT quote(Id)||'|'||quote(SessionId)||'|'||quote(CardId)||'|'||quote(QueueOrder)||'|'||quote(TargetAnswerVariantId) AS Value FROM LearningSessionCards ORDER BY Id"),
        await RowsAsync(fixture, "SELECT quote(Id)||'|'||quote(Status)||'|'||quote(TotalCards)||'|'||quote(CompletedCards)||'|'||quote(UpdatedAtUtc) AS Value FROM LearningSessions ORDER BY Id"),
        await RowsAsync(fixture, "SELECT quote(Id)||'|'||quote(CardId)||'|'||quote(AnswerVariantId)||'|'||quote(IsMastered)||'|'||quote(UpdatedAtUtc) AS Value FROM AnswerVariantProgress ORDER BY Id"),
        await RowsAsync(fixture, "SELECT quote(Id)||'|'||quote(CardId)||'|'||quote(TargetAnswerVariantId)||'|'||quote(MatchedAnswerVariantId) AS Value FROM LearningReviews ORDER BY Id")
    });

    private static async Task<string> CaptureWordGraphAsync(Schema7Fixture fixture, int wordId) => string.Join("\n", new[]
    {
        await RowsAsync(fixture, $"SELECT quote(Id)||'|'||quote(Status)||'|'||quote(PreparationState)||'|'||quote(TotalOccurrenceCount)||'|'||quote(DocumentCount)||'|'||quote(UpdatedAt) AS Value FROM Words WHERE Id = {wordId}"),
        await RowsAsync(fixture, $"SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(Status)||'|'||quote(UpdatedAtUtc) AS Value FROM Senses WHERE WordId = {wordId} ORDER BY Id"),
        await RowsAsync(fixture, $"SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(SenseId)||'|'||quote(DisplayTerm)||'|'||quote(UpdatedAt) AS Value FROM Meanings WHERE WordId = {wordId} ORDER BY Id"),
        await RowsAsync(fixture, $"SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(SenseId)||'|'||quote(PreferredMeaningId)||'|'||quote(Direction)||'|'||quote(State)||'|'||quote(DueAtUtc)||'|'||quote(UpdatedAtUtc) AS Value FROM LearningCards WHERE WordId = {wordId} ORDER BY Id"),
        await RowsAsync(fixture, $"SELECT quote(Id)||'|'||quote(SenseId)||'|'||quote(DisplayText)||'|'||quote(UpdatedAtUtc) AS Value FROM AnswerVariants WHERE SenseId IN (SELECT Id FROM Senses WHERE WordId = {wordId}) ORDER BY Id"),
        await RowsAsync(fixture, $"SELECT quote(Id)||'|'||quote(SenseId)||'|'||quote(CardDirection)||'|'||quote(AnswerVariantId)||'|'||quote(Requirement)||'|'||quote(IsPreferred)||'|'||quote(UpdatedAtUtc) AS Value FROM SenseAnswerVariantAssignments WHERE SenseId IN (SELECT Id FROM Senses WHERE WordId = {wordId}) ORDER BY Id"),
        await RowsAsync(fixture, $"SELECT quote(Id)||'|'||quote(SessionId)||'|'||quote(CardId)||'|'||quote(QueueOrder)||'|'||quote(TargetAnswerVariantId) AS Value FROM LearningSessionCards WHERE CardId IN (SELECT Id FROM LearningCards WHERE WordId = {wordId}) ORDER BY Id")
    });

    private static async Task<string> RowsAsync(Schema7Fixture fixture, string sql) =>
        string.Join(';', (await fixture.Connection.QueryAsync<ValueRow>(sql)).Select(row => row.Value));

    private sealed record SeededCard(int WordId, int MeaningId, int CardId);
    private sealed record QueueRow
    {
        public int Id { get; set; }
        public int SessionId { get; set; }
        public int CardId { get; set; }
        public int QueueOrder { get; set; }
        public bool IsAgainRepeat { get; set; }
        public bool IsCompleted { get; set; }
        public int? TargetAnswerVariantId { get; set; }
    }
    private sealed class SessionRow
    {
        public int Id { get; set; }
        public LearningSessionStatus Status { get; set; }
        public int TotalCards { get; set; }
        public int CompletedCards { get; set; }
    }
    private sealed class ValueRow { public string Value { get; set; } = string.Empty; }
}
