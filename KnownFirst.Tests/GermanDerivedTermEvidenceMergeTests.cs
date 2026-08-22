using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Settings;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Schema8;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.Lexical;
using KnownFirst.Services.Study;
using SQLite;

namespace KnownFirst.Tests;

/// <summary>
/// German Enhanced Term Recognition Package 5A-2 — populated-target merge/convergence coverage for
/// transported <see cref="DerivedTermEvidenceEntity"/> rows. Every database is a synthetic, isolated
/// <see cref="Schema7Fixture"/> upgraded to the current schema via the real <see cref="DatabaseSchema.InitializeAsync"/>
/// path — each fixture gets its own unique root directory (see <see cref="Schema7Fixture.CreateAsync"/>), so
/// concurrent tests' merge-safety-copy sibling directories can never collide.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class GermanDerivedTermEvidenceMergeTests
{
    private static async Task<Schema7Fixture> CreateSchema11FixtureAsync()
    {
        var fixture = await Schema7Fixture.CreateAsync();
        await DatabaseSchema.InitializeAsync(fixture.Connection);
        return fixture;
    }

    private static async Task<int> ImportGermanReviewWithMaschineUnknownAsync(IKnownFirstDatabase database, string content = "Die Schreibmaschine steht hier.")
    {
        var review = new TextReviewService(
            database, new TextAnalyzer(), new EnabledEnhancedRecognitionSettings(), new FixtureGermanLexicon());
        var result = await review.ImportAsync(new ImportTextRequest("German import", content, "de", "de"));
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);

        var maschineWordId = -1;
        while (await review.GetCurrentCandidateAsync() is { } candidate)
        {
            if (candidate.Identity == "W:maschine")
            {
                maschineWordId = candidate.WordId;
                await review.DecideAsync(candidate.WordId, WordStatus.UnknownBacklog);
                continue;
            }

            await review.DecideAsync(candidate.WordId, WordStatus.Known);
        }

        Assert.AreNotEqual(-1, maschineWordId, "The derived 'maschine' candidate must have been reviewed.");
        return maschineWordId;
    }

    private static async Task ImportUnrelatedEnglishKnownWordAsync(IKnownFirstDatabase database)
    {
        var review = new TextReviewService(
            database, new TextAnalyzer(), new EnabledEnhancedRecognitionSettings(), new FixtureGermanLexicon());
        var result = await review.ImportAsync(new ImportTextRequest("English import", "harbor.", "en", "en"));
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);
        var candidate = await review.GetCurrentCandidateAsync();
        Assert.IsNotNull(candidate);
        await review.DecideAsync(candidate!.WordId, WordStatus.Known);
    }

    private static async Task<byte[]> ExportPortableArchiveAsync(IKnownFirstDatabase database)
    {
        using var stream = new MemoryStream();
        await new BackupService(database, new Schema8BackupFixtureBuilders.FakePlatformInfo())
            .CreatePortableArchiveAsync(stream, CancellationToken.None);
        return stream.ToArray();
    }

    private static Task<PortableImportResult> ImportArchiveAsync(IKnownFirstDatabase database, byte[] archiveBytes)
    {
        using var stream = new MemoryStream(archiveBytes);
        return new BackupService(database, new Schema8BackupFixtureBuilders.FakePlatformInfo())
            .ImportPortableArchiveAsync(stream, CancellationToken.None);
    }

    [TestMethod]
    public async Task PopulatedTargetMerge_InsertsMissingDerivedEvidenceSafely()
    {
        await using var sourceFixture = await CreateSchema11FixtureAsync();
        var sourceDatabase = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(sourceFixture);
        await ImportGermanReviewWithMaschineUnknownAsync(sourceDatabase);
        var archiveBytes = await ExportPortableArchiveAsync(sourceDatabase);

        await using var targetFixture = await CreateSchema11FixtureAsync();
        var targetDatabase = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture);
        await ImportUnrelatedEnglishKnownWordAsync(targetDatabase);

        var result = await ImportArchiveAsync(targetDatabase, archiveBytes);
        Assert.AreEqual(PortableImportStatus.Success, result.Status);

        var evidenceRows = await targetFixture.Connection.Table<DerivedTermEvidenceEntity>().ToListAsync();
        Assert.IsNotEmpty(evidenceRows, "Populated-target merge must insert the missing transported evidence.");
        var evidence = evidenceRows.Single();
        Assert.AreEqual("Schreibmaschine", evidence.SourceSurfaceForm);

        var candidateCount = await targetFixture.Connection.Table<ReviewCandidateEntity>()
            .Where(c => c.Id == evidence.ReviewCandidateId).CountAsync();
        Assert.AreEqual(1, candidateCount, "The merged evidence must resolve to exactly one real local ReviewCandidate row.");

        // Unrelated pre-existing target data (Direct candidate, unrelated Completed session) must remain unchanged.
        var harborWord = await targetFixture.Connection.Table<WordEntity>().Where(w => w.NormalizedTerm == "W:harbor").FirstAsync();
        Assert.AreEqual(WordStatus.Known, harborWord.Status);
    }

    [TestMethod]
    public async Task PopulatedTargetMerge_RepeatedImportConvergesToNoChanges()
    {
        await using var sourceFixture = await CreateSchema11FixtureAsync();
        var sourceDatabase = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(sourceFixture);
        await ImportGermanReviewWithMaschineUnknownAsync(sourceDatabase);
        var archiveBytes = await ExportPortableArchiveAsync(sourceDatabase);

        await using var targetFixture = await CreateSchema11FixtureAsync();
        var targetDatabase = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture);
        await ImportUnrelatedEnglishKnownWordAsync(targetDatabase);

        var firstResult = await ImportArchiveAsync(targetDatabase, archiveBytes);
        Assert.AreEqual(PortableImportStatus.Success, firstResult.Status);
        var evidenceCountAfterFirst = await targetFixture.Connection.Table<DerivedTermEvidenceEntity>().CountAsync();
        var candidateCountAfterFirst = await targetFixture.Connection.Table<ReviewCandidateEntity>().CountAsync();
        Assert.AreEqual(1, evidenceCountAfterFirst);

        var secondResult = await ImportArchiveAsync(targetDatabase, archiveBytes);
        Assert.AreEqual(PortableImportStatus.Success, secondResult.Status);

        var evidenceCountAfterSecond = await targetFixture.Connection.Table<DerivedTermEvidenceEntity>().CountAsync();
        var candidateCountAfterSecond = await targetFixture.Connection.Table<ReviewCandidateEntity>().CountAsync();
        Assert.AreEqual(evidenceCountAfterFirst, evidenceCountAfterSecond, "Re-importing the identical archive must not duplicate evidence.");
        Assert.AreEqual(candidateCountAfterFirst, candidateCountAfterSecond, "Re-importing the identical archive must not duplicate the candidate.");
    }

    [TestMethod]
    public async Task PopulatedTargetMerge_TwoInstallationExchangeConverges()
    {
        // A is the sole real origin of the German derived-evidence content (TextReviewService has no
        // injectable clock, so two genuinely independent real-time imports of "the same" text always
        // produce two distinct completed-review histories by design — see backup-merge-v1-design.md §4.4;
        // that is not a Package-5A-2 concern). This instead exercises the repeated PC<->phone exchange
        // shape: A's content is merged into B, merged into B again (idempotent), and the archive B now
        // holds (still exactly A's content, since B started with only unrelated data) is merged back into
        // A — the round trip must never duplicate the candidate or its evidence on either side.
        await using var fixtureA = await CreateSchema11FixtureAsync();
        var databaseA = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixtureA);
        await ImportGermanReviewWithMaschineUnknownAsync(databaseA);
        var archiveFromA = await ExportPortableArchiveAsync(databaseA);

        await using var fixtureB = await CreateSchema11FixtureAsync();
        var databaseB = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(fixtureB);
        await ImportUnrelatedEnglishKnownWordAsync(databaseB);

        // PC -> phone
        Assert.AreEqual(PortableImportStatus.Success, (await ImportArchiveAsync(databaseB, archiveFromA)).Status);
        // Repeated exchange (phone re-receives the same PC archive) must converge, never duplicate.
        Assert.AreEqual(PortableImportStatus.Success, (await ImportArchiveAsync(databaseB, archiveFromA)).Status);

        var evidenceCountInB = await fixtureB.Connection.Table<DerivedTermEvidenceEntity>().CountAsync();
        var candidateCountInB = await fixtureB.Connection.Table<ReviewCandidateEntity>().CountAsync();
        Assert.AreEqual(1, evidenceCountInB, "Repeated PC->phone exchange must dedupe evidence, never duplicate.");
        Assert.AreEqual(1, candidateCountInB, "Repeated PC->phone exchange must dedupe the candidate, never duplicate.");

        // phone -> PC: B's archive now carries exactly A's own original content, so merging it back into A
        // must be recognized as fully duplicate and converge, not create a second candidate/evidence row.
        var archiveFromB = await ExportPortableArchiveAsync(databaseB);
        Assert.AreEqual(PortableImportStatus.Success, (await ImportArchiveAsync(databaseA, archiveFromB)).Status);

        var evidenceCountInA = await fixtureA.Connection.Table<DerivedTermEvidenceEntity>().CountAsync();
        var candidateCountInA = await fixtureA.Connection.Table<ReviewCandidateEntity>().CountAsync();
        Assert.AreEqual(1, evidenceCountInA, "Merging the phone's archive back into the PC must dedupe evidence, never duplicate.");
        Assert.AreEqual(1, candidateCountInA, "Merging the phone's archive back into the PC must dedupe the candidate, never duplicate.");
    }

    /// <summary>MarkKnown after a populated-target merge must clean up the merged evidence and candidate,
    /// exactly as it does for evidence created natively on that installation.</summary>
    [TestMethod]
    public async Task PopulatedTargetMerge_MarkKnownCleansUpMergedEvidence()
    {
        await using var sourceFixture = await CreateSchema11FixtureAsync();
        var sourceDatabase = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(sourceFixture);
        var maschineWordId = await ImportGermanReviewWithMaschineUnknownAsync(sourceDatabase);
        var archiveBytes = await ExportPortableArchiveAsync(sourceDatabase);

        await using var targetFixture = await CreateSchema11FixtureAsync();
        var targetDatabase = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture);
        await ImportUnrelatedEnglishKnownWordAsync(targetDatabase);
        var result = await ImportArchiveAsync(targetDatabase, archiveBytes);
        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        Assert.IsGreaterThan(0, await targetFixture.Connection.Table<DerivedTermEvidenceEntity>().CountAsync());

        var targetWord = await targetFixture.Connection.Table<WordEntity>().Where(w => w.NormalizedTerm == "W:maschine").FirstAsync();

        var clock = new FakeClock(new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc));
        var preparation = new PreparationService(
            targetDatabase,
            new LexicalEnrichmentService(
                new AcronymExpansionDetector(),
                new MeaningRanker(),
                new LexicalCacheRepository(targetDatabase),
                new LexicalLookupProviderResolver([])),
            clock);
        try
        {
            await preparation.StartAsync(PreparationMethod.Manual, 20);
            var item = await preparation.GetCurrentAsync();
            Assert.IsNotNull(item);
            Assert.AreEqual(targetWord.Id, item!.WordId);
            await preparation.MarkKnownAsync(item.CandidateId);
        }
        finally
        {
            await preparation.CancelPrefetchAsync();
        }

        Assert.AreEqual(0, await targetFixture.Connection.Table<DerivedTermEvidenceEntity>().CountAsync());
        Assert.AreEqual(0, await targetFixture.Connection.Table<ReviewCandidateEntity>().Where(c => c.WordId == targetWord.Id).CountAsync());
        var reloadedWord = await targetFixture.Connection.Table<WordEntity>().Where(w => w.Id == targetWord.Id).FirstAsync();
        Assert.AreEqual(WordStatus.Known, reloadedWord.Status);
    }

    /// <summary>Exclude after a populated-target merge must clean up the merged evidence and candidate,
    /// exactly as MarkKnown already does (both share the same production completion path), and must not
    /// disturb unrelated pre-existing target data.</summary>
    [TestMethod]
    public async Task PopulatedTargetMerge_ExcludeCleansUpMergedEvidence()
    {
        await using var sourceFixture = await CreateSchema11FixtureAsync();
        var sourceDatabase = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(sourceFixture);
        var maschineWordId = await ImportGermanReviewWithMaschineUnknownAsync(sourceDatabase);
        var archiveBytes = await ExportPortableArchiveAsync(sourceDatabase);

        await using var targetFixture = await CreateSchema11FixtureAsync();
        var targetDatabase = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture);
        await ImportUnrelatedEnglishKnownWordAsync(targetDatabase);
        var result = await ImportArchiveAsync(targetDatabase, archiveBytes);
        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        Assert.IsGreaterThan(0, await targetFixture.Connection.Table<DerivedTermEvidenceEntity>().CountAsync());

        var targetWord = await targetFixture.Connection.Table<WordEntity>().Where(w => w.NormalizedTerm == "W:maschine").FirstAsync();
        var harborWord = await targetFixture.Connection.Table<WordEntity>().Where(w => w.NormalizedTerm == "W:harbor").FirstAsync();
        var harborCandidateCountBefore = await targetFixture.Connection.Table<ReviewCandidateEntity>()
            .Where(c => c.WordId == harborWord.Id).CountAsync();

        var clock = new FakeClock(new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc));
        var preparation = new PreparationService(
            targetDatabase,
            new LexicalEnrichmentService(
                new AcronymExpansionDetector(),
                new MeaningRanker(),
                new LexicalCacheRepository(targetDatabase),
                new LexicalLookupProviderResolver([])),
            clock);
        try
        {
            await preparation.StartAsync(PreparationMethod.Manual, 20);
            var item = await preparation.GetCurrentAsync();
            Assert.IsNotNull(item);
            Assert.AreEqual(targetWord.Id, item!.WordId);
            await preparation.ExcludeAsync(item.CandidateId);
        }
        finally
        {
            await preparation.CancelPrefetchAsync();
        }

        Assert.AreEqual(0, await targetFixture.Connection.Table<DerivedTermEvidenceEntity>().CountAsync(),
            "Exclude must remove the merged derivation evidence, exactly as MarkKnown already does.");
        Assert.AreEqual(0, await targetFixture.Connection.Table<ReviewCandidateEntity>().Where(c => c.WordId == targetWord.Id).CountAsync(),
            "Exclude must remove the retained owning ReviewCandidate.");
        var reloadedWord = await targetFixture.Connection.Table<WordEntity>().Where(w => w.Id == targetWord.Id).FirstAsync();
        Assert.AreEqual(WordStatus.Ignored, reloadedWord.Status);

        var reloadedHarborWord = await targetFixture.Connection.Table<WordEntity>().Where(w => w.Id == harborWord.Id).FirstAsync();
        Assert.AreEqual(WordStatus.Known, reloadedHarborWord.Status, "Unrelated pre-existing target data must remain unchanged.");
        var harborCandidateCountAfter = await targetFixture.Connection.Table<ReviewCandidateEntity>()
            .Where(c => c.WordId == harborWord.Id).CountAsync();
        Assert.AreEqual(harborCandidateCountBefore, harborCandidateCountAfter, "Unrelated candidate rows must not be deleted by Exclude.");
    }

    /// <summary>
    /// Package-5A-2 counterpart of the native-evidence generic-document-cleanup protection already proven by
    /// <c>GermanDerivedTermPreparationTests.GenericDocumentCleanup_DoesNotOrphanRetainedDerivedEvidence</c>:
    /// evidence, its owning candidate, session, Document, and exact SentenceSpan recreated through an actual
    /// populated-target merge (never locally authored on this installation) must survive the same generic
    /// cleanup sweep, triggered here by an entirely unrelated word's completion elsewhere in the same
    /// database. The unrelated document itself is asserted deleted, proving active protection rather than a
    /// no-op cleanup invocation.
    /// </summary>
    [TestMethod]
    public async Task PopulatedTargetMerge_GenericDocumentCleanupDoesNotOrphanMergedEvidence()
    {
        await using var sourceFixture = await CreateSchema11FixtureAsync();
        var sourceDatabase = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(sourceFixture);
        await ImportGermanReviewWithMaschineUnknownAsync(sourceDatabase);
        var archiveBytes = await ExportPortableArchiveAsync(sourceDatabase);

        await using var targetFixture = await CreateSchema11FixtureAsync();
        var targetDatabase = new Schema8BackupFixtureBuilders.Schema8DatabaseAdapter(targetFixture);

        var result = await ImportArchiveAsync(targetDatabase, archiveBytes);
        Assert.AreEqual(PortableImportStatus.Success, result.Status);

        var evidenceBefore = await targetFixture.Connection.Table<DerivedTermEvidenceEntity>().ToListAsync();
        Assert.HasCount(1, evidenceBefore, "Precondition: the merge must have recreated exactly one evidence row.");
        var evidence = evidenceBefore[0];

        var candidate = await targetFixture.Connection.FindAsync<ReviewCandidateEntity>(evidence.ReviewCandidateId);
        Assert.IsNotNull(candidate, "Precondition: the evidence's owning ReviewCandidate must exist after merge.");
        var session = await targetFixture.Connection.FindAsync<ReviewSessionEntity>(candidate.SessionId);
        Assert.IsNotNull(session, "Precondition: the owning ReviewSession must exist after merge.");
        var mergedDocumentId = session.DocumentId;
        var mergedDocument = await targetFixture.Connection.FindAsync<DocumentEntity>(mergedDocumentId);
        Assert.IsNotNull(mergedDocument, "Precondition: the owning Document must exist after merge.");
        var mergedSentence = await targetFixture.Connection.Table<SentenceSpanEntity>()
            .Where(s => s.DocumentId == mergedDocumentId && s.Order == evidence.SourceSentenceOrder).FirstOrDefaultAsync();
        Assert.IsNotNull(mergedSentence, "Precondition: the exact owning SentenceSpan must exist after merge.");

        var mergedOccurrenceCount = await targetFixture.Connection.Table<WordOccurrenceEntity>()
            .Where(o => o.DocumentId == mergedDocumentId).CountAsync();
        Assert.AreEqual(0, mergedOccurrenceCount,
            "Precondition: the merged document must have zero occurrences, matching the generic cleanup's own eligibility signal.");

        // Unrelated, ordinary English import/decision entirely on the target — the merged German document
        // is never touched by this trigger, only reached by the generic sweep it causes.
        var unrelatedReview = new TextReviewService(
            targetDatabase, new TextAnalyzer(), new EnabledEnhancedRecognitionSettings(), new FixtureGermanLexicon());
        var unrelatedImport = await unrelatedReview.ImportAsync(new ImportTextRequest("English import", "harbor.", "en", "en"));
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, unrelatedImport.Outcome);
        var unrelatedCandidate = await unrelatedReview.GetCurrentCandidateAsync();
        Assert.IsNotNull(unrelatedCandidate);
        await unrelatedReview.DecideAsync(unrelatedCandidate!.WordId, WordStatus.UnknownBacklog);

        var clock = new FakeClock(new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc));
        var preparation = new PreparationService(
            targetDatabase,
            new LexicalEnrichmentService(
                new AcronymExpansionDetector(),
                new MeaningRanker(),
                new LexicalCacheRepository(targetDatabase),
                new LexicalLookupProviderResolver([])),
            clock);
        try
        {
            await preparation.StartAsync(PreparationMethod.Manual, 20);
            var unrelatedItem = await preparation.GetCurrentAsync();
            Assert.IsNotNull(unrelatedItem);
            Assert.AreNotEqual(candidate.WordId, unrelatedItem!.WordId, "The trigger must act on the unrelated word, not the retained derived word.");

            // Triggers PreparationService.CompleteWithoutLearningAsync, which calls
            // DocumentCleanupOperations.CleanupEligibleDocuments across every document in the target database.
            await preparation.MarkKnownAsync(unrelatedItem.CandidateId);
        }
        finally
        {
            await preparation.CancelPrefetchAsync();
        }

        var evidenceAfter = await targetFixture.Connection.Table<DerivedTermEvidenceEntity>().ToListAsync();
        Assert.HasCount(1, evidenceAfter, "The merged derivation evidence must survive the unrelated cleanup trigger.");

        var documentSurvives = await targetFixture.Connection.Table<DocumentEntity>()
            .Where(d => d.Id == mergedDocumentId).CountAsync() > 0;
        Assert.IsTrue(documentSurvives, "The generic document cleanup must not delete the merged evidence's owning Document.");

        var sentenceSurvives = await targetFixture.Connection.Table<SentenceSpanEntity>()
            .Where(s => s.DocumentId == mergedDocumentId && s.Order == evidence.SourceSentenceOrder).CountAsync() > 0;
        Assert.IsTrue(sentenceSurvives, "The generic document cleanup must not delete the merged evidence's exact owning SentenceSpan.");

        var reloadedDocument = await targetFixture.Connection.FindAsync<DocumentEntity>(mergedDocumentId);
        var actualSubstring = reloadedDocument.Content.Substring(evidence.SourceStartPosition, evidence.SourceLength);
        Assert.AreEqual(evidence.SourceSurfaceForm, actualSubstring, "The source substring/range must still resolve correctly after cleanup.");

        // Proves active protection rather than a no-op cleanup invocation: the genuinely unreferenced
        // unrelated English document must still be deleted by the same sweep.
        var unrelatedDocumentSurvives = await targetFixture.Connection.Table<DocumentEntity>()
            .Where(d => d.Id == unrelatedImport.DocumentId).CountAsync() > 0;
        Assert.IsFalse(unrelatedDocumentSurvives, "A genuinely unreferenced unrelated document must remain cleanup-eligible.");
    }

    private sealed class EnabledEnhancedRecognitionSettings : IAppSettingsService
    {
        public int PreparationLimit => 20;
        public IReadOnlyList<int> SupportedPreparationLimits => [20];
        public CardDirectionPreference CardDirection => CardDirectionPreference.Both;
        public LearningMode LearningMode => LearningMode.Automatic;
        public bool HasOnlineLookupConsent => false;
        public bool EnhancedTermRecognitionEnabled => true;
        public LearningTimezoneMode LearningTimezoneMode => LearningTimezoneMode.System;
        public string? ExplicitLearningTimezoneId => null;
        public int LearningDayCutoffMinutes => 0;

        public void SetPreparationLimit(int preparationLimit) => throw new NotSupportedException();
        public void SetCardDirection(CardDirectionPreference preference) => throw new NotSupportedException();
        public void SetLearningMode(LearningMode mode) => throw new NotSupportedException();
        public void GrantOnlineLookupConsent() => throw new NotSupportedException();
        public void RevokeOnlineLookupConsent() => throw new NotSupportedException();
        public void SetEnhancedTermRecognitionEnabled(bool value) => throw new NotSupportedException();
        public void SetLearningTimezoneMode(LearningTimezoneMode mode) => throw new NotSupportedException();
        public void SetExplicitLearningTimezoneId(string? timezoneId) => throw new NotSupportedException();
        public void SetLearningDayCutoffMinutes(int minutes) => throw new NotSupportedException();
        public void Reset() => throw new NotSupportedException();
    }
}
