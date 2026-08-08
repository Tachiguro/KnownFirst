using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Schema8;
using KnownFirst.Models;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using KnownFirst.Services.DataSafety.Merge;
using SQLite;

namespace KnownFirst.Tests;

/// <summary>
/// KF-MEANING-001 Slice 9 — real end-to-end validation of the complete production Import path:
/// archive creation → validation → read-only preview → preflight → validated safety copy → transactional
/// writer → deterministic scheduler replay → result summary → repeated-import no-change. Every database is
/// a synthetic, isolated temporary SQLite fixture (<see cref="Schema7Fixture"/> migrated via
/// <see cref="Data.Migrations.Schema8.Schema8DormantMigration"/>, then relocated into its own uniquely
/// named directory by <see cref="IsolatedMergeTarget"/> so its safety-copy sibling directory can never
/// collide with another test's); no real user database is ever opened. Convergence is proven by comparing
/// content-derived semantic projections (never SQLite integer ids) of two independently-built, divergent
/// Schema-8 databases after merging each into the other.
/// </summary>
[TestClass]
public sealed class PortableImportEndToEndConvergenceTests
{
    private sealed class FakePlatformInfo : IBackupPlatformInfo
    {
        public BackupSourcePlatform SourcePlatform => BackupSourcePlatform.Windows;
        public string SourceAppVersion => "1.0.0-test";
    }

    /// <summary>Fires once, the first time the named writer checkpoint is reached, then throws — proves a
    /// real mid-transaction production failure rolls back completely even after a real safety copy has
    /// already been created and validated.</summary>
    private sealed class ThrowAtCheckpointInjector(string checkpointName) : IBackupImportFailureInjector
    {
        private bool _fired;

        public void AfterMutation(int mutationCount)
        {
        }

        public void AtCheckpoint(string checkpointName2)
        {
            if (!_fired && string.Equals(checkpointName2, checkpointName, StringComparison.Ordinal))
            {
                _fired = true;
                throw new InvalidOperationException($"Injected failure at checkpoint '{checkpointName2}'.");
            }
        }
    }

    private const string DuringSchedulerReplayCheckpoint = "MergeWriter.DuringSchedulerReplay";

    private static readonly DateTime Epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A populated Schema-8 database relocated into its own uniquely named directory, so its safety-copy
    /// sibling directory (<see cref="MergeSafetyCopyService.DirectoryName"/>, resolved next to
    /// <see cref="DatabasePath"/>) can never collide with another <see cref="Schema7Fixture"/>-based test —
    /// every plain <see cref="Schema7Fixture"/> shares the same bare system temp directory as its
    /// database's parent. <see cref="FromBuiltFixtureAsync"/> closes the source fixture's connection and
    /// moves its already-built-and-migrated file here; the source fixture must not be used again afterward.
    /// </summary>
    private sealed class IsolatedMergeTarget : IKnownFirstDatabase, IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly string _testRoot;

        private IsolatedMergeTarget(string testRoot, string databasePath, SQLiteAsyncConnection connection)
        {
            _testRoot = testRoot;
            DatabasePath = databasePath;
            Connection = connection;
        }

        public string DatabasePath { get; }

        public SQLiteAsyncConnection Connection { get; }

        public static async Task<IsolatedMergeTarget> FromBuiltFixtureAsync(Schema7Fixture source)
        {
            await source.Connection.CloseAsync();

            var testRoot = Path.Combine(Path.GetTempPath(), "kf-e2e-convergence-test", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);
            var databasePath = Path.Combine(testRoot, "knownfirst.db3");
            File.Move(source.DatabasePath, databasePath);

            return new IsolatedMergeTarget(testRoot, databasePath, new SQLiteAsyncConnection(databasePath));
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task<T> ReadAsync<T>(Func<SQLiteAsyncConnection, Task<T>> operation)
        {
            await _gate.WaitAsync();
            try
            {
                return await operation(Connection);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<T> RunInTransactionAsync<T>(Func<SQLiteConnection, T> operation)
        {
            await _gate.WaitAsync();
            try
            {
                T? result = default;
                await Connection.RunInTransactionAsync(connection => result = operation(connection));
                return result!;
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task<T> ExecuteSnapshotAsync<T>(Func<SQLiteConnection, T> operation) => RunInTransactionAsync(operation);

        public Task ResetAsync() => throw new NotSupportedException("Not used by these tests.");

        public async ValueTask DisposeAsync()
        {
            await TemporaryDatabaseFiles.CloseAndDeleteAsync(Connection, DatabasePath);
            try
            {
                Directory.Delete(_testRoot, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of the private isolated directory.
            }

            _gate.Dispose();
        }
    }

    // ==== Fixture construction ====

    /// <summary>
    /// Builds one deterministic, populated Schema-8 device database containing: a "shared" word/Sense/
    /// Meaning/Card present with byte-identical content on both A and B (so it deduplicates and its card
    /// receives the OTHER side's review event during merge — the review-history difference that exercises
    /// scheduler replay); one "only{ownSuffix}"-named word/Sense/Meaning/Card unique to this side (proves
    /// source-only/target-only preservation); and a "split" word carrying two independently-discriminated
    /// Meanings — present identically on both sides — that must remain two independent Senses after merge.
    /// The shared word's non-default automatic-learning counters guarantee a derived
    /// <c>AnswerVariantProgress</c> row. Building this helper twice with different <paramref name="ownReviewedAt"/>/
    /// <paramref name="ownRating"/> values yields two databases whose shared content matches by stable merge
    /// identity but whose review history genuinely diverges.
    /// </summary>
    private static async Task<Schema7Fixture> BuildDivergentDatabaseAsync(
        string ownSuffix, DateTime ownReviewedAt, ReviewRating ownRating)
    {
        var fixture = await Schema7Fixture.CreateAsync();

        var documentId = await fixture.InsertDocumentAsync(
            title: "Shared Doc", content: "shared demonstration text", importedAt: Epoch);

        // ---- Shared entity graph: identical content on both A and B ----
        var sharedWordId = await fixture.InsertWordAsync(
            "shared", status: WordStatus.Learning,
            consecutiveRecallSuccessCount: 2, consecutiveTypingSuccessCount: 1, consecutiveTypingFailureCount: 0,
            createdAt: Epoch, updatedAt: Epoch);
        var sharedMeaningId = await fixture.InsertMeaningAsync(
            sharedWordId, displayTerm: "shared", translation: "geteilt", selectedMeaningId: "shared-1",
            definition: "shared meaning", createdAt: Epoch, updatedAt: Epoch);
        var sharedCardId = await fixture.InsertCardAsync(
            sharedWordId, sharedMeaningId, CardDirection.MeaningToTerm,
            state: CardState.New, dueAtUtc: Epoch, intervalDays: 0,
            easeFactor: SimpleSpacedRepetitionScheduler.DefaultEaseFactor,
            createdAtUtc: Epoch, updatedAtUtc: Epoch);
        await fixture.InsertContextAsync(
            sharedMeaningId, sharedWordId, sourceDocumentId: documentId, sourceDocumentTitle: "Shared Doc",
            text: "shared demonstration text", targetStart: 0, targetLength: 6,
            normalizedFingerprint: "shared-context-fp", createdAtUtc: Epoch);

        // ---- Own-only entity: exists only on this side ----
        var onlyWordId = await fixture.InsertWordAsync(
            $"only{ownSuffix}", status: WordStatus.Learning, createdAt: Epoch, updatedAt: Epoch);
        var onlyMeaningId = await fixture.InsertMeaningAsync(
            onlyWordId, displayTerm: $"only{ownSuffix}", translation: $"nur{ownSuffix}",
            selectedMeaningId: $"only-{ownSuffix}", definition: "own-only meaning", createdAt: Epoch, updatedAt: Epoch);
        var onlyCardId = await fixture.InsertCardAsync(
            onlyWordId, onlyMeaningId, CardDirection.TermToMeaning,
            dueAtUtc: Epoch, createdAtUtc: Epoch, updatedAtUtc: Epoch);

        // Two independent real LearningSessions per side: one over the shared card (whose StartedAtUtc/
        // CompletedAtUtc/Rating genuinely diverge between A and B — the corrected Schema-8 session
        // identity contract, KF-MEANING-001 Slice 9), and one over this side's own-only card. Neither
        // needs a different card set to stay distinct: the shared-card session on A and on B is now
        // correctly kept apart by its own timestamps/rating, not by an artificial card-set difference.
        var sharedSessionId = await fixture.InsertLearningSessionAsync(
            startedAtUtc: ownReviewedAt, updatedAtUtc: ownReviewedAt, completedAtUtc: ownReviewedAt);
        await fixture.InsertReviewAsync(
            sharedCardId, sessionId: sharedSessionId, rating: ownRating, wasTypedAnswer: true,
            wasCorrect: ownRating != ReviewRating.Again,
            reviewedAtUtc: ownReviewedAt, dueAtUtc: ownReviewedAt.AddDays(1), intervalDays: 1,
            easeFactor: SimpleSpacedRepetitionScheduler.DefaultEaseFactor);
        await fixture.InsertQueueItemAsync(
            sessionId: sharedSessionId, cardId: sharedCardId, queueOrder: 1, isCompleted: true, rating: ownRating,
            completedAtUtc: ownReviewedAt);

        var ownOnlySessionId = await fixture.InsertLearningSessionAsync(
            startedAtUtc: ownReviewedAt, updatedAtUtc: ownReviewedAt, completedAtUtc: ownReviewedAt);
        await fixture.InsertReviewAsync(
            onlyCardId, sessionId: ownOnlySessionId, rating: ReviewRating.Good, wasTypedAnswer: true, wasCorrect: true,
            reviewedAtUtc: ownReviewedAt, dueAtUtc: ownReviewedAt.AddDays(1), intervalDays: 1,
            easeFactor: SimpleSpacedRepetitionScheduler.DefaultEaseFactor);
        await fixture.InsertQueueItemAsync(
            sessionId: ownOnlySessionId, cardId: onlyCardId, queueOrder: 1, isCompleted: true, rating: ReviewRating.Good,
            completedAtUtc: ownReviewedAt);

        // ---- Multi-Sense word: identical on both sides, two independent Senses ----
        var splitWordId = await fixture.InsertWordAsync("split", status: WordStatus.Learning, createdAt: Epoch, updatedAt: Epoch);
        var splitMeaningAId = await fixture.InsertMeaningAsync(
            splitWordId, displayTerm: "split", translation: "teilen", selectedMeaningId: "split-a",
            definition: "to divide", createdAt: Epoch, updatedAt: Epoch);
        var splitMeaningBId = await fixture.InsertMeaningAsync(
            splitWordId, displayTerm: "split", translation: "Riss", selectedMeaningId: "split-b",
            definition: "a crack", createdAt: Epoch, updatedAt: Epoch);
        // Pre-migration Schema-7 shape enforces a single (WordId, Direction) card, so the two Senses'
        // cards must use different directions here — Schema-8's own (SenseId, Direction) uniqueness (which
        // permits same-direction cards across distinct Senses) only exists after migration. dueAtUtc is
        // pinned to Epoch (never the default wall-clock "now") so A's and B's independently-built,
        // otherwise-identical cards are byte-identical, not merely close in time.
        await fixture.InsertCardAsync(
            splitWordId, splitMeaningAId, CardDirection.MeaningToTerm,
            dueAtUtc: Epoch, createdAtUtc: Epoch, updatedAtUtc: Epoch);
        await fixture.InsertCardAsync(
            splitWordId, splitMeaningBId, CardDirection.TermToMeaning,
            dueAtUtc: Epoch, createdAtUtc: Epoch, updatedAtUtc: Epoch);

        return fixture;
    }

    private static async Task MigrateAsync(Schema7Fixture fixture) =>
        await Schema8BackupFixtureBuilders.MigrateAsync(fixture);

    // ==== Archive capture ====

    private static async Task<byte[]> CaptureV2ArchiveBytesAsync(SQLiteAsyncConnection connection)
    {
        Schema8BackupSnapshot? snapshot = null;
        await connection.RunInTransactionAsync(conn => snapshot = Schema8BackupSnapshotRepository.CaptureSnapshot(conn));
        BackupSchemaCapabilityResult? capabilityResult = null;
        await connection.RunInTransactionAsync(conn => capabilityResult = BackupSchemaCapability.Resolve(conn));
        var capability = ((Schema8CapabilityResult)capabilityResult!).Capability;
        var payload = BackupModelMapperV2.MapToExternal(snapshot!);

        using var stream = new MemoryStream();
        await BackupArchiveWriterV2.WriteArchiveAsync(payload, new FakePlatformInfo(), capability, DateTime.UtcNow, stream, CancellationToken.None);
        return stream.ToArray();
    }

    private static async Task<byte[]> CaptureV1ArchiveBytesAsync(Schema7Fixture fixture)
    {
        BackupSnapshot? snapshot = null;
        await fixture.Connection.RunInTransactionAsync(conn => snapshot = BackupSnapshotRepository.CaptureSnapshot(conn));
        BackupSchemaCapabilityResult? capabilityResult = null;
        await fixture.Connection.RunInTransactionAsync(conn => capabilityResult = BackupSchemaCapability.Resolve(conn));
        var capability = ((Schema7CapabilityResult)capabilityResult!).Capability;
        var payload = BackupModelMapper.MapToExternal(snapshot!);

        using var stream = new MemoryStream();
        await BackupArchiveWriter.WriteArchiveAsync(payload, new FakePlatformInfo(), capability, DateTime.UtcNow, stream, CancellationToken.None);
        return stream.ToArray();
    }

    private static async Task<BackupPayloadV2> CaptureCurrentPayloadAsync(SQLiteAsyncConnection connection)
    {
        Schema8PortableSnapshotCaptureResult? result = null;
        await connection.RunInTransactionAsync(
            conn => result = Schema8BackupSnapshotRepository.CapturePortableSnapshotForMergeSafetyCopy(conn));
        return BackupModelMapperV2.MapToExternal(result!.Snapshot!);
    }

    // ==== Canonical semantic projection: content-derived, never a SQLite integer id ====

    private sealed record CanonicalProjection(
        IReadOnlyList<string> Vocabulary,
        IReadOnlyList<string> Senses,
        IReadOnlyList<string> Meanings,
        IReadOnlyList<string> AnswerVariants,
        IReadOnlyList<string> Assignments,
        IReadOnlyList<string> Cards);

    private static string Norm(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Projects a captured payload into deterministic, sorted content-key strings. Every relationship is
    /// resolved through the payload's own archive-local ids (never a target-database integer id), and
    /// every key is built from real content (canonical vocabulary identity, Sense discriminators, answer
    /// text, requirement/preference, and — for cards — the derived scheduling fields the merge writer
    /// itself produces). Wall-clock creation/update timestamps are deliberately excluded.
    /// </summary>
    private static CanonicalProjection Canonicalize(BackupPayloadV2 payload)
    {
        var vocabById = payload.Vocabulary.ToDictionary(v => v.Id);
        var senseById = payload.Senses.ToDictionary(s => s.Id);
        var variantById = payload.AnswerVariants.ToDictionary(v => v.Id);

        string VocabKey(string vocabularyId)
        {
            var v = vocabById[vocabularyId];
            return $"{v.Language}|{v.IdentityKey}";
        }

        string SenseKey(string senseId)
        {
            var s = senseById[senseId];
            return $"{VocabKey(s.VocabularyId)}||{s.SourceLanguage}|{s.ExplanationLanguage}|{s.ProviderSenseId}";
        }

        var vocabulary = payload.Vocabulary
            .Select(v => $"{v.Language}|{v.IdentityKey}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var senses = payload.Senses
            .Select(s => SenseKey(s.Id))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var meanings = payload.PreparedLearning
            .Select(m => $"{SenseKey(m.SenseId)}||{m.SourceLanguage}|{m.ExplanationLanguage}|{Norm(m.Translation)}|{Norm(m.Definition)}|{m.ConfirmedByUser}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var answerVariants = payload.AnswerVariants
            .Select(v => $"{SenseKey(v.SenseId)}||{v.AnswerLanguage}|{v.NormalizedText}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var assignments = payload.SenseAnswerVariantAssignments
            .Select(a =>
            {
                var variant = variantById[a.AnswerVariantId];
                return $"{SenseKey(a.SenseId)}||{a.CardDirection}|{variant.AnswerLanguage}|{variant.NormalizedText}|{a.Requirement}|{a.IsPreferred}";
            })
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var cards = payload.Learning.Cards
            .Select(c => $"{VocabKey(c.VocabularyId)}||{SenseKey(c.SenseId)}|{c.Direction}||{c.State}|{c.DueAtUtc:O}|{c.IntervalDays}|{c.EaseFactor}|{c.SuccessfulReviewCount}|{c.LapseCount}|{c.LastReviewedAtUtc:O}|{c.LastRating}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        return new CanonicalProjection(vocabulary, senses, meanings, answerVariants, assignments, cards);
    }

    private static void AssertProjectionsConverge(CanonicalProjection a, CanonicalProjection b)
    {
        CollectionAssert.AreEqual((System.Collections.ICollection)a.Vocabulary, (System.Collections.ICollection)b.Vocabulary, "Vocabulary sets must converge.");
        CollectionAssert.AreEqual((System.Collections.ICollection)a.Senses, (System.Collections.ICollection)b.Senses, "Sense sets must converge.");
        CollectionAssert.AreEqual((System.Collections.ICollection)a.Meanings, (System.Collections.ICollection)b.Meanings, "Meaning content sets must converge.");
        CollectionAssert.AreEqual((System.Collections.ICollection)a.AnswerVariants, (System.Collections.ICollection)b.AnswerVariants, "AnswerVariant sets must converge.");
        CollectionAssert.AreEqual((System.Collections.ICollection)a.Assignments, (System.Collections.ICollection)b.Assignments, "Assignment sets must converge.");
        CollectionAssert.AreEqual((System.Collections.ICollection)a.Cards, (System.Collections.ICollection)b.Cards, "Card identity + derived scheduling sets must converge.");
    }

    // ==== Relationship-integrity checks (adapted from MergeWriterServiceTests.AssertReferentialIntegrityAsync) ====

    private static async Task<int> CountAsync(SQLiteAsyncConnection connection, string sql) =>
        await connection.ExecuteScalarAsync<int>(sql);

    private static async Task AssertReferentialIntegrityAsync(SQLiteAsyncConnection connection)
    {
        Assert.AreEqual(0, await CountAsync(connection, "SELECT COUNT(*) FROM Senses s LEFT JOIN Words w ON w.Id = s.WordId WHERE w.Id IS NULL"));
        Assert.AreEqual(0, await CountAsync(connection, "SELECT COUNT(*) FROM Meanings m LEFT JOIN Words w ON w.Id = m.WordId LEFT JOIN Senses s ON s.Id = m.SenseId WHERE w.Id IS NULL OR s.Id IS NULL"));
        Assert.AreEqual(0, await CountAsync(connection, "SELECT COUNT(*) FROM AnswerVariants v LEFT JOIN Senses s ON s.Id = v.SenseId WHERE s.Id IS NULL"));
        Assert.AreEqual(0, await CountAsync(connection, "SELECT COUNT(*) FROM SenseAnswerVariantAssignments a LEFT JOIN Senses s ON s.Id = a.SenseId LEFT JOIN AnswerVariants v ON v.Id = a.AnswerVariantId WHERE s.Id IS NULL OR v.Id IS NULL"));
        Assert.AreEqual(0, await CountAsync(connection, "SELECT COUNT(*) FROM LearningCards c LEFT JOIN Words w ON w.Id = c.WordId LEFT JOIN Senses s ON s.Id = c.SenseId LEFT JOIN Meanings m ON m.Id = c.PreferredMeaningId WHERE w.Id IS NULL OR s.Id IS NULL OR m.Id IS NULL"));
        Assert.AreEqual(0, await CountAsync(connection, "SELECT COUNT(*) FROM LearningReviews r LEFT JOIN LearningCards c ON c.Id = r.CardId LEFT JOIN LearningSessions ls ON ls.Id = r.SessionId WHERE c.Id IS NULL OR ls.Id IS NULL"));
        Assert.AreEqual(0, await CountAsync(connection, "SELECT COUNT(*) FROM AnswerVariantProgress p LEFT JOIN LearningCards c ON c.Id = p.CardId LEFT JOIN AnswerVariants v ON v.Id = p.AnswerVariantId WHERE c.Id IS NULL OR v.Id IS NULL"));
        Assert.AreEqual(0, await CountAsync(connection, "SELECT COUNT(*) FROM LearningSessionCards q LEFT JOIN LearningSessions ls ON ls.Id = q.SessionId LEFT JOIN LearningCards c ON c.Id = q.CardId WHERE ls.Id IS NULL OR c.Id IS NULL"));
        Assert.AreEqual(0, await CountAsync(connection, "SELECT COUNT(*) FROM ContextSnapshots cs LEFT JOIN Meanings m ON m.Id = cs.MeaningId LEFT JOIN Senses s ON s.Id = cs.SenseId LEFT JOIN Documents d ON d.Id = cs.SourceDocumentId WHERE m.Id IS NULL OR s.Id IS NULL OR d.Id IS NULL"));
        Assert.AreEqual(0, await CountAsync(connection, "SELECT COUNT(*) FROM ReviewCandidates rc LEFT JOIN ReviewSessions rs ON rs.Id = rc.SessionId LEFT JOIN Words w ON w.Id = rc.WordId WHERE rs.Id IS NULL OR w.Id IS NULL"));
        Assert.AreEqual(0, await CountAsync(connection, "SELECT COUNT(*) FROM PreparationCandidates pc LEFT JOIN PreparationSessions ps ON ps.Id = pc.SessionId WHERE ps.Id IS NULL"));
    }

    // ==== Safety-copy evidence ====

    private static string SafetyCopyDirectory(string databasePath) =>
        Path.Combine(Path.GetDirectoryName(databasePath)!, MergeSafetyCopyService.DirectoryName);

    private static async Task<string> AssertSingleValidSafetyCopyAsync(string databasePath)
    {
        var directory = SafetyCopyDirectory(databasePath);
        Assert.IsTrue(Directory.Exists(directory), "A safety-copy directory must exist after a mutating merge.");

        var archives = Directory.GetFiles(directory, "*.kfarchive");
        var metadata = Directory.GetFiles(directory, "*.metadata.json");
        var staging = Directory.GetFiles(directory, "*.mergestaging");
        Assert.HasCount(1, archives, "Exactly one safety-copy archive must exist.");
        Assert.HasCount(1, metadata, "Exactly one safety-copy metadata file must exist.");
        Assert.IsEmpty(staging, "No staging artifact may remain.");

        await using var readStream = new FileStream(archives[0], FileMode.Open, FileAccess.Read);
        var validated = await BackupArchiveReader.ValidateVersionedAsync(readStream, CancellationToken.None);
        Assert.IsNotNull(validated.V2, "The safety copy must be a valid, readable v2 archive.");

        return archives[0];
    }

    // ==== 1, 2, 3, 4: bidirectional divergent-database convergence, preview/result agreement, ====
    // ==== safety-copy validity, and reimport no-change convergence ====
    [TestMethod]
    public async Task BidirectionalDivergentDatabases_ConvergeSemanticaly_WithValidatedSafetyCopiesAndNoChangeReimport()
    {
        var aReviewedAt = Epoch.AddDays(2);
        var bReviewedAt = Epoch.AddDays(3);

        var builtA = await BuildDivergentDatabaseAsync("alpha", aReviewedAt, ReviewRating.Good);
        var builtB = await BuildDivergentDatabaseAsync("beta", bReviewedAt, ReviewRating.Hard);
        await MigrateAsync(builtA);
        await MigrateAsync(builtB);

        await using var dbA = await IsolatedMergeTarget.FromBuiltFixtureAsync(builtA);
        await using var dbB = await IsolatedMergeTarget.FromBuiltFixtureAsync(builtB);

        // Archives captured strictly before any merge — used for both the initial merges and the later
        // no-change reimport, exactly mirroring what a real device would export once.
        var archiveA0 = await CaptureV2ArchiveBytesAsync(dbA.Connection);
        var archiveB0 = await CaptureV2ArchiveBytesAsync(dbB.Connection);

        var serviceOverA = new BackupService(dbA, new FakePlatformInfo());
        var serviceOverB = new BackupService(dbB, new FakePlatformInfo());

        // ---- A0 -> B: preview then real import ----
        PortableImportPreview previewAIntoB;
        using (var previewStream = new MemoryStream(archiveA0))
        {
            previewAIntoB = await serviceOverB.PreviewPortableImportAsync(previewStream, CancellationToken.None);
        }
        Assert.AreEqual(PortableImportPreviewDisposition.MergeChanges, previewAIntoB.Disposition);
        Assert.IsTrue(previewAIntoB.WillMutate);
        Assert.IsTrue(previewAIntoB.RequiresSafetyCopy);
        Assert.IsTrue(previewAIntoB.CanConfirm);

        PortableImportResult resultAIntoB;
        using (var importStream = new MemoryStream(archiveA0))
        {
            resultAIntoB = await serviceOverB.ImportPortableArchiveAsync(importStream, CancellationToken.None);
        }
        Assert.AreEqual(PortableImportStatus.Success, resultAIntoB.Status);
        Assert.AreEqual(PortableImportDisposition.MergeApplied, resultAIntoB.Summary!.Disposition);
        Assert.IsTrue(resultAIntoB.Summary.SafetyCopyCreated);

        // ---- 2. Preview/result agreement: aggregate counts must match exactly ----
        Assert.AreEqual(previewAIntoB.InsertedCount, resultAIntoB.Summary.InsertedCount);
        Assert.AreEqual(previewAIntoB.EnrichedCount, resultAIntoB.Summary.EnrichedCount);
        Assert.AreEqual(previewAIntoB.PreservedVariantCount, resultAIntoB.Summary.PreservedVariantCount);
        Assert.AreEqual(previewAIntoB.SkippedCount, resultAIntoB.Summary.SkippedCount);

        // ---- 3. Safety-copy evidence: exists, validates, and (word count) represents PRE-merge B ----
        var safetyCopyPathB1 = await AssertSingleValidSafetyCopyAsync(dbB.DatabasePath);
        await using (var readStream = new FileStream(safetyCopyPathB1, FileMode.Open, FileAccess.Read))
        {
            var validated = await BackupArchiveReader.ValidateVersionedAsync(readStream, CancellationToken.None);
            var vocabTerms = validated.V2!.Payload.Vocabulary.Select(v => v.CanonicalTerm).ToHashSet(StringComparer.Ordinal);
            Assert.Contains("onlybeta", vocabTerms, "The safety copy of B must contain B's own pre-merge data.");
            Assert.DoesNotContain("onlyalpha", vocabTerms, "The safety copy of B must represent B's state BEFORE A's data was merged in, not after.");
        }

        // ---- B0 -> A: preview then real import ----
        PortableImportPreview previewBIntoA;
        using (var previewStream = new MemoryStream(archiveB0))
        {
            previewBIntoA = await serviceOverA.PreviewPortableImportAsync(previewStream, CancellationToken.None);
        }
        Assert.AreEqual(PortableImportPreviewDisposition.MergeChanges, previewBIntoA.Disposition);
        Assert.IsTrue(previewBIntoA.RequiresSafetyCopy);

        PortableImportResult resultBIntoA;
        using (var importStream = new MemoryStream(archiveB0))
        {
            resultBIntoA = await serviceOverA.ImportPortableArchiveAsync(importStream, CancellationToken.None);
        }
        Assert.AreEqual(PortableImportStatus.Success, resultBIntoA.Status);
        Assert.AreEqual(PortableImportDisposition.MergeApplied, resultBIntoA.Summary!.Disposition);
        Assert.IsTrue(resultBIntoA.Summary.SafetyCopyCreated);
        Assert.AreEqual(previewBIntoA.InsertedCount, resultBIntoA.Summary.InsertedCount);
        Assert.AreEqual(previewBIntoA.EnrichedCount, resultBIntoA.Summary.EnrichedCount);
        Assert.AreEqual(previewBIntoA.PreservedVariantCount, resultBIntoA.Summary.PreservedVariantCount);
        Assert.AreEqual(previewBIntoA.SkippedCount, resultBIntoA.Summary.SkippedCount);

        var safetyCopyPathA1 = await AssertSingleValidSafetyCopyAsync(dbA.DatabasePath);
        await using (var readStream = new FileStream(safetyCopyPathA1, FileMode.Open, FileAccess.Read))
        {
            var validated = await BackupArchiveReader.ValidateVersionedAsync(readStream, CancellationToken.None);
            var vocabTerms = validated.V2!.Payload.Vocabulary.Select(v => v.CanonicalTerm).ToHashSet(StringComparer.Ordinal);
            Assert.Contains("onlyalpha", vocabTerms, "The safety copy of A must contain A's own pre-merge data.");
            Assert.DoesNotContain("onlybeta", vocabTerms, "The safety copy of A must represent A's state BEFORE B's data was merged in.");
        }

        // ---- 1. Semantic convergence: A and B now hold the same portable-data state ----
        var projectionA = Canonicalize(await CaptureCurrentPayloadAsync(dbA.Connection));
        var projectionB = Canonicalize(await CaptureCurrentPayloadAsync(dbB.Connection));
        AssertProjectionsConverge(projectionA, projectionB);

        // Target-only preserved, source-only added: both sides now hold all four words.
        Assert.HasCount(4, (await dbA.Connection.QueryScalarsAsync<string>("SELECT CanonicalTerm FROM Words")).Distinct());
        Assert.HasCount(4, (await dbB.Connection.QueryScalarsAsync<string>("SELECT CanonicalTerm FROM Words")).Distinct());

        // Shared data was not duplicated.
        Assert.AreEqual(1, await CountAsync(dbA.Connection, "SELECT COUNT(*) FROM Words WHERE CanonicalTerm = 'shared'"));
        Assert.AreEqual(1, await CountAsync(dbB.Connection, "SELECT COUNT(*) FROM Words WHERE CanonicalTerm = 'shared'"));

        // Multiple Senses remain independent on both sides.
        var splitWordIdA = await dbA.Connection.ExecuteScalarAsync<int>("SELECT Id FROM Words WHERE CanonicalTerm = 'split'");
        var splitSensesA = await dbA.Connection.QueryScalarsAsync<int>("SELECT Id FROM Senses WHERE WordId = ?", splitWordIdA);
        Assert.HasCount(2, splitSensesA.Distinct());
        var splitWordIdB = await dbB.Connection.ExecuteScalarAsync<int>("SELECT Id FROM Words WHERE CanonicalTerm = 'split'");
        var splitSensesB = await dbB.Connection.QueryScalarsAsync<int>("SELECT Id FROM Senses WHERE WordId = ?", splitWordIdB);
        Assert.HasCount(2, splitSensesB.Distinct());

        // Card schedules derived from the merged review history are equal on both sides.
        var expectedSharedSchedule = CardScheduleReplayer.Replay(
            CardSchedule.New(Epoch),
            [
                new CardScheduleReplayer.ReviewEvent(aReviewedAt, ReviewRating.Good, "event-a"),
                new CardScheduleReplayer.ReviewEvent(bReviewedAt, ReviewRating.Hard, "event-b")
            ],
            new SimpleSpacedRepetitionScheduler());

        var sharedCardA = await dbA.Connection.QueryAsync<Schema8CardRow>(
            "SELECT * FROM LearningCards WHERE WordId = (SELECT Id FROM Words WHERE CanonicalTerm = 'shared')");
        var sharedCardB = await dbB.Connection.QueryAsync<Schema8CardRow>(
            "SELECT * FROM LearningCards WHERE WordId = (SELECT Id FROM Words WHERE CanonicalTerm = 'shared')");
        Assert.HasCount(1, sharedCardA);
        Assert.HasCount(1, sharedCardB);
        AssertScheduleEquals(expectedSharedSchedule, sharedCardA[0]);
        AssertScheduleEquals(expectedSharedSchedule, sharedCardB[0]);
        Assert.AreEqual(2, await CountAsync(dbA.Connection, $"SELECT COUNT(*) FROM LearningReviews WHERE CardId = {sharedCardA[0].Id}"));
        Assert.AreEqual(2, await CountAsync(dbB.Connection, $"SELECT COUNT(*) FROM LearningReviews WHERE CardId = {sharedCardB[0].Id}"));

        // Relationship-integrity checks pass on both converged databases.
        await AssertReferentialIntegrityAsync(dbA.Connection);
        await AssertReferentialIntegrityAsync(dbB.Connection);

        // ==== 4. Reimport convergence: reimporting the original pre-merge archives is a stable no-op ====
        var wordCountBBeforeReimport = await CountAsync(dbB.Connection, "SELECT COUNT(*) FROM Words");
        var reviewCountBBeforeReimport = await CountAsync(dbB.Connection, "SELECT COUNT(*) FROM LearningReviews");
        var cardsBeforeReimportB = await dbB.Connection.QueryAsync<Schema8CardRow>("SELECT * FROM LearningCards ORDER BY Id");

        PortableImportPreview reimportPreviewAIntoB;
        using (var previewStream = new MemoryStream(archiveA0))
        {
            reimportPreviewAIntoB = await serviceOverB.PreviewPortableImportAsync(previewStream, CancellationToken.None);
        }
        Assert.AreEqual(PortableImportPreviewDisposition.MergeNoChange, reimportPreviewAIntoB.Disposition);
        Assert.IsFalse(reimportPreviewAIntoB.CanConfirm);
        Assert.IsFalse(reimportPreviewAIntoB.RequiresSafetyCopy);

        PortableImportResult reimportResultAIntoB;
        using (var importStream = new MemoryStream(archiveA0))
        {
            reimportResultAIntoB = await serviceOverB.ImportPortableArchiveAsync(importStream, CancellationToken.None);
        }
        Assert.AreEqual(PortableImportStatus.Success, reimportResultAIntoB.Status);
        Assert.AreEqual(PortableImportDisposition.MergeNoChange, reimportResultAIntoB.Summary!.Disposition);
        Assert.IsFalse(reimportResultAIntoB.Summary.SafetyCopyCreated);

        Assert.AreEqual(wordCountBBeforeReimport, await CountAsync(dbB.Connection, "SELECT COUNT(*) FROM Words"));
        Assert.AreEqual(reviewCountBBeforeReimport, await CountAsync(dbB.Connection, "SELECT COUNT(*) FROM LearningReviews"), "No duplicate review event may appear.");
        var cardsAfterReimportB = await dbB.Connection.QueryAsync<Schema8CardRow>("SELECT * FROM LearningCards ORDER BY Id");
        AssertCardListsEqual(cardsBeforeReimportB, cardsAfterReimportB);

        // The existing valid safety copy from the earlier mutating merge is untouched: no new copy, still valid.
        var safetyCopyFilesAfterReimportB = Directory.GetFiles(SafetyCopyDirectory(dbB.DatabasePath), "*.kfarchive");
        Assert.HasCount(1, safetyCopyFilesAfterReimportB);
        Assert.AreEqual(safetyCopyPathB1, safetyCopyFilesAfterReimportB[0]);
        await using (var readStream = new FileStream(safetyCopyPathB1, FileMode.Open, FileAccess.Read))
        {
            var validated = await BackupArchiveReader.ValidateVersionedAsync(readStream, CancellationToken.None);
            Assert.IsNotNull(validated.V2, "The pre-existing safety copy must remain valid and undamaged after a no-change reimport.");
        }

        // Mirror the same reimport proof for the other direction (B0 -> A).
        var wordCountABeforeReimport = await CountAsync(dbA.Connection, "SELECT COUNT(*) FROM Words");
        var reviewCountABeforeReimport = await CountAsync(dbA.Connection, "SELECT COUNT(*) FROM LearningReviews");

        PortableImportPreview reimportPreviewBIntoA;
        using (var previewStream = new MemoryStream(archiveB0))
        {
            reimportPreviewBIntoA = await serviceOverA.PreviewPortableImportAsync(previewStream, CancellationToken.None);
        }
        Assert.AreEqual(PortableImportPreviewDisposition.MergeNoChange, reimportPreviewBIntoA.Disposition);

        PortableImportResult reimportResultBIntoA;
        using (var importStream = new MemoryStream(archiveB0))
        {
            reimportResultBIntoA = await serviceOverA.ImportPortableArchiveAsync(importStream, CancellationToken.None);
        }
        Assert.AreEqual(PortableImportStatus.Success, reimportResultBIntoA.Status);
        Assert.AreEqual(PortableImportDisposition.MergeNoChange, reimportResultBIntoA.Summary!.Disposition);
        Assert.IsFalse(reimportResultBIntoA.Summary.SafetyCopyCreated);
        Assert.AreEqual(wordCountABeforeReimport, await CountAsync(dbA.Connection, "SELECT COUNT(*) FROM Words"));
        Assert.AreEqual(reviewCountABeforeReimport, await CountAsync(dbA.Connection, "SELECT COUNT(*) FROM LearningReviews"));

        var safetyCopyFilesAfterReimportA = Directory.GetFiles(SafetyCopyDirectory(dbA.DatabasePath), "*.kfarchive");
        Assert.HasCount(1, safetyCopyFilesAfterReimportA);
        Assert.AreEqual(safetyCopyPathA1, safetyCopyFilesAfterReimportA[0]);
    }

    private static void AssertScheduleEquals(CardSchedule expected, Schema8CardRow actual)
    {
        Assert.AreEqual(expected.State, actual.State);
        Assert.AreEqual(expected.DueAtUtc, actual.DueAtUtc);
        Assert.AreEqual(expected.IntervalDays, actual.IntervalDays);
        Assert.AreEqual(expected.EaseFactor, actual.EaseFactor);
        Assert.AreEqual(expected.SuccessfulReviewCount, actual.SuccessfulReviewCount);
        Assert.AreEqual(expected.LapseCount, actual.LapseCount);
        Assert.AreEqual(expected.LastReviewedAtUtc, actual.LastReviewedAtUtc);
        Assert.AreEqual(expected.LastRating, actual.LastRating);
    }

    private static void AssertCardListsEqual(IReadOnlyList<Schema8CardRow> before, IReadOnlyList<Schema8CardRow> after)
    {
        Assert.AreEqual(before.Count, after.Count);
        for (var i = 0; i < before.Count; i++)
        {
            Assert.AreEqual(before[i].Id, after[i].Id);
            Assert.AreEqual(before[i].State, after[i].State);
            Assert.AreEqual(before[i].DueAtUtc, after[i].DueAtUtc);
            Assert.AreEqual(before[i].IntervalDays, after[i].IntervalDays);
            Assert.AreEqual(before[i].EaseFactor, after[i].EaseFactor);
            Assert.AreEqual(before[i].SuccessfulReviewCount, after[i].SuccessfulReviewCount);
            Assert.AreEqual(before[i].LapseCount, after[i].LapseCount);
            Assert.AreEqual(before[i].LastReviewedAtUtc, after[i].LastReviewedAtUtc);
            Assert.AreEqual(before[i].LastRating, after[i].LastRating);
        }
    }

    // ==== 5. Archive-format-v1 populated-target convergence ====
    [TestMethod]
    public async Task ArchiveV1Source_PopulatedSchema8Target_UpgradesInMemoryAndConverges()
    {
        // A real archive-format-v1 source: a still-Schema-7 device with meaningful learning data.
        await using var v1Source = await Schema7Fixture.CreateAsync();
        var wordId = await v1Source.InsertWordAsync("legacyword", status: WordStatus.Learning, createdAt: Epoch, updatedAt: Epoch);
        var meaningId = await v1Source.InsertMeaningAsync(
            wordId, displayTerm: "legacyword", translation: "Altwort", selectedMeaningId: "legacy-1",
            definition: "a legacy word", createdAt: Epoch, updatedAt: Epoch);
        var cardId = await v1Source.InsertCardAsync(
            wordId, meaningId, CardDirection.MeaningToTerm, createdAtUtc: Epoch, updatedAtUtc: Epoch);
        var sessionId = await v1Source.InsertLearningSessionAsync(startedAtUtc: Epoch, updatedAtUtc: Epoch, completedAtUtc: Epoch);
        await v1Source.InsertReviewAsync(
            cardId, sessionId: sessionId, rating: ReviewRating.Good, wasTypedAnswer: true, wasCorrect: true,
            reviewedAtUtc: Epoch.AddDays(1), dueAtUtc: Epoch.AddDays(4), intervalDays: 3, easeFactor: 2.5);
        var archiveV1Bytes = await CaptureV1ArchiveBytesAsync(v1Source);

        var builtTarget = await BuildDivergentDatabaseAsync("gamma", Epoch.AddDays(1), ReviewRating.Good);
        await MigrateAsync(builtTarget);
        await using var target = await IsolatedMergeTarget.FromBuiltFixtureAsync(builtTarget);
        var wordCountBefore = await CountAsync(target.Connection, "SELECT COUNT(*) FROM Words");

        var service = new BackupService(target, new FakePlatformInfo());

        PortableImportPreview preview;
        using (var previewStream = new MemoryStream(archiveV1Bytes))
        {
            preview = await service.PreviewPortableImportAsync(previewStream, CancellationToken.None);
        }
        Assert.AreEqual(PortableImportPreviewDisposition.MergeChanges, preview.Disposition);
        Assert.IsTrue(preview.RequiresSafetyCopy);

        PortableImportResult result;
        using (var importStream = new MemoryStream(archiveV1Bytes))
        {
            result = await service.ImportPortableArchiveAsync(importStream, CancellationToken.None);
        }
        Assert.AreEqual(PortableImportStatus.Success, result.Status);
        Assert.AreEqual(PortableImportDisposition.MergeApplied, result.Summary!.Disposition);
        Assert.IsTrue(result.Summary.SafetyCopyCreated);

        Assert.AreEqual(wordCountBefore + 1, await CountAsync(target.Connection, "SELECT COUNT(*) FROM Words"));
        Assert.AreEqual(1, await CountAsync(target.Connection, "SELECT COUNT(*) FROM Words WHERE CanonicalTerm = 'legacyword'"));
        var legacySenseId = await target.Connection.ExecuteScalarAsync<int>(
            "SELECT SenseId FROM Meanings WHERE WordId = (SELECT Id FROM Words WHERE CanonicalTerm = 'legacyword')");
        Assert.IsGreaterThan(0, legacySenseId, "The upgraded v1 meaning must own a real Schema-8 Sense.");
        var legacyCard = (await target.Connection.QueryAsync<Schema8CardRow>(
            "SELECT * FROM LearningCards WHERE WordId = (SELECT Id FROM Words WHERE CanonicalTerm = 'legacyword')")).Single();
        Assert.AreEqual(legacySenseId, legacyCard.SenseId);
        Assert.AreEqual(1, await CountAsync(target.Connection, $"SELECT COUNT(*) FROM LearningReviews WHERE CardId = {legacyCard.Id}"));

        await AssertReferentialIntegrityAsync(target.Connection);

        // Schema is not downgraded: the target remains Schema 8.
        Assert.AreEqual(8, await target.Connection.ExecuteScalarAsync<int>("PRAGMA user_version"));

        // ---- Reimport is a no-op ----
        PortableImportPreview reimportPreview;
        using (var previewStream = new MemoryStream(archiveV1Bytes))
        {
            reimportPreview = await service.PreviewPortableImportAsync(previewStream, CancellationToken.None);
        }
        Assert.AreEqual(PortableImportPreviewDisposition.MergeNoChange, reimportPreview.Disposition);

        PortableImportResult reimportResult;
        using (var importStream = new MemoryStream(archiveV1Bytes))
        {
            reimportResult = await service.ImportPortableArchiveAsync(importStream, CancellationToken.None);
        }
        Assert.AreEqual(PortableImportStatus.Success, reimportResult.Status);
        Assert.AreEqual(PortableImportDisposition.MergeNoChange, reimportResult.Summary!.Disposition);
        Assert.AreEqual(wordCountBefore + 1, await CountAsync(target.Connection, "SELECT COUNT(*) FROM Words"));
    }

    // ==== 6. Real failure after safety-copy success, during the writer transaction ====
    [TestMethod]
    public async Task RealFailure_AfterSafetyCopySucceeds_DuringWriterTransaction_RollsBackAndRetainsSafetyCopy()
    {
        await using var sourceFixture = await Schema7Fixture.CreateAsync();
        var sourceWordId = await sourceFixture.InsertWordAsync("bank", status: WordStatus.Learning, createdAt: Epoch, updatedAt: Epoch);
        var sourceMeaningId = await sourceFixture.InsertMeaningAsync(
            sourceWordId, displayTerm: "bank", translation: "Bank", selectedMeaningId: "finance-1",
            definition: "financial institution", createdAt: Epoch, updatedAt: Epoch);
        var sourceCardId = await sourceFixture.InsertCardAsync(
            sourceWordId, sourceMeaningId, CardDirection.MeaningToTerm,
            state: CardState.New, dueAtUtc: Epoch, intervalDays: 0,
            easeFactor: SimpleSpacedRepetitionScheduler.DefaultEaseFactor, createdAtUtc: Epoch, updatedAtUtc: Epoch);
        var sourceSessionId = await sourceFixture.InsertLearningSessionAsync(
            startedAtUtc: Epoch.AddDays(1), updatedAtUtc: Epoch.AddDays(1), completedAtUtc: Epoch.AddDays(1));
        await sourceFixture.InsertReviewAsync(
            sourceCardId, sessionId: sourceSessionId, rating: ReviewRating.Good, wasTypedAnswer: true, wasCorrect: true,
            reviewedAtUtc: Epoch.AddDays(1), dueAtUtc: Epoch.AddDays(4), intervalDays: 3, easeFactor: 2.5);
        await MigrateAsync(sourceFixture);
        var archiveBytes = await CaptureV2ArchiveBytesAsync(sourceFixture.Connection);

        // Target already has the SAME card with NO reviews — the archive's review event forces a real
        // scheduler-replay writer step, giving the injector a real checkpoint to fire at.
        var builtTarget = await Schema7Fixture.CreateAsync();
        var targetWordId = await builtTarget.InsertWordAsync("bank", status: WordStatus.Learning, createdAt: Epoch, updatedAt: Epoch);
        var targetMeaningId = await builtTarget.InsertMeaningAsync(
            targetWordId, displayTerm: "bank", translation: "Bank", selectedMeaningId: "finance-1",
            definition: "financial institution", createdAt: Epoch, updatedAt: Epoch);
        await builtTarget.InsertCardAsync(
            targetWordId, targetMeaningId, CardDirection.MeaningToTerm,
            state: CardState.New, dueAtUtc: Epoch, intervalDays: 0,
            easeFactor: SimpleSpacedRepetitionScheduler.DefaultEaseFactor, createdAtUtc: Epoch, updatedAtUtc: Epoch);
        await MigrateAsync(builtTarget);
        await using var target = await IsolatedMergeTarget.FromBuiltFixtureAsync(builtTarget);

        var beforeState = await PersistentDatabaseSnapshot.CaptureCompleteAsync(target.Connection);
        Assert.IsFalse(Directory.Exists(SafetyCopyDirectory(target.DatabasePath)), "Precondition: no safety copy yet.");

        var injector = new ThrowAtCheckpointInjector(DuringSchedulerReplayCheckpoint);
        var service = new BackupService(target, new FakePlatformInfo(), failureInjector: injector);

        PortableImportResult result;
        using (var importStream = new MemoryStream(archiveBytes))
        {
            result = await service.ImportPortableArchiveAsync(importStream, CancellationToken.None);
        }

        Assert.AreEqual(PortableImportStatus.Failed, result.Status);
        Assert.IsNotNull(result.ErrorCode);
        Assert.DoesNotContain("Injected failure", result.ErrorCode);
        Assert.IsNull(result.Summary);

        // The target is semantically and persistently identical to its pre-import state — full snapshot equality.
        var afterState = await PersistentDatabaseSnapshot.CaptureCompleteAsync(target.Connection);
        CollectionAssert.AreEqual(beforeState, afterState);
        Assert.AreEqual(0, await CountAsync(target.Connection, "SELECT COUNT(*) FROM LearningReviews"), "No partial review history may remain.");

        // The safety copy created before the injected failure remains present and valid.
        await AssertSingleValidSafetyCopyAsync(target.DatabasePath);
    }

    // ==== 7. Invalid/manipulated archive fails closed ====
    [TestMethod]
    public async Task ManipulatedArchive_FailsClosed_NoMutationNoSafetyCopyNoRawContentLeaked()
    {
        var builtSource = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        var validArchiveBytes = await CaptureV2ArchiveBytesAsync(builtSource.Connection);
        await builtSource.DisposeAsync();

        // Manipulate the archive: flip one payload byte deep inside the ZIP so the manifest checksum no
        // longer matches — a realistic tamper scenario, distinct from a structurally-corrupt non-ZIP blob.
        var manipulated = (byte[])validArchiveBytes.Clone();
        manipulated[^20] ^= 0xFF;

        var builtTarget = await Schema8BackupFixtureBuilders.CreateSchema8FixtureAsync();
        await using var target = await IsolatedMergeTarget.FromBuiltFixtureAsync(builtTarget);
        var wordCountBefore = await CountAsync(target.Connection, "SELECT COUNT(*) FROM Words");
        var beforeState = await PersistentDatabaseSnapshot.CaptureCompleteAsync(target.Connection);

        var service = new BackupService(target, new FakePlatformInfo());

        PortableImportPreview preview;
        using (var previewStream = new MemoryStream(manipulated))
        {
            preview = await service.PreviewPortableImportAsync(previewStream, CancellationToken.None);
        }
        Assert.AreEqual(PortableImportPreviewDisposition.ValidationFailed, preview.Disposition);
        Assert.IsNotNull(preview.ErrorCode);
        Assert.IsNull(preview.ArchiveSummary);

        PortableImportResult result;
        using (var importStream = new MemoryStream(manipulated))
        {
            result = await service.ImportPortableArchiveAsync(importStream, CancellationToken.None);
        }
        Assert.AreEqual(PortableImportStatus.ValidationFailed, result.Status);
        Assert.IsNotNull(result.ErrorCode);
        Assert.IsNull(result.Summary);
        Assert.DoesNotContain("bank", result.ErrorCode, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", result.ErrorCode, StringComparison.Ordinal);

        Assert.AreEqual(wordCountBefore, await CountAsync(target.Connection, "SELECT COUNT(*) FROM Words"));
        var afterState = await PersistentDatabaseSnapshot.CaptureCompleteAsync(target.Connection);
        CollectionAssert.AreEqual(beforeState, afterState);
        Assert.IsFalse(Directory.Exists(SafetyCopyDirectory(target.DatabasePath)), "An invalid archive must never create a safety-copy directory.");
    }

    // ==== Package C: two-installation exchange of divergent completed Schema-9 review histories ====
    //
    // A owns completed history H1, B owns H2. Both describe the same document, tie on every session-level
    // field the v2 mapper orders by, and differ only in which word each history recorded as Known. After
    // A -> B and B -> A both installations hold {H1, H2} — but each installation's *imported* history is its
    // newer row, so the two installations end up with the opposite local ReviewSession row ids. That is the
    // Package-C precondition arising naturally from convergence itself rather than from a synthetic fixture.
    //
    // Whole-archive byte equality is deliberately NOT asserted: Sense/Meaning/AnswerVariant StableId values
    // are Guid.NewGuid()-generated and therefore installation-random by design. Only the completed-review
    // subgraph, whose exported content is fully content-derived, can converge canonically.

    private const string PackageCReviewDocumentContent = "The bank is open near the river.";
    private static readonly DateTime PackageCReviewStartedAtUtc = new(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PackageCReviewCompletedAtUtc = new(2026, 5, 1, 9, 30, 0, DateTimeKind.Utc);

    /// <summary>
    /// One Schema-9 installation holding exactly one completed review history over the shared document.
    /// Word rows are identical on both sides (so no KnowledgeState conflict decision can block the merge);
    /// only the two histories' recorded candidate decisions differ, which is exactly what the Schema-9
    /// full-history identity distinguishes and exactly what the v2 mapper's session-level key cannot see.
    /// </summary>
    private static async Task<Schema7Fixture> BuildPackageCReviewInstallationAsync(bool bankDecidedKnown)
    {
        var fixture = await Schema7Fixture.CreateAsync();

        var documentId = await fixture.InsertDocumentAsync(
            title: "Review history document", content: PackageCReviewDocumentContent,
            wordCount: 7, importedAt: Epoch);
        var bankWordId = await fixture.InsertWordAsync(
            "bank", status: WordStatus.Known, createdAt: Epoch, updatedAt: Epoch);
        var riverWordId = await fixture.InsertWordAsync(
            "river", status: WordStatus.UnknownBacklog, createdAt: Epoch, updatedAt: Epoch);

        // Identical retained outcome counters on both sides: exactly one Known and one UnknownBacklog
        // decision either way, so the two histories are indistinguishable at session level.
        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO ReviewSessions
                (DocumentId, Status, TotalCandidates, ReviewedCount, KnownCount, UnknownCount, IgnoredCount,
                 DecisionSequence, StartedAt, CompletedAt)
            VALUES (?, ?, 2, 2, 1, 1, 0, 2, ?, ?)
            """,
            documentId, (int)ReviewSessionStatus.Completed,
            PackageCReviewStartedAtUtc, PackageCReviewCompletedAtUtc);
        var sessionId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");

        await InsertPackageCReviewCandidateAsync(
            fixture, sessionId, bankWordId, order: 0,
            status: bankDecidedKnown ? WordStatus.Known : WordStatus.UnknownBacklog);
        await InsertPackageCReviewCandidateAsync(
            fixture, sessionId, riverWordId, order: 1,
            status: bankDecidedKnown ? WordStatus.UnknownBacklog : WordStatus.Known);

        await Schema8BackupFixtureBuilders.MigrateAsync(fixture);
        await Schema9RawFixture.ReplaceLegacyIndexAsync(fixture.Connection);
        await fixture.Connection.ExecuteAsync("PRAGMA user_version = 9");

        BackupSchemaCapabilityResult? capability = null;
        await fixture.Connection.RunInTransactionAsync(connection => capability = BackupSchemaCapability.Resolve(connection));
        Assert.IsInstanceOfType<Schema9CapabilityResult>(
            capability,
            "Both Package-C installations must be genuinely Schema-9 shaped, so two completed review "
            + "histories for one document are representable at all.");

        return fixture;
    }

    private static Task InsertPackageCReviewCandidateAsync(
        Schema7Fixture fixture, int sessionId, int wordId, int order, WordStatus status) =>
        fixture.Connection.ExecuteAsync(
            """
            INSERT INTO ReviewCandidates
                (SessionId, WordId, "Order", Status, PreviousWordStatus, PreviousTotalOccurrenceCount,
                 PreviousDocumentCount, PreviousUpdatedAt, DecisionSequence, WasWordCreatedForSession, DecidedAt)
            VALUES (?, ?, ?, ?, ?, 0, 0, ?, ?, 0, ?)
            """,
            sessionId, wordId, order, (int)status, (int)WordStatus.Unreviewed,
            PackageCReviewStartedAtUtc, order + 1, PackageCReviewStartedAtUtc.AddMinutes(order + 1));

    private static async Task<byte[]> CaptureSchema9ArchiveBytesAsync(SQLiteAsyncConnection connection)
    {
        Schema8BackupSnapshot? snapshot = null;
        await connection.RunInTransactionAsync(conn => snapshot = Schema8BackupSnapshotRepository.CapturePortableSnapshot(conn));
        BackupSchemaCapabilityResult? capabilityResult = null;
        await connection.RunInTransactionAsync(conn => capabilityResult = BackupSchemaCapability.Resolve(conn));
        var capability = ((Schema9CapabilityResult)capabilityResult!).Capability;
        var payload = BackupModelMapperV2.MapToExternal(snapshot!);

        using var stream = new MemoryStream();
        await BackupArchiveWriterV2.WriteArchiveAsync(
            payload, new FakePlatformInfo(), capability, DateTime.UtcNow, stream, CancellationToken.None);
        return stream.ToArray();
    }

    /// <summary>
    /// The installation-independent part of a payload's completed-review subgraph: which archive-local
    /// workflow/item id binds to which document content, decision content, and ordinal position. Parent
    /// documents and vocabulary are addressed by their own content, never by a positional archive id, so
    /// this projection isolates the review-session ordering itself.
    /// </summary>
    private static List<string> CanonicalReviewSubgraphProjection(BackupPayloadV2 payload)
    {
        var documentContentById = payload.SourceMaterials.ToDictionary(d => d.Id, d => d.ContentSha256, StringComparer.Ordinal);
        var vocabularyKeyById = payload.Vocabulary.ToDictionary(
            v => v.Id, v => $"{v.Language}|{v.IdentityKey}", StringComparer.Ordinal);

        return payload.Workflows.VocabularyReviews
            .Select(workflow => string.Join(
                " | ",
                new[]
                {
                    workflow.Id,
                    documentContentById[workflow.SourceMaterialId],
                    workflow.Status.ToString(),
                    $"K{workflow.KnownCount}/U{workflow.UnknownCount}/I{workflow.IgnoredCount}",
                    $"{workflow.StartedAtUtc:O}..{workflow.CompletedAtUtc:O}"
                }.Concat(workflow.Items.Select(item =>
                    $"{item.Id}/{vocabularyKeyById[item.VocabularyId]}/{item.Order}/{item.Status}"))))
            .ToList();
    }

    private static async Task<List<string>> CaptureCanonicalReviewSubgraphAsync(SQLiteAsyncConnection connection) =>
        CanonicalReviewSubgraphProjection(await CaptureCurrentPayloadAsync(connection));

    [TestMethod]
    public async Task TwoInstallations_ExchangeDivergentCompletedReviewHistories_ConvergeAndExportIdenticalCanonicalReviewSubgraph()
    {
        var builtA = await BuildPackageCReviewInstallationAsync(bankDecidedKnown: true);
        var builtB = await BuildPackageCReviewInstallationAsync(bankDecidedKnown: false);

        await using var dbA = await IsolatedMergeTarget.FromBuiltFixtureAsync(builtA);
        await using var dbB = await IsolatedMergeTarget.FromBuiltFixtureAsync(builtB);

        var archiveA = await CaptureSchema9ArchiveBytesAsync(dbA.Connection);
        var archiveB = await CaptureSchema9ArchiveBytesAsync(dbB.Connection);

        var serviceOverA = new BackupService(dbA, new FakePlatformInfo());
        var serviceOverB = new BackupService(dbB, new FakePlatformInfo());

        // ---- A -> B ----
        PortableImportResult resultAIntoB;
        using (var importStream = new MemoryStream(archiveA))
        {
            resultAIntoB = await serviceOverB.ImportPortableArchiveAsync(importStream, CancellationToken.None);
        }
        Assert.AreEqual(PortableImportStatus.Success, resultAIntoB.Status, $"A -> B failed: '{resultAIntoB.ErrorCode}'.");
        Assert.AreEqual(PortableImportDisposition.MergeApplied, resultAIntoB.Summary!.Disposition);

        // ---- B -> A (B's pre-merge archive, exactly what a real device would have exported once) ----
        PortableImportResult resultBIntoA;
        using (var importStream = new MemoryStream(archiveB))
        {
            resultBIntoA = await serviceOverA.ImportPortableArchiveAsync(importStream, CancellationToken.None);
        }
        Assert.AreEqual(PortableImportStatus.Success, resultBIntoA.Status, $"B -> A failed: '{resultBIntoA.ErrorCode}'.");
        Assert.AreEqual(PortableImportDisposition.MergeApplied, resultBIntoA.Summary!.Disposition);

        // ---- Semantic convergence: one document, both completed histories, on both sides ----
        foreach (var installation in new[] { dbA, dbB })
        {
            Assert.AreEqual(1, await CountAsync(installation.Connection, "SELECT COUNT(*) FROM Documents"));
            Assert.AreEqual(
                2, await CountAsync(installation.Connection, "SELECT COUNT(*) FROM ReviewSessions"),
                "Both divergent completed histories must coexist after the exchange.");
            Assert.AreEqual(
                4, await CountAsync(installation.Connection, "SELECT COUNT(*) FROM ReviewCandidates"),
                "Each completed history keeps its own two candidate rows.");
            Assert.AreEqual(
                0,
                await CountAsync(installation.Connection, $"SELECT COUNT(*) FROM ReviewSessions WHERE Status <> {(int)ReviewSessionStatus.Completed}"),
                "No Active session may be produced by the exchange.");
            await AssertReferentialIntegrityAsync(installation.Connection);
        }

        // ---- The Package-C precondition really arose: the two installations hold the same two histories
        // under the opposite local row ids. ----
        var localOrderA = await ReviewCandidateStatusesByRowOrderAsync(dbA.Connection);
        var localOrderB = await ReviewCandidateStatusesByRowOrderAsync(dbB.Connection);
        Assert.AreNotEqual(
            string.Join(";", localOrderA), string.Join(";", localOrderB),
            "The exchange must leave the two installations with the opposite local ReviewSession row order; "
            + "otherwise this test would not exercise the Package-C boundary at all.");

        // ---- Canonical review-subgraph convergence ----
        Assert.AreEqual(
            string.Join(Environment.NewLine, await CaptureCanonicalReviewSubgraphAsync(dbA.Connection)),
            string.Join(Environment.NewLine, await CaptureCanonicalReviewSubgraphAsync(dbB.Connection)),
            "After converging, both installations must export the same archive-local vr-*/rc-* binding for "
            + "the same completed review history, independently of their local SQLite row ids.");
    }

    // ---- Package C idempotence: once the two installations have converged, a further exchange in both
    // directions must be a pure no-change on both sides, with every completed history preserved exactly once
    // and every candidate row still attached to its own parent session. ----
    [TestMethod]
    public async Task TwoInstallations_AfterConvergence_RepeatedExchange_IsNoChangeAndPreservesEveryCompletedHistory()
    {
        var builtA = await BuildPackageCReviewInstallationAsync(bankDecidedKnown: true);
        var builtB = await BuildPackageCReviewInstallationAsync(bankDecidedKnown: false);

        await using var dbA = await IsolatedMergeTarget.FromBuiltFixtureAsync(builtA);
        await using var dbB = await IsolatedMergeTarget.FromBuiltFixtureAsync(builtB);

        var serviceOverA = new BackupService(dbA, new FakePlatformInfo());
        var serviceOverB = new BackupService(dbB, new FakePlatformInfo());

        // ---- Converge first (the scenario the previous test establishes) ----
        var initialArchiveA = await CaptureSchema9ArchiveBytesAsync(dbA.Connection);
        var initialArchiveB = await CaptureSchema9ArchiveBytesAsync(dbB.Connection);
        await ImportExpectingAsync(serviceOverB, initialArchiveA, PortableImportDisposition.MergeApplied, "A -> B");
        await ImportExpectingAsync(serviceOverA, initialArchiveB, PortableImportDisposition.MergeApplied, "B -> A");

        var reviewStateA = await CompletedReviewHistoryStateAsync(dbA.Connection);
        var reviewStateB = await CompletedReviewHistoryStateAsync(dbB.Connection);
        Assert.HasCount(2, reviewStateA, "A must hold both completed histories after convergence.");
        CollectionAssert.AreEqual(
            reviewStateA, reviewStateB,
            "Both installations must hold the same two completed histories after convergence.");

        // ---- Repeat the exchange with freshly captured, post-convergence archives ----
        var convergedArchiveA = await CaptureSchema9ArchiveBytesAsync(dbA.Connection);
        var convergedArchiveB = await CaptureSchema9ArchiveBytesAsync(dbB.Connection);
        await ImportExpectingAsync(serviceOverB, convergedArchiveA, PortableImportDisposition.MergeNoChange, "A -> B (repeat)");
        await ImportExpectingAsync(serviceOverA, convergedArchiveB, PortableImportDisposition.MergeNoChange, "B -> A (repeat)");

        // ---- And once more, to prove the no-change result is stable rather than a one-off ----
        await ImportExpectingAsync(serviceOverB, convergedArchiveA, PortableImportDisposition.MergeNoChange, "A -> B (second repeat)");
        await ImportExpectingAsync(serviceOverA, convergedArchiveB, PortableImportDisposition.MergeNoChange, "B -> A (second repeat)");

        foreach (var installation in new[] { dbA, dbB })
        {
            Assert.AreEqual(
                2, await CountAsync(installation.Connection, "SELECT COUNT(*) FROM ReviewSessions"),
                "Repeated exchange must neither duplicate nor drop a completed review history.");
            Assert.AreEqual(
                4, await CountAsync(installation.Connection, "SELECT COUNT(*) FROM ReviewCandidates"),
                "Repeated exchange must neither duplicate nor drop a candidate row.");
            Assert.AreEqual(
                0,
                await CountAsync(installation.Connection, $"SELECT COUNT(*) FROM ReviewSessions WHERE Status <> {(int)ReviewSessionStatus.Completed}"),
                "Repeated exchange must never produce an Active session.");
            await AssertReferentialIntegrityAsync(installation.Connection);
        }

        CollectionAssert.AreEqual(
            reviewStateA, await CompletedReviewHistoryStateAsync(dbA.Connection),
            "A's completed review history must be unchanged by the repeated exchange.");
        CollectionAssert.AreEqual(
            reviewStateB, await CompletedReviewHistoryStateAsync(dbB.Connection),
            "B's completed review history must be unchanged by the repeated exchange.");

        Assert.AreEqual(
            string.Join(Environment.NewLine, await CaptureCanonicalReviewSubgraphAsync(dbA.Connection)),
            string.Join(Environment.NewLine, await CaptureCanonicalReviewSubgraphAsync(dbB.Connection)),
            "Both installations must still export the same canonical review subgraph after the repeated exchange.");
    }

    private static async Task ImportExpectingAsync(
        BackupService service, byte[] archiveBytes, PortableImportDisposition expected, string step)
    {
        using var importStream = new MemoryStream(archiveBytes);
        var result = await service.ImportPortableArchiveAsync(importStream, CancellationToken.None);
        Assert.AreEqual(PortableImportStatus.Success, result.Status, $"{step} failed: '{result.ErrorCode}'.");
        Assert.AreEqual(expected, result.Summary!.Disposition, $"{step} produced an unexpected disposition.");
    }

    /// <summary>
    /// Each completed review history reduced to content only — its retained outcome counters and the ordered
    /// decision content of the candidate rows attached to that specific parent session. Sorted by content, so
    /// the projection is comparable across installations whose local row ids differ, while still proving each
    /// candidate stayed with its own parent.
    /// </summary>
    private static async Task<List<string>> CompletedReviewHistoryStateAsync(SQLiteAsyncConnection connection)
    {
        var sessions = await connection.QueryAsync<KnownFirst.Data.Entities.ReviewSessionEntity>("SELECT * FROM ReviewSessions");
        var candidates = await connection.QueryAsync<KnownFirst.Data.Entities.ReviewCandidateEntity>("SELECT * FROM ReviewCandidates");
        var words = await connection.QueryAsync<KnownFirst.Data.Entities.WordEntity>("SELECT * FROM Words");
        var termByWordId = words.ToDictionary(word => word.Id, word => $"{word.Language}|{word.NormalizedTerm}");

        return sessions
            .Select(session =>
            {
                var ownCandidates = candidates
                    .Where(candidate => candidate.SessionId == session.Id)
                    .OrderBy(candidate => candidate.Order)
                    .Select(candidate => $"{candidate.Order}:{termByWordId[candidate.WordId]}:{candidate.Status}");
                return $"{session.Status}|K{session.KnownCount}/U{session.UnknownCount}/I{session.IgnoredCount}"
                    + $"|{session.StartedAt:O}..{session.CompletedAt:O}|{string.Join(",", ownCandidates)}";
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The per-session candidate decisions in raw local row order — the installation-local view the
    /// canonical export must NOT depend on.</summary>
    private static async Task<List<string>> ReviewCandidateStatusesByRowOrderAsync(SQLiteAsyncConnection connection)
    {
        var rows = await connection.QueryAsync<ReviewCandidateRowProjection>(
            """
            SELECT rc.SessionId AS SessionId, rc."Order" AS CandidateOrder, rc.Status AS Status
            FROM ReviewCandidates rc
            ORDER BY rc.SessionId, rc."Order"
            """);
        return rows.Select(row => $"{row.CandidateOrder}:{row.Status}").ToList();
    }

    private sealed class ReviewCandidateRowProjection
    {
        public int SessionId { get; set; }
        public int CandidateOrder { get; set; }
        public int Status { get; set; }
    }

    // ==== Package D (KF-BACKUP-003): two-installation exchange of completed preparation and learning
    // workflows whose parent rows tie on every field the current v2 mapper orders by.
    //
    // Package C closed the same class of gap for SourceMaterials and completed ReviewSessions. The
    // PreparationSessions and LearningSessions ordering keys still omit the emitted CompletedAtUtc and every
    // emitted child row, and end in no tie-break at all, so two histories that tie fall through to raw local
    // row order. After a bidirectional exchange each installation holds both histories under the opposite
    // local row ids, so the same pb-*/pi-*/ls-*/lq-* ids bind to different histories on the two sides and the
    // canonical payload stops being a pure function of exported content. ====

    private const string PackageDWorkflowDocumentContent = "The bank is open near the river.";
    private static readonly DateTime PackageDWorkflowStartedAtUtc = new(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PackageDWorkflowUpdatedAtUtc = new(2026, 5, 1, 9, 30, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task TwoInstallations_ExchangeTiedCompletedPreparationAndLearningWorkflows_ConvergeAndExportIdenticalCanonicalWorkflowSubgraph()
    {
        var builtA = await BuildPackageDWorkflowInstallationAsync(bankPrepared: true);
        var builtB = await BuildPackageDWorkflowInstallationAsync(bankPrepared: false);

        await using var dbA = await IsolatedMergeTarget.FromBuiltFixtureAsync(builtA);
        await using var dbB = await IsolatedMergeTarget.FromBuiltFixtureAsync(builtB);

        var archiveA = await CaptureSchema9ArchiveBytesAsync(dbA.Connection);
        var archiveB = await CaptureSchema9ArchiveBytesAsync(dbB.Connection);

        var serviceOverA = new BackupService(dbA, new FakePlatformInfo());
        var serviceOverB = new BackupService(dbB, new FakePlatformInfo());

        await PreviewAndImportPackageDAsync(serviceOverB, archiveA, "A -> B");
        await PreviewAndImportPackageDAsync(serviceOverA, archiveB, "B -> A");

        // ---- Convergence: both completed histories of each kind on both sides, exactly once ----
        foreach (var installation in new[] { dbA, dbB })
        {
            Assert.AreEqual(
                2, await CountAsync(installation.Connection, "SELECT COUNT(*) FROM PreparationSessions"),
                "Both divergent completed preparation histories must coexist after the exchange.");
            Assert.AreEqual(
                4, await CountAsync(installation.Connection, "SELECT COUNT(*) FROM PreparationCandidates"),
                "Each completed preparation history keeps its own two candidate rows.");
            Assert.AreEqual(
                2, await CountAsync(installation.Connection, "SELECT COUNT(*) FROM LearningSessions"),
                "Both divergent completed learning histories must coexist after the exchange.");
            Assert.AreEqual(
                2, await CountAsync(installation.Connection, "SELECT COUNT(*) FROM LearningSessionCards"),
                "Each completed learning history keeps its own queue item.");
            Assert.AreEqual(
                2, await CountAsync(installation.Connection, "SELECT COUNT(*) FROM LearningReviews"),
                "Each completed learning history keeps its own review event.");
            Assert.AreEqual(
                0,
                await CountAsync(
                    installation.Connection,
                    $"SELECT COUNT(*) FROM PreparationSessions WHERE Status <> {(int)PreparationSessionStatus.Completed}"),
                "No Active preparation session may be produced by the exchange.");
            Assert.AreEqual(
                0,
                await CountAsync(
                    installation.Connection,
                    $"SELECT COUNT(*) FROM LearningSessions WHERE Status <> {(int)LearningSessionStatus.Completed}"),
                "No Active learning session may be produced by the exchange.");
            await AssertReferentialIntegrityAsync(installation.Connection);
        }

        var workflowStateA = await CompletedWorkflowHistoryStateAsync(dbA.Connection);
        var workflowStateB = await CompletedWorkflowHistoryStateAsync(dbB.Connection);
        Assert.HasCount(4, workflowStateA, "Two preparation histories plus two learning histories.");
        CollectionAssert.AreEqual(
            workflowStateA, workflowStateB,
            "Both installations must hold the same completed workflow histories, with every candidate and "
            + "queue item still attached to its own semantic parent.");

        // ---- The ordering boundary really arose: the two installations hold the same two preparation
        // histories under the opposite local row order. ----
        var localOrderA = await PreparationCandidateStatusesByRowOrderAsync(dbA.Connection);
        var localOrderB = await PreparationCandidateStatusesByRowOrderAsync(dbB.Connection);
        Assert.AreNotEqual(
            string.Join(";", localOrderA), string.Join(";", localOrderB),
            "The exchange must leave the two installations with the opposite local PreparationSession row "
            + "order; otherwise this test would not exercise the ordering boundary at all.");

        // ---- Canonical workflow-subgraph convergence (never whole-archive bytes: Sense/Meaning/
        // AnswerVariant/Assignment StableId values are installation-random by design). ----
        Assert.AreEqual(
            string.Join(Environment.NewLine, await CaptureCanonicalWorkflowSubgraphAsync(dbA.Connection)),
            string.Join(Environment.NewLine, await CaptureCanonicalWorkflowSubgraphAsync(dbB.Connection)),
            "After converging, both installations must export the same archive-local pb-*/pi-*/ls-*/lq-* "
            + "binding for the same completed workflow history, independently of their local row ids.");

        // ---- One further exchange must be a pure no-change on both sides ----
        var convergedArchiveA = await CaptureSchema9ArchiveBytesAsync(dbA.Connection);
        var convergedArchiveB = await CaptureSchema9ArchiveBytesAsync(dbB.Connection);
        await ImportExpectingAsync(serviceOverB, convergedArchiveA, PortableImportDisposition.MergeNoChange, "A -> B (repeat)");
        await ImportExpectingAsync(serviceOverA, convergedArchiveB, PortableImportDisposition.MergeNoChange, "B -> A (repeat)");

        foreach (var installation in new[] { dbA, dbB })
        {
            Assert.AreEqual(2, await CountAsync(installation.Connection, "SELECT COUNT(*) FROM PreparationSessions"));
            Assert.AreEqual(4, await CountAsync(installation.Connection, "SELECT COUNT(*) FROM PreparationCandidates"));
            Assert.AreEqual(2, await CountAsync(installation.Connection, "SELECT COUNT(*) FROM LearningSessions"));
            Assert.AreEqual(2, await CountAsync(installation.Connection, "SELECT COUNT(*) FROM LearningSessionCards"));
            Assert.AreEqual(2, await CountAsync(installation.Connection, "SELECT COUNT(*) FROM LearningReviews"));
            await AssertReferentialIntegrityAsync(installation.Connection);
        }

        CollectionAssert.AreEqual(
            workflowStateA, await CompletedWorkflowHistoryStateAsync(dbA.Connection),
            "A's completed workflow history must be unchanged by the repeated exchange.");
        CollectionAssert.AreEqual(
            workflowStateB, await CompletedWorkflowHistoryStateAsync(dbB.Connection),
            "B's completed workflow history must be unchanged by the repeated exchange.");
    }

    private static async Task PreviewAndImportPackageDAsync(BackupService service, byte[] archiveBytes, string step)
    {
        PortableImportPreview preview;
        using (var previewStream = new MemoryStream(archiveBytes))
        {
            preview = await service.PreviewPortableImportAsync(previewStream, CancellationToken.None);
        }
        Assert.AreEqual(
            PortableImportPreviewDisposition.MergeChanges, preview.Disposition,
            $"{step} preview must report a mutating merge (error code '{preview.ErrorCode}').");

        PortableImportResult result;
        using (var importStream = new MemoryStream(archiveBytes))
        {
            result = await service.ImportPortableArchiveAsync(importStream, CancellationToken.None);
        }
        Assert.AreEqual(PortableImportStatus.Success, result.Status, $"{step} failed: '{result.ErrorCode}'.");
        Assert.AreEqual(PortableImportDisposition.MergeApplied, result.Summary!.Disposition);

        Assert.AreEqual(preview.InsertedCount, result.Summary.InsertedCount, $"{step} preview/result inserted count disagreement.");
        Assert.AreEqual(preview.EnrichedCount, result.Summary.EnrichedCount, $"{step} preview/result enriched count disagreement.");
        Assert.AreEqual(preview.PreservedVariantCount, result.Summary.PreservedVariantCount, $"{step} preview/result preserved count disagreement.");
        Assert.AreEqual(preview.SkippedCount, result.Summary.SkippedCount, $"{step} preview/result skipped count disagreement.");
    }

    /// <summary>
    /// One Schema-9 installation holding exactly one completed preparation history and one completed learning
    /// history. Every field the current v2 mapper orders those two collections by is identical on both
    /// installations; only the emitted CompletedAtUtc and the child content (which candidate succeeded, which
    /// card was queued) differ — exactly what the current keys cannot see.
    /// </summary>
    private static async Task<Schema7Fixture> BuildPackageDWorkflowInstallationAsync(bool bankPrepared)
    {
        var fixture = await Schema7Fixture.CreateAsync();

        _ = await fixture.InsertDocumentAsync(
            title: "Workflow history document", content: PackageDWorkflowDocumentContent,
            wordCount: 7, importedAt: Epoch);

        // Identical vocabulary/meaning/card content on both installations, so no KnowledgeState,
        // semantic-grouping or preferred-variant decision can block the merge.
        var bankWordId = await fixture.InsertWordAsync(
            "bank", status: WordStatus.Learning, createdAt: Epoch, updatedAt: Epoch);
        var riverWordId = await fixture.InsertWordAsync(
            "river", status: WordStatus.Learning, createdAt: Epoch, updatedAt: Epoch);

        var bankMeaningId = await fixture.InsertMeaningAsync(
            bankWordId, displayTerm: "bank", translation: "Bank", selectedMeaningId: "bank-1",
            definition: "a financial institution", createdAt: Epoch, updatedAt: Epoch);
        var riverMeaningId = await fixture.InsertMeaningAsync(
            riverWordId, displayTerm: "river", translation: "Fluss", selectedMeaningId: "river-1",
            definition: "a large natural stream", createdAt: Epoch, updatedAt: Epoch);

        var bankCardId = await fixture.InsertCardAsync(
            bankWordId, bankMeaningId, CardDirection.TermToMeaning,
            dueAtUtc: Epoch, createdAtUtc: Epoch, updatedAtUtc: Epoch);
        var riverCardId = await fixture.InsertCardAsync(
            riverWordId, riverMeaningId, CardDirection.MeaningToTerm,
            dueAtUtc: Epoch, createdAtUtc: Epoch, updatedAtUtc: Epoch);

        var completedAtUtc = bankPrepared
            ? PackageDWorkflowStartedAtUtc.AddMinutes(20)
            : PackageDWorkflowStartedAtUtc.AddMinutes(25);

        // Completed preparation history: identical Method/Status/TotalItems/CompletedItems/StartedAtUtc/
        // UpdatedAtUtc on both sides; only CompletedAtUtc and which candidate succeeded differ.
        await fixture.Connection.ExecuteAsync(
            """
            INSERT INTO PreparationSessions
                (Status, Method, TotalItems, CompletedItems, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc)
            VALUES (?, 0, 2, 2, ?, ?, ?)
            """,
            (int)PreparationSessionStatus.Completed,
            PackageDWorkflowStartedAtUtc, PackageDWorkflowUpdatedAtUtc, completedAtUtc);
        var preparationSessionId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");

        await InsertPackageDPreparationCandidateAsync(
            fixture, preparationSessionId, bankWordId, order: 0,
            status: bankPrepared ? PreparationCandidateStatus.Prepared : PreparationCandidateStatus.Failed);
        await InsertPackageDPreparationCandidateAsync(
            fixture, preparationSessionId, riverWordId, order: 1,
            status: bankPrepared ? PreparationCandidateStatus.Failed : PreparationCandidateStatus.Prepared);

        // Completed learning history: identical counters/timestamps on both sides; only CompletedAtUtc and
        // the queued card differ.
        var learningSessionId = await fixture.InsertLearningSessionAsync(
            startedAtUtc: PackageDWorkflowStartedAtUtc, updatedAtUtc: PackageDWorkflowUpdatedAtUtc,
            completedAtUtc: completedAtUtc);
        var queuedCardId = bankPrepared ? bankCardId : riverCardId;
        await fixture.InsertQueueItemAsync(
            sessionId: learningSessionId, cardId: queuedCardId, queueOrder: 0, isCompleted: true,
            rating: ReviewRating.Good, completedAtUtc: completedAtUtc);
        await fixture.InsertReviewAsync(
            queuedCardId, sessionId: learningSessionId, rating: ReviewRating.Good, wasTypedAnswer: true,
            wasCorrect: true, reviewedAtUtc: completedAtUtc, dueAtUtc: completedAtUtc.AddDays(1),
            intervalDays: 1, easeFactor: SimpleSpacedRepetitionScheduler.DefaultEaseFactor);

        await Schema8BackupFixtureBuilders.MigrateAsync(fixture);
        await Schema9RawFixture.ReplaceLegacyIndexAsync(fixture.Connection);
        await fixture.Connection.ExecuteAsync("PRAGMA user_version = 9");

        BackupSchemaCapabilityResult? capability = null;
        await fixture.Connection.RunInTransactionAsync(connection => capability = BackupSchemaCapability.Resolve(connection));
        Assert.IsInstanceOfType<Schema9CapabilityResult>(
            capability, "Both Package-D installations must be genuinely Schema-9 shaped.");

        return fixture;
    }

    private static Task InsertPackageDPreparationCandidateAsync(
        Schema7Fixture fixture, int sessionId, int wordId, int order, PreparationCandidateStatus status) =>
        fixture.Connection.ExecuteAsync(
            """
            INSERT INTO PreparationCandidates
                (SessionId, WordId, "Order", Status, ResultJson, SelectedMeaningIndex, LastErrorCode,
                 LookupAttemptCount, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, '', 0, ?, 1, ?)
            """,
            sessionId, wordId, order, (int)status,
            status == PreparationCandidateStatus.Failed ? "lookup-failed" : string.Empty,
            PackageDWorkflowUpdatedAtUtc);

    /// <summary>
    /// Every completed preparation and learning history reduced to content only — its own scalars plus the
    /// ordered child content attached to that specific parent row, with vocabulary and cards addressed by
    /// canonical content rather than by local row id. Sorted by content, so the projection is comparable
    /// across installations while still proving each child stayed with its own semantic parent.
    /// </summary>
    private static async Task<List<string>> CompletedWorkflowHistoryStateAsync(SQLiteAsyncConnection connection)
    {
        var words = await connection.QueryAsync<KnownFirst.Data.Entities.WordEntity>("SELECT * FROM Words");
        var termByWordId = words.ToDictionary(word => word.Id, word => $"{word.Language}|{word.NormalizedTerm}");

        var preparationSessions = await connection.QueryAsync<KnownFirst.Data.Entities.PreparationSessionEntity>(
            "SELECT * FROM PreparationSessions");
        var preparationCandidates = await connection.QueryAsync<KnownFirst.Data.Entities.PreparationCandidateEntity>(
            "SELECT * FROM PreparationCandidates");
        var learningSessions = await connection.QueryAsync<KnownFirst.Data.Entities.LearningSessionEntity>(
            "SELECT * FROM LearningSessions");
        var queueItems = await connection.QueryAsync<Schema8QueueRow>("SELECT * FROM LearningSessionCards");
        var cards = await connection.QueryAsync<Schema8CardRow>("SELECT * FROM LearningCards");
        var cardKeyByRowId = cards.ToDictionary(
            card => card.Id, card => $"{termByWordId[card.WordId]}|{card.Direction}");

        var preparationState = preparationSessions.Select(session =>
        {
            var ownCandidates = preparationCandidates
                .Where(candidate => candidate.SessionId == session.Id)
                .OrderBy(candidate => candidate.Order)
                .Select(candidate => $"{candidate.Order}:{termByWordId[candidate.WordId]}:{candidate.Status}");
            return $"prep|{session.Method}|{session.Status}|{session.TotalItems}/{session.CompletedItems}"
                + $"|{session.StartedAtUtc:O}..{session.CompletedAtUtc:O}|{string.Join(",", ownCandidates)}";
        });

        var learningState = learningSessions.Select(session =>
        {
            var ownItems = queueItems
                .Where(item => item.SessionId == session.Id)
                .OrderBy(item => item.QueueOrder)
                .Select(item => $"{item.QueueOrder}:{cardKeyByRowId[item.CardId]}:{item.Rating}:{item.IsCompleted}");
            return $"learn|{session.Status}|{session.TotalCards}/{session.CompletedCards}"
                + $"|{session.StartedAtUtc:O}..{session.CompletedAtUtc:O}|{string.Join(",", ownItems)}";
        });

        return preparationState
            .Concat(learningState)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The per-session preparation decisions in raw local row order — the installation-local view the
    /// canonical export must NOT depend on.</summary>
    private static async Task<List<string>> PreparationCandidateStatusesByRowOrderAsync(SQLiteAsyncConnection connection)
    {
        var rows = await connection.QueryAsync<PreparationCandidateRowProjection>(
            """
            SELECT pc.SessionId AS SessionId, pc."Order" AS CandidateOrder, pc.Status AS Status
            FROM PreparationCandidates pc
            ORDER BY pc.SessionId, pc."Order"
            """);
        return rows.Select(row => $"{row.CandidateOrder}:{row.Status}").ToList();
    }

    private sealed class PreparationCandidateRowProjection
    {
        public int SessionId { get; set; }
        public int CandidateOrder { get; set; }
        public int Status { get; set; }
    }

    private static async Task<List<string>> CaptureCanonicalWorkflowSubgraphAsync(SQLiteAsyncConnection connection) =>
        CanonicalWorkflowSubgraphProjection(await CaptureCurrentPayloadAsync(connection));

    /// <summary>
    /// The installation-independent part of a payload's completed preparation/learning subgraph: which
    /// archive-local workflow/item id binds to which content. Vocabulary and cards are addressed by their own
    /// content, never by a positional archive id, so this projection isolates the workflow ordering itself.
    /// </summary>
    private static List<string> CanonicalWorkflowSubgraphProjection(BackupPayloadV2 payload)
    {
        var vocabularyKeyById = payload.Vocabulary.ToDictionary(
            item => item.Id, item => $"{item.Language}|{item.IdentityKey}", StringComparer.Ordinal);
        var cardKeyById = payload.Learning.Cards.ToDictionary(
            card => card.Id, card => $"{vocabularyKeyById[card.VocabularyId]}|{card.Direction}", StringComparer.Ordinal);

        var preparation = payload.Workflows.PreparationBatches
            .Select(workflow => string.Join(
                " | ",
                new[]
                {
                    "prep",
                    workflow.Id,
                    workflow.Status.ToString(),
                    workflow.Method.ToString(),
                    $"{workflow.StartedAtUtc:O}..{workflow.CompletedAtUtc:O}"
                }.Concat(workflow.Items.Select(item =>
                    $"{item.Id}/{vocabularyKeyById[item.VocabularyId]}/{item.Order}/{item.Status}"))));

        var learning = payload.Workflows.LearningSessions
            .Select(workflow => string.Join(
                " | ",
                new[]
                {
                    "learn",
                    workflow.Id,
                    workflow.Status.ToString(),
                    $"{workflow.StartedAtUtc:O}..{workflow.CompletedAtUtc:O}"
                }.Concat(workflow.QueueItems.Select(item =>
                    $"{item.Id}/{cardKeyById[item.CardId]}/{item.QueueOrder}/{item.Rating}"))));

        // Review events are compared as a content-sorted set, never in emitted order. The review ordering is
        // keyed first by the emitted c-* reference, and the Cards collection is ordered by Sense StableId —
        // a Guid.NewGuid() value the Schema-8 migration generates per installation and which Package C
        // explicitly records as installation-random by design. Package D makes the review ordering total
        // over emitted content (proved at mapper level by
        // CreateBackupV2_LearningReviewsTiedOnSortKeyButDifferingInSessionOrAnswerVariant_...); it does not,
        // and must not, make the Cards collection's own ordering installation-independent. What this
        // projection does assert is that both sides bind the same ls-* workflow and the same card content to
        // the same review event.
        var reviews = payload.Learning.ReviewEvents
            .Select(review => string.Join(
                " | ",
                "review",
                cardKeyById[review.CardId],
                review.LearningSessionId,
                review.Rating.ToString(),
                $"{review.ReviewedAtUtc:O}"))
            .OrderBy(value => value, StringComparer.Ordinal);

        return [.. preparation, .. learning, .. reviews];
    }

    // ==== Schema-9 LearningReview merge integrity ====

    private static readonly DateTime ReviewVariantReviewedAtUtc = new(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);
    private const string ReviewVariantAlphaText = "alpha-answer";
    private const string ReviewVariantBetaText = "beta-answer";

    [TestMethod]
    public async Task SameInstantVariantDistinctLearningReviews_ExchangeConvergesWithoutMissingOrDuplicateEvents()
    {
        // Both installations hold identical vocabulary, sense, card and answer-variant content, and one
        // completed learning session whose single review ties on every field the meaning-aware fingerprint
        // considered — same instant, rating, outcome and scheduling snapshot. They differ only in which
        // answer variant the review exercised, and they store the two equivalent variants under opposite
        // local row ids, so only stable variant identity can decide the match.
        var builtA = await BuildReviewVariantInstallationAsync(targetsAlpha: true);
        var builtB = await BuildReviewVariantInstallationAsync(targetsAlpha: false);

        await using var dbA = await IsolatedMergeTarget.FromBuiltFixtureAsync(builtA);
        await using var dbB = await IsolatedMergeTarget.FromBuiltFixtureAsync(builtB);

        var localVariantOrderA = await VariantTextsByRowOrderAsync(dbA.Connection);
        var localVariantOrderB = await VariantTextsByRowOrderAsync(dbB.Connection);
        Assert.AreNotEqual(
            string.Join(";", localVariantOrderA), string.Join(";", localVariantOrderB),
            "The two installations must hold the equivalent answer variants under the opposite local row "
            + "order; otherwise this test would not exercise the stable-identity boundary at all.");

        var archiveA = await CaptureSchema9ArchiveBytesAsync(dbA.Connection);
        var archiveB = await CaptureSchema9ArchiveBytesAsync(dbB.Connection);

        var serviceOverA = new BackupService(dbA, new FakePlatformInfo());
        var serviceOverB = new BackupService(dbB, new FakePlatformInfo());

        await PreviewAndImportPackageDAsync(serviceOverB, archiveA, "A -> B");
        await PreviewAndImportPackageDAsync(serviceOverA, archiveB, "B -> A");

        foreach (var installation in new[] { dbA, dbB })
        {
            Assert.AreEqual(
                2, await CountAsync(installation.Connection, "SELECT COUNT(*) FROM LearningReviews"),
                "Two reviews exercising different answer variants are distinct events: neither may be lost "
                + "and neither may be duplicated.");
            Assert.AreEqual(
                1, await CountAsync(installation.Connection, "SELECT COUNT(*) FROM LearningSessions"),
                "The two identical completed sessions must match by identity rather than duplicate.");
            Assert.AreEqual(
                0,
                await CountAsync(
                    installation.Connection,
                    "SELECT COUNT(*) FROM LearningReviews r LEFT JOIN LearningSessions s ON s.Id = r.SessionId WHERE s.Id IS NULL"),
                "Every retained review must stay attached to a real LearningSession row.");
            await AssertReferentialIntegrityAsync(installation.Connection);
        }

        var reviewStateA = await ReviewVariantAttachmentStateAsync(dbA.Connection);
        var reviewStateB = await ReviewVariantAttachmentStateAsync(dbB.Connection);
        CollectionAssert.AreEqual(
            new List<string> { $"{ReviewVariantAlphaText}|{ReviewVariantAlphaText}", $"{ReviewVariantBetaText}|{ReviewVariantBetaText}" },
            reviewStateA,
            "A must end up with exactly one review per answer variant, each keeping its own target/matched "
            + "variant attachment.");
        CollectionAssert.AreEqual(
            reviewStateA, reviewStateB,
            "Both installations must converge on the same review/answer-variant attachment.");

        // One further exchange must be a pure no-change on both sides.
        var convergedArchiveA = await CaptureSchema9ArchiveBytesAsync(dbA.Connection);
        var convergedArchiveB = await CaptureSchema9ArchiveBytesAsync(dbB.Connection);
        await ImportExpectingAsync(serviceOverB, convergedArchiveA, PortableImportDisposition.MergeNoChange, "A -> B (repeat)");
        await ImportExpectingAsync(serviceOverA, convergedArchiveB, PortableImportDisposition.MergeNoChange, "B -> A (repeat)");

        foreach (var installation in new[] { dbA, dbB })
        {
            Assert.AreEqual(2, await CountAsync(installation.Connection, "SELECT COUNT(*) FROM LearningReviews"));
            Assert.AreEqual(1, await CountAsync(installation.Connection, "SELECT COUNT(*) FROM LearningSessions"));
            await AssertReferentialIntegrityAsync(installation.Connection);
        }

        CollectionAssert.AreEqual(reviewStateA, await ReviewVariantAttachmentStateAsync(dbA.Connection));
        CollectionAssert.AreEqual(reviewStateB, await ReviewVariantAttachmentStateAsync(dbB.Connection));
    }

    /// <summary>
    /// One Schema-9 installation with a single card carrying two extra assigned answer variants and exactly
    /// one completed learning history. The two installations differ only in which variant the review
    /// exercised and in the local row order of the two equivalent variants.
    /// </summary>
    private static async Task<Schema7Fixture> BuildReviewVariantInstallationAsync(bool targetsAlpha)
    {
        var fixture = await Schema7Fixture.CreateAsync();

        var wordId = await fixture.InsertWordAsync("bank", status: WordStatus.Learning, createdAt: Epoch, updatedAt: Epoch);
        var meaningId = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "bank", translation: "Bank", selectedMeaningId: "bank-1",
            definition: "a financial institution", createdAt: Epoch, updatedAt: Epoch);
        var cardId = await fixture.InsertCardAsync(
            wordId, meaningId, CardDirection.TermToMeaning,
            dueAtUtc: Epoch, createdAtUtc: Epoch, updatedAtUtc: Epoch);

        var learningSessionId = await fixture.InsertLearningSessionAsync(
            startedAtUtc: ReviewVariantReviewedAtUtc, updatedAtUtc: ReviewVariantReviewedAtUtc,
            completedAtUtc: ReviewVariantReviewedAtUtc);
        await fixture.InsertReviewAsync(
            cardId, sessionId: learningSessionId, rating: ReviewRating.Good, wasTypedAnswer: true,
            wasCorrect: true, reviewedAtUtc: ReviewVariantReviewedAtUtc,
            dueAtUtc: ReviewVariantReviewedAtUtc.AddDays(1), intervalDays: 1,
            easeFactor: SimpleSpacedRepetitionScheduler.DefaultEaseFactor);

        await Schema8BackupFixtureBuilders.MigrateAsync(fixture);
        await Schema9RawFixture.ReplaceLegacyIndexAsync(fixture.Connection);
        await fixture.Connection.ExecuteAsync("PRAGMA user_version = 9");

        // The two extra variants are inserted in the opposite order on the two installations, so equivalent
        // answer content lands on opposite local row ids.
        var senseId = await fixture.Connection.ExecuteScalarAsync<int>("SELECT Id FROM Senses LIMIT 1");
        var texts = targetsAlpha
            ? new[] { ReviewVariantAlphaText, ReviewVariantBetaText }
            : [ReviewVariantBetaText, ReviewVariantAlphaText];

        var variantIdByText = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var text in texts)
        {
            var variantId = await fixture.InsertAnswerVariantAsync(
                senseId, displayText: text, answerLanguage: "de", createdAtUtc: Epoch);
            await fixture.InsertAssignmentAsync(
                senseId, CardDirection.TermToMeaning, variantId,
                Data.Migrations.Schema8.AnswerVariantRequirement.AcceptedOnly,
                isPreferred: false, requiredSinceUtc: null, createdAtUtc: Epoch);
            variantIdByText[text] = variantId;
        }

        var exercisedVariantId = variantIdByText[targetsAlpha ? ReviewVariantAlphaText : ReviewVariantBetaText];
        await fixture.Connection.ExecuteAsync(
            "UPDATE LearningReviews SET TargetAnswerVariantId = ?, MatchedAnswerVariantId = ?",
            exercisedVariantId, exercisedVariantId);

        BackupSchemaCapabilityResult? capability = null;
        await fixture.Connection.RunInTransactionAsync(connection => capability = BackupSchemaCapability.Resolve(connection));
        Assert.IsInstanceOfType<Schema9CapabilityResult>(
            capability, "Both installations must be genuinely Schema-9 shaped.");

        return fixture;
    }

    private static async Task<List<string>> VariantTextsByRowOrderAsync(SQLiteAsyncConnection connection) =>
        [.. (await connection.QueryAsync<TextRow>("SELECT NormalizedText AS Value FROM AnswerVariants ORDER BY Id")).Select(row => row.Value)];

    /// <summary>Each review reduced to the normalized answer text of its target and matched variant, sorted
    /// by content so the projection is comparable across installations with different local ids.</summary>
    private static async Task<List<string>> ReviewVariantAttachmentStateAsync(SQLiteAsyncConnection connection) =>
        [.. (await connection.QueryAsync<TextRow>(
            """
            SELECT COALESCE(t.NormalizedText, '<none>') || '|' || COALESCE(m.NormalizedText, '<none>') AS Value
            FROM LearningReviews r
            LEFT JOIN AnswerVariants t ON t.Id = r.TargetAnswerVariantId
            LEFT JOIN AnswerVariants m ON m.Id = r.MatchedAnswerVariantId
            """)).Select(row => row.Value).OrderBy(value => value, StringComparer.Ordinal)];

    private sealed class TextRow
    {
        public string Value { get; set; } = string.Empty;
    }
}
