using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Data;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema11;
using KnownFirst.Models;
using SQLite;

namespace KnownFirst.Tests;

[TestClass]
[DoNotParallelize]
public sealed class Schema11EvidenceValidationTests
{
    private const string SampleContent = "Die Schreibmaschine steht hier.";
    private const string GermanLanguage = "de";

    [TestMethod]
    public async Task Validation_ValidEvidence_Succeeds()
    {
        var path = CreateTemporaryPath();
        SQLiteAsyncConnection? connection = null;
        try
        {
            connection = new SQLiteAsyncConnection(path);
            await DatabaseSchema.InitializeAsync(connection);

            await SeedValidDocumentAndEvidenceAsync(connection);

            var isValid = false;
            await connection.RunInTransactionAsync(conn =>
            {
                isValid = Schema11ShapeValidator.IsValidDatabase(conn, out _);
            });
            Assert.IsTrue(isValid);
        }
        finally
        {
            await TemporaryDatabaseFiles.CloseAndDeleteAsync(connection, path);
        }
    }

    [TestMethod]
    public async Task Validation_OrphanReviewCandidateId_FailsClosed()
    {
        await AssertCorruptedEvidenceFailsAsync(
            corruptAction: conn => conn.Execute("UPDATE DerivedTermEvidenceEntries SET ReviewCandidateId = 99999"));
    }

    [TestMethod]
    public async Task Validation_BlankSourceIdentity_FailsClosed()
    {
        await AssertCorruptedEvidenceFailsAsync(
            corruptAction: conn => conn.Execute("UPDATE DerivedTermEvidenceEntries SET SourceIdentity = '   '"));
    }

    [TestMethod]
    public async Task Validation_BlankComponentForm_FailsClosed()
    {
        await AssertCorruptedEvidenceFailsAsync(
            corruptAction: conn => conn.Execute("UPDATE DerivedTermEvidenceEntries SET ComponentForm = ''"));
    }

    [TestMethod]
    public async Task Validation_BlankSourceSurfaceForm_FailsClosed()
    {
        await AssertCorruptedEvidenceFailsAsync(
            corruptAction: conn => conn.Execute("UPDATE DerivedTermEvidenceEntries SET SourceSurfaceForm = ''"));
    }

    [TestMethod]
    public async Task Validation_NegativeSourceStartPosition_FailsClosed()
    {
        await AssertCorruptedEvidenceFailsAsync(
            corruptAction: conn => conn.Execute("UPDATE DerivedTermEvidenceEntries SET SourceStartPosition = -1"));
    }

    [TestMethod]
    public async Task Validation_NonPositiveSourceLength_FailsClosed()
    {
        await AssertCorruptedEvidenceFailsAsync(
            corruptAction: conn => conn.Execute("UPDATE DerivedTermEvidenceEntries SET SourceLength = 0"));
    }

    [TestMethod]
    public async Task Validation_OutOfDocumentRange_FailsClosed()
    {
        await AssertCorruptedEvidenceFailsAsync(
            corruptAction: conn => conn.Execute("UPDATE DerivedTermEvidenceEntries SET SourceStartPosition = 25, SourceLength = 20"));
    }

    [TestMethod]
    public async Task Validation_OverflowSourceRange_FailsClosed()
    {
        await AssertCorruptedEvidenceFailsAsync(
            corruptAction: conn => conn.Execute(
                "UPDATE DerivedTermEvidenceEntries SET SourceStartPosition = ?, SourceLength = ?",
                int.MaxValue - 2, 10));
    }

    [TestMethod]
    public async Task Validation_SourceSurfaceFormMismatch_FailsClosed()
    {
        await AssertCorruptedEvidenceFailsAsync(
            corruptAction: conn => conn.Execute("UPDATE DerivedTermEvidenceEntries SET SourceSurfaceForm = 'Waschmaschine'"));
    }

    [TestMethod]
    public async Task Validation_UnresolvedSourceSentenceOrder_FailsClosed()
    {
        await AssertCorruptedEvidenceFailsAsync(
            corruptAction: conn => conn.Execute("UPDATE DerivedTermEvidenceEntries SET SourceSentenceOrder = 99"));
    }

    [TestMethod]
    public async Task Validation_SourceRangeOutsideSentenceSpan_FailsClosed()
    {
        await AssertCorruptedEvidenceFailsAsync(
            corruptAction: conn => conn.Execute("UPDATE SentenceSpans SET StartPosition = 10, Length = 15"));
    }

    [TestMethod]
    public async Task Validation_MissingSourceWordIdentity_FailsClosed()
    {
        await AssertCorruptedEvidenceFailsAsync(
            corruptAction: conn => conn.Execute("UPDATE DerivedTermEvidenceEntries SET SourceIdentity = 'W:unbekannt'"));
    }

    [TestMethod]
    public async Task Validation_DuplicateSemanticEvidence_FailsClosed()
    {
        var path = CreateTemporaryPath();
        SQLiteAsyncConnection? connection = null;
        try
        {
            connection = new SQLiteAsyncConnection(path);
            await DatabaseSchema.InitializeAsync(connection);
            await SeedValidDocumentAndEvidenceAsync(connection);

            // Insert duplicate semantic evidence row bypassing unique index
            await connection.ExecuteAsync("DROP INDEX IX_DerivedTermEvidenceEntries_Candidate_Source_Range_Component");
            await connection.ExecuteAsync("""
                INSERT INTO DerivedTermEvidenceEntries (
                    ReviewCandidateId, SourceIdentity, SourceSurfaceForm, SourceStartPosition, SourceLength, SourceSentenceOrder, ComponentForm
                ) VALUES (1, 'W:schreibmaschine', 'Schreibmaschine', 4, 15, 0, 'Schreib')
                """);

            var isValid = false;
            await connection.RunInTransactionAsync(conn =>
            {
                isValid = Schema11ShapeValidator.IsValidDatabase(conn, out _);
            });
            Assert.IsFalse(isValid, "Duplicate semantic evidence must fail validation.");
        }
        finally
        {
            await TemporaryDatabaseFiles.CloseAndDeleteAsync(connection, path);
        }
    }

    private static async Task AssertCorruptedEvidenceFailsAsync(Action<SQLiteConnection> corruptAction)
    {
        var path = CreateTemporaryPath();
        SQLiteAsyncConnection? connection = null;
        try
        {
            connection = new SQLiteAsyncConnection(path);
            await DatabaseSchema.InitializeAsync(connection);
            await SeedValidDocumentAndEvidenceAsync(connection);

            await connection.RunInTransactionAsync(corruptAction);

            var isValid = false;
            await connection.RunInTransactionAsync(conn =>
            {
                isValid = Schema11ShapeValidator.IsValidDatabase(conn, out _);
            });
            Assert.IsFalse(isValid, "Corrupted evidence entry must fail validation.");

            var exception = await Assert.ThrowsExactlyAsync<KnownFirst.Data.Migrations.Schema12.Schema12MigrationException>(
                () => DatabaseSchema.InitializeAsync(connection));
            Assert.AreEqual("schema12-migration-already-applied-shape-invalid", exception.ErrorCode);
        }
        finally
        {
            await TemporaryDatabaseFiles.CloseAndDeleteAsync(connection, path);
        }
    }

    private static async Task SeedValidDocumentAndEvidenceAsync(SQLiteAsyncConnection connection)
    {
        await connection.RunInTransactionAsync(conn =>
        {
            var now = DateTime.UtcNow;
            var doc = new DocumentEntity
            {
                Title = "Test Document",
                TextLanguage = GermanLanguage,
                ExplanationLanguage = GermanLanguage,
                LookupMode = LexicalLookupMode.Definition,
                TargetLanguage = string.Empty,
                Content = SampleContent,
                ContentFingerprint = "test-fingerprint",
                ImportedAt = now,
                WordCount = 3
            };
            conn.Insert(doc);

            var sentence = new SentenceSpanEntity
            {
                DocumentId = doc.Id,
                StartPosition = 0,
                Length = SampleContent.Length,
                Order = 0
            };
            conn.Insert(sentence);

            var sourceWord = new WordEntity
            {
                Language = GermanLanguage,
                CanonicalTerm = "Schreibmaschine",
                NormalizedTerm = "W:schreibmaschine",
                TokenKind = TokenKind.Word,
                Status = WordStatus.Unreviewed,
                TotalOccurrenceCount = 1,
                DocumentCount = 1,
                CreatedAt = now,
                UpdatedAt = now
            };
            conn.Insert(sourceWord);

            var derivedWord = new WordEntity
            {
                Language = GermanLanguage,
                CanonicalTerm = "schreiben",
                NormalizedTerm = "W:schreiben",
                TokenKind = TokenKind.Word,
                Status = WordStatus.Unreviewed,
                TotalOccurrenceCount = 0,
                DocumentCount = 1,
                CreatedAt = now,
                UpdatedAt = now
            };
            conn.Insert(derivedWord);

            var session = new ReviewSessionEntity
            {
                DocumentId = doc.Id,
                Status = ReviewSessionStatus.Active,
                StartedAt = now,
                TotalCandidates = 2
            };
            conn.Insert(session);

            var candidate = new ReviewCandidateEntity
            {
                SessionId = session.Id,
                WordId = derivedWord.Id,
                Order = 1,
                Status = WordStatus.Unreviewed,
                WasWordCreatedForSession = true
            };
            conn.Insert(candidate);

            var evidence = new DerivedTermEvidenceEntity
            {
                ReviewCandidateId = candidate.Id,
                SourceIdentity = "W:schreibmaschine",
                SourceSurfaceForm = "Schreibmaschine",
                SourceStartPosition = 4,
                SourceLength = 15,
                SourceSentenceOrder = 0,
                ComponentForm = "Schreib"
            };
            conn.Insert(evidence);
        });
    }

    private static string CreateTemporaryPath() =>
        Path.Combine(Path.GetTempPath(), $"knownfirst-schema11-validation-{Guid.NewGuid():N}.db3");
}
