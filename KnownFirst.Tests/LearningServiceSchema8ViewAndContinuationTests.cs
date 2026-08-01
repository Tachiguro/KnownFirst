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
        await EnsureSourceDocumentAsync(fixture, 1, "Term doc");
        await EnsureSourceDocumentAsync(fixture, 2, "Meaning doc");
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

        await AssertContextOwnershipAsync(fixture);
        Assert.AreEqual(
            1,
            await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM ContextSnapshots x JOIN Documents d ON d.Id = x.SourceDocumentId WHERE x.MeaningId = ? AND d.Id = 1 AND d.Title = 'Term doc' AND x.SourceDocumentTitle = 'Term doc'",
                termMeaningId));
        Assert.AreEqual(
            1,
            await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM ContextSnapshots x JOIN Documents d ON d.Id = x.SourceDocumentId WHERE x.MeaningId = ? AND d.Id = 2 AND d.Title = 'Meaning doc' AND x.SourceDocumentTitle = 'Meaning doc'",
                explanationMeaningId));
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

    [TestMethod]
    public async Task CardView_Schema8_MalformedContextFailsClosedWithoutMutation()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var seeded = await SeedSchema7CardAsync(fixture, 40, "context", "context-target");
        await fixture.MigrateToSchema8Async();
        var service = CreateService(fixture);
        var created = await service.GetOrStartAsync();
        Assert.IsNotNull(created.Card);
        await AssertContextOwnershipAsync(fixture);

        var contextId = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT Id FROM ContextSnapshots WHERE MeaningId = ?", seeded.MeaningId);
        var textLength = await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT length(Text) FROM ContextSnapshots WHERE Id = ?", contextId);
        await fixture.Connection.ExecuteAsync(
            "UPDATE ContextSnapshots SET TargetStart = ?, TargetLength = 1 WHERE Id = ?",
            textLength + 1, contextId);
        var before = await CaptureStateAsync(fixture);

        var exception = await Assert.ThrowsExactlyAsync<Schema8LearningDataException>(
            () => service.GetOrStartAsync());

        var after = await CaptureStateAsync(fixture);
        Assert.AreEqual(Schema8LearningDataErrorCode.InvalidCardGraph, exception.Code);
        Assert.AreEqual(before, after);
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ContextSnapshots WHERE Id = ? AND TargetStart = ? AND TargetLength = 1",
            contextId, textLength + 1));
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessions"));
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningSessionCards"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LearningReviews"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AnswerVariantProgress"));
    }

    [TestMethod]
    public async Task MarkPermanentlyKnown_Schema8_PreservesImportedWordEvidence()
    {
        await using var fixture = await Schema7Fixture.CreateAsync();
        var selected = await SeedSchema7CardAsync(
            fixture, 40, "evidence", "selected-target", frequency: 2);
        var unrelated = await SeedSchema7CardAsync(
            fixture, 41, "retained", "retained-target", frequency: 1);

        var selectedDocumentId = await fixture.InsertDocumentAsync(
            title: "Selected evidence", content: "Evidence evidence", wordCount: 2, importedAt: Now);
        await InsertImportedEvidenceAsync(
            fixture, selected.WordId, selectedDocumentId, "Evidence evidence",
            ("Evidence", 0), ("evidence", 9));
        var unrelatedDocumentId = await fixture.InsertDocumentAsync(
            title: "Unrelated evidence", content: "retained", wordCount: 1, importedAt: Now.AddMinutes(1));
        await InsertImportedEvidenceAsync(
            fixture, unrelated.WordId, unrelatedDocumentId, "retained", ("retained", 0));

        await fixture.MigrateToSchema8Async();
        await AddSecondSenseGraphAsync(
            fixture, selected.WordId, selected.MeaningId, 200, 42, 901, "selected-second");
        var selectedSenseIds = (await fixture.Connection.QueryAsync<IdRow>(
            "SELECT Id FROM Senses WHERE WordId = ? ORDER BY Id", selected.WordId))
            .Select(row => row.Id)
            .ToArray();
        Assert.HasCount(2, selectedSenseIds);

        var service = CreateService(fixture);
        await service.GetOrStartAsync();
        var queues = await ReadQueueAsync(fixture);
        var selectedQueue = queues.First(row => row.CardId == selected.CardId);
        var sessionId = selectedQueue.SessionId;
        await fixture.InsertProgressAsync(
            selectedQueue.CardId, selectedQueue.TargetAnswerVariantId!.Value, Now);
        var reviewId = await fixture.InsertReviewAsync(
            selectedQueue.CardId, sessionId, reviewedAtUtc: Now);
        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningReviews SET TargetAnswerVariantId = ? WHERE Id = ?",
            selectedQueue.TargetAnswerVariantId, reviewId);

        var selectedOccurrencesBefore = await RowsAsync(
            fixture,
            $"SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(DocumentId)||'|'||quote(SentenceSpanId)||'|'||quote(StartPosition)||'|'||quote(Length)||'|'||quote(SurfaceForm)||'|'||quote(TechnicalFamily)||'|'||quote(TechnicalInstanceYear)||'|'||quote(TechnicalInstanceIdentifier)||'|'||quote(TechnicalVariant)||'|'||quote(\"Order\") AS Value FROM WordOccurrences WHERE WordId = {selected.WordId} ORDER BY Id");
        var selectedFormsBefore = await RowsAsync(
            fixture,
            $"SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(SurfaceForm)||'|'||quote(OccurrenceCount) AS Value FROM WordForms WHERE WordId = {selected.WordId} ORDER BY Id");
        var selectedCountersBefore = await RowsAsync(
            fixture,
            $"SELECT quote(TotalOccurrenceCount)||'|'||quote(DocumentCount) AS Value FROM Words WHERE Id = {selected.WordId}");
        var selectedDocumentBefore = await CaptureDocumentEvidenceAsync(fixture, selectedDocumentId);
        var unrelatedBefore = await CaptureWordEvidenceAsync(fixture, unrelated.WordId);

        Assert.IsTrue(await service.MarkPermanentlyKnownAsync(selected.WordId, confirmed: true));

        Assert.AreEqual(
            (int)WordStatus.Known,
            await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT Status FROM Words WHERE Id = ?", selected.WordId));
        Assert.AreEqual(selectedOccurrencesBefore, await RowsAsync(
            fixture,
            $"SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(DocumentId)||'|'||quote(SentenceSpanId)||'|'||quote(StartPosition)||'|'||quote(Length)||'|'||quote(SurfaceForm)||'|'||quote(TechnicalFamily)||'|'||quote(TechnicalInstanceYear)||'|'||quote(TechnicalInstanceIdentifier)||'|'||quote(TechnicalVariant)||'|'||quote(\"Order\") AS Value FROM WordOccurrences WHERE WordId = {selected.WordId} ORDER BY Id"));
        Assert.AreEqual(selectedFormsBefore, await RowsAsync(
            fixture,
            $"SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(SurfaceForm)||'|'||quote(OccurrenceCount) AS Value FROM WordForms WHERE WordId = {selected.WordId} ORDER BY Id"));
        Assert.AreEqual(selectedCountersBefore, await RowsAsync(
            fixture,
            $"SELECT quote(TotalOccurrenceCount)||'|'||quote(DocumentCount) AS Value FROM Words WHERE Id = {selected.WordId}"));
        Assert.AreEqual(selectedDocumentBefore, await CaptureDocumentEvidenceAsync(fixture, selectedDocumentId));
        Assert.AreEqual(unrelatedBefore, await CaptureWordEvidenceAsync(fixture, unrelated.WordId));

        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Senses WHERE WordId = ?", selected.WordId));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Meanings WHERE WordId = ?", selected.WordId));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ContextSnapshots WHERE WordId = ?", selected.WordId));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningCards WHERE WordId = ?", selected.WordId));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM AnswerVariants WHERE SenseId IN (?, ?)", selectedSenseIds[0], selectedSenseIds[1]));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM SenseAnswerVariantAssignments WHERE SenseId IN (?, ?)", selectedSenseIds[0], selectedSenseIds[1]));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningReviews WHERE CardId IN (?, ?)", selected.CardId, 42));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM AnswerVariantProgress WHERE CardId IN (?, ?)", selected.CardId, 42));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessionCards WHERE CardId IN (?, ?)", selected.CardId, 42));

        var normalizedSession = (await fixture.Connection.QueryAsync<SessionRow>(
            "SELECT Id, Status, TotalCards, CompletedCards FROM LearningSessions WHERE Id = ?", sessionId)).Single();
        Assert.AreEqual(LearningSessionStatus.Active, normalizedSession.Status);
        Assert.AreEqual(1, normalizedSession.TotalCards);
        Assert.AreEqual(0, normalizedSession.CompletedCards);
        Assert.AreEqual(1, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessionCards WHERE SessionId = ? AND CardId = ?",
            sessionId, unrelated.CardId));

        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningSessionCards q LEFT JOIN LearningCards c ON c.Id = q.CardId WHERE c.Id IS NULL"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningReviews r LEFT JOIN LearningCards c ON c.Id = r.CardId WHERE c.Id IS NULL"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM AnswerVariantProgress p LEFT JOIN LearningCards c ON c.Id = p.CardId WHERE c.Id IS NULL"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM SenseAnswerVariantAssignments a LEFT JOIN Senses s ON s.Id = a.SenseId LEFT JOIN AnswerVariants v ON v.Id = a.AnswerVariantId WHERE s.Id IS NULL OR v.Id IS NULL"));
        Assert.AreEqual(0, await fixture.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LearningCards c LEFT JOIN Senses s ON s.Id = c.SenseId LEFT JOIN Meanings m ON m.Id = c.PreferredMeaningId WHERE s.Id IS NULL OR m.Id IS NULL"));
    }

    private static LearningService CreateService(Schema7Fixture fixture) => new(
        new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixture),
        new SimpleSpacedRepetitionScheduler(), new SpellingAnswerComparer(), new FakeClock(Now));

    private static async Task<SeededCard> SeedSchema7CardAsync(
        Schema7Fixture fixture, int cardId, string term, string answer, int frequency = 1)
    {
        var wordId = await fixture.InsertWordAsync(term, totalOccurrenceCount: frequency, createdAt: Now, updatedAt: Now);
        var meaningId = await fixture.InsertMeaningAsync(wordId, displayTerm: answer, translation: $"meaning-{answer}", definition: $"definition-{answer}", createdAt: Now, updatedAt: Now);
        await EnsureSourceDocumentAsync(fixture, cardId, $"doc-{term}");
        await fixture.InsertContextAsync(meaningId, wordId, sourceDocumentId: cardId, sourceDocumentTitle: $"doc-{term}", text: $"{term} context", targetStart: 0, targetLength: term.Length);
        await fixture.InsertCardAsync(wordId, meaningId, CardDirection.MeaningToTerm, id: cardId, createdAtUtc: Now, updatedAtUtc: Now);
        return new SeededCard(wordId, meaningId, cardId);
    }

    /// <summary>
    /// Every ContextSnapshot in these fixtures is document-backed, and Schema 8 validates
    /// <c>ContextSnapshots.SourceDocumentId</c> against a real <c>Documents</c> row. Seeds exactly the
    /// referenced Document with deterministic content and a real content fingerprint, so the ownership
    /// invariant holds without weakening the fixture or nulling the reference. <c>INSERT OR IGNORE</c> keeps
    /// the call idempotent when several snapshots legitimately share one source document.
    /// </summary>
    private static Task EnsureSourceDocumentAsync(Schema7Fixture fixture, int documentId, string title)
    {
        var content = $"{title} content";
        var fingerprint = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)))
            .ToLowerInvariant();
        return fixture.Connection.ExecuteAsync(
            """
            INSERT OR IGNORE INTO Documents
                (Id, Title, TextLanguage, ExplanationLanguage, LookupMode, TargetLanguage, Content,
                 ContentFingerprint, ImportedAt, WordCount)
            VALUES (?, ?, 'en', 'de', 0, '', ?, ?, ?, 1)
            """,
            documentId, title, content, fingerprint, Now);
    }

    /// <summary>
    /// Asserts the two Schema-8 ContextSnapshot ownership invariants: every snapshot resolves to its source
    /// Document, and to a Meaning belonging to the snapshot's own Word and Sense.
    /// </summary>
    private static async Task AssertContextOwnershipAsync(Schema7Fixture fixture)
    {
        Assert.AreEqual(
            0,
            await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM ContextSnapshots x LEFT JOIN Documents d ON d.Id = x.SourceDocumentId WHERE d.Id IS NULL"),
            "Every ContextSnapshot must resolve to its source Document.");
        Assert.AreEqual(
            0,
            await fixture.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM ContextSnapshots x LEFT JOIN Meanings m ON m.Id = x.MeaningId WHERE m.Id IS NULL OR m.WordId <> x.WordId OR m.SenseId <> x.SenseId"),
            "Every ContextSnapshot must be owned by a Meaning of the same Word and Sense.");
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
        await EnsureSourceDocumentAsync(fixture, meaningId, $"doc-{displayTerm}");
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
        await RowsAsync(fixture, "SELECT quote(Id)||'|'||quote(Status)||'|'||quote(PreparationState)||'|'||quote(TotalOccurrenceCount)||'|'||quote(DocumentCount)||'|'||quote(AutomaticInteractionMode)||'|'||quote(ConsecutiveRecallSuccessCount)||'|'||quote(ConsecutiveTypingSuccessCount)||'|'||quote(ConsecutiveTypingFailureCount)||'|'||quote(MasteryReviewExtensionScheduled)||'|'||quote(UpdatedAt) AS Value FROM Words ORDER BY Id"),
        await RowsAsync(fixture, "SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(Status)||'|'||quote(DefaultMeaningId)||'|'||quote(UpdatedAtUtc) AS Value FROM Senses ORDER BY Id"),
        await RowsAsync(fixture, "SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(SenseId)||'|'||quote(StableId)||'|'||quote(DisplayTerm)||'|'||quote(UpdatedAt) AS Value FROM Meanings ORDER BY Id"),
        await RowsAsync(fixture, "SELECT quote(Id)||'|'||quote(MeaningId)||'|'||quote(SenseId)||'|'||quote(WordId)||'|'||quote(SourceDocumentId)||'|'||quote(Text)||'|'||quote(TargetStart)||'|'||quote(TargetLength)||'|'||quote(CreatedAtUtc) AS Value FROM ContextSnapshots ORDER BY Id"),
        await RowsAsync(fixture, "SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(SenseId)||'|'||quote(PreferredMeaningId)||'|'||quote(Direction)||'|'||quote(State)||'|'||quote(DueAtUtc)||'|'||quote(IntervalDays)||'|'||quote(EaseFactor)||'|'||quote(SuccessfulReviewCount)||'|'||quote(LapseCount)||'|'||quote(LastReviewedAtUtc)||'|'||quote(LastRating)||'|'||quote(UpdatedAtUtc) AS Value FROM LearningCards ORDER BY Id"),
        await RowsAsync(fixture, "SELECT quote(Id)||'|'||quote(SenseId)||'|'||quote(DisplayText)||'|'||quote(NormalizedText)||'|'||quote(UpdatedAtUtc) AS Value FROM AnswerVariants ORDER BY Id"),
        await RowsAsync(fixture, "SELECT quote(Id)||'|'||quote(SenseId)||'|'||quote(CardDirection)||'|'||quote(AnswerVariantId)||'|'||quote(Requirement)||'|'||quote(IsPreferred)||'|'||quote(RequiredSinceUtc)||'|'||quote(UpdatedAtUtc) AS Value FROM SenseAnswerVariantAssignments ORDER BY Id"),
        await RowsAsync(fixture, "SELECT quote(Id)||'|'||quote(SessionId)||'|'||quote(CardId)||'|'||quote(QueueOrder)||'|'||quote(IsDueCard)||'|'||quote(IsAgainRepeat)||'|'||quote(AnswerRevealed)||'|'||quote(SpellingChecked)||'|'||quote(SpellingCorrect)||'|'||quote(IsCompleted)||'|'||quote(Rating)||'|'||quote(CompletedAtUtc)||'|'||quote(TargetAnswerVariantId) AS Value FROM LearningSessionCards ORDER BY Id"),
        await RowsAsync(fixture, "SELECT quote(Id)||'|'||quote(Status)||'|'||quote(TotalCards)||'|'||quote(CompletedCards)||'|'||quote(AgainCount)||'|'||quote(HardCount)||'|'||quote(GoodCount)||'|'||quote(EasyCount)||'|'||quote(StartedAtUtc)||'|'||quote(UpdatedAtUtc)||'|'||quote(CompletedAtUtc) AS Value FROM LearningSessions ORDER BY Id"),
        await RowsAsync(fixture, "SELECT quote(Id)||'|'||quote(CardId)||'|'||quote(AnswerVariantId)||'|'||quote(InteractionMode)||'|'||quote(ConsecutiveReadingSuccessCount)||'|'||quote(ConsecutiveTypingSuccessCount)||'|'||quote(ConsecutiveTypingFailureCount)||'|'||quote(IsMastered)||'|'||quote(ReplayVersion)||'|'||quote(CreatedAtUtc)||'|'||quote(UpdatedAtUtc) AS Value FROM AnswerVariantProgress ORDER BY Id"),
        await RowsAsync(fixture, "SELECT quote(Id)||'|'||quote(CardId)||'|'||quote(SessionId)||'|'||quote(Rating)||'|'||quote(WasTypedAnswer)||'|'||quote(WasCorrect)||'|'||quote(ReviewedAtUtc)||'|'||quote(TargetAnswerVariantId)||'|'||quote(MatchedAnswerVariantId) AS Value FROM LearningReviews ORDER BY Id")
    });

    private static async Task InsertImportedEvidenceAsync(
        Schema7Fixture fixture,
        int wordId,
        int documentId,
        string content,
        params (string SurfaceForm, int Start)[] occurrences)
    {
        await fixture.Connection.ExecuteAsync(
            "INSERT INTO SentenceSpans (DocumentId, StartPosition, Length, \"Order\") VALUES (?, 0, ?, 0)",
            documentId, content.Length);
        var sentenceId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
        for (var index = 0; index < occurrences.Length; index++)
        {
            var occurrence = occurrences[index];
            await fixture.Connection.ExecuteAsync(
                "INSERT INTO WordOccurrences (WordId, DocumentId, SentenceSpanId, StartPosition, Length, SurfaceForm, TechnicalFamily, TechnicalInstanceYear, TechnicalInstanceIdentifier, TechnicalVariant, \"Order\") VALUES (?, ?, ?, ?, ?, ?, 0, NULL, '', '', ?)",
                wordId, documentId, sentenceId, occurrence.Start, occurrence.SurfaceForm.Length,
                occurrence.SurfaceForm, index);
            await fixture.Connection.ExecuteAsync(
                "INSERT INTO WordForms (WordId, SurfaceForm, OccurrenceCount) VALUES (?, ?, 1)",
                wordId, occurrence.SurfaceForm);
        }
    }

    private static async Task<string> CaptureDocumentEvidenceAsync(Schema7Fixture fixture, int documentId) =>
        string.Join("\n", new[]
        {
            await RowsAsync(fixture, $"SELECT quote(Id)||'|'||quote(Title)||'|'||quote(Content)||'|'||quote(ContentFingerprint)||'|'||quote(ImportedAt)||'|'||quote(WordCount) AS Value FROM Documents WHERE Id = {documentId}"),
            await RowsAsync(fixture, $"SELECT quote(Id)||'|'||quote(DocumentId)||'|'||quote(StartPosition)||'|'||quote(Length)||'|'||quote(\"Order\") AS Value FROM SentenceSpans WHERE DocumentId = {documentId} ORDER BY Id"),
            await RowsAsync(fixture, $"SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(DocumentId)||'|'||quote(SentenceSpanId)||'|'||quote(StartPosition)||'|'||quote(Length)||'|'||quote(SurfaceForm)||'|'||quote(\"Order\") AS Value FROM WordOccurrences WHERE DocumentId = {documentId} ORDER BY Id")
        });

    private static async Task<string> CaptureWordEvidenceAsync(Schema7Fixture fixture, int wordId) =>
        string.Join("\n", new[]
        {
            await CaptureWordGraphAsync(fixture, wordId),
            await RowsAsync(fixture, $"SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(SurfaceForm)||'|'||quote(OccurrenceCount) AS Value FROM WordForms WHERE WordId = {wordId} ORDER BY Id"),
            await RowsAsync(fixture, $"SELECT quote(Id)||'|'||quote(WordId)||'|'||quote(DocumentId)||'|'||quote(SentenceSpanId)||'|'||quote(StartPosition)||'|'||quote(Length)||'|'||quote(SurfaceForm)||'|'||quote(\"Order\") AS Value FROM WordOccurrences WHERE WordId = {wordId} ORDER BY Id"),
            await RowsAsync(fixture, $"SELECT quote(d.Id)||'|'||quote(d.Title)||'|'||quote(d.Content)||'|'||quote(d.ContentFingerprint)||'|'||quote(d.ImportedAt)||'|'||quote(d.WordCount) AS Value FROM Documents d WHERE d.Id IN (SELECT DocumentId FROM WordOccurrences WHERE WordId = {wordId}) ORDER BY d.Id"),
            await RowsAsync(fixture, $"SELECT quote(s.Id)||'|'||quote(s.DocumentId)||'|'||quote(s.StartPosition)||'|'||quote(s.Length)||'|'||quote(s.\"Order\") AS Value FROM SentenceSpans s WHERE s.Id IN (SELECT SentenceSpanId FROM WordOccurrences WHERE WordId = {wordId}) ORDER BY s.Id")
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
    private sealed class IdRow { public int Id { get; set; } }
}
