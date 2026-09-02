using KnownFirst.Data;
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
    public async Task ProductionStartup_OldSchemaIsRejectedAndPreservedWithoutArtifacts(int legacyVersion)
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
            var before = await PersistentDatabaseSnapshot.CaptureCompleteAsync(asyncConnection);
            var exception = await Assert.ThrowsExactlyAsync<DatabaseSchemaCompatibilityException>(
                () => DatabaseSchema.InitializeAsync(asyncConnection));

            Assert.AreEqual(legacyVersion, exception.FoundVersion);
            Assert.AreEqual(DatabaseSchema.CurrentVersion, exception.SupportedVersion);
            Assert.AreEqual(
                legacyVersion == 0
                    ? DatabaseSchemaCompatibilityReason.UnknownNonEmptyUnversionedDatabase
                    : DatabaseSchemaCompatibilityReason.UnsupportedOlderVersion,
                exception.Reason);
            Assert.AreEqual(DatabaseSchemaCompatibilityException.StableErrorCode, exception.ErrorCode);
            Assert.AreEqual("network", await asyncConnection.ExecuteScalarAsync<string>(
                "SELECT CanonicalTerm FROM Words WHERE Id = 42"));
            Assert.AreEqual(7, await asyncConnection.ExecuteScalarAsync<int>(
                "SELECT TotalOccurrenceCount FROM Words WHERE Id = 42"));
            Assert.AreEqual("network evidence", await asyncConnection.ExecuteScalarAsync<string>(
                "SELECT Content FROM Documents WHERE Id = 17"));
            if (legacyVersion >= 4)
            {
                Assert.AreEqual("v2|legacy-cache", await asyncConnection.ExecuteScalarAsync<string>(
                    "SELECT CacheKey FROM LexicalCache WHERE Id = 23"));
            }

            if (legacyVersion >= 3)
            {
                Assert.AreEqual("Netzwerk", await asyncConnection.ExecuteScalarAsync<string>(
                    "SELECT Translation FROM Meanings WHERE Id = 31"));
                Assert.AreEqual("legacy attribution", await asyncConnection.ExecuteScalarAsync<string>(
                    "SELECT Attribution FROM Meanings WHERE Id = 31"));
            }

            if (legacyVersion >= 4)
            {
                Assert.AreEqual("Network", await asyncConnection.ExecuteScalarAsync<string>(
                    "SELECT SurfaceForm FROM WordOccurrences WHERE Id = 33"));
            }

            Assert.AreEqual(legacyVersion, await asyncConnection.ExecuteScalarAsync<int>("PRAGMA user_version"));
            CollectionAssert.AreEqual(before, await PersistentDatabaseSnapshot.CaptureCompleteAsync(asyncConnection));
            Assert.AreEqual(0, await asyncConnection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Senses'"));
            Assert.IsTrue(File.Exists(tempPath));

            Console.WriteLine($"Test used isolated temp database at: {tempPath}");
        }
        finally
        {
            await TemporaryDatabaseFiles.CloseAndDeleteAsync(asyncConnection, tempPath);
        }
    }


    [TestMethod]
    public async Task LegacyBaseline_ProductionStartupRejectsWithoutMutationAndRetryIsIdentical()
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
            var before = await PersistentDatabaseSnapshot.CaptureCompleteAsync(asyncConnection);
            var first = await Assert.ThrowsExactlyAsync<DatabaseSchemaCompatibilityException>(
                () => DatabaseSchema.InitializeAsync(asyncConnection));
            var retry = await Assert.ThrowsExactlyAsync<DatabaseSchemaCompatibilityException>(
                () => DatabaseSchema.InitializeAsync(asyncConnection));

            Assert.AreEqual(DatabaseSchemaCompatibilityReason.UnsupportedOlderVersion, first.Reason);
            Assert.AreEqual(DatabaseSchemaCompatibilityReason.UnsupportedOlderVersion, retry.Reason);
            Assert.AreEqual(3, await asyncConnection.ExecuteScalarAsync<int>("PRAGMA user_version"));
            CollectionAssert.AreEqual(before, await PersistentDatabaseSnapshot.CaptureCompleteAsync(asyncConnection));
            Assert.AreEqual(1, await asyncConnection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Words WHERE Id = 42 AND Language = 'en' AND CanonicalTerm = 'retry' AND TokenKind = 1"));
            Assert.AreEqual(1, await asyncConnection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Meanings WHERE Id = 31 AND WordId = 42 AND DisplayTerm = 'retry' AND Translation = 'Wiederholung'"));
            Assert.AreEqual(1, await asyncConnection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name = 'FailMeaningTokenKindBackfill'"));
            Assert.AreEqual(0, await asyncConnection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Senses'"));
            Assert.IsTrue(File.Exists(tempPath));
        }
        finally
        {
            await TemporaryDatabaseFiles.CloseAndDeleteAsync(asyncConnection, tempPath);
        }
    }

}
