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
}
