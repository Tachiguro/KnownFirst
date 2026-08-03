using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DatabaseMigrationTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    public async Task Migration_OldSchemaToNewSchema_PreservesDataAndAddsDefaults(int legacyVersion)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"knownfirst-migration-{Guid.NewGuid():N}.db3");
        SQLiteAsyncConnection? asyncConnection = null;
        try
        {
            using (var connection = new SQLiteConnection(tempPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache))
            {
                connection.Execute("CREATE TABLE Documents (Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT, TextLanguage TEXT, ExplanationLanguage TEXT, Content TEXT, ContentFingerprint TEXT, ImportedAt INTEGER, WordCount INTEGER)");
                if (legacyVersion >= 6)
                {
                    connection.Execute("ALTER TABLE Documents ADD COLUMN LookupMode INTEGER");
                    connection.Execute("ALTER TABLE Documents ADD COLUMN TargetLanguage TEXT");
                }

                connection.Execute("INSERT INTO Documents (Id, Title, TextLanguage, ExplanationLanguage, Content, ContentFingerprint, ImportedAt, WordCount) VALUES (17, 'Legacy document', 'en', 'de', 'network evidence', 'legacy-fingerprint', 0, 2)");
                if (legacyVersion >= 6)
                {
                    connection.Execute("UPDATE Documents SET LookupMode = ?, TargetLanguage = 'de' WHERE Id = 17", (int)LexicalLookupMode.Translation);
                }

                connection.Execute("CREATE TABLE Words (Id INTEGER PRIMARY KEY AUTOINCREMENT, Language TEXT, CanonicalTerm TEXT, NormalizedTerm TEXT, Status INTEGER, TotalOccurrenceCount INTEGER, DocumentCount INTEGER, CreatedAt INTEGER, UpdatedAt INTEGER)");
                if (legacyVersion >= 3)
                {
                    connection.Execute("ALTER TABLE Words ADD COLUMN TokenKind INTEGER");
                }

                if (legacyVersion >= 4)
                {
                    connection.Execute("ALTER TABLE Words ADD COLUMN PreparationState INTEGER");
                }

                connection.Execute("INSERT INTO Words (Id, Language, CanonicalTerm, NormalizedTerm, Status, TotalOccurrenceCount, DocumentCount, CreatedAt, UpdatedAt) VALUES (42, 'en', 'network', 'network', 0, 7, 3, 0, 0)");
                if (legacyVersion >= 3)
                {
                    connection.Execute("UPDATE Words SET TokenKind = ? WHERE Id = 42", (int)TokenKind.Acronym);
                }

                if (legacyVersion >= 4)
                {
                    connection.Execute("UPDATE Words SET PreparationState = ? WHERE Id = 42", (int)PreparationState.Prepared);
                }

                if (legacyVersion >= 4)
                {
                    connection.Execute("CREATE TABLE LexicalCache (Id INTEGER PRIMARY KEY AUTOINCREMENT, CacheKey TEXT, SourceLanguage TEXT, ExplanationLanguage TEXT, NormalizedLemma TEXT, TokenKind INTEGER, Provider TEXT, ProviderSchemaVersion INTEGER, ResultJson TEXT, SourceProject TEXT, PageTitle TEXT, RevisionId INTEGER, Attribution TEXT, FetchedAtUtc INTEGER)");
                    if (legacyVersion >= 6)
                    {
                        connection.Execute("ALTER TABLE LexicalCache ADD COLUMN LookupMode INTEGER");
                        connection.Execute("ALTER TABLE LexicalCache ADD COLUMN TargetLanguage TEXT");
                        connection.Execute("ALTER TABLE LexicalCache ADD COLUMN CanonicalLookupTerm TEXT");
                    }

                    connection.Execute("INSERT INTO LexicalCache (Id, CacheKey, SourceLanguage, ExplanationLanguage, NormalizedLemma, TokenKind, Provider, ProviderSchemaVersion, ResultJson, SourceProject, PageTitle, Attribution, FetchedAtUtc) VALUES (23, 'v2|legacy-cache', 'en', 'de', 'network', ?, 'legacy-provider', 1, '{}', 'legacy-project', 'Legacy page', 'legacy-attribution', 0)", (int)TokenKind.TechnicalTerm);
                    if (legacyVersion >= 6)
                    {
                        connection.Execute("UPDATE LexicalCache SET LookupMode = ?, TargetLanguage = 'de', CanonicalLookupTerm = 'network' WHERE Id = 23", (int)LexicalLookupMode.Translation);
                    }
                }


                if (legacyVersion >= 3)
                {
                    connection.Execute("CREATE TABLE Meanings (Id INTEGER PRIMARY KEY AUTOINCREMENT, WordId INTEGER, ExplanationLanguage TEXT, SourceLanguage TEXT, DisplayTerm TEXT, EncounteredSurfaceForm TEXT, GrammaticalRelationship TEXT, SelectedMeaningId TEXT, AcronymExpansion TEXT, Translation TEXT, Definition TEXT, DictionaryExample TEXT, AdditionalNote TEXT, AcceptedAliasesJson TEXT, TranslationOrDefinition TEXT, Source TEXT, SourceProject TEXT, SourcePageTitle TEXT, SourceRevisionId INTEGER, Attribution TEXT, ConfirmedByUser INTEGER, CreatedAt INTEGER, UpdatedAt INTEGER, PreparedAt INTEGER)");
                    if (legacyVersion >= 4)
                    {
                        connection.Execute("ALTER TABLE Meanings ADD COLUMN TokenKind INTEGER");
                    }

                    connection.Execute("INSERT INTO Meanings (Id, WordId, ExplanationLanguage, SourceLanguage, DisplayTerm, EncounteredSurfaceForm, GrammaticalRelationship, SelectedMeaningId, AcronymExpansion, Translation, Definition, DictionaryExample, AdditionalNote, AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle, SourceRevisionId, Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt) VALUES (31, 42, 'de', 'en', 'network', 'Network', 'noun', 'network-meaning', '', 'Netzwerk', 'connected system', 'network example', 'legacy note', '[\"net\"]', 'Netzwerk', 'legacy-source', 'legacy-project', 'Legacy network', 456, 'legacy attribution', 1, 100, 200, 300)");
                    if (legacyVersion >= 4)
                    {
                        connection.Execute("UPDATE Meanings SET TokenKind = ? WHERE Id = 31", (int)TokenKind.TechnicalTerm);
                    }
                }

                if (legacyVersion >= 4)
                {
                    connection.Execute("CREATE TABLE SentenceSpans (Id INTEGER PRIMARY KEY AUTOINCREMENT, DocumentId INTEGER, StartPosition INTEGER, Length INTEGER, \"Order\" INTEGER)");
                    connection.Execute("INSERT INTO SentenceSpans (Id, DocumentId, StartPosition, Length, \"Order\") VALUES (32, 17, 4, 7, 2)");
                    connection.Execute("CREATE TABLE WordOccurrences (Id INTEGER PRIMARY KEY AUTOINCREMENT, WordId INTEGER, DocumentId INTEGER, SentenceSpanId INTEGER, StartPosition INTEGER, Length INTEGER, SurfaceForm TEXT, \"Order\" INTEGER)");
                    if (legacyVersion >= 5)
                    {
                        connection.Execute("ALTER TABLE WordOccurrences ADD COLUMN TechnicalFamily INTEGER");
                    }

                    connection.Execute("INSERT INTO WordOccurrences (Id, WordId, DocumentId, SentenceSpanId, StartPosition, Length, SurfaceForm, \"Order\") VALUES (33, 42, 17, 32, 4, 7, 'Network', 5)");
                    if (legacyVersion >= 5)
                    {
                        connection.Execute("UPDATE WordOccurrences SET TechnicalFamily = ? WHERE Id = 33", (int)TechnicalTokenFamily.Sha);
                    }
                }

                connection.Execute($"PRAGMA user_version = {legacyVersion}");
            }

            asyncConnection = new SQLiteAsyncConnection(tempPath);
            await DatabaseSchema.InitializeAsync(asyncConnection);

            var words = await asyncConnection.Table<WordEntity>().ToListAsync();
            Assert.HasCount(1, words);
            var word = words[0];
            Assert.AreEqual(42, word.Id);
            Assert.AreEqual("network", word.CanonicalTerm);
            Assert.AreEqual(7, word.TotalOccurrenceCount);
            Assert.AreEqual(3, word.DocumentCount);
            Assert.AreEqual(
                legacyVersion >= 3 ? TokenKind.Acronym : TokenKind.Word,
                word.TokenKind);
            Assert.AreEqual(
                legacyVersion >= 4 ? PreparationState.Prepared : PreparationState.Unprepared,
                word.PreparationState);
            Assert.AreEqual(LearningInteractionMode.Reading, word.AutomaticInteractionMode);
            Assert.AreEqual(0, word.ConsecutiveRecallSuccessCount);
            var document = await asyncConnection.FindAsync<DocumentEntity>(17);
            Assert.IsNotNull(document);
            Assert.AreEqual("Legacy document", document.Title);
            Assert.AreEqual("network evidence", document.Content);
            Assert.AreEqual(2, document.WordCount);
            Assert.AreEqual(
                legacyVersion >= 6 ? LexicalLookupMode.Translation : LexicalLookupMode.Definition,
                document.LookupMode);
            if (legacyVersion >= 4)
            {
                var cache = await asyncConnection.FindAsync<LexicalCacheEntity>(23);
                Assert.IsNotNull(cache);
                Assert.AreEqual("v2|legacy-cache", cache.CacheKey);
                Assert.AreEqual(TokenKind.TechnicalTerm, cache.TokenKind);
                Assert.AreEqual(
                    legacyVersion >= 6 ? LexicalLookupMode.Translation : LexicalLookupMode.Definition,
                    cache.LookupMode);
            }

            if (legacyVersion >= 3)
            {
                var meaning = await asyncConnection.QueryAsync<HistoricalMeaningRow>(
                    "SELECT Id, WordId, DisplayTerm, EncounteredSurfaceForm, Translation, Definition, DictionaryExample, AdditionalNote, AcceptedAliasesJson, SourceProject, SourcePageTitle, SourceRevisionId, Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt, TokenKind FROM Meanings WHERE Id = 31");
                Assert.HasCount(1, meaning);
                Assert.AreEqual(31, meaning[0].Id);
                Assert.AreEqual(42, meaning[0].WordId);
                Assert.AreEqual("network", meaning[0].DisplayTerm);
                Assert.AreEqual("Network", meaning[0].EncounteredSurfaceForm);
                Assert.AreEqual("Netzwerk", meaning[0].Translation);
                Assert.AreEqual("connected system", meaning[0].Definition);
                Assert.AreEqual("network example", meaning[0].DictionaryExample);
                Assert.AreEqual("legacy note", meaning[0].AdditionalNote);
                Assert.AreEqual("[\"net\"]", meaning[0].AcceptedAliasesJson);
                Assert.AreEqual("legacy-project", meaning[0].SourceProject);
                Assert.AreEqual("Legacy network", meaning[0].SourcePageTitle);
                Assert.AreEqual(456L, meaning[0].SourceRevisionId);
                Assert.AreEqual("legacy attribution", meaning[0].Attribution);
                Assert.AreEqual(1, meaning[0].ConfirmedByUser);
                Assert.AreEqual(100L, meaning[0].CreatedAt);
                Assert.AreEqual(200L, meaning[0].UpdatedAt);
                Assert.AreEqual(300L, meaning[0].PreparedAt);
                Assert.AreEqual(
                    legacyVersion == 3 ? (int)TokenKind.Word : (int)TokenKind.TechnicalTerm,
                    meaning[0].TokenKind);
            }

            if (legacyVersion >= 4)
            {
                var occurrence = await asyncConnection.QueryAsync<HistoricalOccurrenceRow>(
                    "SELECT Id, WordId, DocumentId, SentenceSpanId, StartPosition, Length, SurfaceForm, \"Order\", TechnicalFamily FROM WordOccurrences WHERE Id = 33");
                Assert.HasCount(1, occurrence);
                Assert.AreEqual(33, occurrence[0].Id);
                Assert.AreEqual(42, occurrence[0].WordId);
                Assert.AreEqual(17, occurrence[0].DocumentId);
                Assert.AreEqual(32, occurrence[0].SentenceSpanId);
                Assert.AreEqual(4, occurrence[0].StartPosition);
                Assert.AreEqual(7, occurrence[0].Length);
                Assert.AreEqual("Network", occurrence[0].SurfaceForm);
                Assert.AreEqual(5, occurrence[0].Order);
                Assert.AreEqual(
                    legacyVersion == 4 ? (int)TechnicalTokenFamily.None : (int)TechnicalTokenFamily.Sha,
                    occurrence[0].TechnicalFamily);
            }

            Assert.AreEqual(
                DatabaseSchema.CurrentVersion,
                await asyncConnection.ExecuteScalarAsync<int>("PRAGMA user_version"));

            Console.WriteLine($"Test used isolated temp database at: {tempPath}");
        }
        finally
        {
            await TemporaryDatabaseFiles.CloseAndDeleteAsync(asyncConnection, tempPath);
        }
    }


    [TestMethod]
    public async Task LegacyBaseline_BackfillFailureRollsBackAndRetryReachesCurrentSchema()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"knownfirst-migration-retry-{Guid.NewGuid():N}.db3");
        SQLiteAsyncConnection? asyncConnection = null;
        try
        {
            using (var connection = new SQLiteConnection(tempPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache))
            {
                connection.Execute("CREATE TABLE Words (Id INTEGER PRIMARY KEY AUTOINCREMENT, Language TEXT, CanonicalTerm TEXT, NormalizedTerm TEXT, Status INTEGER, TokenKind INTEGER, TotalOccurrenceCount INTEGER, DocumentCount INTEGER, CreatedAt INTEGER, UpdatedAt INTEGER)");
                connection.Execute("INSERT INTO Words (Id, Language, CanonicalTerm, NormalizedTerm, Status, TokenKind, TotalOccurrenceCount, DocumentCount, CreatedAt, UpdatedAt) VALUES (42, 'en', 'retry', 'retry', 0, 1, 7, 3, 100, 200)");
                connection.Execute("CREATE TABLE Meanings (Id INTEGER PRIMARY KEY AUTOINCREMENT, WordId INTEGER, ExplanationLanguage TEXT, SourceLanguage TEXT, DisplayTerm TEXT, Translation TEXT, ConfirmedByUser INTEGER, CreatedAt INTEGER, UpdatedAt INTEGER, PreparedAt INTEGER)");
                connection.Execute("INSERT INTO Meanings (Id, WordId, ExplanationLanguage, SourceLanguage, DisplayTerm, Translation, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt) VALUES (31, 42, 'de', 'en', 'retry', 'Wiederholung', 1, 100, 200, 300)");
                connection.Execute("CREATE TRIGGER FailMeaningTokenKindBackfill BEFORE UPDATE OF TokenKind ON Meanings WHEN NEW.TokenKind IS NOT NULL BEGIN SELECT RAISE(ABORT, 'injected meaning token backfill failure'); END");
                connection.Execute("PRAGMA user_version = 3");
            }

            asyncConnection = new SQLiteAsyncConnection(tempPath);
            await Assert.ThrowsExactlyAsync<SQLiteException>(() => DatabaseSchema.InitializeAsync(asyncConnection));
            await asyncConnection.CloseAsync();
            asyncConnection = null;

            using (var reopened = new SQLiteConnection(tempPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache))
            {
                Assert.AreEqual(3, reopened.ExecuteScalar<int>("PRAGMA user_version"));
                Assert.AreEqual(1, reopened.ExecuteScalar<int>("SELECT COUNT(*) FROM Words WHERE Id = 42 AND Language = 'en' AND CanonicalTerm = 'retry' AND TokenKind = 1 AND PreparationState IS NULL"));
                Assert.AreEqual(1, reopened.ExecuteScalar<int>("SELECT COUNT(*) FROM Meanings WHERE Id = 31 AND WordId = 42 AND DisplayTerm = 'retry' AND Translation = 'Wiederholung' AND TokenKind IS NULL"));
                reopened.Execute("DROP TRIGGER FailMeaningTokenKindBackfill");
            }

            asyncConnection = new SQLiteAsyncConnection(tempPath);
            await DatabaseSchema.InitializeAsync(asyncConnection);
            Assert.AreEqual(DatabaseSchema.CurrentVersion, await asyncConnection.ExecuteScalarAsync<int>("PRAGMA user_version"));
            Assert.AreEqual((int)PreparationState.Unprepared, await asyncConnection.ExecuteScalarAsync<int>("SELECT PreparationState FROM Words WHERE Id = 42"));
            Assert.AreEqual((int)TokenKind.Word, await asyncConnection.ExecuteScalarAsync<int>("SELECT TokenKind FROM Meanings WHERE Id = 31"));
            Assert.AreEqual(1, await asyncConnection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Senses WHERE WordId = 42"));
        }
        finally
        {
            await TemporaryDatabaseFiles.CloseAndDeleteAsync(asyncConnection, tempPath);
        }
    }

    private sealed class HistoricalMeaningRow
    {
        public int Id { get; set; }
        public int WordId { get; set; }
        public string DisplayTerm { get; set; } = string.Empty;
        public string EncounteredSurfaceForm { get; set; } = string.Empty;
        public string Translation { get; set; } = string.Empty;
        public string Definition { get; set; } = string.Empty;
        public string DictionaryExample { get; set; } = string.Empty;
        public string AdditionalNote { get; set; } = string.Empty;
        public string AcceptedAliasesJson { get; set; } = string.Empty;
        public string SourceProject { get; set; } = string.Empty;
        public string SourcePageTitle { get; set; } = string.Empty;
        public long? SourceRevisionId { get; set; }
        public string Attribution { get; set; } = string.Empty;
        public int ConfirmedByUser { get; set; }
        public long CreatedAt { get; set; }
        public long UpdatedAt { get; set; }
        public long PreparedAt { get; set; }
        public int TokenKind { get; set; }
    }

    private sealed class HistoricalOccurrenceRow
    {
        public int Id { get; set; }
        public int WordId { get; set; }
        public int DocumentId { get; set; }
        public int SentenceSpanId { get; set; }
        public int StartPosition { get; set; }
        public int Length { get; set; }
        public string SurfaceForm { get; set; } = string.Empty;
        public int Order { get; set; }
        public int TechnicalFamily { get; set; }
    }
}
