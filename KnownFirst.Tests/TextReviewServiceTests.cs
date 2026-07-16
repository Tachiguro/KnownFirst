using KnownFirst.Core.Review;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Services;
using SQLite;
using System.Text.Json;

namespace KnownFirst.Tests;

[TestClass]
public sealed class TextReviewServiceTests
{
    private TemporaryDatabase _database = null!;
    private TextReviewService _service = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _database = new TemporaryDatabase();
        await _database.InitializeAsync();
        _service = new TextReviewService(_database, new TextAnalyzer());
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _database.DisposeAsync();
    }

    [TestMethod]
    public async Task ImportAsync_StoresOriginalTextAndExactCoordinates()
    {
        const string content = "  Die Häuser stehen hier.\r\nOAuth2 bleibt!  ";

        var result = await _service.ImportAsync(CreateRequest(content));
        var stored = await _database.ReadAsync(async connection =>
        {
            var document = await connection.FindAsync<DocumentEntity>(result.DocumentId);
            var sentences = await connection.Table<SentenceSpanEntity>()
                .Where(item => item.DocumentId == result.DocumentId)
                .ToListAsync();
            var occurrences = await connection.Table<WordOccurrenceEntity>()
                .Where(item => item.DocumentId == result.DocumentId)
                .ToListAsync();
            return (Document: document!, Sentences: sentences, Occurrences: occurrences);
        });

        Assert.AreEqual(content, stored.Document.Content);
        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);
        Assert.AreEqual(64, stored.Document.ContentFingerprint.Length);
        Assert.AreEqual("en", stored.Document.TextLanguage);
        Assert.AreEqual("de", stored.Document.ExplanationLanguage);
        foreach (var sentence in stored.Sentences)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(
                stored.Document.Content.Substring(sentence.StartPosition, sentence.Length)));
        }

        foreach (var occurrence in stored.Occurrences)
        {
            Assert.AreEqual(
                occurrence.SurfaceForm,
                stored.Document.Content.Substring(occurrence.StartPosition, occurrence.Length));
        }
    }

    [TestMethod]
    public async Task ImportAsync_RequiredCorpusStoresUniqueCandidatesAndEveryOccurrence()
    {
        const string content = "IT protects smart systems. It protects smart networks. Smart systems use OAuth2.";

        var result = await _service.ImportAsync(CreateRequest(content));
        var stored = await _database.ReadAsync(async connection =>
        {
            var spans = await connection.Table<SentenceSpanEntity>()
                .Where(item => item.DocumentId == result.DocumentId)
                .ToListAsync();
            var queue = await connection.Table<ReviewCandidateEntity>()
                .Where(item => item.SessionId == result.SessionId)
                .ToListAsync();
            var occurrences = await connection.Table<WordOccurrenceEntity>()
                .Where(item => item.DocumentId == result.DocumentId)
                .ToListAsync();
            return (spans, queue, occurrences);
        });

        Assert.AreEqual(8, result.CandidateCount);
        Assert.HasCount(3, stored.spans);
        Assert.HasCount(8, stored.queue);
        Assert.AreEqual(8, stored.queue.Select(item => item.WordId).Distinct().Count());
        Assert.HasCount(12, stored.occurrences);
        Assert.IsTrue(stored.occurrences.Any(item =>
            item.SurfaceForm == "IT" && item.StartPosition == 0 && item.Length == 2));
        Assert.IsTrue(stored.occurrences.Any(item =>
            item.SurfaceForm == "smart" && item.StartPosition == 12 && item.Length == 5));
        Assert.IsTrue(stored.occurrences.Any(item =>
            item.SurfaceForm == "OAuth2" && item.StartPosition == 73 && item.Length == 6));
    }

    [TestMethod]
    public async Task DecideAsync_PersistsEveryDecisionAndResumeFindsFirstUnresolvedCandidate()
    {
        await _service.ImportAsync(CreateRequest("Alpha Beta Gamma."));
        var first = await _service.GetCurrentCandidateAsync();
        await _service.DecideAsync(first!.WordId, WordStatus.Known);

        var recreatedService = new TextReviewService(_database, new TextAnalyzer());
        var resumed = await recreatedService.GetCurrentCandidateAsync();
        var persisted = await _database.ReadAsync(connection =>
            connection.FindAsync<WordEntity>(first.WordId));
        var active = await recreatedService.GetActiveReviewAsync();

        Assert.AreEqual(WordStatus.Known, persisted!.Status);
        Assert.AreNotEqual(first.WordId, resumed!.WordId);
        Assert.AreEqual(1, active!.ReviewedCount);
        Assert.AreEqual(3, active.TotalCandidates);
    }

    [TestMethod]
    public async Task GetCurrentCandidateAsync_ReturnsAtMostThreeDistinctOriginalContexts()
    {
        const string content = "Target first. Target second. Target third. Target fourth.";
        await _service.ImportAsync(CreateRequest(content));

        var candidate = await _service.GetCurrentCandidateAsync()
            ?? throw new InvalidOperationException("The expected review candidate was not created.");
        var storedRows = await _database.ReadAsync(async connection =>
        {
            var queueRows = await connection.Table<ReviewCandidateEntity>()
                .Where(item => item.WordId == candidate.WordId)
                .CountAsync();
            var occurrenceRows = await connection.Table<WordOccurrenceEntity>()
                .Where(item => item.WordId == candidate.WordId)
                .CountAsync();
            return (queueRows, occurrenceRows);
        });

        Assert.AreEqual("Target", candidate.Candidate);
        Assert.AreEqual(4, candidate.OccurrenceCount);
        Assert.AreEqual(1, storedRows.queueRows);
        Assert.AreEqual(4, storedRows.occurrenceRows);
        Assert.HasCount(3, candidate.Contexts);
        CollectionAssert.AreEqual(
            new[] { "Target first.", "Target second.", "Target third." },
            candidate.Contexts
                .Select(context => context.BeforeTarget + context.Target + context.AfterTarget)
                .ToArray());
        foreach (var context in candidate.Contexts)
        {
            Assert.AreEqual(
                context.Target,
                content.Substring(context.StartPosition, context.Length));
        }
    }

    [TestMethod]
    public async Task UndoPreviousDecisionAsync_RestoresPreviousCandidateAndStatus()
    {
        await _service.ImportAsync(CreateRequest("Alpha Beta."));
        var first = await _service.GetCurrentCandidateAsync();
        await _service.DecideAsync(first!.WordId, WordStatus.Ignored);

        var undone = await _service.UndoPreviousDecisionAsync();
        var restored = await _service.GetCurrentCandidateAsync();
        var word = await _database.ReadAsync(connection => connection.FindAsync<WordEntity>(first.WordId));

        Assert.IsTrue(undone);
        Assert.AreEqual(first.WordId, restored!.WordId);
        Assert.AreEqual(WordStatus.Unreviewed, word!.Status);
        Assert.AreEqual(0, restored.ReviewedCount);
    }

    [TestMethod]
    public async Task ImportAsync_WhenReviewIsActive_BlocksSecondImport()
    {
        await _service.ImportAsync(CreateRequest("Alpha Beta."));

        await Assert.ThrowsExactlyAsync<ActiveReviewExistsException>(
            () => _service.ImportAsync(CreateRequest("Second import.")));

        var documentCount = await _database.ReadAsync(connection =>
            connection.Table<DocumentEntity>().CountAsync());
        Assert.AreEqual(1, documentCount);
    }

    [TestMethod]
    public async Task ReviewRoutePolicy_BlocksDirectLearnPrepareAndImportUntilCompletion()
    {
        await _service.ImportAsync(CreateRequest("Only."));
        var active = await _service.GetActiveReviewAsync();

        Assert.IsTrue(ReviewRoutePolicy.IsBlocked("learn", active is not null));
        Assert.IsTrue(ReviewRoutePolicy.IsBlocked("prepare-words", active is not null));
        Assert.IsTrue(ReviewRoutePolicy.IsBlocked("import-text", active is not null));

        var candidate = await _service.GetCurrentCandidateAsync();
        await _service.DecideAsync(candidate!.WordId, WordStatus.Known);
        active = await _service.GetActiveReviewAsync();

        Assert.IsFalse(ReviewRoutePolicy.IsBlocked("learn", active is not null));
    }

    [TestMethod]
    public async Task Completion_KnownAndIgnoredRetainOnlyMinimalMarkerData()
    {
        await _service.ImportAsync(CreateRequest("Alpha Beta."));
        var known = await _service.GetCurrentCandidateAsync();
        await _service.DecideAsync(known!.WordId, WordStatus.Known);
        var ignored = await _service.GetCurrentCandidateAsync();
        await _service.DecideAsync(ignored!.WordId, WordStatus.Ignored);

        var retained = await _database.ReadAsync(async connection =>
        {
            var knownWord = await connection.FindAsync<WordEntity>(known.WordId);
            var ignoredWord = await connection.FindAsync<WordEntity>(ignored.WordId);
            var knownOccurrences = await connection.Table<WordOccurrenceEntity>()
                .Where(item => item.WordId == known.WordId)
                .CountAsync();
            var ignoredOccurrences = await connection.Table<WordOccurrenceEntity>()
                .Where(item => item.WordId == ignored.WordId)
                .CountAsync();
            var formCount = await connection.Table<WordFormEntity>()
                .Where(item => item.WordId == known.WordId || item.WordId == ignored.WordId)
                .CountAsync();
            return (knownWord!, ignoredWord!, knownOccurrences, ignoredOccurrences, formCount);
        });

        Assert.AreEqual(WordStatus.Known, retained.Item1.Status);
        Assert.AreEqual(WordStatus.Ignored, retained.Item2.Status);
        Assert.AreEqual(0, retained.Item1.TotalOccurrenceCount);
        Assert.AreEqual(0, retained.Item2.TotalOccurrenceCount);
        Assert.AreEqual(0, retained.knownOccurrences);
        Assert.AreEqual(0, retained.ignoredOccurrences);
        Assert.AreEqual(2, retained.formCount);
    }

    [TestMethod]
    public async Task Completion_UnknownRetainsAtMostThreeDistinctContexts()
    {
        await _service.ImportAsync(CreateRequest("Target. Target! Target? Target."));
        var candidate = await _service.GetCurrentCandidateAsync();

        await _service.DecideAsync(candidate!.WordId, WordStatus.UnknownBacklog);

        var retained = await _database.ReadAsync(async connection =>
        {
            var word = await connection.FindAsync<WordEntity>(candidate.WordId);
            var occurrences = await connection.Table<WordOccurrenceEntity>()
                .Where(item => item.WordId == candidate.WordId)
                .ToListAsync();
            return (Word: word!, Occurrences: occurrences);
        });

        Assert.AreEqual(WordStatus.UnknownBacklog, retained.Word.Status);
        Assert.AreEqual(4, retained.Word.TotalOccurrenceCount);
        Assert.HasCount(3, retained.Occurrences);
        Assert.AreEqual(3, retained.Occurrences.Select(item => item.SentenceSpanId).Distinct().Count());
    }

    [TestMethod]
    public async Task ImportAsync_WhenReorderedTextContainsOnlyExistingVocabulary_RejectsWithoutWrites()
    {
        await _service.ImportAsync(CreateRequest("Alpha Beta."));
        var alpha = await _service.GetCurrentCandidateAsync();
        await _service.DecideAsync(alpha!.WordId, WordStatus.Known);
        var beta = await _service.GetCurrentCandidateAsync();
        await _service.DecideAsync(beta!.WordId, WordStatus.Ignored);
        var before = await CaptureDatabaseStateAsync();

        var nextImport = await _service.ImportAsync(CreateRequest("Beta Alpha."));
        var after = await CaptureDatabaseStateAsync();

        Assert.AreEqual(ImportAnalysisOutcome.NoNewVocabulary, nextImport.Outcome);
        Assert.IsTrue(nextImport.IsComplete);
        Assert.AreEqual(0, nextImport.CandidateCount);
        Assert.IsNull(await _service.GetActiveReviewAsync());
        Assert.AreEqual(before, after);
    }

    [TestMethod]
    public async Task ImportAsync_WhenCompletedTextIsImportedAgain_RejectsExactDuplicateWithoutWrites()
    {
        const string content = "Alpha Beta Alpha.";
        await _service.ImportAsync(CreateRequest(content));
        await CompleteReviewAsync(_ => WordStatus.UnknownBacklog);
        var before = await CaptureDatabaseStateAsync();

        var result = await _service.ImportAsync(CreateRequest(content));
        var after = await CaptureDatabaseStateAsync();

        Assert.AreEqual(ImportAnalysisOutcome.ExactDuplicate, result.Outcome);
        Assert.AreEqual(0, result.DocumentId);
        Assert.AreEqual(0, result.SessionId);
        Assert.AreEqual(before, after);
    }

    [TestMethod]
    public async Task ImportAsync_WhenExistingUnknownAndOneNewCandidate_AcceptsOnlyNewReviewCandidate()
    {
        await _service.ImportAsync(CreateRequest("Knownword Unknownword Ignoredword."));
        await CompleteReviewAsync(candidate => candidate.Candidate switch
        {
            "Knownword" => WordStatus.Known,
            "Unknownword" => WordStatus.UnknownBacklog,
            "Ignoredword" => WordStatus.Ignored,
            _ => throw new InvalidOperationException("Unexpected setup candidate.")
        });
        var wordsBefore = await GetWordsByCanonicalTermAsync();

        var result = await _service.ImportAsync(CreateRequest(
            "Unknownword Unknownword Knownword Ignoredword Newword."));
        var active = await _service.GetActiveReviewAsync();
        var current = await _service.GetCurrentCandidateAsync();
        var wordsAfter = await GetWordsByCanonicalTermAsync();

        Assert.AreEqual(ImportAnalysisOutcome.Accepted, result.Outcome);
        Assert.AreEqual(1, result.CandidateCount);
        Assert.AreEqual(1, active!.TotalCandidates);
        Assert.AreEqual("Newword", current!.Candidate);
        Assert.AreEqual(
            wordsBefore["Unknownword"].TotalOccurrenceCount + 2,
            wordsAfter["Unknownword"].TotalOccurrenceCount);
        Assert.AreEqual(
            wordsBefore["Unknownword"].DocumentCount + 1,
            wordsAfter["Unknownword"].DocumentCount);
        Assert.AreEqual(
            wordsBefore["Knownword"].TotalOccurrenceCount,
            wordsAfter["Knownword"].TotalOccurrenceCount);
        Assert.AreEqual(
            wordsBefore["Ignoredword"].TotalOccurrenceCount,
            wordsAfter["Ignoredword"].TotalOccurrenceCount);
    }

    [TestMethod]
    public async Task ImportAsync_WhenPreflightFindsNoNewVocabulary_DatabaseStateIsIdentical()
    {
        await _service.ImportAsync(CreateRequest("Existing."));
        await CompleteReviewAsync(_ => WordStatus.Known);
        var before = await CaptureDatabaseStateAsync();

        var result = await _service.ImportAsync(CreateRequest("Existing Existing."));
        var after = await CaptureDatabaseStateAsync();

        Assert.AreEqual(ImportAnalysisOutcome.NoNewVocabulary, result.Outcome);
        Assert.AreEqual(before, after);
    }

    [TestMethod]
    public async Task Completion_AcceptedImportUpdatesExistingUnknownAndRetainsAtMostThreeContexts()
    {
        await _service.ImportAsync(CreateRequest("Existing. Existing!"));
        await CompleteReviewAsync(_ => WordStatus.UnknownBacklog);

        await _service.ImportAsync(CreateRequest(
            "Existing? Existing. Existing! Genuinelynew."));
        var newCandidate = await _service.GetCurrentCandidateAsync();
        Assert.AreEqual("Genuinelynew", newCandidate!.Candidate);
        await _service.DecideAsync(newCandidate.WordId, WordStatus.Known);

        var retained = await _database.ReadAsync(async connection =>
        {
            var existing = await connection.Table<WordEntity>()
                .Where(word => word.NormalizedTerm == "W:existing")
                .FirstAsync();
            var occurrences = await connection.Table<WordOccurrenceEntity>()
                .Where(occurrence => occurrence.WordId == existing.Id)
                .ToListAsync();
            return (existing, occurrences);
        });

        Assert.AreEqual(5, retained.existing.TotalOccurrenceCount);
        Assert.AreEqual(2, retained.existing.DocumentCount);
        Assert.IsLessThanOrEqualTo(3, retained.occurrences.Count);
        Assert.AreEqual(
            retained.occurrences.Count,
            retained.occurrences.Select(occurrence => occurrence.SentenceSpanId).Distinct().Count());
    }

    [TestMethod]
    public async Task DiagnosticsReportFormatter_ContainsReadableReviewInformation()
    {
        await _service.ImportAsync(CreateRequest("Alpha appears. Alpha returns."));

        var snapshot = await _service.GetDiagnosticsAsync();
        var report = DiagnosticsReportFormatter.Format(snapshot);

        Assert.Contains("\"Documents\"", report);
        Assert.Contains("\"Sentences\"", report);
        Assert.Contains("\"Candidates\"", report);
        Assert.Contains("\"Occurrences\"", report);
        Assert.Contains("\"Sessions\"", report);
        Assert.Contains("Test document", report);
        Assert.Contains("Alpha appears.", report);
        Assert.Contains("\"CandidateText\": \"Alpha\"", report);
        Assert.Contains("\"Status\": \"Active\"", report);
    }

    [TestMethod]
    public async Task DiscardActiveImportAsync_RemovesOnlyImportSpecificData()
    {
        var stableWords = await _database.RunInTransactionAsync(connection =>
        {
            var statuses = new[]
            {
                WordStatus.Known,
                WordStatus.UnknownBacklog,
                WordStatus.Ignored,
                WordStatus.Mastered
            };
            var words = new List<(int Id, WordStatus Status)>();

            foreach (var status in statuses)
            {
                var canonical = $"Stable{status}";
                var stable = new WordEntity
                {
                    Language = "en",
                    CanonicalTerm = canonical,
                    NormalizedTerm = $"W:{canonical.ToLowerInvariant()}",
                    TokenKind = TokenKind.Word,
                    Status = status,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                connection.Insert(stable);
                connection.Insert(new WordFormEntity { WordId = stable.Id, SurfaceForm = canonical });
                words.Add((stable.Id, status));
            }

            return words;
        });
        await _service.ImportAsync(CreateRequest("Temporary Remaining."));
        var temporary = await _service.GetCurrentCandidateAsync();
        await _service.DecideAsync(temporary!.WordId, WordStatus.Known);

        await _service.DiscardActiveImportAsync();

        var remaining = await _database.ReadAsync(async connection =>
        {
            var preservedWords = new List<WordEntity>();
            foreach (var stableWord in stableWords)
            {
                preservedWords.Add((await connection.FindAsync<WordEntity>(stableWord.Id))!);
            }

            var documents = await connection.Table<DocumentEntity>().CountAsync();
            var sessions = await connection.Table<ReviewSessionEntity>().CountAsync();
            var importSpecificWords = await connection.Table<WordEntity>()
                .Where(item => item.NormalizedTerm == "W:temporary" || item.NormalizedTerm == "W:remaining")
                .CountAsync();
            return (preservedWords, documents, sessions, importSpecificWords);
        });

        Assert.HasCount(4, remaining.preservedWords);
        foreach (var stableWord in stableWords)
        {
            Assert.AreEqual(
                stableWord.Status,
                remaining.preservedWords.Single(item => item.Id == stableWord.Id).Status);
        }

        Assert.AreEqual(0, remaining.documents);
        Assert.AreEqual(0, remaining.sessions);
        Assert.AreEqual(0, remaining.importSpecificWords);
    }

    [TestMethod]
    public async Task DiscardActiveImportAsync_RollsBackExistingUnknownContribution()
    {
        await _service.ImportAsync(CreateRequest("Existing."));
        await CompleteReviewAsync(_ => WordStatus.UnknownBacklog);
        var before = await CaptureDatabaseStateAsync();

        await _service.ImportAsync(CreateRequest("Existing Existing Newword."));
        await _service.DiscardActiveImportAsync();
        var after = await CaptureDatabaseStateAsync();

        Assert.AreEqual(before, after);
    }

    [TestMethod]
    public async Task DatabaseSchema_ForwardMigrationPreservesExistingDataAndSettingsTable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"knownfirst-migration-{Guid.NewGuid():N}.db3");
        var connection = new SQLiteAsyncConnection(path);

        try
        {
            await connection.CreateTableAsync<DocumentEntity>();
            var document = new DocumentEntity
            {
                Title = "Existing",
                Content = "Unchanged",
                TextLanguage = "en",
                ExplanationLanguage = "de"
            };
            await connection.InsertAsync(document);
            await connection.ExecuteAsync("CREATE TABLE AppSettings (SettingKey TEXT PRIMARY KEY, SettingValue TEXT)");
            await connection.ExecuteAsync(
                "INSERT INTO AppSettings (SettingKey, SettingValue) VALUES (?, ?)",
                "theme_preference",
                "2");

            await DatabaseSchema.InitializeAsync(connection);

            var preservedDocument = await connection.FindAsync<DocumentEntity>(document.Id);
            var preservedSetting = await connection.ExecuteScalarAsync<string>(
                "SELECT SettingValue FROM AppSettings WHERE SettingKey = ?",
                "theme_preference");
            var version = await connection.ExecuteScalarAsync<int>("PRAGMA user_version");

            Assert.AreEqual("Unchanged", preservedDocument!.Content);
            Assert.AreEqual("2", preservedSetting);
            Assert.AreEqual(DatabaseSchema.CurrentVersion, version);
        }
        finally
        {
            await connection.CloseAsync();
            File.Delete(path);
        }
    }

    private async Task CompleteReviewAsync(Func<ReviewCandidateDetails, WordStatus> statusSelector)
    {
        while (await _service.GetCurrentCandidateAsync() is { } candidate)
        {
            await _service.DecideAsync(candidate.WordId, statusSelector(candidate));
        }
    }

    private Task<Dictionary<string, WordEntity>> GetWordsByCanonicalTermAsync() =>
        _database.ReadAsync(async connection =>
        {
            var words = await connection.Table<WordEntity>().ToListAsync();
            return words.ToDictionary(word => word.CanonicalTerm, StringComparer.Ordinal);
        });

    private Task<string> CaptureDatabaseStateAsync() => _database.ReadAsync(async connection =>
    {
        var state = new
        {
            Documents = await connection.Table<DocumentEntity>().OrderBy(item => item.Id).ToListAsync(),
            Sentences = await connection.Table<SentenceSpanEntity>().OrderBy(item => item.Id).ToListAsync(),
            Words = await connection.Table<WordEntity>().OrderBy(item => item.Id).ToListAsync(),
            Forms = await connection.Table<WordFormEntity>().OrderBy(item => item.Id).ToListAsync(),
            Occurrences = await connection.Table<WordOccurrenceEntity>().OrderBy(item => item.Id).ToListAsync(),
            Meanings = await connection.Table<MeaningEntity>().OrderBy(item => item.Id).ToListAsync(),
            ReviewStates = await connection.Table<ReviewStateEntity>().OrderBy(item => item.Id).ToListAsync(),
            Sessions = await connection.Table<ReviewSessionEntity>().OrderBy(item => item.Id).ToListAsync(),
            ReviewCandidates = await connection.Table<ReviewCandidateEntity>().OrderBy(item => item.Id).ToListAsync()
        };
        return JsonSerializer.Serialize(state);
    });

    private static ImportTextRequest CreateRequest(string content) => new(
        "Test document",
        content,
        "en",
        "de");

    private sealed class TemporaryDatabase : IKnownFirstDatabase, IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private SQLiteAsyncConnection? _connection;

        public TemporaryDatabase()
        {
            DatabasePath = Path.Combine(
                Path.GetTempPath(),
                $"knownfirst-review-{Guid.NewGuid():N}.db3");
        }

        public string DatabasePath { get; }

        public async Task InitializeAsync()
        {
            _connection ??= new SQLiteAsyncConnection(DatabasePath);
            await DatabaseSchema.InitializeAsync(_connection);
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

        public async Task ResetAsync()
        {
            await DisposeConnectionAsync();
            File.Delete(DatabasePath);
            await InitializeAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeConnectionAsync();
            File.Delete(DatabasePath);
            _gate.Dispose();
        }

        private async Task DisposeConnectionAsync()
        {
            if (_connection is null)
            {
                return;
            }

            await _connection.CloseAsync();
            _connection = null;
        }
    }
}
