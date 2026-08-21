using KnownFirst.Core.Learning;
using KnownFirst.Data;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models.Backup;
using KnownFirst.Services.DataSafety;
using SQLite;

namespace KnownFirst.Tests;

/// <summary>Shared fixture helpers for KF-MEANING-001 Slice 2 archive-v2/Schema-8 backup tests.</summary>
internal static class Schema8BackupFixtureBuilders
{
    internal sealed class FakePlatformInfo : IBackupPlatformInfo
    {
        public BackupSourcePlatform SourcePlatform => BackupSourcePlatform.Windows;
        public string SourceAppVersion => "1.0.0-test";
    }

    /// <summary>
    /// Builds a representative Schema-7 fixture (a grouped Sense pair via a shared provider sense id, a
    /// split Sense pair with no discriminator, both card directions, reviews, queue items, and automatic
    /// progress state), then migrates it in place via <see cref="Schema8DormantMigration.ApplyAsync"/> —
    /// producing a real Schema-8 database for capture/restore/parity tests.
    /// </summary>
    public static async Task<Schema7Fixture> CreateAndMigrateRepresentativeFixtureAsync()
    {
        var fixture = await Schema7Fixture.CreateAsync();

        var documentId = await fixture.InsertDocumentAsync(title: "Doc A", content: "the bank text");
        var wordId = await fixture.InsertWordAsync("bank", status: KnownFirst.Models.WordStatus.Learning);

        // Grouped pair: same provider sense id (reliable discriminator) — becomes one Sense.
        var groupedMeaning1 = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "bank", translation: "Bank", selectedMeaningId: "finance-1",
            definition: "financial institution");
        var groupedMeaning2 = await fixture.InsertMeaningAsync(
            wordId, displayTerm: "bank", translation: "Kreditinstitut", selectedMeaningId: "finance-1",
            definition: "money-handling institution");

        var termCardId = await fixture.InsertCardAsync(wordId, groupedMeaning1, CardDirection.MeaningToTerm, intervalDays: 3);
        var meaningCardId = await fixture.InsertCardAsync(wordId, groupedMeaning1, CardDirection.TermToMeaning, intervalDays: 5);

        var sessionId = await fixture.InsertLearningSessionAsync();
        await fixture.InsertContextAsync(groupedMeaning1, wordId, sourceDocumentId: documentId, sourceDocumentTitle: "Doc A", text: "the bank text", targetStart: 4, targetLength: 4);
        await fixture.InsertReviewAsync(termCardId, sessionId: sessionId, wasTypedAnswer: true, wasCorrect: true);
        await fixture.InsertReviewAsync(meaningCardId, sessionId: sessionId, wasTypedAnswer: false, wasCorrect: true);
        await fixture.InsertQueueItemAsync(sessionId: sessionId, cardId: termCardId, queueOrder: 1);
        await fixture.InsertQueueItemAsync(sessionId: sessionId, cardId: meaningCardId, queueOrder: 2, isCompleted: true);

        // Split pair (a second word): no reliable discriminator, differing content — must split.
        var wordId2 = await fixture.InsertWordAsync("light");
        await fixture.InsertMeaningAsync(wordId2, displayTerm: "light", translation: "Licht", definition: "illumination");
        await fixture.InsertMeaningAsync(wordId2, displayTerm: "light", translation: "leicht", definition: "not heavy");

        return fixture;
    }

    /// <summary>Migrates the fixture's underlying database (must be a still-Schema-7 fixture) to Schema 8.</summary>
    public static async Task MigrateAsync(Schema7Fixture fixture)
    {
        var result = await Schema8DormantMigration.ApplyAsync(fixture.Connection);
        if (result.Outcome is not (Schema8MigrationOutcome.Migrated or Schema8MigrationOutcome.AlreadyApplied))
        {
            throw new InvalidOperationException("Expected the fixture to migrate to Schema 8.");
        }
    }

    /// <summary>Convenience: build, migrate, and return a synchronously-usable Schema-8 fixture in one call.</summary>
    public static async Task<Schema7Fixture> CreateSchema8FixtureAsync()
    {
        var fixture = await CreateAndMigrateRepresentativeFixtureAsync();
        await MigrateAsync(fixture);
        return fixture;
    }

    /// <summary>Builds an empty (zero-row) Schema-8 fixture — a Schema-7 fixture with no data, migrated.</summary>
    public static async Task<Schema7Fixture> CreateEmptySchema8FixtureAsync()
    {
        var fixture = await Schema7Fixture.CreateAsync();
        await MigrateAsync(fixture);
        return fixture;
    }

    /// <summary>
    /// KF-MEANING-001 Slice 4 deterministic archive/restore boundary values. Every timestamp here is fixed
    /// (never <see cref="DateTime.UtcNow"/>) so a round-trip test can assert exact tick equality instead of
    /// a tolerance window, and every StableId is fixed so a restored row can be located in the destination
    /// database by identity rather than by insertion order or local numeric id.
    /// </summary>
    internal static class Slice4Boundary
    {
        public const string SenseStableId = "se-slice4-0001";
        public const string MeaningStableId = "me-slice4-0001";

        /// <summary>The TermToMeaning variant carrying the Required assignment under test.</summary>
        public const string RequiredVariantStableId = "av-slice4-required";

        /// <summary>A second TermToMeaning variant, deliberately AcceptedOnly — the fixture is mixed-role,
        /// never Required-everywhere merely to satisfy the invariant.</summary>
        public const string AcceptedOnlyVariantStableId = "av-slice4-accepted";

        /// <summary>The MeaningToTerm variant — AcceptedOnly and preferred, proving Requirement and
        /// IsPreferred stay independent facts.</summary>
        public const string TermVariantStableId = "av-slice4-term";

        public const string RequiredAssignmentStableId = "sa-slice4-required";
        public const string AcceptedOnlyAssignmentStableId = "sa-slice4-accepted";
        public const string TermAssignmentStableId = "sa-slice4-term";

        /// <summary>Every row that is not itself the Required-epoch boundary.</summary>
        public static readonly DateTime BaseUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>The Required-epoch boundary under test — also the current-epoch progress row's
        /// <c>CreatedAtUtc</c>.</summary>
        public static readonly DateTime RequiredSinceUtc = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        public const int WordRowCount = 1;
        public const int SenseRowCount = 1;
        public const int MeaningRowCount = 1;
        public const int AnswerVariantRowCount = 3;
        public const int AssignmentRowCount = 3;
        public const int CardRowCount = 1;
        public const int ProgressRowCount = 1;
    }

    /// <summary>
    /// Builds a deterministic Schema-8 fixture whose <c>SenseAnswerVariantAssignments</c> form a mixed
    /// Requirement graph: one Required + preferred TermToMeaning assignment carrying an explicit
    /// <c>RequiredSinceUtc</c> boundary, a second AcceptedOnly TermToMeaning assignment with a null
    /// boundary, and one AcceptedOnly + preferred MeaningToTerm assignment (also null). The single
    /// <c>AnswerVariantProgress</c> row is the Required assignment's current-epoch row — its
    /// <c>CreatedAtUtc</c> is the same instant as that assignment's boundary.
    ///
    /// Built with direct SQL against an already-migrated empty Schema-8 database rather than through the
    /// live dormant migration, so every StableId and timestamp is fixed by this fixture and the
    /// archive/restore assertions never depend on migration-derived values.
    /// </summary>
    public static async Task<Schema7Fixture> CreateSlice4BoundarySchema8FixtureAsync()
    {
        var fixture = await CreateEmptySchema8FixtureAsync();
        var connection = fixture.Connection;
        var ts = Slice4Boundary.BaseUtc;

        await connection.ExecuteAsync(
            """
            INSERT INTO Words
                (Language, CanonicalTerm, NormalizedTerm, Status, TokenKind, PreparationState,
                 TotalOccurrenceCount, DocumentCount, AutomaticInteractionMode, ConsecutiveRecallSuccessCount,
                 ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, MasteryReviewExtensionScheduled,
                 CreatedAt, UpdatedAt)
            VALUES ('en', 'boundary', 'boundary', ?, 0, 0, 1, 1, 0, 0, 0, 0, 0, ?, ?)
            """,
            (int)KnownFirst.Models.WordStatus.UnknownBacklog, ts, ts);
        var wordId = await connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");

        await connection.ExecuteAsync(
            """
            INSERT INTO Senses
                (StableId, WordId, SourceLanguage, ExplanationLanguage, ProviderSenseId, TopicOrDomain,
                 PartOfSpeech, GrammaticalRelationship, AcronymExpansion, DefaultMeaningId, Status,
                 CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, 'en', 'de', 'slice4-1', '', '', '', '', NULL, ?, ?, ?)
            """,
            Slice4Boundary.SenseStableId, wordId, (int)SenseStatus.Learning, ts, ts);
        var senseId = await connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");

        await connection.ExecuteAsync(
            """
            INSERT INTO Meanings
                (WordId, SenseId, StableId, ExplanationLanguage, SourceLanguage, DisplayTerm,
                 EncounteredSurfaceForm, GrammaticalRelationship, TokenKind, SelectedMeaningId,
                 AcronymExpansion, Translation, Definition, DictionaryExample, AdditionalNote,
                 AcceptedAliasesJson, TranslationOrDefinition, Source, SourceProject, SourcePageTitle,
                 SourceRevisionId, Attribution, ConfirmedByUser, CreatedAt, UpdatedAt, PreparedAt)
            VALUES (?, ?, ?, 'de', 'en', 'boundary', 'boundary', '', 0, 'slice4-1', '', 'Grenze',
                    'a dividing line', '', '', '[]', 'Grenze', 'Manual', '', '', NULL, '', 1, ?, ?, ?)
            """,
            wordId, senseId, Slice4Boundary.MeaningStableId, ts, ts, ts);
        var meaningId = await connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");

        await connection.ExecuteAsync("UPDATE Senses SET DefaultMeaningId = ? WHERE Id = ?", meaningId, senseId);

        async Task<int> InsertVariantAsync(string stableId, string language, string displayText, string normalizedText)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO AnswerVariants
                    (StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText, SourceMeaningId,
                     CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                stableId, senseId, language, displayText, normalizedText, meaningId, ts, ts);
            return await connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");
        }

        var requiredVariantId = await InsertVariantAsync(Slice4Boundary.RequiredVariantStableId, "de", "Grenze", "grenze");
        var acceptedOnlyVariantId = await InsertVariantAsync(Slice4Boundary.AcceptedOnlyVariantStableId, "de", "Rand", "rand");
        var termVariantId = await InsertVariantAsync(Slice4Boundary.TermVariantStableId, "en", "boundary", "boundary");

        // Requirement = Required carries a non-null boundary; every AcceptedOnly assignment carries null —
        // the archive invariant this fixture exists to exercise, expressed as data, not as policy.
        async Task InsertAssignmentAsync(
            string stableId,
            CardDirection direction,
            int answerVariantId,
            AnswerVariantRequirement requirement,
            bool isPreferred,
            DateTime? requiredSinceUtc)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO SenseAnswerVariantAssignments
                    (StableId, SenseId, CardDirection, AnswerVariantId, Requirement, IsPreferred,
                     RequiredSinceUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                stableId, senseId, (int)direction, answerVariantId, (int)requirement, isPreferred,
                requiredSinceUtc, ts, ts);
        }

        await InsertAssignmentAsync(
            Slice4Boundary.RequiredAssignmentStableId, CardDirection.TermToMeaning, requiredVariantId,
            AnswerVariantRequirement.Required, isPreferred: true, Slice4Boundary.RequiredSinceUtc);
        await InsertAssignmentAsync(
            Slice4Boundary.AcceptedOnlyAssignmentStableId, CardDirection.TermToMeaning, acceptedOnlyVariantId,
            AnswerVariantRequirement.AcceptedOnly, isPreferred: false, requiredSinceUtc: null);
        await InsertAssignmentAsync(
            Slice4Boundary.TermAssignmentStableId, CardDirection.MeaningToTerm, termVariantId,
            AnswerVariantRequirement.AcceptedOnly, isPreferred: true, requiredSinceUtc: null);

        await connection.ExecuteAsync(
            """
            INSERT INTO LearningCards
                (WordId, SenseId, PreferredMeaningId, Direction, State, DueAtUtc, IntervalDays, EaseFactor,
                 SuccessfulReviewCount, LapseCount, LastReviewedAtUtc, LastRating, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, 3, 2.5, 1, 0, ?, ?, ?, ?)
            """,
            wordId, senseId, meaningId, (int)CardDirection.TermToMeaning, (int)CardState.Review, ts, ts,
            (int)ReviewRating.Good, ts, ts);
        var cardId = await connection.ExecuteScalarAsync<int>("SELECT last_insert_rowid()");

        // Current Required epoch: the progress row's CreatedAtUtc is the assignment's RequiredSinceUtc.
        await connection.ExecuteAsync(
            """
            INSERT INTO AnswerVariantProgress
                (CardId, AnswerVariantId, InteractionMode, ConsecutiveReadingSuccessCount,
                 ConsecutiveTypingSuccessCount, ConsecutiveTypingFailureCount, LastAssessedAtUtc,
                 MasteryReviewExtensionScheduled, IsMastered, ReplayVersion, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, 2, 1, 0, ?, 0, 0, 1, ?, ?)
            """,
            cardId, requiredVariantId, (int)LearningInteractionMode.Reading, Slice4Boundary.RequiredSinceUtc,
            Slice4Boundary.RequiredSinceUtc, Slice4Boundary.RequiredSinceUtc);

        return fixture;
    }

    /// <summary>
    /// Smallest valid <see cref="BackupPayloadV2"/> holding exactly one assignment, whose
    /// <paramref name="requirement"/> and <paramref name="requiredSinceUtc"/> are supplied verbatim —
    /// including a deliberately invariant-violating combination, so fail-closed contract behaviour can be
    /// asserted without building a SQLite fixture. Shared by the contract and archive-v2 boundary tests;
    /// every timestamp is <see cref="Slice4Boundary.BaseUtc"/> so serialized bytes are deterministic.
    /// </summary>
    public static BackupPayloadV2 BuildSingleAssignmentPayloadV2(
        BackupAnswerVariantRequirement requirement,
        DateTime? requiredSinceUtc)
    {
        var ts = Slice4Boundary.BaseUtc;

        var vocabulary = new BackupVocabularyItem(
            "v-000001", "en", "boundary", "boundary", BackupTokenKind.Word, BackupKnowledgeState.Unreviewed,
            BackupPreparationState.Prepared, 1, 1, ts, ts, [],
            new BackupAutomaticLearningState(BackupLearningInteractionMode.Reading, 0, 0, 0, false), []);

        var sense = new BackupSense(
            "se-000001", "se-stable-1", "v-000001", "en", "de", "", "", "", "", "", "m-000001",
            BackupSenseStatus.Learning, ts, ts);

        var meaning = new BackupPreparedItemV2(
            "m-000001", "se-000001", "m-stable-1", "v-000001", "en", "de", "boundary", null, null,
            BackupTokenKind.Word, null, null, "Grenze", null, null, null, null, [], true,
            new BackupSourceReference("Manual", "", "", null, ""), ts, ts, ts, []);

        var variant = new BackupAnswerVariant(
            "av-000001", "av-stable-1", "se-000001", "de", "Grenze", "grenze", "m-000001", ts, ts);

        var assignment = new BackupSenseAnswerVariantAssignment(
            "sa-000001", "sa-stable-1", "se-000001", BackupCardDirection.TermToMeaning, "av-000001",
            requirement, true, ts, ts, requiredSinceUtc);

        var card = new BackupLearningCardV2(
            "c-000001", "v-000001", "se-000001", "m-000001", BackupCardDirection.TermToMeaning,
            BackupCardState.Review, ts, 1, 2.5, 0, 0, null, null, ts, ts);

        return new BackupPayloadV2(
            [], [vocabulary], [sense], [meaning], [variant], [assignment], [],
            new BackupLearningDataV2([card], []),
            new BackupWorkflowDataV2([], [], []),
            [],
            new BackupExtensions(new Dictionary<string, BackupExtensionPayload>(StringComparer.Ordinal)));
    }

    /// <summary>
    /// <see cref="IKnownFirstDatabase"/> adapter over an already-Schema-8 <see cref="Schema7Fixture"/>
    /// connection — lets <see cref="Services.DataSafety.BackupService"/> and
    /// <see cref="Services.DataSafety.Merge.MergeSafetyCopyService"/> be exercised directly against a
    /// synthetic Schema-8 database in tests, without ever going through
    /// <see cref="DatabaseSchema.InitializeAsync"/> (which never creates Schema-8 shape).
    /// </summary>
    internal sealed class Schema8DatabaseAdapter(Schema7Fixture fixture) : IKnownFirstDatabase
    {
        private readonly SemaphoreSlim _gate = new(1, 1);

        public string DatabasePath => fixture.DatabasePath;

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task<T> ReadAsync<T>(Func<SQLiteAsyncConnection, Task<T>> operation)
        {
            await _gate.WaitAsync();
            try
            {
                return await operation(fixture.Connection);
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
                await fixture.Connection.RunInTransactionAsync(connection => result = operation(connection));
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
                T? result = default;
                await fixture.Connection.RunInTransactionAsync(connection => result = operation(connection));
                return result!;
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task ResetAsync() => throw new NotSupportedException("Not used by these tests.");
    }
}
