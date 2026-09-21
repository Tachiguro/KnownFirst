using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Services;
using KnownFirst.Tests.AnalysisEvidence;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
public sealed class ReviewAdmissionCorrelationTests
{
    private AdmissionTestDatabase _database = null!;
    private TextReviewService _service = null!;
    private TextAnalyzer _analyzer = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _database = new AdmissionTestDatabase();
        await _database.InitializeAsync();
        _analyzer = new TextAnalyzer();
        _service = new TextReviewService(
            _database,
            _analyzer,
            new DisabledEnhancedRecognitionSettings(),
            new ThrowingGermanLexicon());
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _database.DisposeAsync();
    }

    [TestMethod]
    public async Task CorrelateAsync_NewWord_AdmitsNewWord()
    {
        var request = new ImportTextRequest("Test doc", "Alpha.", "en", "de");
        var evidence = await ReviewAdmissionCorrelator.CorrelateAsync(_database, _service, request, _analyzer);

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, evidence.ImportResult.Outcome);
        Assert.IsTrue(evidence.HasAcceptedReviewSession);
        Assert.AreEqual(1, evidence.CandidateAdmissions.Count);

        var candidate = evidence.CandidateAdmissions[0];
        Assert.AreEqual("Alpha", candidate.CanonicalTerm);
        Assert.IsNull(candidate.PreImportStatus);
        Assert.AreEqual(ReviewAdmissionDisposition.AdmittedNewWord, candidate.Disposition);
        Assert.AreEqual(0, candidate.PersistedReviewOrder);
        Assert.IsTrue(candidate.WasWordCreatedForSession);
        Assert.AreEqual(0, candidate.TotalOccurrenceCountBefore);
        Assert.AreEqual(1, candidate.TotalOccurrenceCountAfter);
        Assert.AreEqual(0, candidate.DocumentCountBefore);
        Assert.AreEqual(1, candidate.DocumentCountAfter);
        Assert.AreEqual(1, candidate.OccurrenceContributionCount);

        Assert.AreEqual(1, evidence.PersistedReviewCandidates.Count);
        Assert.AreEqual("Alpha", evidence.PersistedReviewCandidates[0].CanonicalTerm);
        Assert.AreEqual(0, evidence.PersistedReviewCandidates[0].Order);
        Assert.AreEqual(1, evidence.PersistedReviewCandidates[0].OccurrenceCount);
        Assert.IsTrue(evidence.PersistedReviewCandidates[0].WasWordCreatedForSession);
    }

    [TestMethod]
    public async Task CorrelateAsync_ExistingUnreviewed_AdmitsExistingUnreviewed()
    {
        await SeedWordAsync("Alpha", "W:alpha", WordStatus.Unreviewed, occurrences: 2, docCount: 1);

        var request = new ImportTextRequest("Test doc", "Alpha.", "en", "de");
        var evidence = await ReviewAdmissionCorrelator.CorrelateAsync(_database, _service, request, _analyzer);

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, evidence.ImportResult.Outcome);
        Assert.IsTrue(evidence.HasAcceptedReviewSession);
        Assert.AreEqual(1, evidence.PreImportVocabulary.Count);
        Assert.AreEqual(WordStatus.Unreviewed, evidence.PreImportVocabulary[0].Status);

        Assert.AreEqual(1, evidence.CandidateAdmissions.Count);
        var candidate = evidence.CandidateAdmissions[0];
        Assert.AreEqual(WordStatus.Unreviewed, candidate.PreImportStatus);
        Assert.AreEqual(ReviewAdmissionDisposition.AdmittedExistingUnreviewed, candidate.Disposition);
        Assert.AreEqual(0, candidate.PersistedReviewOrder);
        Assert.IsFalse(candidate.WasWordCreatedForSession);
        Assert.AreEqual(2, candidate.TotalOccurrenceCountBefore);
        Assert.AreEqual(3, candidate.TotalOccurrenceCountAfter);
        Assert.AreEqual(1, candidate.DocumentCountBefore);
        Assert.AreEqual(2, candidate.DocumentCountAfter);
        Assert.AreEqual(1, candidate.OccurrenceContributionCount);

        Assert.AreEqual(1, evidence.PersistedReviewCandidates.Count);
        Assert.IsFalse(evidence.PersistedReviewCandidates[0].WasWordCreatedForSession);
    }

    [TestMethod]
    public async Task CorrelateAsync_AcceptedMixedImport_WithUnknownBacklog_ExcludesBacklogFromReviewAndRecordsContribution()
    {
        await SeedWordAsync("Backlogword", "W:backlogword", WordStatus.UnknownBacklog, occurrences: 4, docCount: 2);

        var request = new ImportTextRequest("Mixed doc", "Backlogword Newword.", "en", "de");
        var evidence = await ReviewAdmissionCorrelator.CorrelateAsync(_database, _service, request, _analyzer);

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, evidence.ImportResult.Outcome);
        Assert.IsTrue(evidence.HasAcceptedReviewSession);
        Assert.AreEqual(2, evidence.CandidateAdmissions.Count);

        var backlog = evidence.CandidateAdmissions.Single(c => c.CanonicalTerm == "Backlogword");
        Assert.AreEqual(WordStatus.UnknownBacklog, backlog.PreImportStatus);
        Assert.AreEqual(ReviewAdmissionDisposition.NotAdmittedExistingUnknownBacklog, backlog.Disposition);
        Assert.IsNull(backlog.PersistedReviewOrder);
        Assert.IsFalse(backlog.WasWordCreatedForSession);
        Assert.AreEqual(4, backlog.TotalOccurrenceCountBefore);
        Assert.AreEqual(5, backlog.TotalOccurrenceCountAfter);
        Assert.AreEqual(2, backlog.DocumentCountBefore);
        Assert.AreEqual(3, backlog.DocumentCountAfter);
        Assert.AreEqual(1, backlog.OccurrenceContributionCount);

        var newWord = evidence.CandidateAdmissions.Single(c => c.CanonicalTerm == "Newword");
        Assert.IsNull(newWord.PreImportStatus);
        Assert.AreEqual(ReviewAdmissionDisposition.AdmittedNewWord, newWord.Disposition);
        Assert.AreEqual(0, newWord.PersistedReviewOrder);
        Assert.IsTrue(newWord.WasWordCreatedForSession);
        Assert.AreEqual(1, newWord.OccurrenceContributionCount);

        Assert.AreEqual(1, evidence.PersistedReviewCandidates.Count);
        Assert.AreEqual("Newword", evidence.PersistedReviewCandidates[0].CanonicalTerm);

        var docOccurrences = await _database.ReadAsync(conn =>
            conn.Table<WordOccurrenceEntity>()
                .Where(o => o.DocumentId == evidence.ImportResult.DocumentId)
                .ToListAsync());
        Assert.AreEqual(2, docOccurrences.Count);
        Assert.IsTrue(docOccurrences.Any(o => o.WordId == backlog.PersistedWordId));
        Assert.IsTrue(docOccurrences.Any(o => o.WordId == newWord.PersistedWordId));
    }

    [TestMethod]
    public async Task CorrelateAsync_AcceptedMixedImport_WithKnown_ExcludesKnownFromReviewAndDoesNotRecordContribution()
    {
        await SeedWordAsync("Knownword", "W:knownword", WordStatus.Known, occurrences: 3, docCount: 1);

        var request = new ImportTextRequest("Mixed doc", "Knownword Newword.", "en", "de");
        var evidence = await ReviewAdmissionCorrelator.CorrelateAsync(_database, _service, request, _analyzer);

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, evidence.ImportResult.Outcome);
        Assert.AreEqual(2, evidence.CandidateAdmissions.Count);

        var known = evidence.CandidateAdmissions.Single(c => c.CanonicalTerm == "Knownword");
        Assert.AreEqual(WordStatus.Known, known.PreImportStatus);
        Assert.AreEqual(ReviewAdmissionDisposition.NotAdmittedExistingKnown, known.Disposition);
        Assert.IsNull(known.PersistedReviewOrder);
        Assert.IsFalse(known.WasWordCreatedForSession);
        Assert.AreEqual(3, known.TotalOccurrenceCountBefore);
        Assert.AreEqual(3, known.TotalOccurrenceCountAfter);
        Assert.AreEqual(1, known.DocumentCountBefore);
        Assert.AreEqual(1, known.DocumentCountAfter);
        Assert.AreEqual(0, known.OccurrenceContributionCount);

        var newWord = evidence.CandidateAdmissions.Single(c => c.CanonicalTerm == "Newword");
        Assert.AreEqual(ReviewAdmissionDisposition.AdmittedNewWord, newWord.Disposition);

        Assert.AreEqual(1, evidence.PersistedReviewCandidates.Count);
        Assert.AreEqual("Newword", evidence.PersistedReviewCandidates[0].CanonicalTerm);

        var docOccurrences = await _database.ReadAsync(conn =>
            conn.Table<WordOccurrenceEntity>()
                .Where(o => o.DocumentId == evidence.ImportResult.DocumentId)
                .ToListAsync());
        Assert.AreEqual(1, docOccurrences.Count);
        Assert.AreEqual(newWord.PersistedWordId, docOccurrences[0].WordId);
    }

    [TestMethod]
    public async Task CorrelateAsync_AcceptedMixedImport_WithIgnored_ExcludesIgnoredFromReviewAndDoesNotRecordContribution()
    {
        await SeedWordAsync("Ignoredword", "W:ignoredword", WordStatus.Ignored, occurrences: 2, docCount: 1);

        var request = new ImportTextRequest("Mixed doc", "Ignoredword Newword.", "en", "de");
        var evidence = await ReviewAdmissionCorrelator.CorrelateAsync(_database, _service, request, _analyzer);

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, evidence.ImportResult.Outcome);
        Assert.AreEqual(2, evidence.CandidateAdmissions.Count);

        var ignored = evidence.CandidateAdmissions.Single(c => c.CanonicalTerm == "Ignoredword");
        Assert.AreEqual(WordStatus.Ignored, ignored.PreImportStatus);
        Assert.AreEqual(ReviewAdmissionDisposition.NotAdmittedExistingIgnored, ignored.Disposition);
        Assert.IsNull(ignored.PersistedReviewOrder);
        Assert.IsFalse(ignored.WasWordCreatedForSession);
        Assert.AreEqual(2, ignored.TotalOccurrenceCountBefore);
        Assert.AreEqual(2, ignored.TotalOccurrenceCountAfter);
        Assert.AreEqual(1, ignored.DocumentCountBefore);
        Assert.AreEqual(1, ignored.DocumentCountAfter);
        Assert.AreEqual(0, ignored.OccurrenceContributionCount);

        var newWord = evidence.CandidateAdmissions.Single(c => c.CanonicalTerm == "Newword");
        Assert.AreEqual(ReviewAdmissionDisposition.AdmittedNewWord, newWord.Disposition);

        Assert.AreEqual(1, evidence.PersistedReviewCandidates.Count);
        Assert.AreEqual("Newword", evidence.PersistedReviewCandidates[0].CanonicalTerm);

        var docOccurrences = await _database.ReadAsync(conn =>
            conn.Table<WordOccurrenceEntity>()
                .Where(o => o.DocumentId == evidence.ImportResult.DocumentId)
                .ToListAsync());
        Assert.AreEqual(1, docOccurrences.Count);
        Assert.AreEqual(newWord.PersistedWordId, docOccurrences[0].WordId);
    }

    [TestMethod]
    public async Task CorrelateAsync_AllCandidatesEstablished_ReportsNoNewVocabularyWithoutFabricatingReviewOrPersistence()
    {
        await SeedWordAsync("Knownword", "W:knownword", WordStatus.Known, occurrences: 5, docCount: 2);
        await SeedWordAsync("Backlogword", "W:backlogword", WordStatus.UnknownBacklog, occurrences: 3, docCount: 1);

        var request = new ImportTextRequest("Established doc", "Knownword Backlogword.", "en", "de");
        var evidence = await ReviewAdmissionCorrelator.CorrelateAsync(_database, _service, request, _analyzer);

        Assert.AreEqual(ImportAnalysisOutcome.NoNewVocabulary, evidence.ImportResult.Outcome);
        Assert.AreEqual(0, evidence.ImportResult.DocumentId);
        Assert.AreEqual(0, evidence.ImportResult.SessionId);
        Assert.IsFalse(evidence.HasAcceptedReviewSession);
        Assert.AreEqual(0, evidence.PersistedReviewCandidates.Count);

        Assert.AreEqual(2, evidence.CandidateAdmissions.Count);
        var known = evidence.CandidateAdmissions.Single(c => c.CanonicalTerm == "Knownword");
        Assert.AreEqual(ReviewAdmissionDisposition.NotAdmittedExistingKnown, known.Disposition);
        Assert.IsNull(known.PersistedReviewOrder);
        Assert.IsFalse(known.WasWordCreatedForSession);
        Assert.AreEqual(5, known.TotalOccurrenceCountBefore);
        Assert.AreEqual(5, known.TotalOccurrenceCountAfter);
        Assert.AreEqual(2, known.DocumentCountBefore);
        Assert.AreEqual(2, known.DocumentCountAfter);
        Assert.AreEqual(0, known.OccurrenceContributionCount);

        var backlog = evidence.CandidateAdmissions.Single(c => c.CanonicalTerm == "Backlogword");
        Assert.AreEqual(ReviewAdmissionDisposition.NotAdmittedExistingUnknownBacklog, backlog.Disposition);
        Assert.IsNull(backlog.PersistedReviewOrder);
        Assert.IsFalse(backlog.WasWordCreatedForSession);
        Assert.AreEqual(3, backlog.TotalOccurrenceCountBefore);
        Assert.AreEqual(3, backlog.TotalOccurrenceCountAfter);
        Assert.AreEqual(1, backlog.DocumentCountBefore);
        Assert.AreEqual(1, backlog.DocumentCountAfter);
        Assert.AreEqual(0, backlog.OccurrenceContributionCount);
    }

    [TestMethod]
    public async Task CorrelateAsync_ExactDuplicateDocument_ReportsExactDuplicateWithoutFabricatingReview()
    {
        const string content = "Unique text for duplicate test.";
        var initialRequest = new ImportTextRequest("Doc 1", content, "en", "de");
        await _service.ImportAsync(initialRequest);

        while (await _service.GetCurrentCandidateAsync() is { } current)
        {
            await _service.DecideAsync(current.WordId, WordStatus.UnknownBacklog);
        }

        var duplicateRequest = new ImportTextRequest("Doc 2", content, "en", "de");
        var evidence = await ReviewAdmissionCorrelator.CorrelateAsync(_database, _service, duplicateRequest, _analyzer);

        Assert.AreEqual(ImportAnalysisOutcome.ExactDuplicate, evidence.ImportResult.Outcome);
        Assert.AreEqual(0, evidence.ImportResult.DocumentId);
        Assert.AreEqual(0, evidence.ImportResult.SessionId);
        Assert.IsFalse(evidence.HasAcceptedReviewSession);
        Assert.AreEqual(0, evidence.CandidateAdmissions.Count);
        Assert.AreEqual(0, evidence.PersistedReviewCandidates.Count);
    }

    private async Task SeedWordAsync(
        string canonicalTerm,
        string normalizedTerm,
        WordStatus status,
        int occurrences = 1,
        int docCount = 1,
        string language = "en")
    {
        await _database.RunInTransactionAsync(conn =>
        {
            conn.Insert(new WordEntity
            {
                Language = language,
                CanonicalTerm = canonicalTerm,
                NormalizedTerm = normalizedTerm,
                TokenKind = TokenKind.Word,
                Status = status,
                TotalOccurrenceCount = occurrences,
                DocumentCount = docCount,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            return true;
        });
    }

    private sealed class ThrowingGermanLexicon : IGermanLexicon
    {
        public bool TryLookupLemma(string form, out GermanLexemeEntry? entry) =>
            throw new InvalidOperationException("The German lexicon must not be queried when enhanced recognition is inactive.");

        public bool TryLookupStem(string componentForm, out GermanCompoundStemEntry? entry) =>
            throw new InvalidOperationException("The German lexicon must not be queried when enhanced recognition is inactive.");
    }

    private sealed class AdmissionTestDatabase : IKnownFirstDatabase, IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private SQLiteAsyncConnection? _connection;
        private bool _initialized;

        public AdmissionTestDatabase()
        {
            DatabasePath = Path.Combine(
                Path.GetTempPath(),
                $"knownfirst-admission-{Guid.NewGuid():N}.db3");
        }

        public string DatabasePath { get; }

        public async Task InitializeAsync()
        {
            if (_initialized)
            {
                return;
            }

            _connection ??= new SQLiteAsyncConnection(DatabasePath);
            await DatabaseSchema.InitializeAsync(_connection);
            _initialized = true;
        }

        public async Task<T> ReadAsync<T>(Func<SQLiteAsyncConnection, Task<T>> operation)
        {
            await _gate.WaitAsync();
            try
            {
                await InitializeAsync();
                return await operation(_connection!);
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
                await InitializeAsync();
                T? result = default;
                await _connection!.RunInTransactionAsync(connection => result = operation(connection));
                return result!;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<T> ExecuteSnapshotAsync<T>(Func<SQLiteConnection, T> operation)
        {
            await _gate.WaitAsync();
            try
            {
                await InitializeAsync();
                T? result = default;
                await _connection!.RunInTransactionAsync(connection => result = operation(connection));
                return result!;
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task ResetAsync() => throw new NotSupportedException();

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_connection is not null)
                {
                    await _connection.CloseAsync();
                    _connection = null;
                }

                TemporaryDatabaseFiles.Delete(DatabasePath);
            }
            finally
            {
                _gate.Dispose();
            }
        }
    }
}
